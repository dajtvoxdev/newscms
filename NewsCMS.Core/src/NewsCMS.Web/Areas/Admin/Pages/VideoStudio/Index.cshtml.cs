using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.VideoStudio;

/// <summary>Danh sách video quảng cáo AI của site hiện tại.</summary>
[Authorize(Permissions.VideoStudio.View)]
public class IndexModel : PageModel
{
    public const int PageSize = 20;

    private readonly IAdVideoClient _advideo;
    private readonly IAdVideoConnectionService _connection;

    public IndexModel(IAdVideoClient advideo, IAdVideoConnectionService connection)
    {
        _advideo = advideo;
        _connection = connection;
    }

    public bool IsLinked { get; private set; }
    public AdVideoJobPageDto? Jobs { get; private set; }
    public string? Error { get; private set; }
    public string? Status { get; private set; }

    public async Task OnGetAsync(string? status, int page = 1)
    {
        Status = string.IsNullOrWhiteSpace(status) ? null : status;
        IsLinked = await _connection.IsCurrentSiteLinkedAsync();

        if (!IsLinked)
        {
            return;
        }

        Result<AdVideoJobPageDto> result = await _advideo.ListJobsAsync(Status, page, PageSize);
        Jobs = result.Value;
        Error = result.Error;
    }
}
