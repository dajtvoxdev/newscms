using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Application.KeoBia;

public sealed record KeoBiaPublicHomeDto(
    IReadOnlyList<KeoBiaPublicMatchDto> FeaturedMatches,
    IReadOnlyList<KeoBiaPublicMatchDto> ScheduleMatches,
    IReadOnlyList<KeoBiaActivityFeedDto> RecentFeed,
    IReadOnlyList<KeoBiaChatMessageDto> CommunityChat,
    IReadOnlyList<KeoBiaPlayerListItemDto> Leaderboard);

public sealed record KeoBiaPublicMatchDto(
    Guid Id,
    string ExternalId,
    string Stage,
    string HomeName,
    string HomeCode,
    string HomePrimary,
    string HomeSecondary,
    string AwayName,
    string AwayCode,
    string AwayPrimary,
    string AwaySecondary,
    DateTime KickoffAt,
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
    string Status,
    int? HomeScore,
    int? AwayScore,
    int? ResultHomeScore,
    int? ResultAwayScore,
    int? PenaltyHomeScore,
    int? PenaltyAwayScore,
    string? ResultChoice,
    int HomeBettors,
    int DrawBettors,
    int AwayBettors,
    string? CorrectScoreOddsJson = null)
{
    // Cached so views / Telegram / analysis do not all need to re-import the Domain helper.
    public bool AllowsDraw => KeoBiaStage.AllowsDraw(KickoffAt);
    public string UnitCode => KeoBiaUnitRules.GetUnitCode(Stage, KickoffAt);
    public string UnitLabel => KeoBiaUnitRules.FullLabel(UnitCode);
    public string UnitShortLabel => KeoBiaUnitRules.ShortLabel(UnitCode);
}

public sealed record KeoBiaPlayerListItemDto(
    Guid Id,
    string PublicKey,
    string DisplayName,
    string? AvatarUrl,
    DateTime CreatedAt,
    DateTime LastSeenAt,
    int TotalBets,
    int TotalCups,
    int CorrectBets,
    int WrongBets,
    bool IsBlocked,
    int HopeStars,
    int DevilStars,
    int PenaltyCups,
    int MissedMatchCredit,
    int SharedCups,
    long? TelegramUserId)
{
    public int TotalBeerCups { get; init; }
    public int TotalPeanutPacks { get; init; }
    public int OutstandingBeerCups { get; init; }
    public int OutstandingPeanutPacks { get; init; }
    public int SharedBeerCups { get; init; }
    public int SharedPeanutPacks { get; init; }
    public int MissedCreditBeerCups { get; init; }
    public int MissedCreditPeanutPacks { get; init; }
    public DateTime? StoppedPlayingAt { get; init; }
}

public sealed record KeoBiaPlayerLossLeaderboardDto(
    Guid Id,
    string PublicKey,
    string DisplayName,
    string? AvatarUrl,
    string? TelegramUsername,
    int TotalBets,
    int TotalCups,
    int CorrectBets,
    int WrongBets,
    int LostCups,
    int MissedMatches,
    int SharedCups,
    int ReceivedCups,
    int StarItemsUsed,
    int PromoCups,
    int StarCupEffect,
    int QuizRewardCups = 0,
    int PaidBeerCups = 0,
    int LostBeerCups = 0,
    int LostPeanutPacks = 0,
    int PaidLegacyBeerCups = 0,
    int PaidPeanutPacks = 0,
    int SharedBeerCups = 0,
    int SharedPeanutPacks = 0,
    int ReceivedBeerCups = 0,
    int ReceivedPeanutPacks = 0,
    int PromoBeerCups = 0,
    int PromoPeanutPacks = 0,
    int StarBeerCupEffect = 0,
    int StarPeanutPackEffect = 0,
    int QuizRewardBeerCups = 0,
    int QuizRewardPeanutPacks = 0,
    int QuizPenaltyBeerCups = 0,
    int QuizPenaltyPeanutPacks = 0,
    int CorrectScoreRewardBeerCups = 0,
    int CorrectScoreRewardPeanutPacks = 0,
    int CorrectScorePenaltyBeerCups = 0,
    int CorrectScorePenaltyPeanutPacks = 0,
    DateTime? StoppedPlayingAt = null)
{
    // Paid units remain part of the historical loss used for the public ranking.
    public int HistoricalLostBeerCups => LostBeerCups + PaidLegacyBeerCups;
    public int HistoricalLostPeanutPacks => LostPeanutPacks + PaidPeanutPacks;
    public int HistoricalLostCups => HistoricalLostBeerCups + HistoricalLostPeanutPacks;
}

