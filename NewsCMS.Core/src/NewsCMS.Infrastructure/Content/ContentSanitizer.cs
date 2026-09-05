using Ganss.Xss;

namespace NewsCMS.Infrastructure.Content;

/// <summary>
/// Bọc HtmlSanitizer (https://github.com/mganss/HtmlSanitizer) với whitelist
/// phù hợp cho nội dung từ TinyMCE: cho phép định dạng cơ bản, ảnh, video, code,
/// nhưng chặn script, iframe lạ, on* attribute.
/// </summary>
public class ContentSanitizer
{
    private readonly HtmlSanitizer _sanitizer;
    private readonly HtmlSanitizer _builderSanitizer;

    // CSS properties allowed in inline style. Excludes positioning/layout properties
    // that could be abused for UI-redress/clickjacking/phishing overlays.
    private static readonly HashSet<string> AllowedCssProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        // Typography
        "color", "background-color", "font-family", "font-size", "font-weight", "font-style",
        "text-decoration", "text-align", "text-indent", "line-height", "letter-spacing",
        "word-spacing", "white-space", "vertical-align",
        // Spacing
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        // Borders
        "border", "border-top", "border-right", "border-bottom", "border-left",
        "border-color", "border-width", "border-style", "border-radius",
        "border-top-left-radius", "border-top-right-radius",
        "border-bottom-left-radius", "border-bottom-right-radius",
        // Display & layout (safe subset — no position, z-index, top/left/right/bottom, inset, float)
        "display", "width", "max-width", "min-width", "height", "max-height", "min-height",
        // Visual
        "background", "background-image", "background-size", "background-position",
        "background-repeat", "opacity", "box-shadow", "text-shadow",
        "list-style", "list-style-type", "list-style-position",
        // Tables
        "border-collapse", "border-spacing", "table-layout",
        // Misc
        "overflow", "overflow-x", "overflow-y", "cursor",
        "visibility", "word-break", "overflow-wrap", "hyphens",
    };

    /// <summary>
    /// CSS properties bổ sung cho builder: nới thêm layout/flex/grid để GrapesJS dựng được
    /// trang phức tạp, nhưng vẫn chặn position/z-index/inset/top/left/right/bottom/float
    /// để không tạo overlay clickjack hay phishing.
    /// </summary>
    private static readonly HashSet<string> BuilderExtraCssProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        // Flexbox
        "flex", "flex-direction", "flex-wrap", "flex-flow", "flex-grow", "flex-shrink", "flex-basis",
        "justify-content", "align-items", "align-content", "align-self", "order", "gap",
        "row-gap", "column-gap",
        // Grid
        "grid", "grid-template", "grid-template-columns", "grid-template-rows", "grid-template-areas",
        "grid-column", "grid-row", "grid-area", "grid-auto-columns", "grid-auto-rows", "grid-auto-flow",
        "place-items", "place-content", "place-self",
        // Layout mở rộng (vẫn chặn position/z-index/inset)
        "aspect-ratio", "object-fit", "object-position",
        // Transform (compositor-friendly, không gây reflow)
        "transform", "transform-origin",
        // Transition/animation nhẹ
        "transition", "transition-property", "transition-duration", "transition-timing-function",
        "animation", "animation-name", "animation-duration", "animation-timing-function",
        "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode",
        // Filter
        "filter", "backdrop-filter",
        // Clip
        "clip-path",
    };

    public ContentSanitizer()
    {
        _sanitizer = CreateBaseSanitizer();
        _builderSanitizer = CreateBuilderSanitizer();
    }

    public string Sanitize(string? html) => string.IsNullOrEmpty(html) ? string.Empty : _sanitizer.Sanitize(html);

    /// <summary>
    /// Sanitize HTML từ builder (GrapesJS): nới whitelist so với TinyMCE — cho section/article/nav/
    /// svg/data-*/CSS layout (flex/grid/transform), vẫn chặn script/on*/position/z-index.
    /// Custom JS đi đường riêng (Page.CustomJs), KHÔNG nằm trong HTML.
    /// </summary>
    public string SanitizeBuilder(string? html) =>
        string.IsNullOrEmpty(html) ? string.Empty : _builderSanitizer.Sanitize(html);

    private static HtmlSanitizer CreateBaseSanitizer()
    {
        var s = new HtmlSanitizer();
        ConfigureCommon(s, AllowedCssProperties);
        return s;
    }

    private static HtmlSanitizer CreateBuilderSanitizer()
    {
        var s = new HtmlSanitizer();

        // Tags bổ sung cho builder (ngoài những gì Common đã thêm).
        foreach (var tag in new[] {
            "section", "article", "aside", "nav", "header", "footer", "main",
            "details", "summary", "mark", "time", "abbr", "address",
            "svg", "path", "circle", "rect", "line", "polyline", "polygon", "g", "defs", "use", "symbol"
        })
            s.AllowedTags.Add(tag);

        // Attributes bổ sung cho SVG + builder data attributes.
        foreach (var attr in new[] {
            "viewBox", "fill", "stroke", "stroke-width", "d", "cx", "cy", "r",
            "x", "y", "x1", "y1", "x2", "y2", "points", "preserveAspectRatio",
            "xmlns", "aria-label", "aria-hidden", "role", "tabindex"
        })
            s.AllowedAttributes.Add(attr);

        // CSS: base + builder extras (vẫn chặn position/z-index/inset qua việc KHÔNG add chúng).
        var allCss = new HashSet<string>(AllowedCssProperties, StringComparer.OrdinalIgnoreCase);
        foreach (var p in BuilderExtraCssProperties) allCss.Add(p);
        ConfigureCommon(s, allCss);

        return s;
    }

    private static void ConfigureCommon(HtmlSanitizer sanitizer, HashSet<string> cssProperties)
    {
        // Cho phép thêm thẻ mà default whitelist của HtmlSanitizer chưa có
        sanitizer.AllowedTags.Add("figure");
        sanitizer.AllowedTags.Add("figcaption");
        sanitizer.AllowedTags.Add("iframe");   // chỉ cho embed YouTube/Vimeo hoặc PDF local
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedAttributes.Add("id");
        // HtmlSanitizer 8.x requires AllowedAttributes to be set BEFORE adding data-* pattern.
        // Add specific data attributes used by builder dynamic blocks explicitly.
        sanitizer.AllowedAttributes.Add("data-nc-block");
        sanitizer.AllowedAttributes.Add("data-nc-props");
        sanitizer.AllowedAttributes.Add("data-nc-body");
        // Code riêng từng khối: sid = scope selector, css/js = mã của khối (xem BlockCodeExtractor).
        sanitizer.AllowedAttributes.Add("data-nc-sid");
        sanitizer.AllowedAttributes.Add("data-nc-css");
        sanitizer.AllowedAttributes.Add("data-nc-js");
        sanitizer.AllowedAttributes.Add("data-*");
        sanitizer.AllowedAttributes.Add("frameborder");
        sanitizer.AllowedAttributes.Add("allowfullscreen");
        sanitizer.AllowedAttributes.Add("allow");
        sanitizer.AllowedAttributes.Add("src");
        sanitizer.AllowedAttributes.Add("title");
        sanitizer.AllowedAttributes.Add("loading");
        sanitizer.AllowedAttributes.Add("width");
        sanitizer.AllowedAttributes.Add("height");
        sanitizer.AllowedAttributes.Add("target");
        sanitizer.AllowedAttributes.Add("rel");
        sanitizer.AllowedAttributes.Add("sandbox");
        sanitizer.AllowedAttributes.Add("style");

        // Restrict inline CSS to safe properties only — blocks position/z-index overlay attacks
        foreach (var prop in cssProperties)
            sanitizer.AllowedCssProperties.Add(prop);

        sanitizer.AllowedSchemes.Add("data");  // cho phép base64 inline image nhỏ

        // Whitelist iframe chỉ cho YouTube/Vimeo (chống XSS qua iframe)
        sanitizer.PostProcessNode += (sender, e) =>
        {
            if (e.Node is not HtmlAgilityPack.HtmlNode node)
            {
                return;
            }

            if (node.Name == "a")
            {
                node.SetAttributeValue("rel", "noopener");
                return;
            }

            if (node.Name != "iframe")
            {
                return;
            }

            var src = node.GetAttributeValue("src", "");

            // YouTube embed
            if (src.StartsWith("https://www.youtube.com/")
                || src.StartsWith("https://www.youtube-nocookie.com/")
                || src.StartsWith("https://player.vimeo.com/"))
            {
                // Safe embed — no sandbox modifications needed
                return;
            }

            // Local PDF — require exact .pdf extension (not .Contains, which is bypassable)
            var uriPath = src;
            var queryIndex = src.IndexOf('?');
            if (queryIndex >= 0)
                uriPath = src[..queryIndex];
            var hashIndex = uriPath.IndexOf('#');
            if (hashIndex >= 0)
                uriPath = uriPath[..hashIndex];

            if (uriPath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)
                && uriPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                // PDF sandbox: no allow-scripts — prevents sandbox escape
                node.SetAttributeValue("sandbox", "allow-same-origin");
                node.SetAttributeValue("loading", "lazy");
                return;
            }

            // Unknown iframe source — remove
            node.Remove();
        };
    }
}
