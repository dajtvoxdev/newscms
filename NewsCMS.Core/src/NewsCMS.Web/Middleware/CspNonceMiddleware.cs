using System.Security.Cryptography;

namespace NewsCMS.Web.Middleware;

/// <summary>
/// Sinh CSP nonce per-request và chèn header Content-Security-Policy. Nonce được lưu trong
/// HttpContext.Items để PageRenderer đọc khi sinh inline script/style. Chỉ áp dụng cho theme
/// Universal (site có DefaultTheme = "Universal"); các theme RCL cũ không bị ảnh hưởng.
/// </summary>
public class CspNonceMiddleware
{
    public const string NonceItemKey = "__CspNonce";

    private readonly RequestDelegate _next;

    public CspNonceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Chỉ áp dụng CSP cho theme Universal — theme RCL cũ có thể dùng inline script riêng.
        var theme = ctx.Items.TryGetValue(Theming.ThemeEndpointMatcherPolicy.ThemeItemKey, out var t)
            ? t as string : null;

        if (!string.Equals(theme, "Universal", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        // Bỏ qua static assets (CSS/JS/image từ wwwroot) — CSP chỉ cần cho HTML responses.
        // Bỏ qua luôn /Admin: trang quản trị dùng inline script (TinyMCE, media picker,
        // repeater form) và tải TinyMCE từ CDN, nên script-src 'self' 'nonce-...' sẽ chặn
        // sạch — editor không khởi tạo được và form lưu thiếu dữ liệu.
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_nc/site/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToBase64String(nonceBytes);
        ctx.Items[NonceItemKey] = nonce;

        // CSP: nonce cho script; style cho phép inline.
        // Site Chu Kafe dựng hoàn toàn bằng inline style="..." (hàng chục style khác nhau).
        // Theo CSP, 'unsafe-inline' trong style-src bị BỎ QUA khi header còn chứa 'nonce-'
        // hoặc 'sha256-' — nên kể cả 'nonce + unsafe-inline + unsafe-hashes' vẫn bị Chromium
        // block toàn bộ style attribute (128 lỗi "unsafe-inline is ignored if nonce present").
        // Cách tương thích nhất: tách riêng — script-src giữ 'nonce-' (chặt), style-src bỏ
        // 'nonce-' và chỉ dùng 'unsafe-inline' cho style. Khi có yêu cầu siết lại style-src,
        // hãy chuyển compiledHtml sang class/stylesheet thay vì style="..." rồi bật lại nonce.
        var csp = $"default-src 'self'; " +
                  $"script-src 'self' 'nonce-{nonce}'; " +
                  $"style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                  $"font-src 'self' https://fonts.gstatic.com; " +
                  $"img-src 'self' data: https:; " +
                  $"connect-src 'self'; " +
                  // frame-src từng là 'none' — chặn luôn iframe bản đồ/widget mà khối "Nhúng HTML"
                  // (data-nc-html) sinh ra, dù mã nhúng đã đổ vào trang nguyên vẹn. Khung nhúng là
                  // nội dung admin chủ đích dán vào (quyền Builder.Code.Manage), và trang bị nhúng vẫn
                  // tự bảo vệ bằng X-Frame-Options/CSP frame-ancestors của chính nó, nên mở cho https
                  // là mức chặt hợp lý: chặn frame http/data/javascript, không chặn nguồn nhúng thật.
                  $"frame-src https:; " +
                  $"object-src 'none'; " +
                  $"base-uri 'self'";

        ctx.Response.Headers.ContentSecurityPolicy = csp;

        await _next(ctx);
    }
}
