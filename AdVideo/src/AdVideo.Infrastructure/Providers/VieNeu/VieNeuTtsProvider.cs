using System.Net.Http.Json;
using System.Text.Json;
using AdVideo.Core.Configuration;
using AdVideo.Core.Providers;
using AdVideo.Core.Qc;
using AdVideo.Infrastructure.Media;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers.VieNeu;

/// <summary>
/// Adapter cho VieNeu-TTS tự host.
/// </summary>
/// <remarks>
/// <para>
/// <b>Engine này KHÔNG trả mốc thời gian.</b> Vì thế capability nạp từ DB phải để
/// <c>HasWordTimings = false</c>, và bộ chọn provider chỉ được dùng nó cho tier Nháp. Nếu vì lý
/// do nào đó nó được gọi cho bản thành phẩm, <see cref="TtsResult.CanLockTimeline"/> trả false và
/// bước 5 dừng lại — đó là hành vi đúng, tốt hơn nhiều so với khoá timeline bằng số liệu đoán.
/// </para>
/// <para>
/// <b>Chỉ có ba vùng giọng: Bắc, Trung, Nam.</b> Tài liệu nguồn của dự án ghi "23 preset vùng
/// miền" — sai. Đừng hứa với khách tính năng chọn giọng theo tỉnh thành.
/// </para>
/// <para>
/// <b>Độ dài audio đo bằng ffprobe, không ước lượng.</b> Engine tự host không báo độ dài, mà bước
/// sau cần con số thật. Ước lượng theo số ký tự là cách chắc chắn nhất để tiếng và hình lệch nhau.
/// </para>
/// <para>
/// <b>Không có lớp kiểm duyệt nào của nhà cung cấp.</b> Tự host nghĩa là clone giọng từ 3–8 giây
/// mẫu mà không ai chặn — rủi ro R6 nằm hoàn toàn ở phía hệ thống này
/// (<c>ConsentRecord</c>), không phải ở phía engine.
/// </para>
/// </remarks>
public sealed class VieNeuTtsProvider : ITtsProvider
{
    /// <summary>Ba vùng giọng thật sự có. Dùng để cảnh báo sớm khi voice id trông không giống vùng nào.</summary>
    private static readonly string[] KnownRegions = ["bac", "trung", "nam", "north", "central", "south"];

    private readonly HttpClient _http;
    private readonly ResolvedCredential _credential;
    private readonly IMediaInspector _inspector;
    private readonly ILogger _logger;

    public VieNeuTtsProvider(
        HttpClient http,
        ResolvedCredential credential,
        TtsProviderCapability capability,
        IMediaInspector inspector,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(credential);

        _http = http;
        _credential = credential;
        _inspector = inspector;
        _logger = logger;

        Capability = capability;

        // Bản tự host có thể chạy trần trong mạng nội bộ, không đặt key. Chỉ gắn header khi thật
        // sự có key, để một chuỗi rỗng không biến thành "Bearer " và bị máy chủ từ chối.
        if (!string.IsNullOrWhiteSpace(credential.ApiKey))
        {
            _http.DefaultRequestHeaders.Remove("Authorization");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {credential.ApiKey}");
        }
    }

    public string Name => _credential.Provider;

    public TtsProviderCapability Capability { get; }

