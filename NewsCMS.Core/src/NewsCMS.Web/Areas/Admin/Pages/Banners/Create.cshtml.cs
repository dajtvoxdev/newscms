using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Banners;

[Authorize(Policy = Permissions.Site.ManageBanner)]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    public CreateModel(AppDbContext db) => _db = db;

    [BindProperty]
    public BannerInput Input { get; set; } = new();

    public List<NewsCMS.Domain.Entities.Content.Media> Images { get; private set; } = new();

    public async Task OnGetAsync() => await LoadImagesAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadImagesAsync();
            return Page();
        }

        _db.Banners.Add(new Banner
        {
            Title = Input.Title.Trim(),
            ImageId = Input.ImageId,
            Url = Input.Url?.Trim(),
            Position = Input.Position.Trim(),
            StartAt = Input.StartAt,
            EndAt = Input.EndAt,
            Order = Input.Order,
            IsActive = Input.IsActive
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo banner.";
        return RedirectToPage("/Banners/Index");
    }

    private async Task LoadImagesAsync()
    {
        Images = await _db.Medias.AsNoTracking()
            .Where(m => m.MimeType.StartsWith("image/"))
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .ToListAsync();
    }

    public sealed class BannerInput
    {
        [Required(ErrorMessage = "Vui lòng nhập tiêu đề.")]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;
        [Required(ErrorMessage = "Vui lòng chọn ảnh.")]
        public Guid ImageId { get; set; }
        [StringLength(500)]
        public string? Url { get; set; }
        [Required]
        [StringLength(80)]
        public string Position { get; set; } = "home-hero";
        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public int Order { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
