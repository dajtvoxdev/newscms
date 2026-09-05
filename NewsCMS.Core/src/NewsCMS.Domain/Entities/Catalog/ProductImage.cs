using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Catalog;

public class ProductImage : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public string Url { get; set; } = default!;
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
}
