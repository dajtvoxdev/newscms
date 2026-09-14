using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// API endpoints cho builder page (Phase 2). GrapesJS gọi các endpoint này qua storage remote.
/// Tất cả đều yêu cầu quyền Builder.* và anti-forgery token qua header RequestVerificationToken.
/// </summary>
[Authorize]
[Route("admin/api/builder/pages")]
public class BuilderPageApiController : ControllerBase
{
    private readonly IBuilderPageService _service;

    public BuilderPageApiController(IBuilderPageService service) => _service = service;

    [HttpGet("{id:guid}")]
    [Authorize(Permissions.Builder.PageView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpGet]
    [Authorize(Permissions.Builder.PageView)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var list = await _service.ListAsync(page, pageSize, search, ct);
        return Ok(list);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Permissions.Builder.PageEdit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] BuilderPageSaveRequest request, CancellationToken ct)
    {
        if (CarriesScript(request)
            && !User.HasClaim(Permissions.Prefix, Permissions.Builder.CodeManage))
            return Forbid();

        var result = await _service.UpdateAsync(id, request, ct);
        if (!result.Succeeded) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost]
    [Authorize(Permissions.Builder.PageCreate)]
    public async Task<IActionResult> Create([FromBody] BuilderPageSaveRequest request, CancellationToken ct)
    {
        if (CarriesScript(request)
            && !User.HasClaim(Permissions.Prefix, Permissions.Builder.CodeManage))
            return Forbid();

        var result = await _service.CreateAsync(request, ct);
        if (!result.Succeeded) return BadRequest(new { error = result.Error });
        return Created($"/admin/api/builder/pages/{result.Value!.Id}", result.Value);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Permissions.Builder.PageDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _service.DeleteAsync(id, ct);
        return result.Succeeded ? NoContent() : NotFound(new { error = result.Error });
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Permissions.Builder.PagePublish)]
    public async Task<IActionResult> Publish(Guid id, [FromBody] BuilderPagePublishRequest? request, CancellationToken ct)
    {
        var result = await _service.PublishAsync(id, request ?? new BuilderPagePublishRequest(null), ct);
        if (!result.Succeeded) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpGet("{id:guid}/revisions")]
    [Authorize(Permissions.Builder.PageView)]
    public async Task<IActionResult> Revisions(Guid id, CancellationToken ct)
    {
        var revisions = await _service.GetRevisionsAsync(id, ct);
        return Ok(revisions);
    }

    [HttpPost("{id:guid}/revisions/{version:int}/restore")]
    [Authorize(Permissions.Builder.RevisionRestore)]
    public async Task<IActionResult> RestoreRevision(Guid id, int version, CancellationToken ct)
    {
        var result = await _service.RestoreRevisionAsync(id, version, ct);
        if (!result.Succeeded) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    /// <summary>
    /// Request có mang mã chạy trên trình duyệt khách hay không → đòi Builder.Code.Manage,
    /// kể cả khi user đã có PageEdit. Ba nguồn:
    ///   • <c>CustomJs</c> — JS riêng của trang;
    ///   • <c>data-nc-js</c> trong HTML — JS riêng từng khối, BlockCodeExtractor nối vào script
    ///     của trang lúc render;
    ///   • <c>data-nc-html</c> trong HTML — khối "Nhúng HTML", đổ nguyên văn vào trang lúc render
    ///     nên KHÔNG qua ContentSanitizer: thừa sức mang script/iframe/overlay.
    /// Request không mang mã vẫn qua được để người thiếu quyền sửa phần còn lại của trang.
    /// </summary>
    private static bool CarriesScript(BuilderPageSaveRequest request) =>
        !string.IsNullOrWhiteSpace(request.CustomJs)
        || (request.CompiledHtml?.Contains("data-nc-js", StringComparison.OrdinalIgnoreCase) ?? false)
        || BlockCodeExtractor.CarriesRawHtml(request.CompiledHtml);
}
