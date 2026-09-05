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

[Authorize(Permissions.Content.CreatePost)]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ContentSanitizer _sanitizer;
    private readonly SlugHelper _slug;
    private readonly UserManager<AppUser> _users;
    private readonly IRouteRegistry _routes;

    public CreateModel(AppDbContext db, ContentSanitizer sanitizer, SlugHelper slug, UserManager<AppUser> users, IRouteRegistry routes)
    {
        _db = db;
        _sanitizer = sanitizer;
        _slug = slug;
        _users = users;
        _routes = routes;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Category> Categories { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Categories = await _db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        Input.Status = PostStatus.Draft;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Categories = await _db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();

        if (!ModelState.IsValid) return Page();

        // Slug: nếu user bỏ trống thì sinh từ tiêu đề; sau đó đảm bảo duy nhất
        var slug = string.IsNullOrWhiteSpace(Input.Slug) ? _slug.Generate(Input.Title) : _slug.Generate(Input.Slug);
        slug = await EnsureUniqueSlugAsync(slug);

        var userId = Guid.Parse(_users.GetUserId(User)!);

        var post = new Post
        {
            Title = Input.Title.Trim(),
            Slug = slug,
            Excerpt = Input.Excerpt?.Trim(),
            // Sanitize HTML từ TinyMCE - chống XSS
            Content = _sanitizer.Sanitize(Input.Content ?? string.Empty),
            CategoryId = Input.CategoryId,
            Status = Input.Status,
            PublishedAt = Input.Status == PostStatus.Published && Input.PublishedAt is null
                ? DateTime.UtcNow
                : Input.PublishedAt?.ToUniversalTime(),
            IsFeatured = Input.IsFeatured,
            AuthorId = userId,
            CreatedBy = userId
        };

        // Ảnh đại diện - đã upload qua Media.Upload và FeaturedImageUrl mang URL trả về
        if (!string.IsNullOrWhiteSpace(Input.FeaturedImageUrl))
        {
            var media = await _db.Medias.FirstOrDefaultAsync(m => m.FilePath == Input.FeaturedImageUrl);
            if (media != null) post.FeaturedImageId = media.Id;
        }

        _db.Posts.Add(post);
        await _db.SaveChangesAsync();

        // Sinh SiteRoute /{chuyên-mục}/{slug} để trang chi tiết bài viết truy cập được ngay.
        await _routes.SyncPostRouteAsync(post.Id);

        TempData["Success"] = "Đã tạo bài viết.";
        return RedirectToPage("/Posts/Edit", new { id = post.Id });
    }

    private async Task<string> EnsureUniqueSlugAsync(string baseSlug)
    {
        var slug = baseSlug; int i = 1;
        while (await _db.Posts.AnyAsync(p => p.Slug == slug)) slug = $"{baseSlug}-{++i}";
        return slug;
    }

    public class InputModel
    {
        [Required, StringLength(300, MinimumLength = 3)]
        public string Title { get; set; } = default!;

        [StringLength(320)]
        public string? Slug { get; set; }

        [StringLength(500)]
        public string? Excerpt { get; set; }

        public string? Content { get; set; }

        [Required] public Guid CategoryId { get; set; }
        public string? FeaturedImageUrl { get; set; }
        public bool IsFeatured { get; set; }
        public PostStatus Status { get; set; }
        public DateTime? PublishedAt { get; set; }
    }
}
