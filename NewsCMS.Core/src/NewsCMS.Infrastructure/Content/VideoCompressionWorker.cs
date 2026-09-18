using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Content;

/// <summary>
/// Background service tiêu thụ hàng đợi <see cref="IVideoCompressionQueue"/>: mỗi khi có mediaId
/// mới, gọi <see cref="IVideoCompressionService.CompressAsync"/> để nén video bằng ffmpeg (H.264)
/// giảm dung lượng. Chạy tuần tự, một job tại một lúc — đủ cho news CMS có upload video thỉnh
/// thoảng, không cần worker pool phức tạp.
///
/// KHÔNG inject IVideoCompressionService trực tiếp: service đó là scoped (giữ AppDbContext),
/// còn worker là singleton — inject trực tiếp tạo captive dependency (DbContext sống suốt đời
/// app, stale data + lỗi concurrency). Mỗi job tạo scope riêng qua IServiceScopeFactory.
///
/// Khi khởi động, sweep một lần toàn bộ video .mp4 vượt ngưỡng nén trong DB rồi enqueue —
/// vì hàng đợi là Channel trong bộ nhớ, app restart (deploy/rebuild) làm mất job đang chờ;
/// không sweep thì video upload trước lúc restart mãi mãi không được nén.
///
/// Không throw exception nếu ffmpeg fail: video gốc vẫn giữ nguyên, chỉ log warning.
/// </summary>
public sealed class VideoCompressionWorker : BackgroundService
{
    private readonly IVideoCompressionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VideoCompressionWorker> _logger;

    public VideoCompressionWorker(
        IVideoCompressionQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<VideoCompressionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VideoCompressionWorker started.");

        await SweepUncompressedAsync(stoppingToken);

        await foreach (var mediaId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await CompressOneAsync(mediaId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error during video compression for Media {MediaId}.", mediaId);
            }
        }

        _logger.LogInformation("VideoCompressionWorker stopped.");
    }

    private async Task CompressOneAsync(Guid mediaId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var compression = scope.ServiceProvider.GetRequiredService<IVideoCompressionService>();
        var result = await compression.CompressAsync(mediaId, ct);
        if (!result.Succeeded)
            // LogInformation (không phải Debug): skip/fail là chuyện người vận hành cần thấy —
            // để Debug thì "video không được nén" hoàn toàn im lặng, đúng cái bẫy vừa gặp.
            _logger.LogInformation("Nén video bỏ qua/thất bại cho Media {MediaId}: {Error}", mediaId, result.Error);
    }

    /// <summary>
    /// Quét video .mp4 vượt ngưỡng nén còn sót trong DB (upload trước khi worker chạy, hoặc job
    /// trong Channel bị mất do restart) rồi enqueue lại. Heuristic "chưa nén": Size vượt gấp đôi
    /// ngưỡng MinSizeMb — video đã qua CRF 28 hiếm khi còn to như vậy, nên quét này gần như chỉ
    /// trúng video gốc chưa nén, không nén lại video đã nén (tránh vòng lặp tốn CPU mỗi restart).
    /// </summary>
    private async Task SweepUncompressedAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            if (!(cfg.GetValue<bool?>("Storage:VideoCompression:Enabled") ?? true))
                return;

            var minSizeMb = cfg.GetValue<int?>("Storage:VideoCompression:MinSizeMb") ?? 20;
            var threshold = minSizeMb * 2L * 1024L * 1024L;

            // IgnoreQueryFilters: worker chạy ngoài HTTP request nên CurrentSiteId = Guid.Empty,
            // global filter SiteId làm query khớp 0 dòng (bug "sweep im lặng, không nén gì").
            // Nén là tác vụ hệ thống nên quét MỌI site — đúng như TusCleanupWorker đang làm.
            var ids = await db.Medias.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.Kind == "video"
                    && m.StorageKey.EndsWith(".mp4")
                    && !m.IsDeleted
                    && m.Size >= threshold)
                .Select(m => m.Id)
                .ToListAsync(ct);

            foreach (var id in ids)
                _queue.Enqueue(id);

            if (ids.Count > 0)
                _logger.LogInformation("VideoCompressionWorker sweep: enqueue {Count} video chưa nén.", ids.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Sweep lỗi (DB chưa migrate...) không được chặn worker nhận job realtime.
            _logger.LogWarning(ex, "VideoCompressionWorker sweep thất bại — bỏ qua, vẫn nhận job mới.");
        }
    }
}
