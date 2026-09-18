using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Common;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Infrastructure.Content;

/// <summary>
/// Nén video .mp4 bằng ffmpeg (H.264 + AAC, CRF-based) để giảm dung lượng — video quay thẳng từ
/// điện thoại/ghi màn hình thường bitrate rất cao (vd 2 phút ~500MB, ~33Mbps) trong khi web chỉ
/// cần 2-5Mbps là đã mượt. Chạy NGOÀI request HTTP (xem <see cref="VideoCompressionWorker"/>) vì
/// một video dài có thể mất vài phút để nén — giữ nguyên trong request tus/upload sẽ có nguy cơ
/// timeout qua reverse proxy/Cloudflare Tunnel.
///
/// CHỈ nén file .mp4 tại CHỖ (giữ nguyên StorageKey/FilePath/extension) — KHÔNG đổi định dạng
/// (.mov/.webm/.ogv). Lý do: mediaHtml() ở editor gắn &lt;source type="video/quicktime"&gt; cho
/// .mov theo đúng extension gốc; nếu ta lặng lẽ đổi container sang mp4 mà giữ khác/đổi extension,
/// trình duyệt (Chrome) sẽ bỏ qua source đó theo type attribute — hỏng phát ngay cả khi byte thật
/// chạy tốt. Đổi extension/URL cũng phá bài viết ĐÃ lưu (dán src cũ). An toàn nhất: chỉ tối ưu
/// case KHÔNG cần đổi URL — đúng lúc file đã là .mp4.
/// </summary>
public interface IVideoCompressionService
{
    Task<Result> CompressAsync(Guid mediaId, CancellationToken ct = default);
}

