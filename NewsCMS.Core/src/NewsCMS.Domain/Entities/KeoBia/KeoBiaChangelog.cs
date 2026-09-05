using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaChangelog : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string Version { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;
}
