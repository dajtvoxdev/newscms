namespace NewsCMS.Application.Analytics.Dtos;

public sealed record VisitorStatsDto(
    int TodayUnique,
    int OnlineNow,
    int Total7Days,
    int TotalAllTime
);

public sealed record VisitorTrackingContext(
    string VisitorKey,
    string IpHash,
    string UserAgent,
    bool IsAuthenticated,
    Guid? UserId
);
