using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Application.Common;

namespace NewsCMS.Application.Catalog;

public interface IProductCategoryService
{
    Task<IReadOnlyList<ProductCategoryDto>> GetAllAsync(CancellationToken ct = default);
    Task<ProductCategoryDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<Guid>> CreateAsync(ProductCategoryUpsertDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(ProductCategoryUpsertDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}
