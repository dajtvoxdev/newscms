using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Categories;

[Authorize(Permissions.Content.ManageCategory)]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<CategoryRow> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Items = await _db.Categories
            .AsNoTracking()
            .Include(c => c.Parent)
            .OrderBy(c => c.Order).ThenBy(c => c.Name)
            .Select(c => new CategoryRow(c.Id, c.Name, c.Slug, c.Parent != null ? c.Parent.Name : null, c.Order, c.IsActive))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var hasPosts = await _db.Posts.AnyAsync(p => p.CategoryId == id);
        if (hasPosts)
        {
            TempData["Error"] = "Không thể xoá chuyên mục đang có bài viết.";
            return RedirectToPage();
        }
        var cat = await _db.Categories.FindAsync(id);
        if (cat != null)
        {
            _db.Categories.Remove(cat);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã xoá chuyên mục.";
        }
        return RedirectToPage();
    }

    public record CategoryRow(Guid Id, string Name, string Slug, string? ParentName, int Order, bool IsActive);
}
