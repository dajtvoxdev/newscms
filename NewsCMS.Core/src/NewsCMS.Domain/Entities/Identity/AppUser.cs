using Microsoft.AspNetCore.Identity;
using NewsCMS.Domain.Entities.Site;

namespace NewsCMS.Domain.Entities.Identity;

public class AppUser : IdentityUser<Guid>
{
    public Guid? SiteId { get; set; }
    public NewsCMS.Domain.Entities.Site.Site? Site { get; set; }
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
    public bool IsSystem { get; set; }  // SuperAdmin role không cho xoá
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = default!;     // VD: Content.Post.Edit
    public string DisplayName { get; set; } = default!;
    public string Module { get; set; } = default!;
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public AppRole Role { get; set; } = default!;
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = default!;
}
