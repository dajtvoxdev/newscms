using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Catalog;
using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.ProductCategories;

[Authorize(Permissions.Catalog.ManageCategory)]
public class IndexModel : PageModel
{
    private readonly IProductCategoryService _svc;
    public IndexModel(IProductCategoryService svc) => _svc = svc;

    public IReadOnlyList<ProductCategoryDto> Items { get; private set; } = Array.Empty<ProductCategoryDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _svc.GetAllAsync(ct);
    }
}
