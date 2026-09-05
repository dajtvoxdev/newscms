using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NewsCMS.Shared.Theming;

namespace NewsCMS.Theme.HaiLuuNguoc;

public class ThemeStartup : IThemeModule
{
    public string Name => "HaiLuuNguoc";
    public string DisplayName => "Hải lưu ngược";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void ConfigureRoutes(IEndpointRouteBuilder endpoints)
    {
        // AR routes MUST come before the greedy {categorySlug} catch-all
        endpoints.MapThemeRoute(Name,
            "hailuunguoc_ar_ping",
            "ar/{slug}/view-ping",
            new { area = "Theme", controller = "Ar", action = "ViewPing" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_ar_fallback",
            "ar/{slug}/xem",
            new { area = "Theme", controller = "Ar", action = "Fallback" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_ar_entry",
            "ar",
            new { area = "Theme", controller = "Ar", action = "Entry" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_ar",
            "ar/{slug}",
            new { area = "Theme", controller = "Ar", action = "Index" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_commitment_sign",
            "cam-ket/sign",
            new { area = "Theme", controller = "Commitment", action = "Sign" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_commitment_wall",
            "cam-ket/wall",
            new { area = "Theme", controller = "Commitment", action = "Wall" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_commitment_status",
            "cam-ket/status",
            new { area = "Theme", controller = "Commitment", action = "Status" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_commitment",
            "cam-ket",
            new { area = "Theme", controller = "Commitment", action = "Index" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_product_detail",
            "san-pham/{slug}",
            new { area = "Theme", controller = "Product", action = "Detail" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_product_list",
            "san-pham",
            new { area = "Theme", controller = "Product", action = "Index" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_contest",
            "cuoc-thi",
            new { area = "Theme", controller = "Home", action = "Contest" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_post_detail",
            "{categorySlug}/{postSlug}",
            new { area = "Theme", controller = "Post", action = "Detail" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_category",
            "{categorySlug}",
            new { area = "Theme", controller = "Category", action = "Index" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_page",
            "page/{slug}",
            new { area = "Theme", controller = "Page", action = "Detail" });

        endpoints.MapThemeRoute(Name,
            "hailuunguoc_home",
            "",
            new { area = "Theme", controller = "Home", action = "Index" });
    }
}
