using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsCMS.Application.Common;
using NewsCMS.Application.KeoBia;
using NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026.Hubs;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026.Controllers;

[Area("KeoBia2026")]
public sealed class TelegramBotController : Controller
{
    private const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";
    private const string ScheduledStatus = "Scheduled";
    private const string LiveStatus = "Live";
    private const string FinishedStatus = "Finished";
    private const string HomeChoice = "home";
    private const string DrawChoice = "draw";
    private const string AwayChoice = "away";
    private static readonly ConcurrentDictionary<string, byte> RunningChatJobs = new();
    private static readonly ConcurrentDictionary<long, int> PendingBeerPrompts = new();
    private static readonly ConcurrentDictionary<long, PendingScorePrompt> PendingScorePrompts = new();

    private static readonly TimeSpan VoteOpenWindow = TimeSpan.FromDays(1);
    private static readonly TimeSpan VoteLockWindow = TimeSpan.FromMinutes(20);

    private readonly IKeoBiaService _keoBia;
    private readonly IKeoBiaAnalysisService _analysis;
    private readonly ITelegramBotClient _bot;
    private readonly IHubContext<KeoBiaRealtimeHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KeoBiaTelegramOptions _telegram;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramBotController> _logger;

    public TelegramBotController(
        IKeoBiaService keoBia,
        IKeoBiaAnalysisService analysis,
        ITelegramBotClient bot,
        IHubContext<KeoBiaRealtimeHub> hubContext,
        IServiceScopeFactory scopeFactory,
        IOptions<KeoBiaTelegramOptions> telegram,
        IConfiguration configuration,
        ILogger<TelegramBotController> logger)
    {
        _keoBia = keoBia;
        _analysis = analysis;
        _bot = bot;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _telegram = telegram.Value;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Webhook([FromBody] Update update, CancellationToken ct)
    {
        if (!HasValidSecretToken())
            return Unauthorized();

        try
        {
            await HandleUpdateAsync(update, ct);
        }
        catch (Exception ex) when (ex.Message.Contains("message is not modified"))
        {
            _logger.LogDebug("Telegram update {UpdateId} skipped: message content unchanged.", update.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Telegram update {UpdateId}", update.Id);
        }

        return Ok();
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PaymentWebhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(ct);
        var result = await _keoBia.ConfirmBeerPaymentWebhookAsync(payload, ReadPaymentWebhookSecret(), ct);
        if (!result.Succeeded || result.Value is null)
            return BadRequest(new { ok = false, error = result.Error });

        if (!result.Value.WasAlreadyPaid)
            await NotifyBeerPaymentPaidAsync(result.Value.Payment, ct);

        return Ok(new { ok = true, paid = true, alreadyPaid = result.Value.WasAlreadyPaid });
    }

    private async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        if (update.Message?.From is { } messageUser)
        {
            var ensured = await EnsurePlayerAsync(messageUser, ct);
            if (!ensured.Succeeded)
            {
                await _bot.SendMessage(
                    update.Message.Chat.Id,
                    ensured.Error ?? "Không tạo được hồ sơ Telegram.",
                    cancellationToken: ct);
                return;
            }

            await HandleMessageAsync(update.Message, ct);
            return;
        }

        if (update.CallbackQuery is { } callback)
        {
            if (callback.Data is "transition:continue" or "transition:stop")
            {
                await HandleUnitTransitionCallbackAsync(callback, ct);
                return;
            }

            var ensured = await EnsurePlayerAsync(callback.From, ct);
            if (!ensured.Succeeded)
            {
                await AnswerCallbackAsync(callback, ensured.Error ?? "Không tạo được hồ sơ Telegram.", ct);
                return;
            }

            await HandleCallbackAsync(callback, ct);
        }
    }

    private async Task HandleUnitTransitionCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        if (callback.Data == "transition:stop")
        {
            await HandleStopPlayingConfirmationAsync(callback, ct);
            return;
        }

        var result = await _keoBia.RespondUnitTransitionNoticeByTelegramIdAsync(
            callback.From.Id,
            false,
            ct);
        if (!result.Succeeded || result.Value is null)
        {
            await AnswerCallbackAsync(callback, result.Error ?? "Chưa ghi nhận được lựa chọn.", ct);
            return;
        }

        const string text = "🥜 Bạn đã chọn tiếp tục chơi với gói lạc từ vòng tứ kết.";
        await AnswerCallbackAsync(callback, "Đã ghi nhận lựa chọn.", ct);

        if (callback.Message is null) return;
        try
        {
            await _bot.EditMessageText(
                callback.Message.Chat.Id,
                callback.Message.Id,
                text,
                replyMarkup: MainMenuKeyboard(),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not edit Telegram unit transition notice message.");
            await _bot.SendMessage(
                callback.Message.Chat.Id,
                text,
                replyMarkup: MainMenuKeyboard(),
                cancellationToken: ct);
        }
    }

    private async Task HandleStopPlayingConfirmationAsync(CallbackQuery callback, CancellationToken ct)
    {
        var result = await _keoBia.RespondUnitTransitionNoticeByTelegramIdAsync(
            callback.From.Id,
            true,
            ct);
        if (!result.Succeeded || result.Value is null)
        {
            await AnswerCallbackAsync(callback, result.Error ?? "Chưa ghi nhận được yêu cầu dừng lại.", ct);
            return;
        }

        const string text = "🛑 Bạn đã dừng lại. Tài khoản không tham gia các kèo tiếp theo, các kèo chưa diễn ra đã được hủy và bạn không bị tính kèo miss mới.";
        await AnswerCallbackAsync(callback, "Đã ghi nhận yêu cầu dừng lại.", ct);

        if (callback.Message is null) return;
        try
        {
            await _bot.EditMessageText(
                callback.Message.Chat.Id,
                callback.Message.Id,
                text,
                replyMarkup: null,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not edit Telegram stop-playing confirmation message.");
            await _bot.SendMessage(callback.Message.Chat.Id, text, cancellationToken: ct);
        }
    }

    private async Task SendStopPlayingPromptAsync(long chatId, CancellationToken ct)
    {
        await _bot.SendMessage(
            chatId,
            BuildStopPlayingPromptText(),
            replyMarkup: StopPlayingConfirmationKeyboard(),
            cancellationToken: ct);
    }

    private async Task EditStopPlayingPromptAsync(Message message, CancellationToken ct)
    {
        await EditTextOrSendAsync(
            message,
            BuildStopPlayingPromptText(),
            StopPlayingConfirmationKeyboard(),
            ct);
    }

    private static string BuildStopPlayingPromptText() =>
        "🛑 Bạn muốn dừng tham gia?\n\n" +
        "Khi xác nhận, hệ thống sẽ khóa các kèo tiếp theo, hủy các kèo chưa diễn ra và không tính miss mới. Lựa chọn này không thể hoàn tác trên bot.";

    private static InlineKeyboardMarkup StopPlayingConfirmationKeyboard() => new(
        [
            [InlineKeyboardButton.WithCallbackData("🛑 Tôi muốn dừng lại", "stop:confirm")],
            [InlineKeyboardButton.WithCallbackData("← Quay lại", "cmd:start")]
        ]);

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        var command = NormalizeCommand(message.Text);
        switch (command)
        {
            case "/start":
                await SendStartAsync(message.Chat.Id, ct);
                break;
            case "/datkeo":
                await SendBetMatchListAsync(message.Chat.Id, ct);
                break;
            case "/keo":
                await SendMyBetsAsync(message.Chat.Id, message.From!.Id, ct);
                break;
            case "/lich":
                await SendScheduleAsync(message.Chat.Id, ct);
                break;
            case "/bxh":
            case "/xephang":
                await SendLeaderboardAsync(message.Chat.Id, ct);
                break;
            case "/quiz":
            case "/binhchon":
                await SendQuizResultsAsync(message.Chat.Id, ct);
                break;
            case "/nopbia":
                await SendBeerPaymentPromptAsync(message.Chat.Id, message.From!.Id, ct);
                break;
            case "/lichsunopbia":
                await SendBeerPaymentHistoryAsync(message.Chat.Id, ct);
                break;
            case "/dunglai":
                await SendStopPlayingPromptAsync(message.Chat.Id, ct);
                break;
            case "/changelog":
                await SendChangelogAsync(message.Chat.Id, ct);
                break;
            case "/chat":
                QueueChatCommand(message);
                break;
            default:
                if (await TryHandlePendingScorePromptAsync(message, ct))
                    break;
                if (PendingBeerPrompts.ContainsKey(message.From!.Id) && int.TryParse((message.Text ?? string.Empty).Trim(), out var typedCups))
                {
                    await CreateAndSendBeerPaymentAsync(message.Chat.Id, message.From.Id, typedCups, ct);
                    break;
                }
                await SendStartAsync(message.Chat.Id, ct);
                break;
        }
    }

    private sealed record PendingScorePrompt(Guid MatchId, string Choice, string? StarType, int Cups, int? HomeScore);

    private async Task HandleCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        var data = callback.Data ?? string.Empty;
        if (callback.Message is null)
        {
            await AnswerCallbackAsync(callback, "Không xác định được tin nhắn.", ct);
            return;
        }

        if (data == "cmd:datkeo")
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditBetMatchListAsync(callback.Message, ct);
            return;
        }