public sealed record KeoBiaPlayerPresenceDto(
    Guid PlayerId,
    string PublicKey,
    string DisplayName,
    string? AvatarUrl,
    bool IsNewPlayer,
    long? TelegramUserId = null,
    string? TelegramUsername = null);

public sealed record KeoBiaTelegramLinkDto(
    string PublicKey,
    string DisplayName,
    string? AvatarUrl,
    IReadOnlyDictionary<string, string> Fields,
    string? IpAddress,
    string? UserAgent);

/// <summary>
/// Identity already verified out of the OIDC id_token (signature/iss/aud/exp/nonce
/// validated by the OIDC service). The link service just attaches it to the player.
/// </summary>
public sealed record KeoBiaTelegramOidcLinkDto(
    string PublicKey,
    string DisplayName,
    string? AvatarUrl,
    long TelegramUserId,
    string? Username,
    string? FirstName,
    string? LastName,
    string? PhotoUrl,
    string? IpAddress,
    string? UserAgent);

public sealed record KeoBiaTelegramBotIdentityDto(
    long TelegramUserId,
    string? Username,
    string? FirstName,
    string? LastName,
    string? PhotoUrl);

public sealed record KeoBiaPlayerClaimCandidateDto(
    Guid PlayerId,
    string PublicKey,
    string DisplayName,
    string? AvatarUrl);

public sealed record KeoBiaPlayerProfileUpsertResultDto(
    KeoBiaPlayerPresenceDto? Player,
    bool RequiresClaimConfirmation,
    bool IsClaimedPlayer,
    KeoBiaPlayerClaimCandidateDto? ClaimCandidate);

public sealed record KeoBiaMatchListItemDto(
    Guid Id,
    string ExternalId,
    string Stage,
    string HomeName,
    string HomeCode,
    string AwayName,
    string AwayCode,
    DateTime KickoffAt,
    string Venue,
    bool IsHot,
    string Status,
    int BetCount,
    int CupCount,
    int? HomeScore,
    int? AwayScore,
    int? PenaltyHomeScore,
    int? PenaltyAwayScore,
    string? ResultChoice,
    string? CorrectScoreOddsJson = null)
{
    public string UnitCode => KeoBiaUnitRules.GetUnitCode(Stage, KickoffAt);
    public string UnitLabel => KeoBiaUnitRules.FullLabel(UnitCode);
    public string UnitShortLabel => KeoBiaUnitRules.ShortLabel(UnitCode);
}

public record KeoBiaBetFeedDto(
    Guid Id,
    string PlayerName,
    string? AvatarUrl,
    string MatchLabel,
    string Choice,
    string ChoiceLabel,
    int Cups,
    DateTime CreatedAt,
    bool IsSettled,
    bool? IsCorrect,
    string MatchStage = "",
    DateTime? MatchKickoffAt = null)
{
    public string UnitCode => KeoBiaUnitRules.GetUnitCode(MatchStage, MatchKickoffAt ?? CreatedAt);
    public string UnitLabel => KeoBiaUnitRules.FullLabel(UnitCode);
}

public sealed record KeoBiaPlayerMatchPredictionDto(
    Guid MatchId,
    string Choice,
    string ChoiceLabel,
    int Cups,
    string? StarType,
    int? PredictedHomeScore = null,
    int? PredictedAwayScore = null,
    decimal? CorrectScoreOdds = null,
    int? ResultHomeScore = null,
    int? ResultAwayScore = null);

public sealed record KeoBiaMatchPredictionHistoryItemDto(
    Guid Id,
    string PlayerName,
    string? AvatarUrl,
    string Choice,
    string ChoiceLabel,
    int Cups,
    DateTime CreatedAt,
    string? StarType = null,
    int? PredictedHomeScore = null,
    int? PredictedAwayScore = null,
    decimal? CorrectScoreOdds = null,
    int? ResultHomeScore = null,
    int? ResultAwayScore = null,
    string UnitCode = KeoBiaUnitCode.Beer);

