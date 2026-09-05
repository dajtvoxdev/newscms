using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Ar;

namespace NewsCMS.Theme.HaiLuuNguoc.Areas.Theme.Controllers;

[Area("Theme")]
public class ArController : Controller
{
    private readonly IArExperienceService _ar;
    private readonly IArScannerService _scanner;

    public ArController(IArExperienceService ar, IArScannerService scanner)
    {
        _ar = ar;
        _scanner = scanner;
    }

    // Universal scanner: one camera that recognises ANY published target image and
    // plays the matching video in place. No slug needed — used by the navbar link
    // and the floating camera button.
    public async Task<IActionResult> Entry(CancellationToken ct)
    {
        var data = await _scanner.GetScannerDataAsync(ct);
        if (data is null) return View("ComingSoon");
        return View("Scanner", data);
    }

    public async Task<IActionResult> Index(string slug, CancellationToken ct)
    {
        var model = await _ar.GetBySlugAsync(slug, ct);
        if (model is null) return NotFound();
        return View(model);
    }

    public async Task<IActionResult> Fallback(string slug, CancellationToken ct)
    {
        var model = await _ar.GetBySlugAsync(slug, ct);
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ViewPing(string slug, CancellationToken ct)
    {
        var model = await _ar.GetBySlugAsync(slug, ct);
        if (model is not null)
            await _ar.IncrementViewAsync(model.Id, ct);
        return Ok();
    }
}
