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
}
