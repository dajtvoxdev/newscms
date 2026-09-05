using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;
using BuilderRouteContext = NewsCMS.Application.Builder.RouteContext;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// API preview cho dynamic block — builder canvas gọi endpoint này để hiển thị nội dung thật
/// của khối động khi kéo vào canvas. Props gửi qua body JSON.
/// </summary>
[Authorize(Permissions.Builder.PageEdit)]
[Route("admin/api/builder/block-preview")]
public class BlockPreviewApiController : ControllerBase
{
    private readonly IDynamicBlockRegistry _registry;
    private readonly Application.Site.ICurrentSite _currentSite;
    private readonly IContentTypeRegistry _contentTypes;

    public BlockPreviewApiController(
        IDynamicBlockRegistry registry,
        Application.Site.ICurrentSite currentSite,
        IContentTypeRegistry contentTypes)
    {
        _registry = registry;
        _currentSite = currentSite;
        _contentTypes = contentTypes;
    }

    [HttpPost("{key}")]
    public async Task<IActionResult> Preview(string key, [FromBody] BlockPreviewRequest? request, CancellationToken ct)
    {
        var block = _registry.Get(key);
        if (block is null)
            return NotFound(new { error = $"Không tìm thấy dynamic block '{key}'." });

        try
        {
            BuilderRouteContext? routeContext = null;
            if (request?.SampleEntityId is { } entityId && entityId != Guid.Empty)
            {
                var sampleType = request.SampleType?.Trim().ToLowerInvariant();
                var contentType = !string.IsNullOrEmpty(sampleType)
                    ? _contentTypes.FindByKey(sampleType)
                    : null;

                // Nếu không chỉ rõ sampleType, thử Post rồi Product
                if (contentType is null)
                {
                    var d = await _contentTypes.LoadDetailAsync(Domain.Enums.RouteType.Post, entityId, ct);
                    if (d is not null)
                        contentType = _contentTypes.FindByRouteType(Domain.Enums.RouteType.Post);
                    else
                        contentType = _contentTypes.FindByRouteType(Domain.Enums.RouteType.Product);
                }

                if (contentType is not null)
                {
                    var detail = await _contentTypes.LoadDetailAsync(contentType.RouteType, entityId, ct);
                    if (detail is not null)
                    {
                        var path = await contentType.BuildPathAsync(entityId, ct);
                        routeContext = new BuilderRouteContext(
                            contentType.RouteType,
                            detail.Id,
                            detail.Slug,
                            path ?? $"/{detail.Slug}",
                            detail.CategoryId,
                            detail.CategorySlug);
                    }
                }
            }

            var ctx = new DynamicBlockContext(_currentSite.SiteId, "vi", request?.PropsJson, routeContext);
            var html = await block.RenderAsync(ctx, ct);
            return Ok(new { html, matchedCount = await CountAsync(block, ctx, ct) });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Lỗi render block: {ex.Message}" });
        }
    }

    /// <summary>
    /// Số bản ghi khớp bộ lọc, cho panel hiện "Khớp 12 bài, hiển thị 6". Khối không cài
    /// <see cref="IBlockMatchCounter"/> hoặc đếm lỗi thì trả null — mất một dòng thông tin còn hơn
    /// làm preview thất bại, vì HTML đã render xong rồi.
    /// </summary>
    private static async Task<int?> CountAsync(IDynamicBlock block, DynamicBlockContext ctx, CancellationToken ct)
    {
        if (block is not IBlockMatchCounter counter) return null;
        try
        {
            return await counter.CountMatchesAsync(ctx, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Danh sách tất cả dynamic blocks đã đăng ký (cho builder panel).</summary>
    [HttpGet]
    public IActionResult List()
    {
        var blocks = _registry.All.Select(b => new { b.Key }).ToList();
        return Ok(blocks);
    }

    public sealed record BlockPreviewRequest(
        string? PropsJson,
        Guid? SampleEntityId = null,
        string? SampleType = null);
}
