using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Dynamic block hiển thị một tài liệu PDF dưới dạng sách lật (StPageFlip).
/// Props: { "src": "/uploads/flipbook/profile-chu", "title": "...", "eyebrow": "...", "intro": "...",
///          "downloadUrl": "/uploads/documents/profile-chu-kafe.pdf", "downloadLabel": "Tải PDF", "eagerPages": 1 }.
///
/// PDF được rasterise sẵn thành WebP theo 3 kích thước (full/ small/ thumb/) + manifest.json;
/// block chỉ phát ra khung trang, ảnh do chu-flipbook.js nạp theo cửa sổ quanh trang đang xem
/// nên khách chỉ tải vài trăm KB thay vì cả file PDF gốc.
/// </summary>
public sealed class PdfFlipbookBlock : IDynamicBlock
{
    public string Key => "pdf-flipbook";

    // page-flip không kèm CSS trong dist/ → vendor hoá stylesheet gốc (src/Style/stPageFlip.css).
    // Thiếu file này thì .stf__item không có position:absolute và trang đang lật rơi xuống
    // dưới sách như một "bóng ma".
    private const string LibCssPath = "/css/vendor/page-flip.css";
    private const string CssPath = "/css/chu-flipbook.css";
    private const string LibPath = "/js/vendor/page-flip.browser.js";
    private const string JsPath = "/js/chu-flipbook.js";

    private readonly IWebHostEnvironment _env;

    public PdfFlipbookBlock(IWebHostEnvironment env) => _env = env;

    public BlockDescriptor Descriptor => new(
        Label: "Sách lật PDF",
        Category: "Nội dung",
        Description: "Hồ sơ/catalogue PDF đã rasterise, lật trang như sách thật.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<path d=\"M2 4h7a3 3 0 0 1 3 3v13a2.5 2.5 0 0 0-2.5-2.5H2Z\"/>" +
                 "<path d=\"M22 4h-7a3 3 0 0 0-3 3v13a2.5 2.5 0 0 1 2.5-2.5H22Z\"/></svg>",
        Presets: [],
        Props:
        [
            new BlockPropDescriptor("src", BlockPropTypes.Text, "Thư mục flipbook", BlockPropGroups.Data,
                Placeholder: "/uploads/flipbook/ten-tai-lieu",
                Hint: "Thư mục chứa manifest.json do script rasterise PDF sinh ra."),
            new BlockPropDescriptor("title", BlockPropTypes.Text, "Tiêu đề (aria/alt)", BlockPropGroups.Content),
            new BlockPropDescriptor("eyebrow", BlockPropTypes.Text, "Chữ nhỏ phía trên", BlockPropGroups.Content),
            new BlockPropDescriptor("intro", BlockPropTypes.Text, "Mô tả ngắn", BlockPropGroups.Content),
            new BlockPropDescriptor("downloadUrl", BlockPropTypes.Text, "Link tải PDF gốc", BlockPropGroups.Content),
            new BlockPropDescriptor("downloadLabel", BlockPropTypes.Text, "Nhãn nút tải",
                BlockPropGroups.Content, "Tải bản PDF"),
            new BlockPropDescriptor("showControls", BlockPropTypes.Checkbox, "Hiện nút điều hướng",
                BlockPropGroups.Display, "false"),
            new BlockPropDescriptor("eagerPages", BlockPropTypes.Number, "Số trang nạp sẵn",
                BlockPropGroups.Advanced, "1", Min: 1, Max: 20,
                Hint: "Nạp sẵn nhiều trang thì mở nhanh hơn nhưng tốn băng thông hơn.")
        ]);

    public Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var src = NormalizeSrc(props.GetString("src") ?? "/uploads/flipbook/profile-chu");

        var manifest = ReadManifest(src);
        if (manifest is null || manifest.PageCount <= 0)
            return Task.FromResult($"<!-- pdf-flipbook: không đọc được {src}/manifest.json -->");

