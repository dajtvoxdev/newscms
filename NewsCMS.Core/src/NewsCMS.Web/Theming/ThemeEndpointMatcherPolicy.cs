using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using NewsCMS.Shared.Theming;

namespace NewsCMS.Web.Theming;

/// <summary>
/// Khi nhiều theme cùng active đăng ký route trùng pattern ("/", "san-pham", ...),
/// policy này loại các endpoint có ThemeRouteMetadata khác theme đã resolve theo host.
/// Endpoint không gắn metadata (admin, razor pages) không bị ảnh hưởng.
/// </summary>
public sealed class ThemeEndpointMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    public const string ThemeItemKey = "__ResolvedTheme";

    // Chạy sau các policy mặc định.
    public override int Order => 1000;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints)
    {
        foreach (var e in endpoints)
        {
            if (e.Metadata.GetMetadata<ThemeRouteMetadata>() is not null)
                return true;
        }
        return false;
    }

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        var theme = httpContext.Items.TryGetValue(ThemeItemKey, out var t) ? t as string : null;

        for (var i = 0; i < candidates.Count; i++)
        {
            if (!candidates.IsValidCandidate(i))
                continue;

            var meta = candidates[i].Endpoint.Metadata.GetMetadata<ThemeRouteMetadata>();
            if (meta is not null &&
                !string.Equals(meta.Theme, theme, StringComparison.OrdinalIgnoreCase))
            {
                candidates.SetValidity(i, false);
            }
        }

        return Task.CompletedTask;
    }
}
