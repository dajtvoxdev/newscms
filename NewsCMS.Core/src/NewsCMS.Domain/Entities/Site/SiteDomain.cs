using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Site;

/// <summary>
/// Một host (domain) trỏ về một site. Cho phép nhiều domain/site.
/// Resolver tra host → site (không bị global filter vì đây là bảng mapping cấp hệ thống).
/// </summary>
public class SiteDomain : BaseEntity
{
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = default!;

    /// <summary>Host thuần, không scheme/port. Ví dụ: juiceandflower.io.vn</summary>
    public string Host { get; set; } = default!;

    /// <summary>Domain chính của site (hiển thị/ưu tiên trong admin).</summary>
    public bool IsPrimary { get; set; }
}
