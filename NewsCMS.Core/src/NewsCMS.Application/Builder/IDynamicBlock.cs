using NewsCMS.Domain.Enums;

namespace NewsCMS.Application.Builder;

/// <summary>
/// Entity mà URL hiện tại đang trỏ tới, khi trang được render từ một TEMPLATE thay vì từ nội dung
/// tĩnh của chính nó — ví dụ <c>/tin-tuc/cold-brew-hat-cau-dat</c> render bằng template chi tiết
/// bài viết. Không có ngữ cảnh này thì khối động không thể biết phải hiển thị bài nào, nên trang
/// chi tiết buộc phải là HTML hardcode trong C#.
///
/// Null với trang thường (landing/tĩnh) — khối cần entity phải tự xử lý trường hợp đó bằng
/// placeholder, KHÔNG được throw (xem hợp đồng của <see cref="IDynamicBlock.RenderAsync"/>).
/// </summary>
/// <param name="Path">Path chuẩn hoá của URL đang phục vụ, vd "/tin-tuc/cold-brew-hat-cau-dat".</param>
/// <param name="CategoryId">Chuyên mục của entity; null với chính route chuyên mục hoặc entity không có.</param>
public sealed record RouteContext(
    RouteType RouteType,
    Guid EntityId,
    string Slug,
    string Path,
    Guid? CategoryId = null,
    string? CategorySlug = null);

/// <summary>
/// Ngữ cảnh truyền vào dynamic block khi render: site hiện tại, culture, và props từ builder.
/// </summary>
public sealed record DynamicBlockContext(
    Guid SiteId,
    string Culture,
    /// <summary>Props JSON từ builder (block definition PropsSchemaJson mô tả schema).</summary>
    string? PropsJson,
    /// <summary>Entity của URL hiện tại khi đây là trang chi tiết; null với trang thường.</summary>
    RouteContext? Route = null);

/// <summary>
/// Interface cho khối động — mỗi block đăng ký một key (post-list, product-grid...) và
/// tự render HTML server-side dựa trên props. Registry theo pattern IAiTool/AiToolRegistry.
/// </summary>
public interface IDynamicBlock
{
    /// <summary>Key ổn định khớp BlockDefinition.DynamicHandler (ví dụ "post-list").</summary>
    string Key { get; }

    /// <summary>
    /// Mô tả khối cho builder UI và agent MCP: nhãn, icon, preset, danh sách prop.
    /// Bắt buộc để catalog chỉ còn MỘT nguồn sự thật — xem <see cref="BlockDescriptor"/>.
    /// </summary>
    BlockDescriptor Descriptor { get; }

    /// <summary>Render HTML của block. Trả chuỗi rỗng nếu không có dữ liệu (không được throw).</summary>
    Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default);
}
