using NewsCMS.Domain.Enums;

namespace NewsCMS.Application.Builder;

/// <summary>
/// Kết quả tra template cho một URL chi tiết.
/// </summary>
/// <param name="PageId">
/// Trang builder dùng làm template, hoặc <c>null</c> khi site chưa dựng template nào cho loại này —
/// khi đó renderer quay về HTML dựng sẵn trong C# (đường cũ, không đổi hành vi).
/// </param>
/// <param name="Route">
/// Ngữ cảnh entity của URL. LUÔN có giá trị kể cả khi <paramref name="PageId"/> null, vì shell
/// (SiteLayout) cũng chạy qua DynamicBlockRenderer và khối breadcrumb trong shell vẫn cần biết
/// đang ở path nào.
/// </param>
public sealed record ResolvedTemplate(Guid? PageId, RouteContext Route);

/// <summary>
/// Tra "URL chi tiết → trang builder nào render nó". Đây là mắt xích còn thiếu khiến trang chi tiết
/// bài viết/sản phẩm không dựng được bằng builder: renderer trước đây nhảy thẳng từ RouteType sang
/// một hàm C# cố định, bỏ qua toàn bộ tầng <c>Page</c>.
///
/// Mô hình: template là TRANG CON của trang listing (<c>Page.ParentPageId</c>). Template gắn dưới
/// trang <c>/tin-tuc</c> phục vụ mọi URL <c>/tin-tuc/{slug}</c> — "trang cấp 3 suy ra từ URL".
/// Prefix không lưu ở đâu cả, nó là slug của trang cha, nên không có nguồn sự thật thứ hai để lệch.
///
/// Thứ tự tìm (dừng ở cái đầu tiên dùng được):
/// <list type="number">
///   <item>Template có <c>ParentPageId</c> = trang cha suy từ path;</item>
///   <item>Template có <c>IsDefaultTemplate</c> cho <c>Kind</c> đó (dự phòng toàn site);</item>
///   <item>Không có → <c>PageId = null</c>, renderer dùng HTML dựng sẵn.</item>
/// </list>
///
/// Cache theo <c>(site, culture, kind, parentPath)</c> bằng IMemoryCache + SiteCacheSignal, cùng
/// pattern <see cref="IRouteRegistry"/> — request nóng không thêm query nào.
/// </summary>
public interface ITemplateResolver
{
    /// <summary>
    /// Template chi tiết bài viết. <paramref name="path"/> null → tra ngược từ SiteRoutes
    /// (index IX_SiteRoutes_RouteType_TargetId), rồi mới dựng từ slug chuyên mục nếu vẫn không có.
    /// Trả null khi bài không tồn tại.
    /// </summary>
    Task<ResolvedTemplate?> ForPostAsync(Guid postId, string? path, string culture, CancellationToken ct = default);

    /// <summary>Template chi tiết sản phẩm. Trả null khi sản phẩm không tồn tại.</summary>
    Task<ResolvedTemplate?> ForProductAsync(Guid productId, string? path, string culture, CancellationToken ct = default);

    /// <summary>
    /// Template trang chuyên mục (listing). Ưu tiên <c>Category.TemplatePageId</c> — cột đã có
    /// trong schema từ đầu và đúng nghĩa "template listing của chuyên mục này" — trước khi tìm
    /// theo trang cha / template mặc định. Trả null khi chuyên mục không tồn tại.
    /// </summary>
    Task<ResolvedTemplate?> ForCategoryAsync(Guid categoryId, string? path, string culture, CancellationToken ct = default);

    /// <summary>
    /// Tra template hợp nhất cho mọi loại thực thể (Post, Product, Category...) dựa trên Content Type Registry.
    /// Cho phép mở rộng loại nội dung mới mà không cần thêm phương thức ForXxxAsync riêng.
    /// </summary>
    Task<ResolvedTemplate?> ForEntityAsync(RouteType routeType, Guid entityId, string? path, string culture, CancellationToken ct = default);

    /// <summary>
    /// Tra template theo <c>Kind</c> cho một path bất kỳ — dùng cho preview trong builder và cho
    /// test. Trả null nếu không có template dùng được.
    /// </summary>
    Task<Guid?> ResolvePageIdAsync(PageKind kind, string path, string culture, CancellationToken ct = default);
}
