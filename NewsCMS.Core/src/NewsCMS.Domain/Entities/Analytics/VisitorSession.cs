using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Analytics;

public class VisitorSession : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string VisitorKey { get; set; } = default!;
    public DateOnly DayBucket { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string IpHash { get; set; } = default!;
    public string UserAgent { get; set; } = default!;
    public int PageViews { get; set; }
    public bool IsAuthenticated { get; set; }
    public Guid? UserId { get; set; }
}
