using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Khối "Nhúng Video": người dùng dán MỘT URL (YouTube, Vimeo, hoặc link video trực tiếp
/// .mp4/.webm/.ogg), khối tự nhận diện nhà cung cấp rồi dựng khung nhúng responsive (tỉ lệ khung
/// hình cố định bằng padding-top — không dùng CSS <c>aspect-ratio</c> vì cần khớp cách
/// <see cref="Blocks.PdfFlipbookBlock"/> đã làm cho trình duyệt cũ).
///
/// Khác với "Nhúng HTML" (data-nc-html, <see cref="BlockCodeExtractor"/>, khoá sau quyền
/// Builder.Code.Manage vì đổ HTML/script tuỳ ý của người dùng thẳng ra trang): khối này KHÔNG bao
/// giờ nhận HTML từ người dùng, chỉ nhận URL — server tự dựng iframe/thẻ &lt;video&gt; từ danh
/// sách nhà cung cấp đã biết (<see cref="VideoUrlParser"/>), nên không cần quyền đặc biệt và mọi
/// người có quyền sửa trang đều dùng được.
/// </summary>
public sealed class VideoEmbedBlock : IDynamicBlock
{
    public string Key => "video-embed";

    public BlockDescriptor Descriptor => new(
        Label: "Nhúng Video",
        Category: "Nội dung",
        Description: "Dán link YouTube, Vimeo, hoặc video .mp4/.webm — tự dựng khung responsive.",
        IconSvg: "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">" +
                 "<rect x=\"2\" y=\"4\" width=\"20\" height=\"16\" rx=\"3\"/>" +
                 "<path d=\"M10 9.5v5l4.5-2.5Z\" fill=\"currentColor\" stroke=\"none\"/></svg>",
        Presets:
        [
            new("16-9", "Ngang 16:9 (mặc định)",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"3\" y=\"7\" width=\"42\" height=\"18\" rx=\"2\"/></svg>",
                "{\"ratio\":\"16-9\"}"),
            new("9-16", "Dọc 9:16 (Reels/Shorts)",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"16\" y=\"1\" width=\"16\" height=\"30\" rx=\"2\"/></svg>",
                "{\"ratio\":\"9-16\"}"),
            new("1-1", "Vuông 1:1",
                "<svg viewBox=\"0 0 48 32\"><rect x=\"9\" y=\"1\" width=\"30\" height=\"30\" rx=\"2\"/></svg>",
                "{\"ratio\":\"1-1\"}")
        ],
        Props:
        [
            new BlockPropDescriptor("url", BlockPropTypes.Text, "Link video", BlockPropGroups.Data,
                Placeholder: "https://www.youtube.com/watch?v=...",
                Hint: "YouTube, Vimeo, hoặc link video trực tiếp (.mp4/.webm/.ogg)."),
            new BlockPropDescriptor("ratio", BlockPropTypes.Select, "Tỉ lệ khung hình", BlockPropGroups.Display, "16-9",
                Options:
                [
                    new("16-9", "Ngang 16:9"),
                    new("9-16", "Dọc 9:16 (Reels/Shorts)"),
                    new("4-3", "Ngang 4:3"),
                    new("1-1", "Vuông 1:1"),
                    new("21-9", "Rạp chiếu 21:9")
                ]),
            new BlockPropDescriptor("radius", BlockPropTypes.Select, "Bo góc", BlockPropGroups.Display, "card",
                Options:
                [
                    new("card", "Theo theme var(--radius-card, 18px)"),
                    new("none", "Không bo"),
                    new("sm", "Nhỏ (4px)"),
                    new("md", "Vừa (8px)"),
                    new("lg", "Lớn (12px)")
                ]),
            new BlockPropDescriptor("caption", BlockPropTypes.Text, "Chú thích dưới video", BlockPropGroups.Content),
            new BlockPropDescriptor("autoplay", BlockPropTypes.Checkbox, "Tự phát (câm tiếng)", BlockPropGroups.Advanced, "false",
                Hint: "Trình duyệt chỉ cho tự phát khi video câm tiếng — bật mục này sẽ tự câm tiếng theo."),
            new BlockPropDescriptor("loop", BlockPropTypes.Checkbox, "Phát lặp lại", BlockPropGroups.Advanced, "false"),
            new BlockPropDescriptor("controls", BlockPropTypes.Checkbox, "Hiện nút điều khiển", BlockPropGroups.Advanced, "true")
        ]);

    public Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
    {
        var props = new JsonProps(context.PropsJson);
        var url = (props.GetString("url") ?? string.Empty).Trim();

        if (url.Length == 0)
            return Task.FromResult(EmptyPlaceholder());

        var video = VideoUrlParser.Parse(url);
        if (video is null)
            return Task.FromResult(UnsupportedPlaceholder(url));

        var ratio = NormalizeRatioPercent(props.GetString("ratio"));
        var radius = NormalizeRadius(props.GetString("radius"));
        var caption = props.GetString("caption");
        var autoplay = props.GetBool("autoplay", false);
        var loop = props.GetBool("loop", false);
        var controls = props.GetBool("controls", true);
        var title = string.IsNullOrWhiteSpace(caption) ? "Video" : caption!;

        var frameHtml = video.Provider == "direct"
            ? BuildNativeVideo(video.EmbedSrc, autoplay, loop, controls)
            : BuildIframe(video, autoplay, loop, controls, title);

        var sb = new StringBuilder();
        sb.Append("<figure data-nc-part=\"wrapper\" style=\"margin:0\">");
        sb.Append($"<div data-nc-part=\"frame\" style=\"position:relative;width:100%;padding-top:{ratio}%;" +
                  $"border-radius:{radius};overflow:hidden;background:#000\">{frameHtml}</div>");
        if (!string.IsNullOrWhiteSpace(caption))
            sb.Append($"<figcaption data-nc-part=\"caption\" style=\"margin-top:10px;text-align:center;color:var(--color-muted,#64748b);" +
                      $"font-size:.9rem\">{Enc(caption)}</figcaption>");
        sb.Append("</figure>");
        return Task.FromResult(sb.ToString());
    }

    private static string BuildIframe(VideoRef video, bool autoplay, bool loop, bool controls, string title)
    {
        var qs = new List<string>();
        switch (video.Provider)
        {
            case "youtube":
                qs.Add("rel=0");
                qs.Add("modestbranding=1");
                if (!controls) qs.Add("controls=0");
                if (autoplay) { qs.Add("autoplay=1"); qs.Add("mute=1"); }
                if (loop) { qs.Add("loop=1"); qs.Add($"playlist={video.Id}"); }
                break;
            case "vimeo":
                qs.Add("title=0");
                qs.Add("byline=0");
                qs.Add("portrait=0");
                if (!controls) qs.Add("controls=0");
                if (autoplay) { qs.Add("autoplay=1"); qs.Add("muted=1"); }
                if (loop) qs.Add("loop=1");
                break;
        }

        var src = video.EmbedSrc + (qs.Count > 0 ? "?" + string.Join("&", qs) : string.Empty);
        return $"<iframe data-nc-part=\"player\" src=\"{Enc(src)}\" style=\"position:absolute;inset:0;width:100%;height:100%;border:0\" " +
               $"loading=\"lazy\" title=\"{Enc(title)}\" " +
               "allow=\"accelerometer;autoplay;clipboard-write;encrypted-media;gyroscope;picture-in-picture;web-share\" " +
               "allowfullscreen referrerpolicy=\"strict-origin-when-cross-origin\"></iframe>";
    }

    private static string BuildNativeVideo(string src, bool autoplay, bool loop, bool controls)
    {
        var attrs = new StringBuilder();
        if (controls) attrs.Append(" controls");
        if (autoplay) attrs.Append(" autoplay muted"); // trình duyệt chỉ tự phát khi video câm tiếng.
        if (loop) attrs.Append(" loop");

        return $"<video data-nc-part=\"player\" src=\"{Enc(src)}\" style=\"position:absolute;inset:0;width:100%;height:100%;object-fit:contain\"" +
               $"{attrs} playsinline preload=\"metadata\"></video>";
    }

    private static string EmptyPlaceholder() =>
        "<div data-nc-part=\"wrapper\" style=\"padding:40px 20px;text-align:center;color:#94a3b8;font-family:system-ui,sans-serif;" +
        "border:2px dashed #cbd5e1;border-radius:12px;background:#f8fafc\">" +
        "<div data-nc-part=\"placeholder-title\" style=\"font-weight:700;margin-bottom:4px\">Khối nhúng video</div>" +
        "<div data-nc-part=\"placeholder-hint\" style=\"font-size:.85rem\">Dán link YouTube, Vimeo hoặc video .mp4 vào ô “Link video”.</div></div>";

    private static string UnsupportedPlaceholder(string url) =>
        "<div data-nc-part=\"wrapper\" style=\"padding:40px 20px;text-align:center;color:#b45309;font-family:system-ui,sans-serif;" +
        "border:2px dashed #fbbf24;border-radius:12px;background:#fffbeb\">" +
        "<div data-nc-part=\"placeholder-title\" style=\"font-weight:700;margin-bottom:4px\">Không nhận diện được link video</div>" +
        $"<div data-nc-part=\"placeholder-url\" style=\"font-size:.85rem;word-break:break-all\">{Enc(url)}</div>" +
        "<div data-nc-part=\"placeholder-hint\" style=\"font-size:.8rem;margin-top:6px\">Hỗ trợ YouTube, Vimeo, hoặc link video trực tiếp (.mp4/.webm/.ogg).</div></div>";

    /// <summary>Tỉ lệ khung hình → % padding-top (kỹ thuật "padding hack" cho khung responsive).</summary>
    private static string NormalizeRatioPercent(string? ratio) => ratio switch
    {
        "9-16" => "177.78",
        "4-3" => "75",
        "1-1" => "100",
        "21-9" => "42.86",
        _ => "56.25"
    };

    private static string NormalizeRadius(string? radius) => radius switch
    {
        "none" => "0px",
        "sm" => "4px",
        "md" => "8px",
        "lg" => "12px",
        _ => "var(--radius-card, 18px)"
    };

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

/// <summary>Kết quả nhận diện một URL video: nhà cung cấp, id gốc, và URL nhúng đã dựng sẵn.</summary>
internal sealed record VideoRef(string Provider, string Id, string EmbedSrc);

/// <summary>
/// Nhận diện URL video người dùng dán vào, KHÔNG BAO GIỜ throw (URL sai định dạng thì trả null,
/// khối tự hiện placeholder cảnh báo). Chỉ trích ra ID (YouTube: chữ/số/gạch, Vimeo: số) rồi tự
/// dựng URL nhúng — không bao giờ phản chiếu nguyên văn URL người dùng vào iframe, nên một domain
/// giả dạng ("evilyoutube.com/watch?v=...") vẫn an toàn dù có khớp nhầm.
/// </summary>
internal static class VideoUrlParser
{
    // \b trước "youtube"/"vimeo" để domain giả dạng như "evilyoutube.com" (không có ranh giới từ
    // giữa "evil" và "youtube") không bị nhận nhầm là YouTube thật.
    private static readonly Regex YouTubeId = new(
        @"\byoutube(?:-nocookie)?\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/)([\w-]{6,32})|\byoutu\.be/([\w-]{6,32})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VimeoId = new(
        @"\bvimeo\.com/(?:video/)?(\d{5,12})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DirectFileExt = new(
        @"\.(mp4|webm|ogg|ogv|mov)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static VideoRef? Parse(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var yt = YouTubeId.Match(url);
        if (yt.Success)
        {
            var id = yt.Groups[1].Success ? yt.Groups[1].Value : yt.Groups[2].Value;
            return new VideoRef("youtube", id, $"https://www.youtube-nocookie.com/embed/{id}");
        }

        var vimeo = VimeoId.Match(url);
        if (vimeo.Success)
            return new VideoRef("vimeo", vimeo.Groups[1].Value, $"https://player.vimeo.com/video/{vimeo.Groups[1].Value}");

        // Link video trực tiếp: chỉ nhận http(s) tuyệt đối (chặn javascript:/data: URI) có đuôi
        // file video ở phần path (không xét query string, tránh bị lách bằng ?x=.mp4).
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && DirectFileExt.IsMatch(uri.AbsolutePath))
            return new VideoRef("direct", url, url);

        return null;
    }
}