        var title = props.GetString("title") ?? manifest.Title ?? "Hồ sơ năng lực";
        var eyebrow = props.GetString("eyebrow");
        var intro = props.GetString("intro");
        var downloadUrl = props.GetString("downloadUrl");
        var downloadLabel = props.GetString("downloadLabel") ?? "Tải bản PDF";
        var eager = Math.Clamp(props.GetInt("eagerPages", 1), 1, manifest.PageCount);
        var showControls = props.GetBool("showControls", false);
        var ratio = manifest.Ratio > 0 ? manifest.Ratio : 0.707;
        var ratioText = ratio.ToString("0.####", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append($"<link rel=\"stylesheet\" href=\"{Versioned(LibCssPath)}\">");
        sb.Append($"<link rel=\"stylesheet\" href=\"{Versioned(CssPath)}\">");

        sb.Append($"<section data-nc-part=\"wrapper\" class=\"chu-flip\" data-chu-flipbook data-src=\"{Enc(src)}\" data-ratio=\"{ratioText}\"");
        sb.Append($" data-small=\"{manifest.SmallWidth}\" data-full=\"{manifest.FullWidth}\"");
        sb.Append($" style=\"--chu-flip-ratio:{ratioText}\" tabindex=\"0\" role=\"region\" aria-label=\"{Enc(title)}\">");

        if (!string.IsNullOrWhiteSpace(eyebrow) || !string.IsNullOrWhiteSpace(intro))
        {
            sb.Append("<div data-nc-part=\"head\" class=\"chu-flip__head\" style=\"text-align:center;margin:0 0 28px\">");
            if (!string.IsNullOrWhiteSpace(eyebrow))
                sb.Append($"<p data-nc-part=\"eyebrow\" style=\"margin:0 0 10px;font-size:.75rem;letter-spacing:.22em;text-transform:uppercase;color:var(--color-accent-500)\">{Enc(eyebrow)}</p>");
            if (!string.IsNullOrWhiteSpace(intro))
                sb.Append($"<p data-nc-part=\"intro\" style=\"margin:0 auto;max-width:640px;line-height:1.8;color:var(--color-muted)\">{Enc(intro)}</p>");
            sb.Append("</div>");
        }

        sb.Append("<div data-nc-part=\"viewport\" class=\"chu-flip__viewport\" data-flip-viewport><div data-nc-part=\"book\" class=\"chu-flip__book\" data-flip-book>");
        for (var i = 1; i <= manifest.PageCount; i++)
        {
            var pageAlt = Enc($"{title} – trang {i}");
            if (i <= eager)
            {
                var small = $"{src}/small/{i:00}.{manifest.Ext}";
                var full = $"{src}/full/{i:00}.{manifest.Ext}";
                sb.Append($"<div data-nc-part=\"page\" class=\"chu-flip__page is-ready\" data-page=\"{i}\">");
                sb.Append($"<img data-nc-part=\"page-image\" class=\"is-loaded\" data-loaded=\"1\" src=\"{Enc(small)}\"");
                sb.Append($" srcset=\"{Enc(small)} {manifest.SmallWidth}w, {Enc(full)} {manifest.FullWidth}w\"");
                sb.Append(" sizes=\"(max-width: 767px) 92vw, 46vw\"");
                sb.Append($" width=\"{manifest.Width}\" height=\"{manifest.Height}\" alt=\"{pageAlt}\" fetchpriority=\"high\" decoding=\"async\"></div>");
            }
            else
            {
                sb.Append($"<div data-nc-part=\"page\" class=\"chu-flip__page\" data-page=\"{i}\">");
                sb.Append($"<img data-nc-part=\"page-image\" alt=\"{pageAlt}\" width=\"{manifest.Width}\" height=\"{manifest.Height}\" decoding=\"async\"></div>");
            }
        }
        sb.Append("</div>");

        // Gợi ý lật sách: nháy ở mép ngoài trang bìa, tự tắt sau lần lật đầu tiên.
        sb.Append($"<div data-nc-part=\"nudge\" class=\"chu-flip__nudge\" data-flip-nudge aria-hidden=\"true\">{IconSwipe}</div>");
        sb.Append("</div>");

        if (showControls)
        {
        sb.Append("<div data-nc-part=\"bar\" class=\"chu-flip__bar\">");
        sb.Append($"<button data-nc-part=\"prev\" type=\"button\" class=\"chu-flip__btn chu-flip__btn--icon\" data-flip-prev aria-label=\"Trang trước\">{IconPrev}</button>");
        sb.Append($"<span data-nc-part=\"counter\" class=\"chu-flip__counter\" data-flip-counter aria-live=\"polite\">1 / {manifest.PageCount}</span>");
        sb.Append($"<button data-nc-part=\"next\" type=\"button\" class=\"chu-flip__btn chu-flip__btn--icon\" data-flip-next aria-label=\"Trang sau\">{IconNext}</button>");
        sb.Append($"<button data-nc-part=\"fullscreen\" type=\"button\" class=\"chu-flip__btn\" data-flip-fullscreen aria-pressed=\"false\">{IconExpand}Toàn màn hình</button>");
        if (!string.IsNullOrWhiteSpace(downloadUrl))
            sb.Append($"<a data-nc-part=\"download\" class=\"chu-flip__btn\" href=\"{Enc(downloadUrl)}\" download>{IconDownload}{Enc(downloadLabel)}</a>");
        sb.Append("</div>");
        sb.Append("<p data-nc-part=\"hint\" class=\"chu-flip__hint\">Kéo mép trang hoặc dùng phím ← → để lật.</p>");
        }

        if (!string.IsNullOrWhiteSpace(downloadUrl))
            sb.Append($"<noscript><p style=\"text-align:center\"><a href=\"{Enc(downloadUrl)}\">{Enc(downloadLabel)}</a></p></noscript>");

        sb.Append("</section>");

        // Script same-origin nên qua được CSP script-src 'self'; defer giữ đúng thứ tự lib → init.
        sb.Append($"<script src=\"{Versioned(LibPath)}\" defer></script>");
        sb.Append($"<script src=\"{Versioned(JsPath)}\" defer></script>");

        return Task.FromResult(sb.ToString());
    }

