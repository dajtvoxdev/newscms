using System.Security.Cryptography;
using System.Text;

namespace NewsCMS.Web.Common;

/// <summary>
/// Stable visitor key + ip hash used by both analytics tracking and pledge signing.
/// Keep cookie names distinct per surface so resetting one does not reset the other.
/// </summary>
public static class VisitorIdentity
{
    public const string TrackingCookieName = "_nv_vid";
    public const string PledgeCookieName = "_nv_pledge_vid";

    public static string ResolveOrIssueGuidCookie(HttpContext ctx, string cookieName, TimeSpan lifetime)
    {
        var existing = ctx.Request.Cookies[cookieName];
        if (!string.IsNullOrWhiteSpace(existing) && existing.Length is >= 16 and <= 64)
        {
            return existing;
        }

        var fresh = Guid.NewGuid().ToString("N");
        ctx.Response.Cookies.Append(cookieName, fresh, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.Add(lifetime),
            IsEssential = true
        });
        return fresh;
    }

    public static string Sha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static string IpHashFromRequest(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
        // Honour first X-Forwarded-For entry if behind a proxy and ForwardedHeaders not configured.
        var fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(fwd))
        {
            var first = fwd.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first)) ip = first;
        }
        return Sha256(ip);
    }
}
