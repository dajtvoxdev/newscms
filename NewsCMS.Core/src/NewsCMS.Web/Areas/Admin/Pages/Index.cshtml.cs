using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Analytics;
using NewsCMS.Application.Analytics.Dtos;
using NewsCMS.Application.Engagement;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Areas.Admin.Pages;

[Authorize]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IVisitorTrackingService _tracking;
    private readonly ICommitmentService _commitments;
    private readonly ICurrentSite _currentSite;

    public IndexModel(AppDbContext db, IVisitorTrackingService tracking, ICommitmentService commitments, ICurrentSite currentSite)
    {
        _db = db;
        _tracking = tracking;
        _commitments = commitments;
        _currentSite = currentSite;
    }

    public bool ShowNews { get; private set; }
    public bool ShowProducts { get; private set; }
    public bool ShowCommitments { get; private set; }
    public bool ShowForms { get; private set; }

    public int TotalPosts { get; private set; }
    public int TotalCategories { get; private set; }
    public int TotalProducts { get; private set; }
    public int TotalProductCategories { get; private set; }
    public int TotalUsers { get; private set; }
    public int TotalSubmissions { get; private set; }
    public VisitorStatsDto Visitors { get; private set; } = new(0, 0, 0, 0);
    public int TotalCommitments { get; private set; }
    public List<RecentPost> RecentPosts { get; private set; } = new();
    public List<RecentProduct> RecentProducts { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var enabledModules = await _db.SiteFeatureModules
            .AsNoTracking()
            .Include(x => x.FeatureModule)
            .Where(x => x.SiteId == _currentSite.SiteId && x.IsEnabled)
            .Select(x => x.FeatureModule.Code)
            .ToListAsync(ct);

        var modules = DashboardModuleState.FromEnabledModules(enabledModules);
        ShowNews = modules.ShowNews;
        ShowProducts = modules.ShowProducts;
        ShowCommitments = modules.ShowCommitments;
        ShowForms = modules.ShowForms;

        TotalUsers = await _db.Users.CountAsync(u => u.SiteId == _currentSite.SiteId, ct);
        Visitors = await _tracking.GetStatsAsync(ct);

        if (ShowNews)
            await LoadNewsStatsAsync(ct);

        if (ShowProducts)
            await LoadProductStatsAsync(ct);

        if (ShowForms)
            TotalSubmissions = await _db.FormSubmissions.CountAsync(ct);

        if (ShowCommitments)
            TotalCommitments = await _commitments.GetTotalCountAsync(ct);
    }

    public async Task<IActionResult> OnGetVisitorStatsAsync(CancellationToken ct)
    {
        var stats = await _tracking.GetStatsAsync(ct);
        return new JsonResult(new
        {
            onlineNow = stats.OnlineNow,
            todayUnique = stats.TodayUnique,
            total7Days = stats.Total7Days,
            totalAllTime = stats.TotalAllTime,
            updatedAt = DateTime.UtcNow
        });
    }

    private async Task LoadNewsStatsAsync(CancellationToken ct)
    {
        TotalPosts = await _db.Posts.CountAsync(ct);
        TotalCategories = await _db.Categories.CountAsync(ct);

        RecentPosts = await _db.Posts
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .Select(p => new RecentPost(p.Title, p.Category.Name, p.Status.ToString(), p.CreatedAt))
            .ToListAsync(ct);
    }

    private async Task LoadProductStatsAsync(CancellationToken ct)
    {
        TotalProducts = await _db.Products.CountAsync(ct);
        TotalProductCategories = await _db.ProductCategories.CountAsync(ct);

        RecentProducts = await _db.Products
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .Select(p => new RecentProduct(p.Name, p.ProductCategory.Name, p.Status.ToString(), p.CreatedAt))
            .ToListAsync(ct);
    }

    public record RecentPost(string Title, string CategoryName, string Status, DateTime CreatedAt);
    public record RecentProduct(string Name, string CategoryName, string Status, DateTime CreatedAt);
}
