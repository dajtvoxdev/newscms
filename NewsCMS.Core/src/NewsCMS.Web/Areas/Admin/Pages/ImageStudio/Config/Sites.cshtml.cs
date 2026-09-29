using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.ImageStudio.Config;

/// <summary>Bật Xưởng ảnh cho từng site + hạn mức ảnh/tháng và ảnh/người/ngày.</summary>
[Authorize(Roles = "SuperAdmin")]
public class SitesModel : PageModel
{
    private readonly IImageStudioSiteService _sites;

    public SitesModel(IImageStudioSiteService sites) => _sites = sites;

    public IReadOnlyList<ImageStudioSiteRowDto> Sites { get; private set; } = Array.Empty<ImageStudioSiteRowDto>();

    public async Task OnGetAsync() => Sites = await _sites.GetSitesAsync();

    public async Task<IActionResult> OnPostSaveAsync(Guid siteId, bool enabled, int monthlyImageQuota, int perUserDailyQuota)
    {
        Result result = await _sites.SaveAsync(new ImageStudioSiteQuotaInput(siteId, enabled, monthlyImageQuota, perUserDailyQuota));
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã lưu cấu hình site." : result.Error;
        return RedirectToPage();
    }
}
