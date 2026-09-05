using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.KeoBia;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.KeoBia;

public sealed class OpenFootballWorldCupSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenFootballWorldCupSyncWorker> _logger;

    public OpenFootballWorldCupSyncWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OpenFootballWorldCupSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ReadBool("KeoBia:OpenFootball:Enabled", true))
        {
            _logger.LogInformation("OpenFootball World Cup sync is disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(ReadInt("KeoBia:OpenFootball:IntervalMinutes", 60), 5, 1440));
        var initialDelay = TimeSpan.FromSeconds(Math.Clamp(ReadInt("KeoBia:OpenFootball:InitialDelaySeconds", 15), 0, 600));

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
                _logger.LogWarning("OpenFootball sync skipped because KeoBia site was not found.");
                return;
            }

            var currentSite = scope.ServiceProvider.GetRequiredService<ICurrentSite>();
            currentSite.Set(site.SiteId, site.Slug, site.Theme);

            var sync = scope.ServiceProvider.GetRequiredService<IKeoBiaOpenFootballSyncService>();
            var result = await sync.SyncAsync(force: false, ct);
            if (result.Succeeded && result.Value is not null)
            {
                _logger.LogInformation(
                    "OpenFootball sync completed for site {SiteSlug}: {Imported}/{Total} rows, skipped {Skipped}.",
                    site.Slug,
                    result.Value.ImportedRows,
                    result.Value.TotalRows,
                    result.Value.SkippedRows);
            }
            else
            {
                _logger.LogWarning("OpenFootball sync failed for site {SiteSlug}: {Error}", site.Slug, result.Error);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenFootball sync worker failed.");
        }
    }

    private async Task<ResolvedSite?> ResolveKeoBiaSiteAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var slug = _configuration["KeoBia:OpenFootball:SiteSlug"];
        if (string.IsNullOrWhiteSpace(slug)) slug = "keobia2026";

        var bySlug = await db.Sites.AsNoTracking()
            .Where(x => x.IsActive && x.Slug == slug)
            .Select(x => new ResolvedSite(x.Id, x.Slug, x.DefaultTheme))
            .FirstOrDefaultAsync(ct);
        if (bySlug is not null) return bySlug;

        var host = _configuration["KeoBia:OpenFootball:SiteHost"];
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
