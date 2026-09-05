namespace NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026.Controllers;

public sealed record HomeViewModel(
    IReadOnlyList<HeroStatViewModel> HeroStats,
    IReadOnlyList<InsightChipViewModel> InsightChips,
    IReadOnlyList<MatchCardViewModel> FeaturedMatches,
    IReadOnlyList<MatchCardViewModel> ScheduleMatches,
    IReadOnlyList<FeedItemViewModel> RecentFeed,
    IReadOnlyList<CommunityChatMessageViewModel> CommunityChat,
    IReadOnlyList<LossLeaderboardEntryViewModel> LossLeaderboard,
    IReadOnlyList<LeaderboardEntryViewModel> Leaderboard,
    string Version);

public sealed record HeroStatViewModel(string Value, string Label, string Icon);

public sealed record InsightChipViewModel(string Label, string Value, string Tone);

public sealed record MatchCardViewModel(
    string Id,
    string Stage,
    string HomeName,
    string HomeCode,
    string HomePrimary,
    string HomeSecondary,
    string AwayName,
    string AwayCode,
    string AwayPrimary,
    string AwaySecondary,
    string KickoffDisplay,
    string Venue,
    bool IsHot,
    string HotLabel,
    int CommunityHome,
    int CommunityDraw,
    int CommunityAway,
    int AiHome,
    int AiDraw,
    int AiAway,
    string AiSummary,
    int DefaultCups,
    DateTime KickoffAt,
    string KickoffIso,
    string Status,
    int HomeBettors,
    int DrawBettors,
    int AwayBettors,
    int? HomeScore,
    int? AwayScore,
    int? ResultHomeScore,
    int? ResultAwayScore,
    int? PenaltyHomeScore,
    int? PenaltyAwayScore,
    string? ResultChoice,
    bool AllowsDraw,
    string? CorrectScoreOddsJson)
{
    public bool IsFinished => string.Equals(Status, "Finished", StringComparison.OrdinalIgnoreCase)
        && HomeScore.HasValue && AwayScore.HasValue;

    // Distinct players who picked an outcome other than the actual result.
    public int WrongBettors => !IsFinished || string.IsNullOrEmpty(ResultChoice)
        ? 0
        : (ResultChoice == "home" ? 0 : HomeBettors)
          + (ResultChoice == "draw" ? 0 : DrawBettors)
          + (ResultChoice == "away" ? 0 : AwayBettors);

    public string ResultWinnerLabel => ResultChoice switch
    {
        "home" => HomeName,
        "away" => AwayName,
        "draw" => "Hòa",
        _ => ""
    };

    public string ResultScoreLabel => ResultHomeScore.HasValue && ResultAwayScore.HasValue
        ? $"{ResultHomeScore}-{ResultAwayScore}"
        : string.Empty;

    public string FullScoreLabel => HomeScore.HasValue && AwayScore.HasValue
        ? $"{HomeScore}-{AwayScore}"
        : string.Empty;

    public string PenaltyScoreLabel => PenaltyHomeScore.HasValue && PenaltyAwayScore.HasValue
        ? $"pen {PenaltyHomeScore}-{PenaltyAwayScore}"
        : string.Empty;

    public bool HasResultScore => !string.IsNullOrWhiteSpace(ResultScoreLabel);

    public string UnitCode => NewsCMS.Application.KeoBia.KeoBiaUnitRules.GetUnitCode(Stage, KickoffAt);
    public string UnitLabel => NewsCMS.Application.KeoBia.KeoBiaUnitRules.FullLabel(UnitCode);
    public string UnitShortLabel => NewsCMS.Application.KeoBia.KeoBiaUnitRules.ShortLabel(UnitCode);

    public string DisplayScoreLabel => HasResultScore ? ResultScoreLabel : FullScoreLabel;

    public string ResultTagLabel => HasResultScore ? "90p" : "Kết quả";

    public string FinalScoreLabel => HasResultScore && (FullScoreLabel != ResultScoreLabel || !string.IsNullOrWhiteSpace(PenaltyScoreLabel))
        ? $"Chung cuộc: {FullScoreLabel}{(string.IsNullOrWhiteSpace(PenaltyScoreLabel) ? string.Empty : $" ({PenaltyScoreLabel})")}"
        : string.Empty;

    public string ResultSummaryLabel
    {
        get
        {
            var winner = ResultChoice == "draw" ? "Hai đội hoà" : $"{ResultWinnerLabel} thắng";
            return !HasResultScore
                ? winner
                : $"90p: {HomeName} {ResultScoreLabel} {AwayName}";
        }
    }
}

public sealed record FeedItemViewModel(
    string Id,
    string AvatarAsset,
    string Text,
    string TimeAgo,
    string Choice,
    string ChoiceLabel,
    string Badge, string CreatedAtIso);

public sealed record CommunityChatMessageViewModel(
    string Id,
    string PlayerId,
    string PublicKey,
    string PlayerName,
    string AvatarAsset,
    string? Message,
    string? ImageUrl,
    string TimeLabel,
    string CreatedAtIso);

public sealed record LossLeaderboardEntryViewModel(
    int Rank,
    string PlayerId,
    string PublicKey,
    string Name,
    string AvatarAsset,
    string? TelegramUsername,
    int LostCups,
    int WrongBets,
    int TotalBets,
    int TotalCups,
    int MissedMatches,
    int SharedCups,
    int ReceivedCups,
    int StarItemsUsed,
    int PromoCups,
    int StarCupEffect,
    int QuizRewardCups,
    int PaidBeerCups,
    int LostBeerCups,
    int LostPeanutPacks,
    int PaidLegacyBeerCups,
    int PaidPeanutPacks,
    int SharedBeerCups,
    int SharedPeanutPacks,
    int ReceivedBeerCups,
    int ReceivedPeanutPacks,
    int PromoBeerCups,
    int PromoPeanutPacks,
    int StarBeerCupEffect,
    int StarPeanutPackEffect,
    int QuizRewardBeerCups,
    int QuizRewardPeanutPacks,
    int QuizPenaltyBeerCups,
    int QuizPenaltyPeanutPacks,
    int CorrectScoreRewardBeerCups,
    int CorrectScoreRewardPeanutPacks,
    int CorrectScorePenaltyBeerCups,
    int CorrectScorePenaltyPeanutPacks,
    DateTime? StoppedPlayingAt);

public sealed record LeaderboardEntryViewModel(int Rank, string Name, string AvatarAsset, int WinRate, int Streak, int CupsOwed);
