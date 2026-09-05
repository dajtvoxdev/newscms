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

[Authorize(Permissions.Ar.EditAr)]
public class EditModel : PageModel
{
    private readonly IArExperienceService _ar;
    private readonly AppDbContext _db;

    public EditModel(IArExperienceService ar, AppDbContext db)
    {
        _ar = ar;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public ArExperienceDto? Current { get; private set; }
    public List<SelectListItem> Posts { get; private set; } = new();

    public class InputModel
    {
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
        public double VideoWidth { get; set; } = 1.0;
        public double VideoHeight { get; set; } = 0.5625;
        public Guid? FallbackPostId { get; set; }
        public bool IsPublished { get; set; }

        // Pre-populated with current URL; changed by _MediaPicker
        public string? TargetImageUrl { get; set; }
        public string? MindFileUrl { get; set; }
        public string? VideoUrl { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Current = await _ar.GetByIdAsync(Id, ct);
        if (Current is null) return NotFound();

        Input = new InputModel
        {
            Title          = Current.Title,
            Slug           = Current.Slug,
            Description    = Current.Description,
            VideoWidth     = Current.VideoWidth,
            VideoHeight    = Current.VideoHeight,
            FallbackPostId = Current.FallbackPostId,
            IsPublished    = Current.IsPublished,
            TargetImageUrl = Current.TargetImageUrl,
            MindFileUrl    = Current.MindFileUrl,
            VideoUrl       = Current.VideoUrl
        };

        await LoadPostsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Current = await _ar.GetByIdAsync(Id, ct);
        if (Current is null) return NotFound();

        await LoadPostsAsync(ct);
        if (!ModelState.IsValid) return Page();

        var dto = new ArExperienceUpsertDto(
            Id, Input.Title, Input.Slug, Input.Description,
            Input.VideoWidth, Input.VideoHeight, Input.FallbackPostId, Input.IsPublished)
        {
            TargetImageUrl = Input.TargetImageUrl,
            MindFileUrl    = Input.MindFileUrl,
            VideoUrl       = Input.VideoUrl
        };

        var result = await _ar.UpdateAsync(dto, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Có lỗi xảy ra.");
            return Page();
        }

        TempData["Success"] = $"Đã cập nhật \"{Input.Title}\".";
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
