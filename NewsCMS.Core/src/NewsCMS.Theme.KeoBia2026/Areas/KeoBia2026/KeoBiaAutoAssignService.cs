using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsCMS.Application.KeoBia;
using NewsCMS.Application.Site;
using NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026.Hubs;
using Telegram.Bot;

namespace NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026;

// Once a match crosses the 20-minute lock, every verified player who never picked is auto-bet
// onto the crowd-favourite side (KeoBiaService.AutoAssignMissingBetsAsync) and notified on
// Telegram. Ticks once a minute so the auto-pick lands close to the lock; the DB makes the
// assignment idempotent, so re-running over the same match is a no-op.
internal sealed class KeoBiaAutoAssignService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan VoteLockWindow = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan MaxLateWindow = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClient _bot;
    private readonly IHubContext<KeoBiaRealtimeHub> _hubContext;
    private readonly KeoBiaTelegramOptions _telegram;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeoBiaAutoAssignService> _logger;

    public KeoBiaAutoAssignService(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient bot,
        IHubContext<KeoBiaRealtimeHub> hubContext,
        IOptions<KeoBiaTelegramOptions> telegram,
        IConfiguration configuration,
        ILogger<KeoBiaAutoAssignService> logger)
    {
        _scopeFactory = scopeFactory;
        _bot = bot;
        _hubContext = hubContext;
        _telegram = telegram.Value;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_telegram.IsConfigured || !IsEnabled())
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                if (!await ResolveSiteScopeAsync(scope.ServiceProvider, stoppingToken))
                {
                    await Task.Delay(CheckInterval, stoppingToken);
                    continue;
                }

                var keoBia = scope.ServiceProvider.GetRequiredService<IKeoBiaService>();
                var now = DateTime.UtcNow;

                var scheduled = await keoBia.SearchMatchesAsync(null, "Scheduled", 1, 50, stoppingToken);
                var lockedMatches = scheduled.Items
                    .Where(m => now >= m.KickoffAt.Add(-VoteLockWindow)
                             && m.KickoffAt >= now.Subtract(MaxLateWindow))
                    .ToList();

                foreach (var match in lockedMatches)
                {
                    var result = await keoBia.AutoAssignMissingBetsAsync(match.Id, stoppingToken);
                    if (result.Notifications.Count == 0)
                        continue;

                    _logger.LogInformation(
                        "Auto-assigned {Count} missing bets for match {MatchId}.",
                        result.Notifications.Count, match.Id);

                    // Push the auto-picks into the live feed so open browsers see them immediately.
                    foreach (var activity in result.Activities)
                    {
                        try
                        {
                            await _hubContext.Clients.All.SendAsync(
                                KeoBiaRealtimeHub.ActivityAddedMethod,
                                ToActivityPayload(activity),
                                stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Failed to broadcast auto-assign activity {ActivityId}.", activity.Id);
                        }
                    }

                    foreach (var bet in result.Notifications)
                    {
                        try
                        {
                            await _bot.SendMessage(
                                bet.TelegramUserId,
                                TelegramBotText.BuildAutoAssignText(bet),
                                cancellationToken: stoppingToken);

                            await Task.Delay(50, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Failed to send auto-assign notice for match {MatchId} to user {TelegramUserId}.",
                                match.Id, bet.TelegramUserId);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KeoBiaAutoAssignService tick failed.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    // Mirrors HomeController.ToActivityPayload so live-feed clients get the same shape
    // whether an activity comes from a manual bet or an auto-assign.
    private static object ToActivityPayload(KeoBiaActivityFeedDto activity) => new
    {
        id = activity.Id,
        activityType = activity.ActivityType,
        playerName = activity.PlayerName,
        avatarUrl = activity.AvatarUrl,
        text = activity.Text,
        badge = activity.Badge,
        choice = activity.Choice,
        choiceLabel = activity.ChoiceLabel,
        cups = activity.Cups,
        createdAt = activity.CreatedAt
    };

    private bool IsEnabled() =>
        _configuration.GetValue("KeoBia:Telegram:AutoAssign:Enabled", true);

    private async Task<bool> ResolveSiteScopeAsync(IServiceProvider services, CancellationToken ct)
    {
        var resolver = services.GetRequiredService<ISiteResolver>();
        var currentSite = services.GetRequiredService<ICurrentSite>();
        var slug = _configuration["KeoBia:Telegram:ResultNotifications:SiteSlug"] ??
                   _configuration["KeoBia:OpenFootball:SiteSlug"] ??
                   "keobia2026";

        ResolvedSite? site = null;
        if (!string.IsNullOrWhiteSpace(slug))
            site = await resolver.ResolveBySlugAsync(slug.Trim(), ct);

        if (site is null)
        {
            var host = ResolveSiteHost();
            site = await resolver.ResolveAsync(host, ct);
        }

        if (site is null || site.SiteId == Guid.Empty || !site.IsActive)
        {
            _logger.LogWarning("Cannot resolve an active KeoBia site for auto-assign.");
            return false;
        }

        currentSite.Set(site.SiteId, site.Slug, site.Theme);
        return true;
    }

    private string ResolveSiteHost()
    {
        var configured = _configuration["KeoBia:Telegram:ResultNotifications:SiteHost"] ??
                         _configuration["KeoBia:OpenFootball:SiteHost"] ??
                         "cakeo26.click";

        if (Uri.TryCreate(configured, UriKind.Absolute, out var uri))
            return uri.Host;

        return configured;
    }
}
