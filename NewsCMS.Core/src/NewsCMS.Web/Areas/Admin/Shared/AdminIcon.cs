using System.Collections.Generic;
using Microsoft.AspNetCore.Html;

namespace NewsCMS.Web.Areas.Admin.Shared;

/// <summary>
/// Bộ icon SVG tự vẽ theo phong cách Lucide (24×24, stroke currentColor) thay cho emoji
/// trong giao diện quản trị. Dùng trong Razor: <code>@AdminIcon.Svg("beer")</code>,
/// hoặc lấy chuỗi thô cho JS/TinyMCE: <code>AdminIcon.Tag("bot")</code>.
/// Kích thước icon bám theo font-size của phần tử cha (class .nc-icon trong Styles/admin.css),
/// nên không cần truyền class cỡ riêng trừ khi muốn override.
/// </summary>
public static class AdminIcon
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["palette"] = @"<circle cx=""13.5"" cy=""6.5"" r="".5"" fill=""currentColor""/><circle cx=""17.5"" cy=""10.5"" r="".5"" fill=""currentColor""/><circle cx=""8.5"" cy=""7.5"" r="".5"" fill=""currentColor""/><circle cx=""6.5"" cy=""12.5"" r="".5"" fill=""currentColor""/><path d=""M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125a1.64 1.64 0 0 1 1.668-1.668h1.996c3.051 0 5.555-2.503 5.555-5.554C21.965 6.012 17.461 2 12 2z""/>",
        ["layout-template"] = @"<rect width=""18"" height=""7"" x=""3"" y=""3"" rx=""1""/><rect width=""9"" height=""7"" x=""3"" y=""14"" rx=""1""/><rect width=""9"" height=""7"" x=""14"" y=""14"" rx=""1""/>",
        ["search"] = @"<circle cx=""11"" cy=""11"" r=""8""/><path d=""m21 21-4.3-4.3""/>",
        ["key-round"] = @"<path d=""M2.586 17.414A2 2 0 0 0 2 18.828V21a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h1a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h.172a2 2 0 0 0 1.414-.586l.814-.814a6.5 6.5 0 1 0-4-4z""/><circle cx=""16.5"" cy=""7.5"" r="".5"" fill=""currentColor""/>",
        ["rocket"] = @"<path d=""M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.71-.84.7-2.13-.09-2.91a2.18 2.18 0 0 0-2.91-.09z""/><path d=""m12 15-3-3a22 22 0 0 1 2-3.95A12.88 12.88 0 0 1 22 2c0 2.72-.78 7.5-6 11a22.35 22.35 0 0 1-4 2z""/><path d=""M9 12H4s.55-3.03 2-4c1.62-1.08 5 0 5 0""/><path d=""M12 15v5s3.03-.55 4-2c1.08-1.62 0-5 0-5""/>",
        ["paperclip"] = @"<path d=""m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l8.57-8.57A4 4 0 1 1 18 8.84l-8.59 8.57a2 2 0 0 1-2.83-2.83l8.49-8.48""/>",
        ["x"] = @"<path d=""M18 6 6 18""/><path d=""m6 6 12 12""/>",
        ["film"] = @"<rect width=""18"" height=""18"" x=""3"" y=""3"" rx=""2""/><path d=""M7 3v18""/><path d=""M3 7.5h4""/><path d=""M3 12h18""/><path d=""M3 16.5h4""/><path d=""M17 3v18""/><path d=""M17 7.5h4""/><path d=""M17 16.5h4""/>",
        ["music"] = @"<path d=""M9 18V5l12-2v13""/><circle cx=""6"" cy=""18"" r=""3""/><circle cx=""18"" cy=""16"" r=""3""/>",
        ["file-text"] = @"<path d=""M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z""/><path d=""M14 2v4a2 2 0 0 0 2 2h4""/><path d=""M10 9H8""/><path d=""M16 13H8""/><path d=""M16 17H8""/>",
        ["folder-open"] = @"<path d=""m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2""/>",
        ["trash"] = @"<path d=""M3 6h18""/><path d=""M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6""/><path d=""M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2""/><line x1=""10"" x2=""10"" y1=""11"" y2=""17""/><line x1=""14"" x2=""14"" y1=""11"" y2=""17""/>",
        ["pencil"] = @"<path d=""M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z""/><path d=""m15 5 4 4""/>",
        ["monitor"] = @"<rect width=""20"" height=""14"" x=""2"" y=""3"" rx=""2""/><line x1=""8"" x2=""16"" y1=""21"" y2=""21""/><line x1=""12"" x2=""12"" y1=""17"" y2=""21""/>",
        ["tablet"] = @"<rect width=""16"" height=""20"" x=""4"" y=""2"" rx=""2"" ry=""2""/><line x1=""12"" x2=""12.01"" y1=""18"" y2=""18""/>",
        ["smartphone"] = @"<rect width=""14"" height=""20"" x=""5"" y=""2"" rx=""2"" ry=""2""/><path d=""M12 18h.01""/>",
        ["save"] = @"<path d=""M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z""/><path d=""M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7""/><path d=""M7 3v4a1 1 0 0 0 1 1h7""/>",
        ["check"] = @"<path d=""M20 6 9 17l-5-5""/>",
        ["circle-check"] = @"<circle cx=""12"" cy=""12"" r=""10""/><path d=""m9 12 2 2 4-4""/>",
        ["star"] = @"<polygon points=""12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2""/>",
        ["skull"] = @"<circle cx=""9"" cy=""12"" r=""1""/><circle cx=""15"" cy=""12"" r=""1""/><path d=""M8 20v2h8v-2""/><path d=""m12.5 17-.5-1-.5 1h1z""/><path d=""M16 20a2 2 0 0 0 1.56-3.25 8 8 0 1 0-11.12 0A2 2 0 0 0 8 20""/>",
        ["beer"] = @"<path d=""M17 11h1a3 3 0 0 1 0 6h-1""/><path d=""M9 12v6""/><path d=""M13 12v6""/><path d=""M14 7.5c-1 0-1.44.5-3 .5s-2-.5-3-.5-1.72.5-2.5.5a2.5 2.5 0 0 1 0-5c.78 0 1.57.5 2.5.5S9.44 2 11 2s2 1.5 3 1.5 1.72-.5 2.5-.5a2.5 2.5 0 0 1 0 5c-.78 0-1.5-.5-2.5-.5Z""/><path d=""M5 8v12a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V8""/>",
        ["peanut"] = @"<circle cx=""9.5"" cy=""8"" r=""4.5""/><circle cx=""14.5"" cy=""16"" r=""4.5""/><path d=""M11.2 11.5c.5-.4 1.1-.4 1.6 0""/>",
        ["refresh-cw"] = @"<path d=""M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8""/><path d=""M21 3v5h-5""/><path d=""M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16""/><path d=""M8 16H3v5""/>",
        ["bot"] = @"<path d=""M12 8V4H8""/><rect width=""16"" height=""12"" x=""4"" y=""8"" rx=""2""/><path d=""M2 14h2""/><path d=""M20 14h2""/><path d=""M15 13v2""/><path d=""M9 13v2""/>",
        ["code"] = @"<polyline points=""16 18 22 12 16 6""/><polyline points=""8 6 2 12 8 18""/>",
        ["arrow-up-right"] = @"<path d=""M7 7h10v10""/><path d=""M7 17 17 7""/>",
        ["arrow-left"] = @"<path d=""m12 19-7-7 7-7""/><path d=""M19 12H5""/>",
        ["arrow-right"] = @"<path d=""M5 12h14""/><path d=""m12 5 7 7-7 7""/>",
        ["corner-down-right"] = @"<polyline points=""15 10 20 15 15 20""/><path d=""M4 4v7a4 4 0 0 0 4 4h12""/>",
        ["rotate-cw"] = @"<path d=""M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8""/><path d=""M21 3v5h-5""/>",
    };

    /// <summary>Chuỗi &lt;svg&gt; hoàn chỉnh — dùng cho Html.Raw, JS template, TinyMCE addIcon.</summary>
    public static string Tag(string name, string? cssClass = null)
    {
        if (!Paths.TryGetValue(name, out var inner)) return string.Empty;
        var cls = "nc-icon" + (string.IsNullOrEmpty(cssClass) ? "" : " " + cssClass);
        return
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" class=\"{cls}\">{inner}</svg>";
    }

    /// <summary>Render trực tiếp trong Razor.</summary>
    public static IHtmlContent Svg(string name, string? cssClass = null)
        => new HtmlString(Tag(name, cssClass));

    /// <summary>Toàn bộ icon (name → markup) để nhúng vào window.ncIcons phía JS.</summary>
    public static IReadOnlyDictionary<string, string> All()
        => Paths.ToDictionary(p => p.Key, p => Tag(p.Key));
}