public sealed record KeoBiaMatchPredictionHistoryDto(
    Guid MatchId,
    string MatchLabel,
    int TotalEntries,
    int TotalCups,
    IReadOnlyList<KeoBiaMatchPredictionHistoryItemDto> Items,
    string UnitCode = KeoBiaUnitCode.Beer);

public sealed record KeoBiaPlayerPredictionHistoryItemDto(
    Guid Id,
    Guid MatchId,
    string MatchLabel,
    string Choice,
    string ChoiceLabel,
    int Cups,
    DateTime CreatedAt,
    string MatchStatus,
    int? HomeScore,
    int? AwayScore,
    string? ResultChoice,
    string? ResultLabel,
    bool IsSettled,
    bool? IsCorrect,
    bool CanDelete,
    string? DeleteLockedReason,
    bool IsMissed = false,
    string? StarType = null,
    int? PredictedHomeScore = null,
    int? PredictedAwayScore = null,
    decimal? CorrectScoreOdds = null,
    int? ResultHomeScore = null,
    int? ResultAwayScore = null,
    string UnitCode = KeoBiaUnitCode.Beer);

public sealed record KeoBiaPlayerHistoryDto(
    Guid PlayerId,
    string PublicKey,
    string DisplayName,
    string? AvatarUrl,
    int TotalBets,
    int TotalCups,
    int WonCups,
    decimal LostCups,
    int PendingCups,
    int CorrectBets,
    int WrongBets,
    IReadOnlyList<KeoBiaPlayerPredictionHistoryItemDto> Items,
    int QuizRewardCups = 0,
    int PaidBeerCups = 0,
    int MissedMatches = 0,
    int PromoCups = 0,
    int HopeStarCupEffect = 0,
    int DevilStarCupEffect = 0,
    int QuizPenaltyCups = 0,
    int CorrectScorePenaltyCups = 0,
    int CorrectScoreRewardCups = 0,
    int OutstandingBeerCups = 0,
    int OutstandingPeanutPacks = 0,
    int PaidLegacyBeerCups = 0,
    int PaidPeanutPacks = 0,
    int TotalBeerCups = 0,
    int TotalPeanutPacks = 0,
    int PendingBeerCups = 0,
    int PendingPeanutPacks = 0,
    int PromoBeerCups = 0,
    int PromoPeanutPacks = 0,
    int HopeStarBeerCupEffect = 0,
    int HopeStarPeanutPackEffect = 0,
    int DevilStarBeerCupEffect = 0,
    int DevilStarPeanutPackEffect = 0,
    int QuizRewardBeerCups = 0,
    int QuizRewardPeanutPacks = 0,
    int QuizPenaltyBeerCups = 0,
    int QuizPenaltyPeanutPacks = 0,
    int CorrectScorePenaltyBeerCups = 0,
    int CorrectScorePenaltyPeanutPacks = 0,
    int CorrectScoreRewardBeerCups = 0,
    int CorrectScoreRewardPeanutPacks = 0);

public sealed record KeoBiaShareBeerDto(
    string FromPublicKey,
    string ToPublicKey,
    int Cups);

public sealed record KeoBiaShareBeerResultDto(
    string FromDisplayName,
    string ToDisplayName,
    int Cups,
    int FromNewLostCups,
    int ToNewLostCups,
    int FromLostBeerCups = 0,
    int FromLostPeanutPacks = 0,
    int ToLostBeerCups = 0,
    int ToLostPeanutPacks = 0);

public sealed record KeoBiaChangelogDto(
    string Version,
    string Title,
    string Content,
    DateTime CreatedAt);

public sealed record KeoBiaCupLogDto(
    Guid Id,
    string ChangeType,
    int Cups,
    string Reason,
    int BalanceAfter,
    DateTime CreatedAt,
    string? MatchLabel,
    string UnitCode = KeoBiaUnitCode.Beer,
    int BalanceAfterBeerCups = 0,
    int BalanceAfterPeanutPacks = 0);

