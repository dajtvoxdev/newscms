using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.ContentTypes;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối động hiển thị toàn bộ nội dung giàu (HTML) của bài viết hoặc phần mô tả chi tiết của sản phẩm.
/// Bao bọc trong lớp CSS "nc-post-body" để hưởng đầy đủ kiểu dáng typography của hệ thống.
/// </summary>
public sealed class EntityContentBlock : IDynamicBlock
{
    public string Key => "entity-content";

    private readonly IContentTypeRegistry _contentTypes;

    public EntityContentBlock(IContentTypeRegistry contentTypes) => _contentTypes = contentTypes;

    public BlockDescriptor Descriptor => new(
        Label: "Nội dung chi tiết",
        Category: "Chi tiết",
        Description: "Nội dung đầy đủ của bài viết hoặc mô tả chi tiết sản phẩm.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z\"/>" +
                 "<polyline points=\"14 2 14 8 20 8\"/><line x1=\"16\" y1=\"13\" x2=\"8\" y2=\"13\"/>" +
                 "<line x1=\"16\" y1=\"17\" x2=\"8\" y2=\"17\"/><polyline points=\"10 9 9 9 8 9\"/></svg>",
        Presets:
        [
            new("prose-standard", "Chiều rộng bài báo 820px",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"6\" y=\"4\" width=\"36\" height=\"4\" rx=\"1\"/><rect x=\"6\" y=\"11\" width=\"36\" height=\"3\" rx=\"1\"/><rect x=\"6\" y=\"17\" width=\"36\" height=\"3\" rx=\"1\"/><rect x=\"6\" y=\"23\" width=\"28\" height=\"3\" rx=\"1\"/></svg>",
                "{\"maxWidth\":\"820px\",\"fontSize\":\"base\",\"lineHeight\":\"relaxed\"}"),
            new("full-width", "Tràn chiều rộng khối cha",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"0\" y=\"4\" width=\"48\" height=\"4\" rx=\"1\"/><rect x=\"0\" y=\"11\" width=\"48\" height=\"3\" rx=\"1\"/><rect x=\"0\" y=\"17\" width=\"48\" height=\"3\" rx=\"1\"/><rect x=\"0\" y=\"23\" width=\"38\" height=\"3\" rx=\"1\"/></svg>",
                "{\"maxWidth\":\"none\",\"fontSize\":\"base\",\"lineHeight\":\"relaxed\"}"),
            new("reading-large", "Chữ lớn dễ đọc 768px",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"8\" y=\"4\" width=\"32\" height=\"5\" rx=\"1\"/><rect x=\"8\" y=\"12\" width=\"32\" height=\"4\" rx=\"1\"/><rect x=\"8\" y=\"19\" width=\"32\" height=\"4\" rx=\"1\"/><rect x=\"8\" y=\"26\" width=\"24\" height=\"4\" rx=\"1\"/></svg>",
                "{\"maxWidth\":\"768px\",\"fontSize\":\"lg\",\"lineHeight\":\"loose\"}")
        ],
        Props:
        [
            new("maxWidth", BlockPropTypes.Select, "Chiều rộng tối đa", BlockPropGroups.Display, "none",
                Options:
                [
                    new("none", "Tràn khối cha (100%)"),
                    new("640px", "Gọn gàng (640px)"),
                    new("768px", "Đọc sách (768px)"),
                    new("820px", "Chuẩn bài báo (820px)"),
                    new("1024px", "Rộng (1024px)")
                ]),
            new("fontSize", BlockPropTypes.Select, "Cỡ chữ nội dung", BlockPropGroups.Display, "base",
                Options:
                [
                    new("sm", "Nhỏ (0.925rem)"),
                    new("base", "Vừa chuẩn (1rem)"),
                    new("lg", "Lớn (1.125rem)")
                ]),
            new("lineHeight", BlockPropTypes.Select, "Giãn dòng", BlockPropGroups.Display, "relaxed",
                Options:
                [
                    new("normal", "Bình thường (1.6)"),
                    new("relaxed", "Thoáng đãng (1.8)"),
                    new("loose", "Rộng rãi (2.0)")
                ])
        ],
        DefaultPropsJson: "{\"maxWidth\":\"none\",\"fontSize\":\"base\",\"lineHeight\":\"relaxed\"}",
        EntityScoped: true);

