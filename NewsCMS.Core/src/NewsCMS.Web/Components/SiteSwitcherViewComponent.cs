using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Components;

/// <summary>
/// Bộ chuyển site trên header admin. Chỉ hiện cho SuperAdmin (admin gắn site bị khóa cứng).
/// Đổi site set cookie AdminSiteId, AdminSiteScopeMiddleware sẽ re-scope request kế tiếp.
/// </summary>
public sealed class SiteSwitcherViewComponent : ViewComponent
{
    private readonly AppDbContext _db;
    private readonly ICurrentSite _currentSite;

    public SiteSwitcherViewComponent(AppDbContext db, ICurrentSite currentSite)
    {
        _db = db;
        _currentSite = currentSite;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var isSuperAdmin = UserClaimsPrincipal?.IsInRole("SuperAdmin") == true;
        if (!isSuperAdmin)
            return Content(string.Empty);

        var sites = await _db.Sites
            .IgnoreQueryFilters()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new SiteOption(s.Id, s.Name, s.Slug))
            .ToListAsync();

        var model = new SiteSwitcherModel(sites, _currentSite.SiteId, _currentSite.Slug);
        return View(model);
    }

    public sealed record SiteOption(Guid Id, string Name, string Slug);
    public sealed record SiteSwitcherModel(IReadOnlyList<SiteOption> Sites, Guid CurrentSiteId, string CurrentSlug);
}