    public async Task<TtsResult> SynthesizeAsync(
        TtsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!KnownRegions.Any(region => request.VoiceId.Contains(region, StringComparison.OrdinalIgnoreCase)))
        {
            // Không chặn: bản tự host có thể đã nạp thêm giọng clone với tên tuỳ ý. Chỉ ghi lại,
            // vì gõ nhầm tên vùng thì máy chủ lặng lẽ đọc bằng giọng mặc định.
            _logger.LogInformation(
                "Voice id {VoiceId} không khớp vùng nào của VieNeu (bac/trung/nam) — kiểm tra lại nếu giọng đọc ra không đúng.",
                request.VoiceId);
        }

        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            $"{BaseUrl()}/tts",
            BuildPayload(request),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            return new TtsResult
            {
                IsSuccess = false,
                FailureKind = ProviderFailureMapper.FromStatus(response.StatusCode, body),
                FailureReason = $"VieNeu trả {(int)response.StatusCode}.",
                RawError = body,
                RetryAfterSeconds = ProviderFailureMapper.ReadRetryAfter(response),
            };
        }

        (byte[]? audio, string contentType, string? failure) = await ReadAudioAsync(response, cancellationToken);

        if (audio is null || audio.Length == 0)
        {
            return new TtsResult
            {
                IsSuccess = false,
                FailureKind = VideoFailureKind.Unknown,
                FailureReason = failure ?? "VieNeu trả 200 nhưng không kèm audio.",
            };
        }

        double duration = await MeasureDurationAsync(audio, contentType, request.JobId, cancellationToken);

        if (duration <= 0)
        {
            return new TtsResult
            {
                IsSuccess = false,

                // Không đo được độ dài thì mọi bước sau đều mù. Dừng ở đây rẻ hơn là để bước ghép
                // dựng ra một video dài sai.
                FailureKind = VideoFailureKind.ProviderUnavailable,
                FailureReason = "Không đo được độ dài audio của VieNeu bằng ffprobe.",
            };
        }

        _logger.LogDebug(
            "VieNeu đã dựng {Seconds:0.##} giây audio cho job {JobId} bằng giọng {VoiceId}.",
            duration,
            request.JobId,
            request.VoiceId);

        return new TtsResult
        {
            IsSuccess = true,
            AudioBytes = audio,
            AudioContentType = contentType,
            AudioDurationSeconds = duration,

            // Cố tình để rỗng. Xem ghi chú đầu lớp: engine này không có mốc thời gian, và giả vờ
            // có bằng cách chia đều theo từ là cách tạo ra timeline sai mà không ai phát hiện.
            WordTimings = [],
            CharacterTimings = [],
            ProviderRequestId = null,

            // Tự host: tiền đã trả cho VPS, mỗi lần gọi không phát sinh thêm. Ghi 0 để bảng đối
            // soát phân biệt được "miễn phí" với "chưa biết giá".
            ReportedCostUsd = 0m,
            BilledCharacterCount = request.Text.Length,
        };
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync($"{BaseUrl()}/health", cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            // Máy chủ tự host tắt hẳn là chuyện bình thường (VPS restart), nên đây là cảnh báo
            // chứ không phải lỗi.
            _logger.LogWarning(ex, "Ping VieNeu thất bại — máy chủ tự host có đang chạy không?");

            return false;
        }
    }

    private string BaseUrl()
        => string.IsNullOrWhiteSpace(_credential.Endpoint)
            ? "http://127.0.0.1:8080"
            : _credential.Endpoint.TrimEnd('/');

    private Dictionary<string, object?> BuildPayload(TtsRequest request)
        => new(StringComparer.Ordinal)
        {
            ["text"] = request.Text,

            // Gửi cả hai tên: bản tự host đọc "voice", một số nhánh đọc "region". Trường không
            // biết bị bỏ qua, còn thiếu trường đúng thì đọc bằng giọng mặc định mà không báo lỗi.
            ["voice"] = request.VoiceId,
            ["region"] = request.VoiceId,
            ["model"] = _credential.ModelId,
            ["speed"] = decimal.ToDouble(request.Speed <= 0 ? 1.0m : request.Speed),
        };

    /// <remarks>
    /// Máy chủ tự host trả về một trong hai dạng tuỳ phiên bản: luồng audio nhị phân, hoặc JSON
    /// bọc base64. Nhận cả hai để nâng cấp máy chủ không làm hỏng adapter.
    /// </remarks>
    private static async Task<(byte[]? Audio, string ContentType, string? Failure)> ReadAudioAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        if (!mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            return (bytes, NormalizeAudioType(mediaType), null);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        string? base64 = ProviderJson.FindString(document.RootElement, "audio_base64")
            ?? ProviderJson.FindString(document.RootElement, "audio");

        if (string.IsNullOrWhiteSpace(base64))
        {
            return (null, "audio/wav", "Phản hồi JSON của VieNeu không có trường audio.");
        }

        try
        {
            return (Convert.FromBase64String(base64), "audio/wav", null);
        }
        catch (FormatException)
        {
            return (null, "audio/wav", "Trường audio của VieNeu không phải base64 hợp lệ.");
        }
    }

    private async Task<double> MeasureDurationAsync(
        byte[] audio,
        string contentType,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"advideo-vieneu-{jobId:N}-{Guid.NewGuid():N}{ExtensionFor(contentType)}");

        try
        {
            await File.WriteAllBytesAsync(path, audio, cancellationToken);

            MediaProbeResult probe = await _inspector.ProbeAsync(path, cancellationToken: cancellationToken);

            return probe.DurationSeconds;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Không ghi được file tạm để đo audio của VieNeu.");

            return 0;
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Không xoá được file tạm {Path}.", path);
            }
        }
    }

    private static string NormalizeAudioType(string mediaType)
        => mediaType.Contains("mpeg", StringComparison.OrdinalIgnoreCase) ? "audio/mpeg" : "audio/wav";

    private static string ExtensionFor(string contentType)
        => contentType.Contains("mpeg", StringComparison.OrdinalIgnoreCase) ? ".mp3" : ".wav";
}
