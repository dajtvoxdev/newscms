using System.Security.Claims;
using System.Text.RegularExpressions;
using NewsCMS.Application.Analytics;
using NewsCMS.Application.Analytics.Dtos;
using NewsCMS.Web.Common;

namespace NewsCMS.Web.Middleware;

public sealed class VisitorTrackingMiddleware
{
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(365);

    private static readonly HashSet<string> SkipPathPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/admin",
        "/_content",
        "/lib",
        "/css",
        "/js",
        "/img",
        "/images",
        "/uploads",
        "/api",
        "/hangfire",
        "/Identity",
        "/Account"
    };

    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".map",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".mp3", ".mp4", ".webm", ".ogg",
        ".pdf"
    };

    private static readonly Regex BotPattern = new(
        @"bot|crawler|spider|slurp|bingpreview|facebookexternalhit|whatsapp|telegram|preview|pinterest|embedly|quora|outbrain|vkshare|w3c_validator|redditbot|applebot|duckduckbot|yandex|baidu",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly RequestDelegate _next;
    private readonly ILogger<VisitorTrackingMiddleware> _logger;

    public VisitorTrackingMiddleware(RequestDelegate next, ILogger<VisitorTrackingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx, IVisitorTrackingService tracking)
    {
        if (ShouldTrack(ctx))
        {
            try
            {
                var visitorCookie = VisitorIdentity.ResolveOrIssueGuidCookie(
                    ctx, VisitorIdentity.TrackingCookieName, CookieLifetime);
                var visitorKey = VisitorIdentity.Sha256(visitorCookie);
                var ipHash = VisitorIdentity.IpHashFromRequest(ctx);
                var ua = ctx.Request.Headers.UserAgent.ToString();

                var isAuthenticated = ctx.User?.Identity?.IsAuthenticated == true;
                Guid? userId = null;
                if (isAuthenticated)
                {
                    var sub = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (Guid.TryParse(sub, out var parsed)) userId = parsed;
                }

                var context = new VisitorTrackingContext(visitorKey, ipHash, ua, isAuthenticated, userId);
                await tracking.TrackAsync(context, ctx.RequestAborted);
            }
            catch (Exception ex)
            {
                // Tracking failures must never break the request pipeline.
                _logger.LogWarning(ex, "VisitorTrackingMiddleware failed for {Path}", ctx.Request.Path);
            }
        }

        await _next(ctx);
    }

    private static bool ShouldTrack(HttpContext ctx)
    {
        if (!HttpMethods.IsGet(ctx.Request.Method)) return false;

        var path = ctx.Request.Path.Value;
        if (string.IsNullOrEmpty(path)) return true;

        foreach (var prefix in SkipPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        }

        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && SkipExtensions.Contains(ext)) return false;

        var ua = ctx.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrEmpty(ua) && BotPattern.IsMatch(ua)) return false;

        return true;
    }
}
