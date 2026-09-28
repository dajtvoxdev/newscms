using System.Text.Json;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;

namespace AdVideo.Api.Admin;

// Hợp đồng JSON của /v1/admin. snake_case như API công khai (ApiJson). Tách khỏi entity để đổi
// schema DB không lặng lẽ đổi thứ mà app quản trị đang đọc.

public sealed record SettingView(
    string Key,
    string Value,
    SettingValueType ValueType,
    string Description,
    bool IsProvisional,
    string? MinValue,
    string? MaxValue,
    DateTime? UpdatedAt)
{
    public static SettingView From(SystemSetting s) =>
        new(s.Key, s.Value, s.ValueType, s.Description, s.IsProvisional, s.MinValue, s.MaxValue, s.UpdatedAt ?? s.CreatedAt);
}

public sealed record UpdateSettingRequest(string? Value, bool? IsProvisional);

/// <summary>
/// Credential nhìn từ màn hình quản trị. <b>Không có trường nào chứa key gốc</b> — chỉ bản che.
/// </summary>
public sealed record CredentialView(
    Guid Id,
    string Provider,
    string ModelId,
    ProviderCategory Category,
    string? EndpointUrl,
    bool HasKey,
    string MaskedKey,
    bool IsActive,
    int Priority,
    string? Notes,
    JsonElement? Capability,
    DateTime? UpdatedAt)
{
    public static CredentialView From(ProviderCredential c) =>
        new(
            c.Id,
            c.Provider,
            c.ModelId,
            c.Category,
            c.EndpointUrl,
            !string.IsNullOrEmpty(c.EncryptedApiKey),
            CredentialAdmin.Mask(c.EncryptedApiKey),
            c.IsActive,
            c.Priority,
            c.Notes,
            TryParse(c.CapabilityJson),
            c.UpdatedAt ?? c.CreatedAt);

    private static JsonElement? TryParse(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <param name="ApiKey">Key gốc. Bỏ trống = giữ key đang có. Không bao giờ được trả ngược ra.</param>
/// <param name="Capability">Object capability. Bỏ trống = lấy từ descriptor rồi tới manifest viết tay.</param>
public sealed record UpsertCredentialRequest(
    string? ApiKey,
    string? ModelId,
    string? EndpointUrl,
    JsonElement? Capability,
    int? Priority,
    bool? IsActive,
    string? Note);

public sealed record UpsertCredentialResponse(CredentialView Credential, bool KeyChanged, IReadOnlyList<string> Notices);

public sealed record ReasonRequest(string? Reason);

public sealed record DescriptorView(
    Guid Id,
    string Code,
    int Version,
    string Kind,
    bool IsActive,
    string Sha256,
    string? ChangeNote,
    DateTime CreatedAt,
    JsonElement? Descriptor,
    string? DescriptorJson)
{
    /// <summary>Descriptor lưu nguyên văn, có thể kèm comment — đọc như bộ parse descriptor vẫn đọc.</summary>
    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <param name="includeJson">
    /// True = kèm cả bản đã parse (<see cref="Descriptor"/>, mất comment) lẫn nguyên văn
    /// (<see cref="DescriptorJson"/>, giữ comment — thứ người vận hành muốn đọc lại và sửa tiếp).
    /// </param>
    public static DescriptorView From(ProviderDescriptorRow row, bool includeJson)
    {
        JsonElement? json = null;

        if (includeJson)
        {
            using JsonDocument doc = JsonDocument.Parse(row.Json, Lenient);
            json = doc.RootElement.Clone();
        }

        return new DescriptorView(
            row.Id,
            row.Code,
            row.Version,
            row.Kind.ToString().ToLowerInvariant(),
            row.IsActive,
            row.Sha256,
            row.ChangeNote,
            row.CreatedAt,
            json,
            includeJson ? row.Json : null);
    }
}

/// <param name="Descriptor">Descriptor dạng JSON object.</param>
/// <param name="DescriptorJson">
/// Hoặc nguyên văn file descriptor dạng chuỗi — dùng khi dán từ <c>samples/providers</c>: file ở đó có
/// comment giải thích, JSON object thì không mang comment được mà chuỗi thì giữ nguyên. Có cả hai thì
/// lấy chuỗi.
/// </param>
public sealed record AddDescriptorRequest(JsonElement? Descriptor, string? DescriptorJson, string? Note)
{
    public string? RawJson => !string.IsNullOrWhiteSpace(DescriptorJson)
        ? DescriptorJson
        : Descriptor is { ValueKind: JsonValueKind.Object } d ? d.GetRawText() : null;
}

/// <summary>Chạy khô: gửi descriptor mới, hoặc chỉ tới bản đã lưu bằng <see cref="Provider"/> + <see cref="Version"/>.</summary>
public sealed record PreviewDescriptorRequest(JsonElement? Descriptor, string? DescriptorJson, string? Provider, int? Version)
{
    public string? RawJson => !string.IsNullOrWhiteSpace(DescriptorJson)
        ? DescriptorJson
        : Descriptor is { ValueKind: JsonValueKind.Object } d ? d.GetRawText() : null;
}

public sealed record DescriptorPreviewView(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    IReadOnlyList<string> Warnings);

public sealed record PromptView(
    Guid Id,
    string Code,
    int Version,
    PromptKind Kind,
    bool IsActive,
    string Content,
    string? FormatCode,
    string? ChangeNote,
    DateTime CreatedAt)
{
    public static PromptView From(PromptTemplate p) =>
        new(p.Id, p.Code, p.Version, p.Kind, p.IsActive, p.Content, p.FormatCode, p.ChangeNote, p.CreatedAt);
}

/// <param name="Activate">True = bật ngay bản vừa thêm. Mặc định false: thêm rồi xem lại, bật sau.</param>
public sealed record AddPromptVersionRequest(
    PromptKind? Kind,
    string? Content,
    string? ChangeNote,
    string? FormatCode,
    bool? Activate);

public sealed record TenantView(
    Guid Id,
    string Name,
    string ApiKeyPrefix,
    bool IsActive,
    string? Note,
    DateTime CreatedAt,
    DateTime? ApiKeyRotatedAt)
{
    public static TenantView From(Tenant t) =>
        new(t.Id, t.Name, t.ApiKeyPrefix, t.IsActive, t.Note, t.CreatedAt, t.ApiKeyRotatedAt);
}

public sealed record CreateTenantRequest(string? Name, string? Note);

/// <summary>Phản hồi DUY NHẤT có chứa API key gốc của tenant. Kèm <c>Cache-Control: no-store</c>.</summary>
public sealed record IssuedTenantKeyResponse(TenantView Tenant, string ApiKey);

/// <param name="Source"><c>minio</c> = font đã tải lên; <c>config</c> = <c>AdVideo:Ffmpeg:FontFile</c> của máy chạy worker.</param>
public sealed record LabelFontView(string Source, string? ObjectKey, string? Sha256, long? SizeBytes, string? Format, IReadOnlyList<string> MissingCharacters);
