using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Roles;

[Authorize(Permissions.Identity.ManageRole)]
public class CreateModel : PageModel
{
    private readonly RoleManager<AppRole> _roles;
    public CreateModel(RoleManager<AppRole> roles) => _roles = roles;

    [BindProperty] public InputModel Input { get; set; } = new();

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var role = new AppRole { Name = Input.Name.Trim(), Description = Input.Description?.Trim() };
        var result = await _roles.CreateAsync(role);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
        TempData["Success"] = "Đã tạo vai trò.";
        return RedirectToPage("/Roles/Edit", new { id = role.Id });
    }

    public class InputModel
    {
        [Required, StringLength(100)] public string Name { get; set; } = default!;
        [StringLength(300)] public string? Description { get; set; }
    }
}
