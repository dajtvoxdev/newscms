using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Ai;

/// <summary>
/// Gom nội dung site hiện tại làm ngữ cảnh cho AI. Mọi truy vấn ở đây đi qua
/// AppDbContext nên global query filter theo ISiteScoped đã lo phần tách site:
/// không cần tự truyền SiteId, và cũng không thể vô tình đọc chéo site khác.
/// </summary>
public sealed class SiteContextBuilder : ISiteContextBuilder
{
    private readonly AppDbContext _db;
    private readonly ICurrentSite _currentSite;

    public SiteContextBuilder(AppDbContext db, ICurrentSite currentSite)
    {
        _db = db;
        _currentSite = currentSite;
    }

    public async Task<SiteContextSnapshot> BuildAsync(CancellationToken ct = default)
    {
        // SiteSettings là ISiteScoped nên hai khoá dưới đây tự lấy đúng site hiện tại.
        var settings = await _db.SiteSettings.AsNoTracking()
            .Where(x => x.Key == "Site.Name" || x.Key == "Site.Description")
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

        // Nhiều site chỉ có Site.Description mà không có Site.Name (site tạo qua
        // seeder/script không set khoá này). Sites.Name là nguồn dự phòng luôn có,
        // nếu không thì AI mất hẳn tên thương hiệu khỏi ngữ cảnh.
        // Site KHÔNG phải ISiteScoped nên phải tự lọc theo SiteId.
        var siteName = settings.GetValueOrDefault("Site.Name");
        if (string.IsNullOrWhiteSpace(siteName) && _currentSite.IsResolved)
        {
            siteName = await _db.Sites.AsNoTracking()
                .Where(s => s.Id == _currentSite.SiteId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct);
        }

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Order).ThenBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync(ct);

        // Chỉ lấy bài đã xuất bản: bài nháp có thể là thử nghiệm hoặc sai sót,
        // cho AI bắt chước giọng của chúng là phản tác dụng.
        var posts = await _db.Posts.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == PostStatus.Published)
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .Take(SiteContextFormatter.MaxPosts)
            .Select(p => new { p.Title, p.Excerpt, p.Content })
            .ToListAsync(ct);

        var products = await _db.Products.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == ProductStatus.Published)
            .OrderBy(p => p.SortOrder).ThenByDescending(p => p.CreatedAt)
            .Take(SiteContextFormatter.MaxProducts)
            .Select(p => new { p.Name, p.ShortDescription })
            .ToListAsync(ct);

        return new SiteContextSnapshot(
            siteName,
            settings.GetValueOrDefault("Site.Description"),
            categories,
            posts.Select(p => new SiteContextPost(
                p.Title,
                p.Excerpt,
                // Nội dung là HTML; bỏ thẻ trước khi gửi để AI thấy chữ chứ không
                // thấy markup, và để phần cắt ngắn tính theo ký tự chữ thật.
                StripHtml(p.Content))).ToList(),
            products.Select(p => string.IsNullOrWhiteSpace(p.ShortDescription)
                ? p.Name
                : p.Name + " (" + p.ShortDescription.Trim() + ")").ToList());
    }

    /// <summary>Bỏ thẻ HTML, gộp khoảng trắng — đủ dùng cho việc lấy trích đoạn văn.</summary>
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);

        var sb = new System.Text.StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }
}
