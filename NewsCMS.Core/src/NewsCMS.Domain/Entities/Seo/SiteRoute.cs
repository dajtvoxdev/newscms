using NewsCMS.Domain.Common;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Domain.Entities.Seo;

/// <summary>
/// Ánh xạ path → entity cho theme Universal. IRouteRegistry.ResolveAsync tra bảng này theo
/// (SiteId, Culture, Path) để biết phải render gì. Path chuẩn hoá: bắt đầu "/", không "/" cuối,
/// lowercase. Sinh tự động từ entity (SyncAsync) — không sửa tay trừ khi route custom.
/// </summary>
public class SiteRoute : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    /// <summary>Path chuẩn hoá, ví dụ "/gioi-thieu", "/tin-tuc/bai-viet-a". Riêng trang chủ là "/".</summary>
    public string Path { get; set; } = default!;

    /// <summary>Mã ngôn ngữ (ví dụ "vi", "en"). Ngôn ngữ mặc định của site dùng Culture mặc định.</summary>
    public string Culture { get; set; } = default!;

    public RouteType RouteType { get; set; }
    /// <summary>Id của entity đích (Page/Category/Post/Product). Route Custom có thể null.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Đánh dấu route chính khi một entity có nhiều path (canonical là cái IsPrimary).</summary>
    public bool IsPrimary { get; set; } = true;
}
