using System.Text;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Media;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;
using AdVideo.Core.Storage;
using AdVideo.Core.Timeline;
using AdVideo.Infrastructure.Media;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Worker.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AdVideo.Worker.Steps;

/// <summary>
/// Bước 8 — ghép các clip đã dựng, voice-over và nhãn AI thành một file MP4.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bước này kéo mọi thứ về đĩa cục bộ.</b> FFmpeg đọc file, không đọc object key. Thư mục tạm
/// được xoá trong <c>finally</c> kể cả khi ghép thất bại: một job hỏng để lại vài trăm MB trên đĩa
/// worker, và vài chục job như thế là hết đĩa — một kiểu hỏng không liên quan gì tới video.
/// </para>
/// <para>
/// <b>Nhãn AI dựng ở đây và đi thẳng sang bước 9.</b> QC không tự dựng lại đặc tả nhãn: nếu hai
/// bước tự tính riêng thì QC đang chấm một thứ khác với thứ đã vẽ lên video.
/// </para>
/// <para>
/// <b>Tiếng động gốc được TÁCH RA thành file riêng ở đây</b>, chứ không để FFmpeg lấy từ clip.
/// Đồ thị lọc ở <see cref="FfmpegCommandBuilder"/> nối hình bằng <c>concat</c> với <c>a=0</c> —
/// tức là track tiếng trong clip bị bỏ qua hoàn toàn. Muốn giữ tiếng động thì phải trích nó ra
/// và đưa vào như một đầu vào riêng.
/// </para>
/// </remarks>
public sealed class ComposeStep : IPipelineStep
{
    private readonly AdVideoDbContext _db;
    private readonly IVideoComposer _composer;
    private readonly IFfmpegRunner _ffmpeg;
    private readonly ISettingsStore _settings;
    private readonly FfmpegOptions _options;
    private readonly JobArtifacts _artifacts;
    private readonly ILogger<ComposeStep> _logger;

    public ComposeStep(
        AdVideoDbContext db,
        IVideoComposer composer,
        IFfmpegRunner ffmpeg,
        ISettingsStore settings,
        IOptions<FfmpegOptions> options,
        JobArtifacts artifacts,
        ILogger<ComposeStep> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _composer = composer;
        _ffmpeg = ffmpeg;
        _settings = settings;
        _options = options.Value;
        _artifacts = artifacts;
        _logger = logger;
    }

    public int Order => 8;

    public JobStatus RunningStatus => JobStatus.Composing;

    public string DisplayName => "Đang ghép video";

