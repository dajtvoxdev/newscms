using System.Text.Json;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Api.Admin;

/// <summary>
/// Đầu vào để nạp hoặc sửa credential provider scope System.
/// </summary>
/// <remarks>
/// Mọi trường trừ <see cref="Provider"/> đều tuỳ chọn. <b>Null = giữ giá trị đang có</b> (hoặc mặc
/// định nếu là credential mới) — sửa priority không được bắt gõ lại key, và đổi key không được làm
/// mất priority.
/// </remarks>
public sealed record CredentialInput
{
    public required string Provider { get; init; }

    /// <summary>Key gốc. Null hoặc rỗng = giữ key đang có.</summary>
    public string? ApiKey { get; init; }

    public string? ModelId { get; init; }
    public string? EndpointUrl { get; init; }

    /// <summary>JSON capability. Null = lấy từ descriptor mới nhất, rồi tới manifest viết tay.</summary>
    public string? CapabilityJson { get; init; }

    public int? Priority { get; init; }
    public bool? IsActive { get; init; }
    public string? Note { get; init; }
}

/// <summary>Kết quả nạp credential. <see cref="Error"/> khác null = không ghi gì.</summary>
public sealed record CredentialAdminResult(
    ProviderCredential? Credential,
    bool KeyChanged,
    string? Error,
    IReadOnlyList<string> Notices)
{
    public static CredentialAdminResult Fail(string error) => new(null, false, error, []);
}

/// <summary>
/// Nạp/sửa credential provider — đường logic DUY NHẤT, dùng chung cho lệnh <c>set-credential</c> và
/// <c>PUT /v1/admin/credentials/{provider}</c>.
/// </summary>
/// <remarks>
/// Hai lớp vỏ (CLI, HTTP) mà hai bộ quy tắc là cách để một provider nạp qua app bị từ chối còn nạp
/// qua CLI thì lọt, hoặc ngược lại. Mọi quy tắc nằm ở đây.
/// </remarks>
public sealed class CredentialAdmin
{
    private readonly AdVideoDbContext _db;
    private readonly ICredentialStore _credentials;
    private readonly IDescriptorStore _descriptors;

    public CredentialAdmin(AdVideoDbContext db, ICredentialStore credentials, IDescriptorStore descriptors)
    {
        _db = db;
        _credentials = credentials;
        _descriptors = descriptors;
    }

    public async Task<CredentialAdminResult> UpsertAsync(CredentialInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        string provider = input.Provider.Trim().ToLowerInvariant();

        if (provider.Length == 0)
        {
            return CredentialAdminResult.Fail("Thiếu tên provider.");
        }

        ProviderCategory? category = ProviderCapabilityCatalog.CategoryOf(provider);

        // Provider khai báo: tên không có trong catalog viết tay nhưng có descriptor trong DB. Loại
        // và capability lấy từ descriptor đang bật, không có thì bản mới nhất.
        ProviderDescriptor? descriptor = await LatestDescriptorAsync(provider, cancellationToken);

        if (category is null && descriptor is not null)
        {
            category = descriptor.Kind == DescriptorKind.Video ? ProviderCategory.Video : ProviderCategory.TextToSpeech;
        }

        if (category is null)
        {
            return CredentialAdminResult.Fail(
                $"Không nhận ra provider \"{provider}\". Các tên viết tay: kling, seedance, vidu, elevenlabs, vieneu; " +
                "provider khác phải nạp descriptor trước.");
        }

        string? capabilityJson;

        if (!string.IsNullOrWhiteSpace(input.CapabilityJson))
        {
            if (!IsJsonObject(input.CapabilityJson, out string? jsonProblem))
            {
                return CredentialAdminResult.Fail($"Capability không phải JSON object hợp lệ: {jsonProblem}");
            }

            capabilityJson = input.CapabilityJson;
        }
        else
        {
            capabilityJson = CapabilityFromTemplates(provider, category.Value, descriptor, input.ModelId);
        }

        if (capabilityJson is null)
        {
            return CredentialAdminResult.Fail(
                $"Chưa có manifest mẫu cho \"{provider}\". Gửi kèm capability (JSON) hoặc nạp descriptor trước.");
        }

        string modelId = string.IsNullOrWhiteSpace(input.ModelId)
            ? ReadModelId(capabilityJson) ?? provider
            : input.ModelId.Trim();

        ProviderCredential? existing = await _db.ProviderCredentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Provider == provider
                    && x.ModelId == modelId
                    && x.Category == category.Value
                    && x.TenantId == null,
                cancellationToken);

