using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Catalog;
using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Infrastructure.Catalog;

public sealed class ProductService : IProductService
{
    private readonly AppDbContext _db;
    private readonly SlugHelper _slug;
    private readonly ContentSanitizer _sanitizer;
    private readonly SiteCacheSignal _cacheSignal;
    private readonly IRouteRegistry _routeRegistry;

    public ProductService(AppDbContext db, SlugHelper slug, ContentSanitizer sanitizer,
        SiteCacheSignal cacheSignal, IRouteRegistry routeRegistry)
    {
        _db = db;
        _slug = slug;
        _sanitizer = sanitizer;
        _cacheSignal = cacheSignal;
        _routeRegistry = routeRegistry;
    }

    public async Task<PagedList<ProductListItemDto>> SearchAsync(
        string? keyword, Guid? categoryId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Products.AsNoTracking()
            .Include(x => x.ProductCategory)
            .Include(x => x.Thumbnail)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(x => x.Name.Contains(keyword) || x.Sku.Contains(keyword));

        if (categoryId.HasValue)
            query = query.Where(x => x.ProductCategoryId == categoryId.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            // Sắp theo đúng thứ tự hiển thị ngoài trang khách để admin nhìn danh sách
            // là biết ngay sản phẩm nào đứng trước.
            .OrderBy(x => x.SortOrder)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProductListItemDto(
                x.Id, x.Name, x.Slug, x.Sku, x.Price, x.SalePrice,
                x.StockQuantity, x.IsTrackingStock, x.Status, x.IsFeatured, x.SortOrder,
                x.ProductCategory.Name, x.ProductCategory.Slug,
                x.Thumbnail == null ? null : x.Thumbnail.FilePath,
                x.CreatedAt))
            .ToListAsync(ct);

        return new PagedList<ProductListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task<PagedList<ProductListItemDto>> SearchPublishedAsync(
        Guid? categoryId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = Published();
        if (categoryId.HasValue)
            query = query.Where(x => x.ProductCategoryId == categoryId.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.SortOrder).ThenByDescending(x => x.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapList())
            .ToListAsync(ct);

        return new PagedList<ProductListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public Task<ProductDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Query().Where(x => x.Id == id).Select(MapDetail()).FirstOrDefaultAsync(ct);

    public Task<ProductDetailDto?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Query()
            .Where(x => x.Slug == slug && x.Status == ProductStatus.Published)
            .Select(MapDetail())
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ProductListItemDto>> GetFeaturedAsync(int take = 8, CancellationToken ct = default) =>
        await Published()
            .Where(x => x.IsFeatured)
            .OrderBy(x => x.SortOrder).ThenByDescending(x => x.PublishedAt)
            .Take(take)
            .Select(MapList())
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProductListItemDto>> GetByCategoryAsync(string categorySlug, int take = 20, CancellationToken ct = default) =>
        await Published()
            .Where(x => x.ProductCategory.Slug == categorySlug)
            .OrderBy(x => x.SortOrder).ThenByDescending(x => x.PublishedAt)
            .Take(take)
            .Select(MapList())
            .ToListAsync(ct);

    public async Task<Result<Guid>> CreateAsync(ProductUpsertDto dto, CancellationToken ct = default)
    {
        var slug = NormalizeSlug(dto.Slug, dto.Name);
        if (await _db.Products.AnyAsync(x => x.Slug == slug, ct))
            return Result<Guid>.Failure("Slug đã tồn tại.");

        var sku = dto.Sku.Trim();
        if (await _db.Products.AnyAsync(x => x.Sku == sku, ct))
            return Result<Guid>.Failure("SKU đã tồn tại.");

        if (!await _db.ProductCategories.AnyAsync(c => c.Id == dto.ProductCategoryId, ct))
            return Result<Guid>.Failure("Chuyên mục không hợp lệ.");

        var entity = new Product
        {
            Name = dto.Name.Trim(),
            Slug = slug,
            Sku = sku,
            ShortDescription = dto.ShortDescription?.Trim(),
            Description = _sanitizer.Sanitize(dto.Description ?? string.Empty),
            Price = dto.Price,
            SalePrice = dto.SalePrice,
            StockQuantity = dto.StockQuantity,
            IsTrackingStock = dto.IsTrackingStock,
            Status = dto.Status,
            PublishedAt = dto.Status == ProductStatus.Published ? (dto.PublishedAt ?? DateTime.UtcNow) : null,
            IsFeatured = dto.IsFeatured,
            SortOrder = dto.SortOrder,
            ProductCategoryId = dto.ProductCategoryId,
            ThumbnailMediaId = dto.ThumbnailMediaId,
            Images = dto.Images.Select(MapImage).ToList(),
            Variants = dto.Variants.Select(MapVariant).ToList()
        };

        _db.Products.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Đảm bảo route /san-pham/{slug} tồn tại ngay khi sản phẩm publish (theme Universal).
        if (entity.Status == ProductStatus.Published)
            await _routeRegistry.SyncProductRouteAsync(entity.Id, ct);

        _cacheSignal.Invalidate();
        return Result<Guid>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(ProductUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.Id is null) return Result.Failure("Thiếu Id.");
        var entity = await _db.Products
            .Include(x => x.Images)
            .Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy sản phẩm.");

        var slug = NormalizeSlug(dto.Slug, dto.Name);
        if (await _db.Products.AnyAsync(x => x.Slug == slug && x.Id != dto.Id, ct))
            return Result.Failure("Slug đã tồn tại.");

        var sku = dto.Sku.Trim();
        if (await _db.Products.AnyAsync(x => x.Sku == sku && x.Id != dto.Id, ct))
            return Result.Failure("SKU đã tồn tại.");

        if (!await _db.ProductCategories.AnyAsync(c => c.Id == dto.ProductCategoryId, ct))
            return Result.Failure("Chuyên mục không hợp lệ.");

        entity.Name = dto.Name.Trim();
        entity.Slug = slug;
        entity.Sku = sku;
        entity.ShortDescription = dto.ShortDescription?.Trim();
        entity.Description = _sanitizer.Sanitize(dto.Description ?? string.Empty);
        entity.Price = dto.Price;
        entity.SalePrice = dto.SalePrice;
        entity.StockQuantity = dto.StockQuantity;
        entity.IsTrackingStock = dto.IsTrackingStock;
        entity.IsFeatured = dto.IsFeatured;
        entity.SortOrder = dto.SortOrder;
        entity.ProductCategoryId = dto.ProductCategoryId;
        entity.ThumbnailMediaId = dto.ThumbnailMediaId;
        entity.UpdatedAt = DateTime.UtcNow;

        var becamePublished = entity.Status != ProductStatus.Published && dto.Status == ProductStatus.Published;
        entity.Status = dto.Status;
        if (becamePublished) entity.PublishedAt = dto.PublishedAt ?? DateTime.UtcNow;
        else if (dto.Status != ProductStatus.Published) entity.PublishedAt = null;

        _db.ProductImages.RemoveRange(entity.Images);
        _db.ProductVariants.RemoveRange(entity.Variants);
        await _db.SaveChangesAsync(ct);

        foreach (var img in dto.Images)
        {
            var newImg = MapImage(img);
            newImg.ProductId = entity.Id;
            _db.ProductImages.Add(newImg);
        }
        foreach (var v in dto.Variants)
        {
            var newV = MapVariant(v);
            newV.ProductId = entity.Id;
            _db.ProductVariants.Add(newV);
        }
        await _db.SaveChangesAsync(ct);

        // Slug/status đổi → route /san-pham/{slug} phải theo kịp (tạo/đổi/redirect 301).
        await _routeRegistry.SyncProductRouteAsync(entity.Id, ct);

        _cacheSignal.Invalidate();
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy sản phẩm.");
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _routeRegistry.SyncProductRouteAsync(id, ct);
        _cacheSignal.Invalidate();
        return Result.Success();
    }

    public async Task<Result> TogglePublishAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy sản phẩm.");
        if (entity.Status == ProductStatus.Published)
        {
            entity.Status = ProductStatus.Draft;
            entity.PublishedAt = null;
        }
        else
        {
            entity.Status = ProductStatus.Published;
            entity.PublishedAt = entity.PublishedAt ?? DateTime.UtcNow;
        }
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _routeRegistry.SyncProductRouteAsync(id, ct);
        _cacheSignal.Invalidate();
        return Result.Success();
    }

    public async Task IncrementViewAsync(Guid id, CancellationToken ct = default) =>
        await _db.Products
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ViewCount, x => x.ViewCount + 1), ct);

    private IQueryable<Product> Query() =>
        _db.Products.AsNoTracking()
            .Include(x => x.ProductCategory)
            .Include(x => x.Thumbnail)
            .Include(x => x.Images)
            .Include(x => x.Variants);

    private IQueryable<Product> Published() =>
        _db.Products.AsNoTracking()
            .Include(x => x.ProductCategory)
            .Include(x => x.Thumbnail)
            .Where(x => x.Status == ProductStatus.Published && x.PublishedAt != null && x.ProductCategory.IsActive);

    private static System.Linq.Expressions.Expression<Func<Product, ProductListItemDto>> MapList() =>
        x => new ProductListItemDto(
            x.Id, x.Name, x.Slug, x.Sku, x.Price, x.SalePrice,
            x.StockQuantity, x.IsTrackingStock, x.Status, x.IsFeatured, x.SortOrder,
            x.ProductCategory.Name, x.ProductCategory.Slug,
            x.Thumbnail == null ? null : x.Thumbnail.FilePath,
            x.CreatedAt);

    private static System.Linq.Expressions.Expression<Func<Product, ProductDetailDto>> MapDetail() =>
        x => new ProductDetailDto(
            x.Id, x.Name, x.Slug, x.Sku, x.ShortDescription, x.Description,
            x.Price, x.SalePrice, x.StockQuantity, x.IsTrackingStock,
            x.Status, x.PublishedAt, x.IsFeatured, x.SortOrder, x.ViewCount,
            x.ProductCategoryId, x.ProductCategory.Name, x.ProductCategory.Slug,
            x.ThumbnailMediaId, x.Thumbnail == null ? null : x.Thumbnail.FilePath,
            x.Images.OrderBy(i => i.SortOrder)
                .Select(i => new ProductImageDto(i.Id, i.Url, i.AltText, i.SortOrder)).ToList(),
            x.Variants.OrderBy(v => v.SortOrder)
                .Select(v => new ProductVariantDto(v.Id, v.Sku, v.Name, v.Price, v.SalePrice, v.StockQuantity, v.IsActive, v.SortOrder)).ToList());

    private static ProductImage MapImage(ProductImageUpsertDto dto) => new()
    {
        Id = dto.Id ?? Guid.NewGuid(),
        Url = dto.Url.Trim(),
        AltText = dto.AltText?.Trim(),
        SortOrder = dto.SortOrder
    };

    private static ProductVariant MapVariant(ProductVariantUpsertDto dto) => new()
    {
        Id = dto.Id ?? Guid.NewGuid(),
        Sku = dto.Sku.Trim(),
        Name = dto.Name.Trim(),
        Price = dto.Price,
        SalePrice = dto.SalePrice,
        StockQuantity = dto.StockQuantity,
        IsActive = dto.IsActive,
        SortOrder = dto.SortOrder
    };

    private string NormalizeSlug(string? raw, string fallback) =>
        string.IsNullOrWhiteSpace(raw) ? _slug.Generate(fallback) : raw.Trim().ToLowerInvariant();
}
