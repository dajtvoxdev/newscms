namespace NewsCMS.Domain.Enums;

/// <summary>Loại trang dựng bằng builder — quyết định template/route mặc định.</summary>
public enum PageKind
{
    Landing = 0,
    Static = 1,
    CategoryTemplate = 2,
    PostTemplate = 3,
    ArchiveTemplate = 4,
    SystemError = 5,
    ProductTemplate = 6
}

public static class PageKindExtensions
{
    /// <summary>
    /// Trang template KHÔNG có URL riêng: nó chỉ được chọn khi render một entity
    /// (bài viết / sản phẩm / chuyên mục) qua <c>ITemplateResolver</c>. Vì vậy
    /// <c>RouteRegistry.SyncPageRouteAsync</c> không sinh SiteRoute cho nó — có route
    /// nghĩa là template lộ ra như một trang trắng, trùng nội dung và lọt vào sitemap.
    /// </summary>
    public static bool IsTemplate(this PageKind kind) => kind
        is PageKind.CategoryTemplate
        or PageKind.PostTemplate
        or PageKind.ProductTemplate
        or PageKind.ArchiveTemplate;
}
