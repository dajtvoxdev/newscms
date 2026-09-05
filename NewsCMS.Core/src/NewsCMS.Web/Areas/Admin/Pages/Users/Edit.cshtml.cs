using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Users;

[Authorize(Permissions.Identity.EditUser)]
public class EditModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<AppRole> _roles;
    private readonly AppDbContext _db;

    public EditModel(UserManager<AppUser> users, RoleManager<AppRole> roles, AppDbContext db)
    { _users = users; _roles = roles; _db = db; }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public string? NewPassword { get; set; }
    public string UserEmail { get; private set; } = default!;
    public string? UserName { get; private set; }
    public List<SelectListItem> RoleOptions { get; private set; } = new();
    public List<SelectListItem> SiteOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.FindByIdAsync(Id.ToString());
        if (user == null) return NotFound();
        UserEmail = user.Email!;
        UserName = user.UserName;
        Input = new InputModel
        {
            FullName = user.FullName,
            IsActive = user.IsActive,
            SiteId = user.SiteId,
            RoleName = (await _users.GetRolesAsync(user)).FirstOrDefault()
        };
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadOptionsAsync();
        var user = await _users.FindByIdAsync(Id.ToString());
        if (user == null) return NotFound();
        UserEmail = user.Email!;
        UserName = user.UserName;
        if (!ModelState.IsValid) return Page();

        user.FullName = Input.FullName?.Trim();
        user.IsActive = Input.IsActive;
        user.SiteId = Input.SiteId;
        await _users.UpdateAsync(user);

        var currentRoles = await _users.GetRolesAsync(user);
        await _users.RemoveFromRolesAsync(user, currentRoles);
        if (!string.IsNullOrWhiteSpace(Input.RoleName))
            await _users.AddToRoleAsync(user, Input.RoleName);

        TempData["Success"] = "Đã cập nhật người dùng.";
        return RedirectToPage("/Users/Index");
    }

    public async Task<IActionResult> OnPostResetPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 8)
        {
            TempData["Error"] = "Mật khẩu mới tối thiểu 8 ký tự.";
            return RedirectToPage(new { Id });
        }
        var user = await _users.FindByIdAsync(Id.ToString());
        if (user == null) return NotFound();
        await _users.RemovePasswordAsync(user);
        var result = await _users.AddPasswordAsync(user, NewPassword);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã đặt lại mật khẩu."
            : string.Join("; ", result.Errors.Select(e => e.Description));
        return RedirectToPage(new { Id });
    }

    private async Task LoadOptionsAsync()
    {
        RoleOptions = await _roles.Roles.OrderBy(r => r.Name)
            .Select(r => new SelectListItem(r.Name, r.Name))
            .ToListAsync();
        SiteOptions = await _db.Sites.OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.Id.ToString()))
            .ToListAsync();
    }

    public class InputModel
    {
        [StringLength(200)] public string? FullName { get; set; }
        public bool IsActive { get; set; } = true;
        public string? RoleName { get; set; }
        public Guid? SiteId { get; set; }
    }
}
