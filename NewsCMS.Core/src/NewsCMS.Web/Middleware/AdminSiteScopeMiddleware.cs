using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Middleware;

/// <summary>
/// Sau khi xác thực, re-scope site cho request vùng /admin:
///  - Admin gắn site (AppUser.SiteId != null): KHÓA cứng theo site đó (ranh giới bảo mật tenant).
///  - SuperAdmin (SiteId == null): chọn site qua cookie AdminSiteId; không có thì giữ site resolve theo host.
/// Phải chạy SAU UseAuthentication (cần User identity). AppDbContext đọc CurrentSiteId lazy
/// nên gọi lại ICurrentSite.Set() sẽ áp lại query filter cho mọi truy vấn admin tiếp theo.
/// </summary>
public class AdminSiteScopeMiddleware
{
    public const string SiteCookie = "AdminSiteId";

    private readonly RequestDelegate _next;

    public AdminSiteScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext ctx, ICurrentSite currentSite, AppDbContext db)
    {
        if (!IsAdminRequest(ctx) || ctx.User?.Identity?.IsAuthenticated != true)
        {
            await _next(ctx);
            return;
        }

        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userId, out var uid))
        {
            var boundSiteId = await db.Users
                .Where(u => u.Id == uid)
                .Select(u => u.SiteId)
                .FirstOrDefaultAsync(ctx.RequestAborted);

            var targetSiteId = boundSiteId ?? ReadCookieSiteId(ctx);
            if (targetSiteId is { } sid && sid != Guid.Empty && sid != currentSite.SiteId)
            {
                var site = await db.Sites
                    .Where(s => s.Id == sid && s.IsActive)
                    .Select(s => new { s.Id, s.Slug, s.DefaultTheme })
                    .FirstOrDefaultAsync(ctx.RequestAborted);

                if (site is not null)
                    currentSite.Set(site.Id, site.Slug, site.DefaultTheme);
            }
        }

        await _next(ctx);
    }

    private static bool IsAdminRequest(HttpContext ctx) =>
        ctx.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase) ||
        ctx.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase);

    private static Guid? ReadCookieSiteId(HttpContext ctx) =>
        ctx.Request.Cookies.TryGetValue(SiteCookie, out var raw) && Guid.TryParse(raw, out var id)
            ? id
            : null;
}
