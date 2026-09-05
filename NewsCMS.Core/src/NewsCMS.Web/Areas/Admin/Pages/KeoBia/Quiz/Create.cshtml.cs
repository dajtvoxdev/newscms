using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using NewsCMS.Application.KeoBia;
using NewsCMS.Shared.Constants;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace NewsCMS.Web.Areas.Admin.Pages.KeoBia.Quiz;

[Authorize(Permissions.KeoBia.ManageQuiz)]
public class CreateModel : PageModel
{
    // Vietnam has no DST, so a fixed +7 offset converts admin local time to UTC.
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);

    private readonly IKeoBiaService _svc;
    private readonly ITelegramBotClient _bot;
    private readonly IConfiguration _config;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(IKeoBiaService svc, ITelegramBotClient bot, IConfiguration config, ILogger<CreateModel> logger)
    {
        _svc = svc;
        _bot = bot;
        _config = config;
        _logger = logger;
    }

    [BindProperty] public string Text { get; set; } = "";
    [BindProperty] public int RewardCups { get; set; } = 1;
    [BindProperty] public int PenaltyCups { get; set; }
    [BindProperty] public DateTime? ClosesAt { get; set; }
    [BindProperty] public List<string> ChoiceLabels { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var labels = (ChoiceLabels ?? new List<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToList();
        if (labels.Count < 2)
        {
            TempData["Error"] = "Cần ít nhất 2 lựa chọn.";
            return Page();
        }

        // Keys are colon-free so Telegram callback "q:{id}:{key}" stays unambiguous.
        var choices = labels
            .Select((label, i) => new KeoBiaQuestionChoiceDto($"c{i + 1}", label))
            .ToList();

        // datetime-local is admin local wall-clock; store as UTC.
        DateTime? closesAtUtc = ClosesAt.HasValue
            ? ClosesAt.Value - VietnamUtcOffset
            : null;

        var result = await _svc.CreateQuestionAsync(
            new KeoBiaCreateQuestionDto(Text, choices, RewardCups, PenaltyCups, closesAtUtc), ct);
        if (!result.Succeeded || result.Value is null)
        {
            TempData["Error"] = result.Error ?? "Không tạo được câu hỏi.";
            return Page();
        }

        var sentToTelegram = await SendToTelegramAsync(result.Value, ct);
        TempData["Success"] = sentToTelegram
            ? "Đã tạo câu hỏi và gửi vào nhóm Telegram."
            : "Đã tạo câu hỏi, nhưng chưa gửi được vào nhóm Telegram.";
        return RedirectToPage("Index");
    }

    private async Task<bool> SendToTelegramAsync(KeoBiaQuestionAdminDto q, CancellationToken ct)
    {
        try
        {
            var chatIdStr = _config["KeoBia:Telegram:ShareBeer:GroupChatId"];
            if (string.IsNullOrWhiteSpace(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
                return false;

            var wrongText = q.PenaltyCups > 0
                ? $"Sai mất {KeoBiaUnitRules.FormatCount(q.PenaltyCups, q.UnitCode)}."
                : "Sai không mất gì.";
            var text = $"❓ CÂU HỎI NHANH\n\n{q.Text}\n\n🎁 Đúng giảm {KeoBiaUnitRules.FormatCount(q.RewardCups, q.UnitCode)}. {wrongText}";
            var rows = q.Choices
                .Select(c => new[] { InlineKeyboardButton.WithCallbackData(c.Label, $"q:{q.Id}:{c.Key}") })
                .ToArray();

            var msg = await _bot.SendMessage(chatId, text,
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
            await _svc.SetQuestionTelegramMessageAsync(q.Id, chatIdStr, msg.MessageId, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast quiz question {QuestionId} to Telegram.", q.Id);
            return false;
        }
    }
}
