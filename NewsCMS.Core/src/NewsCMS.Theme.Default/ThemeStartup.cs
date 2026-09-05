using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NewsCMS.Shared.Theming;

namespace NewsCMS.Theme.Default;

public class ThemeStartup : IThemeModule
{
    public string Name => "Default";
    public string DisplayName => "NewsCMS Default Theme";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        // Theme có thể đăng ký service riêng tại đây nếu cần.
    }

    public void ConfigureRoutes(IEndpointRouteBuilder endpoints)
    {
        // Public routes của theme
        endpoints.MapControllerRoute(
            name: "theme_post_detail",
            pattern: "{categorySlug}/{postSlug}",
            defaults: new { area = "Theme", controller = "Post", action = "Detail" });

        endpoints.MapControllerRoute(
            name: "theme_category",
            pattern: "{categorySlug}",
            defaults: new { area = "Theme", controller = "Category", action = "Index" });

        endpoints.MapControllerRoute(
            name: "theme_home",
            pattern: "",
            defaults: new { area = "Theme", controller = "Home", action = "Index" });
    }
}
