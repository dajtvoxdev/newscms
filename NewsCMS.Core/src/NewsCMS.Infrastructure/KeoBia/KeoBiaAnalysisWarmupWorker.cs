using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.KeoBia;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.KeoBia;

public sealed class KeoBiaAnalysisWarmupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeoBiaAnalysisWarmupWorker> _logger;

    public KeoBiaAnalysisWarmupWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<KeoBiaAnalysisWarmupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ReadBool("KeoBia:AnalysisWarmup:Enabled", true))
        {
            _logger.LogInformation("KeoBia analysis warmup is disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(ReadInt("KeoBia:AnalysisWarmup:IntervalMinutes", 1440), 10, 1440));
        var initialDelay = TimeSpan.FromSeconds(Math.Clamp(ReadInt("KeoBia:AnalysisWarmup:InitialDelaySeconds", 90), 0, 900));

        if (initialDelay > TimeSpan.Zero)
            await Task.Delay(initialDelay, stoppingToken);

        await WarmOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await WarmOnceAsync(stoppingToken);
    }

    private async Task WarmOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var site = await ResolveKeoBiaSiteAsync(scope.ServiceProvider, ct);
            if (site is null || site.SiteId == Guid.Empty)
            {
                _logger.LogWarning("KeoBia analysis warmup skipped because the site was not found.");
                return;
            }

            var currentSite = scope.ServiceProvider.GetRequiredService<ICurrentSite>();
            currentSite.Set(site.SiteId, site.Slug, site.Theme);

            var analysis = scope.ServiceProvider.GetRequiredService<IKeoBiaAnalysisService>();
            var result = await analysis.WarmUpcomingMatchesAsync(DateTime.UtcNow, ct);
            if (result.Succeeded)
            {
                _logger.LogInformation(
                    "KeoBia analysis warmup completed for site {SiteSlug}: generated {Count} cached analyses.",
                    site.Slug,
                    result.Value);
            }
            else
            {
                _logger.LogWarning("KeoBia analysis warmup failed for site {SiteSlug}: {Error}", site.Slug, result.Error);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KeoBia analysis warmup worker failed.");
        }
    }

    private async Task<ResolvedSite?> ResolveKeoBiaSiteAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var slug = _configuration["KeoBia:AnalysisWarmup:SiteSlug"];
        if (string.IsNullOrWhiteSpace(slug)) slug = "keobia2026";

        var bySlug = await db.Sites.AsNoTracking()
            .Where(x => x.IsActive && x.Slug == slug)
            .Select(x => new ResolvedSite(x.Id, x.Slug, x.DefaultTheme ?? "KeoBia2026"))
            .FirstOrDefaultAsync(ct);
        if (bySlug is not null) return bySlug;

        var host = _configuration["KeoBia:AnalysisWarmup:SiteHost"];
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
