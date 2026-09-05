using Microsoft.AspNetCore.Mvc;

namespace NewsCMS.Theme.Default.Areas.Theme.Controllers;

[Area("Theme")]
public class HomeController : Controller
{
    // Inject IPostService, IMenuService, IBannerService... khi đã có implementation
    public IActionResult Index() => View();
}

[Area("Theme")]
public class CategoryController : Controller
{
    public IActionResult Index(string categorySlug)
    {
        ViewBag.CategorySlug = categorySlug;
        return View();
    }
}

[Area("Theme")]
public class PostController : Controller
{
    public IActionResult Detail(string categorySlug, string postSlug)
    {
        ViewBag.CategorySlug = categorySlug;
        ViewBag.PostSlug = postSlug;
        return View();
    }
}
