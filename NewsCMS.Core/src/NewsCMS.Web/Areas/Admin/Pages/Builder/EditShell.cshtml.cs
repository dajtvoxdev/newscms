using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// Builder full-screen cho SHELL (header/footer bọc mọi trang). Cùng GrapesJS, cùng palette khối
/// động, cùng canvas như <see cref="EditModel"/> — chỉ khác nguồn dữ liệu: lưu vào
/// <c>SiteLayout</c> qua <c>/admin/api/builder/layouts/{id}</c> thay vì <c>Page</c>.
///
/// Vì sao tách trang riêng thay vì thêm chế độ vào Edit.cshtml: trang có draft/publish/SEO/lịch sử
/// phiên bản, shell thì không có cái nào; nhét chung sẽ là một mớ if. Phần thực sự dùng chung
/// (canvas, font, asset manager) đã nằm ở wwwroot/js/admin/nc-builder-canvas.js.
///
/// Quyền: Builder.Layout.Manage — sửa shell là sửa mọi trang của site cùng lúc, nặng hơn sửa
/// một trang nên không dùng chung quyền với Builder.Page.Edit.
/// </summary>
[Authorize(Permissions.Builder.LayoutManage)]
public class EditShellModel : PageModel
{
    private readonly ISiteLayoutService _layouts;
    private readonly IDesignTokenCssBuilder _tokenCss;
    private readonly ICurrentSite _currentSite;
    private readonly ISiteCustomCodeService _siteCode;

    public EditShellModel(
        ISiteLayoutService layouts,
        IDesignTokenCssBuilder tokenCss,
        ICurrentSite currentSite,
        ISiteCustomCodeService siteCode)
    {
        _layouts = layouts;
        _tokenCss = tokenCss;
        _currentSite = currentSite;
        _siteCode = siteCode;
    }

    public Guid LayoutId { get; private set; }
    public string LayoutName { get; private set; } = string.Empty;
    public string LayoutKey { get; private set; } = string.Empty;
    public string Kind { get; private set; } = "Shell";
    public bool IsDefault { get; private set; }

    /// <summary>URL CSS design token của site — nc-tailwind.js đọc khối @@theme từ đây.</summary>
    public string TokenCssUrl { get; private set; } = string.Empty;

    /// <summary>CSS/JS/HeadHtml tầng site — canvas phải dựng lại đúng môi trường trang public.</summary>
    public string SiteCustomCss { get; private set; } = string.Empty;
    public string SiteCustomJs { get; private set; } = string.Empty;
    public string SiteHeadHtml { get; private set; } = string.Empty;

    /// <summary>Cùng danh sách với PageRenderer nên font chọn ở đây chắc chắn được trang public nạp.</summary>
    public IReadOnlyList<GoogleFont> GoogleFonts { get; } = GoogleFontCatalog.All;

    /// <summary>Builder.Code.Manage — ẩn/hiện ô JavaScript trong modal Code.</summary>
    public bool HasCodeScope { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var result = await _layouts.GetByIdAsync(id, ct);
        if (!result.Succeeded) return NotFound();

        var layout = result.Value!;
        LayoutId = layout.Id;
        LayoutName = layout.Name;
        LayoutKey = layout.Key;
        Kind = layout.Kind;
        IsDefault = layout.IsDefault;

        TokenCssUrl = $"/_nc/site/{_currentSite.Slug}-{await _tokenCss.GetCssHashAsync(ct)}.css";
        var siteCode = await _siteCode.GetAsync(ct);
        SiteCustomCss = siteCode.CustomCss ?? string.Empty;
        SiteCustomJs = siteCode.CustomJs ?? string.Empty;
        SiteHeadHtml = siteCode.HeadHtml ?? string.Empty;
        HasCodeScope = User.HasClaim(Permissions.Prefix, Permissions.Builder.CodeManage);

        return Page();
    }
}
