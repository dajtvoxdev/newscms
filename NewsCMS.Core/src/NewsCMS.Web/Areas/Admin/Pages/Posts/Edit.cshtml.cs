using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Domain.Enums;
using NewsCMS.Application.Builder;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Posts;

[Authorize(Permissions.Content.EditPost)]
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ContentSanitizer _sanitizer;
    private readonly SlugHelper _slug;
    private readonly UserManager<AppUser> _users;
    private readonly IRouteRegistry _routes;

    public EditModel(AppDbContext db, ContentSanitizer sanitizer, SlugHelper slug, UserManager<AppUser> users, IRouteRegistry routes)
    {
        _db = db;
        _sanitizer = sanitizer;
        _slug = slug;
        _users = users;
        _routes = routes;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Category> Categories { get; private set; } = new();
    public long ViewCount { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? UpdatedAtLocal { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var post = await _db.Posts.FindAsync(id);
        if (post is null) return NotFound();

        Categories = await _db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        ViewCount = post.ViewCount;
        UpdatedAt = post.UpdatedAt;
        UpdatedAtLocal = ToVietnamTime(post.UpdatedAt);

        string? featuredUrl = null;
        if (post.FeaturedImageId.HasValue)
            featuredUrl = await _db.Medias.Where(m => m.Id == post.FeaturedImageId)
                .Select(m => m.FilePath).FirstOrDefaultAsync();

        Input = new InputModel
        {
            Id = post.Id,
            Title = post.Title,
            Slug = post.Slug,
            Excerpt = post.Excerpt,
            Content = post.Content,
            CategoryId = post.CategoryId,
            FeaturedImageUrl = featuredUrl,
            IsFeatured = post.IsFeatured,
            Status = post.Status,
            PublishedAt = ToVietnamTime(post.PublishedAt)
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Categories = await _db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        if (!ModelState.IsValid) return Page();

        var post = await _db.Posts.FindAsync(Input.Id);
        if (post is null) return NotFound();

        // Nếu slug thay đổi, đảm bảo duy nhất
        var newSlug = _slug.Generate(string.IsNullOrWhiteSpace(Input.Slug) ? Input.Title : Input.Slug);
        if (newSlug != post.Slug)
            newSlug = await EnsureUniqueSlugAsync(newSlug, exceptId: post.Id);

        post.Title = Input.Title.Trim();
        post.Slug = newSlug;
        post.Excerpt = Input.Excerpt?.Trim();
        post.Content = _sanitizer.Sanitize(Input.Content ?? string.Empty);
        post.CategoryId = Input.CategoryId;
        post.Status = Input.Status;
        post.PublishedAt = Input.Status == PostStatus.Published && Input.PublishedAt is null
            ? DateTime.UtcNow
            : Input.PublishedAt?.ToUniversalTime();
        post.IsFeatured = Input.IsFeatured;
        post.UpdatedAt = DateTime.UtcNow;
        post.UpdatedBy = Guid.Parse(_users.GetUserId(User)!);

        if (!string.IsNullOrWhiteSpace(Input.FeaturedImageUrl))
        {
            var media = await _db.Medias.FirstOrDefaultAsync(m => m.FilePath == Input.FeaturedImageUrl);
            if (media != null) post.FeaturedImageId = media.Id;
        }
        else
        {
            post.FeaturedImageId = null;
        }

        await _db.SaveChangesAsync();

        // Slug/trạng thái đổi → cập nhật (hoặc gỡ) route bài viết, kèm redirect 301 nếu đổi slug.
        await _routes.SyncPostRouteAsync(post.Id);

        TempData["Success"] = "Đã lưu thay đổi.";
        return RedirectToPage(new { id = post.Id });
    }

    [Authorize(Permissions.Content.DeletePost)]
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var post = await _db.Posts.FindAsync(id);
        if (post is null) return NotFound();
        post.IsDeleted = true;
        post.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _routes.SyncPostRouteAsync(post.Id);
        return RedirectToPage("/Posts/Index");
    }

    private static DateTime? ToVietnamTime(DateTime? value)
    {
        if (value is null) return null;
        return DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).AddHours(7);
    }

    private async Task<string> EnsureUniqueSlugAsync(string baseSlug, Guid? exceptId = null)
    {
        var slug = baseSlug; int i = 1;
        while (await _db.Posts.AnyAsync(p => p.Slug == slug && (exceptId == null || p.Id != exceptId)))
            slug = $"{baseSlug}-{++i}";
        return slug;
    }

    public class InputModel
    {
        public Guid Id { get; set; }
        [Required, StringLength(300, MinimumLength = 3)] public string Title { get; set; } = default!;
        [StringLength(320)] public string? Slug { get; set; }
        [StringLength(500)] public string? Excerpt { get; set; }
        public string? Content { get; set; }
        [Required] public Guid CategoryId { get; set; }
        public string? FeaturedImageUrl { get; set; }
        public bool IsFeatured { get; set; }
        public PostStatus Status { get; set; }
        public DateTime? PublishedAt { get; set; }
    }
}
