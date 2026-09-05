using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NewsCMS.Shared.Theming;

namespace NewsCMS.Theme.Universal;

/// <summary>
/// Theme "Universal" — nền tảng dựng site kéo-thả (Site Builder). Khác với các theme RCL
/// biên dịch sẵn (HaiLuuNguoc/KeoBia2026/PhuPhucYaka) đăng ký route cứng, Universal chỉ đăng ký
/// MỘT route catch-all {**path} duy nhất. UniversalController tự resolve path → SiteRoute → render
/// nội dung lưu trong DB (BuilderPage/SiteLayout), nên tạo site/trang mới KHÔNG cần code hay rebuild.
///
/// Catch-all có độ ưu tiên thấp hơn mọi route literal, nên /admin/**, /Account/**, /Sitemap do
/// MapRazorPages() đăng ký trước vẫn thắng. ThemeEndpointMatcherPolicy lọc route này theo host
/// (chỉ site nào có DefaultTheme = "Universal" mới vào đây) → không đụng 3 theme cũ.
/// </summary>
public class ThemeStartup : IThemeModule
{
    public string Name => "Universal";
    public string DisplayName => "Universal Site Builder";
    public string Version => "0.1.0";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void ConfigureRoutes(IEndpointRouteBuilder endpoints)
    {
        // CSS design token: /_nc/site/{slug}-{hash}.css. Route cụ thể hơn catch-all nên luôn
        // thắng; đăng ký trước cho rõ ràng. Cache immutable do hash trong URL.
        endpoints.MapThemeRoute(Name,
            "universal_site_css",
            "_nc/site/{file}",
            new { area = "Theme", controller = "Universal", action = "SiteCss" });

        // Catch-all duy nhất. path = "" cho trang chủ. UniversalController nhận toàn bộ path.
        endpoints.MapThemeRoute(Name,
            "universal_render",
            "{**path}",
            new { area = "Theme", controller = "Universal", action = "Render" });
    }
}
