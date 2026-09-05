using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>API translation (Phase 5). CRUD bản dịch trang theo culture.</summary>
[Authorize]
[Route("admin/api/builder/translations")]
public class BuilderTranslationApiController : ControllerBase
{
    private readonly IPageTranslationService _service;

    public BuilderTranslationApiController(IPageTranslationService service) => _service = service;

    [HttpGet("page/{pageId:guid}")]
    [Authorize(Permissions.Builder.PageView)]
    public async Task<IActionResult> ListByPage(Guid pageId, CancellationToken ct)
    {
        var list = await _service.ListByPageAsync(pageId, ct);
        return Ok(list);
    }

    [HttpGet("page/{pageId:guid}/{culture}")]
    [Authorize(Permissions.Builder.PageView)]
    public async Task<IActionResult> Get(Guid pageId, string culture, CancellationToken ct)
    {
        var t = await _service.GetAsync(pageId, culture, ct);
        return t is not null ? Ok(t) : NotFound();
    }

    [HttpPost]
    [Authorize(Permissions.Builder.PageEdit)]
    public async Task<IActionResult> Save([FromBody] PageTranslationSaveRequest request, CancellationToken ct)
    {
        var result = await _service.SaveAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Permissions.Builder.PageEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _service.DeleteAsync(id, ct);
        return result.Succeeded ? NoContent() : NotFound(new { error = result.Error });
    }
}
