using Microsoft.AspNetCore.Mvc.Razor;
using NewsCMS.Shared.Theming;

namespace NewsCMS.Web.Theming;

/// <summary>
/// Cho phép override view của theme bằng file vật lý trong /Themes/{activeTheme}/Views
/// mà không phải build lại Razor Class Library của theme.
///
/// KHÔNG thêm đường dẫn /Areas/... ở đây: view gốc của theme nằm trong area riêng
/// ("Theme" cho HaiLuuNguoc, "KeoBia2026", "PhuPhucYaka") và MVC đã sinh sẵn
/// "/Areas/{2}/Views/{1}/{0}.cshtml" từ route value `area`. Hardcode một tên area
/// cụ thể sẽ khiến mọi theme cùng nhận view của theme đó → InvalidOperationException
/// vì model type không khớp.
/// </summary>
public class ThemeViewLocationExpander : IViewLocationExpander
{
    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
    {
        if (!context.Values.TryGetValue("theme", out var theme) || string.IsNullOrEmpty(theme))
            return viewLocations;

        var themeLocations = new[]
        {
            $"/Themes/{theme}/Views/{{1}}/{{0}}.cshtml",
            $"/Themes/{theme}/Views/Shared/{{0}}.cshtml",
        };
        return themeLocations.Concat(viewLocations);
    }

    public void PopulateValues(ViewLocationExpanderContext context)
    {
        context.Values["theme"] = ThemeContext.Current?.Name;
    }
}
