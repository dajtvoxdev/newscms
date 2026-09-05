namespace NewsCMS.Application.Catalog.Dtos;

public record ProductCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    int Order,
    bool IsActive,
    Guid? ParentId,
    string? ParentName,
    int ProductCount,
    Guid? TemplatePageId = null);

public record ProductCategoryUpsertDto(
    Guid? Id,
    string Name,
    string Slug,
    string? Description,
    int Order,
    bool IsActive,
    Guid? ParentId,
    Guid? TemplatePageId = null);
