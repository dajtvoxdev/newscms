using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>API SEO meta + JSON-LD (Phase 5). Dùng chung partial _SeoMetaEditor cho mọi entity.</summary>
[Authorize]
[Route("admin/api/builder/seo")]
public class BuilderSeoApiController : ControllerBase
{
    private readonly ISeoMetaService _service;

    public BuilderSeoApiController(ISeoMetaService service) => _service = service;

    [HttpGet]
    [Authorize(Permissions.Seo.EditMeta)]
    public async Task<IActionResult> Get([FromQuery] string entityType, [FromQuery] Guid entityId,
        [FromQuery] string culture = "vi", CancellationToken ct = default)
    {
        var meta = await _service.GetAsync(entityType, entityId, culture, ct);
        return meta is not null ? Ok(meta) : Ok(new { });
    }

    [HttpPut]
    [Authorize(Permissions.Seo.EditMeta)]
    public async Task<IActionResult> Save([FromBody] SeoMetaSaveRequest request, CancellationToken ct)
    {
        var result = await _service.SaveAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpPost("jsonld")]
    [Authorize(Permissions.Seo.EditMeta)]
    public async Task<IActionResult> GenerateJsonLd([FromQuery] string entityType, [FromQuery] Guid entityId,
        [FromQuery] string culture = "vi", CancellationToken ct = default)
    {
        var jsonLd = await _service.GenerateJsonLdAsync(entityType, entityId, culture, ct);
        return Ok(new { jsonLd });
    }
}
