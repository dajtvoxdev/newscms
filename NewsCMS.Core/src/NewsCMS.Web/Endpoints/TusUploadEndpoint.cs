using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Content;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;
using tusdotnet.Stores;

namespace NewsCMS.Web.Endpoints;

/// <summary>
/// Endpoint upload resumable chuẩn tus.io tại /admin/media/tus (tusdotnet).
/// File > 32MB từ admin sẽ đi qua đây thay vì POST /Admin/Media/Upload:
/// - Chia chunk ~64MB nên vượt qua giới hạn body của Cloudflare Tunnel (~100MB).
/// - Rớt mạng giữa chừng chỉ cần retry phần còn thiếu (resume theo offset).
/// - Validate extension + size TRƯỚC khi nhận byte nào (OnBeforeCreateAsync).
/// - Hoàn tất thì stream qua IMediaService.CreateFromUploadAsync — tái dùng 100%
///   validation/kind/subfolder/audit logic, không nhân bản quy tắc.
/// - Video thì sinh poster bằng ffmpeg rồi mới báo completed cho client.
/// </summary>
public static class TusUploadEndpoint
{
    public const string Path = "/admin/media/tus";

    public static async Task<DefaultTusConfiguration> BuildConfigurationAsync(HttpContext httpContext)
    {
        var sp = httpContext.RequestServices;
        var cfg = sp.GetRequiredService<IConfiguration>();
        var store = sp.GetRequiredService<TusDiskStore>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("NewsCMS.Web.Endpoints.TusUploadEndpoint");

        var maxVideoMb = cfg.GetValue<int?>("Storage:MaxVideoSizeMb") ?? 2048;
        var maxVideoBytes = maxVideoMb * 1024L * 1024L;

        // Kestrel mặc định chặn body ~28.6MB — chunk 64MB sẽ bị cắt nếu không nâng
        // limit RIÊNG cho request này. Cloudflare Tunnel vẫn là gate thực tế bên ngoài.
        var bodySizeFeature = httpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature != null)
            bodySizeFeature.MaxRequestBodySize = maxVideoBytes;

