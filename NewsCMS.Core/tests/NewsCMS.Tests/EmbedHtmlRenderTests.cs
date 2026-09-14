using System.Text.RegularExpressions;
using NewsCMS.Application.Builder;
using Xunit;

namespace NewsCMS.Tests;

/// <summary>
/// Hợp đồng render của khối "Nhúng HTML" (data-nc-html): mã thô phải ra trang public NGUYÊN VĂN,
/// attribute bị gỡ, và <script> inline trong mã nhúng phải mang nonce CSP — thiếu nonce thì
/// CspNonceMiddleware (script-src 'self' 'nonce-…') chặn script chạy im lặng, đúng lỗi đã gặp
/// 2026-09-12 khi iframe bản đồ sống còn script khởi tạo widget thì chết.
/// </summary>
public sealed class EmbedHtmlRenderTests
{
    private const string Embed =
        "<iframe src=\"https://maps.example/embed?id=7\" width=\"100%\" height=\"500\" loading=\"lazy\"></iframe>"
        + "<script>initWidget()</script>"
        + "<script src=\"/lib/vendor.js\"></script>";

    private static string Encoded(string raw) =>
        raw.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    [Fact]
    public async Task Render_DoMaNhungNguyenVanVaGoAttribute()
    {
        using var fx = new BuilderRenderFixture("embed-render");
        var page = fx.AddPage("Trang nhúng", "trang-nhung",
            "<section><div data-nc-html=\"" + Encoded(Embed) + "\"></div></section>");

        var rendered = await fx.NewPageRenderer().RenderAsync(page.Id, "vi");

        Assert.NotNull(rendered);
        var html = rendered!.Html;
        Assert.Contains("<iframe src=\"https://maps.example/embed?id=7\" width=\"100%\" height=\"500\" loading=\"lazy\"></iframe>", html);
        Assert.Contains("initWidget()", html);
        Assert.DoesNotContain("data-nc-html", html);
    }

    [Fact]
    public async Task Render_ScriptInlineTrongMaNhung_MangNonceCsp()
    {
        using var fx = new BuilderRenderFixture("embed-nonce");
        var page = fx.AddPage("Trang nhúng", "trang-nhung",
            "<div data-nc-html=\"" + Encoded(Embed) + "\"></div>");

        var rendered = await fx.NewPageRenderer().RenderAsync(page.Id, "vi");

        Assert.NotNull(rendered);
        var html = rendered!.Html;

        // Script inline của mã nhúng phải có nonce — nếu không, CSP script-src 'nonce-…' chặn chạy.
        var inline = Regex.Match(html, "<script[^>]*>initWidget\\(\\)</script>", RegexOptions.IgnoreCase);
        Assert.True(inline.Success, "không tìm thấy script inline của mã nhúng");
        Assert.Matches("<script[^>]*nonce=\"[^\"]+\"[^>]*>", inline.Value);

        // Script có src không cần nonce và phải giữ nguyên src.
        Assert.Contains("<script src=\"/lib/vendor.js\"></script>", html);
    }

    [Fact]
    public async Task Render_NonceCuaMaNhung_KhopNonceCuaTrang()
    {
        // Cùng một nonce per-request cho mọi script inline: lấy nonce từ script CustomJs của page
        // (được PageRenderer bọc nonce sẵn) rồi đòi mã nhúng dùng đúng nonce đó.
        using var fx = new BuilderRenderFixture("embed-nonce-match");
        var page = fx.AddPage("Trang nhúng", "trang-nhung",
            "<div data-nc-html=\"" + Encoded("<script>initWidget()</script>") + "\"></div>");
        page.CustomJs = "console.log('page js')";
        fx.Db.SaveChanges();

        var rendered = await fx.NewPageRenderer().RenderAsync(page.Id, "vi");

        Assert.NotNull(rendered);
        var pageNonce = Regex.Match(rendered!.Html, "<script nonce=\"([^\"]+)\">console\\.log");
        Assert.True(pageNonce.Success, "không tìm thấy script CustomJs mang nonce");
        Assert.Contains($"<script nonce=\"{pageNonce.Groups[1].Value}\">initWidget()", rendered.Html);
    }
}
