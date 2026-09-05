using NewsCMS.Domain.Common;
using NewsCMS.Domain.Entities.Identity;

namespace NewsCMS.Domain.Entities.Site;

public class Site : BaseEntity
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? PrimaryDomain { get; set; }
    public string DefaultTheme { get; set; } = "Default";
    public bool IsActive { get; set; } = true;

    // ── Đa ngôn ngữ ────────────────────────────────────────────────────────────
    /// <summary>Ngôn ngữ mặc định (ví dụ "vi"). Route không prefix culture là ngôn ngữ này.</summary>
    public string DefaultCulture { get; set; } = "vi";
    /// <summary>Danh sách ngôn ngữ hỗ trợ, CSV (ví dụ "vi,en").</summary>
    public string SupportedCultures { get; set; } = "vi";

    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<SiteFeatureModule> FeatureModules { get; set; } = new List<SiteFeatureModule>();
    public ICollection<SiteDomain> Domains { get; set; } = new List<SiteDomain>();
}
