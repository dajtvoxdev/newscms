using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;

    public LoginModel(SignInManager<AppUser> signIn, UserManager<AppUser> um, AppDbContext db)
    {
        _signIn = signIn;
        _userManager = um;
        _db = db;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required] public string UserName { get; set; } = default!;
        [Required, DataType(DataType.Password)] public string Password { get; set; } = default!;
        public bool RememberMe { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid) return Page();

        // Force clear any stale auth cookie before authenticating new user
        await _signIn.SignOutAsync();

        var user = await _userManager.FindByNameAsync(Input.UserName);
        if (user is null || !user.IsActive)
        {
            ErrorMessage = "Tài khoản không tồn tại hoặc đã bị khóa.";
            return Page();
        }

        var result = await _signIn.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ErrorMessage = result.IsLockedOut ? "Tài khoản bị khoá do nhập sai nhiều lần." : "Sai thông tin đăng nhập.";
            return Page();
        }

        // Nạp permission làm claim trước khi sign-in (remove stale + add fresh)
        var roleNames = await _userManager.GetRolesAsync(user);
        var permCodes = await _db.RolePermissions
            .Where(rp => _db.Roles.Where(r => roleNames.Contains(r.Name!)).Select(r => r.Id).Contains(rp.RoleId))
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToListAsync();

        var existing = await _userManager.GetClaimsAsync(user);
        var oldPermClaims = existing.Where(c => c.Type == Permissions.Prefix).ToList();
        if (oldPermClaims.Any())
            await _userManager.RemoveClaimsAsync(user, oldPermClaims);

        var newClaims = permCodes.Select(c => new Claim(Permissions.Prefix, c)).ToList();
        if (newClaims.Any())
            await _userManager.AddClaimsAsync(user, newClaims);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        await _signIn.SignInAsync(user, Input.RememberMe);
        return LocalRedirect(returnUrl ?? "/admin");
    }
}
