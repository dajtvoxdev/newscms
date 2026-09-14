using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// Màn hình builder full-screen cho một trang. Layout = null để GrapesJS chiếm toàn bộ viewport.
/// API calls đi qua BuilderPageApiController (/admin/api/builder/pages/{id}).
/// </summary>
[Authorize(Permissions.Builder.PageEdit)]
public class EditModel : PageModel
{
    private readonly IBuilderPageService _service;
    private readonly IDesignTokenCssBuilder _tokenCss;
    private readonly ICurrentSite _currentSite;
    private readonly ISiteCustomCodeService _siteCode;
    private readonly ISiteLayoutService _layouts;

    public EditModel(
        IBuilderPageService service,
        IDesignTokenCssBuilder tokenCss,
        ICurrentSite currentSite,
        ISiteCustomCodeService siteCode,
        ISiteLayoutService layouts)
    {
        _service = service;
        _tokenCss = tokenCss;
        _currentSite = currentSite;
        _siteCode = siteCode;
        _layouts = layouts;
    }

    public Guid PageId { get; set; }
    public string PageTitle { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Kind { get; set; } = "Static";
    public int Version { get; set; }
    public string StatusLabel { get; set; } = "Draft";

    /// <summary>Xác định trang có phải là template trang chi tiết hoặc chuyên mục không.</summary>
    public bool IsTemplate => Kind == "PostTemplate" || Kind == "ProductTemplate" || Kind == "CategoryTemplate";

    /// <summary>Loại thực thể mẫu: "product" cho template sản phẩm, "post" cho template bài viết.</summary>
    public string SampleType => Kind == "ProductTemplate" ? "product" : "post";

    /// <summary>Shell đang gán cho trang; Guid.Empty = dùng shell mặc định của site.</summary>
    public Guid LayoutId { get; set; }

    /// <summary>URL CSS design token của site — nc-tailwind.js đọc khối @theme từ đây.</summary>
    public string TokenCssUrl { get; private set; } = string.Empty;

    /// <summary>
    /// CSS tuỳ biến tầng site (component CSS của theme: .chu-cta, .chu-cup svg, swiper…).
    /// PageRenderer.BuildHtml nhồi khối này vào &lt;head&gt; mọi trang public; canvas builder phải nạp
    /// cùng CSS để preview khối động (news-grid, banner-slider) hiển thị đúng cỡ icon/layout, không
    /// còn SVG cốc cà phê phình to hay slider vỡ. Nạp server-side (không qua endpoint site-code vốn
    /// khoá sau Code.Manage) để user chỉ có PageEdit vẫn xem đúng.
    /// </summary>
    public string SiteCustomCss { get; private set; } = string.Empty;

    /// <summary>
    /// CSS của shell đang gán cho trang (CompiledCss + CustomCss) — PageRenderer nối lớp này vào
    /// &lt;head&gt; trang public NGAY SAU CSS site, nên canvas thiếu nó thì trang dựng qua MCP/API
    /// (chỉ có CompiledCss trên layout, không có BuilderJson) hiện không style: SVG icon phình
    /// tràn canvas, section mất màu mất cột. Nạp server-side như SiteCustomCss để user chỉ có
    /// PageEdit vẫn xem đúng.
    /// </summary>
    public string LayoutCss { get; private set; } = string.Empty;

    /// <summary>
    /// Font Google dựng dropdown "Font family" trong Style Manager. Lấy từ cùng một
    /// <see cref="GoogleFontCatalog"/> mà PageRenderer dùng để nhét thẻ link vào trang public — nếu
    /// tách thành hai danh sách thì sẽ có ngày builder cho chọn một font mà trang public không nạp.
    /// </summary>
    public IReadOnlyList<GoogleFont> GoogleFonts { get; } = GoogleFontCatalog.All;

    /// <summary>
    /// JS tuỳ biến tầng site (khởi tạo swiper coverflow, masonry .chu-pin-grid, nav toggle…).
    /// PageRenderer nhồi vào cuối &lt;body&gt; mọi trang public. Nhiều khối động dùng mẫu "ẩn tới khi JS
    /// bật" (vd .chu-pin-item{position:absolute;opacity:0} chỉ hiện khi masonry thêm .is-pin-ready), nên
    /// nếu canvas nạp CSS site mà KHÔNG chạy JS thì các khối này sập. Chạy JS trong iframe canvas để
    /// preview trung thực, tổng quát cho mọi theme thay vì hardcode class. JS này vốn public (chạy cho
    /// mọi khách xem trang) nên phục vụ server-side cho user PageEdit không phải là leo thang quyền.
    /// </summary>
    public string SiteCustomJs { get; private set; } = string.Empty;

    /// <summary>
    /// HTML chèn cuối &lt;head&gt; trang public (font, &lt;link&gt; CSS vendor, &lt;script src&gt; vendor như
    /// swiper/masonry/imagesloaded). Canvas cần các thư viện này để JS site khởi tạo được. Nội dung
    /// public, phục vụ server-side để preview khối động chạy đúng.
    /// </summary>
    public string SiteHeadHtml { get; private set; } = string.Empty;

    /// <summary>User có quyền Builder.Code.Manage — ẩn/hiện tab JavaScript trong modal Code.</summary>
    public bool HasCodeScope { get; private set; }

    /// <summary>User có quyền Seo.EditMeta — ẩn/hiện nút SEO trên topbar.</summary>
    public bool HasSeoScope { get; private set; }

    /// <summary>User có quyền Builder.Revision.Restore — cho phép khôi phục version trong panel Lịch sử.</summary>
    public bool HasRestoreScope { get; private set; }

    /// <summary>
    /// User có quyền Builder.Layout.Manage — hiện nút "Sửa shell" trên topbar. Sửa shell là sửa
    /// header/footer của MỌI trang nên chỉ ai có quyền layout mới thấy đường vào từ đây.
    /// </summary>
    public bool HasLayoutScope { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        if (!result.Succeeded) return NotFound();

        TokenCssUrl = $"/_nc/site/{_currentSite.Slug}-{await _tokenCss.GetCssHashAsync(ct)}.css";
        var siteCode = await _siteCode.GetAsync(ct);
        SiteCustomCss = siteCode.CustomCss ?? string.Empty;
        SiteCustomJs = siteCode.CustomJs ?? string.Empty;
        SiteHeadHtml = siteCode.HeadHtml ?? string.Empty;

        var page = result.Value!;

        // CSS shell gán cho trang: LayoutId null = shell mặc định của site (PageRenderer cũng
        // fallback vậy khi compose). Thiếu thì canvas preview trang không style.
        var layoutId = page.LayoutId;
        if (layoutId is null)
        {
            var defaultShell = await _layouts.ListAsync(ct);
            layoutId = defaultShell.FirstOrDefault(l => l.Kind == "Shell" && l.IsDefault)?.Id;
        }
        if (layoutId is { } lid)
        {
            var layout = await _layouts.GetByIdAsync(lid, ct);
            if (layout.Succeeded && layout.Value is { } l)
                LayoutCss = string.Join("\n", new[] { l.CompiledCss, l.CustomCss }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        PageId = page.Id;
        PageTitle = page.Title;
        Slug = page.Slug;
        Kind = page.Kind;
        Version = page.Version;
        LayoutId = page.LayoutId ?? Guid.Empty;
        StatusLabel = page.Status switch
        {
            "Published" => "Đã xuất bản",
            "Scheduled" => "Hẹn giờ",
            _ => "Nháp"
        };
        HasCodeScope = User.HasClaim(Permissions.Prefix, Permissions.Builder.CodeManage);
        HasSeoScope = User.HasClaim(Permissions.Prefix, Permissions.Seo.EditMeta);
        HasRestoreScope = User.HasClaim(Permissions.Prefix, Permissions.Builder.RevisionRestore);
        HasLayoutScope = User.HasClaim(Permissions.Prefix, Permissions.Builder.LayoutManage);

        return Page();
    }
}
