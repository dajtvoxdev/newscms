using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// Form sửa metadata + custom CSS/JS của một layout. HTML của layout dựng bằng builder shell
/// (cùng GrapesJS) — trang này chỉ cho sửa tên/loại và code, không thay chỗ dựng cấu trúc.
/// </summary>
[Authorize(Permissions.Builder.LayoutManage)]
public class EditLayoutModel : PageModel
{
    public Guid Id { get; private set; }

    /// <summary>User có quyền Builder.Code.Manage — ẩn field JavaScript khi thiếu.</summary>
    public bool HasCodeScope { get; private set; }

    public void OnGet(Guid id)
    {
        Id = id;
        HasCodeScope = User.HasClaim(Permissions.Prefix, Permissions.Builder.CodeManage);
    }
}
