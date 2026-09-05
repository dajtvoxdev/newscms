using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Domain.Entities.Identity;

namespace NewsCMS.Web.Pages.Account;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class LogoutModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;

    public LogoutModel(SignInManager<AppUser> signIn)
    {
        _signIn = signIn;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await _signIn.SignOutAsync();
        return LocalRedirect("/Account/Login");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _signIn.SignOutAsync();
        return LocalRedirect("/Account/Login");
    }
}
