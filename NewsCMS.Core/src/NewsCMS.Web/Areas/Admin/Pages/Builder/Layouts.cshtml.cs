using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

[Authorize(Permissions.Builder.LayoutManage)]
public class LayoutsModel : PageModel
{
    public void OnGet() { }
}
