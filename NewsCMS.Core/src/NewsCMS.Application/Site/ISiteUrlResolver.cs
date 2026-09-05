namespace NewsCMS.Application.Site;

/// <summary>
/// Tra base URL chính thức (canonical) của site hiện tại — dùng cho mọi chỗ cần URL TUYỆT ĐỐI:
/// thẻ <c>canonical</c>, <c>og:url</c>, <c>og:image</c>, và <c>&lt;loc&gt;</c> trong sitemap.
///
/// Vì sao cần một chỗ dùng chung: domain của site nằm ở HAI nơi — <c>Site.PrimaryDomain</c>
/// (một chuỗi, có thể để trống) và bảng <c>SiteDomains</c> (nhiều host, một cái
/// <c>IsPrimary</c>). Trước đây <c>SitemapGenerator</c> chỉ đọc <c>Site.PrimaryDomain</c> rồi
/// fallback ra chuỗi <c>"localhost"</c>, nên site đã khai domain thật trong <c>SiteDomains</c>
/// mà bỏ trống <c>PrimaryDomain</c> sẽ phát sitemap trỏ <c>https://localhost/...</c> —
/// vô dụng với Google. Gom về đây để canonical và sitemap không thể lệch nhau.
/// </summary>
public interface ISiteUrlResolver
{
    /// <summary>
    /// Base URL không có dấu <c>/</c> ở cuối, ví dụ <c>https://chukafe.vn</c>.
    /// Trả chuỗi rỗng khi site chưa khai domain nào và cũng không có request HTTP để suy ra —
    /// caller khi đó giữ path tương đối thay vì dựng ra <c>https:///path</c>.
    /// </summary>
    Task<string> GetBaseUrlAsync(CancellationToken ct = default);

    /// <summary>
    /// Ghép <paramref name="pathOrUrl"/> thành URL tuyệt đối. Giá trị đã tuyệt đối
    /// (<c>https://cdn…</c>) hoặc protocol-relative (<c>//cdn…</c>) được giữ nguyên — ghép thêm
    /// domain vào là sinh ra URL rác kiểu <c>https://site.vn/https://cdn…</c>.
    /// </summary>
    Task<string?> ToAbsoluteAsync(string? pathOrUrl, CancellationToken ct = default);
}
