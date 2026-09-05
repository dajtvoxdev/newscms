using Microsoft.Extensions.Caching.Memory;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;

namespace NewsCMS.Web.Middleware;

/// <summary>
/// Xác thực API key cho endpoint MCP và set ICurrentSite theo key. Đây là điểm duy nhất quyết định
/// agent thao tác lên site nào — sau bước này global query filter của AppDbContext tự bảo vệ ranh
/// giới tenant, ISiteBuilderApi không nhận siteId từ bên ngoài nên không thể trỏ sang site khác.
///
/// SiteResolveMiddleware chạy trước đã set site theo Host; với /mcp ta ghi đè bằng site của key.
/// Kèm rate limit theo key (sliding window 1 phút, IMemoryCache) như kế hoạch Phase 7.
/// </summary>
public sealed class McpApiKeyMiddleware
{
    /// <summary>Header mang key thô. Cũng chấp nhận "Authorization: Bearer &lt;key&gt;".</summary>
    private const string ApiKeyHeader = "X-NewsCMS-Api-Key";

    /// <summary>Header chọn site cho key phạm vi root; key gắn site bỏ qua header này.</summary>
    private const string SiteHeader = "X-NewsCMS-Site";

    private readonly RequestDelegate _next;
    private readonly ILogger<McpApiKeyMiddleware> _logger;

    public McpApiKeyMiddleware(RequestDelegate next, ILogger<McpApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext ctx,
        ISiteApiKeyService keys,
        ICurrentSite currentSite,
        IMemoryCache cache)
    {
        // Chỉ chắn /mcp; phần còn lại của site đi tiếp bình thường.
        if (!ctx.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var plainKey = ExtractKey(ctx.Request);
        if (string.IsNullOrWhiteSpace(plainKey))
        {
            await WriteProblemAsync(ctx, StatusCodes.Status401Unauthorized,
                $"Thiếu API key. Gửi header '{ApiKeyHeader}' hoặc 'Authorization: Bearer <key>'.");
            return;
        }

        var requestedSite = ctx.Request.Headers[SiteHeader].FirstOrDefault();
        var authenticated = await keys.AuthenticateAsync(plainKey, requestedSite, ctx.RequestAborted);

        if (authenticated is null)
        {
            // Không nói rõ lý do (sai key / hết hạn / thu hồi) để không thành oracle dò key.
            _logger.LogWarning("MCP: từ chối API key không hợp lệ từ {Ip}.",
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            await WriteProblemAsync(ctx, StatusCodes.Status401Unauthorized, "API key không hợp lệ.");
            return;
        }

        if (!TryConsumeRateLimit(cache, authenticated))
        {
            ctx.Response.Headers.RetryAfter = "60";
            await WriteProblemAsync(ctx, StatusCodes.Status429TooManyRequests,
                $"Vượt giới hạn {authenticated.RateLimitPerMinute} lượt/phút.");
            return;
        }

        // Ghi đè site đã resolve theo Host bằng site của key: agent gọi /mcp qua bất kỳ domain nào
        // cũng chỉ thao tác được đúng site mà key cho phép.
        currentSite.Set(authenticated.SiteId, authenticated.SiteSlug, authenticated.SiteTheme);
        ctx.Items[McpContextItems.ApiKey] = authenticated;

        await _next(ctx);
    }

    /// <summary>Đọc key từ header riêng, fallback sang Authorization: Bearer.</summary>
    private static string? ExtractKey(HttpRequest request)
    {
        var direct = request.Headers[ApiKeyHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(direct)) return direct.Trim();

        var auth = request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        return null;
    }

    /// <summary>
    /// Rate limit đơn giản theo key: đếm số lượt trong cửa sổ 1 phút. Đủ cho mục đích chặn agent
    /// chạy vòng lặp hỏng; không thay thế rate limit ở tầng reverse proxy.
    /// </summary>
    private static bool TryConsumeRateLimit(IMemoryCache cache, AuthenticatedApiKey key)
    {
        var window = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var cacheKey = $"mcp:rate:{key.KeyId}:{window}";

        var count = cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return 0;
        });

        if (count >= key.RateLimitPerMinute) return false;

        cache.Set(cacheKey, count + 1, TimeSpan.FromMinutes(2));
        return true;
    }

    private static async Task WriteProblemAsync(HttpContext ctx, int statusCode, string message)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsJsonAsync(new { error = message });
    }
}

/// <summary>Khoá HttpContext.Items dùng chung giữa middleware và MCP tool.</summary>
public static class McpContextItems
{
    public const string ApiKey = "mcp.apikey";
}
