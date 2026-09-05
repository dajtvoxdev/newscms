using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using NewsCMS.Shared.Constants;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace NewsCMS.Web.Areas.Admin.Pages.KeoBia.Chat;

[Authorize(Permissions.KeoBia.SendTelegram)]
public class IndexModel : PageModel
{
    private readonly ITelegramBotClient _bot;
    private readonly IConfiguration _config;
    private static readonly Regex ImgTagRe = new(@"<img[^>]+src\s*=\s*[""']([^""']+)[""'][^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IndexModel(ITelegramBotClient bot, IConfiguration config)
    {
        _bot = bot;
        _config = config;
    }

    [BindProperty] public string? MessageHtml { get; set; }
    [BindProperty] public string? ImageUrl { get; set; }

    public string? GroupChatId { get; private set; }
    public bool IsConfigured { get; private set; }

    public void OnGet()
    {
        GroupChatId = _config["KeoBia:Telegram:ShareBeer:GroupChatId"];
        IsConfigured = !string.IsNullOrWhiteSpace(GroupChatId);
    }

    public async Task<IActionResult> OnPostSendAsync(CancellationToken ct)
    {
        GroupChatId = _config["KeoBia:Telegram:ShareBeer:GroupChatId"];
        IsConfigured = !string.IsNullOrWhiteSpace(GroupChatId);

        if (!IsConfigured || !long.TryParse(GroupChatId, out var chatId))
        {
            TempData["Error"] = "GroupChatId chưa cấu hình hoặc không hợp lệ.";
            return Page();
        }

        var html = (MessageHtml ?? string.Empty).Trim();
        var imageUrl = (ImageUrl ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(html) && string.IsNullOrWhiteSpace(imageUrl))
        {
            TempData["Error"] = "Vui lòng nhập nội dung hoặc chọn ảnh.";
            return Page();
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                if (!imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    imageUrl = $"{Request.Scheme}://{Request.Host}{imageUrl}";

                var caption = string.IsNullOrWhiteSpace(html) ? null : ConvertToTelegramHtml(html);
                await _bot.SendPhoto(
                    chatId,
                    imageUrl,
                    caption: caption,
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
                TempData["Success"] = "Đã gửi ảnh + caption tới nhóm Telegram.";
            }
            else
            {
                var telegramHtml = ConvertToTelegramHtml(html);
                await _bot.SendMessage(
                    chatId,
                    telegramHtml,
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
                TempData["Success"] = "Đã gửi tin nhắn tới nhóm Telegram.";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Gửi thất bại: {ex.Message}";
        }

        return RedirectToPage();
    }

    private static string ConvertToTelegramHtml(string html)
    {
        var s = html;

        s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</p>", "\n\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</div>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</li>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<hr\s*/?>", "\n———\n", RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"<h[1-6][^>]*>", "\n<b>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</h[1-6]>", "</b>\n", RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"<blockquote[^>]*>", "\n<blockquote>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</blockquote>", "</blockquote>\n", RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"<strong>", "<b>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</strong>", "</b>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<em>", "<i>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</em>", "</i>", RegexOptions.IgnoreCase);

        s = ImgTagRe.Replace(s, "");

        s = Regex.Replace(s, @"<(?!/?(b|i|u|s|code|pre|a|blockquote|tg-spoiler)\b)[^>]+>", "", RegexOptions.IgnoreCase);

        s = WebUtility.HtmlDecode(s);

        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        s = s.Trim();

        if (s.Length > 4096)
            s = s[..4093] + "...";

        return s;
    }
}
