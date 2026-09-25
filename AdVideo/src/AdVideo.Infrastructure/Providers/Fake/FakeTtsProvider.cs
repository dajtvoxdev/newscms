using System.Globalization;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdVideo.Infrastructure.Providers.Fake;

/// <summary>
/// Engine TTS giả: sinh file audio thật bằng ffmpeg và mốc thời gian theo từ được tính ra.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mốc thời gian là thứ quan trọng nhất ở đây, không phải tiếng động.</b> Bước 5 khoá timeline
/// dựa hoàn toàn vào mốc theo từ; không có mốc thì bước 6 không biết mỗi shot dài bao nhiêu. Nên
/// provider giả phải trả mốc có hình dạng đúng — tăng dần, không chồng lấn, tổng khớp với độ dài
/// file audio — dù âm thanh chỉ là một nốt sine.
/// </para>
/// <para>
/// <b>4,5 âm tiết mỗi giây</b> là tốc độ đọc quảng cáo tiếng Việt thường gặp. Đây là con số ước
/// lượng để pipeline chạy được, KHÔNG phải số đo — khi nối engine thật thì mốc đến từ engine.
/// </para>
/// </remarks>
public sealed class FakeTtsProvider : ITtsProvider
{
    private const double SyllablesPerSecond = 4.5;
    private const double MinimumDurationSeconds = 0.8;

    /// <summary>Khoảng nghỉ thêm sau dấu câu, tính theo tỉ lệ của một âm tiết.</summary>
    private const double PunctuationPauseWeight = 0.35;

    private readonly IFfmpegRunner _runner;
    private readonly FakeProviderOptions _options;
    private readonly ILogger<FakeTtsProvider> _logger;

    public FakeTtsProvider(
        IFfmpegRunner runner,
        IOptions<FakeProviderOptions> options,
        ILogger<FakeTtsProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _runner = runner;
        _options = options.Value;
        _logger = logger;

        Capability = new TtsProviderCapability
        {
            Provider = ProviderNames.Fake,
            ModelId = "fake-tts-v1",

            // Phản ánh đúng cờ cấu hình: bật TtsWithoutTimings là giả lập một engine không có mốc
            // thời gian, và khi đó bộ chọn provider phải tự loại nó khỏi tier Thành phẩm.
            HasWordTimings = !_options.TtsWithoutTimings,
            HasCharacterTimings = false,
            SupportedLanguages = ["vi"],
            SupportsVoiceCloning = false,
            SupportsProsodyContinuation = false,
            CostPer1000CharsUsd = 0m,
        };
    }

    public string Name => ProviderNames.Fake;

    public TtsProviderCapability Capability { get; }

    public async Task<TtsResult> SynthesizeAsync(
        TtsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_options.LatencyMs > 0)
        {
            await Task.Delay(_options.LatencyMs, cancellationToken);
        }

        string[] tokens = request.Text.Split(
            [' ', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return new TtsResult
            {
                IsSuccess = false,
                FailureKind = VideoFailureKind.ContentRejected,
                FailureReason = "Không có chữ nào để đọc.",
            };
        }

        double[] weights = tokens.Select(Weight).ToArray();
        double totalWeight = weights.Sum();

        // Speed là số chia: đọc nhanh gấp đôi thì thời lượng còn một nửa.
        double speed = request.Speed <= 0 ? 1.0 : (double)request.Speed;
        double duration = Math.Max(MinimumDurationSeconds, totalWeight / SyllablesPerSecond / speed);

        string outputPath = Path.Combine(Path.GetTempPath(), $"advideo-fake-tts-{request.JobId:N}-{Guid.NewGuid():N}.wav");

        FfmpegRunResult run = await _runner.RunFfmpegAsync(
            [
                "-hide_banner",
                "-nostats",
                "-y",
                "-f", "lavfi",
                "-i", $"sine=frequency=220:duration={duration.ToString("0.###", CultureInfo.InvariantCulture)}",

                // WAV PCM chứ không MP3: không phụ thuộc libmp3lame có được biên dịch vào ffmpeg
                // trên máy đó hay không, và bước ghép dù sao cũng mã hoá lại.
                "-c:a", "pcm_s16le",
                "-ar", "44100",
                outputPath,
            ],
            cancellationToken);

        if (!run.IsSuccess)
        {
            return new TtsResult
            {
                IsSuccess = false,
                FailureKind = VideoFailureKind.ProviderUnavailable,
                FailureReason = "Engine TTS giả không dựng được audio — ffmpeg trên máy này có vấn đề.",
                RawError = run.Tail(),
            };
        }

        try
        {
            byte[] audio = await File.ReadAllBytesAsync(outputPath, cancellationToken);

            IReadOnlyList<WordTiming> timings = _options.TtsWithoutTimings
                ? []
                : BuildTimings(tokens, weights, totalWeight, duration);

            _logger.LogDebug(
                "Engine TTS giả đã dựng {Seconds:0.##} giây audio cho {Words} từ của job {JobId}.",
                duration,
                tokens.Length,
                request.JobId);

            return new TtsResult
            {
                IsSuccess = true,
                AudioBytes = audio,
                AudioContentType = "audio/wav",
                AudioDurationSeconds = duration,
                WordTimings = timings,
                ProviderRequestId = $"fake-tts-{Guid.NewGuid():N}",
                ReportedCostUsd = 0m,
                BilledCharacterCount = request.Text.Length,
            };
        }
        finally
        {
            DeleteQuietly(outputPath);
        }
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_options.PingSucceeds);

    /// <remarks>
    /// Mốc được chia theo TRỌNG SỐ chứ không chia đều: từ có dấu câu ở cuối được thêm khoảng nghỉ.
    /// Chia đều thì mọi từ dài bằng nhau, và bước 5 sẽ cắt shot ở những chỗ mà giọng đọc thật
    /// không bao giờ ngắt.
    /// </remarks>
    private static IReadOnlyList<WordTiming> BuildTimings(
        string[] tokens,
        double[] weights,
        double totalWeight,
        double duration)
    {
        var timings = new List<WordTiming>(tokens.Length);
        double cursor = 0;

        for (int i = 0; i < tokens.Length; i++)
        {
            double slice = duration * (weights[i] / totalWeight);

            // Chốt mốc cuối bằng đúng độ dài file: cộng dồn số thực sẽ lệch vài phần nghìn giây,
            // và một mốc vượt quá độ dài audio làm bước khoá timeline tính ra shot dài hơn tiếng.
            double end = i == tokens.Length - 1 ? duration : cursor + slice;

            timings.Add(new WordTiming(tokens[i], cursor, end));

            cursor = end;
        }

        return timings;
    }

    private static double Weight(string token)
    {
        bool endsWithPunctuation = token.Length > 0 && char.IsPunctuation(token[^1]);

        return 1 + (endsWithPunctuation ? PunctuationPauseWeight : 0);
    }

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
            _logger.LogDebug(ex, "Không xoá được file tạm {Path}.", path);
        }
    }
}
