using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>Trang kiểm tra sức khỏe SEO (Phase 5): thiếu meta, trùng slug, trang mồ côi.</summary>
[Authorize(Permissions.Seo.EditMeta)]
public class SeoHealthModel : PageModel
{
    private readonly ISeoHealthService _service;

    public SeoHealthModel(ISeoHealthService service) => _service = service;

    public SeoHealthReport Report { get; set; } = default!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Report = await _service.CheckAsync(ct);
    }
}
