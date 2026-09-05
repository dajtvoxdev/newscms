using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Banners;

[Authorize(Policy = Permissions.Site.ManageBanner)]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<BannerRow> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Items = await _db.Banners
            .AsNoTracking()
            .OrderBy(b => b.Position).ThenBy(b => b.Order)
            .Select(b => new BannerRow(b.Id, b.Title, b.Position, b.Order, b.IsActive, b.StartAt, b.EndAt))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var banner = await _db.Banners.FindAsync(id);
        if (banner != null)
        {
            _db.Banners.Remove(banner);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã xoá banner.";
        }
        return RedirectToPage();
    }

    public record BannerRow(Guid Id, string Title, string Position, int Order, bool IsActive, DateTime? StartAt, DateTime? EndAt);
}
