using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NewsCMS.Web.Pages.Account;

[AllowAnonymous]
public class AccessDeniedModel : PageModel
{
}
