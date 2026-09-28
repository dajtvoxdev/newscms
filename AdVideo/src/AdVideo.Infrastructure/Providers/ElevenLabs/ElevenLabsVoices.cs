using System.Net.Http.Headers;
using System.Text.Json;
using AdVideo.Core.Configuration;
using AdVideo.Core.Providers;
using AdVideo.Core.Security;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers.ElevenLabs;

/// <summary>
/// Thư viện giọng và Instant Voice Cloning của ElevenLabs.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /v1/voices</c> trả mọi giọng tài khoản dùng được (giọng dựng sẵn + giọng đã thêm + giọng
/// clone). <c>POST /v1/voices/add</c> clone từ vài file mẫu, trả <c>voice_id</c> dùng ngay được với
/// <c>/text-to-speech</c>. Model <c>eleven_flash_v2_5</c> đọc tiếng Việt bằng MỌI giọng, kể cả giọng
/// clone từ mẫu tiếng Anh — nhưng mẫu tiếng Việt cho chất giọng tự nhiên hơn nhiều.
/// </para>
/// <para>
/// Mỗi giọng clone chiếm một "slot" của gói ElevenLabs (gói thấp ~10–30 slot cho cả tài khoản) —
/// lý do có setting <c>MaxClonedVoicesPerTenant</c> và xoá bên ElevenLabs khi khách xoá giọng.
/// </para>
/// </remarks>
public sealed class ElevenLabsVoices : IProviderVoices
{
    private readonly HttpClient _http;
    private readonly ResolvedCredential _credential;
    private readonly ILogger _logger;

    public ElevenLabsVoices(HttpClient http, ResolvedCredential credential, ILogger logger)
    {
        _http = http;
        _credential = credential;
        _logger = logger;
    }

    public string Name => _credential.Provider;

    public bool SupportsCloning => true;

    public async Task<IReadOnlyList<ProviderVoice>> ListAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.SendAsync(Authorized(HttpMethod.Get, "/v1/voices"), cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ElevenLabs trả {(int)response.StatusCode} khi liệt kê giọng: {Redact(Truncate(body))}");
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        var voices = new List<ProviderVoice>();

        if (doc.RootElement.TryGetProperty("voices", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement v in list.EnumerateArray())
            {
                string? id = Text(v, "voice_id");

                if (id is null)
                {
                    continue;
                }

                var labels = new Dictionary<string, string>(StringComparer.Ordinal);

                if (v.TryGetProperty("labels", out JsonElement l) && l.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in l.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                    {
                        labels[p.Name] = p.Value.GetString()!;
                    }
                }

                voices.Add(new ProviderVoice(id, Text(v, "name") ?? id, Text(v, "category"), Text(v, "description"), Text(v, "preview_url"), labels));
            }
        }

        return voices;
    }

    public async Task<VoiceCloneResult> CloneAsync(
        string name, string? description, IReadOnlyList<VoiceSample> samples, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(name), "name" },

            // Mẫu ghi bằng điện thoại trong cửa hàng hay lẫn tiếng ồn; lọc nền trước khi học giọng.
            { new StringContent("true"), "remove_background_noise" },
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            form.Add(new StringContent(description), "description");
        }

        foreach (VoiceSample sample in samples)
        {
            var file = new ByteArrayContent(sample.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(sample.ContentType);
            form.Add(file, "files", sample.FileName);
        }

        HttpRequestMessage request = Authorized(HttpMethod.Post, "/v1/voices/add");
        request.Content = form;

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return VoiceCloneResult.Fail(
                $"ElevenLabs không clone được giọng ({(int)response.StatusCode}): {Detail(body)}",
                Redact(Truncate(body)));
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        string? voiceId = Text(doc.RootElement, "voice_id");

        if (voiceId is null)
        {
            return VoiceCloneResult.Fail("ElevenLabs trả 200 nhưng không có voice_id.", Redact(Truncate(body)));
        }

        if (doc.RootElement.TryGetProperty("requires_verification", out JsonElement verify) && verify.ValueKind == JsonValueKind.True)
        {
            _logger.LogWarning("Giọng clone {VoiceId} cần xác minh bên ElevenLabs trước khi dùng.", voiceId);
        }

        return new VoiceCloneResult(true, voiceId, null);
    }

    public async Task<bool> DeleteAsync(string voiceId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.SendAsync(
            Authorized(HttpMethod.Delete, $"/v1/voices/{Uri.EscapeDataString(voiceId)}"), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ElevenLabs trả {Status} khi xoá giọng {VoiceId}.", (int)response.StatusCode, voiceId);
        }

        return response.IsSuccessStatusCode;
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path) =>
        ProviderHttp.Create(method, BaseUrl() + path, new Uri(BaseUrl()), "xi-api-key", _credential.ApiKey);

    private string BaseUrl()
        => string.IsNullOrWhiteSpace(_credential.Endpoint)
            ? "https://api.elevenlabs.io"
            : _credential.Endpoint.TrimEnd('/');

    private string? Redact(string? text) => SecretRedactor.RedactKnown(text, _credential.ApiKey);

    /// <summary>ElevenLabs trả lỗi dạng <c>{"detail":{"message":"…"}}</c> hoặc <c>{"detail":"…"}</c>.</summary>
    private string Detail(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("detail", out JsonElement detail))
            {
                string? message = detail.ValueKind == JsonValueKind.String ? detail.GetString() : Text(detail, "message");

                if (!string.IsNullOrWhiteSpace(message))
                {
                    return Redact(message)!;
                }
            }
        }
        catch (JsonException)
        {
        }

        return Redact(Truncate(body)) ?? "không rõ lý do";
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
