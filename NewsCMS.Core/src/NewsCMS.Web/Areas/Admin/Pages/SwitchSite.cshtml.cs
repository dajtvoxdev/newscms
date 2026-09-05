using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Web.Middleware;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Areas.Admin.Pages;

[Authorize(Roles = "SuperAdmin")]
public class SwitchSiteModel : PageModel
{
    private readonly AppDbContext db;

    public SwitchSiteModel(AppDbContext db)
    {
        this.db = db;
    }

    public IActionResult OnGet() => RedirectToPage("/Index", new { area = "Admin" });

    public async Task<IActionResult> OnPostAsync(Guid siteId, string? returnUrl)
    {
        var exists = await db.Sites.IgnoreQueryFilters()
            .AnyAsync(s => s.Id == siteId && s.IsActive);

        if (exists)
        {
            Response.Cookies.Append(
                AdminSiteScopeMiddleware.SiteCookie,
                siteId.ToString(),
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToPage("/Index", new { area = "Admin" });
    }
}
