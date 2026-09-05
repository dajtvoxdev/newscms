using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.KeoBia;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.KeoBia.Quiz;

[Authorize(Permissions.KeoBia.ManageQuiz)]
public class IndexModel : PageModel
{
    private readonly IKeoBiaService _svc;

    public IndexModel(IKeoBiaService svc) => _svc = svc;

    public IReadOnlyList<KeoBiaQuestionAdminDto> Items { get; private set; } = Array.Empty<KeoBiaQuestionAdminDto>();
    public Dictionary<Guid, KeoBiaQuestionVotesDto> VotesByQuestion { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _svc.GetQuestionsAsync(ct);
        foreach (var q in Items)
        {
            var votes = await _svc.GetQuestionVotesAsync(q.Id, ct);
            if (votes.Succeeded && votes.Value is not null)
                VotesByQuestion[q.Id] = votes.Value;
        }
    }

    public async Task<IActionResult> OnPostCloseAsync(Guid id, CancellationToken ct)
    {
        var r = await _svc.CloseQuestionAsync(id, ct);
        TempData[r.Succeeded ? "Success" : "Error"] = r.Succeeded ? "Đã đóng bình chọn." : r.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevealAsync(Guid id, string correctChoiceKey, CancellationToken ct)
    {
        var r = await _svc.RevealQuestionAnswerAsync(id, correctChoiceKey, ct);
        TempData[r.Succeeded ? "Success" : "Error"] = r.Succeeded
            ? $"Đã công bố đáp án. {r.Value!.RewardedCount} người được thưởng, tổng {KeoBiaUnitRules.FormatCount(r.Value.TotalRewardCups, r.Value.UnitCode)}. {r.Value.PenalizedCount} người bị phạt, tổng {KeoBiaUnitRules.FormatCount(r.Value.TotalPenaltyCups, r.Value.UnitCode)}."
            : r.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var r = await _svc.DeleteQuestionAsync(id, ct);
        TempData[r.Succeeded ? "Success" : "Error"] = r.Succeeded ? "Đã xoá câu hỏi." : r.Error;
        return RedirectToPage();
    }
}
