using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdVideo.Core.Configuration;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers.Fal;

/// <summary>
/// Gọi model video qua hàng đợi của fal.ai — dùng chung cho Kling, Seedance và Vidu.
/// </summary>
/// <remarks>
/// <para>
/// <b>Header là <c>Authorization: Key &lt;key&gt;</c>, KHÔNG phải <c>Bearer</c>.</b> fal.ai dùng
/// lược đồ riêng. Gửi Bearer thì nhận 401 và đi tìm lỗi ở phía key.
/// </para>
/// <para>
/// <b>Ba lần gọi cho một lần render:</b> gửi vào hàng đợi, hỏi trạng thái tới khi xong, rồi lấy
/// kết quả ở một URL khác. Hai URL đó do phản hồi đầu tiên cung cấp và phải dùng đúng cái được
/// trả về, không tự ghép — fal.ai đổi host giữa các vùng.
/// </para>
/// <para>
/// <b>Vidu bật âm thanh gốc theo mặc định.</b> Đây là lỗi hay gặp nhất khi ghép: giọng đọc chồng
/// lên tiếng của model. Xem <see cref="BuildPayload"/>.
/// </para>
/// </remarks>
public sealed class FalQueueVideoProvider : IVideoProvider
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly ResolvedCredential _credential;
    private readonly ILogger _logger;

    public FalQueueVideoProvider(
        HttpClient http,
        ResolvedCredential credential,
        VideoProviderCapability capability,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(credential);

        _http = http;
        _credential = credential;
        _logger = logger;

        Capability = capability;

        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Key {credential.ApiKey}");
    }

    public string Name => _credential.Provider;

    public VideoProviderCapability Capability { get; }

    public async Task<VideoResult> GenerateAsync(
        VideoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string submitUrl = BuildSubmitUrl();

        using HttpResponseMessage submit = await _http.PostAsJsonAsync(
            submitUrl,
            BuildPayload(request),
            cancellationToken);

        if (!submit.IsSuccessStatusCode)
        {
            return await BuildFailureAsync(submit, "gửi yêu cầu", cancellationToken);
        }

        using JsonDocument queued = await ReadJsonAsync(submit, cancellationToken);

        string? requestId = ProviderJson.FindString(queued.RootElement, "request_id");
        string? statusUrl = ProviderJson.FindString(queued.RootElement, "status_url");
        string? responseUrl = ProviderJson.FindString(queued.RootElement, "response_url");

        if (statusUrl is null || responseUrl is null)
        {
            return new VideoResult
            {
                IsSuccess = false,
                FailureKind = VideoFailureKind.Unknown,
                FailureReason = "fal.ai nhận yêu cầu nhưng không trả về status_url/response_url.",
                RawError = queued.RootElement.ToString(),
            };
        }

        _logger.LogDebug(
            "Đã đưa shot {ShotIndex} của job {JobId} vào hàng đợi fal.ai, request_id={RequestId}.",
            request.ShotIndex,
            request.JobId,
            requestId);

        VideoResult? pollFailure = await WaitUntilCompletedAsync(statusUrl, cancellationToken);

        if (pollFailure is not null)
        {
            return pollFailure;
        }

        using HttpResponseMessage final = await _http.GetAsync(responseUrl, cancellationToken);

        if (!final.IsSuccessStatusCode)
        {
            return await BuildFailureAsync(final, "lấy kết quả", cancellationToken);
        }

        using JsonDocument payload = await ReadJsonAsync(final, cancellationToken);

        Uri? videoUri = ProviderJson.FindVideoUrl(payload.RootElement);

        if (videoUri is null)
        {
            return new VideoResult
            {
                IsSuccess = false,
                FailureKind = VideoFailureKind.Unknown,
                FailureReason = "Không tìm thấy URL video trong kết quả của fal.ai.",
                RawError = payload.RootElement.ToString(),
            };
        }

        return new VideoResult
        {
            IsSuccess = true,
            ProviderRequestId = requestId,
            VideoUri = videoUri,

            // Chỉ biết chắc là CÓ tiếng khi capability nói model sinh tiếng và ta không tắt nó.
            // Con số thật do ffprobe xác nhận ở bước tải về — đây chỉ là dự đoán để bước sau biết
            // đường xử lý.
            HasNativeAudio = Capability.GeneratesNativeAudio && !request.SuppressNativeAudio,
            MeasuredDurationSeconds = request.DurationSeconds,
            ReportedCostUsd = Capability.CostPerSecondUsd * request.DurationSeconds,
        };
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Không có endpoint health riêng. Gọi thẳng vào model với payload rỗng: 401/403 nghĩa
            // là key hỏng, còn 422 (payload sai) lại là tin tốt — nghĩa là key được chấp nhận.
            using HttpResponseMessage response = await _http.PostAsJsonAsync(
                BuildSubmitUrl(),
                new { },
                cancellationToken);

            return response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Ping fal.ai thất bại cho provider {Provider}.", Name);

            return false;
        }
    }

    private string BuildSubmitUrl()
    {
        // Endpoint lấy từ DB (D10) để đổi model không cần deploy. Model id nằm ở phần đường dẫn,
        // ví dụ https://queue.fal.run/fal-ai/kling-video/v2/master/text-to-video.
        string endpoint = string.IsNullOrWhiteSpace(_credential.Endpoint)
            ? "https://queue.fal.run"
            : _credential.Endpoint.TrimEnd('/');

        return $"{endpoint}/{_credential.ModelId.TrimStart('/')}";
    }

    /// <remarks>
    /// Payload dùng tên trường chung của fal.ai. Phần khác biệt giữa các model gom hết vào đây để
    /// khi thêm model mới thì chỉ sửa một chỗ, không phải rải điều kiện khắp file.
    /// </remarks>
    private Dictionary<string, object?> BuildPayload(VideoRequest request)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["prompt"] = request.Prompt,
            ["duration"] = request.DurationSeconds.ToString(CultureInfo.InvariantCulture),
            ["aspect_ratio"] = ToAspectRatioString(request.AspectRatio),
        };

        if (!string.IsNullOrWhiteSpace(request.NegativePrompt))
        {
            payload["negative_prompt"] = request.NegativePrompt;
        }

        if (request.Seed is { } seed && Capability.SupportsSeed)
        {
            payload["seed"] = seed;
        }

        if (request.ReferenceImageUrls.Count > 0 && Capability.SupportsImageToVideo)
        {
            // Model image-to-video của fal.ai nhận một ảnh ở image_url; model nhiều ảnh nhận mảng.
            // Gửi cả hai dạng thì model bỏ qua trường nó không biết, còn thiếu thì mất ảnh tham chiếu.
            payload["image_url"] = request.ReferenceImageUrls[0];
            payload["image_urls"] = request.ReferenceImageUrls.Take(Math.Max(1, Capability.MaxReferenceImages)).ToArray();
        }

        if (request.SuppressNativeAudio)
        {
            // Ba tên trường cho cùng một ý "đừng sinh tiếng", vì ba model gọi nó ba kiểu. Vidu bật
            // sẵn nên thiếu dòng này là giọng đọc chồng lên tiếng model ở bản dựng cuối — lỗi tốn
            // thời gian nhất để phát hiện, vì video vẫn phát bình thường.
            payload["enable_audio"] = false;
            payload["generate_audio"] = false;
            payload["bgm"] = false;
        }

        return payload;
    }

    private async Task<VideoResult?> WaitUntilCompletedAsync(string statusUrl, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using HttpResponseMessage status = await _http.GetAsync(statusUrl, cancellationToken);

            if (!status.IsSuccessStatusCode)
            {
                return await BuildFailureAsync(status, "hỏi trạng thái", cancellationToken);
            }

            using JsonDocument document = await ReadJsonAsync(status, cancellationToken);

            string? state = ProviderJson.FindString(document.RootElement, "status");

            if (string.Equals(state, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                string body = document.RootElement.ToString();

                return new VideoResult
                {
                    IsSuccess = false,

                    // Hàng đợi báo hỏng thường là do nội dung bị chặn; phần còn lại là sự cố phía
                    // họ. Đọc thân phản hồi để không xếp nhầm một prompt bị từ chối thành lỗi tạm thời.
                    FailureKind = ProviderFailureMapper.LooksLikeContentPolicy(body)
                        ? VideoFailureKind.ContentRejected
                        : VideoFailureKind.ProviderUnavailable,
                    FailureReason = "fal.ai báo job trong hàng đợi thất bại.",
                    RawError = body,
                };
            }

            // Timeout tổng do HttpClient.Timeout và CancellationToken của bước gọi lo; vòng lặp này
            // không tự đặt trần để khỏi có hai con số hạn định đá nhau.
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task<VideoResult> BuildFailureAsync(
        HttpResponseMessage response,
        string phase,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new VideoResult
        {
            IsSuccess = false,
            FailureKind = ProviderFailureMapper.FromStatus(response.StatusCode, body),
            FailureReason = $"fal.ai trả {(int)response.StatusCode} khi {phase}.",
            RawError = body,
            RetryAfterSeconds = ProviderFailureMapper.ReadRetryAfter(response),
        };
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string ToAspectRatioString(AspectRatio ratio) => ratio switch
    {
        AspectRatio.Portrait9x16 => "9:16",
        AspectRatio.Square1x1 => "1:1",
        AspectRatio.Landscape16x9 => "16:9",
        _ => "9:16",
    };
}
