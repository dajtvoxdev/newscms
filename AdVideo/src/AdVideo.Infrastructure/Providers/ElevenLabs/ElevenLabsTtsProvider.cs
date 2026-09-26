using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AdVideo.Core.Configuration;
using AdVideo.Core.Providers;
using AdVideo.Core.Security;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers.ElevenLabs;

/// <summary>
/// Adapter ElevenLabs, luôn gọi biến thể <c>/with-timestamps</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Header là <c>xi-api-key</c>.</b> Không phải Authorization, không phải Bearer.
/// </para>
/// <para>
/// <b>Luôn dùng <c>/with-timestamps</c>, kể cả khi không cần phụ đề.</b> Endpoint thường chỉ trả
/// về một luồng audio trần, không có mốc thời gian — mà không có mốc thì bước 5 không khoá được
/// timeline. Hai endpoint cùng giá, nên không có lý do gì để gọi cái kia.
/// </para>
/// <para>
/// <b>Mốc trả về là theo KÝ TỰ, không theo từ.</b> Ghép lại thành từ là việc của adapter này —
/// xem <see cref="WordTimingBuilder"/>. Đây cũng là chỗ duy nhất trong hệ thống biết hình dạng
/// alignment của ElevenLabs.
/// </para>
/// <para>
/// <b>Giọng "Default" của ElevenLabs ngừng phục vụ từ 31/12/2026.</b> Voice id cấu hình trong DB
/// phải là giọng đã thêm vào thư viện của tài khoản, không phải id mẫu chép từ tài liệu.
/// </para>
/// </remarks>
public sealed class ElevenLabsTtsProvider : ITtsProvider
{
    /// <summary>Định dạng đầu ra. mp3 128kbps là mức cao nhất mà gói trả phí thấp nhất cho phép.</summary>
    private const string OutputFormat = "mp3_44100_128";

    private readonly HttpClient _http;
    private readonly ResolvedCredential _credential;
    private readonly ILogger _logger;

    public ElevenLabsTtsProvider(
        HttpClient http,
        ResolvedCredential credential,
        TtsProviderCapability capability,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(credential);

        _http = http;
        _credential = credential;
        _logger = logger;

        Capability = capability;
    }

    public string Name => _credential.Provider;

    public TtsProviderCapability Capability { get; }

