using System.Text;
using System.Text.RegularExpressions;

namespace NewsCMS.Application.Builder;

/// <summary>Một font Google dùng được trong builder.</summary>
/// <param name="Name">Tên đúng như Google Fonts đặt — dùng luôn làm giá trị font-family.</param>
/// <param name="Category">Nhóm hiển thị trong dropdown (sans-serif, serif, display).</param>
/// <param name="Weights">Các độ đậm sẽ tải. Xin weight font KHÔNG có thì Google trả 400 cho cả
/// request, mất luôn các font khác trong cùng URL — nên khai theo đúng thứ font thật sự có.</param>
public sealed record GoogleFont(string Name, string Category, string Weights);

/// <summary>
/// Danh sách font Google dùng được trong Site Builder, và bộ phát hiện font nào đang thật sự được
/// dùng để trang public tự nạp đúng thứ cần.
///
/// Vì sao là danh sách tự chọn thay vì gọi Google Fonts Developer API:
///   - API cần API key, cần cấu hình, và hỏng mạng là dropdown trống;
///   - dropdown 1500 font không giúp ai chọn nhanh hơn 25 font đã lọc;
///   - QUAN TRỌNG NHẤT: phần lớn font trên Google Fonts KHÔNG có bộ ký tự tiếng Việt. Chọn nhầm
///     một font như thế thì dấu tiếng Việt rơi về font dự phòng, chữ vỡ lỗ chỗ mà không có lỗi nào
///     báo. Danh sách này chỉ gồm font CÓ subset vietnamese.
///
/// Thêm font mới: thêm một dòng vào <see cref="All"/>. Kiểm tra subset tiếng Việt bằng cách mở
/// https://fonts.googleapis.com/css2?family=TÊN_FONT và tìm khối chú thích "/* vietnamese */".
/// Test <c>GoogleFontCatalogTests.CatalogFonts_AllSupportVietnamese</c> (cần mạng, mặc định bỏ qua)
/// kiểm đúng điều đó cho toàn bộ danh sách.
/// </summary>
public static class GoogleFontCatalog
{
    private const string SansSerif = "sans-serif";
    private const string Serif = "serif";
    private const string Display = "display";

    /// <summary>Font nền cho thân bài xếp trước, font tiêu đề/trang trí xếp sau.</summary>
    public static IReadOnlyList<GoogleFont> All { get; } = new[]
    {
        new GoogleFont("Be Vietnam Pro", SansSerif, "400;500;600;700"),
        new GoogleFont("Inter", SansSerif, "400;500;600;700"),
        new GoogleFont("Roboto", SansSerif, "400;500;700"),
        new GoogleFont("Open Sans", SansSerif, "400;600;700"),
        new GoogleFont("Montserrat", SansSerif, "400;500;600;700"),
        new GoogleFont("Lato", SansSerif, "400;700"),
        new GoogleFont("Nunito", SansSerif, "400;600;700"),
        new GoogleFont("Nunito Sans", SansSerif, "400;600;700"),
        new GoogleFont("Mulish", SansSerif, "400;600;700"),
        new GoogleFont("Quicksand", SansSerif, "400;500;600;700"),
        new GoogleFont("Barlow", SansSerif, "400;500;600;700"),
        new GoogleFont("Rubik", SansSerif, "400;500;600;700"),
        new GoogleFont("Work Sans", SansSerif, "400;500;600;700"),
        new GoogleFont("Manrope", SansSerif, "400;500;600;700"),
        new GoogleFont("Figtree", SansSerif, "400;500;600;700"),
        new GoogleFont("Plus Jakarta Sans", SansSerif, "400;500;600;700"),
        new GoogleFont("Raleway", SansSerif, "400;500;600;700"),
        new GoogleFont("Source Sans 3", SansSerif, "400;600;700"),
        new GoogleFont("Noto Sans", SansSerif, "400;500;700"),
        new GoogleFont("Merriweather", Serif, "400;700"),
        new GoogleFont("Playfair Display", Serif, "400;500;600;700"),
        new GoogleFont("Noto Serif", Serif, "400;700"),
        new GoogleFont("Bitter", Serif, "400;700"),
        new GoogleFont("Oswald", Display, "400;500;600;700"),
        new GoogleFont("Dancing Script", Display, "400;700"),
        new GoogleFont("Lobster", Display, "400"),
        new GoogleFont("Pacifico", Display, "400")
    };