        return await Task.FromResult(new DefaultTusConfiguration
        {
            // LƯU Ý: không được set UrlPath khi dùng endpoint routing (MapTus) —
            // tusdotnet sẽ ném TusConfigurationException.
            Store = store,
            MaxAllowedUploadSizeInBytesLong = maxVideoBytes,
            AllowedExtensions = TusExtensions.All.Except(TusExtensions.Concatenation),
            Expiration = new SlidingExpiration(TimeSpan.FromHours(24)),
            MetadataParsingStrategy = MetadataParsingStrategy.AllowEmptyValues,
            UsePipelinesIfAvailable = true,
            FileLockProvider = sp.GetRequiredService<tusdotnet.Interfaces.ITusFileLockProvider>(),
            Events = new Events
            {
                // Mirror PermissionAuthorizationHandler: claim type "Permission" == Media.Upload.
                OnAuthorizeAsync = eventContext =>
                {
                    var user = eventContext.HttpContext.User;
                    if (user?.Identity?.IsAuthenticated != true)
                    {
                        eventContext.FailRequest(HttpStatusCode.Unauthorized);
                        return Task.CompletedTask;
                    }

                    if (!user.Claims.Any(c => c.Type == Permissions.Prefix && c.Value == Permissions.Media.Upload))
                        eventContext.FailRequest(HttpStatusCode.Forbidden);

                    return Task.CompletedTask;
                },

                // Fail fast trước khi nhận byte nào: sai đuôi file → 400, quá size → 413.
                OnBeforeCreateAsync = eventContext =>
                {
                    var metadata = eventContext.Metadata;
                    if (!metadata.TryGetValue("filename", out var fileNameMeta) || string.IsNullOrWhiteSpace(fileNameMeta.GetString(System.Text.Encoding.UTF8)))
                    {
                        eventContext.FailRequest(HttpStatusCode.BadRequest);
                        return Task.CompletedTask;
                    }

                    var fileName = fileNameMeta.GetString(System.Text.Encoding.UTF8)!;
                    metadata.TryGetValue("filetype", out var fileTypeMeta);
                    var contentType = fileTypeMeta?.GetString(System.Text.Encoding.UTF8) ?? "application/octet-stream";

                    var allowed = cfg.GetSection("Storage:AllowedExtensions").Get<string[]>()
                                  ?? new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg" };
                    var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
                    if (!allowed.Contains(ext))
                    {
                        logger.LogInformation("Tus upload bị chặn: phần mở rộng '{Ext}' không được phép ({FileName}).", ext, fileName);
                        eventContext.FailRequest(HttpStatusCode.BadRequest);
                        return Task.CompletedTask;
                    }

                    var kind = MediaKindResolver.Resolve(contentType, fileName);
                    var maxMb = kind == MediaKind.Video
                        ? cfg.GetValue<int?>("Storage:MaxVideoSizeMb") ?? 2048
                        : cfg.GetValue<int?>("Storage:MaxFileSizeMb") ?? 20;
                    if (!eventContext.UploadLengthIsDeferred && eventContext.UploadLength > maxMb * 1024L * 1024L)
                    {
                        logger.LogInformation("Tus upload bị chặn: {FileName} vượt quá {Max}MB.", fileName, maxMb);
                        eventContext.FailRequest(HttpStatusCode.RequestEntityTooLarge);
                    }

                    return Task.CompletedTask;
                },

                // PATCH cuối hoàn tất → tạo bản ghi Media (+ poster video) rồi xoá file tạm.
                // Chạy đồng bộ trong request cuối: restart giữa chừng không để lại session mồ côi,
                // chi phí tối đa bounded bởi timeout ffmpeg trong VideoThumbnailService.
                OnFileCompleteAsync = eventContext => CompleteUploadAsync(eventContext, logger)
            }
        });
    }

    private static async Task CompleteUploadAsync(FileCompleteContext eventContext, ILogger logger)
    {
        var httpContext = eventContext.HttpContext;
        var sp = httpContext.RequestServices;
        var db = sp.GetRequiredService<AppDbContext>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var media = sp.GetRequiredService<IMediaService>();
        var thumbnails = sp.GetRequiredService<IVideoThumbnailService>();
        var store = sp.GetRequiredService<TusDiskStore>();
        var env = sp.GetRequiredService<IWebHostEnvironment>();

        var fileId = eventContext.FileId;
        var ct = eventContext.CancellationToken;

        // Idempotency: PATCH cuối có thể retry — session completed rồi thì thôi.
        var existing = await db.MediaUploadSessions.FirstOrDefaultAsync(s => s.TusFileId == fileId, ct);
        if (existing is { Status: "completed" })
            return;

        var file = await eventContext.GetFileAsync();
        var meta = await file.GetMetadataAsync(ct);
        var fileName = meta.TryGetValue("filename", out var n) ? n.GetString(System.Text.Encoding.UTF8) ?? "file" : "file";
        var contentType = meta.TryGetValue("filetype", out var t) && !string.IsNullOrWhiteSpace(t.GetString(System.Text.Encoding.UTF8))
            ? t.GetString(System.Text.Encoding.UTF8)!
            : "application/octet-stream";
        Guid? folderId = null;
        if (meta.TryGetValue("folderid", out var folderMeta)
            && Guid.TryParse(folderMeta.GetString(System.Text.Encoding.UTF8), out var parsedFolder))
            folderId = parsedFolder;

        var userIdStr = users.GetUserId(httpContext.User);
        if (userIdStr is null || !Guid.TryParse(userIdStr, out var userId))
        {
            await MarkSessionAsync(db, existing, fileId, "failed", "Phiên đăng nhập không hợp lệ.", null,
                userId: Guid.Empty, fileName, file, ct);
            return;
        }

        // Copy ra file temp MỘT lần duy nhất: IMediaService đọc seek-able stream
        // (probe kích thước ảnh reset Position), ffmpeg cũng đọc lại từ temp này.
        var ext = System.IO.Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) ext = ".bin";
        var tmpDir = System.IO.Path.Combine(env.ContentRootPath, "App_Data", "tmp");
        Directory.CreateDirectory(tmpDir);
        var tempPath = System.IO.Path.Combine(tmpDir, $"{Guid.NewGuid():N}{ext}");

        try
        {
            await using (var content = await file.GetContentAsync(ct))
            await using (var fs = File.Create(tempPath))
                await content.CopyToAsync(fs, ct);

            var size = new FileInfo(tempPath).Length;

            var session = existing ?? new MediaUploadSession
            {
                TusFileId = fileId,
                UserId = userId,
                FileName = fileName,
                Size = size
            };
            session.Status = "processing";
            session.Error = null;
            session.Size = size;
            session.UpdatedAt = DateTime.UtcNow;
            if (existing == null) db.MediaUploadSessions.Add(session);
            await db.SaveChangesAsync(ct);

            await using var videoStream = File.OpenRead(tempPath);
            var result = await media.CreateFromUploadAsync(videoStream, fileName, contentType, size, userId, folderId, ct);

            if (!result.Succeeded)
            {
                logger.LogWarning("Tus upload {FileName} thất bại khi tạo Media: {Error}", fileName, result.Error);
                await MarkSessionAsync(db, session, fileId, "failed", result.Error ?? "Lỗi không xác định.", null, userId, fileName, file, ct);
                return;
            }

            var dto = result.Value!;

            // Poster video: lỗi được nuốt trong service, không làm đổ upload.
            string? posterUrl = null;
            if (dto.Kind == "video")
            {
                await using var posterSource = File.OpenRead(tempPath);
                var posterResult = await thumbnails.GenerateForVideoAsync(posterSource, fileName, ct);
                if (posterResult.Succeeded && posterResult.Value != null)
                {
                    posterUrl = posterResult.Value.PublicUrl;
                    var mediaRow = await db.Medias.FirstAsync(m => m.Id == dto.Id, ct);
                    mediaRow.PosterUrl = posterResult.Value.PublicUrl;
                    mediaRow.PosterStorageKey = posterResult.Value.StorageKey;
                    mediaRow.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }

            session.Status = "completed";
            session.MediaId = dto.Id;
            session.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Video .mp4 qua tus (file lớn) cần nén nhất — đẩy vào hàng đợi nén nền,
            // giống MediaService cho luồng upload trực tiếp. Không chặn response: nén chạy
            // sau, URL giữ nguyên nên bài viết chèn trước hay sau đều không vỡ.
            if (dto.Kind == "video"
                && string.Equals(System.IO.Path.GetExtension(fileName), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                if (cfg.GetValue<bool?>("Storage:VideoCompression:Enabled") ?? true)
                {
                    try { sp.GetRequiredService<IVideoCompressionQueue>().Enqueue(dto.Id); }
                    catch (Exception ex) { logger.LogWarning(ex, "Không enqueue được job nén video cho Media {MediaId}.", dto.Id); }
                }
            }

            // Xoá file tus khỏi store: tusdotnet KHÔNG tự xoá file đã hoàn tất.
            try { await store.DeleteFileAsync(fileId, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Không xoá được file tus {FileId}.", fileId); }

            logger.LogInformation("Tus upload hoàn tất: {FileName} → {Location} (poster: {Poster})", fileName, dto.Location, posterUrl ?? "không");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tus upload {FileName} lỗi không mong muốn.", fileName);
            try
            {
                await MarkSessionAsync(db, existing, fileId, "failed", $"Lỗi xử lý: {ex.Message}", null, userId, fileName, file, ct);
            }
            catch (Exception inner)
            {
                logger.LogWarning(inner, "Không cập nhật được session sau lỗi.");
            }
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* temp */ }
        }
    }

    /// <summary>Ghi status vào session — dùng chung các nhánh thoát (failed không kèm xoá store).</summary>
    private static async Task MarkSessionAsync(
        AppDbContext db, MediaUploadSession? session, string fileId, string status, string error,
        Guid? mediaId, Guid userId, string fileName, ITusFile file, CancellationToken ct)
    {
        session ??= new MediaUploadSession
        {
            TusFileId = fileId,
            UserId = userId,
            FileName = fileName
        };
        session.Status = status;
        session.Error = error.Length > 500 ? error[..500] : error;
        session.MediaId = mediaId;
        session.UpdatedAt = DateTime.UtcNow;
        if (db.Entry(session).State == EntityState.Detached)
            db.MediaUploadSessions.Add(session);
        await db.SaveChangesAsync(ct);
    }
}
