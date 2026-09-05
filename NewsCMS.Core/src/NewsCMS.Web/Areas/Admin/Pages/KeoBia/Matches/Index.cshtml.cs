using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Common;
using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.KeoBia.Matches;

[Authorize(Permissions.KeoBia.ManageMatches)]
public class IndexModel : PageModel
{
    private static readonly ConcurrentDictionary<Guid, AnalysisRunStatus> AnalysisRuns = new();

    private readonly IKeoBiaService _svc;
    private readonly IKeoBiaFootballDataSyncService _footballDataSync;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IKeoBiaService svc,
        IKeoBiaFootballDataSyncService footballDataSync,
        IServiceScopeFactory scopeFactory,
        ILogger<IndexModel> logger)
    {
        _svc = svc;
        _footballDataSync = footballDataSync;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true, Name = "q")] public string? Keyword { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public string? FilterStatus { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int CurrentPage { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "queuedAnalysisId")] public Guid? QueuedAnalysisId { get; set; }

    public PagedList<KeoBiaMatchListItemDto> Items { get; private set; } = PagedList<KeoBiaMatchListItemDto>.Empty();
    public List<SelectListItem> StatusOptions { get; } =
    [
        new("Tất cả trạng thái", ""),
        new("Sắp diễn ra", KeoBiaMatchStatus.Scheduled),
        new("Đang live", KeoBiaMatchStatus.Live),
        new("Đã kết thúc", KeoBiaMatchStatus.Finished),
        new("Đã hủy", KeoBiaMatchStatus.Cancelled)
    ];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _svc.SearchMatchesAsync(Keyword, FilterStatus, CurrentPage, 20, ct);
    }

    public async Task<IActionResult> OnPostResultAsync(Guid id, int? homeScore, int? awayScore, int? penaltyHomeScore, int? penaltyAwayScore, string status, CancellationToken ct)
    {
        var result = await _svc.UpdateResultAsync(id, homeScore, awayScore, status, ct, penaltyHomeScore, penaltyAwayScore);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã cập nhật kết quả trận." : result.Error;
        return RedirectToPage(new { q = Keyword, status = FilterStatus, p = CurrentPage });
    }

    public async Task<IActionResult> OnPostOddsAsync(Guid id, string? correctScoreOddsJson, CancellationToken ct)
    {
        var result = await _svc.UpdateCorrectScoreOddsAsync(id, correctScoreOddsJson, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã cập nhật tỉ số gợi ý." : result.Error;
        return RedirectToPage(new { q = Keyword, status = FilterStatus, p = CurrentPage });
    }

    public IActionResult OnPostForceAnalysis(Guid id)
    {
        AnalysisRuns[id] = new AnalysisRunStatus("running", "Đang phân tích AI...", DateTime.UtcNow);
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var analysis = scope.ServiceProvider.GetRequiredService<IKeoBiaAnalysisService>();
                var result = await analysis.AnalyzeMatchAsync(id, force: true, CancellationToken.None);
                if (!result.Succeeded || !string.Equals(result.Value?.Source, "ai", StringComparison.OrdinalIgnoreCase))
                {
                    AnalysisRuns[id] = new AnalysisRunStatus("failed", result.Error ?? "AI chưa tạo được nội dung mới.", DateTime.UtcNow);
                    _logger.LogWarning("Queued KeoBia AI analysis failed for match {MatchId}: {Error}", id, result.Error ?? result.Value?.Source ?? "unknown");
                }
                else
                {
                    AnalysisRuns[id] = new AnalysisRunStatus("done", $"Đã phân tích AI xong cho {result.Value!.Title}.", DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                AnalysisRuns[id] = new AnalysisRunStatus("failed", "Phân tích AI lỗi. Xem log server để biết chi tiết.", DateTime.UtcNow);
                _logger.LogError(ex, "Queued KeoBia AI analysis crashed for match {MatchId}.", id);
            }
        });

        TempData["Success"] = "Đã đưa trận vào hàng đợi phân tích AI. Bạn có thể tiếp tục thao tác, nội dung sẽ cập nhật nền.";
        return RedirectToPage(new { q = Keyword, status = FilterStatus, p = CurrentPage, queuedAnalysisId = id });
    }

    public async Task<IActionResult> OnGetAnalysisEventsAsync(Guid id, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var status = AnalysisRuns.TryGetValue(id, out var current)
                ? current
                : new AnalysisRunStatus("running", "Đang chờ phân tích AI...", DateTime.UtcNow);

            await Response.WriteAsync($"event: {status.State}\n", ct);
            await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(status.Message)}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            if (status.State is "done" or "failed")
            {
                AnalysisRuns.TryRemove(id, out _);
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return new EmptyResult();
    }

    public async Task<IActionResult> OnPostSyncResultsAsync(CancellationToken ct)
    {
        var result = await _footballDataSync.SyncAsync(force: true, ct);
        if (result.Succeeded && result.Value is not null)
        {
            TempData["Success"] =
                $"Đã chạy cập nhật Football-Data: {result.Value.ImportedRows}/{result.Value.TotalRows} trận, bỏ qua {result.Value.SkippedRows}. Kết quả trận và kèo bet đã được xử lý theo dữ liệu mới.";
        }
        else
        {
            TempData["Error"] = result.Error ?? "Không chạy được job cập nhật kết quả.";
        }

        return RedirectToPage(new { q = Keyword, status = FilterStatus, p = CurrentPage });
    }

    private sealed record AnalysisRunStatus(string State, string Message, DateTime UpdatedAt);
}
