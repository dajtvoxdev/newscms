using NewsCMS.Domain.Enums;

namespace NewsCMS.Application.Builder;

/// <summary>
/// Dữ liệu chuẩn hoá của một thực thể nội dung (bài viết, sản phẩm, chuyên mục, sự kiện...)
/// dùng chung cho các khối trường dữ liệu (entity-*) và renderer trang chi tiết.
/// Loại bỏ hoàn toàn sự phụ thuộc vào từng bảng CSDL cụ thể.
/// </summary>
/// <param name="Id">ID thực thể.</param>
/// <param name="Slug">Slug URL của thực thể.</param>
/// <param name="Title">Tiêu đề / Tên thực thể.</param>
/// <param name="Body">Nội dung chi tiết (HTML bài viết hoặc mô tả sản phẩm).</param>
/// <param name="Excerpt">Mô tả ngắn hoặc sapo mở đầu.</param>
/// <param name="ImageUrl">URL ảnh đại diện tiêu biểu.</param>
/// <param name="ImageWidth">Chiều rộng ảnh (nếu có).</param>
/// <param name="ImageHeight">Chiều cao ảnh (nếu có).</param>
/// <param name="ImageAlt">Văn bản thay thế (alt text) của ảnh.</param>
/// <param name="PublishedAt">Thời điểm xuất bản.</param>
/// <param name="CategoryId">ID chuyên mục trực tiếp.</param>
/// <param name="CategorySlug">Slug của chuyên mục.</param>
/// <param name="CategoryName">Tên hiển thị của chuyên mục.</param>
/// <param name="Extra">Các thuộc tính mở rộng đặc thù của loại thực thể (giá, tồn kho, ảnh bổ sung, metadata...).</param>
public sealed record ContentDetail(
    Guid Id,
    string Slug,
    string Title,
    string? Body,
    string? Excerpt,
    string? ImageUrl,
    int? ImageWidth,
    int? ImageHeight,
    string? ImageAlt,
    DateTime? PublishedAt,
    Guid? CategoryId,
    string? CategorySlug,
    string? CategoryName,
    IReadOnlyDictionary<string, object?> Extra);

/// <summary>
/// Khai báo một loại nội dung (Content Type) trong hệ thống Site Builder.
/// Cho phép thêm mới loại nội dung (ví dụ: Event, Course, Portfolio...) mà không cần
/// phải sửa switch-case rải rác trong các khối trường dữ liệu hay renderer.
/// </summary>
public interface IContentType
{
    /// <summary>Khóa định danh dạng chuỗi (vd: "post", "product", "category").</summary>
    string Key { get; }

    /// <summary>Tên hiển thị tiếng Việt (vd: "Bài viết", "Sản phẩm", "Chuyên mục").</summary>
    string DisplayName { get; }

    /// <summary>Loại route tương ứng trong SiteRoutes.</summary>
    RouteType RouteType { get; }

    /// <summary>Loại template trang chi tiết trong PageKind (PostTemplate, ProductTemplate, CategoryTemplate...).</summary>
    PageKind TemplateKind { get; }

    /// <summary>Tải dữ liệu chuẩn hoá của thực thể theo Id. Trả về null nếu không tìm thấy hoặc chưa được xuất bản.</summary>
    Task<ContentDetail?> LoadAsync(Guid id, CancellationToken ct = default);

    /// <summary>Xây dựng URL path chuẩn hoá của thực thể (vd: "/tin-tuc/bai-viet-1" hoặc "/san-pham/ca-phe").</summary>
    Task<string> BuildPathAsync(Guid id, CancellationToken ct = default);

    /// <summary>HTML dựng sẵn dự phòng khi trang chưa có template builder (trả về null nếu loại này không có HTML dự phòng).</summary>
    Task<string?> RenderFallbackHtmlAsync(ContentDetail detail, CancellationToken ct = default);
}

/// <summary>
/// Registry quản lý và tra cứu các Content Type đã đăng ký trong Service Container,
/// tích hợp bộ đệm per-request (scoped cache) cho ContentDetail.
/// </summary>
public interface IContentTypeRegistry
{
    /// <summary>Danh sách tất cả loại nội dung đã đăng ký.</summary>
    IReadOnlyList<IContentType> All { get; }

    /// <summary>Tìm loại nội dung theo Key (không phân biệt hoa thường).</summary>
    IContentType? FindByKey(string key);

    /// <summary>Tìm loại nội dung theo RouteType.</summary>
    IContentType? FindByRouteType(RouteType routeType);

    /// <summary>Tìm loại nội dung theo PageKind của template.</summary>
    IContentType? FindByTemplateKind(PageKind kind);

    /// <summary>
    /// Tải dữ liệu chuẩn hoá của thực thể theo RouteType và EntityId, có lưu vào bộ đệm scoped của request.
    /// Giúp trang chứa 5-6 khối entity (title, image, meta, content, excerpt) chỉ query CSDL 1 lần duy nhất.
    /// </summary>
    Task<ContentDetail?> LoadDetailAsync(RouteType routeType, Guid entityId, CancellationToken ct = default);
}

