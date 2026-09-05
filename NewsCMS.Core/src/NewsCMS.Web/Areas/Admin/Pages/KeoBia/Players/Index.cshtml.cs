using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using NewsCMS.Application.Common;
using NewsCMS.Application.KeoBia;
using NewsCMS.Shared.Constants;
using Telegram.Bot;

namespace NewsCMS.Web.Areas.Admin.Pages.KeoBia.Players;

[Authorize(Permissions.KeoBia.ViewPlayers)]
public class IndexModel : PageModel
{
    private readonly IKeoBiaService _svc;
    private readonly ITelegramBotClient _bot;
    private readonly IConfiguration _config;

    public IndexModel(IKeoBiaService svc, ITelegramBotClient bot, IConfiguration config)
    {
        _svc = svc;
        _bot = bot;
        _config = config;
    }

    [BindProperty(SupportsGet = true, Name = "q")] public string? Keyword { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int CurrentPage { get; set; } = 1;

    public PagedList<KeoBiaPlayerListItemDto> Items { get; private set; } = PagedList<KeoBiaPlayerListItemDto>.Empty();
    public PagedList<KeoBiaBetFeedDto> RecentBets { get; private set; } = PagedList<KeoBiaBetFeedDto>.Empty(pageSize: 12);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _svc.SearchPlayersAsync(Keyword, CurrentPage, 20, ct);
        RecentBets = await _svc.SearchBetsAsync(null, null, 1, 12, ct);
    }

    public async Task<IActionResult> OnPostToggleBlockAsync(Guid id, bool block, CancellationToken ct)
    {
        var result = block
            ? await _svc.StopPlayerAsync(id, ct)
            : await _svc.TogglePlayerBlockAsync(id, false, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? (block ? "Đã dừng người chơi và huỷ các kèo chưa diễn ra." : "Đã mở khoá người chơi.")
            : result.Error;
        return RedirectToPage(new { q = Keyword, p = CurrentPage });
    }

    public async Task<IActionResult> OnPostUpdateStarsAsync(Guid id, string action, CancellationToken ct)
    {
        var player = await _svc.GetPlayerAsync(id, ct);
        if (player is null)
        {
            TempData["Error"] = "Không tìm thấy người chơi.";
            return RedirectToPage(new { q = Keyword, p = CurrentPage });
        }

        int? hope = null, devil = null;
        switch (action)
        {
            case "hope-plus": hope = player.HopeStars + 1; break;
            case "hope-minus": hope = Math.Max(0, player.HopeStars - 1); break;
            case "devil-plus": devil = player.DevilStars + 1; break;
            case "devil-minus": devil = Math.Max(0, player.DevilStars - 1); break;
        }

        var isAdd = action.Contains("plus");
        // Giữ emoji trong tên sao vì chuỗi này đi vào tin nhắn Telegram (plain text).
        var starName = action.Contains("hope") ? "⭐ Ngôi Sao Hi Vọng" : "😈 Ngôi Sao Ma Quỷ";

        var result = await _svc.UpdatePlayerStarsAsync(id, hope, devil, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã cập nhật ngôi sao." : result.Error;

        if (result.Succeeded && isAdd && player.TelegramUserId.HasValue)
        {
            var desc = action.Contains("hope")
                ? "Dùng khi đặt kèo: thắng giảm 1 cốc bia hoặc 1 gói lạc theo loại của trận; thua nhân đôi phần phải nộp."
                : "Dùng khi đặt kèo: kèo cân bằng chọn thoải mái, kèo chênh lệch chỉ được chọn Hòa hoặc đội yếu hơn.\nThắng: giảm 1 cốc bia hoặc 1 gói lạc | Thua: tăng 2 cốc bia hoặc 2 gói lạc theo loại của trận.";
            _ = SendTelegramAsync(player.TelegramUserId.Value,
                $"🎁 Nhà cái đã tặng bạn 1 {starName}!\n{desc}", ct);
        }

        return RedirectToPage(new { q = Keyword, p = CurrentPage });
    }

    public async Task<IActionResult> OnPostAddMissedCreditAsync(Guid id, int cups, CancellationToken ct)
    {
        var result = await _svc.AddMissedMatchCreditAsync(id, cups, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã cập nhật lượt miễn phạt do bỏ lỡ trận."
            : result.Error;
        return RedirectToPage(new { q = Keyword, p = CurrentPage });
    }

    public async Task<IActionResult> OnPostAddBalanceCreditAsync(Guid id, int cups, CancellationToken ct)
    {
        var result = await _svc.AddBalanceCreditAsync(id, cups, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Đã giảm {KeoBiaUnitRules.FormatBreakdown(result.Value!.BeerCups, result.Value.PeanutPacks)}."
            : result.Error;
        return RedirectToPage(new { q = Keyword, p = CurrentPage });
    }

    public async Task<IActionResult> OnPostBulkAddStarAsync(string starType, CancellationToken ct)
    {
        var result = await _svc.BulkAddStarAsync(starType, 1, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Đã thêm 1 {(starType == "hope" ? "Ngôi Sao Hi Vọng" : "Ngôi Sao Ma Quỷ")} cho tất cả."
            : result.Error;

        if (result.Succeeded)
        {
            // Giữ emoji vì chuỗi này đi vào tin nhắn Telegram (plain text).
            var starName = starType == "hope" ? "⭐ Ngôi Sao Hi Vọng" : "😈 Ngôi Sao Ma Quỷ";
            var desc = starType == "hope"
                ? "Dùng khi đặt kèo: thắng giảm 1 cốc bia hoặc 1 gói lạc theo loại của trận; thua nhân đôi phần phải nộp."
                : "Dùng khi đặt kèo: kèo cân bằng chọn thoải mái, kèo chênh lệch chỉ được chọn Hòa hoặc đội yếu hơn.\nThắng: giảm 1 cốc bia hoặc 1 gói lạc | Thua: tăng 2 cốc bia hoặc 2 gói lạc theo loại của trận.";
            _ = SendGroupTelegramAsync(
                $"🎁 Nhà cái đã tặng MỌI NGƯỜI 1 {starName}!\n{desc}", ct);
        }

        return RedirectToPage(new { q = Keyword, p = CurrentPage });
    }

    public async Task<IActionResult> OnPostRejournalCupLogsAsync(CancellationToken ct)
    {
        var count = await _svc.RejournalCupLogsAsync(ct);
        TempData["Success"] = $"Đã tái tạo {count} CupLog entries với balanceAfter chính xác.";
        return RedirectToPage(new { q = Keyword, p = CurrentPage });
    }

    private async Task SendTelegramAsync(long chatId, string text, CancellationToken ct)
    {
        try { await _bot.SendMessage(chatId, text, cancellationToken: ct); }
        catch { }
    }

    private async Task SendGroupTelegramAsync(string text, CancellationToken ct)
    {
        try
        {
            var chatId = _config["KeoBia:Telegram:ShareBeer:GroupChatId"];
            if (!string.IsNullOrWhiteSpace(chatId) && long.TryParse(chatId, out var id))
                await _bot.SendMessage(id, text, cancellationToken: ct);
        }
        catch { }
    }
}