public sealed class VideoCompressionService : IVideoCompressionService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IConfiguration _cfg;
    private readonly IHostEnvironment _env;
    private readonly ILogger<VideoCompressionService> _logger;

    public VideoCompressionService(
        AppDbContext db,
        IFileStorage storage,
        IConfiguration cfg,
        IHostEnvironment env,
        ILogger<VideoCompressionService> logger)
    {
        _db = db;
        _storage = storage;
        _cfg = cfg;
        _env = env;
        _logger = logger;
    }

    public async Task<Result> CompressAsync(Guid mediaId, CancellationToken ct = default)
    {
        var ffmpegPath = _cfg["Storage:FfmpegPath"];
        if (string.IsNullOrWhiteSpace(ffmpegPath))
            return Result.Failure("ffmpeg chưa được cấu hình.");

        var media = await _db.Medias.FirstOrDefaultAsync(m => m.Id == mediaId, ct);
        if (media is null) return Result.Failure("Media không tồn tại (có thể đã bị xoá trước khi job chạy).");

        // Chỉ nén video .mp4 — xem lý do ở doc-comment class. Nếu media đã bị đổi loại/xoá giữa
        // lúc enqueue và lúc worker chạy tới, bỏ qua an toàn thay vì ép nén nhầm file.
        if (!string.Equals(media.Kind, "video", StringComparison.OrdinalIgnoreCase))
            return Result.Failure("Media không phải video.");
        var ext = Path.GetExtension(media.StorageKey);
        if (!string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase))
            return Result.Failure($"Bỏ qua nén: chỉ hỗ trợ .mp4 (media này là {ext}).");

        var sourcePath = _storage.GetLocalPath(media.StorageKey);
        if (!File.Exists(sourcePath))
            return Result.Failure("File gốc không tồn tại trên đĩa.");

        var minSizeMb = _cfg.GetValue<int?>("Storage:VideoCompression:MinSizeMb") ?? 20;
        var originalSize = new FileInfo(sourcePath).Length;
        if (originalSize < minSizeMb * 1024L * 1024L)
            return Result.Failure("File nhỏ hơn ngưỡng nén — bỏ qua, không đáng nén.");

        var crf = _cfg.GetValue<int?>("Storage:VideoCompression:Crf") ?? 26;
        var preset = _cfg["Storage:VideoCompression:Preset"] ?? "veryfast";
        var timeoutMinutes = _cfg.GetValue<int?>("Storage:VideoCompression:TimeoutMinutes") ?? 20;

        var tmpDir = Path.Combine(_env.ContentRootPath, "App_Data", "tmp");
        Directory.CreateDirectory(tmpDir);
        var tmpOutput = Path.Combine(tmpDir, $"{Guid.NewGuid():N}.mp4");

        try
        {
            var ok = await RunFfmpegAsync(ffmpegPath, sourcePath, tmpOutput, crf, preset,
                TimeSpan.FromMinutes(timeoutMinutes), ct);

            if (!ok || !File.Exists(tmpOutput))
            {
                _logger.LogWarning("Nén video thất bại cho Media {MediaId} ({FileName}) — giữ file gốc.",
                    media.Id, media.FileName);
                return Result.Failure("ffmpeg nén thất bại.");
            }

            var newSize = new FileInfo(tmpOutput).Length;
            // Chỉ ghi đè khi nén thực sự có lợi — clip đã nén sẵn từ trước có thể ra file to hơn
            // (giống TryOptimizeImageAsync cho ảnh), lúc đó giữ nguyên bản gốc là đúng.
            if (newSize <= 0 || newSize >= originalSize)
            {
                _logger.LogInformation(
                    "Nén video Media {MediaId} không có lợi ({Old}→{New} bytes) — giữ file gốc.",
                    media.Id, originalSize, newSize);
                return Result.Success();
            }

            // Ghi đè ĐÚNG path gốc — KHÔNG đổi StorageKey/FilePath, URL đã nhúng trong bài viết
            // (nếu admin lưu bài trước khi job này chạy xong) vẫn trỏ đúng chỗ, không hề đổi.
            File.Copy(tmpOutput, sourcePath, overwrite: true);
            media.Size = newSize;
            media.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var savedPct = 100 - (int)Math.Round(newSize * 100.0 / originalSize);
            _logger.LogInformation(
                "Đã nén video Media {MediaId} ({FileName}): {Old:N0} → {New:N0} bytes (giảm {Pct}%).",
                media.Id, media.FileName, originalSize, newSize, savedPct);

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Nén là tối ưu, không phải nghiệp vụ bắt buộc — mọi lỗi (ffmpeg crash, disk đầy...)
            // không được làm mất video gốc, chỉ log và bỏ qua.
            _logger.LogWarning(ex, "Lỗi nén video Media {MediaId}.", mediaId);
            return Result.Failure($"Lỗi nén video: {ex.Message}");
        }
        finally
        {
            TryDelete(tmpOutput);
        }
    }

    private async Task<bool> RunFfmpegAsync(
        string ffmpegPath, string input, string output, int crf, string preset,
        TimeSpan timeout, CancellationToken ct)
    {
        // Cố ý KHÔNG đổi độ phân giải (chỉ re-encode bitrate qua CRF) — video quay từ điện thoại
        // thường ĐÃ đủ độ phân giải hợp lý (1080p/4K), vấn đề thực sự là bitrate quá cao so với
        // độ phân giải đó (vd 2 phút/500MB ≈ 33Mbps — cao gấp 5-10 lần mức web cần). CRF-based
        // encode giữ nguyên kích thước khung hình nhưng nén hiệu quả hơn nhiều so với bitrate quay
        // gốc, thường giảm 80-95% dung lượng mà không cần đụng đến resolution (tránh rủi ro tính
        // sai chiều cho video dọc/ngang nếu thêm bộ lọc scale).
        //
        // +faststart: dời "moov atom" (bảng chỉ mục) lên đầu file — THIẾU cờ này thì trình duyệt
        // phải tải gần hết file mới phát được / tua (seek) sẽ hỏng, phá luôn lợi ích của HTTP
        // Range mà StaticFiles đã hỗ trợ sẵn cho streaming.
        var args = $"-y -hide_banner -loglevel error -i \"{input}\" " +
                   $"-c:v libx264 -crf {crf} -preset {preset} -c:a aac -b:a 128k " +
                   $"-movflags +faststart \"{output}\"";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ffmpeg không chạy được ({FfmpegPath}).", ffmpegPath);
            return false;
        }

        // Đọc stderr bất đồng bộ để pipe không đầy làm treo ffmpeg (video dài, log lỗi có thể dài).
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Hết thời gian cho phép — giết cả cây tiến trình (ffmpeg spawn helper con).
            try { process.Kill(entireProcessTree: true); } catch { /* đã thoát */ }
            _logger.LogWarning("ffmpeg nén video vượt quá {Timeout} — đã huỷ.", timeout);
            return false;
        }

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            _logger.LogDebug("ffmpeg nén thất bại (exit {Code}): {Stderr}",
                process.ExitCode, stderr[..Math.Min(500, stderr.Length)]);
            return false;
        }

        return true;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* temp file kẹt bởi tiến trình khác — dọn ở lần chạy sau, bỏ qua */ }
    }
}
