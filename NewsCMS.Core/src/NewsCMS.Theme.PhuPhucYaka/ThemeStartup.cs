using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NewsCMS.Shared.Theming;

namespace NewsCMS.Theme.PhuPhucYaka;

public class ThemeStartup : IThemeModule
{
    public string Name => "PhuPhucYaka";
    public string DisplayName => "Thế Giới Xe Điện Phú Phúc";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void ConfigureRoutes(IEndpointRouteBuilder endpoints)
    {
        // Product detail must come before the product list (specificity), both before home.
        endpoints.MapThemeRoute(Name,
            "phuphucyaka_product_detail",
            "san-pham/{slug}",
            new { area = "PhuPhucYaka", controller = "Product", action = "Detail" });

        endpoints.MapThemeRoute(Name,
            "phuphucyaka_product_list",
            "san-pham",
            new { area = "PhuPhucYaka", controller = "Product", action = "Index" });

        endpoints.MapThemeRoute(Name,
            "phuphucyaka_home",
            "",
            new { area = "PhuPhucYaka", controller = "Home", action = "Index" });
    }
}
