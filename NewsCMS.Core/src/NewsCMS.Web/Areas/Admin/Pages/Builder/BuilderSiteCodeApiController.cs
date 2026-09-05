using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// API custom CSS/JS/HTML tầng site (Phase 3). Khoá sau Builder.Code.Manage — chỉ root và
/// admin site được tin cậy mới sửa. HeadHtml/BodyEndHtml cho snippet analytics/pixel.
/// </summary>
[Authorize(Permissions.Builder.CodeManage)]
[Route("admin/api/builder/site-code")]
public class BuilderSiteCodeApiController : ControllerBase
{
    private readonly ISiteCustomCodeService _service;

    public BuilderSiteCodeApiController(ISiteCustomCodeService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dto = await _service.GetAsync(ct);
        return Ok(dto);
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SiteCustomCodeSaveRequest request, CancellationToken ct)
    {
        var result = await _service.SaveAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }
}
