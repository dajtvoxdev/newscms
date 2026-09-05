using NewsCMS.Domain.Common;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Domain.Entities.Catalog;

public class Product : AuditableEntity, ISoftDelete, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Sku { get; set; } = default!;

    public string? ShortDescription { get; set; }
    public string Description { get; set; } = default!;

    public decimal Price { get; set; }
    public decimal? SalePrice { get; set; }

    public bool IsTrackingStock { get; set; } = true;
    public int StockQuantity { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public DateTime? PublishedAt { get; set; }
    public bool IsFeatured { get; set; }
    public long ViewCount { get; set; }

    /// <summary>Thứ tự hiển thị trong danh sách (nhỏ trước). Cùng giá trị → xếp theo PublishedAt mới nhất.</summary>
    public int SortOrder { get; set; }

    public Guid ProductCategoryId { get; set; }
    public ProductCategory ProductCategory { get; set; } = default!;

    public Guid? ThumbnailMediaId { get; set; }
    public Media? Thumbnail { get; set; }

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
