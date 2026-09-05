using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.Seo;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.ContentTypes;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Tra path → SiteRoute với cache IMemoryCache + SiteCacheSignal (đúng pattern SiteResolver).
/// SiteRoute bị global query filter scope theo site hiện tại nên không cần lọc SiteId thủ công
/// trong query resolve; cache key vẫn kèm SiteId để an toàn khi site đổi.
/// </summary>
public sealed class RouteRegistry : IRouteRegistry
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly SiteCacheSignal _signal;
    private readonly ICurrentSite _currentSite;
    private readonly IContentTypeRegistry _contentTypes;

    public RouteRegistry(
        AppDbContext db,
        IMemoryCache cache,
        SiteCacheSignal signal,
        ICurrentSite currentSite,
        IContentTypeRegistry contentTypes)
    {
        _db = db;
        _cache = cache;
        _signal = signal;
        _currentSite = currentSite;
        _contentTypes = contentTypes;
    }

    public async Task<ResolvedRoute?> ResolveAsync(string path, string? culture = null, CancellationToken ct = default)
    {
        var normalized = NormalizePath(path);
        var siteId = _currentSite.SiteId;
        culture = await ResolveCultureAsync(culture, ct);

        var key = $"route:{siteId}:{culture}:{normalized}";
        if (_cache.TryGetValue<ResolvedRoute?>(key, out var cached))
        {
            return cached;
        }

        var route = await _db.SiteRoutes.AsNoTracking()
            .Where(r => r.Culture == culture && r.Path == normalized)
            .Select(r => new ResolvedRoute(r.SiteId, r.Path, r.Culture, r.RouteType, r.TargetId, r.IsPrimary))
            .FirstOrDefaultAsync(ct);

        // Cache cả kết quả rỗng trong thời gian ngắn để tránh query lặp cho path 404,
        // nhưng gắn signal để route vừa thêm được thấy ngay khi Invalidate().
        using var entry = _cache.CreateEntry(key);
        entry.ExpirationTokens.Add(_signal.Token);
        entry.AbsoluteExpirationRelativeToNow = route is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(30);
        entry.Value = route;

        return route;
    }

    public async Task SyncPageRouteAsync(Guid pageId, CancellationToken ct = default)
    {
        var page = await _db.Pages
            .FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return;

        var culture = await ResolveCultureAsync(null, ct);

        // Trang template KHÔNG có URL riêng: nó chỉ được chọn qua ITemplateResolver khi render một
        // entity. Để nó có route nghĩa là template lộ ra ngoài như một trang trắng (không entity →
        // mọi khối dữ liệu rỗng), trùng nội dung và lọt vào sitemap.
        if (page.Kind.IsTemplate())
        {
            var stale = await _db.SiteRoutes
                .FirstOrDefaultAsync(r => r.RouteType == RouteType.Page && r.TargetId == pageId && r.Culture == culture, ct);

            if (stale is not null)
            {
                _db.SiteRoutes.Remove(stale);
                await _db.SaveChangesAsync(ct);
            }

            // Invalidate VÔ ĐIỀU KIỆN: đổi ParentPageId hay publish một template không đụng tới
            // path nào, nhưng cache "parentPath → template" của TemplateResolver thì phải bỏ.
            Invalidate();
            return;
        }

        var newPath = NormalizePath(page.Slug);

        var existing = await _db.SiteRoutes
            .FirstOrDefaultAsync(r => r.RouteType == RouteType.Page && r.TargetId == pageId && r.Culture == culture, ct);

        if (existing is null)
        {
            _db.SiteRoutes.Add(new SiteRoute
            {
                SiteId = page.SiteId,
                Path = newPath,
                Culture = culture,
                RouteType = RouteType.Page,
                TargetId = pageId,
                IsPrimary = true
            });
        }
        else if (!string.Equals(existing.Path, newPath, StringComparison.OrdinalIgnoreCase))
        {
            // Slug đổi → giữ path cũ bằng Redirect 301, cập nhật route sang path mới.
            _db.Redirects.Add(new Redirect
            {
                SiteId = page.SiteId,
                FromPath = existing.Path,
                ToPath = newPath,
                StatusCode = 301,
                IsActive = true
            });
            existing.Path = newPath;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            return; // không đổi, khỏi lưu
        }

        await _db.SaveChangesAsync(ct);
        Invalidate();
    }

    public async Task SyncEntityRouteAsync(RouteType routeType, Guid entityId, CancellationToken ct = default)
    {
        var contentType = _contentTypes.FindByRouteType(routeType);
        if (contentType is null) return;

        var detail = await contentType.LoadAsync(entityId, ct);
        var culture = await ResolveCultureAsync(null, ct);
        var existing = await _db.SiteRoutes
            .FirstOrDefaultAsync(r => r.RouteType == routeType && r.TargetId == entityId && r.Culture == culture, ct);

        // Entity nháp/chưa publish/đã xoá không được có route: gỡ đi để trả 404 thay vì lộ nội dung chưa xuất bản.
        if (detail is null)
        {
            if (existing is null) return;
            _db.SiteRoutes.Remove(existing);
            await _db.SaveChangesAsync(ct);
            Invalidate();
            return;
        }

        var newPath = NormalizePath(await contentType.BuildPathAsync(entityId, ct));

        if (existing is null)
        {
            _db.SiteRoutes.Add(new SiteRoute
            {
                SiteId = _currentSite.SiteId,
                Path = newPath,
                Culture = culture,
                RouteType = routeType,
                TargetId = entityId,
                IsPrimary = true
            });
        }
        else if (!string.Equals(existing.Path, newPath, StringComparison.OrdinalIgnoreCase))
        {
            // Slug đổi → giữ path cũ bằng Redirect 301, cập nhật route sang path mới.
            _db.Redirects.Add(new Redirect
            {
                SiteId = _currentSite.SiteId,
                FromPath = existing.Path,
                ToPath = newPath,
                StatusCode = 301,
                IsActive = true
            });
            existing.Path = newPath;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            return; // không đổi, khỏi lưu
        }

        await _db.SaveChangesAsync(ct);
        Invalidate();
    }

    public Task SyncPostRouteAsync(Guid postId, CancellationToken ct = default) =>
        SyncEntityRouteAsync(RouteType.Post, postId, ct);

    public Task SyncProductRouteAsync(Guid productId, CancellationToken ct = default) =>
        SyncEntityRouteAsync(RouteType.Product, productId, ct);

    public async Task ResyncProductRoutesAsync(CancellationToken ct = default)
    {
        var productIds = await _db.Products.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == ProductStatus.Published)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var id in productIds)
        {
            await SyncEntityRouteAsync(RouteType.Product, id, ct);
        }
        Invalidate();
    }

    public void Invalidate() => _signal.Invalidate();

    /// <summary>Chuẩn hoá path: thêm "/" đầu, bỏ "/" cuối, lowercase; rỗng → "/".</summary>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        path = path.Trim();
        if (!path.StartsWith('/')) path = "/" + path;
        if (path.Length > 1) path = path.TrimEnd('/');
        return path.ToLowerInvariant();
    }

    private async Task<string> ResolveCultureAsync(string? culture, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(culture)) return culture.Trim().ToLowerInvariant();

        // Chưa truyền culture → lấy DefaultCulture của site hiện tại.
        var siteId = _currentSite.SiteId;
        var def = await _db.Sites.AsNoTracking()
            .Where(s => s.Id == siteId)
            .Select(s => s.DefaultCulture)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(def) ? "vi" : def;
    }
}
