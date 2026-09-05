namespace NewsCMS.Domain.Entities.Site;

public class SiteFeatureModule
{
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = default!;
    public Guid FeatureModuleId { get; set; }
    public FeatureModule FeatureModule { get; set; } = default!;
    public bool IsEnabled { get; set; }
    public DateTime? EnabledAt { get; set; }
}