    private static readonly Dictionary<string, GoogleFont> ByName =
        All.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bắt mọi khai báo font-family, kể cả trong style="" trên thẻ. Giá trị có thể có ngoặc kép/đơn
    /// và nhiều font dự phòng — nhóm 1 lấy cả chuỗi, việc tách để <see cref="DetectUsed"/> làm.
    /// </summary>
    private static readonly Regex FontFamilyDeclaration = new(
        @"font-family\s*:\s*([^;}""]+|""[^""]*""[^;}]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Bắt khai báo custom property cho font token: <c>--font-display: "Be Vietnam Pro", sans-serif</c>
    /// trong token CSS. Trang chỉ tham chiếu var(--font-*) mà không viết tên font literal vẫn phải
    /// được nạp đúng font — detector chỉ nhìn font-family thuần sẽ bỏ sót trường hợp này.
    /// </summary>
    private static readonly Regex FontTokenDeclaration = new(
        @"--font-[a-z0-9-]*\s*:\s*([^;}]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Tìm font trong danh sách đang thật sự được dùng ở CSS/HTML truyền vào.
    ///
    /// Nhờ vậy trang public tự nạp đúng font cần mà không phải thêm cột vào CSDL hay bắt builder
    /// gửi kèm danh sách — sửa CSS bằng tay hay bằng MCP thì font vẫn được nạp.
    ///
    /// Trả theo thứ tự trong <see cref="All"/> để URL sinh ra ổn định, cache CDN không bị vỡ chỉ vì
    /// người dùng đổi thứ tự khai báo trong CSS.
    /// </summary>
    public static IReadOnlyList<GoogleFont> DetectUsed(params string?[] sources)
    {
        var hits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;

            foreach (Match m in FontFamilyDeclaration.Matches(source))
            {
                foreach (var family in m.Groups[1].Value.Split(','))
                {
                    var name = family.Trim().Trim('"', '\'', ' ');
                    if (name.Length == 0) continue;
                    if (ByName.ContainsKey(name)) hits.Add(name);
                }
            }

            foreach (Match m in FontTokenDeclaration.Matches(source))
            {
                foreach (var family in m.Groups[1].Value.Split(','))
                {
                    var name = family.Trim().Trim('"', '\'', ' ');
                    if (name.Length == 0) continue;
                    if (ByName.ContainsKey(name)) hits.Add(name);
                }
            }
        }

        return hits.Count == 0
            ? Array.Empty<GoogleFont>()
            : All.Where(f => hits.Contains(f.Name)).ToList();
    }

    /// <summary>
    /// Dựng URL css2 cho các font đã cho. Trả null khi danh sách rỗng để nơi gọi khỏi chèn thẻ
    /// link trống. <c>display=swap</c> để chữ hiện ngay bằng font dự phòng thay vì trắng trang
    /// trong lúc font tải.
    /// </summary>
    public static string? BuildStylesheetUrl(IEnumerable<GoogleFont> fonts)
    {
        var sb = new StringBuilder();
        var any = false;

        foreach (var font in fonts)
        {
            sb.Append(any ? "&family=" : "https://fonts.googleapis.com/css2?family=");
            sb.Append(Uri.EscapeDataString(font.Name).Replace("%20", "+"));
            sb.Append(":wght@").Append(font.Weights);
            any = true;
        }

        if (!any) return null;
        sb.Append("&display=swap");
        return sb.ToString();
    }

    /// <summary>Giá trị đặt vào <c>font-family</c>: tên font kèm nhóm dự phòng.</summary>
    public static string ToCssValue(GoogleFont font) => $"\"{font.Name}\", {font.Category}";
}
