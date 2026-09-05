using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Banners;

[Authorize(Policy = Permissions.Site.ManageBanner)]
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    public EditModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public BannerInput Input { get; set; } = new();

    public List<NewsCMS.Domain.Entities.Content.Media> Images { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var banner = await _db.Banners.AsNoTracking().FirstOrDefaultAsync(b => b.Id == Id);
        if (banner == null) return NotFound();
        Input = new BannerInput
        {
            Title = banner.Title,
            ImageId = banner.ImageId,
            Url = banner.Url,
            Position = banner.Position,
            StartAt = banner.StartAt,
            EndAt = banner.EndAt,
            Order = banner.Order,
            IsActive = banner.IsActive
        };
        await LoadImagesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadImagesAsync();
            return Page();
        }

        var banner = await _db.Banners.FindAsync(Id);
        if (banner == null) return NotFound();
        banner.Title = Input.Title.Trim();
        banner.ImageId = Input.ImageId;
        banner.Url = Input.Url?.Trim();
        banner.Position = Input.Position.Trim();
        banner.StartAt = Input.StartAt;
        banner.EndAt = Input.EndAt;
        banner.Order = Input.Order;
        banner.IsActive = Input.IsActive;
        banner.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã lưu banner.";
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
