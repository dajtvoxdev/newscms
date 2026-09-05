using NewsCMS.Domain.Enums;

namespace NewsCMS.Application.Content.Dtos;

public record PostDto(
    Guid Id,
    string Title,
    string Slug,
    string? Excerpt,
    string Content,
    PostStatus Status,
    DateTime? PublishedAt,
    long ViewCount,
    bool IsFeatured,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    Guid? FeaturedImageId,
    string? FeaturedImageUrl,
    IReadOnlyList<string> TagSlugs
);

public record PostListItemDto(
    Guid Id,
    string Title,
    string Slug,
    string? Excerpt,
    DateTime? PublishedAt,
    string CategoryName,
    string CategorySlug,
    string? FeaturedImageUrl
);

public record PostCreateDto(
    string Title,
    string Slug,
    string? Excerpt,
    string Content,
    Guid CategoryId,
    Guid? FeaturedImageId,
    bool IsFeatured,
    PostStatus Status,
    DateTime? PublishedAt,
    IReadOnlyList<Guid>? TagIds
);

public record PostUpdateDto(
    Guid Id,
    string Title,
    string Slug,
    string? Excerpt,
    string Content,
    Guid CategoryId,
    Guid? FeaturedImageId,
    bool IsFeatured,
    PostStatus Status,
    DateTime? PublishedAt,
    IReadOnlyList<Guid>? TagIds
);
