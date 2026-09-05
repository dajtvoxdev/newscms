using NewsCMS.Application.Site;

namespace NewsCMS.Infrastructure.Site;

/// <summary>
/// Holder scoped giữ site đã resolve cho request. Không phụ thuộc DbContext
/// để tránh vòng lặp DI (AppDbContext lại đọc ICurrentSite).
/// </summary>
public sealed class CurrentSiteAccessor : ICurrentSite
{
    public Guid SiteId { get; private set; } = Guid.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Theme { get; private set; } = string.Empty;
    public bool IsResolved { get; private set; }

    public void Set(Guid siteId, string slug, string theme)
    {
        SiteId = siteId;
        Slug = slug ?? string.Empty;
        Theme = theme ?? string.Empty;
        IsResolved = true;
    }
}
