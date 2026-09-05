using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Analytics;

namespace NewsCMS.Infrastructure.Jobs;

public sealed class VisitorRetentionJob
{
    private readonly IVisitorTrackingService _tracking;
    private readonly IConfiguration _config;
    private readonly ILogger<VisitorRetentionJob> _logger;

    public VisitorRetentionJob(
        IVisitorTrackingService tracking,
        IConfiguration config,
        ILogger<VisitorRetentionJob> logger)
    {
        _tracking = tracking;
        _config = config;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var retentionDays = _config.GetValue("Analytics:RetentionDays", 90);
        var deleted = await _tracking.PurgeOlderThanAsync(retentionDays, ct);
        _logger.LogInformation("VisitorRetentionJob removed {Count} sessions older than {Days} days.", deleted, retentionDays);
    }
}
