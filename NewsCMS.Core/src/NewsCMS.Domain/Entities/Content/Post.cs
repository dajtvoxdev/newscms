using NewsCMS.Domain.Common;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Domain.Entities.Content;

public class Post : AuditableEntity, ISoftDelete, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Excerpt { get; set; }
    public string Content { get; set; } = default!;

    public PostStatus Status { get; set; } = PostStatus.Draft;
    public DateTime? PublishedAt { get; set; }
    public long ViewCount { get; set; }
    public bool IsFeatured { get; set; }

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = default!;

    public Guid? FeaturedImageId { get; set; }
    public Media? FeaturedImage { get; set; }

    public Guid AuthorId { get; set; }

    public ICollection<PostTag> PostTags { get; set; } = new List<PostTag>();

    // ISoftDelete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
