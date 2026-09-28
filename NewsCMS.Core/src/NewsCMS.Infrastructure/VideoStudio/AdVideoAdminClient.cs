using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Infrastructure.VideoStudio;

/// <summary>Gọi <c>/v1/admin</c> của AdVideo bằng operator key đã lưu.</summary>
public sealed class AdVideoAdminClient : IAdVideoAdminClient
{
    private readonly IHttpClientFactory _http;
    private readonly AdVideoKeyStore _keys;

    public AdVideoAdminClient(IHttpClientFactory http, AdVideoKeyStore keys)
    {
        _http = http;
        _keys = keys;
    }

    public Task<Result<IReadOnlyList<AdVideoSettingDto>>> GetSettingsAsync(CancellationToken ct = default) =>
        Get<IReadOnlyList<AdVideoSettingDto>>("v1/admin/settings", ct);

    public Task<Result<AdVideoSettingDto>> UpdateSettingAsync(string key, string value, CancellationToken ct = default) =>
        Send<AdVideoSettingDto>(HttpMethod.Put, $"v1/admin/settings/{Uri.EscapeDataString(key)}", new { value }, ct);

    public Task<Result<IReadOnlyList<AdVideoCredentialDto>>> GetCredentialsAsync(CancellationToken ct = default) =>
        Get<IReadOnlyList<AdVideoCredentialDto>>("v1/admin/credentials", ct);

    public async Task<Result<AdVideoCredentialSaved>> SaveCredentialAsync(AdVideoCredentialInput input, CancellationToken ct = default)
    {
        System.Text.Json.JsonElement? capability = null;

        if (!string.IsNullOrWhiteSpace(input.CapabilityJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(input.CapabilityJson);
                capability = doc.RootElement.Clone();
            }
            catch (System.Text.Json.JsonException ex)
            {
                return Result<AdVideoCredentialSaved>.Failure($"Capability không phải JSON hợp lệ: {ex.Message}");
            }
        }

        return await Send<AdVideoCredentialSaved>(
            HttpMethod.Put,
            $"v1/admin/credentials/{Uri.EscapeDataString(input.Provider.Trim().ToLowerInvariant())}",
            new
            {
                api_key = string.IsNullOrWhiteSpace(input.ApiKey) ? null : input.ApiKey.Trim(),
                model_id = NullIfBlank(input.ModelId),
                endpoint_url = NullIfBlank(input.EndpointUrl),
                capability,
                priority = input.Priority,
                is_active = input.IsActive,
                note = NullIfBlank(input.Note),
            },
            ct);
    }

    public Task<Result> DeactivateCredentialAsync(string provider, string? reason, CancellationToken ct = default) =>
        SendNoContent(HttpMethod.Post, $"v1/admin/credentials/{Uri.EscapeDataString(provider)}/deactivate", new { reason }, ct);

    public Task<Result<IReadOnlyList<AdVideoDescriptorDto>>> GetDescriptorsAsync(string? provider = null, CancellationToken ct = default) =>
        Get<IReadOnlyList<AdVideoDescriptorDto>>(
            string.IsNullOrWhiteSpace(provider) ? "v1/admin/descriptors" : $"v1/admin/descriptors?provider={Uri.EscapeDataString(provider)}", ct);

    public Task<Result<AdVideoDescriptorDto>> GetDescriptorAsync(string provider, int version, CancellationToken ct = default) =>
        Get<AdVideoDescriptorDto>($"v1/admin/descriptors/{Uri.EscapeDataString(provider)}/versions/{version}", ct);

    public Task<Result<AdVideoDescriptorDto>> AddDescriptorAsync(string descriptorJson, string? note, CancellationToken ct = default) =>
        Send<AdVideoDescriptorDto>(HttpMethod.Post, "v1/admin/descriptors", new { descriptor_json = descriptorJson, note = NullIfBlank(note) }, ct);

    public Task<Result<AdVideoDescriptorPreviewDto>> PreviewDescriptorAsync(string provider, int version, CancellationToken ct = default) =>
        Send<AdVideoDescriptorPreviewDto>(HttpMethod.Post, "v1/admin/descriptors/preview", new { provider, version }, ct);

    public Task<Result> ActivateDescriptorAsync(string provider, int version, CancellationToken ct = default) =>
        SendNoContent(HttpMethod.Post, $"v1/admin/descriptors/{Uri.EscapeDataString(provider)}/versions/{version}/activate", null, ct);

