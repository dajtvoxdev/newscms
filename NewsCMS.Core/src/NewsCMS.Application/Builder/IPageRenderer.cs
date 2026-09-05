using NewsCMS.Domain.Enums;

namespace NewsCMS.Application.Builder;

/// <summary>
/// Trang đã render hoàn chỉnh từ Layout (shell) ⊕ Page. UniversalController chỉ việc trả Html về.
/// </summary>
public sealed record RenderedPage(
    /// <summary>HTML tài liệu đầy đủ (đã ghép layout + nội dung page + CSS/JS).</summary>
    string Html,
    /// <summary>Tiêu đề trang (cho <title>).</summary>
    string Title,
    /// <summary>HTTP cache tag để OutputCache purge theo site, ví dụ "site:{id}".</summary>
    string CacheTag);

/// <summary>
/// Render một trang builder thành HTML đầy đủ: ghép Layout shell với CompiledHtml của page,
/// chèn CSS (token + CompiledCss của page/layout) và CustomJs. Không truy cập trực tiếp theme RCL.
/// </summary>
public interface IPageRenderer
{
    /// <summary>
    /// Render page theo culture. Trả null nếu page không tồn tại / chưa publish / bị xoá
    /// (caller trả 404). Culture dùng để chọn bản dịch (fallback bản gốc nếu chưa có).
    /// </summary>
    Task<RenderedPage?> RenderAsync(Guid pageId, string culture, CancellationToken ct = default);

    /// <summary>
    /// Render trang chi tiết bài viết (module tin tức). Ưu tiên template builder tra qua
    /// <see cref="ITemplateResolver"/>; site chưa dựng template thì dùng HTML dựng sẵn trong
    /// shell mặc định như trước. Trả null nếu bài không tồn tại / chưa publish / bị xoá.
    /// </summary>
    /// <param name="path">
    /// Path đang phục vụ, để suy trang cha ("/tin-tuc/abc" → "/tin-tuc") và cho breadcrumb.
    /// Null thì tra ngược từ SiteRoutes.
    /// </param>
    Task<RenderedPage?> RenderPostAsync(Guid postId, string culture, string? path = null, CancellationToken ct = default);

    /// <summary>
    /// Render trang chi tiết sản phẩm (catalog). Cùng cơ chế template như
    /// <see cref="RenderPostAsync"/>. Trả null nếu sản phẩm không tồn tại / chưa publish / bị xoá.
    /// </summary>
    Task<RenderedPage?> RenderProductAsync(Guid productId, string culture, string? path = null, CancellationToken ct = default);

    /// <summary>
    /// Render trang chuyên mục (listing). Chỉ render được khi site có template
    /// (<c>Category.TemplatePageId</c> hoặc trang <c>Kind = CategoryTemplate</c>) — không có
    /// template thì trả null và caller trả 404, đúng hành vi trước đây của route Category.
    /// </summary>
    Task<RenderedPage?> RenderCategoryAsync(Guid categoryId, string culture, string? path = null, CancellationToken ct = default);

    /// <summary>
    /// Render trang chi tiết hợp nhất cho mọi loại thực thể (Post, Product, Category, Event...) dựa trên Content Type Registry.
    /// </summary>
    Task<RenderedPage?> RenderEntityAsync(RouteType routeType, Guid entityId, string culture, string? path = null, CancellationToken ct = default);

    /// <summary>
    /// Render xem trước một trang template cụ thể với thực thể mẫu.
    /// </summary>
    Task<RenderedPage?> RenderTemplatePreviewAsync(Guid templatePageId, RouteType routeType, Guid entityId, string culture, CancellationToken ct = default);
}
