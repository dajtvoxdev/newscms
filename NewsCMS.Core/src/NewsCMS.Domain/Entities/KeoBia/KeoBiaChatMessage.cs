using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaChatMessage : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid PlayerId { get; set; }
    public KeoBiaPlayer Player { get; set; } = default!;
    public string PlayerName { get; set; } = default!;
    public string? AvatarUrl { get; set; }
    public string? Message { get; set; }
    public string? ImageUrl { get; set; }
}
