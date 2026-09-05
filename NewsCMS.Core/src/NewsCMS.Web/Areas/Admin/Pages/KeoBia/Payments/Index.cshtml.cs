using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using NewsCMS.Application.Common;
using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.KeoBia.Payments;

[Authorize(Permissions.KeoBia.ManagePayments)]
public class IndexModel : PageModel
{
    private readonly IKeoBiaService _svc;

    public IndexModel(IKeoBiaService svc)
    {
        _svc = svc;
    }

    [BindProperty(SupportsGet = true, Name = "q")] public string? Keyword { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public string? FilterStatus { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int CurrentPage { get; set; } = 1;

    public PagedList<KeoBiaBeerPaymentDto> Items { get; private set; } = PagedList<KeoBiaBeerPaymentDto>.Empty();
    public List<SelectListItem> StatusOptions { get; } =
    [
        new("Tất cả trạng thái", ""),
        new("Pending", KeoBiaBeerPaymentStatus.Pending),
        new("Paid", KeoBiaBeerPaymentStatus.Paid),
        new("Failed", KeoBiaBeerPaymentStatus.Failed),
        new("Cancelled", KeoBiaBeerPaymentStatus.Cancelled)
    ];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _svc.SearchBeerPaymentsAsync(Keyword, FilterStatus, CurrentPage, 20, ct);
    }

    public async Task<IActionResult> OnPostPaidAsync(Guid id, CancellationToken ct)
    {
        var result = await _svc.MarkBeerPaymentPaidAsync(id, "manual-admin", null, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã mark paid." : result.Error;
        return RedirectToPage(new { q = Keyword, status = FilterStatus, p = CurrentPage });
    }

    public async Task<IActionResult> OnPostFailedAsync(Guid id, CancellationToken ct)
    {
        var result = await _svc.MarkBeerPaymentFailedAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã mark failed." : result.Error;
        return RedirectToPage(new { q = Keyword, status = FilterStatus, p = CurrentPage });
    }
}
