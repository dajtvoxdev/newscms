namespace NewsCMS.Domain.Common;

/// <summary>
/// Đánh dấu entity thuộc về một site cụ thể (multi-tenant theo SiteId).
/// AppDbContext áp global query filter + tự stamp SiteId khi thêm mới.
/// </summary>
public interface ISiteScoped
{
    Guid SiteId { get; set; }
}
