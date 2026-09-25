using System.Collections.Concurrent;
using System.Globalization;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdVideo.Infrastructure.Providers.Fake;

/// <summary>
/// Provider video giả: sinh clip thật bằng ffmpeg, không gọi mạng và không tốn tiền.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao sinh video THẬT chứ không trả mấy byte giả:</b> nếu trả dữ liệu giả thì bước 8 (ghép
/// bằng ffmpeg) và bước 9 (QC bằng ffprobe) không có gì để làm, và "xương sống chạy thông" trở
/// thành một câu nói suông. Clip lavfi có đủ codec, độ dài, khung hình và tuỳ chọn có/không audio
/// — đủ để hai bước sau chạy đúng như với clip thật.
/// </para>
/// <para>
/// <b>Đây là provider mặc định của Sprint 1</b> (xem <c>SystemSettingSeeder</c>): chưa có API key
/// và chưa có ngân sách, nên toàn tuyến phải chạy được mà không tiêu một đồng.
/// </para>
/// </remarks>
public sealed class FakeVideoProvider : IVideoProvider
{
    private readonly IFfmpegRunner _runner;
    private readonly FakeProviderOptions _options;
    private readonly ILogger<FakeVideoProvider> _logger;

    // Đếm số lần đã fail cho mỗi shot, để FailTimesBeforeSuccess có nghĩa. Dùng ConcurrentDictionary
    // vì các shot render song song.
    private readonly ConcurrentDictionary<string, int> _failureCounts = new();

    public FakeVideoProvider(
        IFfmpegRunner runner,
        IOptions<FakeProviderOptions> options,
        ILogger<FakeVideoProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => ProviderNames.Fake;

    /// <remarks>
    /// Khai báo <c>GeneratesNativeAudio = true</c> và <c>NativeAudioEnabledByDefault = true</c> có
    /// chủ đích: đó là hình dạng của Vidu, provider hay làm hỏng bản dựng nhất vì âm thanh gốc bật
    /// sẵn. Provider giả mà "sạch" hơn provider thật thì không kiểm tra được quyết định D3.
    /// </remarks>
    public VideoProviderCapability Capability { get; } = new()
    {
        Provider = ProviderNames.Fake,
        ModelId = "fake-v1",
        AllowedDurationSeconds = [4, 5, 6, 8, 10],
        SupportedAspectRatios = [AspectRatio.Portrait9x16, AspectRatio.Square1x1, AspectRatio.Landscape16x9],
        AcceptsHumanFaces = true,
        GeneratesNativeAudio = true,
        NativeAudioEnabledByDefault = true,
        CanSeparateSfxFromSpeech = false,
        SupportsImageToVideo = true,
        SupportsFrameChaining = false,
        MaxReferenceImages = 3,
        MaxSubjectsReliably = 1,
        ServesTiers = [VideoTier.Draft, VideoTier.Standard, VideoTier.Premium],
        CostPerSecondUsd = 0m,
        SupportsSeed = true,
        SupportsLipSync = false,
    };

    public async Task<VideoResult> GenerateAsync(
        VideoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_options.LatencyMs > 0)
        {
            await Task.Delay(_options.LatencyMs, cancellationToken);
        }

        if (ShouldFail(request))
        {
            return BuildFailure(request);
        }

        (int width, int height) = Resolution(request.AspectRatio);

        // Âm thanh gốc chỉ có khi KHÔNG bị yêu cầu tắt — đúng cách provider thật hành xử, và là
        // điều kiện để kiểm tra quyết định D3 ở bước ghép.
        bool withAudio = !request.SuppressNativeAudio;

        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"advideo-fake-{request.JobId:N}-{request.ShotIndex}.mp4");

        List<string> arguments =
        [
            "-hide_banner",
            "-nostats",
            "-y",
            "-f", "lavfi",
            "-i", $"testsrc2=size={width}x{height}:rate=25:duration={request.DurationSeconds}",
        ];

