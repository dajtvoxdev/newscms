using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsCMS.Application.KeoBia;
using NewsCMS.Application.Site;
using Telegram.Bot;

namespace NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026;

internal sealed class TelegramResultNotificationService : BackgroundService
{
    private const int NotificationTake = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClient _bot;
    private readonly KeoBiaTelegramOptions _telegram;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<TelegramResultNotificationService> _logger;

    public TelegramResultNotificationService(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient bot,
        IOptions<KeoBiaTelegramOptions> telegram,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<TelegramResultNotificationService> logger)
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
        var sent = await LoadSentAsync(statePath, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                if (!await ResolveSiteScopeAsync(scope.ServiceProvider, stoppingToken))
                {
                    await Task.Delay(PollInterval(), stoppingToken);
                    continue;
                }

                var keoBia = scope.ServiceProvider.GetRequiredService<IKeoBiaService>();
                var items = await keoBia.GetTelegramResultNotificationsAsync(NotificationTake, stoppingToken);

                foreach (var item in items.OrderBy(x => x.SettledAt))
                {
                    if (sent.Contains(item.BetId))
                        continue;

                    try
                    {
                        await _bot.SendMessage(
                            item.TelegramUserId,
                            TelegramBotText.BuildResultNotificationText(item),
                            replyMarkup: TelegramBotText.MainMenuKeyboard(),
                            cancellationToken: stoppingToken);

                        sent.Add(item.BetId);
                        await SaveSentAsync(statePath, sent, stoppingToken);
                        _logger.LogInformation(
                            "Sent Telegram result notification {BetId} to user {TelegramUserId}.",
                            item.BetId,
                            item.TelegramUserId);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (IsPermanentTelegramDeliveryFailure(ex))
                    {
                        _logger.LogWarning(
                            "Skipping Telegram result notification {BetId} for user {TelegramUserId}; the bot cannot message this chat: {Error}",
                            item.BetId,
                            item.TelegramUserId,
                            ex.Message);
                        sent.Add(item.BetId);
                        await SaveSentAsync(statePath, sent, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to send Telegram result notification {BetId} for user {TelegramUserId}.",
                            item.BetId,
                            item.TelegramUserId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send Telegram result notifications.");
            }

            await Task.Delay(PollInterval(), stoppingToken);
        }
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
            _logger.LogWarning("Cannot resolve an active KeoBia site for Telegram result notifications.");
            return false;
        }

        currentSite.Set(site.SiteId, site.Slug, site.Theme);
        return true;
    }

    private bool IsEnabled() =>
        _configuration.GetValue("KeoBia:Telegram:ResultNotifications:Enabled", true);

    private TimeSpan PollInterval()
    {
        var seconds = _configuration.GetValue("KeoBia:Telegram:ResultNotifications:PollSeconds", 60);
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 15, 3600));
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

    private string StatePath() =>
        Path.Combine(_environment.ContentRootPath, "App_Data", "keobia-telegram-result-notified.txt");

    private static async Task<HashSet<Guid>> LoadSentAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return [];

        var lines = await File.ReadAllLinesAsync(path, ct);
        return lines
            .Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty)
            .Where(x => x != Guid.Empty)
            .ToHashSet();
    }

    private static bool IsPermanentTelegramDeliveryFailure(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("chat not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("can't initiate conversation", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("bot was blocked by the user", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("user is deactivated", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SaveSentAsync(string path, HashSet<Guid> sent, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(path, sent.Select(x => x.ToString("D")).Order(), ct);
    }
}
