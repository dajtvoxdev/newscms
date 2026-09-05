using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Common;
using NewsCMS.Application.Content;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Infrastructure.Content;

/// <summary>
/// Sinh poster video bằng ffmpeg: lấy 1 frame (ưu tiên giây thứ 1, fallback 0s
/// cho clip quá ngắn) rồi resize về rộng 640px, lưu qua IFileStorage (thư mục images).
/// Không bao giờ ném exception — lỗi chỉ log warning và trả Failure.
/// </summary>
public sealed class VideoThumbnailService : IVideoThumbnailService
{
    private const int TimeoutSeconds = 60;

    private readonly IFileStorage _storage;
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<VideoThumbnailService> _logger;

    public VideoThumbnailService(
        IFileStorage storage,
        IConfiguration cfg,
        IWebHostEnvironment env,
        ILogger<VideoThumbnailService> logger)
    {
        _storage = storage;
        _cfg = cfg;
        _env = env;
        _logger = logger;
    }

    public async Task<Result<VideoPosterResult>> GenerateForVideoAsync(Stream videoStream, string originalFileName, CancellationToken ct)
    {
        // FfmpegPath rỗng → tính năng tắt chủ đích.
        var ffmpegPath = _cfg["Storage:FfmpegPath"];
        if (string.IsNullOrWhiteSpace(ffmpegPath))
            return Result<VideoPosterResult>.Failure("ffmpeg chưa được cấu hình.");

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext)) ext = ".mp4";

        var tmpDir = Path.Combine(_env.ContentRootPath, "App_Data", "tmp");
        Directory.CreateDirectory(tmpDir);

        var tmpVideo = Path.Combine(tmpDir, $"{Guid.NewGuid():N}{ext}");
        var tmpPoster = Path.Combine(tmpDir, $"{Guid.NewGuid():N}.jpg");

        try
        {
            await using (var fs = File.Create(tmpVideo))
                await videoStream.CopyToAsync(fs, ct);

            // Thử seek tới giây 1 trước (frame đẹp hơn frame đầu); clip < 1s hoặc container
            // seek chậm sẽ fail → thử lại từ 0s.
            string? posterFile = null;
            foreach (var seek in new[] { "1", "0" })
            {
                ct.ThrowIfCancellationRequested();
                posterFile = await TryExtractAsync(ffmpegPath, tmpVideo, tmpPoster, seek, ct);
                if (posterFile != null) break;
            }

            if (posterFile == null || new FileInfo(posterFile).Length == 0)
            {
                _logger.LogWarning("Không sinh được poster cho {FileName} (ffmpeg exit != 0 hoặc output rỗng).", originalFileName);
                return Result<VideoPosterResult>.Failure("ffmpeg không sinh được poster.");
            }

            var baseName = Path.GetFileNameWithoutExtension(originalFileName);
            await using var posterStream = File.OpenRead(posterFile);
            var key = await _storage.SaveAsync(posterStream, $"{baseName}-poster.jpg", MediaKindResolver.ToSubFolder(MediaKind.Image), ct);

            return Result<VideoPosterResult>.Success(new VideoPosterResult(key, _storage.GetPublicUrl(key)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Poster là tuỳ chọn — mọi sự cố (ffmpeg crash, disk full...) không được làm hỏng upload.
            _logger.LogWarning(ex, "Lỗi sinh poster cho {FileName}.", originalFileName);
            return Result<VideoPosterResult>.Failure($"Lỗi sinh poster: {ex.Message}");
        }
        finally
        {
            TryDelete(tmpVideo);
            TryDelete(tmpPoster);
        }
    }

    /// <summary>Chạy ffmpeg 1 lần; trả về đường dẫn poster nếu thành công, null nếu thất bại/timeout.</summary>
    private async Task<string?> TryExtractAsync(string ffmpegPath, string input, string output, string seekSeconds, CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -hide_banner -loglevel error -ss {seekSeconds} -i \"{input}\" -vframes 1 -vf scale=640:-2 -q:v 3 \"{output}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };

            process.Start();

            // Đọc stderr bất đồng bộ để pipe không đầy làm treo ffmpeg.
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Hết 60s — giết cả cây tiến trình (ffmpeg có thể spawn helper).
                try { process.Kill(entireProcessTree: true); } catch { /* đã thoát */ }
                return null;
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                _logger.LogDebug("ffmpeg -ss {Seek} thất bại (exit {Code}): {Stderr}",
                    seekSeconds, process.ExitCode, (await stderrTask)[..Math.Min(500, (await stderrTask).Length)]);
                return null;
            }

            return output;
        }
        catch (Exception ex)
        {
            // Thường gặp: ffmpeg chưa cài trên máy chủ (exit code / file not found).
            _logger.LogDebug(ex, "ffmpeg không chạy được ({FfmpegPath}).", ffmpegPath);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Temp file kẹt bởi tiến trình khác — dọn ở lần chạy sau, bỏ qua.
        }
    }
}
