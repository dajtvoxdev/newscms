using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// API design token của site (--color-brand-500, --font-display…). Token là nguồn duy nhất sinh ra
/// cả CSS variables lẫn khối @theme Tailwind (DesignTokenCssBuilder), nên sửa ở đây là đổi toàn
/// site: cả var(--color-…) lẫn utility bg-brand-500. Khoá sau Builder.Layout.Manage — cùng nhóm
/// quyền với layout vì cả hai đều là "khung thiết kế" của site.
/// </summary>
[Authorize(Permissions.Builder.LayoutManage)]
[Route("admin/api/builder/design-tokens")]
public class BuilderDesignTokensApiController : ControllerBase
{
    private readonly ISiteBuilderApi _api;

    public BuilderDesignTokensApiController(ISiteBuilderApi api) => _api = api;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await _api.ListDesignTokensAsync(ct));

    /// <summary>Lưu cả danh sách (create/update theo Group+Key). Thứ tự trong mảng = SortOrder.</summary>
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] List<DesignTokenSpec> tokens, CancellationToken ct)
    {
        var result = await _api.SaveDesignTokensAsync(tokens, ct);
        return result.Succeeded ? Ok(new { ok = true }) : BadRequest(new { error = result.Error });
    }

    /// <summary>Xoá một token theo Group+Key. PUT cố tình không xoá thứ thiếu trong danh sách nên UI gọi riêng.</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string group, [FromQuery] string key, CancellationToken ct)
    {
        var result = await _api.DeleteDesignTokenAsync(group, key, ct);
        return result.Succeeded ? Ok(new { ok = true }) : BadRequest(new { error = result.Error });
    }
}