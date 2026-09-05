using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Catalog;

public class ProductCategory : AuditableEntity, ISoftDelete, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Description { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid? ParentId { get; set; }
    public ProductCategory? Parent { get; set; }
    public ICollection<ProductCategory> Children { get; set; } = new List<ProductCategory>();

    public ICollection<Product> Products { get; set; } = new List<Product>();

    // ── Mở rộng cho Site Builder (theme Universal) — đối xứng với Content.Category ─────
    /// <summary>
    /// Path đầy đủ theo cây chuyên mục, dùng làm prefix URL sản phẩm thay cho hằng "/san-pham".
    /// Null = dùng prefix mặc định của site, nên cột này để trống thì URL hiện tại không đổi.
    /// </summary>
    public string? PathSlug { get; set; }
    /// <summary>Trang builder dùng làm template listing cho chuyên mục sản phẩm này.</summary>
    public Guid? TemplatePageId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
