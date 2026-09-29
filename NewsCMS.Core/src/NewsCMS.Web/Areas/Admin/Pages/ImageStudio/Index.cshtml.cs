using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.ImageStudio;

/// <summary>Xưởng ảnh AI: lịch sử ảnh đã tạo của site + nút mở modal tạo ảnh.</summary>
[Authorize(Permissions.ImageStudio.View)]
public class IndexModel : PageModel
{
    public const int PageSize = 20;

    private readonly IImageStudioService _studio;
    private readonly UserManager<AppUser> _users;

    public IndexModel(IImageStudioService studio, UserManager<AppUser> users)
    {
        _studio = studio;
        _users = users;
    }

    public PagedList<ImageJobListItemDto> Jobs { get; private set; } = PagedList<ImageJobListItemDto>.Empty();

    public ImageStudioFormDto? Form { get; private set; }

    public ImageJobStatus? Status { get; private set; }

    public string? Keyword { get; private set; }

    public bool CanCreate { get; private set; }

    public async Task OnGetAsync(ImageJobStatus? status, string? q, int page = 1)
    {
        Status = status;
        Keyword = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        CanCreate = User.IsInRole("SuperAdmin") || User.HasClaim(Permissions.Prefix, Permissions.ImageStudio.Create);

        Jobs = await _studio.SearchAsync(Status, Keyword, page, PageSize);

        if (CanCreate)
        {
            Form = await _studio.GetFormAsync(Guid.TryParse(_users.GetUserId(User), out Guid id) ? id : Guid.Empty);
        }
    }
}
