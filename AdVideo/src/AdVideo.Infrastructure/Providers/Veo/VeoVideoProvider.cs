using System.Net.Http.Json;
using System.Text.Json;
using AdVideo.Core.Configuration;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers.Veo;

/// <summary>
/// Gọi Veo qua Gemini API bằng thao tác chạy dài (<c>predictLongRunning</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Header là <c>x-goog-api-key</c>, không phải <c>Authorization</c>.</b> Gemini API nhận key
/// qua header riêng; gửi Bearer thì nhận 401 kèm thông báo nói về OAuth, dẫn người đọc đi sai hướng.
/// </para>
/// <para>
/// <b>Tải file về NGAY.</b> Google xoá file kết quả sau khoảng hai ngày. Lưu URI vào DB rồi tải
/// sau là một quả bom hẹn giờ: mọi thứ chạy tốt suốt sprint, rồi một sáng thứ Hai tất cả job cũ
/// mất video. Nên provider này trả <c>VideoBytes</c> chứ không trả <c>VideoUri</c>.
/// </para>
/// <para>
/// <b>Veo luôn sinh âm thanh gốc</b> và không có tham số nào tắt được. Yêu cầu tắt tiếng được xử
/// lý ở bước ghép bằng cách bỏ luồng audio (D3), chứ không phải ở đây.
/// </para>
/// </remarks>
public sealed class VeoVideoProvider : IVideoProvider
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly ResolvedCredential _credential;
    private readonly ILogger _logger;

    public VeoVideoProvider(
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

        _http.DefaultRequestHeaders.Remove("x-goog-api-key");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", credential.ApiKey);
    }

    public string Name => _credential.Provider;

    public VideoProviderCapability Capability { get; }

    public async Task<VideoResult> GenerateAsync(
        VideoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string baseUrl = BaseUrl();

        using HttpResponseMessage submit = await _http.PostAsJsonAsync(
            $"{baseUrl}/models/{_credential.ModelId}:predictLongRunning",
            BuildPayload(request),
            cancellationToken);

        if (!submit.IsSuccessStatusCode)
        {
            return await BuildFailureAsync(submit, "gửi yêu cầu", cancellationToken);
        }

        using JsonDocument queued = await ReadJsonAsync(submit, cancellationToken);

        string? operationName = ProviderJson.FindString(queued.RootElement, "name");

        if (string.IsNullOrWhiteSpace(operationName))
        {
            return new VideoResult
            {
                IsSuccess = false,
                FailureKind = VideoFailureKind.Unknown,
                FailureReason = "Gemini nhận yêu cầu nhưng không trả về tên thao tác để theo dõi.",
                RawError = queued.RootElement.ToString(),
            };
        }

        _logger.LogDebug(
            "Veo đã nhận shot {ShotIndex} của job {JobId}, thao tác {Operation}.",
            request.ShotIndex,
            request.JobId,
            operationName);

        (VideoResult? failure, Uri? videoUri) = await WaitForOperationAsync(baseUrl, operationName, cancellationToken);

        if (failure is not null)
        {
            return failure;
        }

        if (videoUri is null)
        {
            return new VideoResult
            {
                IsSuccess = false,
                FailureKind = VideoFailureKind.Unknown,
                FailureReason = "Thao tác Veo báo xong nhưng không có URI video trong kết quả.",
            };
        }

        byte[]? bytes = await DownloadAsync(videoUri, cancellationToken);

        if (bytes is null)
        {
            return new VideoResult
            {
                IsSuccess = false,

                // Tải hỏng là lỗi tạm thời: file vẫn còn ở phía Google trong khoảng hai ngày, nên
                // thử lại có cơ hội thành công thật — khác hẳn với prompt bị từ chối.
                FailureKind = VideoFailureKind.Transient,
                FailureReason = "Không tải được file video từ Gemini.",
            };
        }

        return new VideoResult
        {
            IsSuccess = true,
            ProviderRequestId = operationName,
            VideoBytes = bytes,
            HasNativeAudio = Capability.GeneratesNativeAudio,
            MeasuredDurationSeconds = request.DurationSeconds,
        };
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Đọc metadata model: rẻ, không sinh gì, và vẫn kiểm chứng được key.
            using HttpResponseMessage response = await _http.GetAsync(
                $"{BaseUrl()}/models/{_credential.ModelId}",
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Ping Gemini thất bại cho provider {Provider}.", Name);

            return false;
        }
    }

    private string BaseUrl()
    {
        string endpoint = string.IsNullOrWhiteSpace(_credential.Endpoint)
            ? "https://generativelanguage.googleapis.com/v1beta"
            : _credential.Endpoint.TrimEnd('/');

        return endpoint;
    }

    private object BuildPayload(VideoRequest request)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["aspectRatio"] = ToAspectRatioString(request.AspectRatio),
            ["durationSeconds"] = request.DurationSeconds,
            ["sampleCount"] = 1,
        };

        if (!string.IsNullOrWhiteSpace(request.NegativePrompt))
        {
            parameters["negativePrompt"] = request.NegativePrompt;
        }

        if (request.Seed is { } seed && Capability.SupportsSeed)
        {
            parameters["seed"] = seed;
        }

        // personGeneration phải khai báo tường minh khi cảnh có người: mặc định của API thay đổi
        // theo vùng, và ở một số vùng nó chặn thẳng mọi cảnh có mặt người.
        if (Capability.AcceptsHumanFaces)
        {
            parameters["personGeneration"] = "allow_adult";
        }

        var instance = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["prompt"] = request.Prompt,
        };

        if (request.ReferenceImageUrls.Count > 0 && Capability.SupportsImageToVideo)
        {
            instance["image"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["gcsUri"] = request.ReferenceImageUrls[0],
            };
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["instances"] = new[] { instance },
            ["parameters"] = parameters,
        };
    }

    private async Task<(VideoResult? Failure, Uri? VideoUri)> WaitForOperationAsync(
        string baseUrl,
        string operationName,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(PollInterval, cancellationToken);

            // operationName đã có dạng đường dẫn đầy đủ ("models/.../operations/..."), nên chỉ ghép
            // vào base URL. Tự dựng lại đường dẫn từ id là cách chắc chắn gặp 404.
            using HttpResponseMessage poll = await _http.GetAsync(
                $"{baseUrl}/{operationName.TrimStart('/')}",
                cancellationToken);

            if (!poll.IsSuccessStatusCode)
            {
                return (await BuildFailureAsync(poll, "hỏi trạng thái", cancellationToken), null);
            }

            using JsonDocument document = await ReadJsonAsync(poll, cancellationToken);
            JsonElement root = document.RootElement;

            bool done = root.TryGetProperty("done", out JsonElement doneElement)
                && doneElement.ValueKind == JsonValueKind.True;

            if (!done)
            {
                continue;
            }

            if (root.TryGetProperty("error", out JsonElement error))
            {
                string body = error.ToString();

                return (new VideoResult
                {
                    IsSuccess = false,
                    FailureKind = ProviderFailureMapper.FromFailedJobBody(body),
                    FailureReason = "Thao tác Veo kết thúc với lỗi.",
                    RawError = body,
                }, null);
            }

            // Đường dẫn thật tới URI video nằm sâu trong generateVideoResponse.generatedSamples[].video.uri
            // và đã đổi hình dạng giữa các bản Veo. Dò đệ quy để một lần đổi tên trường không làm
            // hỏng cả provider.
            return (null, ProviderJson.FindVideoUrl(root));
        }
    }

    private async Task<byte[]?> DownloadAsync(Uri videoUri, CancellationToken cancellationToken)
    {
        // Dùng chính _http để header x-goog-api-key đi kèm: URI tải của Gemini vẫn đòi xác thực,
        // và tải bằng client trần sẽ nhận 401 dù URI trông như một link tải công khai.
        using HttpResponseMessage response = await _http.GetAsync(
            videoUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Tải video Veo thất bại với mã {Status} từ {Uri}.",
                (int)response.StatusCode,
                videoUri);

            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
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
            FailureReason = $"Gemini trả {(int)response.StatusCode} khi {phase}.",
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
