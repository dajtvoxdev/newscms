using Microsoft.AspNetCore.Http;
using NewsCMS.Application.Site;

namespace NewsCMS.Web.Middleware;

/// <summary>
/// Site tồn tại nhưng IsActive = false → trả trang bảo trì (503) thay vì để request đi tiếp và 404.
/// Chạy ngay sau SiteResolveMiddleware nên site chắc chắn đã resolve được
/// (host không có mapping đã bị chặn 404 từ trước).
/// </summary>
public class MaintenanceMiddleware
{
    private readonly RequestDelegate _next;

    public MaintenanceMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ISiteResolver resolver)
    {
        // /mcp không đi qua trang bảo trì: site đang tắt chính là lúc agent cần sửa nội dung
        // nhất. Endpoint này đã tự xác thực bằng API key ở McpApiKeyMiddleware.
        if (context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Đọc lại từ resolver (đã cache) thay vì query DB mỗi request. Admin đổi IsActive
        // sẽ gọi SiteCacheSignal.Invalidate() nên cache không bị stale.
        var site = await resolver.ResolveAsync(context.Request.Host.Host, context.RequestAborted);

        if (site is { IsActive: false })
        {
            // 503 + Retry-After: search engine không index trang bảo trì thành nội dung site.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "3600";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(GetMaintenanceHtml());
            return;
        }

        await _next(context);
    }

    // Trang tự chứa, không font/asset ngoài: site đang tắt thì mọi thứ khác cũng
    // không đáng tin. Bố cục khoá trong 100dvh và mọi cỡ chữ dùng clamp() nên
    // không bao giờ phải scroll, kể cả màn 320px.
    private static string GetMaintenanceHtml() => """
        <!DOCTYPE html>
        <html lang="vi">
        <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
        <meta name="robots" content="noindex">
        <title>Đang bảo trì</title>
        <style>
          *, *::before, *::after { box-sizing: border-box; }

          :root {
            --paper: #f2efe7;
            --ink: #1b1815;
            --ink-soft: #6f675c;
            --rule: #d7d0c3;
            --accent: #ad4f29;
            --serif: "Iowan Old Style", "Palatino Linotype", Palatino, Georgia, "Times New Roman", serif;
            --mono: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, monospace;
          }

          @media (prefers-color-scheme: dark) {
            :root {
              --paper: #141210;
              --ink: #ece5d8;
              --ink-soft: #8d8478;
              --rule: #332e27;
              --accent: #d9764a;
            }
          }

          html, body { margin: 0; padding: 0; }

          body {
            height: 100vh;
            height: 100dvh;
            overflow: hidden;
            background: var(--paper);
            color: var(--ink);
            font-family: var(--serif);
            -webkit-font-smoothing: antialiased;
            display: grid;
            grid-template-rows: auto 1fr auto;
            gap: clamp(1rem, 3vh, 2.25rem);
            padding: clamp(1.25rem, 4vw, 3.25rem);
          }

          /* Lưới chấm mờ dần — chất giấy kỹ thuật, thay cho gradient trang trí. */
          body::before {
            content: "";
            position: fixed;
            inset: 0;
            background-image: radial-gradient(circle at 1px 1px, var(--rule) 1px, transparent 0);
            background-size: 30px 30px;
            opacity: .5;
            -webkit-mask-image: linear-gradient(to bottom, #000, transparent 68%);
            mask-image: linear-gradient(to bottom, #000, transparent 68%);
            pointer-events: none;
          }

          body > * { position: relative; }

          .bar {
            display: flex;
            justify-content: space-between;
            align-items: baseline;
            gap: 1rem;
            padding-bottom: clamp(.7rem, 2vh, 1rem);
            border-bottom: 1px solid var(--rule);
            font-family: var(--mono);
            font-size: clamp(.68rem, 1.6vw, .75rem);
            letter-spacing: .14em;
            text-transform: uppercase;
            color: var(--ink-soft);
          }

          .status { display: inline-flex; align-items: center; gap: .6rem; }

          .dot {
            width: 7px;
            height: 7px;
            flex: none;
            border-radius: 50%;
            background: var(--accent);
            animation: pulse 2.4s ease-in-out infinite;
          }

          @keyframes pulse { 0%, 100% { opacity: 1 } 50% { opacity: .25 } }

          .code { color: var(--ink); letter-spacing: .2em; }

          main { align-self: center; max-width: 46rem; }

          h1 {
            margin: 0 0 clamp(1.1rem, 3vh, 1.9rem);
            font-size: clamp(2.6rem, 8.5vw, 6rem);
            font-weight: 400;
            line-height: .95;
            letter-spacing: -.03em;
          }

          h1 em { font-style: italic; color: var(--accent); }

          p {
            margin: 0 0 clamp(1.6rem, 4vh, 2.6rem);
            max-width: 32ch;
            font-size: clamp(1rem, 1.5vw, 1.15rem);
            line-height: 1.55;
            color: var(--ink-soft);
          }

          button {
            display: inline-flex;
            align-items: center;
            gap: .75rem;
            padding: .9rem 1.6rem;
            border: 0;
            border-radius: 2px;
            background: var(--ink);
            color: var(--paper);
            font-family: var(--mono);
            font-size: .78rem;
            letter-spacing: .1em;
            text-transform: uppercase;
            cursor: pointer;
            transition: background-color .18s ease, transform .12s ease;
          }

          button:hover { background: var(--accent); }
          button:hover .arrow { transform: rotate(180deg); }
          button:focus-visible { outline: 2px solid var(--accent); outline-offset: 3px; }
          button:active { transform: translateY(1px); }

          .arrow {
            font-size: 1rem;
            line-height: 1;
            transition: transform .4s cubic-bezier(.16, 1, .3, 1);
          }

          /* Vạch chạy: báo "đang có người xử lý", không phải hiệu ứng cho vui. */
          .track { position: relative; height: 2px; background: var(--rule); overflow: hidden; }

          .track i {
            position: absolute;
            inset-block: 0;
            left: 0;
            width: 28%;
            background: var(--accent);
            animation: sweep 3.4s cubic-bezier(.65, 0, .35, 1) infinite;
          }

          @keyframes sweep { from { transform: translateX(-100%) } to { transform: translateX(357%) } }

          @media (prefers-reduced-motion: reduce) {
            .dot, .track i { animation: none; }
            .track i { width: 34%; }
            .arrow, button { transition: none; }
          }
        </style>
        </head>
        <body>
          <header class="bar">
            <span class="status"><span class="dot"></span>Bảo trì hệ thống</span>
            <span class="code">503</span>
          </header>

          <main>
            <h1>Tạm dừng<br><em>để nâng cấp.</em></h1>
            <p>Chúng tôi đang hoàn thiện một vài thứ. Trang sẽ hoạt động trở lại trong ít phút nữa.</p>
            <button type="button" onclick="location.reload()">
              Tải lại trang <span class="arrow" aria-hidden="true">↻</span>
            </button>
          </main>

          <footer class="track"><i></i></footer>
        </body>
        </html>
        """;
}