        string? endpoint = !string.IsNullOrWhiteSpace(input.EndpointUrl)
            ? input.EndpointUrl.Trim()
            : existing?.EndpointUrl ?? descriptor?.Transport.BaseUrl ?? ProviderCapabilityCatalog.DefaultEndpoint(provider);

        string apiKey = input.ApiKey?.Trim() ?? string.Empty;

        await _credentials.UpsertAsync(
            new ProviderCredential
            {
                Provider = provider,
                ModelId = modelId,
                Category = category.Value,
                Scope = CredentialScope.System,
                TenantId = null,
                EndpointUrl = endpoint,
                EncryptedApiKey = string.Empty,
                CapabilityJson = capabilityJson,
                IsActive = input.IsActive ?? existing?.IsActive ?? true,
                Priority = input.Priority ?? existing?.Priority ?? 0,
                CreditExpiresAt = existing?.CreditExpiresAt,
                DailyCostLimitUsd = existing?.DailyCostLimitUsd,
                Notes = input.Note ?? existing?.Notes,
            },
            apiKey,
            cancellationToken);

        ProviderCredential saved = await _db.ProviderCredentials
            .AsNoTracking()
            .SingleAsync(
                x => x.Provider == provider && x.ModelId == modelId && x.Category == category.Value && x.TenantId == null,
                cancellationToken);

        var notices = new List<string>();

        if (existing is null && apiKey.Length == 0 && provider is not (ProviderNames.VieNeu or ProviderNames.Fake))
        {
            notices.Add($"Credential {provider} được tạo KHÔNG có key — mọi lời gọi sẽ bị provider từ chối cho tới khi nạp key.");
        }

        if (input.CapabilityJson is null && ProviderCapabilityCatalog.Video(provider) is { } catalog)
        {
            notices.Add(
                $"Đơn giá đang dùng: {catalog.CostPerSecondUsd} USD/giây — số tham khảo, KHÔNG phải số đo. " +
                "Kiểm lại bảng giá của nhà cung cấp trước khi bật provider này.");
        }

        return new CredentialAdminResult(saved, apiKey.Length > 0, null, notices);
    }

    /// <summary>Che key để hiện ra ngoài: chỉ 4 ký tự cuối. Cùng quy tắc với <see cref="ResolvedCredential.MaskedKey"/>.</summary>
    public static string Mask(string? plainKey) =>
        string.IsNullOrEmpty(plainKey) ? string.Empty
        : plainKey.Length <= 4 ? "****"
        : $"****{plainKey[^4..]}";

    private async Task<ProviderDescriptor?> LatestDescriptorAsync(string provider, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderDescriptorRow> rows = await _descriptors.ListAsync(provider, cancellationToken);

        ProviderDescriptorRow? row = rows.FirstOrDefault(r => r.IsActive) ?? rows.FirstOrDefault();

        return row is null ? null : ProviderDescriptorParser.Parse(row.Json).Descriptor;
    }

    private static string? CapabilityFromTemplates(
        string provider, ProviderCategory category, ProviderDescriptor? descriptor, string? modelId)
    {
        if (descriptor?.Capability is { } fromDescriptor)
        {
            return fromDescriptor.ToJsonString(ProviderDescriptorParser.JsonOptions);
        }

        if (category == ProviderCategory.Video)
        {
            VideoProviderCapability? template = ProviderCapabilityCatalog.Video(provider);

            if (template is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(modelId))
            {
                template = template with { ModelId = modelId.Trim() };
            }

            return JsonSerializer.Serialize(template, AdVideoJson.Indented);
        }

        TtsProviderCapability? tts = ProviderCapabilityCatalog.Tts(provider);

        if (tts is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(modelId))
        {
            tts = tts with { ModelId = modelId.Trim() };
        }

        return JsonSerializer.Serialize(tts, AdVideoJson.Indented);
    }

    private static bool IsJsonObject(string json, out string? problem)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            problem = doc.RootElement.ValueKind == JsonValueKind.Object ? null : "phải là một object { … }.";

            return problem is null;
        }
        catch (JsonException ex)
        {
            problem = ex.Message;

            return false;
        }
    }

    private static string? ReadModelId(string capabilityJson)
    {
        using JsonDocument doc = JsonDocument.Parse(capabilityJson);

        return doc.RootElement.TryGetProperty("modelId", out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }
}
