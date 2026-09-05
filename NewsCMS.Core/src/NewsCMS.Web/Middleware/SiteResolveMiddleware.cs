using NewsCMS.Application.Site;
using NewsCMS.Shared.Theming;
using NewsCMS.Web.Theming;

namespace NewsCMS.Web.Middleware;

/// <summary>
/// Resolve site theo Host (qua ISiteResolver), set:
///  - ICurrentSite (scoped) cho data scoping của AppDbContext,
///  - ThemeContext.Current cho view location của theme,
///  - HttpContext.Items[ThemeItemKey] cho ThemeEndpointMatcherPolicy.
/// PHẢI chạy trước UseRouting để matcher policy thấy theme.
/// Host không có mapping → 404 (không ném exception, tránh 500 cho domain lạ trỏ vào server).
/// </summary>
public class SiteResolveMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SiteResolveMiddleware> _logger;

    public SiteResolveMiddleware(RequestDelegate next, ILogger<SiteResolveMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx, ISiteResolver resolver, ICurrentSite currentSite)
    {
        // /mcp không phụ thuộc host: site do API key quyết định (McpApiKeyMiddleware set
        // ICurrentSite). Resolve theo host ở đây sẽ chặn 404 khi agent gọi qua domain chưa
        // map — hoàn toàn hợp lệ với một endpoint xác thực bằng key.
        if (ctx.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var host = ctx.Request.Host.Host;
        var site = await resolver.ResolveAsync(host, ctx.RequestAborted);

        if (site is null)
        {
            _logger.LogWarning("No site mapped for host '{Host}' (path {Path}).", host, ctx.Request.Path);
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.WriteAsync($"Không có site nào được cấu hình cho domain '{host}'.");
            return;
        }

        currentSite.Set(site.SiteId, site.Slug, site.Theme);
        ctx.Items[ThemeEndpointMatcherPolicy.ThemeItemKey] = site.Theme;
        ThemeContext.Current = new ThemeInfo(site.Theme, site.Theme, "1.0.0");

        await _next(ctx);
    }
}
