using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Posts;

[Authorize(Permissions.Content.ViewPost)]
public class PostIndexModel : PageModel
{
    private readonly AppDbContext _db;
    public PostIndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Keyword { get; set; }
    [BindProperty(SupportsGet = true, Name = "cat")]
    public Guid? CategoryId { get; set; }

    public List<Category> Categories { get; private set; } = new();
    public List<PostRow> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();

        var q = _db.Posts.AsNoTracking().Include(p => p.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Keyword))
            q = q.Where(p => p.Title.Contains(Keyword));
        if (CategoryId.HasValue)
            q = q.Where(p => p.CategoryId == CategoryId);

        Items = await q.OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .Select(p => new PostRow(p.Id, p.Title, p.Category.Name, p.Status.ToString(), p.PublishedAt))
            .ToListAsync();
    }

    public record PostRow(Guid Id, string Title, string CategoryName, string Status, DateTime? PublishedAt);
}
