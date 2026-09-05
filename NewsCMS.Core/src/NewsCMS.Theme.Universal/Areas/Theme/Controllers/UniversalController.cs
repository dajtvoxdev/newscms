using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Theme.Universal.Areas.Theme.Controllers;

/// <summary>
/// Controller catch-all của theme Universal: resolve path → SiteRoute → render entity từ DB.
/// Hỗ trợ Page (IPageRenderer), Post, Product detail, sitemap.xml, robots.txt. Category/Custom
/// template là Phase 5+.
/// </summary>
[Area("Theme")]
public class UniversalController : Controller
{
    private readonly IRouteRegistry _routes;
    private readonly IPageRenderer _pageRenderer;
    private readonly IDesignTokenCssBuilder _tokenCss;
    private readonly ISitemapGenerator _sitemap;
    private readonly ICurrentSite _currentSite;

    public UniversalController(
        IRouteRegistry routes,
        IPageRenderer pageRenderer,
        IDesignTokenCssBuilder tokenCss,
        ISitemapGenerator sitemap,
        ICurrentSite currentSite)
    {
        _routes = routes;
        _pageRenderer = pageRenderer;
        _tokenCss = tokenCss;
        _sitemap = sitemap;
        _currentSite = currentSite;
    }

    // GET {**path} — path = "" ở trang chủ, còn lại là phần sau domain.
    public async Task<IActionResult> Render(string? path, CancellationToken ct)
    {
        var normalizedPath = IRouteRegistry.NormalizePath(path);

        // Sitemap + robots.txt: xử lý trực tiếp, không qua SiteRoute.
        if (normalizedPath == "/sitemap.xml")
        {
            var xml = await _sitemap.GenerateAsync(ct);
            return Content(xml, "application/xml");
        }
        if (normalizedPath == "/robots.txt")
        {
            var txt = await _sitemap.GenerateRobotsTxtAsync(ct);
            return Content(txt, "text/plain");
        }

        var route = await _routes.ResolveAsync(normalizedPath, null, ct);
        if (route is null)
        {
            return NotFound();
        }

        switch (route)
        {
            case { RouteType: RouteType.Page, TargetId: { } pageId }:
                var rendered = await _pageRenderer.RenderAsync(pageId, route.Culture, ct);
                if (rendered is null) return NotFound();

                Response.Headers["X-NC-Cache-Tag"] = rendered.CacheTag;
                return Content(rendered.Html, "text/html");

            // Mọi loại thực thể nội dung (Post, Product, Category, Event...) đều được render qua RenderEntityAsync
            // dựa trên Content Type Registry — tự động tương thích khi thêm loại nội dung mới mà không cần thêm case.
            case { TargetId: { } entityId }:
                var entityPage = await _pageRenderer.RenderEntityAsync(route.RouteType, entityId, route.Culture, normalizedPath, ct);
                if (entityPage is null) return NotFound();

                Response.Headers["X-NC-Cache-Tag"] = entityPage.CacheTag;
                return Content(entityPage.Html, "text/html");

            default:
                // Custom: route đặc biệt không gắn entity chuẩn, chưa có renderer.
                return NotFound();
        }
    }

    // GET _nc/site/{file} — phục vụ CSS design token, cache immutable.
    public async Task<IActionResult> SiteCss(string file, CancellationToken ct)
    {
        var css = await _tokenCss.BuildCssAsync(ct);
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Content(css, "text/css");
    }
}
