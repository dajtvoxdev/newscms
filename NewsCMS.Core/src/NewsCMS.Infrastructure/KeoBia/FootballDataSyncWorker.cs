using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.KeoBia;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.KeoBia;

// Every 30 minutes (configurable) pulls the World Cup schedule + results from
// football-data.org, updates fixtures (incl. resolved knockout teams) and settles bia.
public sealed class FootballDataSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FootballDataSyncWorker> _logger;

    public FootballDataSyncWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<FootballDataSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ReadBool("KeoBia:FootballData:Enabled", true))
        {
            _logger.LogInformation("Football-Data World Cup sync is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_configuration["KeoBia:FootballData:ApiToken"]))
        {
            _logger.LogWarning("Football-Data sync skipped: KeoBia:FootballData:ApiToken is not configured.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(ReadInt("KeoBia:FootballData:IntervalMinutes", 30), 5, 1440));
        var initialDelay = TimeSpan.FromSeconds(Math.Clamp(ReadInt("KeoBia:FootballData:InitialDelaySeconds", 20), 0, 600));

        if (initialDelay > TimeSpan.Zero)
            await Task.Delay(initialDelay, stoppingToken);

        await SyncOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SyncOnceAsync(stoppingToken);
    }

    private async Task SyncOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var site = await ResolveKeoBiaSiteAsync(scope.ServiceProvider, ct);
            if (site is null || site.SiteId == Guid.Empty)
            {
                _logger.LogWarning("Football-Data sync skipped because KeoBia site was not found.");
                return;
            }

            var currentSite = scope.ServiceProvider.GetRequiredService<ICurrentSite>();
            currentSite.Set(site.SiteId, site.Slug, site.Theme);

            var sync = scope.ServiceProvider.GetRequiredService<IKeoBiaFootballDataSyncService>();
            var result = await sync.SyncAsync(force: false, ct);
            if (result.Succeeded && result.Value is not null)
            {
                _logger.LogInformation(
                    "Football-Data sync completed for site {SiteSlug}: {Imported}/{Total} cập nhật, bỏ qua {Skipped}.",
                    site.Slug,
                    result.Value.ImportedRows,
                    result.Value.TotalRows,
                    result.Value.SkippedRows);

                if (result.Value.ImportedRows > 0)
                {
                    try
                    {
                        var analysis = scope.ServiceProvider.GetRequiredService<IKeoBiaAnalysisService>();
                        var warmed = await analysis.WarmUpcomingMatchesAsync(DateTime.UtcNow, ct);
                        if (warmed.Succeeded && warmed.Value > 0)
                            _logger.LogInformation("Post-sync AI warmup generated {Count} analyses.", warmed.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Post-sync AI warmup skipped.");
                    }
                }
            }
            else
            {
                _logger.LogWarning("Football-Data sync failed for site {SiteSlug}: {Error}", site.Slug, result.Error);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Football-Data sync worker failed.");
        }
    }

    private async Task<ResolvedSite?> ResolveKeoBiaSiteAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var slug = _configuration["KeoBia:FootballData:SiteSlug"];
        if (string.IsNullOrWhiteSpace(slug)) slug = "keobia2026";

        var bySlug = await db.Sites.AsNoTracking()
            .Where(x => x.IsActive && x.Slug == slug)
            .Select(x => new ResolvedSite(x.Id, x.Slug, x.DefaultTheme))
            .FirstOrDefaultAsync(ct);
        if (bySlug is not null) return bySlug;

        var host = _configuration["KeoBia:FootballData:SiteHost"];
        if (string.IsNullOrWhiteSpace(host)) return null;

        var resolver = services.GetRequiredService<ISiteResolver>();
        var byHost = await resolver.ResolveAsync(host, ct);
        return byHost is { IsActive: true } ? byHost : null;
    }

    private bool ReadBool(string key, bool defaultValue)
    {
        var raw = _configuration[key];
        return string.IsNullOrWhiteSpace(raw) ? defaultValue : bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    private int ReadInt(string key, int defaultValue)
    {
        var raw = _configuration[key];
        return int.TryParse(raw, out var value) ? value : defaultValue;
    }
}