    public async Task<TtsResult> SynthesizeAsync(
        TtsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string url = $"{BaseUrl()}/v1/text-to-speech/{Uri.EscapeDataString(request.VoiceId)}/with-timestamps"
            + $"?output_format={OutputFormat}";

        using HttpResponseMessage response = await _http.SendAsync(Authorized(HttpMethod.Post, url, BuildPayload(request)), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            return new TtsResult
            {
                IsSuccess = false,
                FailureKind = ProviderFailureMapper.FromStatus(response.StatusCode, body),
                FailureReason = $"ElevenLabs trả {(int)response.StatusCode}.",
                RawError = Redact(body),
                RetryAfterSeconds = ProviderFailureMapper.ReadRetryAfter(response),
            };
        }

        JsonDocument document;

        await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            string? audioBase64 = ProviderJson.FindString(root, "audio_base64");

            if (string.IsNullOrWhiteSpace(audioBase64))
            {
                return new TtsResult
                {
                    IsSuccess = false,
                    FailureKind = VideoFailureKind.Unknown,
                    FailureReason = "ElevenLabs trả 200 nhưng không có audio_base64 trong phản hồi.",
                    RawError = Redact(Truncate(root.ToString())),
                };
            }

            byte[] audio = Convert.FromBase64String(audioBase64);

            // alignment bám theo văn bản ĐÃ GỬI; normalized_alignment bám theo văn bản sau khi
            // ElevenLabs chuẩn hoá (số đọc thành chữ, viết tắt bung ra). Ta cần cái thứ nhất để
            // mốc khớp với đúng những từ mình dựng phụ đề.
            IReadOnlyList<CharacterTiming> characters = ReadAlignment(root, "alignment")
                ?? ReadAlignment(root, "normalized_alignment")
                ?? [];

            if (characters.Count == 0)
            {
                return new TtsResult
                {
                    IsSuccess = false,
                    FailureKind = VideoFailureKind.Unknown,
                    FailureReason = "ElevenLabs trả audio nhưng thiếu alignment — không khoá được timeline.",
                    RawError = Redact(Truncate(root.ToString())),
                };
            }

            IReadOnlyList<WordTiming> words = WordTimingBuilder.FromCharacters(characters);
            double duration = characters[^1].EndSeconds;

            int billedCharacters = ReadCharacterCount(response) ?? request.Text.Length;

            _logger.LogDebug(
                "ElevenLabs đã đọc {Chars} ký tự thành {Seconds:0.##} giây cho job {JobId}.",
                billedCharacters,
                duration,
                request.JobId);

            return new TtsResult
            {
                IsSuccess = true,
                AudioBytes = audio,
                AudioContentType = "audio/mpeg",
                AudioDurationSeconds = duration,
                WordTimings = words,
                CharacterTimings = characters,

                // Header request-id là thứ duy nhất cho phép nối ngữ điệu ở shot sau
                // (TtsRequest.PreviousRequestId), nên phải giữ lại.
                ProviderRequestId = ReadHeader(response, "request-id"),
                BilledCharacterCount = billedCharacters,

                // KHÔNG điền ReportedCostUsd: ElevenLabs báo số ký tự, không báo tiền. Điền đơn giá
                // manifest × ký tự vào đây là đánh dấu CostIsReported = true cho một con số tự suy,
                // và mất khả năng đối soát hoá đơn. TtsStep tự suy từ BilledCharacterCount.
            };
        }
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // /v1/user trả hạn mức ký tự còn lại: xác nhận key mà không tốn ký tự nào.
            using HttpResponseMessage response = await _http.SendAsync(Authorized(HttpMethod.Get, $"{BaseUrl()}/v1/user"), cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Ping ElevenLabs thất bại cho provider {Provider}.", Name);

            return false;
        }
    }

    private HttpRequestMessage Authorized(HttpMethod method, string url, object? body = null) =>
        ProviderHttp.Create(method, url, new Uri(BaseUrl()), "xi-api-key", _credential.ApiKey, body);

    private string? Redact(string? text) => SecretRedactor.RedactKnown(text, _credential.ApiKey);

    private string BaseUrl()
        => string.IsNullOrWhiteSpace(_credential.Endpoint)
            ? "https://api.elevenlabs.io"
            : _credential.Endpoint.TrimEnd('/');

    private Dictionary<string, object?> BuildPayload(TtsRequest request)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["text"] = request.Text,
            ["model_id"] = _credential.ModelId,
            ["voice_settings"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                // speed nằm trong voice_settings chứ không phải ở gốc payload; đặt sai chỗ thì API
                // vẫn trả 200 và đọc với tốc độ mặc định, nên lỗi này rất khó thấy.
                ["speed"] = decimal.ToDouble(request.Speed <= 0 ? 1.0m : request.Speed),
            },
        };

        if (Capability.SupportsProsodyContinuation)
        {
            // previous_request_ids chính xác hơn previous_text vì nó nối đúng lần đọc trước, nhưng
            // chỉ còn hiệu lực trong vài giờ. Gửi kèm cả hai để hết hạn vẫn còn ngữ cảnh văn bản.
            if (!string.IsNullOrWhiteSpace(request.PreviousRequestId))
            {
                payload["previous_request_ids"] = new[] { request.PreviousRequestId };
            }

            if (!string.IsNullOrWhiteSpace(request.PreviousText))
            {
                payload["previous_text"] = request.PreviousText;
            }
        }

        return payload;
    }

    private static IReadOnlyList<CharacterTiming>? ReadAlignment(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement alignment)
            || alignment.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!alignment.TryGetProperty("characters", out JsonElement characters)
            || !alignment.TryGetProperty("character_start_times_seconds", out JsonElement starts)
            || !alignment.TryGetProperty("character_end_times_seconds", out JsonElement ends))
        {
            return null;
        }

        // Ba mảng song song. Dùng độ dài NGẮN NHẤT thay vì tin mảng ký tự: lệch độ dài là chuyện
        // không nên xảy ra, nhưng nếu xảy ra thì cắt bớt vẫn hơn là ném IndexOutOfRange giữa job.
        int count = Math.Min(characters.GetArrayLength(), Math.Min(starts.GetArrayLength(), ends.GetArrayLength()));

        if (count == 0)
        {
            return null;
        }

        var result = new List<CharacterTiming>(count);

        for (int i = 0; i < count; i++)
        {
            string? text = characters[i].GetString();

            result.Add(new CharacterTiming(
                string.IsNullOrEmpty(text) ? ' ' : text[0],
                starts[i].GetDouble(),
                ends[i].GetDouble()));
        }

        return result;
    }

    /// <summary>Số ký tự bị tính tiền, lấy từ header để đối soát với hoá đơn.</summary>
    private static int? ReadCharacterCount(HttpResponseMessage response)
    {
        string? raw = ReadHeader(response, "xi-character-count")
            ?? ReadHeader(response, "character-cost");

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static string? ReadHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static string Truncate(string value)
        => value.Length <= 2000 ? value : value[..2000];
}
