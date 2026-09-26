using System.Diagnostics;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Worker.Jobs;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Worker.Steps;

/// <summary>
/// Bước 4 — sinh giọng đọc tiếng Việt kèm mốc thời gian.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bước này chạy TRƯỚC khi render hình, và thứ tự đó không đảo được.</b> Độ dài thật của
/// giọng đọc là thứ quyết định mỗi shot dài bao nhiêu giây (bước 5). Render hình trước rồi mới
/// thu tiếng nghĩa là có hình 8 giây và tiếng 9,3 giây, và không có cách sửa nào không xấu:
/// hoặc cắt cụt lời, hoặc tua nhanh giọng đọc, hoặc trả tiền render lại.
/// </para>
/// <para>
/// <b>Không có mốc thời gian thì fail, không phải cảnh báo.</b> Một engine trả audio "thành công"
/// nhưng không kèm mốc từ sẽ làm bước 5 chia shot bằng phép chia đều — nghe sẽ hụt ở mọi chỗ
/// chuyển cảnh mà không ai chỉ được ra vì sao. Thà fail ở đây, nơi lý do còn đọc được.
/// </para>
/// </remarks>
public sealed class TtsStep : IPipelineStep
{
    private readonly AdVideoDbContext _db;
    private readonly IProviderRegistry _registry;
    private readonly JobArtifacts _artifacts;
    private readonly ILogger<TtsStep> _logger;

    public TtsStep(
        AdVideoDbContext db,
        IProviderRegistry registry,
        JobArtifacts artifacts,
        ILogger<TtsStep> logger)
    {
        _db = db;
        _registry = registry;
        _artifacts = artifacts;
        _logger = logger;
    }

    public int Order => 4;

    public JobStatus RunningStatus => JobStatus.SynthesizingVoice;

    public string DisplayName => "Đang thu giọng đọc";

    public bool ShouldRun(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return !string.IsNullOrWhiteSpace(context.NarrationText);
    }

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsCostCeilingHit)
        {
            return StepResult.Fail(
                $"Đã tiêu {context.AccumulatedCostUsd:0.####} USD, chạm trần {context.MaxCostUsd:0.####} USD trước khi thu giọng đọc.");
        }

        AdVideoJob? job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == context.JobId, cancellationToken);

        if (job is null)
        {
            return StepResult.Fail($"Không tìm thấy job {context.JobId} trong DB.");
        }

        ProviderSelectionResult selection = await _registry.SelectTtsProviderAsync(context.Tier, cancellationToken);

        if (!selection.IsSuccess)
        {
            return StepResult.Fail(string.Join(" ", selection.Reasons));
        }

        ITtsProvider? provider = await _registry.FindTtsProviderAsync(selection.ProviderName!, cancellationToken);

        if (provider is null)
        {
            // Chọn được tên nhưng dựng không được: credential vừa bị tắt giữa hai lời gọi.
            return StepResult.Retry($"Engine giọng đọc \"{selection.ProviderName}\" vừa ngừng phục vụ.");
        }

        JobBrief brief = context.Brief();

        var request = new TtsRequest
        {
            JobId = context.JobId,
            Text = context.NarrationText!,

            // Chưa có hồ sơ giọng trong Sprint 1: adapter thật sẽ nhận voice id của nhà cung cấp,
            // còn engine giả bỏ qua giá trị này.
            VoiceId = context.VoiceId ?? "default",
            Speed = (decimal)brief.VoiceSpeed,
            WithTimestamps = true,
        };

        var stopwatch = Stopwatch.StartNew();
        TtsResult result = await provider.SynthesizeAsync(request, cancellationToken);
        stopwatch.Stop();

        // Engine không báo giá thì suy từ số ký tự BỊ TÍNH (header của provider) nếu có — nó có
        // thể khác độ dài văn bản gửi đi — rồi mới tới độ dài văn bản.
        decimal cost = result.ReportedCostUsd
            ?? result.EstimatedCostUsd
            ?? provider.Capability.CostPer1000CharsUsd * (result.BilledCharacterCount ?? request.Text.Length) / 1000m;

        await _artifacts.RecordCallAsync(
            context,
            job,
            provider.Name,
            provider.Capability.ModelId,
            ProviderCategory.TextToSpeech,
            result.IsSuccess,
            cost,
            stopwatch.ElapsedMilliseconds,
            cancellationToken,
            providerRequestId: result.ProviderRequestId,
            failureKind: result.FailureKind,
            rawError: result.RawError,
            costIsReported: result.ReportedCostUsd is not null,
            billedCharacters: result.BilledCharacterCount ?? request.Text.Length,
            descriptorSha256: result.DescriptorSha256);

        if (!result.IsSuccess || result.AudioBytes is null || result.AudioBytes.Length == 0)
        {
            string reason = result.FailureReason ?? "Engine giọng đọc không trả về audio.";

            return result.FailureKind is VideoFailureKind.Transient or VideoFailureKind.RateLimited
                ? StepResult.Retry(reason, result.RawError)
                : StepResult.Fail(reason, result.RawError);
        }

        if (!result.CanLockTimeline)
        {
            return StepResult.Fail(
                $"Engine \"{provider.Name}\" trả audio nhưng không kèm mốc thời gian theo từ. " +
                "Không có mốc thì không khoá được timeline, và video sẽ lệch tiếng-hình ở mọi chỗ chuyển cảnh.",
                result.RawError);
        }

        if (result.AudioDurationSeconds <= 0)
        {
            return StepResult.Fail(
                $"Engine \"{provider.Name}\" không đo được độ dài audio. Độ dài này là đầu vào bắt buộc của bước khoá timeline.");
        }

        using var audio = new MemoryStream(result.AudioBytes, writable: false);

        MediaAsset asset = await _artifacts.SaveAsync(
            context,
            AssetKind.VoiceAudio,
            $"voice{ExtensionFor(result.AudioContentType)}",
            audio,
            result.AudioContentType,
            cancellationToken,
            durationSeconds: result.AudioDurationSeconds,
            hasAudio: true);

        context.TtsProviderName = provider.Name;
        context.VoiceAudioKey = asset.ObjectKey;
        context.VoiceDurationSeconds = result.AudioDurationSeconds;
        context.TtsRequestId = result.ProviderRequestId;
        context.WordTimings.Clear();
        context.WordTimings.AddRange(result.WordTimings);

        job.VoiceAudioAssetId = asset.Id;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "job_id={JobId} thu xong giọng đọc bằng {Provider}: {Duration:0.##}s, {Words} mốc từ.",
            context.JobId,
            provider.Name,
            result.AudioDurationSeconds,
            result.WordTimings.Count);

        return StepResult.Ok();
    }

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "audio/wav" or "audio/x-wav" => ".wav",
        "audio/ogg" => ".ogg",
        "audio/aac" => ".aac",
        _ => ".mp3",
    };
}
