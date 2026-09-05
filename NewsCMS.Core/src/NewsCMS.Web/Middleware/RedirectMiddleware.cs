using Microsoft.EntityFrameworkCore;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Middleware;

/// <summary>
/// Đọc bảng Redirect, nếu khớp FromPath thì trả về 301/302 đến ToPath.
/// </summary>
public class RedirectMiddleware
{
    private readonly RequestDelegate _next;
    public RedirectMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (!string.IsNullOrEmpty(path) && path.StartsWith("/") && !path.StartsWith("/admin"))
        {
            var rule = await db.Redirects.AsNoTracking()
                .FirstOrDefaultAsync(r => r.IsActive && r.FromPath == path);
            if (rule != null)
            {
                ctx.Response.StatusCode = rule.StatusCode;
                ctx.Response.Headers.Location = rule.ToPath;
                return;
            }
        }
        await _next(ctx);
    }
}
