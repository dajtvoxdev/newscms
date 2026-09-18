using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using NewsCMS.Application.Common;
using NewsCMS.Application.Content;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.Audit;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace NewsCMS.Infrastructure.Content;

public sealed class MediaService : IMediaService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ILogger<MediaService> _logger;
    private readonly IConfiguration _cfg;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentSite _currentSite;
    private readonly IVideoCompressionQueue? _compressionQueue;

    public MediaService(
        AppDbContext db,
        IFileStorage storage,
        ILogger<MediaService> logger,
        IConfiguration cfg,
        IHttpContextAccessor httpContextAccessor,
        ICurrentSite currentSite,
        IVideoCompressionQueue? compressionQueue = null)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
        _cfg = cfg;
        _httpContextAccessor = httpContextAccessor;
        _currentSite = currentSite;
        _compressionQueue = compressionQueue;
    }

    public async Task<PagedList<MediaListItemDto>> SearchAsync(string? keyword, MediaKind? kind, Guid? folderId, bool includeDeleted, int page, int pageSize, CancellationToken ct)
    {
        var query = includeDeleted
            ? _db.Medias.IgnoreQueryFilters().AsNoTracking().Where(m => m.SiteId == _currentSite.SiteId && m.IsDeleted).AsQueryable()
            : _db.Medias.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(m => m.FileName.Contains(keyword) || (m.Title != null && m.Title.Contains(keyword)));

        if (kind.HasValue)
        {
            var kindStr = kind.Value.ToString().ToLowerInvariant();
            query = query.Where(m => m.Kind == kindStr);
        }

        if (folderId.HasValue)
            query = query.Where(m => m.FolderId == folderId.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MediaListItemDto(
                m.Id,
                m.FileName,
                m.FilePath,
                m.Kind,
                m.Size,
                m.Title,
                m.AltText,
                m.Width,
                m.Height,
                m.FolderId,
                m.CreatedAt,
                m.PosterUrl))
            .ToListAsync(ct);

        return new PagedList<MediaListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task<MediaDetailDto?> GetByPathAsync(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        // Form chỉ giữ URL ảnh; map ngược về media để lưu FK. So khớp cả FilePath lẫn
        // StorageKey vì picker có thể trả về đường dẫn tuyệt đối hoặc key lưu trữ.
        var normalized = filePath.Trim();
        var m = await _db.Medias.AsNoTracking()
            .Include(x => x.Folder)
            .FirstOrDefaultAsync(x => x.FilePath == normalized || x.StorageKey == normalized, ct);

        return m == null ? null : Map(m);
    }

    public async Task<MediaDetailDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var m = await _db.Medias.AsNoTracking()
            .Include(x => x.Folder)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return m == null ? null : Map(m);
    }

    private static MediaDetailDto Map(Domain.Entities.Content.Media m) => new(
        m.Id,
        m.FileName,
        m.FilePath,
        m.StorageKey,
        m.Kind,
        m.MimeType,
        m.Size,
        m.Title,
        m.AltText,
        m.Width,
        m.Height,
        m.FolderId,
        m.Folder?.Name,
        m.UploadedById,
        m.CreatedAt,
        m.UpdatedAt);

    public async Task<Result> UpdateAsync(Guid id, MediaUpdateDto dto, CancellationToken ct)
    {
        var media = await _db.Medias.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (media == null)
            return Result.Failure("Media not found.");

        if (!await FolderBelongsToCurrentSiteAsync(dto.FolderId, ct))
            return Result.Failure("Folder not found.");

        media.Title = dto.Title;
        media.AltText = dto.AltText;
        media.FolderId = dto.FolderId;
        media.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Media {MediaId} updated", id);
        await WriteAuditLogAsync("Edit", "Media", id.ToString(), $"Title={dto.Title}, FolderId={dto.FolderId}");

        return Result.Success();
    }

    public async Task<Result<MediaUploadResultDto>> CreateFromUploadAsync(Stream stream, string fileName, string contentType, long size, Guid userId, Guid? folderId, CancellationToken ct)
    {
        var allowed = _cfg.GetSection("Storage:AllowedExtensions").Get<string[]>() ?? new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg" };
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (!allowed.Contains(ext))
            return Result<MediaUploadResultDto>.Failure($"Phần mở rộng '{ext}' không được phép.");

        // Resolve kind trước để chọn trần size theo loại: video cho phép lớn hơn hẳn
        // (upload qua tus chunk) — ảnh/document vẫn theo MaxFileSizeMb thông thường.
        var kind = MediaKindResolver.Resolve(contentType, fileName);
        var maxMb = kind == MediaKind.Video
            ? _cfg.GetValue<int?>("Storage:MaxVideoSizeMb") ?? 2048
            : _cfg.GetValue<int?>("Storage:MaxFileSizeMb") ?? 20;
        if (size > maxMb * 1024L * 1024L)
            return Result<MediaUploadResultDto>.Failure($"File vượt quá {maxMb}MB.");

        if (!await FolderBelongsToCurrentSiteAsync(folderId, ct))
            return Result<MediaUploadResultDto>.Failure("Folder not found.");

        var subFolder = MediaKindResolver.ToSubFolder(kind);

        var key = await _storage.SaveAsync(stream, fileName, subFolder, ct);
        var publicUrl = _storage.GetPublicUrl(key);

        int? w = null, h = null;
        if (contentType.StartsWith("image/") && ext != ".svg")
        {
            try
            {
                stream.Position = 0;
                using var img = await Image.LoadAsync(stream, ct);
                w = img.Width;
                h = img.Height;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không đọc được kích thước ảnh {FileName}", fileName);
            }
        }

        var media = new Media
        {
            FileName = fileName,
            StorageKey = key,
            FilePath = publicUrl,
            MimeType = contentType,
            Kind = kind.ToString().ToLowerInvariant(),
            Size = size,
            Width = w,
            Height = h,
            FolderId = folderId,
            UploadedById = userId
        };

        _db.Medias.Add(media);
        await _db.SaveChangesAsync(ct);

        // Nén ảnh lớn lúc upload: resize về tối đa 2560px cạnh dài + encode lại
        // quality 85 để load nhanh ở UI. Ảnh nhỏ giữ nguyên byte gốc.
        if (kind == MediaKind.Image && ext is not ".svg" and not ".gif" && media.Size > 0)
            await TryOptimizeImageAsync(media, contentType, ct);

        _logger.LogInformation("Media uploaded: {FileName} ({Kind}) → {Key}", fileName, kind, key);
        await WriteAuditLogAsync("Upload", "Media", media.Id.ToString(), fileName);

        // Video .mp4 lớn → đẩy vào hàng đợi nén nền bằng ffmpeg (giảm 80-95% dung lượng cho
        // video quay thẳng từ điện thoại). Không chặn request: worker xử lý tuần tự sau, URL
        // giữ nguyên nên bài viết chèn trước hay sau đều không vỡ. Lỗi enqueue không làm hỏng
        // upload — chỉ có nghĩa video đó giữ nguyên kích thước gốc.
        if (kind == MediaKind.Video
            && string.Equals(Path.GetExtension(key), ".mp4", StringComparison.OrdinalIgnoreCase)
            && (_cfg.GetValue<bool?>("Storage:VideoCompression:Enabled") ?? true))
        {
            try { _compressionQueue?.Enqueue(media.Id); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không enqueue được job nén video cho Media {MediaId}.", media.Id);
            }
        }

        // Trả về W/H/Size SAU khi nén (nếu có) để client nhận đúng thông số file cuối.
        return Result<MediaUploadResultDto>.Success(new MediaUploadResultDto(
            media.Id,
            publicUrl,
            fileName,
            media.Size,
            media.Width,
            media.Height,
            media.Kind));
    }

    /// <summary>
    /// Tối ưu ảnh sau khi lưu: nếu quá 2560px cạnh dài hoặc nặng hơn ~1MB thì
    /// resize + re-encode (giữ định dạng) rồi ghi đè file, cập nhật W/H/Size.
    /// Lỗi nén không làm hỏng upload — ảnh gốc vẫn dùng được.
    /// </summary>
    private async Task TryOptimizeImageAsync(Media media, string contentType, CancellationToken ct)
    {
        var optimize = _cfg.GetValue<bool?>("Storage:OptimizeImages") ?? true;
        if (!optimize) return;

        const int maxEdge = 2560;
        const long sizeThreshold = 1024 * 1024;
        if (media.Width <= maxEdge && media.Height <= maxEdge && media.Size <= sizeThreshold)
            return;

        try
        {
            await using var fs = File.OpenRead(_storage.GetLocalPath(media.StorageKey));
            using var image = await Image.LoadAsync(fs, ct);
            if (image.Width > maxEdge || image.Height > maxEdge)
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxEdge, maxEdge),
                    Sampler = KnownResamplers.Lanczos3
                }));

            var tmp = Path.Combine(Path.GetTempPath(), $"ncms-img-{Guid.NewGuid():N}{Path.GetExtension(media.StorageKey)}");
            try
            {
                await using (var outFs = File.Create(tmp))
                    await image.SaveAsync(outFs, GetEncoder(contentType), ct);

                var newSize = new FileInfo(tmp).Length;
                // Chỉ ghi đè khi nén có lợi — ảnh PNG phẳng đôi khi to hơn bản gốc.
                if (newSize >= media.Size) return;

                fs.Close();
                File.Copy(tmp, _storage.GetLocalPath(media.StorageKey), overwrite: true);
                media.Width = image.Width;
                media.Height = image.Height;
                var oldSize = media.Size;
                media.Size = newSize;
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Ảnh {FileName} đã tối ưu: {Old} → {New} bytes.", media.FileName, oldSize, newSize);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* temp */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không tối ưu được ảnh {FileName} — giữ bản gốc.", media.FileName);
        }
    }

    private static IImageEncoder GetEncoder(string contentType) => contentType switch
    {
        "image/png" => new PngEncoder(),
        "image/webp" => new WebpEncoder(),
        _ => new JpegEncoder { Quality = 85 }
    };

    public async Task<Result> SoftDeleteAsync(Guid id, CancellationToken ct)
    {
        var media = await _db.Medias.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (media == null)
            return Result.Failure("Media not found.");

        if (media.IsDeleted)
            return Result.Success();

        media.IsDeleted = true;
        media.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _storage.MoveToTrashAsync(media.StorageKey, ct);

        _logger.LogInformation("Media {MediaId} soft-deleted", id);
        await WriteAuditLogAsync("Delete", "Media", id.ToString(), media.FileName);

        return Result.Success();
    }

    public async Task<Result> RestoreAsync(Guid id, CancellationToken ct)
    {
        var media = await _db.Medias.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == id && m.SiteId == _currentSite.SiteId, ct);
        if (media == null)
            return Result.Failure("Media not found.");

        if (!media.IsDeleted)
            return Result.Success();

        media.IsDeleted = false;
        media.DeletedAt = null;
        await _db.SaveChangesAsync(ct);

        await _storage.RestoreFromTrashAsync(media.StorageKey, ct);

        _logger.LogInformation("Media {MediaId} restored", id);
        await WriteAuditLogAsync("Restore", "Media", id.ToString(), media.FileName);

        return Result.Success();
    }

    public async Task<Result> PurgeAsync(Guid id, CancellationToken ct)
    {
        var media = await _db.Medias.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == id && m.SiteId == _currentSite.SiteId, ct);
        if (media == null)
            return Result.Failure("Media not found.");

        await _storage.DeleteAsync(media.StorageKey, ct);
        _db.Medias.Remove(media);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Media {MediaId} purged", id);
        await WriteAuditLogAsync("Purge", "Media", id.ToString(), media.FileName);

        return Result.Success();
    }

    public async Task<IReadOnlyList<MediaFolderDto>> GetFoldersAsync(CancellationToken ct)
    {
        return await _db.MediaFolders.AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new MediaFolderDto(f.Id, f.Name, f.ParentId))
            .ToListAsync(ct);
    }

    public async Task<Result<MediaFolderDto>> CreateFolderAsync(string name, Guid? parentId, CancellationToken ct)
    {
        if (!await FolderBelongsToCurrentSiteAsync(parentId, ct))
            return Result<MediaFolderDto>.Failure("Parent folder not found.");

        var folder = new MediaFolder { Name = name, ParentId = parentId };
        _db.MediaFolders.Add(folder);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Folder created: {Name}", name);
        return Result<MediaFolderDto>.Success(new MediaFolderDto(folder.Id, folder.Name, folder.ParentId));
    }

    public async Task<Result> RenameFolderAsync(Guid id, string name, CancellationToken ct)
    {
        var folder = await _db.MediaFolders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder == null)
            return Result.Failure("Folder not found.");

        folder.Name = name;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Folder {FolderId} renamed to {Name}", id, name);
        return Result.Success();
    }

    public async Task<Result> DeleteFolderAsync(Guid id, CancellationToken ct)
    {
        var folder = await _db.MediaFolders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder == null)
            return Result.Failure("Folder not found.");

        var hasMedia = await _db.Medias.AnyAsync(m => m.FolderId == id, ct);
        if (hasMedia)
            return Result.Failure("Thư mục còn chứa media. Di chuyển hoặc xoá media trước.");

        _db.MediaFolders.Remove(folder);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Folder {FolderId} deleted", id);
        return Result.Success();
    }

    private async Task<bool> FolderBelongsToCurrentSiteAsync(Guid? folderId, CancellationToken ct)
    {
        if (!folderId.HasValue)
            return true;

        return await _db.MediaFolders.AnyAsync(f => f.Id == folderId.Value, ct);
    }

    private async Task WriteAuditLogAsync(string action, string entity, string? entityId, string? detail)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return;

        var user = httpContext.User;
        var userName = user.Identity?.Name;
        var userIdStr = user.FindFirst("sub")?.Value ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        var userId = Guid.TryParse(userIdStr, out var uid) ? uid : (Guid?)null;
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();

        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            Entity = entity,
            EntityId = entityId,
            Detail = detail,
            UserId = userId,
            UserName = userName,
            IpAddress = ip
        });

        await _db.SaveChangesAsync();
    }
}
