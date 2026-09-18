using NewsCMS.Infrastructure.Content;
using Xunit;

namespace NewsCMS.Tests;

/// <summary>
/// Hợp đồng của ContentSanitizer: những gì cấu hình nói "cho phép" phải thật sự sống sót qua
/// Sanitize. data-* từng bị gỡ im lặng dù AllowedAttributes có "data-*" — HtmlSanitizer 8.x
/// khoá data attributes sau cờ riêng AllowDataAttributes, nên cấu hình bằng pattern là không đủ.
/// </summary>
public sealed class ContentSanitizerTests
{
    private readonly ContentSanitizer _s = new();

    [Fact]
    public void SanitizeBuilder_KeepsDataAttributes()
    {
        var html = _s.SanitizeBuilder(
            "<div data-label=\"Giá niêm yết\" data-foo=\"bar\" onclick=\"evil()\">nội dung</div>");

        Assert.Contains("data-label", html);
        Assert.Contains("data-foo", html);
        Assert.DoesNotContain("onclick", html);
    }

    [Fact]
    public void SanitizeBuilder_KeepsDataNcAttributes()
    {
        var html = _s.SanitizeBuilder(
            "<div data-nc-block=\"post-list\" data-nc-props='{\"count\":6}'></div>");

        Assert.Contains("data-nc-block", html);
        Assert.Contains("data-nc-props", html);
    }

    [Fact]
    public void Sanitize_KeepsDataAttributes_Too()
    {
        // Cùng hợp đồng cho sanitizer nội dung TinyMCE: cấu hình data-* nằm ở ConfigureCommon
        // dùng chung, hai đường sanitize phải hành xử như nhau.
        var html = _s.Sanitize("<span data-campaign=\"hot 2026\">TAKUMI VC1</span>");

        Assert.Contains("data-campaign", html);
    }

    [Fact]
    public void SanitizeBuilder_KeepsEmbedCodeInsideDataNcHtml()
    {
        // Nền móng của khối "Nhúng HTML": mã nằm trong GIÁ TRỊ attribute nên sanitizer không lọc,
        // BlockCodeExtractor mới đổ lại vào ruột khối lúc render. Nếu ngày nào sanitizer bắt đầu
        // gọt giá trị data-* thì tính năng chết âm thầm — test này phải đỏ trước.
        var embed = System.Net.WebUtility.HtmlEncode(
            "<iframe src=\"https://maps.example/x\"></iframe><script>init()</script>");

        var html = _s.SanitizeBuilder($"<div data-nc-html=\"{embed}\"></div>");

        Assert.Contains("data-nc-html", html);
        Assert.Contains("maps.example/x", html);
        Assert.Contains("init()", html);
        // Đối chứng: cùng thẻ script đó nằm THẲNG trong HTML thì bị cắt sạch. Đó chính là lý do
        // khối nhúng phải khoá sau Builder.Code.Manage — nó đi vòng qua bộ lọc này.
        Assert.DoesNotContain("<script", _s.SanitizeBuilder("<div><script>init()</script></div>"));
    }

    [Fact]
    public void Sanitize_KeepsVideoUploadedFromEditor()
    {
        // Đúng markup _TinyMce.cshtml sinh ra khi upload video từ máy. Thiếu bất kỳ
        // phần nào dưới đây thì video biến mất lúc lưu bài — đã xảy ra thật.
        var html = _s.Sanitize(
            "<figure class=\"cms-media-preview\">"
            + "<video controls preload=\"metadata\" poster=\"/uploads/poster.jpg\" "
            + "style=\"max-width:100%;height:auto;border-radius:12px;\">"
            + "<source src=\"/uploads/clip.mp4\" type=\"video/mp4\">"
            + "Trình duyệt không hỗ trợ phát video."
            + "</video></figure>");

        Assert.Contains("<video", html);
        Assert.Contains("<source", html);
        Assert.Contains("controls", html);
        Assert.Contains("/uploads/clip.mp4", html);
        Assert.Contains("type=\"video/mp4\"", html);
        Assert.Contains("poster=\"/uploads/poster.jpg\"", html);
        Assert.Contains("preload", html);
    }

    [Fact]
    public void Sanitize_KeepsAudioUploadedFromEditor()
    {
        var html = _s.Sanitize(
            "<figure class=\"cms-media-preview\">"
            + "<audio controls preload=\"metadata\" style=\"width:100%\">"
            + "<source src=\"/uploads/track.mp3\" type=\"audio/mpeg\">"
            + "</audio></figure>");

        Assert.Contains("<audio", html);
        Assert.Contains("<source", html);
        Assert.Contains("controls", html);
        Assert.Contains("/uploads/track.mp3", html);
    }

    [Fact]
    public void Sanitize_KeepsPlaysinlineAndFullWidthVideo()
    {
        // Markup mới từ _TinyMce.cshtml: thêm playsinline (iOS không force fullscreen)
        // và width:100% trong style để video rộng toàn bộ .post-content.
        var html = _s.Sanitize(
            "<figure class=\"cms-media-preview\">"
            + "<video controls preload=\"metadata\" playsinline poster=\"/uploads/poster.jpg\""
            + " style=\"width:100%;height:auto;max-width:100%;border-radius:12px;\">"
            + "<source src=\"/uploads/clip.mp4\" type=\"video/mp4\">"
            + "Trình duyệt không hỗ trợ phát video."
            + "</video></figure>");

        Assert.Contains("<video", html);
        Assert.Contains("playsinline", html);
        Assert.Contains("controls", html);
        Assert.Contains("max-width", html);
        Assert.Contains("/uploads/clip.mp4", html);
    }

    [Fact]
    public void Sanitize_VanChanScriptTrongTheVideo()
    {
        // Nới whitelist cho video không được mở đường cho XSS qua thẻ con.
        var html = _s.Sanitize(
            "<video controls><source src=\"/uploads/clip.mp4\" onerror=\"evil()\">"
            + "<script>alert(1)</script></video>");

        Assert.Contains("<video", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("onerror", html);
    }
}