public static class KeoBiaCupChangeType
{
    public const string WrongBet = "wrong_bet";
    public const string CorrectBet = "correct_bet";
    public const string MissedMatch = "missed_match";
    public const string MissedMatchCredit = "missed_credit";
    public const string SharedBeer = "shared_beer";
    public const string ReceivedBeer = "received_beer";
    public const string HopeStarWin = "hope_star_win";
    public const string HopeStarLose = "hope_star_lose";
    public const string DevilStarWin = "devil_star_win";
    public const string DevilStarLose = "devil_star_lose";
    public const string UsedHopeStar = "used_hope_star";
    public const string UsedDevilStar = "used_devil_star";
    public const string AdminAdjust = "admin_adjust";
    // Reward for a correct quiz answer — negative Cups, reduces the player's lost-beer.
    public const string QuizCorrect = "quiz_correct";
    public const string QuizWrong = "quiz_wrong";
    public const string PaidBeer = "paid_beer";
    public const string CorrectScore = "correct_score";
}

public sealed record KeoBiaBeerPaymentPromptDto(
    string DisplayName,
    int OutstandingCups,
    IReadOnlyList<int> SuggestedCups,
    int OutstandingBeerCups = 0,
    int OutstandingPeanutPacks = 0);

public sealed record KeoBiaBeerPaymentDto(
    Guid Id,
    string Code,
    string DisplayName,
    string? TelegramUsername,
    long? TelegramUserId,
    int Cups,
    int CoinAmount,
    string Status,
    string QrUrl,
    string TransferContent,
    string? ProviderTransactionId,
    DateTime? PaidAt,
    DateTime CreatedAt,
    int BeerCups = 0,
    int PeanutPacks = 0);

public sealed record KeoBiaBeerPaymentWebhookResultDto(
    bool WasAlreadyPaid,
    KeoBiaBeerPaymentDto Payment);

public sealed record KeoBiaTelegramResultNotificationDto(
    Guid BetId,
    long TelegramUserId,
    string PlayerName,
    Guid MatchId,
    string Stage,
    string HomeName,
    string HomeCode,
    string AwayName,
    string AwayCode,
    DateTime KickoffAt,
    string Venue,
    int? HomeScore,
    int? AwayScore,
    int? ResultHomeScore,
    int? ResultAwayScore,
    int? PenaltyHomeScore,
    int? PenaltyAwayScore,
    string ResultChoice,
    string ResultChoiceLabel,
    string Choice,
    string ChoiceLabel,
    int Cups,
    bool IsCorrect,
    DateTime SettledAt,
    string? StarType,
    int? PredictedHomeScore,
    int? PredictedAwayScore,
    decimal? CorrectScoreOdds)
{
    public string UnitCode => KeoBiaUnitRules.GetUnitCode(Stage, KickoffAt);
}

public sealed record KeoBiaActivityFeedDto(
    Guid Id,
    string ActivityType,
    string PlayerName,
    string? AvatarUrl,
    string Text,
    string Badge,
    string Choice,
    string ChoiceLabel,
    int? Cups,
    DateTime CreatedAt);

public sealed record KeoBiaSubmitBetResultDto(
    KeoBiaPlayerPresenceDto Player,
    KeoBiaActivityFeedDto Activity);

public sealed record KeoBiaDeleteBetResultDto(
    Guid BetId,
    Guid MatchId,
    string Choice,
    int Cups,
    IReadOnlyList<Guid> ActivityIds,
    KeoBiaPlayerHistoryDto History);

public sealed record KeoBiaChatMessageDto(
    Guid Id,
    Guid PlayerId,
    string PublicKey,
    string PlayerName,
    string? AvatarUrl,
    string? Message,
    string? ImageUrl,
    DateTime CreatedAt);

public sealed record KeoBiaImportJobDto(
    Guid Id,
    string Source,
    DateTime CreatedAt,
    int TotalRows,
    int ImportedRows,
    int SkippedRows,
    int ErrorRows,
    string? ErrorLog);