        if (data == "cmd:start")
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditTextOrSendAsync(callback.Message, TelegramBotText.StartText(), MainMenuKeyboard(), ct);
            return;
        }

        if (data == "cmd:dunglai")
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditStopPlayingPromptAsync(callback.Message, ct);
            return;
        }

        if (data == "stop:confirm")
        {
            await HandleStopPlayingConfirmationAsync(callback, ct);
            return;
        }

        if (data == "cmd:keo")
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditMyBetsAsync(callback.Message, callback.From.Id, ct);
            return;
        }

        if (data == "cmd:lich")
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditScheduleAsync(callback.Message, ct);
            return;
        }

        if (data == "cmd:bxh")
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditLeaderboardAsync(callback.Message, ct);
            return;
        }

        if (data == "cmd:quiz")
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditQuizResultsAsync(callback.Message, ct);
            return;
        }

        if (data == "cmd:nopbia")
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditBeerPaymentPromptAsync(callback.Message, callback.From.Id, ct);
            return;
        }

        if (data == "cmd:lichsunopbia")
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditBeerPaymentHistoryAsync(callback.Message, ct);
            return;
        }

        var parts = data.Split(':');
        if (parts.Length == 2 && parts[0] == "m" && Guid.TryParse(parts[1], out var matchId))
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditMatchChoicesAsync(callback.Message, matchId, ct);
            return;
        }

        if (parts.Length == 2 && parts[0] == "a" && Guid.TryParse(parts[1], out matchId))
        {
            await AnswerCallbackAsync(callback, "Đang lấy phân tích AI...", ct);
            await EditMatchAnalysisAsync(callback.Message, matchId, ct);
            return;
        }

        if (parts.Length == 3 &&
            parts[0] == "c" &&
            Guid.TryParse(parts[1], out matchId) &&
            IsChoice(parts[2]))
        {
            await AskCorrectScoreAsync(callback, matchId, parts[2], 1, null, ct);
            return;
        }

        if (parts.Length == 3 && parts[0] == "skipscore" && Guid.TryParse(parts[1], out matchId) && IsChoice(parts[2]))
        {
            await SubmitBetAsync(callback, matchId, parts[2], 1, null, null, null, ct);
            return;
        }

        if (parts.Length >= 4 && parts[0] == "score" && Guid.TryParse(parts[1], out matchId) && IsChoice(parts[2]))
        {
            var star = parts.Length == 5 && (parts[4] == "hope" || parts[4] == "devil") ? parts[4] : null;
            if (parts[3] == "other")
            {
                await StartManualScorePromptAsync(callback, matchId, parts[2], star, ct);
                return;
            }

            var scoreParts = parts[3].Split('-');
            if (scoreParts.Length == 2 && int.TryParse(scoreParts[0], out var homeScore) && int.TryParse(scoreParts[1], out var awayScore))
            {
                await SubmitBetAsync(callback, matchId, parts[2], 1, star, homeScore, awayScore, ct);
                return;
            }
        }

        if (parts.Length == 3 &&
            parts[0] == "star" &&
            Guid.TryParse(parts[1], out matchId) &&
            (parts[2] == "hope" || parts[2] == "devil"))
        {
            await AnswerCallbackAsync(callback, null, ct);
            await EditStarChoiceAsync(callback.Message, matchId, parts[2], ct);
            return;
        }

        if (parts.Length == 4 &&
            parts[0] == "cs" &&
            Guid.TryParse(parts[1], out matchId) &&
            IsChoice(parts[2]) &&
            (parts[3] == "hope" || parts[3] == "devil"))
        {
            await AskCorrectScoreAsync(callback, matchId, parts[2], 1, parts[3], ct);
            return;
        }

        if (parts.Length == 4 && parts[0] == "skipscore" && Guid.TryParse(parts[1], out matchId) && IsChoice(parts[2]) && (parts[3] == "hope" || parts[3] == "devil"))
        {
            await SubmitBetAsync(callback, matchId, parts[2], 1, parts[3], null, null, ct);
            return;
        }

        if (parts.Length == 4 &&
            parts[0] == "b" &&
            Guid.TryParse(parts[1], out matchId) &&
            IsChoice(parts[2]) &&
            int.TryParse(parts[3], out var cups))
        {
            await AskCorrectScoreAsync(callback, matchId, parts[2], 1, null, ct);
            return;
        }

        if (parts.Length == 3 &&
            parts[0] == "q" &&
            Guid.TryParse(parts[1], out var questionId))
        {
            var voteResult = await _keoBia.SubmitQuizVoteByTelegramIdAsync(callback.From.Id, questionId, parts[2], ct);
            await AnswerCallbackAsync(callback,
                voteResult.Succeeded ? "✅ Đã ghi nhận bình chọn!" : voteResult.Error,
                ct);
            if (voteResult.Succeeded)
                await RefreshQuizMessageAsync(callback.Message, questionId, ct);
            return;
        }

        if (parts.Length == 2 && parts[0] == "pay" && int.TryParse(parts[1], out var payCups))
        {
            await AnswerCallbackAsync(callback, null, ct);
            await CreateAndSendBeerPaymentAsync(callback.Message.Chat.Id, callback.From.Id, payCups, ct);
            return;
        }

        await AnswerCallbackAsync(callback, "Lựa chọn không hợp lệ.", ct);
    }

    private async Task RefreshQuizMessageAsync(Message message, Guid questionId, CancellationToken ct)
    {
        var result = await _keoBia.GetQuestionVotesAsync(questionId, ct);
        if (!result.Succeeded || result.Value is null) return;

        var q = result.Value;
        var total = q.Voters.Count;
        var sb = new StringBuilder();
        sb.AppendLine("❓ CÂU HỎI NHANH");
        sb.AppendLine();
        sb.AppendLine(q.Text);
        sb.AppendLine();
        sb.AppendLine($"🎁 Đúng giảm {KeoBiaUnitRules.FormatCount(q.RewardCups, q.UnitCode)} · Sai {(q.PenaltyCups > 0 ? $"mất {KeoBiaUnitRules.FormatCount(q.PenaltyCups, q.UnitCode)}" : "không mất gì")} · {total} bình chọn");
        foreach (var c in q.Choices)
        {
            var names = q.Voters.Where(v => string.Equals(v.ChoiceKey, c.Key, StringComparison.OrdinalIgnoreCase))
                .Select(v => v.DisplayName)
                .Take(8)
                .ToList();
            sb.AppendLine($"• {c.Label}: {c.VoteCount}" + (names.Count > 0 ? $" ({string.Join(", ", names)})" : ""));
        }

        var rows = q.Choices
            .Select(c => new[] { InlineKeyboardButton.WithCallbackData(c.Label, $"q:{q.QuestionId}:{c.Key}") })
            .ToArray();
        try
        {
            await _bot.EditMessageText(message.Chat.Id, message.Id, sb.ToString().TrimEnd(),
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh quiz Telegram message {QuestionId}", questionId);
        }
    }

    private async Task SendStartAsync(long chatId, CancellationToken ct)
    {
        var board = await _keoBia.GetLossLeaderboardAsync(1000, ct);
        var totalBeerCups = board.Sum(x => x.HistoricalLostBeerCups);
        var totalPeanutPacks = board.Sum(x => x.HistoricalLostPeanutPacks);
        await _bot.SendMessage(
            chatId,
            TelegramBotText.StartText(totalBeerCups, totalPeanutPacks),
            replyMarkup: MainMenuKeyboard(),
            cancellationToken: ct);
    }

    private async Task EditTextOrSendAsync(Message message, string text, InlineKeyboardMarkup? replyMarkup, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(message.Text))
        {
            await _bot.SendMessage(message.Chat.Id, text, replyMarkup: replyMarkup, cancellationToken: ct);
            return;
        }

        await _bot.EditMessageText(message.Chat.Id, message.Id, text, replyMarkup: replyMarkup, cancellationToken: ct);
    }

    private async Task SendChangelogAsync(long chatId, CancellationToken ct)
    {
        var changelogs = await _keoBia.GetChangelogsAsync(3, ct);
        if (changelogs.Count == 0)
        {
            await _bot.SendMessage(chatId, "Chưa có nhật ký cập nhật.", cancellationToken: ct);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📋 *Nhật ký cập nhật*\n");
        foreach (var log in changelogs)
        {
            sb.AppendLine($"*{log.Version} — {log.Title}*");
            sb.AppendLine(log.Content);
            sb.AppendLine();
        }

        await _bot.SendMessage(chatId, sb.ToString().TrimEnd(), parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
    }

    private void QueueChatCommand(Message message)
    {
        var key = $"{message.Chat.Id}:{message.Id}";
        if (!RunningChatJobs.TryAdd(key, 0))
        {
            _logger.LogDebug("Telegram chat message {ChatJobKey} already queued/running.", key);
            return;
        }

        var publicBaseUrl = ResolvePublicBaseUrlForBackground();

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var keoBia = scope.ServiceProvider.GetRequiredService<IKeoBiaService>();
                await HandleChatCommandAsync(message, keoBia, publicBaseUrl, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process queued Telegram chat message {ChatJobKey}", key);
            }
            finally
            {
                RunningChatJobs.TryRemove(key, out _);
            }
        });
    }

    private string ResolvePublicBaseUrlForBackground()
    {
        var configured = _configuration["Storage:PublicBaseUrl"]
            ?? _configuration["Site:PublicBaseUrl"]
            ?? _configuration["KeoBia:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        var request = HttpContext?.Request;
        return request is not null && request.Host.HasValue
            ? $"{request.Scheme}://{request.Host}"
            : string.Empty;
    }

    private async Task HandleChatCommandAsync(Message message, IKeoBiaService keoBia, string publicBaseUrl, CancellationToken ct)
    {
        var text = (message.Text ?? "").Trim();
        var spaceIndex = text.IndexOf(' ');
        var question = spaceIndex >= 0 ? text[(spaceIndex + 1)..].Trim() : "";
        question = System.Text.RegularExpressions.Regex.Replace(question, @"@\w+", "").Trim();

        if (string.IsNullOrWhiteSpace(question))
        {
            await _bot.SendMessage(
                message.Chat.Id,
                "Gửi câu hỏi sau lệnh /chat\n\nVí dụ:\n• /chat World Cup 2026 diễn ra ở đâu?\n• /chat Lịch thi đấu hôm nay\n• /chat Tôi đã dự đoán bao nhiêu kèo?",
                cancellationToken: ct);
            return;
        }

        await _bot.SendChatAction(message.Chat.Id, Telegram.Bot.Types.Enums.ChatAction.Typing, cancellationToken: ct);

        if (IsLikelyImageRequest(question))
        {
            var imageResponse = await keoBia.ChatWithAiAsync(message.From!.Id, question, ct);
            await SendAiChatResponseAsync(message.Chat.Id, StripMarkdownBold(imageResponse), publicBaseUrl, ct);
            return;
        }

        var streamMessage = await _bot.SendMessage(message.Chat.Id, "Đang nghĩ...", cancellationToken: ct);
        var buffer = new StringBuilder();
        var lastEdit = DateTime.UtcNow;
        var response = await keoBia.ChatWithAiStreamingAsync(
            message.From!.Id,
            question,
            async (delta, token) =>
            {
                buffer.Append(delta);
                if (buffer.Length < 60 && DateTime.UtcNow - lastEdit < TimeSpan.FromSeconds(1))
                    return;

                lastEdit = DateTime.UtcNow;
                await EditAiStreamMessageAsync(message.Chat.Id, streamMessage.Id, StripMarkdownBold(buffer.ToString()), token);
            },
            ct);
        var cleanResponse = StripMarkdownBold(response);

        await EditAiStreamMessageAsync(message.Chat.Id, streamMessage.Id, cleanResponse, ct);
    }

    private async Task EditAiStreamMessageAsync(long chatId, int messageId, string text, CancellationToken ct)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "Đang nghĩ..." : text;
        if (value.Length > 3900) value = value[^3900..];
        try
        {
            await _bot.EditMessageText(chatId, messageId, value, cancellationToken: ct);
        }
        catch
        {
            // Telegram rejects edits with identical text or temporary message state; next chunk will retry.
        }
    }

    private static bool IsLikelyImageRequest(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("tạo ảnh")
            || value.Contains("tao anh")
            || value.Contains("vẽ ảnh")
            || value.Contains("ve anh")
            || value.Contains("generate image")
            || value.Contains("make image")
            || value.Contains("draw image");
    }

    private async Task SendAiChatResponseAsync(long chatId, string response, string publicBaseUrl, CancellationToken ct)
    {
        if (TryParseImageResponse(response, out var imageUrl, out var caption))
        {
            await _bot.SendPhoto(chatId, ToAbsoluteUrl(imageUrl, publicBaseUrl), caption: caption, cancellationToken: ct);
            return;
        }

        await _bot.SendMessage(chatId, response, cancellationToken: ct);
    }

    private bool TryParseImageResponse(string text, out string imageUrl, out string? caption)
    {
        imageUrl = string.Empty;
        caption = null;

        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("IMAGE_URL:", StringComparison.OrdinalIgnoreCase))
                imageUrl = line["IMAGE_URL:".Length..].Trim();
            else if (line.StartsWith("CAPTION:", StringComparison.OrdinalIgnoreCase))
                caption = line["CAPTION:".Length..].Trim();
        }

        return !string.IsNullOrWhiteSpace(imageUrl);
    }

    private static string ToAbsoluteUrl(string url, string publicBaseUrl)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;

        var path = url.StartsWith('/') ? url : "/" + url;
        if (!string.IsNullOrWhiteSpace(publicBaseUrl))
            return publicBaseUrl.TrimEnd('/') + path;

        return path;
    }

    private static string StripMarkdownBold(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    sb.Append(text, i + 2, end - i - 2);
                    i = end + 2;
                    continue;
                }
            }
            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    private async Task SendBetMatchListAsync(long chatId, CancellationToken ct)
    {
        var (text, keyboard) = await BuildBetMatchListAsync(ct);
        await _bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
    }

    private async Task EditBetMatchListAsync(Message message, CancellationToken ct)
    {
        var (text, keyboard) = await BuildBetMatchListAsync(ct);
        await EditTextOrSendAsync(message, text, keyboard, ct);
    }

    private async Task<(string Text, InlineKeyboardMarkup Keyboard)> BuildBetMatchListAsync(CancellationToken ct)
    {
        var matches = await _keoBia.SearchMatchesAsync(null, ScheduledStatus, 1, 50, ct);
        var openMatches = matches.Items
            .Where(IsOpenForVoting)
            .OrderBy(x => x.KickoffAt)
            .Take(10)
            .ToList();

        if (openMatches.Count == 0)
            return ("🔒 Hiện chưa có trận nào đang mở kèo.", MainMenuKeyboard());

        var rows = openMatches
            .Select(match => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    TelegramBotText.MatchButtonLabel(match),
                    $"m:{match.Id}")
            })
            .Append([InlineKeyboardButton.WithCallbackData("📅 Lịch", "cmd:lich")]);

        return ("🎯 Chọn trận đang mở kèo:", new InlineKeyboardMarkup(rows));
    }

    private async Task EditMatchChoicesAsync(Message message, Guid matchId, CancellationToken ct)
    {
        var match = await _keoBia.GetPublicMatchAsync(matchId, ct);
        if (match is null)
        {
            await _bot.EditMessageText(
                message.Chat.Id,
                message.Id,
                "Không tìm thấy trận đấu.",
                replyMarkup: MainMenuKeyboard(),
                cancellationToken: ct);
            return;
        }

        string? currentChoice = null;
        try
        {
            currentChoice = await _keoBia.GetPlayerBetChoiceAsync(message.Chat.Id, matchId, ct);
        }
        catch { }

        string Label(string choice)
        {
            var label = TelegramBotText.ChoiceButtonLabel(match, choice);
            return currentChoice == choice ? $"✅ {label}" : label;
        }

        InlineKeyboardMarkup Keyboard()
        {
            var firstRow = match.AllowsDraw
                ? new List<InlineKeyboardButton>
                  {
                      InlineKeyboardButton.WithCallbackData(Label(HomeChoice), $"c:{match.Id}:{HomeChoice}"),
                      InlineKeyboardButton.WithCallbackData(Label(DrawChoice), $"c:{match.Id}:{DrawChoice}"),
                      InlineKeyboardButton.WithCallbackData(Label(AwayChoice), $"c:{match.Id}:{AwayChoice}")
                  }
                : new List<InlineKeyboardButton>
                  {
                      InlineKeyboardButton.WithCallbackData(Label(HomeChoice), $"c:{match.Id}:{HomeChoice}"),
                      InlineKeyboardButton.WithCallbackData(Label(AwayChoice), $"c:{match.Id}:{AwayChoice}")
                  };
            return new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>>
            {
                firstRow,
                new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData("⭐ Hi vọng", $"star:{match.Id}:hope"),
                    InlineKeyboardButton.WithCallbackData("😈 Ma quỷ", $"star:{match.Id}:devil")
                },
                new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData("🧠 Phân tích AI", $"a:{match.Id}")
                },
                new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData("↩️ Trận khác", "cmd:datkeo")
                }
            });
        }

        var keyboard = IsOpenForVoting(match) ? Keyboard() : MainMenuKeyboard();

        var detailText = BuildMatchDetailText(match);
        if (currentChoice != null)
        {
            var choiceName = currentChoice == HomeChoice ? match.HomeCode
                : currentChoice == AwayChoice ? match.AwayCode : "Hòa";
            detailText += $"\n\n🍺 Bạn đã chọn: {choiceName} (nhấn để đổi)";
        }

        await _bot.EditMessageText(
            message.Chat.Id,
            message.Id,
            detailText,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task EditStarChoiceAsync(Message message, Guid matchId, string starType, CancellationToken ct)
    {
        var match = await _keoBia.GetPublicMatchAsync(matchId, ct);
        if (match is null)
        {
            await _bot.EditMessageText(message.Chat.Id, message.Id, "Không tìm thấy trận đấu.",
                replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
            return;
        }

        var starIcon = starType == "hope" ? "⭐" : "😈";
        var starName = starType == "hope" ? "Hi vọng" : "Ma quỷ";
        InlineKeyboardMarkup StarKeyboard()
        {
            var firstRow = match.AllowsDraw
                ? new List<InlineKeyboardButton>
                  {
                      InlineKeyboardButton.WithCallbackData($"{starIcon} {match.HomeCode}", $"cs:{matchId}:{HomeChoice}:{starType}"),
                      InlineKeyboardButton.WithCallbackData($"{starIcon} Hòa", $"cs:{matchId}:{DrawChoice}:{starType}"),
                      InlineKeyboardButton.WithCallbackData($"{starIcon} {match.AwayCode}", $"cs:{matchId}:{AwayChoice}:{starType}")
                  }
                : new List<InlineKeyboardButton>
                  {
                      InlineKeyboardButton.WithCallbackData($"{starIcon} {match.HomeCode}", $"cs:{matchId}:{HomeChoice}:{starType}"),
                      InlineKeyboardButton.WithCallbackData($"{starIcon} {match.AwayCode}", $"cs:{matchId}:{AwayChoice}:{starType}")
                  };
            return new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>>
            {
                firstRow,
                new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData("↩️ Đổi cửa", $"m:{matchId}")
                }
            });
        }

        var text = $"Ngôi sao {starName}\n\n" +
                   $"Chọn cửa với ngôi sao {starName}:\n" +
                   (starType == "hope"
                       ? $"Thắng: -1 {match.UnitShortLabel} đã mất | Thua: x2 {match.UnitShortLabel} đã mất"
                       : $"Kèo cân bằng: chọn thoải mái\nKèo chênh lệch: chỉ được chọn Hòa hoặc đội yếu hơn\nThắng: -1 {match.UnitShortLabel} đã mất | Thua: x2 {match.UnitShortLabel} đã mất");

        await _bot.EditMessageText(message.Chat.Id, message.Id, text,
            replyMarkup: StarKeyboard(), cancellationToken: ct);
    }

    private async Task EditMatchAnalysisAsync(Message message, Guid matchId, CancellationToken ct)
    {
        await _bot.EditMessageText(
            message.Chat.Id,
            message.Id,
            "🧠 AI đang đọc dữ liệu trận đấu...",
            replyMarkup: new InlineKeyboardMarkup(
                [
                    [InlineKeyboardButton.WithCallbackData("↩️ Quay lại trận", $"m:{matchId}")],
                    [InlineKeyboardButton.WithCallbackData("🔄 Trận khác", "cmd:datkeo")]
                ]),
            cancellationToken: ct);

        var result = await _analysis.AnalyzeMatchAsync(matchId, force: false, ct);
        if (!result.Succeeded || result.Value is null)
        {
            await _bot.EditMessageText(
                message.Chat.Id,
                message.Id,
                result.Error ?? "Không lấy được phân tích AI lúc này.",
                replyMarkup: new InlineKeyboardMarkup(
                    [
                        [InlineKeyboardButton.WithCallbackData("↩️ Quay lại trận", $"m:{matchId}")],
                        [InlineKeyboardButton.WithCallbackData("🔄 Trận khác", "cmd:datkeo")]
                    ]),
                cancellationToken: ct);
            return;
        }

        await _bot.EditMessageText(
            message.Chat.Id,
            message.Id,
            TelegramBotText.BuildAnalysisText(result.Value),
            replyMarkup: new InlineKeyboardMarkup(
                [
                    [InlineKeyboardButton.WithCallbackData("↩️ Quay lại trận", $"m:{matchId}")],
                    [InlineKeyboardButton.WithCallbackData("🔄 Trận khác", "cmd:datkeo")]
                ]),
            cancellationToken: ct);
    }

    private async Task EditCupChoicesAsync(Message message, Guid matchId, string choice, CancellationToken ct)
    {
        var match = await _keoBia.GetPublicMatchAsync(matchId, ct);
        if (match is null || !IsOpenForVoting(match))
        {
            await _bot.EditMessageText(
                message.Chat.Id,
                message.Id,
                "Trận này chưa mở kèo hoặc đã khóa kèo.",
                replyMarkup: MainMenuKeyboard(),
                cancellationToken: ct);
            return;
        }

        var keyboard = new InlineKeyboardMarkup(
            [
                [
                    InlineKeyboardButton.WithCallbackData($"{KeoBiaUnitRules.Emoji(match.UnitCode)} 1 {match.UnitShortLabel}", $"b:{matchId}:{choice}:1"),
                    InlineKeyboardButton.WithCallbackData($"{KeoBiaUnitRules.Emoji(match.UnitCode)} 3 {match.UnitShortLabel}", $"b:{matchId}:{choice}:3"),
                    InlineKeyboardButton.WithCallbackData($"{KeoBiaUnitRules.Emoji(match.UnitCode)} 5 {match.UnitShortLabel}", $"b:{matchId}:{choice}:5")
                ],
                [InlineKeyboardButton.WithCallbackData("↩️ Đổi cửa", $"m:{matchId}")]
            ]);

        await _bot.EditMessageText(
            message.Chat.Id,
            message.Id,
            TelegramBotText.BuildCupChoiceText(match, choice),
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task AskCorrectScoreAsync(CallbackQuery callback, Guid matchId, string choice, int cups, string? starType, CancellationToken ct)
    {
        var match = await _keoBia.GetPublicMatchAsync(matchId, ct);
        if (match is null) return;
        var keyboard = BuildCorrectScoreKeyboard(match, matchId, choice, starType);
        await AnswerCallbackAsync(callback, null, ct);
        if (callback.Message is null) return;
        await _bot.EditMessageText(
            callback.Message.Chat.Id,
            callback.Message.Id,
            await BuildCorrectScorePromptAsync(match, choice, ct),
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task StartManualScorePromptAsync(CallbackQuery callback, Guid matchId, string choice, string? starType, CancellationToken ct)
    {
        PendingScorePrompts[callback.From.Id] = new PendingScorePrompt(matchId, choice, starType, 1, null);
        await AnswerCallbackAsync(callback, null, ct);
        if (callback.Message is null) return;

        var suffix = starType is null ? "" : $":{starType}";
        await _bot.EditMessageText(
            callback.Message.Chat.Id,
            callback.Message.Id,
            "Nhập số bàn đội nhà trước (0-20).",
            replyMarkup: new InlineKeyboardMarkup([[InlineKeyboardButton.WithCallbackData("Bỏ qua tỉ số", $"skipscore:{matchId}:{choice}{suffix}")]]),
            cancellationToken: ct);
    }

    private static InlineKeyboardMarkup BuildCorrectScoreKeyboard(KeoBiaPublicMatchDto match, Guid matchId, string choice, string? starType)
    {
        var suffix = starType is null ? "" : $":{starType}";
        var rows = BuildTopCorrectScoreOdds(match.CorrectScoreOddsJson, choice)
            .Select(x => InlineKeyboardButton.WithCallbackData(x.Score, $"score:{matchId}:{choice}:{x.Score}{suffix}"))
            .Chunk(2)
            .Select(x => x.ToArray())
            .ToList();
        if (rows.Count == 0)
        {
            rows = GetDefaultScoreSuggestions(choice)
                .Select(x => InlineKeyboardButton.WithCallbackData(x, $"score:{matchId}:{choice}:{x}{suffix}"))
                .Chunk(2)
                .Select(x => x.ToArray())
                .ToList();
        }

        rows.Add([InlineKeyboardButton.WithCallbackData("Khác", $"score:{matchId}:{choice}:other{suffix}")]);
        rows.Add([InlineKeyboardButton.WithCallbackData("Bỏ qua tỉ số", $"skipscore:{matchId}:{choice}{suffix}")]);
        return new InlineKeyboardMarkup(rows);
    }

    private async Task<string> BuildCorrectScorePromptAsync(KeoBiaPublicMatchDto match, string choice, CancellationToken ct)
    {
        var sb = new StringBuilder()
            .AppendLine("Bạn có muốn dự đoán tỉ số chính xác không?")
            .AppendLine()
            .AppendLine($"Luật: chỉ tính tỉ số 90 phút. Đoán sai mất 1 {match.UnitLabel}, đoán đúng được giảm 3 {match.UnitLabel}.");

        var odds = BuildCompactCorrectScoreOdds(match.CorrectScoreOddsJson, choice);
        if (!string.IsNullOrWhiteSpace(odds))
        {
            sb.AppendLine()
                .AppendLine("Tỉ số gợi ý:")
                .AppendLine(odds);
        }

        var picks = await BuildScorePicksTextAsync(match.Id, ct);
        if (!string.IsNullOrWhiteSpace(picks))
        {
            sb.AppendLine()
                .AppendLine("Người đã chọn tỉ số:")
                .AppendLine(picks);
        }

        return sb.AppendLine()
            .Append("Nếu có, hãy nhập số bàn đội nhà trước (ví dụ: 2).")
            .ToString();
    }

    private async Task<string> BuildScorePicksTextAsync(Guid matchId, CancellationToken ct)
    {
        var history = await _keoBia.GetMatchPredictionHistoryAsync(matchId, null, 2000, ct);
        if (!history.Succeeded || history.Value is null) return string.Empty;

        var rows = history.Value.Items
            .Where(x => x.PredictedHomeScore.HasValue && x.PredictedAwayScore.HasValue)
            .Select(x => $"- {x.PlayerName}: {x.PredictedHomeScore}-{x.PredictedAwayScore}")
            .ToList();

        return rows.Count == 0 ? "- Chưa ai chọn tỉ số." : string.Join("\n", rows);
    }

    private static string BuildCompactCorrectScoreOdds(string? oddsJson, string choice)
    {
        if (string.IsNullOrWhiteSpace(oddsJson))
            return string.Join(" · ", GetDefaultScoreSuggestions(choice));
        try
        {
            using var doc = JsonDocument.Parse(oddsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return string.Join(" · ", GetDefaultScoreSuggestions(choice));

            var scores = BuildTopCorrectScoreOdds(oddsJson, choice)
                .Select(x => x.Score)
                .ToArray();
            if (scores.Length == 0)
                scores = GetDefaultScoreSuggestions(choice);
            return string.Join(" · ", scores);
        }
        catch
        {
            return string.Join(" · ", GetDefaultScoreSuggestions(choice));
        }
    }

    private static IReadOnlyList<(string Score, decimal Odds)> BuildTopCorrectScoreOdds(string? oddsJson, string choice)
    {
        if (string.IsNullOrWhiteSpace(oddsJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(oddsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];

            return doc.RootElement.EnumerateObject()
                .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.Name, @"^\d{1,2}-\d{1,2}$") && x.Value.TryGetDecimal(out _))
                .Select(x => (Score: x.Name, Odds: x.Value.GetDecimal()))
                .Where(x => IsScoreConsistentWithChoice(x.Score, choice))
                .OrderBy(x => x.Odds)
                .ThenBy(x => x.Score)
                .Take(8)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsScoreConsistentWithChoice(string score, string choice)
    {
        var parts = score.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var home) || !int.TryParse(parts[1], out var away))
            return false;
        return choice switch
        {
            HomeChoice => home > away,
            DrawChoice => home == away,
            AwayChoice => home < away,
            _ => false
        };
    }

    private static string GetScoreChoiceWarning(string choice) => choice switch
    {
        HomeChoice => "Bạn đã chọn đội nhà thắng, tỉ số dự đoán phải là đội nhà thắng.",
        DrawChoice => "Bạn đã chọn hòa, tỉ số dự đoán phải là hòa.",
        AwayChoice => "Bạn đã chọn đội khách thắng, tỉ số dự đoán phải là đội khách thắng.",
        _ => "Tỉ số dự đoán phải khớp cửa đã chọn."
    };

    private static string[] GetDefaultScoreSuggestions(string choice) => choice switch
    {
        HomeChoice => ["1-0", "2-0", "2-1", "3-1", "3-2", "4-1"],
        DrawChoice => ["0-0", "1-1", "2-2", "3-3"],
        AwayChoice => ["0-1", "0-2", "1-2", "1-3", "2-3", "1-4"],
        _ => []
    };

    private async Task<bool> TryHandlePendingScorePromptAsync(Message message, CancellationToken ct)
    {
        if (message.From is null || !PendingScorePrompts.TryGetValue(message.From.Id, out var pending))
            return false;
        if (!int.TryParse((message.Text ?? string.Empty).Trim(), out var score) || score < 0 || score > 20)
        {
            await _bot.SendMessage(message.Chat.Id, "Nhập tỉ số từ 0 đến 20, hoặc bấm Bỏ qua tỉ số trên tin nhắn trước.", cancellationToken: ct);
            return true;
        }

        if (!pending.HomeScore.HasValue)
        {
            PendingScorePrompts[message.From.Id] = pending with { HomeScore = score };
            await _bot.SendMessage(message.Chat.Id, "Nhập số bàn đội khách (0-20).", cancellationToken: ct);
            return true;
        }

        if (!IsScoreConsistentWithChoice($"{pending.HomeScore.Value}-{score}", pending.Choice))
        {
            PendingScorePrompts[message.From.Id] = pending with { HomeScore = null };
            await _bot.SendMessage(message.Chat.Id, GetScoreChoiceWarning(pending.Choice) + " Nhập lại số bàn đội nhà (0-20).", cancellationToken: ct);
            return true;
        }

        PendingScorePrompts.TryRemove(message.From.Id, out _);
        var result = await _keoBia.SubmitBetByTelegramIdAsync(message.From.Id, pending.MatchId, pending.Choice, pending.Cups, pending.StarType, pending.HomeScore.Value, score, null, ct);
        if (!result.Succeeded || result.Value is null)
        {
            await _bot.SendMessage(message.Chat.Id, result.Error ?? "Không đặt được kèo.", cancellationToken: ct);
            return true;
        }

        var payload = result.Value;
        await NotifyPlayerJoinedAsync(payload.Player, ct);
        await NotifyActivityAsync(payload.Activity, ct);
        await _bot.SendMessage(message.Chat.Id, $"✅ Đã đặt kèo và tỉ số 90p {pending.HomeScore}-{score}.", replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
        return true;
    }

    private async Task SubmitBetAsync(CallbackQuery callback, Guid matchId, string choice, int cups, string? starType, int? predictedHomeScore, int? predictedAwayScore, CancellationToken ct)
    {
        PendingScorePrompts.TryRemove(callback.From.Id, out _);
        var result = await _keoBia.SubmitBetByTelegramIdAsync(callback.From.Id, matchId, choice, cups, starType, predictedHomeScore, predictedAwayScore, null, ct);
        if (!result.Succeeded || result.Value is null)
        {
            await AnswerCallbackAsync(callback, result.Error ?? "Không đặt được kèo.", ct);
            return;
        }

        var payload = result.Value;
        await NotifyPlayerJoinedAsync(payload.Player, ct);
        await NotifyActivityAsync(payload.Activity, ct);

        var starSuffix = starType == "hope" ? " ⭐ Hi vọng" : starType == "devil" ? " 😈 Ma quỷ" : "";
        await AnswerCallbackAsync(callback, $"✅ Đã đặt kèo{starSuffix}.", ct);

        if (callback.Message is null)
            return;

        var match = await _keoBia.GetPublicMatchAsync(matchId, ct);
        var unitCode = match?.UnitCode ?? KeoBiaUnitRules.GetCurrentUnitCode();
        var text = match is null
            ? $"✅ Đã đặt {KeoBiaUnitRules.FormatCount(cups, unitCode)} cho {payload.Activity.ChoiceLabel}."
            : $"✅ Đã đặt {KeoBiaUnitRules.FormatCount(cups, unitCode)} cho {ChoiceLabel(match, choice)}.\n\n{BuildMatchDetailText(match)}";

        await _bot.EditMessageText(
            callback.Message.Chat.Id,
            callback.Message.Id,
            text,
            replyMarkup: MainMenuKeyboard(),
            cancellationToken: ct);
    }

    private async Task SendMyBetsAsync(long chatId, long telegramUserId, CancellationToken ct)
    {
        var text = await BuildMyBetsTextAsync(telegramUserId, ct);
        await _bot.SendMessage(chatId, text, replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
    }

    private async Task EditMyBetsAsync(Message message, long telegramUserId, CancellationToken ct)
    {
        var text = await BuildMyBetsTextAsync(telegramUserId, ct);
        await EditTextOrSendAsync(message, text, MainMenuKeyboard(), ct);
    }

    private async Task<string> BuildMyBetsTextAsync(long telegramUserId, CancellationToken ct)
    {
        var historyResult = await _keoBia.GetPlayerHistoryByTelegramIdAsync(telegramUserId, 10, ct);
        var predictions = await _keoBia.GetPlayerMatchPredictionsByTelegramIdAsync(telegramUserId, ct);
        if (!historyResult.Succeeded || historyResult.Value is null)
            return historyResult.Error ?? "Không tìm thấy kèo của bạn.";

        var history = historyResult.Value;
        var sb = new StringBuilder()
            .AppendLine("🎟️ Kèo của tôi")
            .AppendLine($"👤 {history.DisplayName}")
            .AppendLine($"📊 Tổng: {history.TotalBets} kèo")
            .AppendLine($"🍺🥜 Đã dự đoán: {KeoBiaUnitRules.FormatBreakdown(history.TotalBeerCups, history.TotalPeanutPacks)}")
            .AppendLine($"✅/❌ Đúng/Sai: {history.CorrectBets}/{history.WrongBets}")
            .AppendLine($"⏳ Đang chờ: {KeoBiaUnitRules.FormatBreakdown(history.PendingBeerCups, history.PendingPeanutPacks)}")
            .AppendLine($"🧾 Còn thiếu: {KeoBiaUnitRules.FormatBreakdown(history.OutstandingBeerCups, history.OutstandingPeanutPacks)}");

        if (predictions.Count > 0)
            sb.AppendLine($"🎯 Cửa đang giữ: {predictions.Count}");

        if (history.PaidBeerCups > 0)
            sb.AppendLine($"✅ Đã rút: {KeoBiaUnitRules.FormatBreakdown(history.PaidLegacyBeerCups, history.PaidPeanutPacks)}");

        if (history.Items.Count == 0)
            return sb.AppendLine().Append("Bạn chưa đặt kèo nào.").ToString();

        sb.AppendLine().AppendLine("🧾 Gần đây:");
        foreach (var item in history.Items.Take(10))
            sb.AppendLine(TelegramBotText.BuildHistoryLine(item));

        return sb.ToString();
    }

    private async Task SendQuizResultsAsync(long chatId, CancellationToken ct)
    {
        var text = await BuildQuizResultsTextAsync(ct);
        await _bot.SendMessage(chatId, text, replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
    }

    private async Task SendBeerPaymentPromptAsync(long chatId, long telegramUserId, CancellationToken ct)
    {
        var (text, keyboard) = await BuildBeerPaymentPromptAsync(telegramUserId, ct);
        await _bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
    }

    private async Task EditBeerPaymentPromptAsync(Message message, long telegramUserId, CancellationToken ct)
    {
        var (text, keyboard) = await BuildBeerPaymentPromptAsync(telegramUserId, ct);
        await EditTextOrSendAsync(message, text, keyboard, ct);
    }

    private async Task<(string Text, InlineKeyboardMarkup Keyboard)> BuildBeerPaymentPromptAsync(long telegramUserId, CancellationToken ct)
    {
        var prompt = await _keoBia.GetBeerPaymentPromptAsync(telegramUserId, ct);
        if (!prompt.Succeeded || prompt.Value is null)
            return (prompt.Error ?? "Không lấy được thông tin cần nộp.", MainMenuKeyboard());

        if (prompt.Value.OutstandingCups <= 0)
            return ($"✅ Phần cần nộp\n{prompt.Value.DisplayName} không còn bia hoặc gói lạc nào phải nộp.", MainMenuKeyboard());

        PendingBeerPrompts[telegramUserId] = prompt.Value.OutstandingCups;
        var onlyUnitCode = prompt.Value.OutstandingBeerCups > 0 && prompt.Value.OutstandingPeanutPacks == 0
            ? KeoBiaUnitCode.Beer
            : prompt.Value.OutstandingPeanutPacks > 0 && prompt.Value.OutstandingBeerCups == 0
                ? KeoBiaUnitCode.Peanut
                : null;
        var buttonUnit = onlyUnitCode is null ? "phần" : KeoBiaUnitRules.ShortLabel(onlyUnitCode);
        var rows = prompt.Value.SuggestedCups.Where(x => x < prompt.Value.OutstandingCups)
            .Select(x => new[] { InlineKeyboardButton.WithCallbackData($"{x} {buttonUnit}", $"pay:{x}") })
            .Append([InlineKeyboardButton.WithCallbackData("🍻 Nộp hết", $"pay:{prompt.Value.OutstandingCups}")])
            .Append([InlineKeyboardButton.WithCallbackData("⬅️ Menu", "cmd:start")])
            .ToArray();

        var breakdown = KeoBiaUnitRules.FormatBreakdown(prompt.Value.OutstandingBeerCups, prompt.Value.OutstandingPeanutPacks);
        var oldestFirst = prompt.Value.OutstandingBeerCups > 0 && prompt.Value.OutstandingPeanutPacks > 0
            ? "\nHệ thống ưu tiên phần bia cũ trước."
            : string.Empty;
        return ($"🧾 Phần cần nộp\n{prompt.Value.DisplayName} đang còn {breakdown}.{oldestFirst}\nChọn số lượng hoặc nhắn số nguyên từ 1 đến {prompt.Value.OutstandingCups}.", new InlineKeyboardMarkup(rows));
    }

    private async Task CreateAndSendBeerPaymentAsync(long chatId, long telegramUserId, int cups, CancellationToken ct)
    {
        var prompt = await _keoBia.GetBeerPaymentPromptAsync(telegramUserId, ct);
        if (!prompt.Succeeded || prompt.Value is null || cups <= 0 || cups > prompt.Value.OutstandingCups)
        {
            await _bot.SendMessage(chatId, $"Số lượng không hợp lệ. Chỉ nhập 1 đến {Math.Max(0, prompt.Value?.OutstandingCups ?? 0)}.", replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
            return;
        }

        var result = await _keoBia.CreateBeerPaymentAsync(telegramUserId, cups, ct);
        if (!result.Succeeded || result.Value is null)
        {
            await _bot.SendMessage(chatId, result.Error ?? "Không tạo được giao dịch nộp.", replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
            return;
        }

        PendingBeerPrompts.TryRemove(telegramUserId, out _);
        var p = result.Value;
        var caption = $"🧾 Nộp {KeoBiaUnitRules.FormatBreakdown(p.BeerCups, p.PeanutPacks)}\nNội dung: {p.TransferContent}";
        await _bot.SendPhoto(chatId, p.QrUrl, caption: caption, replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
    }

    private async Task SendBeerPaymentHistoryAsync(long chatId, CancellationToken ct)
    {
        await _bot.SendMessage(chatId, await BuildBeerPaymentHistoryTextAsync(ct), replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
    }

    private async Task EditBeerPaymentHistoryAsync(Message message, CancellationToken ct)
    {
        await EditTextOrSendAsync(message, await BuildBeerPaymentHistoryTextAsync(ct), MainMenuKeyboard(), ct);
    }

    private async Task<string> BuildBeerPaymentHistoryTextAsync(CancellationToken ct)
    {
        var rows = await _keoBia.GetPublicBeerPaymentHistoryAsync(0, ct);
        if (rows.Count == 0) return "🧾 Chưa có lịch sử nộp.";
        var totalCoin = await _keoBia.GetBeerPaymentFundTotalAsync(ct);
        var grouped = rows
            .GroupBy(x => new { x.TelegramUserId, x.DisplayName })
            .Select(g => new
            {
                g.Key.DisplayName,
                BeerCups = g.Sum(x => x.BeerCups),
                PeanutPacks = g.Sum(x => x.PeanutPacks),
                LastPaidAt = g.Max(x => x.PaidAt ?? x.CreatedAt)
            })
            .OrderByDescending(x => x.LastPaidAt)
            .ToList();

        var sb = new StringBuilder()
            .AppendLine("🧾 Lịch sử nộp")
            .AppendLine($"🪙 Tổng coin hiện có: {totalCoin:N0}")
            .AppendLine();
        foreach (var player in grouped)
            sb.AppendLine($"• {player.DisplayName} · {KeoBiaUnitRules.FormatBreakdown(player.BeerCups, player.PeanutPacks)}");
        return sb.ToString().TrimEnd();
    }

    private string? ReadPaymentWebhookSecret()
    {
        var auth = Request.Headers["Authorization"].ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return auth[7..].Trim();
        if (auth.StartsWith("Apikey ", StringComparison.OrdinalIgnoreCase)) return auth[7..].Trim();
        if (Request.Headers.TryGetValue("X-Sepay-Signature", out var sepay)) return sepay.ToString();
        if (Request.Headers.TryGetValue("X-Webhook-Secret", out var secret)) return secret.ToString();
        return null;
    }

    private async Task NotifyBeerPaymentPaidAsync(KeoBiaBeerPaymentDto payment, CancellationToken ct)
    {
        var name = payment.TelegramUsername ?? payment.DisplayName;
        var text = $"✅ Cảm ơn {name} đã nộp {KeoBiaUnitRules.FormatBreakdown(payment.BeerCups, payment.PeanutPacks)}!";
        if (payment.TelegramUserId.HasValue)
        {
            try { await _bot.SendMessage(payment.TelegramUserId.Value, text, cancellationToken: ct); }
            catch { }
        }
        var group = _configuration["KeoBia:Telegram:ShareBeer:GroupChatId"] ?? _configuration["KeoBia:Telegram:GroupChatId"];
        if (long.TryParse(group, out var groupId))
        {
            try { await _bot.SendMessage(groupId, text, cancellationToken: ct); }
            catch { }
        }
    }

    private async Task EditQuizResultsAsync(Message message, CancellationToken ct)
    {
        var text = await BuildQuizResultsTextAsync(ct);
        await EditTextOrSendAsync(message, text, MainMenuKeyboard(), ct);
    }

    private async Task<string> BuildQuizResultsTextAsync(CancellationToken ct)
    {
        var questions = (await _keoBia.GetQuestionsAsync(ct)).Take(5).ToList();
        if (questions.Count == 0) return "📊 Chưa có câu hỏi bình chọn nào.";

        var sb = new StringBuilder().AppendLine("📊 Kết quả bình chọn");
        foreach (var q in questions)
        {
            var votes = await _keoBia.GetQuestionVotesAsync(q.Id, ct);
            if (!votes.Succeeded || votes.Value is null) continue;

            sb.AppendLine().AppendLine($"❓ {q.Text}");
            sb.AppendLine($"🎁 Đúng giảm {KeoBiaUnitRules.FormatCount(q.RewardCups, q.UnitCode)} · Sai {(q.PenaltyCups > 0 ? $"mất {KeoBiaUnitRules.FormatCount(q.PenaltyCups, q.UnitCode)}" : "không mất")} · {q.TotalVotes} vote · {q.Status}");
            foreach (var c in votes.Value.Choices)
            {
                var names = votes.Value.Voters
                    .Where(v => string.Equals(v.ChoiceKey, c.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.DisplayName)
                    .Take(8)
                    .ToList();
                var mark = string.Equals(q.CorrectChoiceKey, c.Key, StringComparison.OrdinalIgnoreCase) ? " ✅" : "";
                sb.AppendLine($"• {c.Label}{mark}: {c.VoteCount}" + (names.Count > 0 ? $" ({string.Join(", ", names)})" : ""));
            }
        }
        return sb.ToString().TrimEnd();
    }

    private async Task SendScheduleAsync(long chatId, CancellationToken ct)
    {
        var text = await BuildScheduleTextAsync(ct);
        await _bot.SendMessage(chatId, text, replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
    }

    private async Task EditScheduleAsync(Message message, CancellationToken ct)
    {
        var text = await BuildScheduleTextAsync(ct);
        await EditTextOrSendAsync(message, text, MainMenuKeyboard(), ct);
    }

    private async Task<string> BuildScheduleTextAsync(CancellationToken ct)
    {
        var data = await _keoBia.GetPublicHomeAsync(3, ct);
        var now = DateTime.UtcNow;
        var upcoming = data.ScheduleMatches
            .Where(x => (x.Status == ScheduledStatus || x.Status == LiveStatus) && x.KickoffAt >= now.AddHours(-2))
            .OrderBy(x => x.KickoffAt)
            .Take(8)
            .ToList();
        var finished = data.ScheduleMatches
            .Where(x => x.Status == FinishedStatus)
            .OrderByDescending(x => x.KickoffAt)
            .Take(5)
            .ToList();

        var sb = new StringBuilder().AppendLine("📅 Lịch và kết quả");
        if (upcoming.Count > 0)
        {
            sb.AppendLine().AppendLine("⏱️ Sắp tới:");
            foreach (var match in upcoming)
                sb.AppendLine(TelegramBotText.ScheduleLine(match));
        }

        if (finished.Count > 0)
        {
            sb.AppendLine().AppendLine("🏁 Kết quả:");
            foreach (var match in finished)
                sb.AppendLine(TelegramBotText.ResultLine(match));
        }

        if (upcoming.Count == 0 && finished.Count == 0)
            sb.AppendLine().Append("Chưa có lịch trận.");

        return sb.ToString();
    }

    private async Task SendLeaderboardAsync(long chatId, CancellationToken ct)
    {
        var text = await BuildLeaderboardTextAsync(ct);
        await _bot.SendMessage(chatId, text, replyMarkup: MainMenuKeyboard(), cancellationToken: ct);
    }

    private async Task EditLeaderboardAsync(Message message, CancellationToken ct)
    {
        var text = await BuildLeaderboardTextAsync(ct);
        await EditTextOrSendAsync(message, text, MainMenuKeyboard(), ct);
    }

    private async Task<string> BuildLeaderboardTextAsync(CancellationToken ct)
    {
        var allPlayers = await _keoBia.GetLossLeaderboardAsync(1000, ct);
        var totalBeerCups = allPlayers.Sum(x => x.HistoricalLostBeerCups);
        var totalPeanutPacks = allPlayers.Sum(x => x.HistoricalLostPeanutPacks);
        var topPlayers = allPlayers
            .Where(x => x.HistoricalLostCups > 0)
            .Take(10)
            .ToList();

        var text = new StringBuilder()
            .AppendLine("🏆 Tổng bia và lạc đã mất")
            .AppendLine(KeoBiaUnitRules.FormatBreakdown(totalBeerCups, totalPeanutPacks));

        if (topPlayers.Count == 0)
            return text.AppendLine().Append("Chưa có người chơi nào mất bia hoặc lạc.").ToString();

        text.AppendLine()
            .AppendLine("Top 10 người chơi mất nhiều nhất:");

        for (var index = 0; index < topPlayers.Count; index++)
        {
            var player = topPlayers[index];
            text.AppendLine($"{index + 1}. {player.DisplayName} - {KeoBiaUnitRules.FormatBreakdown(player.HistoricalLostBeerCups, player.HistoricalLostPeanutPacks)}");
        }

        return text.ToString().TrimEnd();
    }

    private async Task<Result<KeoBiaPlayerPresenceDto>> EnsurePlayerAsync(User user, CancellationToken ct)
    {
        return await _keoBia.EnsureTelegramPlayerAsync(new KeoBiaTelegramBotIdentityDto(
            user.Id,
            user.Username,
            user.FirstName,
            user.LastName,
            null), ct);
    }

    private async Task NotifyPlayerJoinedAsync(KeoBiaPlayerPresenceDto player, CancellationToken ct)
    {
        if (!player.IsNewPlayer)
            return;

        try
        {
            await _hubContext.Clients.All.SendAsync(KeoBiaRealtimeHub.PlayerJoinedMethod, new
            {
                playerId = player.PlayerId,
                publicKey = player.PublicKey,
                displayName = player.DisplayName,
                avatarUrl = player.AvatarUrl
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast join toast for Telegram player {PlayerId}", player.PlayerId);
        }
    }

    private async Task NotifyActivityAsync(KeoBiaActivityFeedDto activity, CancellationToken ct)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(KeoBiaRealtimeHub.ActivityAddedMethod, ToActivityPayload(activity), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast Telegram KeoBia activity {ActivityId}", activity.Id);
        }
    }

    private bool HasValidSecretToken()
    {
        if (!_telegram.IsWebhookConfigured)
            return false;

        var expected = _telegram.Webhook.Secret?.Trim();
        if (string.IsNullOrWhiteSpace(expected))
            return false;
        if (!Request.Headers.TryGetValue(SecretTokenHeader, out var actualValues))
            return false;

        var actual = actualValues.ToString();
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static InlineKeyboardMarkup MainMenuKeyboard() =>
        TelegramBotText.MainMenuKeyboard();

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

    private static string BuildMatchDetailText(KeoBiaPublicMatchDto match) =>
        TelegramBotText.BuildMatchDetailText(match);

    private static string ChoiceLabel(KeoBiaPublicMatchDto match, string choice) =>
        TelegramBotText.ChoiceLabel(match, choice);

    private static string HistoryStatus(KeoBiaPlayerPredictionHistoryItemDto item) =>
        TelegramBotText.HistoryStatus(item);

    private static bool IsOpenForVoting(KeoBiaMatchListItemDto match) =>
        TelegramBotText.IsOpenForVoting(match);

    private static bool IsOpenForVoting(KeoBiaPublicMatchDto match) =>
        TelegramBotText.IsOpenForVoting(match);

    private static bool IsChoice(string choice) =>
        choice is HomeChoice or DrawChoice or AwayChoice;

    private static string FormatVietnamTime(DateTime utc) =>
        TelegramBotText.FormatVietnamTime(utc);

    private static string NormalizeCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var command = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var botSuffixIndex = command.IndexOf('@', StringComparison.Ordinal);
        if (botSuffixIndex >= 0)
            command = command[..botSuffixIndex];

        return command.ToLowerInvariant();
    }

    private async Task AnswerCallbackAsync(CallbackQuery callback, string? text, CancellationToken ct)
    {
        await _bot.AnswerCallbackQuery(callback.Id, text: text, cancellationToken: ct);
    }
}
