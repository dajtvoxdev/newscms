namespace NewsCMS.Domain.Enums;

/// <summary>Loại đích của một SiteRoute — cho renderer biết phải render entity nào.</summary>
public enum RouteType
{
    Page = 0,
    Category = 1,
    Post = 2,
    Product = 3,
    /// <summary>Route tuỳ chỉnh (redirect nội bộ, trang đặc biệt không gắn entity chuẩn).</summary>
    Custom = 4
}
