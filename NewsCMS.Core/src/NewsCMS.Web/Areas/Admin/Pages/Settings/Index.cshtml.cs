using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Settings;

[Authorize(Policy = Permissions.Site.EditSetting)]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<SiteSetting> Items { get; private set; } = new();

    [BindProperty]
    public Dictionary<string, string?> Values { get; set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        foreach (var item in Values)
        {
            var setting = await _db.SiteSettings.FindAsync(item.Key);
            if (setting == null)
            {
                _db.SiteSettings.Add(new SiteSetting { Key = item.Key, Value = item.Value, Group = "general", UpdatedAt = DateTime.UtcNow });
                continue;
            }
            setting.Value = item.Value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã lưu cấu hình.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Items = await _db.SiteSettings.AsNoTracking()
            .OrderBy(s => s.Group)
            .ThenBy(s => s.Key)
            .ToListAsync();
    }
}
