using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Analytics;
using NewsCMS.Application.Analytics.Dtos;
using NewsCMS.Domain.Entities.Analytics;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Analytics;

public sealed class VisitorTrackingService : IVisitorTrackingService
{
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;

    public VisitorTrackingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task TrackAsync(VisitorTrackingContext context, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var day = DateOnly.FromDateTime(now);

        var existing = await _db.VisitorSessions
            .FirstOrDefaultAsync(x => x.DayBucket == day && x.VisitorKey == context.VisitorKey, ct);

        if (existing is null)
        {
            _db.VisitorSessions.Add(new VisitorSession
            {
                VisitorKey = context.VisitorKey,
                DayBucket = day,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                IpHash = context.IpHash,
                UserAgent = Truncate(context.UserAgent, 256),
                PageViews = 1,
                IsAuthenticated = context.IsAuthenticated,
                UserId = context.UserId
            });
        }
        else
        {
            existing.LastSeenUtc = now;
            existing.PageViews += 1;
            if (context.IsAuthenticated && existing.UserId is null)
            {
                existing.IsAuthenticated = true;
                existing.UserId = context.UserId;
            }
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race on unique (DayBucket, VisitorKey): another concurrent request inserted first.
            // Reload + retry update; on second failure, swallow to keep request hot path safe.
            _db.ChangeTracker.Clear();
            var retry = await _db.VisitorSessions
                .FirstOrDefaultAsync(x => x.DayBucket == day && x.VisitorKey == context.VisitorKey, ct);
            if (retry is not null)
            {
                retry.LastSeenUtc = now;
                retry.PageViews += 1;
                try { await _db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { /* give up — tracking must never break a page view */ }
            }
        }
    }

    public async Task<VisitorStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var weekStart = today.AddDays(-6);
        var onlineSince = now - OnlineWindow;

        var todayUnique = await _db.VisitorSessions
            .Where(x => x.DayBucket == today)
            .CountAsync(ct);

        var onlineNow = await _db.VisitorSessions
            .Where(x => x.LastSeenUtc >= onlineSince)
            .Select(x => x.VisitorKey)
            .Distinct()
            .CountAsync(ct);

        var weekUnique = await _db.VisitorSessions
            .Where(x => x.DayBucket >= weekStart)
            .Select(x => x.VisitorKey)
            .Distinct()
            .CountAsync(ct);

        var totalAllTime = await _db.VisitorSessions
            .Select(x => x.VisitorKey)
            .Distinct()
            .CountAsync(ct);

        return new VisitorStatsDto(todayUnique, onlineNow, weekUnique, totalAllTime);
    }

    public async Task<int> PurgeOlderThanAsync(int retentionDays, CancellationToken ct = default)
    {
        if (retentionDays <= 0) return 0;
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-retentionDays);
        var deleted = await _db.VisitorSessions
            .Where(x => x.DayBucket < cutoff)
            .ExecuteDeleteAsync(ct);
        return deleted;
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
