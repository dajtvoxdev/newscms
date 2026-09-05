using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Ar;
using NewsCMS.Application.Ar.Dtos;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.ArExperiences;

[Authorize(Permissions.Ar.CreateAr)]
public class CreateModel : PageModel
{
    private readonly IArExperienceService _ar;
    private readonly AppDbContext _db;

    public CreateModel(IArExperienceService ar, AppDbContext db)
    {
        _ar = ar;
        _db = db;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> Posts { get; private set; } = new();

    public class InputModel
    {
        public string Title { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? Description { get; set; }
        public double VideoWidth { get; set; } = 1.0;
        public double VideoHeight { get; set; } = 0.5625;
        public Guid? FallbackPostId { get; set; }
        public bool IsPublished { get; set; }

        // All files go through IMediaService via _MediaPicker
        public string? TargetImageUrl { get; set; }
        public string? MindFileUrl { get; set; }
        public string? VideoUrl { get; set; }
    }

    public async Task OnGetAsync(CancellationToken ct) => await LoadPostsAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadPostsAsync(ct);

        if (string.IsNullOrWhiteSpace(Input.TargetImageUrl))
            ModelState.AddModelError("", "Vui lòng chọn ảnh target.");
        if (string.IsNullOrWhiteSpace(Input.MindFileUrl))
            ModelState.AddModelError("", "Vui lòng tải lên file .mind.");
        if (string.IsNullOrWhiteSpace(Input.VideoUrl))
            ModelState.AddModelError("", "Vui lòng chọn video.");

        if (!ModelState.IsValid) return Page();

        var dto = new ArExperienceUpsertDto(
            null, Input.Title, Input.Slug ?? string.Empty, Input.Description,
            Input.VideoWidth, Input.VideoHeight, Input.FallbackPostId, Input.IsPublished)
        {
            TargetImageUrl = Input.TargetImageUrl,
            MindFileUrl    = Input.MindFileUrl,
            VideoUrl       = Input.VideoUrl
        };

        var result = await _ar.CreateAsync(dto, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Có lỗi xảy ra.");
            return Page();
        }

        TempData["Success"] = $"Đã tạo trải nghiệm AR \"{Input.Title}\".";
        return RedirectToPage("/ArExperiences/Index");
    }

    private async Task LoadPostsAsync(CancellationToken ct)
    {
        Posts = await _db.Posts.AsNoTracking()
            .OrderBy(p => p.Title)
            .Select(p => new SelectListItem(p.Title, p.Id.ToString()))
            .ToListAsync(ct);
    }
}
