using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Catalog;

public class ProductVariant : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public string Sku { get; set; } = default!;
    public string Name { get; set; } = default!;

    public decimal? Price { get; set; }
    public decimal? SalePrice { get; set; }

    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
