namespace NewsCMS.Application.Ar.Dtos;

public record ArExperienceDto(
    Guid Id,
    string Title,
    string Slug,
    string? Description,
    string TargetImageUrl,
    string MindFileUrl,
    string VideoUrl,
    double VideoWidth,
    double VideoHeight,
    Guid? FallbackPostId,
    bool IsPublished,
    long ViewCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ArExperienceDetailDto(
    Guid Id,
    string Title,
    string Slug,
    string? Description,
    string TargetImageUrl,
    string MindFileUrl,
    string VideoUrl,
    double VideoWidth,
    double VideoHeight,
    string? FallbackPostTitle,
    string? FallbackPostContent);

public record ArExperienceUpsertDto(
    Guid? Id,
    string Title,
    string Slug,
    string? Description,
    double VideoWidth,
    double VideoHeight,
    Guid? FallbackPostId,
    bool IsPublished)
{
    // All three files come from _MediaPicker — stored as Media URLs
    public string? TargetImageUrl { get; init; }
    public string? MindFileUrl { get; init; }
    public string? VideoUrl { get; init; }
}
