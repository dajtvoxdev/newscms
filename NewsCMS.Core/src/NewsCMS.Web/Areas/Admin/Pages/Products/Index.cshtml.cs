using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using NewsCMS.Application.Catalog;
using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Products;

[Authorize(Permissions.Catalog.ViewProduct)]
public class IndexModel : PageModel
{
    private readonly IProductService _svc;
    private readonly IProductCategoryService _categories;

    public IndexModel(IProductService svc, IProductCategoryService categories)
    {
        _svc = svc;
        _categories = categories;
    }

    [BindProperty(SupportsGet = true, Name = "q")] public string? Keyword { get; set; }
    [BindProperty(SupportsGet = true, Name = "cat")] public Guid? CategoryId { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int CurrentPage { get; set; } = 1;

    public PagedList<ProductListItemDto> Items { get; private set; } = PagedList<ProductListItemDto>.Empty();
    public List<SelectListItem> Categories { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _svc.SearchAsync(Keyword, CategoryId, CurrentPage, 20, ct);
        var cats = await _categories.GetAllAsync(ct);
        Categories = cats.Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == CategoryId)).ToList();
    }

    public async Task<IActionResult> OnPostTogglePublishAsync(Guid id, CancellationToken ct)
    {
        var result = await _svc.TogglePublishAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Đã cập nhật trạng thái sản phẩm." : result.Error;
        return RedirectToPage(new { q = Keyword, cat = CategoryId, p = CurrentPage });
    }
}