    public Task<Result> DeactivateDescriptorAsync(string provider, CancellationToken ct = default) =>
        SendNoContent(HttpMethod.Post, $"v1/admin/descriptors/{Uri.EscapeDataString(provider)}/deactivate", null, ct);

    public Task<Result<IReadOnlyList<AdVideoPromptDto>>> GetPromptsAsync(CancellationToken ct = default) =>
        Get<IReadOnlyList<AdVideoPromptDto>>("v1/admin/prompts", ct);

    public Task<Result<AdVideoPromptDto>> AddPromptVersionAsync(AdVideoPromptInput input, CancellationToken ct = default) =>
        Send<AdVideoPromptDto>(
            HttpMethod.Post,
            $"v1/admin/prompts/{Uri.EscapeDataString(input.Code.Trim())}/versions",
            new
            {
                kind = input.Kind,
                content = input.Content,
                change_note = input.ChangeNote,
                format_code = NullIfBlank(input.FormatCode),
                activate = input.Activate,
            },
            ct);

    public Task<Result> ActivatePromptAsync(string code, int version, CancellationToken ct = default) =>
        SendNoContent(HttpMethod.Post, $"v1/admin/prompts/{Uri.EscapeDataString(code)}/versions/{version}/activate", null, ct);

    public Task<Result<IReadOnlyList<AdVideoTenantDto>>> GetTenantsAsync(CancellationToken ct = default) =>
        Get<IReadOnlyList<AdVideoTenantDto>>("v1/admin/tenants", ct);

    public Task<Result<AdVideoIssuedTenantKey>> CreateTenantAsync(string name, string? note, CancellationToken ct = default) =>
        Send<AdVideoIssuedTenantKey>(HttpMethod.Post, "v1/admin/tenants", new { name, note = NullIfBlank(note) }, ct);

    public Task<Result<AdVideoIssuedTenantKey>> RotateTenantKeyAsync(Guid tenantId, CancellationToken ct = default) =>
        Send<AdVideoIssuedTenantKey>(HttpMethod.Post, $"v1/admin/tenants/{tenantId}/rotate-key", null, ct);

    public async Task<Result> SetTenantActiveAsync(Guid tenantId, bool isActive, string? reason, CancellationToken ct = default)
    {
        Result<AdVideoTenantDto> result = await Send<AdVideoTenantDto>(
            HttpMethod.Post, $"v1/admin/tenants/{tenantId}/{(isActive ? "activate" : "deactivate")}", new { reason }, ct);

        return result.Succeeded ? Result.Success() : Result.Failure(result.Error!);
    }

    public Task<Result<AdVideoLabelFontDto>> GetLabelFontAsync(CancellationToken ct = default) =>
        Get<AdVideoLabelFontDto>("v1/admin/assets/label-font", ct);

    public async Task<Result<AdVideoLabelFontDto>> UploadLabelFontAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        (AdVideoEndpoint? endpoint, string? error) = await _keys.GetOperatorEndpointAsync(ct);

        if (endpoint is null)
        {
            return Result<AdVideoLabelFontDto>.Failure(error!);
        }

        using HttpClient client = AdVideoHttp.CreateClient(_http, endpoint);
        var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", Path.GetFileName(fileName));

        return await AdVideoHttp.SendAsync<AdVideoLabelFontDto>(client, HttpMethod.Post, "v1/admin/assets/label-font", form, ct);
    }

    public Task<Result> ResetLabelFontAsync(CancellationToken ct = default) =>
        SendNoContent(HttpMethod.Delete, "v1/admin/assets/label-font", null, ct);

    // ------------------------------------------------------------------

    private Task<Result<T>> Get<T>(string path, CancellationToken ct) => Send<T>(HttpMethod.Get, path, null, ct);

    private async Task<Result<T>> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        (AdVideoEndpoint? endpoint, string? error) = await _keys.GetOperatorEndpointAsync(ct);

        if (endpoint is null)
        {
            return Result<T>.Failure(error!);
        }

        using HttpClient client = AdVideoHttp.CreateClient(_http, endpoint);

        return await AdVideoHttp.SendAsync<T>(client, method, path, body is null ? null : AdVideoHttp.JsonBody(body), ct);
    }

    private async Task<Result> SendNoContent(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        (AdVideoEndpoint? endpoint, string? error) = await _keys.GetOperatorEndpointAsync(ct);

        if (endpoint is null)
        {
            return Result.Failure(error!);
        }

        using HttpClient client = AdVideoHttp.CreateClient(_http, endpoint);

        return await AdVideoHttp.SendAsync(client, method, path, body is null ? null : AdVideoHttp.JsonBody(body), ct);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