public sealed record KeoBiaPlayerUpsertDto(
    string PublicKey,
    string DisplayName,
    string? AvatarUrl,
    string? IpAddress,
    string? UserAgent,
    bool ClaimExisting,
    Guid? ClaimPlayerId);

public sealed record KeoBiaUnitTransitionNoticeDto(
    bool ShouldShow,
    bool IsBlocked,
    bool HasStopped,
    DateTime? AcknowledgedAt);

public sealed record KeoBiaUnitTransitionDecisionDto(
    bool IsBlocked,
    bool HasStopped,
    DateTime AcknowledgedAt);

public sealed record KeoBiaChatMessageCreateDto(
    string PublicKey,
    string DisplayName,
    string? AvatarUrl,
    string? Message,
    string? ImageUrl,
    string? IpAddress,
    string? UserAgent);

public sealed record KeoBiaSubmitBetDto(
    string PublicKey,
    string DisplayName,
    string? AvatarUrl,
    Guid MatchId,
    string Choice,
    int Cups,
    string? IpAddress,
    string? UserAgent,
    string? StarType = null,
    int? PredictedHomeScore = null,
    int? PredictedAwayScore = null,
    decimal? CorrectScoreOdds = null);

public sealed record KeoBiaMatchUpsertDto(
    Guid? Id,
    string ExternalId,
    string Stage,
    string HomeName,
    string HomeCode,
    string HomePrimary,
    string HomeSecondary,
    string AwayName,
    string AwayCode,
    string AwayPrimary,
    string AwaySecondary,
    DateTime KickoffAt,
    string Venue,
    bool IsHot,
    string? HotLabel,
    string Status,
    int BaseHomeWeight,
    int BaseDrawWeight,
    int BaseAwayWeight,
    int AiHome,
    int AiDraw,
    int AiAway,
    string? AiSummary,
    int DefaultCups,
    string? CorrectScoreOddsJson = null);

public sealed record KeoBiaImportResultDto(
    int TotalRows,
    int ImportedRows,
    int SkippedRows,
    int ErrorRows,
    string? ErrorLog);

public sealed record KeoBiaAnalysisProbabilityDto(
    int Home,
    int Draw,
    int Away,
    string HomeLabel,
    string DrawLabel,
    string AwayLabel);

public sealed record KeoBiaMatchAnalysisDto(
    string Title,
    string Content,
    string Source,
    bool FromCache,
    DateTime? GeneratedAt,
    KeoBiaAnalysisProbabilityDto? Probability);

public sealed record KeoBiaAutoAssignedBetDto(
    long TelegramUserId,
    string DisplayName,
    Guid MatchId,
    string HomeName,
    string AwayName,
    string Choice,
    string ChoiceLabel,
    string UnitCode = KeoBiaUnitCode.Beer);

public sealed record KeoBiaAutoAssignResultDto(
    IReadOnlyList<KeoBiaAutoAssignedBetDto> Notifications,
    IReadOnlyList<KeoBiaActivityFeedDto> Activities);

// ---- Quick Q&A (Hỏi đáp nhanh) ----

public sealed record KeoBiaQuestionChoiceDto(string Key, string Label);

public sealed record KeoBiaPublicQuestionChoiceDto(string Key, string Label, int VoteCount);

public sealed record KeoBiaCreateQuestionDto(
    string Text,
    IReadOnlyList<KeoBiaQuestionChoiceDto> Choices,
    int RewardCups,
    int PenaltyCups,
    DateTime? ClosesAt);

public sealed record KeoBiaQuestionAdminDto(
    Guid Id,
    string Text,
    IReadOnlyList<KeoBiaQuestionChoiceDto> Choices,
    int RewardCups,
    int PenaltyCups,
    string? CorrectChoiceKey,
    string Status,
    DateTime? ClosesAt,
    DateTime? RevealedAt,
    DateTime CreatedAt,
    int TotalVotes,
    bool SentToTelegram)
{
    public string UnitCode => KeoBiaUnitRules.GetUnitCode(null, RevealedAt ?? DateTime.UtcNow);
    public string UnitLabel => KeoBiaUnitRules.FullLabel(UnitCode);
}

