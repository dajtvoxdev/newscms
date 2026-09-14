using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Bóc CSS/JS gắn riêng từng khối ra khỏi HTML để nối vào CSS/JS của trang lúc render.
///
/// Vì sao code khối nằm trong attribute HTML chứ không nối sẵn vào Page.CustomCss/CustomJs:
/// CompiledHtml là nguồn sự thật duy nhất. Nếu builder nối code khối vào CustomCss thì lần mở
/// sau code đó lại được nạp vào ô soạn thảo rồi lưu thêm một bản — đúng lỗi phình vô hạn đã gặp
/// với nc-builder.css. Giữ trong HTML thì xoá khối là xoá luôn code của nó, không sót rác.
///
/// Quy ước attribute:
///   data-nc-sid  — id ổn định của khối, dùng làm scope selector.
///   data-nc-css  — CSS của khối. Chỉ có khai báo (không dấu ngoặc) → bọc vào chính khối.
///                  Có selector → mọi selector được thêm tiền tố scope; <c>&amp;</c> = chính khối;
///                  <c>^</c> = KHÔNG scope (nhắm phần tử ngoài khối, vd section cha).
///   data-nc-js   — JS của khối, được bọc IIFE với biến <c>root</c> trỏ tới chính khối.
///   data-nc-html — HTML thô người dùng tự nhúng. Đổ thẳng vào ruột khối lúc render và KHÔNG đi
///                  qua ContentSanitizer (đó chính là mục đích: snippet bên thứ ba, iframe,
///                  script…). Vì vậy quyền ghi nó khoá sau Builder.Code.Manage / scope
///                  builder.code, ngang với CustomJs.
/// </summary>
public static class BlockCodeExtractor
{
    /// <summary>HTML đã gỡ attribute code, kèm CSS/JS đã scope để nối vào trang.</summary>
    public sealed record Result(string Html, string Css, string Js);

    private const string SidAttr = "data-nc-sid";
    private const string CssAttr = "data-nc-css";
    private const string JsAttr = "data-nc-js";
    private const string HtmlAttr = "data-nc-html";

    /// <summary>
    /// Tách code khối khỏi <paramref name="html"/>. Không có attribute nào thì trả nguyên HTML
    /// (không đi qua HtmlAgilityPack) để không đổi markup của tuyệt đại đa số trang.
    /// </summary>
    public static Result Extract(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return new Result(html ?? string.Empty, string.Empty, string.Empty);

        var hasCss = html.Contains(CssAttr, StringComparison.OrdinalIgnoreCase);
        var hasJs = html.Contains(JsAttr, StringComparison.OrdinalIgnoreCase);
        var hasEmbed = html.Contains(HtmlAttr, StringComparison.OrdinalIgnoreCase);
        if (!hasCss && !hasJs && !hasEmbed) return new Result(html, string.Empty, string.Empty);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var nodes = doc.DocumentNode.SelectNodes($"//*[@{CssAttr}] | //*[@{JsAttr}] | //*[@{HtmlAttr}]");
        if (nodes is null || nodes.Count == 0) return new Result(html, string.Empty, string.Empty);

        var css = new StringBuilder();
        var js = new StringBuilder();
        var autoIndex = 0;

        foreach (var node in nodes)
        {
            // DeEntitizeValue, KHÔNG GetAttributeValue: CSS/JS chứa >, &, dấu nháy nên được lưu
            // dạng entity; đọc thô sẽ ra code sai cú pháp.
            var blockCss = node.Attributes[CssAttr]?.DeEntitizeValue;
            var blockJs = node.Attributes[JsAttr]?.DeEntitizeValue;

            var blockHtml = node.Attributes[HtmlAttr]?.DeEntitizeValue;

            var hasBlockCss = !string.IsNullOrWhiteSpace(blockCss);
            var hasBlockJs = !string.IsNullOrWhiteSpace(blockJs);
            var hasBlockHtml = !string.IsNullOrWhiteSpace(blockHtml);

            // Khối nhúng chưa dán mã: vẫn phải gỡ attribute để trang public không lộ data-nc-html="".
            if (node.Attributes[HtmlAttr] is not null && !hasBlockHtml)
                node.Attributes.Remove(HtmlAttr);

            if (!hasBlockCss && !hasBlockJs && !hasBlockHtml) continue;

            // Khối chưa có sid (vd. HTML viết tay qua MCP) → gán tạm để scope vẫn đúng.
            var sid = node.GetAttributeValue(SidAttr, string.Empty);
            if (string.IsNullOrWhiteSpace(sid))
            {
                sid = $"auto{++autoIndex}";
                node.SetAttributeValue(SidAttr, sid);
            }

            if (hasBlockCss)
            {
                css.Append("\n/* block ").Append(sid).Append(" */\n");
                css.Append(ScopeCss(blockCss!, sid));
                node.Attributes.Remove(CssAttr);
            }

            if (hasBlockJs)
            {
                js.Append("\n/* block ").Append(sid).Append(" */\n");
                js.Append(WrapJs(blockJs!, sid));
                node.Attributes.Remove(JsAttr);
            }

            if (hasBlockHtml)
            {
                // Đổ nguyên văn vào ruột khối, thay hẳn ruột cũ. Builder không lưu preview vào
                // CompiledHtml nên ruột cũ luôn rỗng; MCP/HTML viết tay thì ý định cũng là thay.
                node.InnerHtml = blockHtml!;
                node.Attributes.Remove(HtmlAttr);
            }
        }

        return new Result(doc.DocumentNode.OuterHtml, css.ToString().Trim(), js.ToString().Trim());
    }

