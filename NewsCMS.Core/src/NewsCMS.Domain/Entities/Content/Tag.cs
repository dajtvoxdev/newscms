using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Content;

public class Tag : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public ICollection<PostTag> PostTags { get; set; } = new List<PostTag>();
}

public class PostTag
{
    public Guid PostId { get; set; }
    public Post Post { get; set; } = default!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = default!;
}
