using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;
using AdVideo.Core.Timeline;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Worker.Jobs;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Worker.Steps;

/// <summary>
/// Bước 5 — chia lời thoại thành shot và gán thời lượng theo lưới của provider.
/// </summary>
/// <remarks>
/// <para>
/// <b>Đây là chỗ lệch tiếng-hình sinh ra.</b> Mọi thứ khác hỏng thì nhìn thấy ngay; cái này hỏng
/// thì video "cảm giác sai sai" mà không ai chỉ ra được vì sao. Phần tính toán nằm trọn trong
/// <see cref="TimelineLocker"/> (thuần hàm, test được 100%); bước này chỉ lo lấy đầu vào đúng và
/// ghi kết quả xuống DB.
/// </para>
/// <para>
/// <b>Timeline khoá xong là bất biến.</b> Bước 6 đọc bảng <see cref="Shot"/> và render đúng số
/// giây đã ghi. Sửa thời lượng một shot sau khi khoá là làm lệch mọi shot đứng sau nó.
/// </para>
/// </remarks>
public sealed class LockTimelineStep : IPipelineStep
{
    private readonly AdVideoDbContext _db;
    private readonly IProviderRegistry _registry;
    private readonly ISettingsStore _settings;
    private readonly ILogger<LockTimelineStep> _logger;

    public LockTimelineStep(
        AdVideoDbContext db,
        IProviderRegistry registry,
        ISettingsStore settings,
        ILogger<LockTimelineStep> logger)
    {
        _db = db;
        _registry = registry;
        _settings = settings;
        _logger = logger;
    }

    public int Order => 5;

    public JobStatus RunningStatus => JobStatus.LockingTimeline;

    public string DisplayName => "Đang khoá timeline";

    public bool ShouldRun(PipelineContext context) => true;

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ProviderName is null)
        {
            return StepResult.Fail("Job chưa có provider — không biết lưới thời lượng nào để chia shot.");
        }

        IVideoProvider? provider = await _registry.FindVideoProviderAsync(context.ProviderName, cancellationToken);

        if (provider is null)
        {
            // Luật 1: không đổi provider giữa chừng. Provider của job tắt thì job dừng.
            return StepResult.Fail(
                $"Provider \"{context.ProviderName}\" của job này không còn phục vụ. " +
                "Hệ thống không đổi provider giữa chừng vì mỗi model cho ra một tông hình khác nhau — " +
                "hãy tạo job mới.");
        }

        int maxDriftMs = await _settings.GetIntAsync(SettingKeys.MaxLipSyncDriftMs, 200, cancellationToken);

        TimelinePlan plan = TimelineLocker.Lock(new TimelineLockRequest
        {
            NarrationDurationSeconds = context.VoiceDurationSeconds,
            WordTimings = context.WordTimings,
            DurationGrid = provider.Capability.AllowedDurationSeconds,
            MaxLipSyncOffsetSeconds = maxDriftMs / 1000.0,
        });

        if (!plan.IsAcceptable)
        {
            return StepResult.Fail(
                "Không chia được lời thoại thành các shot hợp lệ: " + string.Join(" ", plan.Reasons));
        }

        foreach (string warning in plan.Warnings)
        {
            _logger.LogWarning("job_id={JobId} timeline: {Warning}", context.JobId, warning);
        }

        if (plan.HasMidSentenceCut)
        {
            // Không chặn: video vẫn dùng được. Nhưng phải thấy được, vì chỗ cắt giữa câu là thứ
            // người xem nghe thấy "hụt" mà không gọi tên được.
            _logger.LogWarning(
                "job_id={JobId} có shot phải cắt ngoài chỗ im lặng — lời thoại sẽ nghe hụt ở chỗ chuyển cảnh.",
                context.JobId);
        }

        List<Shot> stale = await _db.Shots
            .Where(s => s.JobId == context.JobId)
            .ToListAsync(cancellationToken);

        if (stale.Count > 0)
        {
            // Chỉ xảy ra khi ai đó đẩy lại một job cũ vào hàng đợi bằng tay. Giữ lại shot của lần
            // trước rồi ghi đè từng dòng sẽ để lại shot thừa nếu lần này chia được ít shot hơn.
            _logger.LogWarning(
                "job_id={JobId} đã có {Count} shot từ lần chạy trước; xoá để khoá lại timeline.",
                context.JobId,
                stale.Count);

            _db.Shots.RemoveRange(stale);
        }

        JobBrief brief = context.Brief();

        foreach (ShotPlan shotPlan in plan.Shots)
        {
            _db.Shots.Add(new Shot
            {
                JobId = context.JobId,
                Index = shotPlan.Index,
                Status = ShotStatus.Pending,

                // Sprint 1 chưa có LLM đạo diễn nên mọi shot dùng chung mô tả của khách. Sprint 2
                // sẽ thay chỗ này bằng storyboard — đó là lý do prompt nằm trên từng dòng Shot
                // ngay từ bây giờ chứ không phải đọc lại từ brief lúc render.
                VisualPrompt = brief.Prompt,
                SpokenText = shotPlan.SpokenText,
                NarrationStartSeconds = shotPlan.NarrationStartSeconds,
                NarrationEndSeconds = shotPlan.NarrationEndSeconds,
                VideoDurationSeconds = shotPlan.VideoDurationSeconds,
                VideoStartSeconds = shotPlan.VideoStartSeconds,
                AudioStartInTimelineSeconds = shotPlan.AudioStartInTimelineSeconds,
                Padding = ToPaddingValue(shotPlan.Padding),
                TtsRequestId = context.TtsRequestId,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        context.Timeline = plan;

        _logger.LogInformation(
            "job_id={JobId} khoá timeline: {Shots} shot, {Video:0.##}s hình cho {Narration:0.##}s tiếng, lệch tối đa {Drift:0.###}s.",
            context.JobId,
            plan.Shots.Count,
            plan.TotalVideoSeconds,
            plan.TotalNarrationSeconds,
            plan.MaxLipSyncOffsetSeconds);

        return StepResult.Ok();
    }

    /// <summary>
    /// Ánh xạ enum tính toán sang enum lưu trữ.
    /// </summary>
    /// <remarks>
    /// Hai enum tách rời có chủ đích (xem <see cref="PaddingStrategyValue"/>): dùng chung một
    /// enum thì một thay đổi ở tầng tính toán sẽ âm thầm đổi ý nghĩa của dữ liệu đã lưu.
    /// </remarks>
    private static PaddingStrategyValue ToPaddingValue(PaddingStrategy strategy) => strategy switch
    {
        PaddingStrategy.HoldLastFrame => PaddingStrategyValue.HoldLastFrame,
        PaddingStrategy.KenBurns => PaddingStrategyValue.KenBurns,
        _ => PaddingStrategyValue.None,
    };
}
