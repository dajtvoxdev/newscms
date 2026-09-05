using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaBet : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid PlayerId { get; set; }
    public KeoBiaPlayer Player { get; set; } = default!;
    public Guid MatchId { get; set; }
    public KeoBiaMatch Match { get; set; } = default!;
    public string Choice { get; set; } = default!;
    public int Cups { get; set; }
    public string? StarType { get; set; }
    public int? PredictedHomeScore { get; set; }
    public int? PredictedAwayScore { get; set; }
    public decimal? CorrectScoreOdds { get; set; }
    public bool IsSettled { get; set; }
    public bool? IsCorrect { get; set; }
    public DateTime? SettledAt { get; set; }
}
