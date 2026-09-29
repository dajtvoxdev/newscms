using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.ImageStudio;

/// <summary>Chi tiết một lần tạo ảnh: prompt thật đã gửi, model, chi phí, các ảnh sinh ra.</summary>
[Authorize(Permissions.ImageStudio.View)]
public class DetailModel : PageModel
{
    private readonly IImageStudioService _studio;

    public DetailModel(IImageStudioService studio) => _studio = studio;

    public ImageJobDto Job { get; private set; } = default!;

    public bool CanCreate { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        ImageJobDto? job = await _studio.GetJobAsync(id);

        if (job is null)
        {
            return NotFound();
        }

        Job = job;
        CanCreate = User.IsInRole("SuperAdmin") || User.HasClaim(Permissions.Prefix, Permissions.ImageStudio.Create);

        return Page();
    }
}
