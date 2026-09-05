using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Ar;

public class ArExperience : AuditableEntity, ISoftDelete, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Description { get; set; }

    public string TargetImageUrl { get; set; } = default!;
    public string MindFileUrl { get; set; } = default!;
    public string VideoUrl { get; set; } = default!;

    public double VideoWidth { get; set; } = 1.0;
    public double VideoHeight { get; set; } = 0.5625;

    public Guid? FallbackPostId { get; set; }

    public bool IsPublished { get; set; }
    public long ViewCount { get; set; }

    // ISoftDelete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
