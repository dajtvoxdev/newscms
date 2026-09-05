using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Ar;
using NewsCMS.Application.Ar.Dtos;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.ArExperiences;

[Authorize(Permissions.Ar.DeleteAr)]
public class DeleteModel : PageModel
{
    private readonly IArExperienceService _ar;

    public DeleteModel(IArExperienceService ar) => _ar = ar;

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public ArExperienceDto? Item { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Item = await _ar.GetByIdAsync(Id, ct);
        if (Item is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await _ar.DeleteAsync(Id, ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage("/ArExperiences/Index");
        }

        TempData["Success"] = "Đã xoá trải nghiệm AR.";
        return RedirectToPage("/ArExperiences/Index");
    }
}
