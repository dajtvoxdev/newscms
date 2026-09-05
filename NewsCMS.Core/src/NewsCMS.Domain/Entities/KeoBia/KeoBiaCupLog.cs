using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaCupLog : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid? MatchId { get; set; }
    public string ChangeType { get; set; } = default!;
    public int Cups { get; set; }
    public string Reason { get; set; } = default!;
    public int BalanceAfter { get; set; }

    public KeoBiaPlayer Player { get; set; } = default!;
    public KeoBiaMatch? Match { get; set; }
}
