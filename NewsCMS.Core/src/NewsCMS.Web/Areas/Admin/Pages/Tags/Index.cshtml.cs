using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Tags;

[Authorize(Permissions.Content.ManageTag)]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Keyword { get; set; }

    public List<TagRow> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var q = _db.Tags.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(Keyword))
            q = q.Where(t => t.Name.Contains(Keyword));
        Items = await q.OrderBy(t => t.Name)
            .Select(t => new TagRow(t.Id, t.Name, t.Slug, t.PostTags.Count))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var tag = await _db.Tags.Include(t => t.PostTags).FirstOrDefaultAsync(t => t.Id == id);
        if (tag != null)
        {
            _db.PostTags.RemoveRange(tag.PostTags);
            _db.Tags.Remove(tag);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã xoá tag.";
        }
        return RedirectToPage();
    }

    public record TagRow(Guid Id, string Name, string Slug, int PostCount);
}
