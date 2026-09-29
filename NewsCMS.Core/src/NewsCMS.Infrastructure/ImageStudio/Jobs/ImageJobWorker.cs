using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.ImageStudio.Jobs;

/// <summary>
/// Tiêu thụ <see cref="IImageJobQueue"/> với <c>ImageStudio:MaxConcurrency</c> consumer.
/// </summary>
/// <remarks>
/// Lúc khởi động: job <c>Queued</c> được nạp lại vào hàng đợi; job <c>Running</c> quá hạn bị đánh
/// <c>Failed</c> — <b>không tự chạy lại</b>, vì provider có thể đã sinh ảnh và tính tiền trước khi
/// tiến trình chết. Người dùng thấy lỗi và tự quyết định tạo lại.
/// </remarks>
public sealed class ImageJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IImageJobQueue _queue;
    private readonly ImageStudioOptions _options;
    private readonly ILogger<ImageJobWorker> _logger;

    public ImageJobWorker(IServiceScopeFactory scopes, IImageJobQueue queue, IOptions<ImageStudioOptions> options, ILogger<ImageJobWorker> logger)
    {
        _scopes = scopes;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await RecoverAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DB chưa migrate hay tạm thời không kết nối được: không làm sập host, job mới vẫn chạy.
            _logger.LogError(ex, "Không nạp lại được job tạo ảnh lúc khởi động.");
        }

        int consumers = Math.Clamp(_options.MaxConcurrency, 1, 16);
        await Task.WhenAll(Enumerable.Range(0, consumers).Select(_ => ConsumeAsync(stoppingToken)));
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (Guid jobId in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    using IServiceScope scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IImageJobRunner>().RunAsync(jobId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Worker tạo ảnh lỗi với job {JobId}.", jobId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Tắt ứng dụng.
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopes.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        DateTime staleBefore = DateTime.UtcNow.AddMinutes(-Math.Max(_options.StaleRunningMinutes, 1));

        List<ImageJob> stale = await db.ImageJobs.IgnoreQueryFilters()
            .Where(j => j.Status == ImageJobStatus.Running && j.StartedAt < staleBefore)
            .ToListAsync(ct);

        foreach (ImageJob job in stale)
        {
            job.Status = ImageJobStatus.Failed;
            job.FinishedAt = DateTime.UtcNow;
            job.Error = "Máy chủ khởi động lại giữa chừng. Hãy tạo lại.";
        }

        if (stale.Count > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Instance khác vừa đổi trạng thái — để nó xử lý.
            }

            _logger.LogWarning("Đánh lỗi {Count} job tạo ảnh bị gián đoạn.", stale.Count);
        }

        List<Guid> queued = await db.ImageJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(j => j.Status == ImageJobStatus.Queued)
            .OrderBy(j => j.QueuedAt)
            .Select(j => j.Id)
            .ToListAsync(ct);

        foreach (Guid id in queued)
        {
            _queue.Enqueue(id);
        }
    }
}
