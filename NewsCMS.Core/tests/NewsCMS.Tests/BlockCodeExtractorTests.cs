using NewsCMS.Infrastructure.Builder;

namespace NewsCMS.Tests;

/// <summary>
/// Scope CSS của khối (data-nc-css) qua API công khai <see cref="BlockCodeExtractor.Extract"/>.
///
/// Hai hành vi là gốc của bug "code riêng của khối không có tác dụng":
///   • Selector đơn phải khớp CẢ chính khối (<c>[sid]:is(sel)</c>), vì GrapesJS đặt id tự sinh
///     lên chính phần tử đang chọn — người dùng gõ <c>#it5n{…}</c> nhắm chính khối là chuyện thường.
///   • Tiền tố <c>^</c> giữ nguyên selector (không scope) để nhắm phần tử NGOÀI khối (vd section cha),
///     điều CSS thuần không viết được từ trong scope con cháu.
/// </summary>
public sealed class BlockCodeExtractorTests
{
    private const string Sid = "iqlk";

    private static string ScopeOf(string sid) => $"[data-nc-sid=\"{sid}\"]";

    private static string Div(string css) =>
        $"<div data-nc-sid=\"{Sid}\" data-nc-css=\"{System.Net.WebUtility.HtmlEncode(css)}\">x</div>";

    [Fact]
    public void Extract_BareDeclarations_WrapBlockItself()
    {
        var result = BlockCodeExtractor.Extract(Div("color:red;padding:8px"));

        Assert.Contains($"{ScopeOf(Sid)}{{color:red;padding:8px}}", result.Css);
        // Attribute code đã bị gỡ khỏi HTML để không lưu hai bản.
        Assert.DoesNotContain("data-nc-css", result.Html);
    }

    [Fact]
    public void Extract_SingleSelector_AlsoMatchesBlockItself()
    {
        // Người dùng mở modal ở khối ảnh (#it5n là chính khối) rồi gõ "#it5n{...}".
        // Hai dạng được gộp vào một rule bằng dấu phẩy: "[sid] #it5n,[sid]:is(#it5n){...}".
        var result = BlockCodeExtractor.Extract(Div("#it5n{height:auto}"));

        Assert.Contains($"{ScopeOf(Sid)} #it5n", result.Css);      // dạng con cháu
        Assert.Contains($"{ScopeOf(Sid)}:is(#it5n)", result.Css);  // dạng chính khối
        Assert.Contains("{height:auto}", result.Css);
    }

    [Fact]
    public void Extract_CaretPrefix_LeavesSelectorUnscoped()
    {
        // ^#iqlk nhắm section CHA — phải giữ nguyên, không thêm scope.
        var result = BlockCodeExtractor.Extract(Div("^#iqlk{height:auto}"));

        Assert.Contains("#iqlk{height:auto}", result.Css);
        Assert.DoesNotContain($"{ScopeOf(Sid)} #iqlk", result.Css);
        Assert.DoesNotContain(":is(#iqlk)", result.Css);
    }

    [Fact]
    public void Extract_AmpersandPrefix_AttachesToBlock()
    {
        var result = BlockCodeExtractor.Extract(Div("&:hover{opacity:.8}"));

        Assert.Contains($"{ScopeOf(Sid)}:hover{{opacity:.8}}", result.Css);
    }

    [Fact]
    public void Extract_RuleInsideMediaQuery_IsScoped_ButAtRuleIsNot()
    {
        // Đúng snippet người dùng báo lỗi: rule trong @media phải được scope, dòng @media thì không.
        var result = BlockCodeExtractor.Extract(
            Div("@media (max-width: 768px){#it5n{width:100%}}"));

        Assert.Contains("@media (max-width: 768px){", result.Css);
        Assert.Contains($"{ScopeOf(Sid)} #it5n", result.Css);
        Assert.Contains($"{ScopeOf(Sid)}:is(#it5n)", result.Css);
        Assert.Contains("{width:100%}", result.Css);
        // @media không bị biến thành "[sid] @media".
        Assert.DoesNotContain($"{ScopeOf(Sid)} @media", result.Css);
    }

    [Fact]
    public void Extract_DescendantCombinator_DoesNotEmitSelfMatch()
    {
        // Có dấu tổ hợp (khoảng trắng) → ý người dùng rõ là con cháu, KHÔNG sinh dạng :is().
        var result = BlockCodeExtractor.Extract(Div(".card .title{font-weight:700}"));

        Assert.Contains($"{ScopeOf(Sid)} .card .title{{font-weight:700}}", result.Css);
        Assert.DoesNotContain(":is(", result.Css);
    }

    [Fact]
    public void Extract_PseudoElement_DoesNotEmitSelfMatch()
    {
        // :is() không nhận pseudo-element; sinh ra sẽ làm trình duyệt bỏ CẢ rule.
        var result = BlockCodeExtractor.Extract(Div(".x::before{content:''}"));

        Assert.Contains($"{ScopeOf(Sid)} .x::before{{content:''}}", result.Css);
        Assert.DoesNotContain(":is(", result.Css);
    }

    [Fact]
    public void Extract_NoCodeAttributes_ReturnsHtmlUnchanged()
    {
        const string html = "<section><p>không có code khối</p></section>";
        var result = BlockCodeExtractor.Extract(html);

        Assert.Equal(html, result.Html);
        Assert.Equal(string.Empty, result.Css);
    }
}
