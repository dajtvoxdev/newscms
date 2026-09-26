using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Security;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Persistence.Tenancy;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Worker.Jobs;

/// <summary>
/// Chạy toàn bộ pipeline cho một job, tuần tự theo <see cref="IPipelineStep.Order"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="AutomaticRetryAttribute"/> với 0 lần thử là cố ý, và là quyết định quan trọng
/// nhất trong lớp này.</b> Hangfire mặc định chạy lại một job thất bại 10 lần. Ở đây một lần chạy
/// lại nghĩa là gọi lại provider video và trả tiền lại — với một <see cref="PipelineContext"/>
/// rỗng, vì ngữ cảnh chỉ nằm trong bộ nhớ. Job hỏng là trạng thái cuối: khách tạo job mới, và
/// mỗi đồng tiêu ra đều ứng với một lần khách bấm nút.
/// </para>
/// <para>
/// <b>Thử lại theo TỪNG BƯỚC thì có</b>, ngay trong tiến trình này, khi bước trả
/// <see cref="StepResult.Retry"/>. Ngữ cảnh còn nguyên nên bước 6 biết shot nào đã dựng xong và
/// không dựng lại thứ đã trả tiền.
/// </para>
/// <para>
/// <b>Mọi dòng log đều mang <c>job_id</c>.</b> Worker chạy nhiều job xen kẽ nhau; một dòng log
/// không có job_id là một dòng không ghép được vào câu chuyện nào.
/// </para>
/// </remarks>
[AutomaticRetry(Attempts = 0)]
public sealed class AdVideoJobRunner : IAdVideoJobRunner
{
    /// <summary>Số lần chạy lại một BƯỚC khi nó báo lỗi tạm thời.</summary>
    private const int MaxStepRetries = 2;

    private readonly AdVideoDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISettingsStore _settings;
    private readonly IReadOnlyList<IPipelineStep> _steps;
    private readonly ILogger<AdVideoJobRunner> _logger;

    public AdVideoJobRunner(
        AdVideoDbContext db,
        ITenantContext tenant,
        ISettingsStore settings,
        IEnumerable<IPipelineStep> steps,
        ILogger<AdVideoJobRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(steps);

        _db = db;
        _tenant = tenant;
        _settings = settings;

        // Sắp theo Order chứ không theo thứ tự đăng ký DI: thứ tự 4 → 5 → 6 là một ràng buộc
        // nghiệp vụ (có tiếng trước, khoá timeline theo tiếng, rồi mới dựng hình theo timeline),
        // và để nó phụ thuộc vào thứ tự gõ trong Program.cs là để nó hỏng vì một lần sắp xếp lại
        // vô hại về hình thức.
        _steps = steps.OrderBy(s => s.Order).ToList();
        _logger = logger;
    }

    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // Worker chạy ngoài mọi request nên chưa có tenant nào được ghim. Mở filter để đọc được
        // job, rồi ghim NGAY tenant của job — từ đó trở đi global filter lại bảo vệ như thường.
        _tenant.BypassFilters = true;

        AdVideoJob? job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            // Không ném: job bị xoá là chuyện của người vận hành, và một exception ở đây chỉ làm
            // bảng lỗi của Hangfire đầy lên vì một thứ không ai sửa được.
            _logger.LogWarning("job_id={JobId} không còn trong DB; bỏ qua.", jobId);