    private ManifestInfo? ReadManifest(string src)
    {
        try
        {
            var root = _env.WebRootPath;
            if (string.IsNullOrEmpty(root)) return null;
            var path = Path.GetFullPath(Path.Combine(root, src.TrimStart('/').Replace('/', Path.DirectorySeparatorChar), "manifest.json"));
            // Chặn path traversal qua props: manifest phải nằm trong wwwroot.
            if (!path.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return null;
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            var width = GetInt(r, "width", 1240);
            var height = GetInt(r, "height", 1754);
            var small = width;
            var full = width;
            if (r.TryGetProperty("sizes", out var sizes) && sizes.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sizes.EnumerateArray())
                {
                    var dir = s.TryGetProperty("dir", out var d) ? d.GetString() : null;
                    var w = GetInt(s, "width", 0);
                    if (w <= 0) continue;
                    if (dir == "small") small = w;
                    else if (dir == "full") full = w;
                }
            }

            return new ManifestInfo(
                GetInt(r, "pageCount", 0),
                r.TryGetProperty("title", out var t) ? t.GetString() : null,
                width, height,
                r.TryGetProperty("ratio", out var ra) && ra.ValueKind == JsonValueKind.Number ? ra.GetDouble() : (double)width / height,
                small, full,
                (r.TryGetProperty("ext", out var e) ? e.GetString() : null) ?? "webp");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Thêm ?v=timestamp để đổi file là khách nhận bản mới, không cần xoá cache thủ công.</summary>
    private string Versioned(string path) => BlockAssets.Versioned(_env, path);

    private static int GetInt(JsonElement el, string name, int fallback)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

    private static string NormalizeSrc(string src)
    {
        var s = src.Trim().Replace('\\', '/').TrimEnd('/');
        if (!s.StartsWith('/')) s = "/" + s;
        return s;
    }

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private const string IconPrev = "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M15 19 8 12l7-7\"></path></svg>";
    private const string IconNext = "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"m9 5 7 7-7 7\"></path></svg>";
    private const string IconExpand = "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5\"></path></svg>";
    private const string IconSwipe = "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M20 12H6\"></path><path d=\"m11 7-5 5 5 5\"></path></svg>";
    private const string IconDownload = "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M12 3v12m0 0 4-4m-4 4-4-4M4 19h16\"></path></svg>";

    private sealed record ManifestInfo(
        int PageCount, string? Title, int Width, int Height, double Ratio, int SmallWidth, int FullWidth, string Ext);
}
