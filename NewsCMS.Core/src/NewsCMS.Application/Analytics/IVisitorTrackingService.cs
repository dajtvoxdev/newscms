using NewsCMS.Application.Analytics.Dtos;

namespace NewsCMS.Application.Analytics;

public interface IVisitorTrackingService
{
    Task TrackAsync(VisitorTrackingContext context, CancellationToken ct = default);
    Task<VisitorStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<int> PurgeOlderThanAsync(int retentionDays, CancellationToken ct = default);
}
