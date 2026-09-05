using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Content;

namespace NewsCMS.Theme.HaiLuuNguoc.Areas.Theme.Controllers;

[Area("Theme")]
public class HomeController : Controller
{
    private readonly IPostService _posts;

    public HomeController(IPostService posts)
    {
        _posts = posts;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var latest = await _posts.GetByCategoryAsync("van-de-bien", 3, ct);
        var featured = latest.Where(x => x.FeaturedImageUrl is not null).Take(3).ToArray();
        var viewModel = new HomeViewModel(featured, latest);
        return View(viewModel);
    }

    public IActionResult Contest() => View();
}

[Area("Theme")]
public class CategoryController : Controller
{
    private readonly IPostService _posts;
    public CategoryController(IPostService posts) => _posts = posts;

    public async Task<IActionResult> Index(string categorySlug, CancellationToken ct)
    {
        var items = await _posts.GetByCategoryAsync(categorySlug, 20, ct);
        ViewBag.CategorySlug = categorySlug;
        ViewBag.CategoryName = items.FirstOrDefault()?.CategoryName ?? categorySlug;
        return View(items);
    }
}

[Area("Theme")]
public class PostController : Controller
{
    private readonly IPostService _posts;

    public PostController(IPostService posts)
    {
        _posts = posts;
    }

    public async Task<IActionResult> Detail(string categorySlug, string postSlug, CancellationToken ct)
    {
        var post = await _posts.GetBySlugAsync(postSlug, ct);
        if (post is null || !string.Equals(post.CategorySlug, categorySlug, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        await _posts.IncreaseViewAsync(post.Id, ct);
        return View(post);
    }
}

[Area("Theme")]
public class PageController : Controller
{
    public IActionResult Detail(string slug)
    {
        ViewBag.Slug = slug;
        return View();
    }
}
