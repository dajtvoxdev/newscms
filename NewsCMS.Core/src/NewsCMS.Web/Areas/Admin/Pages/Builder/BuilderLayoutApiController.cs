using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// API endpoints cho layout builder (Phase 3). CRUD shell/header/footer/sidebar.
/// </summary>
[Authorize]
[Route("admin/api/builder/layouts")]
public class BuilderLayoutApiController : ControllerBase
{
    private readonly ISiteLayoutService _service;

    public BuilderLayoutApiController(ISiteLayoutService service) => _service = service;

    [HttpGet("{id:guid}")]
    [Authorize(Permissions.Builder.LayoutManage)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpGet]
    [Authorize(Permissions.Builder.LayoutManage)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await _service.ListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Permissions.Builder.LayoutManage)]
    public async Task<IActionResult> Create([FromBody] SiteLayoutSaveRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        if (!result.Succeeded) return BadRequest(new { error = result.Error });
        return Created($"/admin/api/builder/layouts/{result.Value!.Id}", result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Permissions.Builder.LayoutManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SiteLayoutSaveRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, ct);
        if (!result.Succeeded) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Permissions.Builder.LayoutManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _service.DeleteAsync(id, ct);
        return result.Succeeded ? NoContent() : NotFound(new { error = result.Error });
    }
}