public sealed record KeoBiaPublicQuestionDto(
    Guid Id,
    string Text,
    int RewardCups,
    int PenaltyCups,
    string Status,
    DateTime? ClosesAt,
    string? CorrectChoiceKey,
    bool HasVoted,
    string? MyChoiceKey,
    IReadOnlyList<KeoBiaPublicQuestionChoiceDto> Choices,
    string UnitCode = KeoBiaUnitCode.Beer);

public sealed record KeoBiaQuestionVoterDto(
    string DisplayName,
    string? AvatarUrl,
    string ChoiceKey,
    DateTime CreatedAt);

public sealed record KeoBiaQuestionVotesDto(
    Guid QuestionId,
    string Text,
    int RewardCups,
    int PenaltyCups,
    string Status,
    string? CorrectChoiceKey,
    IReadOnlyList<KeoBiaPublicQuestionChoiceDto> Choices,
    IReadOnlyList<KeoBiaQuestionVoterDto> Voters,
    string UnitCode = KeoBiaUnitCode.Beer);

public sealed record KeoBiaRevealResultDto(
    int RewardedCount,
    int TotalCorrect,
    int TotalRewardCups,
    int PenalizedCount,
    int TotalPenaltyCups,
    string UnitCode = KeoBiaUnitCode.Beer);

public sealed record KeoBiaBalanceCreditResultDto(
    int AppliedCups,
    int BeerCups,
    int PeanutPacks,
    int OutstandingBeerCups,
    int OutstandingPeanutPacks);

