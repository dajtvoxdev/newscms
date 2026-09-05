using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.ContentTypes;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Tra "URL chi tiết → trang builder nào render nó" (xem <see cref="ITemplateResolver"/>).
///
/// Cache theo (site, culture, kind, parentPath) với IMemoryCache + SiteCacheSignal — đúng pattern
/// <see cref="RouteRegistry.ResolveAsync"/>. Bản thân bước tra trang cha lại gọi
/// <see cref="IRouteRegistry.ResolveAsync"/> (cũng đã cache), nên request nóng không thêm query.
/// </summary>
public sealed class TemplateResolver : ITemplateResolver
{
    private readonly AppDbContext _db;
    private readonly IRouteRegistry _routes;
    private readonly IMemoryCache _cache;
    private readonly SiteCacheSignal _signal;
    private readonly ICurrentSite _currentSite;
    private readonly IContentTypeRegistry _contentTypes;

    public TemplateResolver(
        AppDbContext db,
        IRouteRegistry routes,
        IMemoryCache cache,
        SiteCacheSignal signal,
        ICurrentSite currentSite,
        IContentTypeRegistry contentTypes)
    {
        _db = db;
        _routes = routes;
        _cache = cache;
        _signal = signal;
        _currentSite = currentSite;
        _contentTypes = contentTypes;
    }

    public async Task<ResolvedTemplate?> ForEntityAsync(
        RouteType routeType, Guid entityId, string? path, string culture, CancellationToken ct = default)
    {
        var contentType = _contentTypes.FindByRouteType(routeType);
        if (contentType is null) return null;

        var detail = await _contentTypes.LoadDetailAsync(routeType, entityId, ct);
        if (detail is null) return null;

        var fallbackPath = await contentType.BuildPathAsync(entityId, ct);
        var resolvedPath = await ResolvePathAsync(path, routeType, entityId, culture, fallbackPath, ct);

        Guid? pageId = null;
        if (routeType == RouteType.Category && detail.Extra.TryGetValue("TemplatePageId", out var tplIdObj) && tplIdObj is Guid explicitId)
        {
            if (await IsUsableTemplateAsync(explicitId, ct))
                pageId = explicitId;
        }

        pageId ??= await ResolvePageIdAsync(contentType.TemplateKind, resolvedPath, culture, ct);

        return new ResolvedTemplate(pageId, new RouteContext(
            routeType, detail.Id, detail.Slug, resolvedPath, detail.CategoryId, detail.CategorySlug));
    }

    public Task<ResolvedTemplate?> ForPostAsync(
        Guid postId, string? path, string culture, CancellationToken ct = default) =>
        ForEntityAsync(RouteType.Post, postId, path, culture, ct);

    public Task<ResolvedTemplate?> ForProductAsync(
        Guid productId, string? path, string culture, CancellationToken ct = default) =>
        ForEntityAsync(RouteType.Product, productId, path, culture, ct);

    public Task<ResolvedTemplate?> ForCategoryAsync(
        Guid categoryId, string? path, string culture, CancellationToken ct = default) =>
        ForEntityAsync(RouteType.Category, categoryId, path, culture, ct);

    public async Task<Guid?> ResolvePageIdAsync(
        PageKind kind, string path, string culture, CancellationToken ct = default)
    {
        var normalized = IRouteRegistry.NormalizePath(path);
        var parentPath = ParentPath(normalized);
        var siteId = _currentSite.SiteId;

        var key = $"tpl:{siteId}:{culture}:{kind}:{parentPath ?? "-"}";
        if (_cache.TryGetValue<Guid?>(key, out var cached))
            return cached;

        Guid? pageId = null;

        // (1) Template gắn dưới trang cha suy từ URL: "/tin-tuc/abc" → trang "/tin-tuc".
        if (parentPath is not null)
        {
            var parentPageId = await ResolveParentPageIdAsync(parentPath, culture, ct);
            if (parentPageId is { } parent)
            {
                pageId = await UsableTemplates(kind)
                    .Where(p => p.ParentPageId == parent)
                    .Select(p => (Guid?)p.Id)
                    .FirstOrDefaultAsync(ct);
            }
        }

        // (2) Template dự phòng toàn site cho Kind này.
        pageId ??= await UsableTemplates(kind)
            .Where(p => p.IsDefaultTemplate)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        // Cache cả kết quả rỗng nhưng chỉ 30s: site vừa tạo template phải thấy hiệu lực gần như
        // ngay, kể cả khi luồng lưu nào đó quên gọi Invalidate().
        using var entry = _cache.CreateEntry(key);
        entry.ExpirationTokens.Add(_signal.Token);
        entry.AbsoluteExpirationRelativeToNow =
            pageId is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(30);
        entry.Value = pageId;

        return pageId;
    }

