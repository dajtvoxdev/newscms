using NewsCMS.Domain.Enums;

namespace NewsCMS.Application.Catalog.Dtos;

public record ProductVariantDto(
    Guid Id,
    string Sku,
    string Name,
    decimal? Price,
    decimal? SalePrice,
    int StockQuantity,
    bool IsActive,
    int SortOrder);

public record ProductImageDto(
    Guid Id,
    string Url,
    string? AltText,
    int SortOrder);

public record ProductListItemDto(
    Guid Id,
    string Name,
    string Slug,
    string Sku,
    decimal Price,
    decimal? SalePrice,
    int StockQuantity,
    bool IsTrackingStock,
    ProductStatus Status,
    bool IsFeatured,
    int SortOrder,
    string CategoryName,
    string CategorySlug,
    string? ThumbnailUrl,
    DateTime CreatedAt);

public record ProductDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string Sku,
    string? ShortDescription,
    string Description,
    decimal Price,
    decimal? SalePrice,
    int StockQuantity,
    bool IsTrackingStock,
    ProductStatus Status,
    DateTime? PublishedAt,
    bool IsFeatured,
    int SortOrder,
    long ViewCount,
    Guid ProductCategoryId,
    string CategoryName,
    string CategorySlug,
    Guid? ThumbnailMediaId,
    string? ThumbnailUrl,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<ProductVariantDto> Variants);

public record ProductVariantUpsertDto(
    Guid? Id,
    string Sku,
    string Name,
    decimal? Price,
    decimal? SalePrice,
    int StockQuantity,
    bool IsActive,
    int SortOrder);

public record ProductImageUpsertDto(
    Guid? Id,
    string Url,
    string? AltText,
    int SortOrder);

public record ProductUpsertDto(
    Guid? Id,
    string Name,
    string Slug,
    string Sku,
    string? ShortDescription,
    string Description,
    decimal Price,
    decimal? SalePrice,
    int StockQuantity,
    bool IsTrackingStock,
    ProductStatus Status,
    DateTime? PublishedAt,
    bool IsFeatured,
    int SortOrder,
    Guid ProductCategoryId,
    Guid? ThumbnailMediaId,
    IReadOnlyList<ProductImageUpsertDto> Images,
    IReadOnlyList<ProductVariantUpsertDto> Variants);