public interface IKeoBiaService
{
    Task<KeoBiaPublicHomeDto> GetPublicHomeAsync(int featuredTake = 3, CancellationToken ct = default);
    Task<KeoBiaPublicMatchDto?> GetPublicMatchAsync(Guid id, CancellationToken ct = default);
    Task<string?> GetPlayerBetChoiceAsync(long telegramUserId, Guid matchId, CancellationToken ct = default);
    Task<PagedList<KeoBiaPlayerListItemDto>> SearchPlayersAsync(string? keyword, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<IReadOnlyList<KeoBiaPlayerLossLeaderboardDto>> GetLossLeaderboardAsync(int take = 1000, CancellationToken ct = default);
    Task<PagedList<KeoBiaMatchListItemDto>> SearchMatchesAsync(string? keyword, string? status, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<PagedList<KeoBiaBetFeedDto>> SearchBetsAsync(Guid? playerId, Guid? matchId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<IReadOnlyList<KeoBiaPlayerMatchPredictionDto>> GetPlayerMatchPredictionsAsync(string publicKey, CancellationToken ct = default);
    Task<Result<KeoBiaMatchPredictionHistoryDto>> GetMatchPredictionHistoryAsync(Guid matchId, string? choice = null, int take = 100, CancellationToken ct = default);
    Task<Result<KeoBiaPlayerHistoryDto>> GetPlayerHistoryAsync(string publicKey, int take = 100, CancellationToken ct = default);
    Task<Result<KeoBiaDeleteBetResultDto>> DeletePlayerBetAsync(string publicKey, Guid betId, CancellationToken ct = default);
    Task<IReadOnlyList<KeoBiaImportJobDto>> GetRecentImportJobsAsync(int take = 10, CancellationToken ct = default);
    Task<Result<Guid>> UpsertMatchAsync(KeoBiaMatchUpsertDto dto, CancellationToken ct = default);
    Task<Result> UpdateResultAsync(Guid id, int? homeScore, int? awayScore, string status, CancellationToken ct = default, int? penaltyHomeScore = null, int? penaltyAwayScore = null);
    Task<Result> UpdateCorrectScoreOddsAsync(Guid id, string? oddsJson, CancellationToken ct = default);
    Task<Result<KeoBiaImportResultDto>> ImportScheduleCsvAsync(string csv, string source, CancellationToken ct = default);
    Task<Result<KeoBiaImportResultDto>> SyncOpenFootballWorldCupAsync(string json, string source, bool applyResults = true, CancellationToken ct = default);
    Task<Result<KeoBiaImportResultDto>> SyncFootballDataAsync(string json, string source, CancellationToken ct = default);
    Task<Result<string>> UploadAvatarAsync(Stream stream, string fileName, string contentType, long size, CancellationToken ct = default);
    Task<Result<string>> UploadChatImageAsync(Stream stream, string fileName, string contentType, long size, CancellationToken ct = default);
    Task<Result<KeoBiaPlayerProfileUpsertResultDto>> UpsertPlayerAsync(KeoBiaPlayerUpsertDto dto, CancellationToken ct = default);
    Task<Result<KeoBiaPlayerPresenceDto>> LinkTelegramAsync(KeoBiaTelegramLinkDto dto, CancellationToken ct = default);
    Task<Result<KeoBiaPlayerPresenceDto>> LinkTelegramViaOidcAsync(KeoBiaTelegramOidcLinkDto dto, CancellationToken ct = default);
    Task<Result<KeoBiaPlayerPresenceDto>> EnsureTelegramPlayerAsync(KeoBiaTelegramBotIdentityDto dto, CancellationToken ct = default);
    Task<Result<KeoBiaSubmitBetResultDto>> SubmitBetAsync(KeoBiaSubmitBetDto dto, CancellationToken ct = default);
    Task<Result<KeoBiaSubmitBetResultDto>> SubmitBetByTelegramIdAsync(long telegramUserId, Guid matchId, string choice, int cups, string? starType = null, int? predictedHomeScore = null, int? predictedAwayScore = null, decimal? correctScoreOdds = null, CancellationToken ct = default);
    Task<Result<KeoBiaPlayerHistoryDto>> GetPlayerHistoryByTelegramIdAsync(long telegramUserId, int take = 100, CancellationToken ct = default);
    Task<IReadOnlyList<KeoBiaPlayerMatchPredictionDto>> GetPlayerMatchPredictionsByTelegramIdAsync(long telegramUserId, CancellationToken ct = default);
    Task<Result<KeoBiaBeerPaymentPromptDto>> GetBeerPaymentPromptAsync(long telegramUserId, CancellationToken ct = default);
    Task<Result<KeoBiaBeerPaymentDto>> CreateBeerPaymentAsync(long telegramUserId, int cups, CancellationToken ct = default);
    Task<Result<KeoBiaBeerPaymentWebhookResultDto>> ConfirmBeerPaymentWebhookAsync(string payloadJson, string? suppliedSecret, CancellationToken ct = default);
    Task<PagedList<KeoBiaBeerPaymentDto>> SearchBeerPaymentsAsync(string? keyword, string? status, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<Result<KeoBiaBeerPaymentDto>> MarkBeerPaymentPaidAsync(Guid id, string? providerTransactionId = null, string? payloadJson = null, CancellationToken ct = default);
    Task<Result> MarkBeerPaymentFailedAsync(Guid id, CancellationToken ct = default);
    Task<int> GetBeerPaymentFundTotalAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KeoBiaBeerPaymentDto>> GetPublicBeerPaymentHistoryAsync(int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<KeoBiaTelegramResultNotificationDto>> GetTelegramResultNotificationsAsync(int take = 500, CancellationToken ct = default);
    Task<Result<KeoBiaChatMessageDto>> SendChatMessageAsync(KeoBiaChatMessageCreateDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<long>> GetMatchBettorTelegramIdsAsync(Guid matchId, CancellationToken ct = default);
    Task<KeoBiaAutoAssignResultDto> AutoAssignMissingBetsAsync(Guid matchId, CancellationToken ct = default);
    Task<IReadOnlyList<long>> GetAllTelegramUserIdsAsync(CancellationToken ct = default);
    Task<Result<KeoBiaUnitTransitionNoticeDto>> GetUnitTransitionNoticeAsync(string publicKey, CancellationToken ct = default);
    Task<Result<KeoBiaUnitTransitionDecisionDto>> RespondUnitTransitionNoticeAsync(string publicKey, bool stopPlaying, CancellationToken ct = default);
    Task<Result<KeoBiaUnitTransitionDecisionDto>> RespondUnitTransitionNoticeByTelegramIdAsync(long telegramUserId, bool stopPlaying, CancellationToken ct = default);
    Task<IReadOnlyList<long>> GetPendingUnitTransitionTelegramUserIdsAsync(int take = 200, CancellationToken ct = default);
    Task<Result> MarkUnitTransitionTelegramSentAsync(long telegramUserId, CancellationToken ct = default);
    Task<Result> StopPlayerAsync(Guid playerId, CancellationToken ct = default);
    Task<Result> TogglePlayerBlockAsync(Guid playerId, bool isBlocked, CancellationToken ct = default);
    Task<Result<KeoBiaShareBeerResultDto>> ShareBeerAsync(KeoBiaShareBeerDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<KeoBiaChangelogDto>> GetChangelogsAsync(int take = 3, CancellationToken ct = default);
    Task<string> BuildKeoBiaContextAsync(long? telegramUserId = null, CancellationToken ct = default);
    Task<string> ChatWithAiAsync(long telegramUserId, string message, CancellationToken ct = default);
    Task<string> ChatWithAiStreamingAsync(long telegramUserId, string message, Func<string, CancellationToken, Task> onDelta, CancellationToken ct = default);
    Task<IReadOnlyList<KeoBiaCupLogDto>> GetPlayerCupLogsAsync(Guid playerId, int take = 50, CancellationToken ct = default);
    Task<KeoBiaPlayerListItemDto?> GetPlayerAsync(Guid playerId, CancellationToken ct = default);
    Task<Result> UpdatePlayerStarsAsync(Guid playerId, int? hopeStars, int? devilStars, CancellationToken ct = default);
    Task<Result> AddMissedMatchCreditAsync(Guid playerId, int cups, CancellationToken ct = default);
    Task<Result<KeoBiaBalanceCreditResultDto>> AddBalanceCreditAsync(Guid playerId, int cups, CancellationToken ct = default);
    Task<Result> BulkAddStarAsync(string starType, int amount, CancellationToken ct = default);
    Task SeedCupLogsAsync(CancellationToken ct = default);
    Task<int> RejournalCupLogsAsync(CancellationToken ct = default);
    Task<Result<int>> CleanupOrphanedPlaceholderMatchesAsync(CancellationToken ct = default);

    // ---- Quick Q&A (Hỏi đáp nhanh) ----
    Task<IReadOnlyList<KeoBiaQuestionAdminDto>> GetQuestionsAsync(CancellationToken ct = default);
    Task<Result<KeoBiaQuestionAdminDto>> CreateQuestionAsync(KeoBiaCreateQuestionDto dto, CancellationToken ct = default);
    Task SetQuestionTelegramMessageAsync(Guid questionId, string chatId, long messageId, CancellationToken ct = default);
    Task<Result> CloseQuestionAsync(Guid questionId, CancellationToken ct = default);
    Task<Result<KeoBiaRevealResultDto>> RevealQuestionAnswerAsync(Guid questionId, string correctChoiceKey, CancellationToken ct = default);
    Task<Result> DeleteQuestionAsync(Guid questionId, CancellationToken ct = default);
    Task<IReadOnlyList<KeoBiaPublicQuestionDto>> GetActiveQuestionsForPublicAsync(string? publicKey, CancellationToken ct = default);
    Task<Result<KeoBiaQuestionVotesDto>> GetQuestionVotesAsync(Guid questionId, CancellationToken ct = default);
    Task<Result<KeoBiaPublicQuestionDto>> SubmitQuizVoteAsync(string publicKey, Guid questionId, string choiceKey, CancellationToken ct = default);
    Task<Result<KeoBiaPublicQuestionDto>> SubmitQuizVoteByTelegramIdAsync(long telegramUserId, Guid questionId, string choiceKey, CancellationToken ct = default);
}

public interface IKeoBiaOpenFootballSyncService
{
    Task<Result<KeoBiaImportResultDto>> SyncAsync(bool force = false, CancellationToken ct = default);
}

public interface IKeoBiaFootballDataSyncService
{
    Task<Result<KeoBiaImportResultDto>> SyncAsync(bool force = false, CancellationToken ct = default);
}

public interface IKeoBiaAnalysisService
{
    Task<Result<KeoBiaMatchAnalysisDto>> AnalyzeMatchAsync(Guid matchId, bool force = false, CancellationToken ct = default);
    Task<Result<int>> WarmUpcomingMatchesAsync(DateTime utcNow, CancellationToken ct = default);
}