        if (withAudio)
        {
            arguments.AddRange([
                "-f", "lavfi",
                "-i", $"sine=frequency=440:duration={request.DurationSeconds}",
            ]);
        }

        arguments.AddRange([
            "-c:v", "libx264",
            "-preset", "ultrafast",

            // yuv420p là bắt buộc để QuickTime và phần lớn trình duyệt phát được. Bỏ qua thì file
            // vẫn hợp lệ với ffprobe nhưng iPhone hiện màn hình đen.
            "-pix_fmt", "yuv420p",
            "-t", request.DurationSeconds.ToString(CultureInfo.InvariantCulture),
        ]);

        if (withAudio)
        {
            arguments.AddRange(["-c:a", "aac", "-shortest"]);
        }

        arguments.Add(outputPath);

        FfmpegRunResult run = await _runner.RunFfmpegAsync(arguments, cancellationToken);

        if (!run.IsSuccess)
        {
            // Provider giả hỏng là lỗi môi trường (thiếu ffmpeg, thiếu libx264), không phải lỗi
            // provider. Nói thẳng ra để không ai đi tìm nguyên nhân ở phía nhà cung cấp.
            return new VideoResult
            {
                IsSuccess = false,
                FailureKind = VideoFailureKind.ProviderUnavailable,
                FailureReason = "Provider giả không dựng được clip — ffmpeg trên máy này có vấn đề.",
                RawError = run.Tail(),
            };
        }

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);

            _logger.LogDebug(
                "Provider giả đã dựng shot {ShotIndex} của job {JobId}: {Bytes} byte, audio={HasAudio}.",
                request.ShotIndex,
                request.JobId,
                bytes.Length,
                withAudio);

            return new VideoResult
            {
                IsSuccess = true,
                ProviderRequestId = $"fake-{Guid.NewGuid():N}",
                VideoBytes = bytes,
                HasNativeAudio = withAudio,
                MeasuredDurationSeconds = request.DurationSeconds,
                ReportedCostUsd = 0m,
            };
        }
        finally
        {
            DeleteQuietly(outputPath);
        }
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_options.PingSucceeds);

    private bool ShouldFail(VideoRequest request)
    {
        if (!_options.FailShotIndexes.Contains(request.ShotIndex))
        {
            return false;
        }

        if (_options.FailTimesBeforeSuccess <= 0)
        {
            return true;
        }

        string key = $"{request.JobId:N}:{request.ShotIndex}";
        int attempts = _failureCounts.AddOrUpdate(key, 1, (_, current) => current + 1);

        return attempts <= _options.FailTimesBeforeSuccess;
    }

    private VideoResult BuildFailure(VideoRequest request)
    {
        _logger.LogInformation(
            "Provider giả cố ý fail shot {ShotIndex} với kiểu {Kind}.",
            request.ShotIndex,
            _options.FailureKind);

        return new VideoResult
        {
            IsSuccess = false,
            FailureKind = _options.FailureKind,
            FailureReason = $"Provider giả được cấu hình để fail shot {request.ShotIndex}.",

            // RateLimited kèm RetryAfterSeconds vì provider thật luôn gửi kèm header đó, và nhánh
            // chờ-rồi-thử-lại chỉ được kiểm tra nếu giá trị này có mặt.
            RetryAfterSeconds = _options.FailureKind == VideoFailureKind.RateLimited ? 3 : null,
        };
    }

    private static (int Width, int Height) Resolution(AspectRatio ratio) => ratio switch
    {
        AspectRatio.Portrait9x16 => (1080, 1920),
        AspectRatio.Square1x1 => (1080, 1080),
        AspectRatio.Landscape16x9 => (1920, 1080),
        _ => (1080, 1920),
    };

    private void DeleteQuietly(string path)
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
            // File tạm sót lại không đáng làm hỏng một shot đã render xong.
            _logger.LogDebug(ex, "Không xoá được file tạm {Path}.", path);
        }
    }
}
