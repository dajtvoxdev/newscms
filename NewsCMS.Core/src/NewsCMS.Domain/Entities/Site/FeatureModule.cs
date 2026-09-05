using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Site;

public class FeatureModule : BaseEntity
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public ICollection<SiteFeatureModule> Sites { get; set; } = new List<SiteFeatureModule>();
}