    /// <summary>
    /// HTML có mang khối nhúng mã thô (<c>data-nc-html</c>) hay không. Tầng API dùng để đòi quyền
    /// Builder.Code.Manage: nội dung nhúng KHÔNG đi qua ContentSanitizer nên ngang mức rủi ro với
    /// CustomJs — cho phép chạy mã tuỳ ý trên trình duyệt của khách.
    /// </summary>
    public static bool CarriesRawHtml(string? html) =>
        !string.IsNullOrEmpty(html) && html.Contains(HtmlAttr, StringComparison.OrdinalIgnoreCase);

    /// <summary>Selector đại diện một khối trong CSS/JS đã scope.</summary>
    public static string ScopeSelector(string sid) => $"[{SidAttr}=\"{sid}\"]";

    /// <summary>
    /// Thêm tiền tố scope cho mọi selector trong CSS của khối, để CSS khối này không rò rỉ
    /// sang khối khác. Selector bắt đầu bằng <c>&amp;</c> gắn thẳng vào khối (vd. <c>&amp;:hover</c>).
    ///
    /// Selector "đơn" (không có dấu tổ hợp) sinh THÊM dạng khớp chính khối:
    /// <c>#anh</c> → <c>[sid] #anh, [sid]:is(#anh)</c>. Lý do: GrapesJS đặt id tự sinh lên chính
    /// phần tử đang chọn, nên người dùng mở modal ở khối ảnh rồi gõ <c>#it5n{…}</c> là chuyện
    /// thường xuyên — chỉ scope kiểu con cháu thì CSS đó âm thầm không khớp gì cả.
    ///
    /// Selector mở đầu bằng <c>^</c> được giữ nguyên (không scope), dành cho trường hợp phải nhắm
    /// phần tử NGOÀI khối như section cha.
    /// </summary>
    internal static string ScopeCss(string css, string sid)
    {
        var scope = ScopeSelector(sid);
        css = css.Trim();

        // Không có dấu '{' → người dùng chỉ gõ khai báo (color:red;padding:8px).
        if (!css.Contains('{')) return $"{scope}{{{css}}}\n";

        // Bắt phần selector: đứng ngay đầu chuỗi, sau '}' hoặc sau '{' (rule lồng trong @media).
        // Loại '@' khỏi lớp ký tự nên chính dòng @media/@supports không bị thêm scope.
        return Regex.Replace(css, @"(^|[}{])([^{}@]+)\{", m =>
        {
            var selectors = m.Groups[2].Value
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .SelectMany(s => ScopeOneSelector(s, scope));

            var joined = string.Join(",", selectors);
            return joined.Length == 0 ? m.Value : $"{m.Groups[1].Value}{joined}{{";
        }) + "\n";
    }

    /// <summary>
    /// Một selector của người dùng → một hoặc hai selector đã scope.
    /// Dạng khớp-chính-khối chỉ sinh khi an toàn: không có dấu tổ hợp (khi đó ý người dùng rõ ràng
    /// là con cháu) và không có pseudo-element (<c>:is()</c> không nhận pseudo-element; selector sai
    /// cú pháp trong danh sách làm trình duyệt bỏ CẢ rule).
    ///
    /// Tiền tố <c>^</c> là cửa thoát có chủ đích: giữ nguyên selector, không scope. Cần vì khối
    /// thường phải sửa CHÍNH PHẦN TỬ CHA của nó (vd ảnh muốn section bọc ngoài cao tự động trên
    /// mobile) — không có cách nào viết "tổ tiên" bằng CSS thuần từ trong scope.
    /// </summary>
    private static IEnumerable<string> ScopeOneSelector(string selector, string scope)
    {
        if (selector[0] == '^')
        {
            var raw = selector[1..].Trim();
            if (raw.Length > 0) yield return raw;
            yield break;
        }

        if (selector[0] == '&')
        {
            yield return scope + selector[1..];
            yield break;
        }

        yield return $"{scope} {selector}";

        if (CanTargetSelf(selector))
            yield return $"{scope}:is({selector})";
    }

    private static bool CanTargetSelf(string selector) =>
        !selector.Contains("::", StringComparison.Ordinal)
        && !LegacyPseudoElement.IsMatch(selector)
        && !Combinator.IsMatch(selector);

    /// <summary>Dấu tổ hợp: khoảng trắng, &gt;, +, ~ (ngoài dấu ngoặc thuộc tính/pseudo).</summary>
    private static readonly Regex Combinator =
        new(@"[\s>+~]", RegexOptions.Compiled);

    /// <summary>Pseudo-element viết một dấu hai chấm theo cú pháp CSS2.</summary>
    private static readonly Regex LegacyPseudoElement =
        new(@":(before|after|first-line|first-letter)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Bọc JS của khối trong IIFE có <c>root</c> = phần tử khối. Không tìm thấy khối thì thoát
    /// im lặng — trang không vỡ khi khối bị xoá mà JS còn sót ở đâu đó.
    /// </summary>
    internal static string WrapJs(string js, string sid)
    {
        var selector = ScopeSelector(sid).Replace("\\", "\\\\").Replace("'", "\\'");
        return
            "(function(){var root=document.querySelector('" + selector + "');if(!root)return;\n" +
            js.Trim() + "\n})();\n";
    }
}
