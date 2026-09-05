using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace NewsCMS.Shared.Theming;

/// <summary>
/// Mọi theme/landing đều implement interface này. Core sẽ scan và đăng ký
/// theme đang active qua appsettings "Site:ActiveTheme".
/// </summary>
public interface IThemeModule
{
    /// <summary>Tên theme - phải khớp với folder và setting ActiveTheme.</summary>
    string Name { get; }

    /// <summary>Tên hiển thị (UI admin).</summary>
    string DisplayName { get; }

    /// <summary>Version theme.</summary>
    string Version { get; }

    /// <summary>Đăng ký service riêng của theme (nếu có).</summary>
    void ConfigureServices(IServiceCollection services);

    /// <summary>Đăng ký route riêng - chạy sau khi core đã map admin routes.</summary>
    void ConfigureRoutes(IEndpointRouteBuilder endpoints);
}

/// <summary>Ngữ cảnh theme hiện tại - tiêm vào ViewLocationExpander.</summary>
public static class ThemeContext
{
    private static readonly AsyncLocal<ThemeInfo?> _current = new();
    public static ThemeInfo? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

public record ThemeInfo(string Name, string DisplayName, string Version);

/// <summary>
/// Gắn lên endpoint của theme để MatcherPolicy lọc theo theme đã resolve theo host.
/// Cho phép nhiều theme cùng đăng ký route trùng pattern ("/", "san-pham", ...).
/// </summary>
public sealed record ThemeRouteMetadata(string Theme);

public static class ThemeRouteBuilderExtensions
{
    /// <summary>
    /// Map route cho 1 theme: tự thêm metadata theme để policy lọc đúng endpoint
    /// khi nhiều theme active đăng ký cùng pattern.
    /// </summary>
    public static IEndpointConventionBuilder MapThemeRoute(
        this IEndpointRouteBuilder endpoints,
        string theme,
        string name,
        string pattern,
        object defaults)
    {
        return endpoints
            .MapControllerRoute(name, pattern, defaults)
            .WithMetadata(new ThemeRouteMetadata(theme));
    }
}

