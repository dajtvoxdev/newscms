using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Menus;

[Authorize(Policy = Permissions.Site.ManageMenu)]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<MenuRow> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Items = await _db.Menus
            .AsNoTracking()
            .Include(m => m.Items)
            .OrderBy(m => m.Location)
            .Select(m => new MenuRow(m.Id, m.Name, m.Location, m.Items.Count))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var menu = await _db.Menus.Include(m => m.Items).FirstOrDefaultAsync(m => m.Id == id);
        if (menu != null)
        {
            _db.MenuItems.RemoveRange(menu.Items);
            _db.Menus.Remove(menu);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã xoá menu.";
        }
        return RedirectToPage();
    }

    public record MenuRow(Guid Id, string Name, string Location, int ItemCount);
}
