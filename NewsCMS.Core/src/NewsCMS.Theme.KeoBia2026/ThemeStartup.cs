using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NewsCMS.Application.KeoBia;
using NewsCMS.Shared.Theming;
using NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026;
using NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026.Hubs;
using Telegram.Bot;

namespace NewsCMS.Theme.KeoBia2026;

public class ThemeStartup : IThemeModule
{
    public string Name => "KeoBia2026";
    public string DisplayName => "Góp Gạo Thổi Cơm Chung";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<KeoBiaTelegramOptions>>().Value;
            return new TelegramBotClient(options.BotToken);
        });
        services.AddHostedService<TelegramWebhookRegistrar>();
        services.AddHostedService<TelegramResultNotificationService>();
        services.AddHostedService<TelegramUnitTransitionNotificationService>();
        services.AddHostedService<TelegramBetReminderService>();
        services.AddHostedService<KeoBiaAutoAssignService>();
    }

    public void ConfigureRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapThemeRoute(Name,
            "keobia2026_home",
            "",
            new { area = "KeoBia2026", controller = "Home", action = "Index" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_policy",
            "policy",
            new { area = "KeoBia2026", controller = "Home", action = "Policy" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_profile",
            "keobia/profile",
            new { area = "KeoBia2026", controller = "Home", action = "Profile" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_unit_transition_notice",
            "keobia/unit-transition/notice",
            new { area = "KeoBia2026", controller = "Home", action = "UnitTransitionNotice" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_unit_transition_decision",
            "keobia/unit-transition/decision",
            new { area = "KeoBia2026", controller = "Home", action = "UnitTransitionDecision" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_avatar",
            "keobia/avatar",
            new { area = "KeoBia2026", controller = "Home", action = "Avatar" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_chat_image",
            "keobia/chat-image",
            new { area = "KeoBia2026", controller = "Home", action = "ChatImage" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_telegram",
            "keobia/telegram",
            new { area = "KeoBia2026", controller = "Home", action = "Telegram" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_telegram_login",
            "keobia/telegram/login",
            new { area = "KeoBia2026", controller = "Home", action = "TelegramLogin" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_telegram_callback",
            "keobia/telegram/callback",
            new { area = "KeoBia2026", controller = "Home", action = "TelegramCallback" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_telegram_webhook",
            "keobia/telegram/webhook",
            new { area = "KeoBia2026", controller = "TelegramBot", action = "Webhook" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_payment_webhook",
            "keobia/payment/webhook",
            new { area = "KeoBia2026", controller = "TelegramBot", action = "PaymentWebhook" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_bet",
            "keobia/bet",
            new { area = "KeoBia2026", controller = "Home", action = "Bet" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_delete_bet",
            "keobia/delete-bet",
            new { area = "KeoBia2026", controller = "Home", action = "DeleteBet" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_my_predictions",
            "keobia/my-predictions",
            new { area = "KeoBia2026", controller = "Home", action = "MyPredictions" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_player_history",
            "keobia/player-history",
            new { area = "KeoBia2026", controller = "Home", action = "PlayerHistory" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_match_history",
            "keobia/match-history",
            new { area = "KeoBia2026", controller = "Home", action = "MatchHistory" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_chat",
            "keobia/chat",
            new { area = "KeoBia2026", controller = "Home", action = "Chat" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_analysis",
            "keobia/analysis",
            new { area = "KeoBia2026", controller = "Home", action = "Analysis" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_share_beer",
            "keobia/share-beer",
            new { area = "KeoBia2026", controller = "Home", action = "ShareBeer" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_quiz_active",
            "keobia/quiz/active",
            new { area = "KeoBia2026", controller = "Home", action = "QuizActive" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_quiz_vote",
            "keobia/quiz/vote",
            new { area = "KeoBia2026", controller = "Home", action = "QuizVote" });
        endpoints.MapThemeRoute(Name,
            "keobia2026_quiz_votes",
            "keobia/quiz/votes",
            new { area = "KeoBia2026", controller = "Home", action = "QuizVotes" });
        endpoints.MapHub<KeoBiaRealtimeHub>(KeoBiaRealtimeHub.HubPath);
    }
}
