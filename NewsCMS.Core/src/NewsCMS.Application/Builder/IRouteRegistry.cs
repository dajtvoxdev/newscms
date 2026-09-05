using NewsCMS.Domain.Enums;

namespace NewsCMS.Application.Builder;

/// <summary>
/// Kết quả resolve một path thành route. Renderer dựa vào RouteType + TargetId để nạp entity
/// (Page/Category/Post/Product) và render.
/// </summary>
public sealed record ResolvedRoute(
    Guid SiteId,
    string Path,
    string Culture,
    RouteType RouteType,
    Guid? TargetId,
    bool IsPrimary);

/// <summary>
/// Tra path → SiteRoute cho theme Universal. Cache nội bộ (IMemoryCache + SiteCacheSignal) theo
/// đúng pattern SiteResolver. SyncAsync sinh lại SiteRoute khi entity đổi slug; đổi slug tự tạo
/// Redirect 301 từ path cũ (RedirectMiddleware xử lý).
/// </summary>
public interface IRouteRegistry
{
    /// <summary>
    /// Resolve path (đã chuẩn hoá) trong site + culture hiện tại. Trả null nếu không có route
    /// (caller trả 404). Culture null → dùng DefaultCulture của site.
    /// </summary>
    Task<ResolvedRoute?> ResolveAsync(string path, string? culture = null, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ route cho một page: đảm bảo SiteRoute khớp slug hiện tại; slug đổi → tạo Redirect 301
    /// từ path cũ. Gọi sau khi lưu/publish page.
    /// </summary>
    Task SyncPageRouteAsync(Guid pageId, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ route cho một bài viết: path = "/{slug-chuyên-mục}/{slug-bài}". Bài chưa publish
    /// hoặc đã xoá thì route bị gỡ. Gọi sau khi lưu bài ở admin.
    /// </summary>
    Task SyncPostRouteAsync(Guid postId, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ route cho một sản phẩm: path = "/san-pham/{slug}". Sản phẩm chưa publish
    /// hoặc đã xoá thì route bị gỡ. Gọi sau khi lưu/toggle publish/xoá sản phẩm ở admin.
    /// </summary>
    Task SyncProductRouteAsync(Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ route hợp nhất cho mọi loại thực thể (Post, Product, Event...) dựa trên Content Type Registry.
    /// </summary>
    Task SyncEntityRouteAsync(RouteType routeType, Guid entityId, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ lại route cho toàn bộ sản phẩm của site (ví dụ khi cấu hình catalog:detailPrefix hoặc PathSlug thay đổi).
    /// </summary>
    Task ResyncProductRoutesAsync(CancellationToken ct = default);

    /// <summary>Bỏ cache route của site hiện tại (gọi khi đổi route/slug).</summary>
    void Invalidate();

    /// <summary>Chuẩn hoá path: thêm "/" đầu, bỏ "/" cuối, lowercase; rỗng → "/".</summary>
    static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        path = path.Trim();
        if (!path.StartsWith('/')) path = "/" + path;
        if (path.Length > 1) path = path.TrimEnd('/');
        return path.ToLowerInvariant();
    }
}
