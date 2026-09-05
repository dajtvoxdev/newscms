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

internal sealed class TelegramUnitTransitionNotificationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClient _bot;
    private readonly KeoBiaTelegramOptions _telegram;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramUnitTransitionNotificationService> _logger;

    public TelegramUnitTransitionNotificationService(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient bot,
        IOptions<KeoBiaTelegramOptions> telegram,
        IConfiguration configuration,
        ILogger<TelegramUnitTransitionNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _bot = bot;
        _telegram = telegram.Value;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_telegram.IsConfigured || !_configuration.GetValue("KeoBia:Telegram:UnitTransition:Enabled", true))
            return;

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

                var service = scope.ServiceProvider.GetRequiredService<IKeoBiaService>();
                var recipients = await service.GetPendingUnitTransitionTelegramUserIdsAsync(200, stoppingToken);
                var keyboard = new InlineKeyboardMarkup([
                    [
                        InlineKeyboardButton.WithCallbackData("🥜 Tiếp tục chơi", "transition:continue"),
                        InlineKeyboardButton.WithCallbackData("🛑 Dừng tại đây", "transition:stop")
                    ]
                ]);

                foreach (var telegramUserId in recipients)
                {
                    try
                    {
                        await _bot.SendMessage(
                            telegramUserId,
                            TelegramBotText.BuildUnitTransitionText(),
                            replyMarkup: keyboard,
                            cancellationToken: stoppingToken);
                        await service.MarkUnitTransitionTelegramSentAsync(telegramUserId, stoppingToken);
                        await Task.Delay(50, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to send unit transition notice to Telegram user {TelegramUserId}.",
                            telegramUserId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram unit transition notification tick failed.");
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

        var site = await resolver.ResolveBySlugAsync(slug.Trim(), ct);
        if (site is null || site.SiteId == Guid.Empty)
        {
            _logger.LogWarning("Cannot resolve KeoBia site for Telegram unit transition notices.");
            return false;
        }

        currentSite.Set(site.SiteId, site.Slug, site.Theme);
        return true;
    }

    private TimeSpan PollInterval()
    {
        var seconds = _configuration.GetValue("KeoBia:Telegram:UnitTransition:PollSeconds", 60);
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 15, 3600));
    }
}