            return;
        }

        if (IsTerminal(job.Status))
        {
            // Chạy lại một job đã xong là tiêu tiền lần thứ hai cho cùng một video.
            _logger.LogWarning(
                "job_id={JobId} đang ở trạng thái cuối {Status}; không chạy lại.", jobId, job.Status);

            return;
        }

        _tenant.SetTenant(job.TenantId);
        _tenant.BypassFilters = false;

        PipelineContext context = await BuildContextAsync(job, cancellationToken);

        job.StartedAt ??= DateTime.UtcNow;

        _logger.LogInformation(
            "job_id={JobId} bắt đầu: tenant {TenantId}, tier {Tier}, provider {Provider}, trần {MaxCost} USD.",
            jobId,
            job.TenantId,
            job.Tier,
            job.Provider ?? "chưa chọn",
            context.MaxCostUsd);

        try
        {
            foreach (IPipelineStep step in _steps)
            {
                if (!step.ShouldRun(context))
                {
                    _logger.LogInformation(
                        "job_id={JobId} bỏ qua bước {Order} ({Name}).", jobId, step.Order, step.DisplayName);

                    continue;
                }

                if (await IsCancelledAsync(job, cancellationToken))
                {
                    _logger.LogInformation("job_id={JobId} bị huỷ giữa chừng; dừng trước bước {Order}.", jobId, step.Order);

                    return;
                }

                job.Status = step.RunningStatus;
                job.CurrentStep = step.Order;
                await _db.SaveChangesAsync(cancellationToken);

                StepResult result = await RunStepAsync(step, context, jobId, cancellationToken);

                if (!result.IsSuccess)
                {
                    await FailAsync(job, result.FailureReason, result.RawError, step, cancellationToken);

                    return;
                }

                _logger.LogInformation(
                    "job_id={JobId} xong bước {Order} ({Name}).", jobId, step.Order, step.DisplayName);
            }

            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.FailureReason = null;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "job_id={JobId} hoàn thành: {Duration:0.##}s video, chi phí thật {Cost} USD (dự toán {Estimate} USD).",
                jobId,
                context.FinalDurationSeconds,
                job.ActualCostUsd,
                job.EstimatedCostUsd);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Worker đang tắt. Trả job về hàng đợi và để Hangfire giao lại — các bước đã xong
            // được nhận ra qua DB nên lần chạy sau không dựng lại thứ đã trả tiền.
            job.Status = JobStatus.Queued;
            await _db.SaveChangesAsync(CancellationToken.None);

            _logger.LogWarning("job_id={JobId} dừng vì worker đang tắt; trả lại hàng đợi.", jobId);

            throw;
        }
        catch (Exception ex)
        {
            // Lỗi hạ tầng (mất DB, mất kho file). Ghi lại trạng thái rồi ném tiếp để job hiện
            // trong bảng lỗi của Hangfire — nhưng KHÔNG chạy lại tự động.
            _logger.LogError(ex, "job_id={JobId} lỗi không lường trước ở bước {Step}.", jobId, job.CurrentStep);

            await FailAsync(
                job,
                "Lỗi hệ thống khi xử lý job. Đội vận hành đã được ghi nhận.",
                ex.ToString(),
                step: null,
                CancellationToken.None);

            throw;
        }
    }

    /// <summary>
    /// Chạy một bước, thử lại khi bước báo lỗi tạm thời.
    /// </summary>
    /// <remarks>
    /// Chờ tăng dần giữa các lần: lỗi tạm thời hay đi theo cụm (provider đang quá tải, mạng đang
    /// chập), và thử lại ngay lập tức chỉ làm cụm đó dài thêm.
    /// </remarks>
    private async Task<StepResult> RunStepAsync(
        IPipelineStep step, PipelineContext context, Guid jobId, CancellationToken cancellationToken)
    {
        StepResult result = await step.ExecuteAsync(context, cancellationToken);

        for (int attempt = 1; attempt <= MaxStepRetries && !result.IsSuccess && result.IsRetryable; attempt++)
        {
            TimeSpan delay = TimeSpan.FromSeconds(5 * attempt);

            _logger.LogWarning(
                "job_id={JobId} bước {Order} ({Name}) lỗi tạm thời lần {Attempt}: {Reason}. Chờ {Delay}s rồi thử lại.",
                jobId,
                step.Order,
                step.DisplayName,
                attempt,
                result.FailureReason,
                delay.TotalSeconds);

            await Task.Delay(delay, cancellationToken);

            result = await step.ExecuteAsync(context, cancellationToken);
        }

        return result;
    }

    private async Task<PipelineContext> BuildContextAsync(AdVideoJob job, CancellationToken cancellationToken)
    {
        // Trần của job được chốt lúc tạo job; thiếu thì lấy trần hệ thống. Không để null: một job
        // không có trần là một job có thể tiêu tới khi provider ngừng nhận tiền.
        decimal maxCost = job.MaxCostUsd
            ?? await _settings.GetDecimalAsync(SettingKeys.MaxCostPerJobUsd, 5m, cancellationToken);

        return new PipelineContext
        {
            JobId = job.Id,
            TenantId = job.TenantId,
            ProviderName = job.Provider,
            ProviderModelId = job.ProviderModelId,
            Tier = job.Tier,
            AspectRatio = job.AspectRatio,
            TargetDurationSeconds = job.TargetDurationSeconds,
            HasPerson = job.HasPerson,
            VoiceProfileId = job.VoiceProfileId,

            // Bắt đầu từ số đã tiêu chứ không từ 0: job được đẩy lại vào hàng đợi bằng tay vẫn
            // phải chạm cùng một cái trần.
            AccumulatedCostUsd = job.ActualCostUsd,
            MaxCostUsd = maxCost,
        };
    }

    /// <summary>Khách có huỷ job trong lúc bước trước đang chạy không.</summary>
    private async Task<bool> IsCancelledAsync(AdVideoJob job, CancellationToken cancellationToken)
    {
        // Đọc lại từ DB: endpoint huỷ ghi vào một scope khác, nên bản đang theo dõi ở đây không
        // tự biết.
        JobStatus current = await _db.Jobs
            .Where(j => j.Id == job.Id)
            .Select(j => j.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return current == JobStatus.Cancelled;
    }

    private async Task FailAsync(
        AdVideoJob job, string? reason, string? rawError, IPipelineStep? step, CancellationToken cancellationToken)
    {
        job.Status = JobStatus.Failed;
        job.CompletedAt = DateTime.UtcNow;
        job.FailureReason = reason ?? "Job thất bại nhưng không có lý do — đây là lỗi của hệ thống, không phải của brief.";
        // Chuỗi lỗi có thể là ex.ToString() với URL chứa key, hoặc thân phản hồi dội lại request.
        job.RawProviderError = SecretRedactor.RedactPatterns(rawError);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogError(
            "job_id={JobId} FAIL ở bước {Order} ({Name}): {Reason}",
            job.Id,
            step?.Order ?? job.CurrentStep,
            step?.DisplayName ?? "không xác định",
            job.FailureReason);
    }

    private static bool IsTerminal(JobStatus status) =>
        status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled;
}
