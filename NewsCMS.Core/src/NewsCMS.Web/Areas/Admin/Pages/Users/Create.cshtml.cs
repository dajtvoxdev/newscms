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

[Authorize(Permissions.Identity.CreateUser)]
public class CreateModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<AppRole> _roles;
    private readonly AppDbContext _db;

    public CreateModel(UserManager<AppUser> users, RoleManager<AppRole> roles, AppDbContext db)
    { _users = users; _roles = roles; _db = db; }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> RoleOptions { get; private set; } = new();
    public List<SelectListItem> SiteOptions { get; private set; } = new();

    public async Task OnGetAsync() => await LoadOptionsAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadOptionsAsync();
        if (!ModelState.IsValid) return Page();

        var user = new AppUser
        {
            UserName = Input.Email.Trim(),
            Email = Input.Email.Trim(),
            FullName = Input.FullName?.Trim(),
            SiteId = Input.SiteId,
            IsActive = true,
            EmailConfirmed = true
        };
        var result = await _users.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
        if (!string.IsNullOrWhiteSpace(Input.RoleName))
            await _users.AddToRoleAsync(user, Input.RoleName);

        TempData["Success"] = $"Đã tạo tài khoản {user.Email}.";
        return RedirectToPage("/Users/Index");
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
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = default!;
        [StringLength(200)] public string? FullName { get; set; }
        [Required, StringLength(100, MinimumLength = 8)] public string Password { get; set; } = default!;
        public string? RoleName { get; set; }
        public Guid? SiteId { get; set; }
    }
}
