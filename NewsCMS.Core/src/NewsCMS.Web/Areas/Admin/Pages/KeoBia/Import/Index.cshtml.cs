using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.KeoBia;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.KeoBia.Import;

[Authorize(Permissions.KeoBia.ImportSchedule)]
public class IndexModel : PageModel
{
    private readonly IKeoBiaService _svc;
    private readonly IKeoBiaOpenFootballSyncService _openFootballSync;
    private readonly IKeoBiaFootballDataSyncService _footballDataSync;

    public IndexModel(
        IKeoBiaService svc,
        IKeoBiaOpenFootballSyncService openFootballSync,
        IKeoBiaFootballDataSyncService footballDataSync)
    {
        _svc = svc;
        _openFootballSync = openFootballSync;
        _footballDataSync = footballDataSync;
    }

    public IReadOnlyList<KeoBiaImportJobDto> RecentJobs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        RecentJobs = await _svc.GetRecentImportJobsAsync(10, ct);
    }

    public Task<IActionResult> OnPostAsync(CancellationToken ct) => OnPostFootballDataAsync(ct);

    public async Task<IActionResult> OnPostFootballDataAsync(CancellationToken ct)
    {
        ModelState.Clear();

        var result = await _footballDataSync.SyncAsync(force: true, ct);
        if (result.Succeeded && result.Value != null)
        {
            TempData["Success"] = $"Đã đồng bộ Football-Data {result.Value.ImportedRows}/{result.Value.TotalRows} trận. Bỏ qua {result.Value.SkippedRows} dòng.";
            return RedirectToPage();
        }

        RecentJobs = await _svc.GetRecentImportJobsAsync(10, ct);
        ModelState.AddModelError("", result.Error ?? "Đồng bộ Football-Data thất bại.");
        return Page();
    }

    public async Task<IActionResult> OnPostOpenFootballAsync(CancellationToken ct)
    {
        ModelState.Clear();

        var result = await _openFootballSync.SyncAsync(force: true, ct);
        if (result.Succeeded && result.Value != null)
        {
            TempData["Success"] = $"Đã đồng bộ lịch OpenFootball {result.Value.ImportedRows}/{result.Value.TotalRows} trận. Bỏ qua {result.Value.SkippedRows} dòng.";
            return RedirectToPage();
        }

        RecentJobs = await _svc.GetRecentImportJobsAsync(10, ct);
        ModelState.AddModelError("", result.Error ?? "Đồng bộ OpenFootball thất bại.");
        return Page();
    }

    public async Task<IActionResult> OnPostCleanupAsync(CancellationToken ct)
    {
        ModelState.Clear();

        var result = await _svc.CleanupOrphanedPlaceholderMatchesAsync(ct);
        if (result.Succeeded)
        {
            TempData["Success"] = $"Đã xóa {result.Value} trận placeholder orphan. Hãy đồng bộ lại Football-Data.";
            return RedirectToPage();
        }

        RecentJobs = await _svc.GetRecentImportJobsAsync(10, ct);
        ModelState.AddModelError("", result.Error ?? "Dọn dẹp thất bại.");
        return Page();
    }
}
