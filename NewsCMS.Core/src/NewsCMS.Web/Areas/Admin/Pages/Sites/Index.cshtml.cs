using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Areas.Admin.Pages.Sites;

[Authorize(Roles = "SuperAdmin")]
public class IndexModel : PageModel
{
    private readonly AppDbContext db;
    private readonly UserManager<AppUser> userManager;

    public IndexModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        this.db = db;
        this.userManager = userManager;
    }

    public List<SiteRow> Sites { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var sites = await db.Sites
            .Include(x => x.FeatureModules)
            .ThenInclude(x => x.FeatureModule)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var adminUsers = await userManager.Users
            .Where(x => x.SiteId != null)
            .Select(x => new { x.SiteId, x.UserName, x.Email })
            .ToListAsync();

        Sites = sites.Select(site => new SiteRow(
            site.Id,
            site.Name,
            site.Slug,
            site.PrimaryDomain,
            site.IsActive,
            site.FeatureModules
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.FeatureModule.SortOrder)
                .Select(x => x.FeatureModule.Name)
                .ToList(),
            adminUsers
                .Where(x => x.SiteId == site.Id)
                .Select(x => x.Email ?? x.UserName ?? "-")
                .ToList()
        )).ToList();
    }

    public sealed record SiteRow(
        Guid Id,
        string Name,
        string Slug,
        string? PrimaryDomain,
        bool IsActive,
        IReadOnlyList<string> EnabledModules,
        IReadOnlyList<string> Admins);
}
