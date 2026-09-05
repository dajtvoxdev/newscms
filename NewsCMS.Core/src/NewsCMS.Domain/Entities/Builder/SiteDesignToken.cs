using NewsCMS.Domain.Common;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Domain.Entities.Builder;

/// <summary>
/// Design token của site — nguồn duy nhất sinh ra cả CSS variables lẫn khối @theme Tailwind
/// (xem DesignTokenCssBuilder). Đổi một token là đổi toàn site ở cả hai đường Style Manager
/// (var(--color-brand-500)) và utility (bg-brand-500).
/// </summary>
public class SiteDesignToken : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    public DesignTokenGroup Group { get; set; } = DesignTokenGroup.Color;

    /// <summary>Tên token không tiền tố, ví dụ "brand-500", "display", "card".</summary>
    public string Key { get; set; } = default!;
    /// <summary>Giá trị CSS, ví dụ "#0d7c66", ""Be Vietnam Pro", sans-serif", "14px".</summary>
    public string Value { get; set; } = default!;

    public int SortOrder { get; set; }
}