    public async Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var maxWidth = props.GetString("maxWidth") ?? "none";
        var fontSizeProp = props.GetString("fontSize") ?? "base";
        var lineHeightProp = props.GetString("lineHeight") ?? "relaxed";

        var fontSize = fontSizeProp switch
        {
            "sm" => "0.925rem",
            "lg" => "1.125rem",
            _ => "1rem"
        };

        var lineHeight = lineHeightProp switch
        {
            "normal" => "1.6",
            "loose" => "2.0",
            _ => "1.8"
        };

        var maxWStyle = maxWidth != "none" ? $"max-width:{maxWidth};margin-left:auto;margin-right:auto;" : "";
        var style = $"color:var(--color-ink);font-size:{fontSize};line-height:{lineHeight};{maxWStyle}";

        // Khi không có RouteContext
        if (context.Route is null)
        {
            return $"<div data-nc-part=\"wrapper\" class=\"nc-post-body\" style=\"{style};padding:16px 0;opacity:0.75\">" +
                   "<p><em data-nc-part=\"placeholder-text\">(Nội dung chi tiết của bài viết hoặc mô tả sản phẩm sẽ hiển thị tại đây khi xem trên trang public.)</em></p>" +
                   "<p>Khối này tự động nhúng toàn bộ mã HTML đã được định dạng và khử độc từ trình soạn thảo quản trị.</p>" +
                   "</div>";
        }

        var detail = await _contentTypes.LoadDetailAsync(context.Route.RouteType, context.Route.EntityId, ct);
        if (detail is null || string.IsNullOrWhiteSpace(detail.Body))
            return string.Empty;

        // Content đã được khử độc an toàn qua ContentSanitizer khi lưu ở admin
        var html = $"<div data-nc-part=\"wrapper\" class=\"nc-post-body\" style=\"{style}\">{detail.Body}</div>";

        // entity-content là nơi DUY NHẤT trên theme Universal render nội dung bài viết/sản phẩm,
        // nên video/audio chèn từ TinyMCE (upload local hay chọn từ thư viện) chỉ có thể được
        // ép full-width + skin Plyr từ đây — theme HaiLuuNguoc có site.css/Post/Detail.cshtml
        // riêng, nhưng site dùng Universal (site builder) không đi qua đường đó.
        // Chỉ nhúng CSS/JS khi nội dung thực sự có media, tránh tải thừa cho bài không có video.
        // Script <script> KHÔNG có src ở dưới sẽ được PageRenderer.StampInlineScriptNonce tự
        // gắn nonce CSP khi ráp trang — không tự thêm nonce ở đây (nonce sinh per-request, không
        // biết được tại thời điểm block này chạy).
        if (detail.Body.Contains("<video", StringComparison.OrdinalIgnoreCase)
            || detail.Body.Contains("<audio", StringComparison.OrdinalIgnoreCase))
        {
            html += "<style>.nc-post-body video,.nc-post-body audio{width:100%;height:auto;max-width:100%;border-radius:12px}</style>"
                  + "<link rel=\"stylesheet\" href=\"/lib/plyr/plyr.css\">"
                  + "<script src=\"/lib/plyr/plyr.polyfilled.min.js\"></script>"
                  + "<script>(function(){"
                  + "function boot(){"
                  + "document.querySelectorAll('.nc-post-body video,.nc-post-body audio').forEach(function(el){"
                  + "if(el.closest('.plyr'))return;"
                  + "try{new Plyr(el,{iconUrl:location.origin+'/lib/plyr/plyr.svg',captions:{active:true,language:'vi',update:true},ratio:0,"
                  + "controls:['play-large','play','progress','current-time','mute','volume','captions','settings','fullscreen']});}catch(e){}"
                  + "});"
                  + "}"
                  + "if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',boot);}else{boot();}"
                  + "})();</script>";
        }

        return html;
    }
}