    /// <summary>
    /// Trang cha của một path: "/tin-tuc/abc" → "/tin-tuc", "/tin-tuc" → "/" (trang chủ),
    /// "/" → null (trang chủ không có cha).
    /// </summary>
    public static string? ParentPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return null;
        var idx = path.LastIndexOf('/');
        if (idx < 0) return null;
        return idx == 0 ? "/" : path[..idx];
    }

    /// <summary>
    /// Path → id trang đóng vai trò "cha". Route Page thì chính nó; route Category thì trang
    /// listing của chuyên mục (<c>Category.TemplatePageId</c>) — nhờ vậy site chưa dựng trang
    /// listing riêng vẫn gắn được template chi tiết vào cùng một cây.
    /// </summary>
    private async Task<Guid?> ResolveParentPageIdAsync(string parentPath, string culture, CancellationToken ct)
    {
        var route = await _routes.ResolveAsync(parentPath, culture, ct);
        if (route is null || route.TargetId is not { } targetId) return null;

        if (route.RouteType == RouteType.Page) return targetId;

        if (route.RouteType == RouteType.Category)
        {
            return await _db.Categories.AsNoTracking()
                .Where(c => c.Id == targetId)
                .Select(c => c.TemplatePageId)
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }

    /// <summary>
    /// Template dùng được = đúng Kind, chưa xoá, đã publish và CÓ NỘI DUNG. Điều kiện cuối quan
    /// trọng: chọn phải một template rỗng thì trang chi tiết ra trang trắng, tệ hơn hẳn so với
    /// việc rơi về HTML dựng sẵn.
    /// </summary>
    private IQueryable<Page> UsableTemplates(PageKind kind) => _db.Pages.AsNoTracking()
        .Where(p => p.Kind == kind
                    && !p.IsDeleted
                    && (p.Status == BuilderPageStatus.Published || p.IsPublished)
                    && p.CompiledHtml != null
                    && p.CompiledHtml != "")
        .OrderBy(p => p.CreatedAt);

    private Task<bool> IsUsableTemplateAsync(Guid pageId, CancellationToken ct) =>
        _db.Pages.AsNoTracking().AnyAsync(p =>
            p.Id == pageId
            && !p.IsDeleted
            && (p.Status == BuilderPageStatus.Published || p.IsPublished)
            && p.CompiledHtml != null
            && p.CompiledHtml != "", ct);

    /// <summary>
    /// Path đang phục vụ. Caller (UniversalController) biết path thật nên truyền vào; khi không có
    /// (preview, test, gọi trực tiếp service) thì tra ngược SiteRoutes qua index
    /// IX_SiteRoutes_RouteType_TargetId, cuối cùng mới dựng lại từ slug.
    /// </summary>
    private async Task<string> ResolvePathAsync(
        string? path, RouteType routeType, Guid targetId, string culture,
        string fallback, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(path)) return IRouteRegistry.NormalizePath(path);

        var stored = await _db.SiteRoutes.AsNoTracking()
            .Where(r => r.RouteType == routeType && r.TargetId == targetId && r.Culture == culture)
            .OrderByDescending(r => r.IsPrimary)
            .Select(r => r.Path)
            .FirstOrDefaultAsync(ct);

        return IRouteRegistry.NormalizePath(stored ?? fallback);
    }
}
