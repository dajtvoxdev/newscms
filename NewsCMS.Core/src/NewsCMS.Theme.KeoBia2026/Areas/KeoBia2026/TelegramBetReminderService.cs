using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsCMS.Application.KeoBia;
using NewsCMS.Application.Site;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026;

internal sealed class TelegramBetReminderService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReNotifyInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan VoteOpenWindow = TimeSpan.FromDays(1);
    private static readonly TimeSpan VoteLockWindow = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan PreOpenAnalysisWindow = TimeSpan.FromMinutes(25);
    private static readonly TimeSpan PreKickoffAnalysisWindow = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClient _bot;
    private readonly HashSet<Guid> _preOpenAnalysisDone = new();
    private readonly HashSet<Guid> _preKickoffAnalysisDone = new();
    private readonly KeoBiaTelegramOptions _telegram;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<TelegramBetReminderService> _logger;

    public TelegramBetReminderService(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient bot,
        IOptions<KeoBiaTelegramOptions> telegram,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<TelegramBetReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _bot = bot;
        _telegram = telegram.Value;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_telegram.IsConfigured || !IsEnabled())
            return;

        var statePath = StatePath();
        var state = await LoadStateAsync(statePath, stoppingToken);

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

                var openMatches = await keoBia.SearchMatchesAsync(null, "Scheduled", 1, 50, stoppingToken);
                var votableMatches = openMatches.Items
                    .Where(m => now >= m.KickoffAt.Add(-VoteOpenWindow) && now < m.KickoffAt.Add(-VoteLockWindow))
                    .ToList();

                // Pre-open analysis: 10-25 min before betting opens (kickoff - 24h25m to kickoff - 24h10m)
                var preOpenMatches = openMatches.Items
                    .Where(m => now >= m.KickoffAt.Add(-VoteOpenWindow).Add(-PreOpenAnalysisWindow)
                             && now < m.KickoffAt.Add(-VoteOpenWindow)
                             && !_preOpenAnalysisDone.Contains(m.Id))
                    .Take(1)
                    .ToList();

                // Pre-kickoff analysis: 1h-1.5h before kickoff — refresh with latest news
                var preKickoffMatches = openMatches.Items
                    .Where(m => now >= m.KickoffAt.Add(-PreKickoffAnalysisWindow)
                             && now < m.KickoffAt.Add(-TimeSpan.FromHours(1))
                             && !_preKickoffAnalysisDone.Contains(m.Id))
                    .Take(1)
                    .ToList();

                var analysisTargets = preOpenMatches.Concat(preKickoffMatches).DistinctBy(m => m.Id).ToList();

                if (analysisTargets.Count > 0)
                {
                    try
                    {
                        var analysis = scope.ServiceProvider.GetRequiredService<IKeoBiaAnalysisService>();
                        foreach (var m in analysisTargets)
                        {
                            var isPreOpen = preOpenMatches.Any(p => p.Id == m.Id);
                            if (isPreOpen) _preOpenAnalysisDone.Add(m.Id);
                            else _preKickoffAnalysisDone.Add(m.Id);

                            _logger.LogInformation("AI analysis triggered for match {MatchId} ({Phase})", m.Id, isPreOpen ? "pre-open" : "pre-kickoff");
                            await analysis.AnalyzeMatchAsync(m.Id, force: !isPreOpen, stoppingToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Pre-match AI analysis skipped.");
                    }
                }

                if (votableMatches.Count == 0)
                {
                    await Task.Delay(CheckInterval, stoppingToken);
                    continue;
                }

                var playerTelegramIds = await keoBia.GetAllTelegramUserIdsAsync(stoppingToken);

                foreach (var match in votableMatches)
                {
                    var bettors = await keoBia.GetMatchBettorTelegramIdsAsync(match.Id, stoppingToken);
                    var bettorSet = bettors.ToHashSet();

                    foreach (var telegramId in playerTelegramIds)
                    {
                        if (bettorSet.Contains(telegramId))
                        {
                            state.Remove($"{match.Id}:{telegramId}");
                            continue;
                        }

                        var key = $"{match.Id}:{telegramId}";
                        var lastNotified = state.GetValueOrDefault(key);

                        if (lastNotified.HasValue && (now - lastNotified.Value) < ReNotifyInterval)
                            continue;

                        try
                        {
                            var text = TelegramBotText.BuildReminderText(match);
                            var keyboard = new InlineKeyboardMarkup(
                                InlineKeyboardButton.WithCallbackData("🎯 Đặt kèo ngay", $"m:{match.Id}"));

                            await _bot.SendMessage(
                                telegramId,
                                text,
                                replyMarkup: keyboard,
                                cancellationToken: stoppingToken);

                            state[key] = now;
                            await SaveStateAsync(statePath, state, stoppingToken);

                            _logger.LogInformation(
                                "Sent bet reminder for match {MatchId} to user {TelegramUserId}.",
                                match.Id, telegramId);

                            await Task.Delay(50, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex) when (IsPermanentFailure(ex))
                        {
                            _logger.LogWarning(
                                "Skipping bet reminder for match {MatchId} to user {TelegramUserId}: {Error}",
                                match.Id, telegramId, ex.Message);
                            state[key] = now;
                            await SaveStateAsync(statePath, state, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Failed to send bet reminder for match {MatchId} to user {TelegramUserId}.",
                                match.Id, telegramId);
                        }
                    }
                }

                CleanupState(state, votableMatches.Select(m => m.Id).ToHashSet());
                await SaveStateAsync(statePath, state, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TelegramBetReminderService tick failed.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private static void CleanupState(Dictionary<string, DateTime?> state, HashSet<Guid> openMatchIds)
    {
        var keysToRemove = state.Keys
            .Where(k =>
            {
                var matchIdStr = k.Split(':')[0];
                return !Guid.TryParse(matchIdStr, out var id) || !openMatchIds.Contains(id);
            })
            .ToList();

        foreach (var key in keysToRemove)
            state.Remove(key);
    }

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
            _logger.LogWarning("Cannot resolve an active KeoBia site for Telegram bet reminders.");
            return false;
        }

        currentSite.Set(site.SiteId, site.Slug, site.Theme);
        return true;
    }

    private bool IsEnabled() =>
        _configuration.GetValue("KeoBia:Telegram:BetReminders:Enabled", true);

    private string ResolveSiteHost()
    {
        var configured = _configuration["KeoBia:Telegram:ResultNotifications:SiteHost"] ??
                         _configuration["KeoBia:OpenFootball:SiteHost"] ??
                         "cakeo26.click";

        if (Uri.TryCreate(configured, UriKind.Absolute, out var uri))
            return uri.Host;

        return configured;
    }

    private string StatePath() =>
        Path.Combine(_environment.ContentRootPath, "App_Data", "keobia-telegram-bet-reminders.json");

    private static async Task<Dictionary<string, DateTime?>> LoadStateAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return new Dictionary<string, DateTime?>();

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (raw is null)
                return new Dictionary<string, DateTime?>();

            return raw.ToDictionary(
                kv => kv.Key,
                kv => DateTime.TryParse(kv.Value, out var dt) ? (DateTime?)dt : null);
        }
        catch
        {
            return new Dictionary<string, DateTime?>();
        }
    }

    private static async Task SaveStateAsync(string path, Dictionary<string, DateTime?> state, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var raw = state.ToDictionary(
            kv => kv.Key,
            kv => kv.Value?.ToString("o") ?? "");

        var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = false });
        await File.WriteAllTextAsync(path, json, ct);
    }

    private static bool IsPermanentFailure(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("chat not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("can't initiate conversation", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("bot was blocked by the user", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("user is deactivated", StringComparison.OrdinalIgnoreCase);
    }
}