    public bool ShouldRun(PipelineContext context) => true;

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        AdVideoJob? job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == context.JobId, cancellationToken);

        if (job is null)
        {
            return StepResult.Fail($"Không tìm thấy job {context.JobId} trong DB.");
        }

        if (context.Timeline is not { } timeline || timeline.Shots.Count == 0)
        {
            return StepResult.Fail("Chưa có timeline đã khoá — bước 5 phải chạy trước bước ghép.");
        }

        if (context.VoiceAudioKey is not { } voiceKey)
        {
            return StepResult.Fail("Chưa có file giọng đọc — bước 4 phải chạy trước bước ghép.");
        }

        if (context.NativeSound() is not { } nativeSound)
        {
            return StepResult.Fail("Chưa có quyết định về tiếng gốc — bước 6 phải chạy trước bước ghép.");
        }

        if (string.IsNullOrWhiteSpace(_options.FontFile))
        {
            // Chặn trước khi tải vài trăm MB về đĩa: thiếu font thì FFmpeg vẫn chạy, vẫn xuất ra
            // video, chỉ là chữ tiếng Việt hiện thành ô vuông và không có dòng lỗi nào.
            return StepResult.Fail(
                $"Chưa cấu hình {FfmpegOptions.SectionName}:FontFile. Không có font có dấu thì nhãn AI " +
                "hiện thành ô vuông, mà FFmpeg không báo lỗi gì cả.");
        }

        string workDir = Path.Combine(Path.GetTempPath(), $"advideo-job-{context.JobId:N}");

        try
        {
            Directory.CreateDirectory(workDir);

            string voicePath = Path.Combine(workDir, $"voice{Path.GetExtension(voiceKey)}");
            await _artifacts.DownloadToFileAsync(Buckets.Voice, voiceKey, voicePath, cancellationToken);

            IReadOnlyDictionary<string, bool> clipHasAudio = await LoadClipAudioFlagsAsync(context, cancellationToken);

            var shots = new List<ComposeShot>(timeline.Shots.Count);

            foreach (ShotPlan plan in timeline.Shots)
            {
                if (!context.ShotClipKeys.TryGetValue(plan.Index, out string? clipKey))
                {
                    // Luật 1: không ghép video thiếu shot.
                    return StepResult.Fail($"Thiếu clip của shot {plan.Index + 1}/{timeline.Shots.Count}.");
                }

                string clipPath = Path.Combine(workDir, $"shot-{plan.Index:00}.mp4");
                await _artifacts.DownloadToFileAsync(Buckets.Work, clipKey, clipPath, cancellationToken);

                string? nativeAudioPath = null;

                if (nativeSound.KeepSfxAtDb is not null
                    && clipHasAudio.TryGetValue(clipKey, out bool hasAudio)
                    && hasAudio)
                {
                    nativeAudioPath = await ExtractNativeAudioAsync(workDir, plan.Index, clipPath, cancellationToken);
                }

                shots.Add(new ComposeShot(plan, clipPath, nativeAudioPath));
            }

            AiLabelSpec label = AiLabelStamper.Build(context.AspectRatio, timeline.TotalVideoSeconds);

            string labelPath = Path.Combine(workDir, "label.txt");

            // UTF-8 KHÔNG BOM: drawtext đọc BOM như một ký tự thật và vẽ ra ô vuông ở đầu dòng.
            // FfmpegComposer ghi lại file này trong thư mục tạm của nó, nhưng yêu cầu vẫn phải
            // mang một đường dẫn dùng được — người gọi FfmpegCommandBuilder trực tiếp cần nó.
            await File.WriteAllTextAsync(
                labelPath, label.OverlayText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

            decimal targetLufs = await _settings.GetDecimalAsync(SettingKeys.TargetLoudnessLufs, -14m, cancellationToken);
            string outputPath = Path.Combine(workDir, "final.mp4");

            var request = new ComposeRequest
            {
                JobId = context.JobId,
                Shots = shots,
                VoiceAudioPath = voicePath,
                AspectRatio = context.AspectRatio,
                Label = label,
                LabelTextFilePath = labelPath,
                FontFile = _options.FontFile!,
                OutputPath = outputPath,

                // Null nghĩa là bỏ hết track gốc. Lấy từ quyết định đã chốt ở bước 6 chứ không
                // tính lại: bước 6 đã gửi tham số đi theo quyết định đó.
                SfxLevelDb = nativeSound.KeepSfxAtDb,
                TargetLoudnessLufs = (double)targetLufs,
            };

            // Sprint 1 chưa vẽ phụ đề: chỉ có nhãn AI, và nhãn đi trong chính ComposeRequest.
            ComposeResult result = await _composer.ComposeAsync(request, [], cancellationToken);

            if (!result.IsSuccess)
            {
                return StepResult.Fail(
                    result.FailureReason ?? "Không ghép được video.", result.RawError);
            }

            MediaAsset asset = await UploadFinalAsync(context, outputPath, timeline, cancellationToken);

            job.FinalVideoAssetId = asset.Id;
            await _db.SaveChangesAsync(cancellationToken);

            context.FinalVideoKey = asset.ObjectKey;
            context.FinalDurationSeconds = timeline.TotalVideoSeconds;
            context.SetAiLabel(label);

            _logger.LogInformation(
                "job_id={JobId} ghép xong {Shots} shot thành {Key} ({Bytes} bytes, {Duration:0.##}s).",
                context.JobId,
                shots.Count,
                asset.ObjectKey,
                asset.SizeBytes,
                timeline.TotalVideoSeconds);

            return StepResult.Ok();
        }
        catch (IOException ex)
        {
            // Hết đĩa giữa chừng là lỗi hạ tầng, không phải lỗi của brief — chạy lại trên một
            // worker khác có thể thành công.
            return StepResult.Retry($"Lỗi đĩa khi ghép video: {ex.Message}", ex.ToString());
        }
        finally
        {
            DeleteQuietly(workDir);
        }
    }

    /// <summary>Clip của shot nào thật sự có track tiếng, theo cờ đã đo ở bước 6.</summary>
    private async Task<IReadOnlyDictionary<string, bool>> LoadClipAudioFlagsAsync(
        PipelineContext context, CancellationToken cancellationToken)
    {
        List<MediaAsset> assets = await _db.MediaAssets
            .Where(a => a.JobId == context.JobId && a.Kind == AssetKind.ShotClip)
            .ToListAsync(cancellationToken);

        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (MediaAsset asset in assets)
        {
            flags[asset.ObjectKey] = asset.HasAudio;
        }

        return flags;
    }

    /// <summary>
    /// Trích track tiếng của một clip ra file riêng.
    /// </summary>
    /// <remarks>
    /// Thất bại thì bỏ tiếng động chứ không fail job: mất tiếng động nền là một video kém hay
    /// hơn, còn fail job là khách không có video nào. Nhưng phải ghi log — im lặng bỏ qua thì
    /// không ai biết tiếng động đã biến mất.
    /// </remarks>
    private async Task<string?> ExtractNativeAudioAsync(
        string workDir, int shotIndex, string clipPath, CancellationToken cancellationToken)
    {
        string outputPath = Path.Combine(workDir, $"shot-{shotIndex:00}-sfx.wav");

        FfmpegRunResult run = await _ffmpeg.RunFfmpegAsync(
            [
                "-hide_banner", "-nostats", "-y",
                "-i", clipPath,
                "-vn",
                "-acodec", "pcm_s16le",
                "-ar", "48000",
                outputPath,
            ],
            cancellationToken);

        if (run.IsSuccess && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
        {
            return outputPath;
        }

        _logger.LogWarning(
            "Không trích được tiếng gốc của shot {Shot}, ghép không có tiếng động: {Error}",
            shotIndex,
            run.Tail());

        return null;
    }

    private async Task<MediaAsset> UploadFinalAsync(
        PipelineContext context, string outputPath, TimelinePlan timeline, CancellationToken cancellationToken)
    {
        // Stream thẳng từ đĩa chứ không đọc hết vào bộ nhớ: một video 30 giây đã vài chục MB, và
        // worker chạy nhiều job song song.
        await using FileStream file = File.OpenRead(outputPath);

        return await _artifacts.SaveAsync(
            context,
            AssetKind.FinalVideo,
            "final.mp4",
            file,
            "video/mp4",
            cancellationToken,
            durationSeconds: timeline.TotalVideoSeconds,
            hasAudio: true);
    }

    private void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Không xoá được thư mục tạm {Directory}.", directory);
        }
    }
}
