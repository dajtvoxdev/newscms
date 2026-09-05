using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Stores;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Storage;

/// <summary>
/// Dọn dẹp định kỳ cho upload tus:
/// - Xoá file chưa hoàn tất đã hết hạn (SlidingExpiration 24h) khỏi TusDiskStore.
/// - Session "processing" treo quá 2h (app chết giữa chừng PATCH cuối) → "failed".
/// - Purge session "completed"/"failed" cũ quá 7 ngày.
/// </summary>
public sealed class TusCleanupWorker : BackgroundService
{
    private static readonly TimeSpan StaleProcessingThreshold = TimeSpan.FromHours(2);
    private static readonly TimeSpan CompletedRetention = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TusCleanupWorker> _logger;

    public TusCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<TusCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Chờ app khởi động ổn định rồi mới quét đầu tiên.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TusCleanupWorker chu kỳ dọn dẹp lỗi.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CleanupOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<TusDiskStore>();

        // 1. File tus chưa hoàn tất quá hạn — store tự biết qua expiration header.
        var expiredCount = await store.RemoveExpiredFilesAsync(ct);
        if (expiredCount > 0)
            _logger.LogInformation("Đã xoá {Count} file tus hết hạn.", expiredCount);

        // 2. Session "processing" treo: app chết giữa chừng xử lý PATCH cuối.
        var staleCutoff = DateTime.UtcNow - StaleProcessingThreshold;
        var stale = await db.MediaUploadSessions
            .Where(s => s.Status == "processing" && s.CreatedAt < staleCutoff)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            foreach (var s in stale)
            {
                s.Status = "failed";
                s.Error = "Upload bị gián đoạn, vui lòng thử lại.";
                s.UpdatedAt = DateTime.UtcNow;
                // File tus tương ứng có thể vẫn còn trong store nếu chết trước khi xoá.
                try
                {
                    if (await store.FileExistAsync(s.TusFileId, ct))
                        await store.DeleteFileAsync(s.TusFileId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Không xoá được file tus {TusFileId} của session treo.", s.TusFileId);
                }
            }
            await db.SaveChangesAsync(ct);
            _logger.LogWarning("Đã đánh dấu failed {Count} session upload treo.", stale.Count);
        }

        // 3. Session cũ đã kết thúc — giữ lịch sử tối đa 7 ngày rồi purge.
        var oldCutoff = DateTime.UtcNow - CompletedRetention;
        var oldSessions = await db.MediaUploadSessions
            .Where(s => (s.Status == "completed" || s.Status == "failed") && s.UpdatedAt < oldCutoff)
            .ToListAsync(ct);
        if (oldSessions.Count > 0)
        {
            db.MediaUploadSessions.RemoveRange(oldSessions);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Đã purge {Count} session upload cũ.", oldSessions.Count);
        }
    }
}
