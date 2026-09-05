using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Areas.Admin.Pages.Sites;

/// <summary>
/// Tạo site mới từ SiteTemplate (Phase 6) — clone layout/page/menu/category/token + sinh admin
/// riêng cho site. Chỉ SuperAdmin (root). Sau bước này site chạy được ngay: KHÔNG rebuild, KHÔNG redeploy.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
public class CreateFromTemplateModel : PageModel
{
    private readonly ISiteTemplateService _templates;
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public CreateFromTemplateModel(ISiteTemplateService templates, AppDbContext db, UserManager<AppUser> userManager)
    {
        _templates = templates;
        _db = db;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<SiteTemplateDto> Templates { get; private set; } = Array.Empty<SiteTemplateDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Templates = await _templates.ListAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Templates = await _templates.ListAsync(ct);
        if (!ModelState.IsValid) return Page();

        // Kiểm tra admin trùng trước khi clone để không tạo site rồi mới fail.
        var adminExists = await _userManager.FindByNameAsync(Input.AdminUserName) is not null
                          || await _userManager.FindByEmailAsync(Input.AdminEmail) is not null;
        if (adminExists)
        {
            ModelState.AddModelError("Input.AdminEmail", "Admin username hoặc email đã tồn tại.");
            return Page();
        }

        var result = await _templates.CloneToSiteAsync(
            Input.TemplateId, Input.Name, Input.Slug, Input.PrimaryDomain, ct);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Không tạo được site.");
            return Page();
        }

        var siteId = result.Value;

        // Sinh tài khoản admin gắn với site vừa tạo.
        var admin = new AppUser
        {
            UserName = Input.AdminUserName,
            Email = Input.AdminEmail,
            EmailConfirmed = true,
            FullName = Input.AdminFullName,
            SiteId = siteId,
            IsActive = true
        };

        var createResult = await _userManager.CreateAsync(admin, Input.AdminPassword);
        if (!createResult.Succeeded)
        {
            foreach (var e in createResult.Errors)
                ModelState.AddModelError(string.Empty, e.Description);

            // Site đã tạo nhưng admin fail → xoá site để không để lại rác.
            await RollbackSiteAsync(siteId, ct);
            return Page();
        }

        await _userManager.AddToRoleAsync(admin, "Admin");

        TempData["Success"] = $"Đã tạo site '{Input.Name}' từ template. Đăng nhập bằng '{Input.AdminUserName}'.";
        return RedirectToPage("/Sites/Index", new { area = "Admin" });
    }

    /// <summary>Xoá site vừa clone khi bước tạo admin thất bại (tránh site mồ côi).</summary>
    private async Task RollbackSiteAsync(Guid siteId, CancellationToken ct)
    {
        var previousBypass = _db.BypassSiteScope;
        _db.BypassSiteScope = true;
        try
        {
            var site = await _db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
            if (site is not null)
            {
                _db.Sites.Remove(site);
                await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            _db.BypassSiteScope = previousBypass;
        }
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Chọn một mẫu site.")]
        public Guid TemplateId { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        [RegularExpression("^[a-z0-9\\-]+$", ErrorMessage = "Slug chỉ gồm chữ thường, số và dấu gạch ngang.")]
        public string Slug { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? PrimaryDomain { get; set; }

        [Required, MaxLength(100)]
        public string AdminUserName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(256)]
        public string AdminEmail { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string AdminFullName { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string AdminPassword { get; set; } = string.Empty;
    }
}
