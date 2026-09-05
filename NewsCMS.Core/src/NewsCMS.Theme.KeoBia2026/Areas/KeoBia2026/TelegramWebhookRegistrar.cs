using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsCMS.Application.KeoBia;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026;

public sealed class TelegramWebhookRegistrar : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly KeoBiaTelegramOptions _options;
    private readonly ILogger<TelegramWebhookRegistrar> _logger;

    public TelegramWebhookRegistrar(
        IServiceProvider services,
        IOptions<KeoBiaTelegramOptions> options,
        ILogger<TelegramWebhookRegistrar> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Webhook.Enabled)
            return;

        if (!_options.IsConfigured)
        {
            _logger.LogWarning("Telegram webhook is enabled but BotUsername/BotToken is missing.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Webhook.Url) ||
            string.IsNullOrWhiteSpace(_options.Webhook.Secret))
        {
            _logger.LogWarning("Telegram webhook is enabled but Webhook.Url or Webhook.Secret is missing.");
            return;
        }

        try
        {
            var bot = _services.GetRequiredService<ITelegramBotClient>();
            await bot.SetWebhook(
                _options.Webhook.Url.Trim(),
                secretToken: _options.Webhook.Secret.Trim(),
                allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
                dropPendingUpdates: true,
                cancellationToken: cancellationToken);

            await bot.SetMyCommands(
                [
                    new BotCommand("start", "🏠 Bắt đầu"),
                    new BotCommand("datkeo", "🎯 Đặt kèo"),
                    new BotCommand("keo", "🍺 Kèo của tôi"),
                    new BotCommand("lich", "📅 Lịch và kết quả"),
                    new BotCommand("bxh", "🏆 Bảng xếp hạng"),
                    new BotCommand("quiz", "📊 Kết quả bình chọn"),
                    new BotCommand("nopbia", "🥜 Nộp cốc bia và gói lạc"),
                    new BotCommand("lichsunopbia", "🧾 Lịch sử nộp cốc bia và gói lạc"),
                    new BotCommand("dunglai", "🛑 Tôi muốn dừng lại"),
                    new BotCommand("chat", "🤖 Hỏi AI về bóng đá"),
                    new BotCommand("changelog", "📋 Nhật ký cập nhật")
                ],
                cancellationToken: cancellationToken);

            _logger.LogInformation("Telegram webhook registered at {WebhookUrl}.", _options.Webhook.Url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Telegram webhook at {WebhookUrl}.", _options.Webhook.Url);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
