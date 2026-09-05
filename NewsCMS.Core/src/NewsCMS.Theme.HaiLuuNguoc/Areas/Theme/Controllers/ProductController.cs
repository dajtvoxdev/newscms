using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Catalog;

namespace NewsCMS.Theme.HaiLuuNguoc.Areas.Theme.Controllers;

[Area("Theme")]
public class ProductController : Controller
{
    private readonly IProductService _products;
    private readonly IProductCategoryService _categories;

    public ProductController(IProductService products, IProductCategoryService categories)
    {
        _products = products;
        _categories = categories;
    }

    public async Task<IActionResult> Index(string? cat, int p = 1, CancellationToken ct = default)
    {
        var categories = await _categories.GetAllAsync(ct);
        Guid? catId = null;
        if (!string.IsNullOrWhiteSpace(cat))
        {
            var match = categories.FirstOrDefault(c => string.Equals(c.Slug, cat, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                ViewBag.Categories = categories;
                ViewBag.SelectedCategorySlug = cat;
                return View(NewsCMS.Application.Common.PagedList<NewsCMS.Application.Catalog.Dtos.ProductListItemDto>.Empty(p, 20));
            }
            catId = match.Id;
        }

        var items = await _products.SearchPublishedAsync(catId, p, 20, ct);
        ViewBag.Categories = categories;
        ViewBag.SelectedCategorySlug = cat;
        return View(items);
    }

    public async Task<IActionResult> Detail(string slug, CancellationToken ct)
    {
        var item = await _products.GetBySlugAsync(slug, ct);
        if (item is null) return NotFound();
        await _products.IncrementViewAsync(item.Id, ct);
        return View(item);
    }
}
