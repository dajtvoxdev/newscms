using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaMatch : BaseEntity, ISoftDelete, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string ExternalId { get; set; } = default!;
    public string Stage { get; set; } = default!;
    public string HomeName { get; set; } = default!;
    public string HomeCode { get; set; } = default!;
    public string HomePrimary { get; set; } = "#1f7a3a";
    public string HomeSecondary { get; set; } = "#176030";
    public string AwayName { get; set; } = default!;
    public string AwayCode { get; set; } = default!;
    public string AwayPrimary { get; set; } = "#1565c0";
    public string AwaySecondary { get; set; } = "#0a335f";
    public DateTime KickoffAt { get; set; }
    public string Venue { get; set; } = default!;
    public bool IsHot { get; set; }
    public string? HotLabel { get; set; }
    public string Status { get; set; } = KeoBiaMatchStatus.Scheduled;
    public int BaseHomeWeight { get; set; } = 40;
    public int BaseDrawWeight { get; set; } = 20;
    public int BaseAwayWeight { get; set; } = 40;
    public int AiHome { get; set; }
    public int AiDraw { get; set; }
    public int AiAway { get; set; }
    public string? AiSummary { get; set; }
    public string? AiAnalysisContent { get; set; }
    public string? AiAnalysisProbabilityJson { get; set; }
    public string? AiAnalysisSource { get; set; }
    public string? CorrectScoreOddsJson { get; set; }
    public DateTime? AiAnalysisGeneratedAt { get; set; }
    public DateTime? AiAnalysisExpiresAt { get; set; }
    public int DefaultCups { get; set; } = 1;
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public int? ResultHomeScore { get; set; }
    public int? ResultAwayScore { get; set; }
    public int? PenaltyHomeScore { get; set; }
    public int? PenaltyAwayScore { get; set; }
    public string? ResultChoice { get; set; }
    public DateTime? ResultUpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<KeoBiaBet> Bets { get; set; } = new List<KeoBiaBet>();
}

public static class KeoBiaMatchStatus
{
    public const string Scheduled = "Scheduled";
    public const string Live = "Live";
    public const string Finished = "Finished";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All = [Scheduled, Live, Finished, Cancelled];
}

public static class KeoBiaBetChoice
{
    public const string Home = "home";
    public const string Draw = "draw";
    public const string Away = "away";

    public static readonly string[] All = [Home, Draw, Away];
}

public static class KeoBiaStage
{
    // Stage strings are persisted as-is from the football-data API and may differ in casing.
    public static readonly string[] KnockoutStages =
    [
        "Round of 32",
        "Round of 16",
        "Quarter-final",
        "Semi-final",
        "Match for third place",
        "Final"
    ];

    public static bool AllowsDraw(string? stage) => true;

    public static bool AllowsDraw(DateTime kickoffAt) => kickoffAt >= new DateTime(2026, 7, 2, 19, 0, 0, DateTimeKind.Utc);

    public static bool IsKnockout(string? stage) =>
        !string.IsNullOrWhiteSpace(stage)
        && KnockoutStages.Contains(stage.Trim(), StringComparer.OrdinalIgnoreCase);
}
