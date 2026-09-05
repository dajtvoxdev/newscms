using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>API CRUD chuyên mục builder (Phase 5).</summary>
[Authorize]
[Route("admin/api/builder/categories")]
public class BuilderCategoryApiController : ControllerBase
{
    private readonly IBuilderCategoryService _service;

    public BuilderCategoryApiController(IBuilderCategoryService service) => _service = service;

    [HttpGet("{id:guid}")]
    [Authorize(Permissions.Content.ManageCategory)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpGet]
    [Authorize(Permissions.Content.ManageCategory)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var list = await _service.ListAsync(page, pageSize, search, ct);
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Permissions.Content.ManageCategory)]
    public async Task<IActionResult> Create([FromBody] BuilderCategorySaveRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        if (!result.Succeeded) return BadRequest(new { error = result.Error });
        return Created($"/admin/api/builder/categories/{result.Value!.Id}", result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Permissions.Content.ManageCategory)]
    public async Task<IActionResult> Update(Guid id, [FromBody] BuilderCategorySaveRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, ct);
        if (!result.Succeeded) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Permissions.Content.ManageCategory)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _service.DeleteAsync(id, ct);
        return result.Succeeded ? NoContent() : NotFound(new { error = result.Error });
    }
}
