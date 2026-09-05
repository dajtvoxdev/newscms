using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Catalog;

namespace NewsCMS.Theme.PhuPhucYaka.Areas.PhuPhucYaka.Controllers;

[Area("PhuPhucYaka")]
public class HomeController : Controller
{
    private readonly IProductService _products;

    public HomeController(IProductService products)
    {
        _products = products;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var featured = await _products.GetFeaturedAsync(3, ct);
        return View(new HomeViewModel(featured));
    }
}
