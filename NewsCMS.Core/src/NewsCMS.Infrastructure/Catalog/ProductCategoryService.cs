using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Catalog;
using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Catalog;

public sealed class ProductCategoryService : IProductCategoryService
{
    private readonly AppDbContext _db;
    private readonly SlugHelper _slug;

    public ProductCategoryService(AppDbContext db, SlugHelper slug)
    {
        _db = db;
        _slug = slug;
    }

    public async Task<IReadOnlyList<ProductCategoryDto>> GetAllAsync(CancellationToken ct = default) =>
        await _db.ProductCategories.AsNoTracking()
            .OrderBy(x => x.Order).ThenBy(x => x.Name)
            .Select(x => new ProductCategoryDto(
                x.Id, x.Name, x.Slug, x.Description, x.Order, x.IsActive,
                x.ParentId, x.Parent == null ? null : x.Parent.Name,
                x.Products.Count,
                x.TemplatePageId))
            .ToListAsync(ct);

    public Task<ProductCategoryDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.ProductCategories.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ProductCategoryDto(
                x.Id, x.Name, x.Slug, x.Description, x.Order, x.IsActive,
                x.ParentId, x.Parent == null ? null : x.Parent.Name,
                x.Products.Count,
                x.TemplatePageId))
            .FirstOrDefaultAsync(ct);

    public async Task<Result<Guid>> CreateAsync(ProductCategoryUpsertDto dto, CancellationToken ct = default)
    {
        var slug = NormalizeSlug(dto.Slug, dto.Name);
        if (await _db.ProductCategories.AnyAsync(x => x.Slug == slug, ct))
            return Result<Guid>.Failure("Slug đã tồn tại.");

        var entity = new ProductCategory
        {
            Name = dto.Name.Trim(),
            Slug = slug,
            Description = dto.Description?.Trim(),
            Order = dto.Order,
            IsActive = dto.IsActive,
            ParentId = dto.ParentId,
            TemplatePageId = dto.TemplatePageId
        };
        _db.ProductCategories.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(ProductCategoryUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.Id is null) return Result.Failure("Thiếu Id.");
        var entity = await _db.ProductCategories.FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy chuyên mục.");

        var slug = NormalizeSlug(dto.Slug, dto.Name);
        if (await _db.ProductCategories.AnyAsync(x => x.Slug == slug && x.Id != dto.Id, ct))
            return Result.Failure("Slug đã tồn tại.");

        if (dto.ParentId == entity.Id)
            return Result.Failure("Không thể chọn chính nó làm danh mục cha.");

        entity.Name = dto.Name.Trim();
        entity.Slug = slug;
        entity.Description = dto.Description?.Trim();
        entity.Order = dto.Order;
        entity.IsActive = dto.IsActive;
        entity.ParentId = dto.ParentId;
        entity.TemplatePageId = dto.TemplatePageId;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ProductCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy chuyên mục.");
        if (await _db.Products.AnyAsync(p => p.ProductCategoryId == id, ct))
            return Result.Failure("Không thể xoá: vẫn còn sản phẩm thuộc chuyên mục này.");
        if (await _db.ProductCategories.AnyAsync(c => c.ParentId == id, ct))
            return Result.Failure("Không thể xoá: vẫn còn chuyên mục con.");

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private string NormalizeSlug(string? raw, string fallback) =>
        string.IsNullOrWhiteSpace(raw) ? _slug.Generate(fallback) : raw.Trim().ToLowerInvariant();
}
