using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Common;
using NewsCMS.Application.KeoBia;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.KeoBia;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace NewsCMS.Infrastructure.KeoBia;

public sealed class KeoBiaService : IKeoBiaService
{
    private const int MaxCups = 1;
    // Vote opens 1 day before kickoff and locks 20 minutes before kickoff.
    private static readonly TimeSpan VoteOpenWindow = TimeSpan.FromDays(1);
    private static readonly TimeSpan VoteLockWindow = TimeSpan.FromMinutes(20);
    // Vietnam has no DST, so a fixed +7 offset gives the local calendar day.
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);
    private const int MaxChatMessages = 100;
    private const decimal MaxCorrectScoreOdds = 3m;
    private const long MaxAvatarSizeBytes = 5L * 1024 * 1024;
    private const long MaxChatImageSizeBytes = 6L * 1024 * 1024;
    private const int MaxAvatarWidth = 512;
    private const int MaxAvatarHeight = 512;
    private const int MaxChatImageWidth = 1600;
    private const int MaxChatImageHeight = 1600;
    private const string AvatarSubFolder = "images/keobia-avatars";
    private const string ChatImageSubFolder = "images/keobia-chat";

    private static readonly Regex OpenFootballHeadlineTeamRegex = new(
        @"^(France|England|Spain|Portugal|Germany|Netherlands|Belgium|Norway|Argentina|Brazil|Uruguay|Colombia|USA|United States|Mexico|Japan|South Korea|Korea Republic|Morocco|Senegal)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const string TelegramAvatarHttpClientName = "keobia-telegram-avatar";

    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly KeoBiaTelegramOptions _telegram;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAiChatClient? _aiChatClient;
    private readonly IDataProtectionProvider? _dataProtection;
    private readonly KeoBiaAiTools? _aiTools;
    private readonly IEnumerable<IAiTool>? _externalTools;
    private readonly ICurrentSite? _currentSite;
    private readonly ILogger<KeoBiaService>? _logger;
    private readonly IConfiguration _configuration;

    public KeoBiaService(AppDbContext db, IFileStorage storage, IOptions<KeoBiaTelegramOptions> telegram, IHttpClientFactory httpClientFactory, IConfiguration configuration, IAiChatClient? aiChatClient = null, IDataProtectionProvider? dataProtection = null, KeoBiaAiTools? aiTools = null, IEnumerable<IAiTool>? externalTools = null, ICurrentSite? currentSite = null, ILogger<KeoBiaService>? logger = null)
    {
        _db = db;
        _storage = storage;
        _telegram = telegram.Value;
        _httpClientFactory = httpClientFactory;
        _aiChatClient = aiChatClient;
        _dataProtection = dataProtection;
        _aiTools = aiTools;
        _externalTools = externalTools;
        _currentSite = currentSite;
        _logger = logger;
        _configuration = configuration;
    }

    public static string HttpClientName => TelegramAvatarHttpClientName;

    public async Task<KeoBiaPublicHomeDto> GetPublicHomeAsync(int featuredTake = 3, CancellationToken ct = default)
    {
        if (featuredTake < 1) featuredTake = 3;

        var schedule = await _db.KeoBiaMatches.AsNoTracking()
            .Where(x => x.Status != KeoBiaMatchStatus.Cancelled)
            .ToListAsync(ct);

        var ids = schedule.Select(x => x.Id).ToArray();
        var stats = await GetMatchStatsAsync(ids, ct);
        var mapped = schedule
            .Select(x => MapPublicMatch(x, stats))
            .OrderByDescending(x => x.Status == KeoBiaMatchStatus.Finished)
            .ThenByDescending(x => x.Status == KeoBiaMatchStatus.Finished ? x.KickoffAt : DateTime.MinValue)
            .ThenByDescending(x => x.Status != KeoBiaMatchStatus.Finished && (x.HomeBettors + x.DrawBettors + x.AwayBettors) > 0)
            .ThenBy(x => x.Status != KeoBiaMatchStatus.Finished ? x.KickoffAt : DateTime.MaxValue)
            .ToList();
        var feed = await GetRecentActivityQuery()
            .Take(100)
            .ToListAsync(ct);
        var chat = await _db.KeoBiaChatMessages.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(MaxChatMessages)
            .Select(x => new KeoBiaChatMessageDto(
                x.Id,
                x.PlayerId,
                x.Player.PublicKey,
                x.PlayerName,
                x.AvatarUrl,
                x.Message,
                x.ImageUrl,
                x.CreatedAt))
            .ToListAsync(ct);
        chat.Reverse();
        var board = await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.TelegramUserId != null && !x.IsBlocked)
            .OrderByDescending(x => x.CorrectBets)
            .ThenByDescending(x => x.TotalCups)
            .ThenByDescending(x => x.LastSeenAt)
            .Take(10)
            .Select(x => new KeoBiaPlayerListItemDto(
                x.Id, x.PublicKey, x.DisplayName, x.AvatarUrl, x.CreatedAt, x.LastSeenAt,
                x.TotalBets, x.TotalCups, x.CorrectBets, x.WrongBets, x.IsBlocked,
                x.HopeStars, x.DevilStars, x.PenaltyCups, x.MissedMatchCredit, x.SharedCups, x.TelegramUserId))
            .ToListAsync(ct);

        return new KeoBiaPublicHomeDto(
            mapped.Take(featuredTake).ToList(),
            mapped,
            feed,
            chat,
            board);
    }

    public async Task<KeoBiaPublicMatchDto?> GetPublicMatchAsync(Guid id, CancellationToken ct = default)
    {
        var match = await _db.KeoBiaMatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.Status != KeoBiaMatchStatus.Cancelled, ct);
        if (match is null) return null;

        var stats = await GetMatchStatsAsync([match.Id], ct);
        return MapPublicMatch(match, stats);
    }

    public async Task<string?> GetPlayerBetChoiceAsync(long telegramUserId, Guid matchId, CancellationToken ct = default)
    {
        return await _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.Player.TelegramUserId == telegramUserId && x.MatchId == matchId)
            .Select(x => x.Choice)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PagedList<KeoBiaPlayerListItemDto>> SearchPlayersAsync(string? keyword, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePaging(ref page, ref pageSize);
        var query = _db.KeoBiaPlayers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();
            query = query.Where(x => x.DisplayName.Contains(keyword) || x.PublicKey.Contains(keyword));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new KeoBiaPlayerListItemDto(
                x.Id, x.PublicKey, x.DisplayName, x.AvatarUrl, x.CreatedAt, x.LastSeenAt,
                x.TotalBets, x.TotalCups, x.CorrectBets, x.WrongBets, x.IsBlocked,
                x.HopeStars, x.DevilStars, x.PenaltyCups, x.MissedMatchCredit, x.SharedCups, x.TelegramUserId)
            {
                StoppedPlayingAt = x.StoppedPlayingAt
            })
            .ToListAsync(ct);

        items = await EnrichPlayerUnitStatsAsync(items, ct);

        return new PagedList<KeoBiaPlayerListItemDto> { Items = items, Page = page, PageSize = pageSize, TotalItems = total };
    }

    public async Task<IReadOnlyList<KeoBiaPlayerLossLeaderboardDto>> GetLossLeaderboardAsync(int take = 1000, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 2000);

        var players = await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.TelegramUserId != null && (!x.IsBlocked || x.StoppedPlayingAt != null))
            .Select(x => new
            {
                x.Id, x.PublicKey, x.DisplayName, x.AvatarUrl, x.TelegramUsername,
                x.TotalBets, x.TotalCups, x.CorrectBets, x.WrongBets,
                x.PenaltyCups, x.SharedCups, x.StoppedPlayingAt,
                LostCups = (x.Bets.Where(b => b.IsSettled && b.IsCorrect == false).Sum(b => (int?)b.Cups) ?? 0) + x.PenaltyCups + x.SharedCups
            })
            .ToListAsync(ct);

        var ids = players.Select(x => x.Id).ToList();

        var cupLogStats = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId))
            .GroupBy(x => x.PlayerId)
            .Select(g => new
            {
                PlayerId = g.Key,
                // Sum (not Count) so reconcile reversal entries (negative cups) net out.
                ActualMissed = g.Where(x => x.ChangeType == KeoBiaCupChangeType.MissedMatch).Sum(x => x.Cups),
                ReceivedCups = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.ReceivedBeer).Sum(x => x.Cups),
                StarItemsUsed = g.Count(x => x.ChangeType == KeoBiaCupChangeType.UsedHopeStar || x.ChangeType == KeoBiaCupChangeType.UsedDevilStar),
                StarCupEffect = g.Where(x => x.ChangeType == KeoBiaCupChangeType.HopeStarWin || x.ChangeType == KeoBiaCupChangeType.HopeStarLose || x.ChangeType == KeoBiaCupChangeType.DevilStarWin || x.ChangeType == KeoBiaCupChangeType.DevilStarLose).Sum(x => x.Cups),
                PromoCups = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.MissedMatchCredit).Sum(x => x.Cups),
                // Quiz rewards are negative cup logs; Sum (not Abs) so reconcile reversals net out. Negate to a positive magnitude.
                QuizRewardCups = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.QuizCorrect).Sum(x => x.Cups),
                QuizPenaltyCups = g.Where(x => x.ChangeType == KeoBiaCupChangeType.QuizWrong).Sum(x => x.Cups),
                CorrectScoreReward = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.CorrectScore).Sum(x => x.Cups),
                PaidBeerCups = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.PaidBeer).Sum(x => x.Cups)
            })
            .ToListAsync(ct);

        var cupLogByPlayer = cupLogStats.ToDictionary(x => x.PlayerId);
        var rows = players.Select(x =>
        {
            cupLogByPlayer.TryGetValue(x.Id, out var log);
            var receivedCups = log?.ReceivedCups ?? 0;
            var quizRewardCups = log?.QuizRewardCups ?? 0;
            var quizPenaltyCups = log?.QuizPenaltyCups ?? 0;
            var correctScoreReward = log?.CorrectScoreReward ?? 0;
            var paidBeerCups = log?.PaidBeerCups ?? 0;
            var starCupEffect = log?.StarCupEffect ?? 0;
            var lostCups = Math.Max(0, x.LostCups + starCupEffect + quizPenaltyCups
                - receivedCups - quizRewardCups - paidBeerCups - correctScoreReward);
            return new
            {
                Player = x,
                Log = log,
                LostCups = lostCups,
                ReceivedCups = receivedCups,
                QuizRewardCups = quizRewardCups,
                PaidCups = paidBeerCups,
                StarCupEffect = starCupEffect
            };
        }).ToList();

        var lostUnitBalances = await ComputeLossUnitBalancesAsync(
            rows.ToDictionary(x => x.Player.Id, x => x.LostCups), ct);
        var paidUnitBalances = await GetPaidUnitBreakdownsAsync(
            rows.ToDictionary(x => x.Player.Id, x => x.PaidCups), ct);
        var unitEffects = await GetPlayerUnitEffectsAsync(ids, ct);

        return rows.Select(x =>
        {
            var lostUnits = lostUnitBalances.GetValueOrDefault(x.Player.Id, UnitBalance.Empty);
            var paidUnits = paidUnitBalances.GetValueOrDefault(x.Player.Id, UnitBalance.Empty);
            var effects = unitEffects.GetValueOrDefault(x.Player.Id, PlayerUnitEffects.Empty);
            var sharedUnits = ReconcileUnitBalance(
                x.Player.SharedCups, effects.Shared.BeerCups, effects.Shared.PeanutPacks);
            return new KeoBiaPlayerLossLeaderboardDto(
                x.Player.Id, x.Player.PublicKey, x.Player.DisplayName, x.Player.AvatarUrl, x.Player.TelegramUsername,
                x.Player.TotalBets, x.Player.TotalCups, x.Player.CorrectBets, x.Player.WrongBets,
                x.LostCups, x.Log?.ActualMissed ?? 0, x.Player.SharedCups,
                x.ReceivedCups,
                x.Log?.StarItemsUsed ?? 0,
                x.Log?.PromoCups ?? 0,
                x.StarCupEffect,
                x.QuizRewardCups,
                x.PaidCups,
                lostUnits.BeerCups,
                lostUnits.PeanutPacks,
                paidUnits.BeerCups,
                paidUnits.PeanutPacks,
                sharedUnits.BeerCups,
                sharedUnits.PeanutPacks,
                Math.Max(0, effects.Received.BeerCups),
                Math.Max(0, effects.Received.PeanutPacks),
                Math.Max(0, effects.Promo.BeerCups),
                Math.Max(0, effects.Promo.PeanutPacks),
                effects.Star.BeerCups,
                effects.Star.PeanutPacks,
                Math.Max(0, effects.QuizReward.BeerCups),
                Math.Max(0, effects.QuizReward.PeanutPacks),
                Math.Max(0, effects.QuizPenalty.BeerCups),
                Math.Max(0, effects.QuizPenalty.PeanutPacks),
                Math.Max(0, effects.CorrectScoreReward.BeerCups),
                Math.Max(0, effects.CorrectScoreReward.PeanutPacks),
                Math.Max(0, effects.CorrectScorePenalty.BeerCups),
                Math.Max(0, effects.CorrectScorePenalty.PeanutPacks),
                x.Player.StoppedPlayingAt);
        })
        .OrderByDescending(x => x.HistoricalLostCups)
        .ThenByDescending(x => x.WrongBets)
        .ThenByDescending(x => x.TotalCups)
        .ThenBy(x => x.DisplayName)
        .Take(take)
        .ToList();
    }

    public async Task<PagedList<KeoBiaMatchListItemDto>> SearchMatchesAsync(string? keyword, string? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePaging(ref page, ref pageSize);
        var query = _db.KeoBiaMatches.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();
            query = query.Where(x =>
                x.ExternalId.Contains(keyword) ||
                x.HomeName.Contains(keyword) ||
                x.AwayName.Contains(keyword) ||
                x.Stage.Contains(keyword));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            status = NormalizeStatus(status);
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(ct);
        var matches = await query
            .OrderBy(x => x.KickoffAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var ids = matches.Select(x => x.Id).ToArray();
        var stats = await GetMatchStatsAsync(ids, ct);
        var items = matches.Select(x =>
        {
            var stat = stats.TryGetValue(x.Id, out var s) ? s : MatchStats.Empty;
            return new KeoBiaMatchListItemDto(
                x.Id, x.ExternalId, x.Stage, x.HomeName, x.HomeCode, x.AwayName, x.AwayCode,
                x.KickoffAt, x.Venue, x.IsHot, x.Status, stat.BetCount, stat.CupCount,
                x.HomeScore, x.AwayScore, x.PenaltyHomeScore, x.PenaltyAwayScore, x.ResultChoice, x.CorrectScoreOddsJson);
        }).ToList();

        return new PagedList<KeoBiaMatchListItemDto> { Items = items, Page = page, PageSize = pageSize, TotalItems = total };
    }

    public async Task<PagedList<KeoBiaBetFeedDto>> SearchBetsAsync(Guid? playerId, Guid? matchId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePaging(ref page, ref pageSize);
        var query = GetRecentFeedQuery();
        if (playerId.HasValue)
            query = query.Where(x => x.PlayerId == playerId.Value);
        if (matchId.HasValue)
            query = query.Where(x => x.MatchId == matchId.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedList<KeoBiaBetFeedDto> { Items = items, Page = page, PageSize = pageSize, TotalItems = total };
    }

    public async Task<IReadOnlyList<KeoBiaPlayerMatchPredictionDto>> GetPlayerMatchPredictionsAsync(string publicKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
            return [];

        var key = publicKey.Trim();
        return await _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.Player.PublicKey == key && x.Player.TelegramUserId != null)
            .GroupBy(x => new
            {
                x.MatchId,
                x.Choice,
                x.Match.HomeCode,
                x.Match.AwayCode,
                x.PredictedHomeScore,
                x.PredictedAwayScore,
                x.CorrectScoreOdds
            })
            .Select(g => new KeoBiaPlayerMatchPredictionDto(
                g.Key.MatchId,
                g.Key.Choice,
                g.Key.Choice == KeoBiaBetChoice.Home ? g.Key.HomeCode : g.Key.Choice == KeoBiaBetChoice.Away ? g.Key.AwayCode : "Hòa",
                g.Sum(x => x.Cups),
                g.FirstOrDefault()!.StarType,
                g.Key.PredictedHomeScore,
                g.Key.PredictedAwayScore,
                g.Key.CorrectScoreOdds,
                null,
                null))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KeoBiaPlayerMatchPredictionDto>> GetPlayerMatchPredictionsByTelegramIdAsync(long telegramUserId, CancellationToken ct = default)
    {
        if (telegramUserId <= 0)
            return [];

        var player = await FindPlayerByTelegramIdAsync(telegramUserId, ct);
        return player is null
            ? []
            : await GetPlayerMatchPredictionsAsync(player.PublicKey, ct);
    }

    public async Task<Result<KeoBiaMatchPredictionHistoryDto>> GetMatchPredictionHistoryAsync(Guid matchId, string? choice = null, int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var normalizedChoice = string.IsNullOrWhiteSpace(choice) ? null : NormalizeChoice(choice);
        if (!string.IsNullOrWhiteSpace(choice) && normalizedChoice is null)
            return Result<KeoBiaMatchPredictionHistoryDto>.Failure("Bộ lọc lựa chọn không hợp lệ.");

        var match = await _db.KeoBiaMatches.AsNoTracking()
            .Where(x => x.Id == matchId)
            .Select(x => new
            {
                x.Id,
                MatchLabel = x.HomeName + " vs " + x.AwayName,
                x.Stage,
                x.KickoffAt
            })
            .FirstOrDefaultAsync(ct);
        if (match is null)
            return Result<KeoBiaMatchPredictionHistoryDto>.Failure("Không tìm thấy trận đấu.");
        var unitCode = KeoBiaUnitRules.GetUnitCode(match.Stage, match.KickoffAt);

        var query = _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.MatchId == matchId && x.Player.TelegramUserId != null && !x.Player.IsBlocked);
        if (normalizedChoice is not null)
            query = query.Where(x => x.Choice == normalizedChoice);

        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new { Entries = g.Count(), Cups = g.Sum(x => x.Cups) })
            .FirstOrDefaultAsync(ct);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new KeoBiaMatchPredictionHistoryItemDto(
                x.Id,
                x.Player.DisplayName,
                x.Player.AvatarUrl,
                x.Choice,
                x.Choice == KeoBiaBetChoice.Home ? x.Match.HomeCode : x.Choice == KeoBiaBetChoice.Away ? x.Match.AwayCode : "Hòa",
                x.Cups,
                x.CreatedAt,
                x.StarType,
                x.PredictedHomeScore,
                x.PredictedAwayScore,
                x.CorrectScoreOdds,
                null,
                null,
                unitCode))
            .ToListAsync(ct);

        return Result<KeoBiaMatchPredictionHistoryDto>.Success(new KeoBiaMatchPredictionHistoryDto(
            match.Id,
            match.MatchLabel,
            totals?.Entries ?? 0,
            totals?.Cups ?? 0,
            items,
            unitCode));
    }

    public async Task<Result<KeoBiaPlayerHistoryDto>> GetPlayerHistoryAsync(string publicKey, int take = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
            return Result<KeoBiaPlayerHistoryDto>.Failure("Thiếu mã publicKey.");

        take = Math.Clamp(take, 1, 100);
        var key = publicKey.Trim();
        var player = await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.PublicKey == key)
            .Select(x => new
            {
                x.Id,
                x.PublicKey,
                x.DisplayName,
                x.AvatarUrl,
                x.PenaltyCups,
                x.SharedCups
            })
            .FirstOrDefaultAsync(ct);

        if (player is null)
            return Result<KeoBiaPlayerHistoryDto>.Failure("Không tìm thấy người chơi.");

        var deleteCutoff = DateTime.UtcNow.Add(VoteLockWindow);
        var validBets = _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.Player.TelegramUserId != null);

        var summary = await validBets
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalBets = g.Count(),
                TotalCups = g.Sum(x => x.Cups),
                // Correct picks do not award cups; only wrong picks deduct the staked cups.
                WonCups = 0,
                PendingCups = g.Sum(x => !x.IsSettled ? x.Cups : 0),
                CorrectBets = g.Count(x => x.IsSettled && x.IsCorrect == true),
                WrongBets = g.Count(x => x.IsSettled && x.IsCorrect == false),
                // Any wrong pick loses the full cups staked — no half-cup on a draw.
                LostCups = g.Sum(x => x.IsSettled && x.IsCorrect == false ? x.Cups : 0)
            })
            .FirstOrDefaultAsync(ct);

        var betUnitRows = await validBets
            .Select(x => new { x.Cups, x.IsSettled, x.Match.Stage, x.Match.KickoffAt })
            .ToListAsync(ct);
        var totalBetUnits = UnitBalance.Empty;
        var pendingBetUnits = UnitBalance.Empty;
        foreach (var row in betUnitRows)
        {
            var unitCode = KeoBiaUnitRules.GetUnitCode(row.Stage, row.KickoffAt);
            totalBetUnits = AddUnit(totalBetUnits, row.Cups, unitCode);
            if (!row.IsSettled)
                pendingBetUnits = AddUnit(pendingBetUnits, row.Cups, unitCode);
        }

        var receivedCups = -(await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.ReceivedBeer)
            .SumAsync(x => x.Cups, ct));

        // Quiz rewards are negative cup logs (delta entries net out via Sum); negate to a magnitude.
        var quizRewardCups = -(await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.QuizCorrect)
            .SumAsync(x => x.Cups, ct));

        var paidBeerCups = -(await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.PaidBeer)
            .SumAsync(x => x.Cups, ct));

        var cupLogTotals = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                MissedMatches = g.Where(x => x.ChangeType == KeoBiaCupChangeType.MissedMatch).Sum(x => x.Cups),
                PromoCups = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.MissedMatchCredit).Sum(x => x.Cups),
                CorrectScoreReward = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.CorrectScore).Sum(x => x.Cups),
                CorrectScoreRewardCups = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.CorrectScore && x.Cups < 0).Sum(x => x.Cups),
                CorrectScorePenaltyCups = g.Where(x => x.ChangeType == KeoBiaCupChangeType.CorrectScore && x.Cups > 0).Sum(x => x.Cups),
                HopeStarCupEffect = g.Where(x => x.ChangeType == KeoBiaCupChangeType.HopeStarWin || x.ChangeType == KeoBiaCupChangeType.HopeStarLose).Sum(x => x.Cups),
                DevilStarCupEffect = g.Where(x => x.ChangeType == KeoBiaCupChangeType.DevilStarWin || x.ChangeType == KeoBiaCupChangeType.DevilStarLose).Sum(x => x.Cups)
            })
            .FirstOrDefaultAsync(ct);

        var missedMatchesCount = cupLogTotals?.MissedMatches ?? 0;
        var promoCups = cupLogTotals?.PromoCups ?? 0;
        var correctScoreReward = cupLogTotals?.CorrectScoreReward ?? 0;
        var correctScoreRewardCups = cupLogTotals?.CorrectScoreRewardCups ?? 0;
        var correctScorePenaltyCups = cupLogTotals?.CorrectScorePenaltyCups ?? 0;
        var hopeStarCupEffect = cupLogTotals?.HopeStarCupEffect ?? 0;
        var devilStarCupEffect = cupLogTotals?.DevilStarCupEffect ?? 0;
        var starCupEffect = hopeStarCupEffect + devilStarCupEffect;

        var quizPenaltyCups = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.QuizWrong)
            .SumAsync(x => x.Cups, ct);

        var lostCups = Math.Max(0, (summary?.LostCups ?? 0) + player.PenaltyCups + player.SharedCups + starCupEffect + quizPenaltyCups - receivedCups - quizRewardCups - paidBeerCups - correctScoreReward);

        var betMatchIds = await validBets
            .Select(x => x.MatchId)
            .Distinct()
            .ToArrayAsync(ct);

        var items = await validBets
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new KeoBiaPlayerPredictionHistoryItemDto(
                x.Id,
                x.MatchId,
                x.Match.HomeName + " vs " + x.Match.AwayName,
                x.Choice,
                x.Choice == KeoBiaBetChoice.Home ? x.Match.HomeCode : x.Choice == KeoBiaBetChoice.Away ? x.Match.AwayCode : "Hòa",
                x.Cups,
                x.CreatedAt,
                x.Match.Status,
                x.Match.HomeScore,
                x.Match.AwayScore,
                x.Match.ResultChoice,
                x.Match.ResultChoice == null ? null : x.Match.ResultChoice == KeoBiaBetChoice.Home ? x.Match.HomeCode : x.Match.ResultChoice == KeoBiaBetChoice.Away ? x.Match.AwayCode : "Hòa",
                x.IsSettled,
                x.IsCorrect,
                !x.IsSettled && x.Match.Status == KeoBiaMatchStatus.Scheduled && x.Match.KickoffAt > deleteCutoff,
                x.IsSettled
                    ? "Trận đã có kết quả, không thể xoá."
                    : x.Match.Status != KeoBiaMatchStatus.Scheduled
                        ? "Trận không còn mở, không thể xoá."
                        : x.Match.KickoffAt <= deleteCutoff
                            ? "Chỉ được xoá trước giờ bóng lăn tối thiểu 20 phút."
                            : null,
                false,
                x.StarType,
                x.PredictedHomeScore,
                x.PredictedAwayScore,
                x.CorrectScoreOdds,
                x.Match.ResultHomeScore ?? x.Match.HomeScore,
                x.Match.ResultAwayScore ?? x.Match.AwayScore,
                x.Match.KickoffAt >= KeoBiaUnitRules.PeanutSwitchAtUtc
                    ? KeoBiaUnitCode.Peanut
                    : KeoBiaUnitCode.Beer))
            .ToListAsync(ct);

        var missedMatches = await _db.KeoBiaMatches.AsNoTracking()
                .Where(x => x.Status == KeoBiaMatchStatus.Finished
                    && !betMatchIds.Contains(x.Id)
                    && x.KickoffAt < DateTime.UtcNow)
                .OrderByDescending(x => x.KickoffAt)
                .Take(take)
            .Select(x => new KeoBiaPlayerPredictionHistoryItemDto(
                Guid.Empty,
                x.Id,
                x.HomeName + " vs " + x.AwayName,
                "missed",
                "Không tham gia",
                0,
                x.KickoffAt,
                x.Status,
                x.HomeScore,
                x.AwayScore,
                x.ResultChoice,
                x.ResultChoice == null ? null : x.ResultChoice == KeoBiaBetChoice.Home ? x.HomeCode : x.ResultChoice == KeoBiaBetChoice.Away ? x.AwayCode : "Hòa",
                true,
                null,
                false,
                null,
                true,
                null,
                null,
                null,
                null,
                null,
                null,
                x.KickoffAt >= KeoBiaUnitRules.PeanutSwitchAtUtc
                    ? KeoBiaUnitCode.Peanut
                    : KeoBiaUnitCode.Beer))
            .ToListAsync(ct);

        items = items.Concat(missedMatches)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToList();

        var outstandingUnits = await ComputeLossUnitBalanceAsync(player.Id, (int)lostCups, ct);
        var paidUnits = await GetPaidUnitBreakdownAsync(player.Id, paidBeerCups, ct);
        var unitEffects = (await GetPlayerUnitEffectsAsync([player.Id], ct))
            .GetValueOrDefault(player.Id, PlayerUnitEffects.Empty);

        return Result<KeoBiaPlayerHistoryDto>.Success(new KeoBiaPlayerHistoryDto(
            player.Id,
            player.PublicKey,
            player.DisplayName,
            player.AvatarUrl,
            summary?.TotalBets ?? 0,
            summary?.TotalCups ?? 0,
            summary?.WonCups ?? 0,
            lostCups,
            summary?.PendingCups ?? 0,
            summary?.CorrectBets ?? 0,
            summary?.WrongBets ?? 0,
            items,
            quizRewardCups,
            paidBeerCups,
            missedMatchesCount,
            promoCups,
            hopeStarCupEffect,
            devilStarCupEffect,
            quizPenaltyCups,
            correctScorePenaltyCups,
            correctScoreRewardCups,
            outstandingUnits.BeerCups,
            outstandingUnits.PeanutPacks,
            paidUnits.BeerCups,
            paidUnits.PeanutPacks,
            totalBetUnits.BeerCups,
            totalBetUnits.PeanutPacks,
            pendingBetUnits.BeerCups,
            pendingBetUnits.PeanutPacks,
            Math.Max(0, unitEffects.Promo.BeerCups),
            Math.Max(0, unitEffects.Promo.PeanutPacks),
            unitEffects.HopeStar.BeerCups,
            unitEffects.HopeStar.PeanutPacks,
            unitEffects.DevilStar.BeerCups,
            unitEffects.DevilStar.PeanutPacks,
            Math.Max(0, unitEffects.QuizReward.BeerCups),
            Math.Max(0, unitEffects.QuizReward.PeanutPacks),
            Math.Max(0, unitEffects.QuizPenalty.BeerCups),
            Math.Max(0, unitEffects.QuizPenalty.PeanutPacks),
            Math.Max(0, unitEffects.CorrectScorePenalty.BeerCups),
            Math.Max(0, unitEffects.CorrectScorePenalty.PeanutPacks),
            Math.Max(0, unitEffects.CorrectScoreReward.BeerCups),
            Math.Max(0, unitEffects.CorrectScoreReward.PeanutPacks)));
    }

    public async Task<Result<KeoBiaPlayerHistoryDto>> GetPlayerHistoryByTelegramIdAsync(long telegramUserId, int take = 100, CancellationToken ct = default)
    {
        if (telegramUserId <= 0)
            return Result<KeoBiaPlayerHistoryDto>.Failure("Dữ liệu Telegram thiếu ID hợp lệ.");

        var player = await FindPlayerByTelegramIdAsync(telegramUserId, ct);
        return player is null
            ? Result<KeoBiaPlayerHistoryDto>.Failure("Không tìm thấy người chơi Telegram.")
            : await GetPlayerHistoryAsync(player.PublicKey, take, ct);
    }

    public async Task<Result<KeoBiaBeerPaymentPromptDto>> GetBeerPaymentPromptAsync(long telegramUserId, CancellationToken ct = default)
    {
        var disabledMessage = GetBeerPaymentDisabledMessage();
        if (disabledMessage is not null) return Result<KeoBiaBeerPaymentPromptDto>.Failure(disabledMessage);

        var player = await FindPlayerByTelegramIdAsync(telegramUserId, ct);
        if (player is null) return Result<KeoBiaBeerPaymentPromptDto>.Failure("Không tìm thấy người chơi Telegram.");

        var outstanding = await ComputeLossBalanceAsync(player, ct);
        var unitBalance = await ComputeLossUnitBalanceAsync(player.Id, outstanding, ct);
        var suggestions = new[] { 1, 2, 3, 5, 10 }
            .Where(x => x <= outstanding)
            .Append(outstanding)
            .Where(x => x > 0)
            .Distinct()
            .ToList();
        return Result<KeoBiaBeerPaymentPromptDto>.Success(new(
            player.DisplayName,
            outstanding,
            suggestions,
            unitBalance.BeerCups,
            unitBalance.PeanutPacks));
    }

    public async Task<Result<KeoBiaBeerPaymentDto>> CreateBeerPaymentAsync(long telegramUserId, int cups, CancellationToken ct = default)
    {
        var disabledMessage = GetBeerPaymentDisabledMessage();
        if (disabledMessage is not null) return Result<KeoBiaBeerPaymentDto>.Failure(disabledMessage);

        if (cups <= 0) return Result<KeoBiaBeerPaymentDto>.Failure("Số lượng phải từ 1 trở lên.");

        var bank = _configuration["KeoBia:Payment:BankCode"]?.Trim();
        var account = _configuration["KeoBia:Payment:AccountNumber"]?.Trim();
        var holder = _configuration["KeoBia:Payment:AccountName"]?.Trim();
        if (string.IsNullOrWhiteSpace(bank) || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(holder))
            return Result<KeoBiaBeerPaymentDto>.Failure("Chưa cấu hình tài khoản nộp. Báo admin kiểm tra KeoBia:Payment.");

        var player = await FindPlayerByTelegramIdAsync(telegramUserId, ct);
        if (player is null) return Result<KeoBiaBeerPaymentDto>.Failure("Không tìm thấy người chơi Telegram.");

        var outstanding = await ComputeLossBalanceAsync(player, ct);
        if (outstanding <= 0) return Result<KeoBiaBeerPaymentDto>.Failure("Bạn không còn phần nào cần nộp.");
        var unitBalance = await ComputeLossUnitBalanceAsync(player.Id, outstanding, ct);
        if (cups > outstanding)
            return Result<KeoBiaBeerPaymentDto>.Failure(
                $"Chỉ nộp tối đa {KeoBiaUnitRules.FormatBreakdown(unitBalance.BeerCups, unitBalance.PeanutPacks)}.");

        var beerCups = Math.Min(cups, unitBalance.BeerCups);
        var peanutPacks = cups - beerCups;
        if (peanutPacks > unitBalance.PeanutPacks)
            return Result<KeoBiaBeerPaymentDto>.Failure("Số lượng nộp vượt quá phần còn thiếu.");

        var now = DateTime.UtcNow;
        var code = "NB" + now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + RandomNumberGenerator.GetInt32(100, 999).ToString(CultureInfo.InvariantCulture);
        var coin = KeoBiaUnitRules.CalculatePaymentAmount(beerCups, peanutPacks);
        var contentSuffix = beerCups > 0 && peanutPacks > 0 ? "mix" : peanutPacks > 0 ? "lac" : "bia";
        var content = $"{telegramUserId} {cups}{contentSuffix}";
        var template = _configuration["KeoBia:Payment:QrTemplate"]?.Trim();
        if (string.IsNullOrWhiteSpace(template)) template = "compact";
        var store = _configuration["KeoBia:Payment:StoreName"]?.Trim() ?? "KeoBia";
        var qrUrl = "https://vietqr.app/img?" + string.Join('&', new[]
        {
            $"acc={Uri.EscapeDataString(account)}",
            $"bank={Uri.EscapeDataString(bank)}",
            $"amount={coin.ToString(CultureInfo.InvariantCulture)}",
            $"des={Uri.EscapeDataString(content)}",
            $"template={Uri.EscapeDataString(template)}",
            "showinfo=true",
            "fullacc=true",
            $"holder={Uri.EscapeDataString(holder)}",
            $"store={Uri.EscapeDataString(store)}"
        });

        var payment = new KeoBiaBeerPayment
        {
            SiteId = player.SiteId,
            PlayerId = player.Id,
            Code = code,
            Cups = cups,
            CoinAmount = coin,
            Status = KeoBiaBeerPaymentStatus.Pending,
            QrUrl = qrUrl,
            TransferContent = content,
            UpdatedAt = now
        };
        _db.KeoBiaBeerPayments.Add(payment);
        await _db.SaveChangesAsync(ct);

        return Result<KeoBiaBeerPaymentDto>.Success(MapPayment(payment, player));
    }

    public async Task<Result<KeoBiaBeerPaymentWebhookResultDto>> ConfirmBeerPaymentWebhookAsync(string payloadJson, string? suppliedSecret, CancellationToken ct = default)
    {
        var expected = _configuration["KeoBia:Payment:SepayWebhookSecret"];
        if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(expected.Trim(), suppliedSecret?.Trim(), StringComparison.Ordinal))
            return Result<KeoBiaBeerPaymentWebhookResultDto>.Failure("Webhook secret không hợp lệ.");

        (string? providerId, string? content, int? amount) parsed;
        try { parsed = ParsePaymentWebhook(payloadJson); }
        catch (JsonException) { return Result<KeoBiaBeerPaymentWebhookResultDto>.Failure("Webhook JSON không hợp lệ."); }
        var (providerId, content, amount) = parsed;
        if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(providerId))
            return Result<KeoBiaBeerPaymentWebhookResultDto>.Failure("Webhook thiếu nội dung chuyển khoản.");

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            var alreadyPaidPayment = await _db.KeoBiaBeerPayments
                .Include(x => x.Player)
                .FirstOrDefaultAsync(x => x.Status == KeoBiaBeerPaymentStatus.Paid && x.ProviderTransactionId == providerId, ct);
            if (alreadyPaidPayment is not null)
                return Result<KeoBiaBeerPaymentWebhookResultDto>.Success(new(true, MapPayment(alreadyPaidPayment, alreadyPaidPayment.Player)));
        }

        KeoBiaBeerPayment? payment = null;
        if (!string.IsNullOrWhiteSpace(content))
        {
            payment = await _db.KeoBiaBeerPayments
                .Include(x => x.Player)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Status == KeoBiaBeerPaymentStatus.Pending && (x.Code == content || x.TransferContent == content), ct);
        }
        if (payment is null && !string.IsNullOrWhiteSpace(providerId))
        {
            payment = await _db.KeoBiaBeerPayments
                .Include(x => x.Player)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Status == KeoBiaBeerPaymentStatus.Pending && x.Code == providerId, ct);
        }
        if (payment is null && !string.IsNullOrWhiteSpace(content))
        {
            var pending = await _db.KeoBiaBeerPayments
                .Include(x => x.Player)
                .Where(x => x.Status == KeoBiaBeerPaymentStatus.Pending)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
            payment = pending.FirstOrDefault(x => ContainsTransferContent(content, x.TransferContent));
        }
        if (payment is null) return Result<KeoBiaBeerPaymentWebhookResultDto>.Failure("Không tìm thấy giao dịch chờ.");
        if (amount.HasValue && amount.Value < payment.CoinAmount) return Result<KeoBiaBeerPaymentWebhookResultDto>.Failure("Số coin chuyển chưa đủ.");

        var alreadyPaid = payment.Status == KeoBiaBeerPaymentStatus.Paid;
        if (!alreadyPaid)
        {
            var paid = await MarkBeerPaymentPaidInternalAsync(payment, providerId, payloadJson, ct);
            if (!paid.Succeeded || paid.Value is null) return Result<KeoBiaBeerPaymentWebhookResultDto>.Failure(paid.Error ?? "Không xác nhận được giao dịch.");
            return Result<KeoBiaBeerPaymentWebhookResultDto>.Success(new(false, paid.Value));
        }

        return Result<KeoBiaBeerPaymentWebhookResultDto>.Success(new(true, MapPayment(payment, payment.Player)));
    }

    public async Task<PagedList<KeoBiaBeerPaymentDto>> SearchBeerPaymentsAsync(string? keyword, string? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        NormalizePaging(ref page, ref pageSize);
        var query = _db.KeoBiaBeerPayments.AsNoTracking().Include(x => x.Player).AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();
            query = query.Where(x => x.Code.Contains(keyword) || x.TransferContent.Contains(keyword) || x.Player.DisplayName.Contains(keyword));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            status = status.Trim();
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedList<KeoBiaBeerPaymentDto> { Items = items.Select(x => MapPayment(x, x.Player)).ToList(), Page = page, PageSize = pageSize, TotalItems = total };
    }

    public async Task<Result<KeoBiaBeerPaymentDto>> MarkBeerPaymentPaidAsync(Guid id, string? providerTransactionId = null, string? payloadJson = null, CancellationToken ct = default)
    {
        var payment = await _db.KeoBiaBeerPayments.Include(x => x.Player).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (payment is null) return Result<KeoBiaBeerPaymentDto>.Failure("Không tìm thấy giao dịch.");
        if (payment.Status == KeoBiaBeerPaymentStatus.Paid) return Result<KeoBiaBeerPaymentDto>.Failure("Giao dịch đã paid.");
        return await MarkBeerPaymentPaidInternalAsync(payment, providerTransactionId, payloadJson, ct);
    }

    public async Task<Result> MarkBeerPaymentFailedAsync(Guid id, CancellationToken ct = default)
    {
        var payment = await _db.KeoBiaBeerPayments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (payment is null) return Result.Failure("Không tìm thấy giao dịch.");
        if (payment.Status == KeoBiaBeerPaymentStatus.Paid) return Result.Failure("Giao dịch đã paid, không thể đổi failed.");
        payment.Status = KeoBiaBeerPaymentStatus.Failed;
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<int> GetBeerPaymentFundTotalAsync(CancellationToken ct = default)
    {
        return await _db.KeoBiaBeerPayments.AsNoTracking()
            .Where(x => x.Status == KeoBiaBeerPaymentStatus.Paid)
            .SumAsync(x => (int?)x.CoinAmount, ct) ?? 0;
    }

    public async Task<IReadOnlyList<KeoBiaBeerPaymentDto>> GetPublicBeerPaymentHistoryAsync(int take = 20, CancellationToken ct = default)
    {
        var query = _db.KeoBiaBeerPayments.AsNoTracking()
            .Include(x => x.Player)
            .Where(x => x.Status == KeoBiaBeerPaymentStatus.Paid)
            .OrderByDescending(x => x.PaidAt ?? x.CreatedAt);
        var rows = take <= 0
            ? await query.ToListAsync(ct)
            : await query.Take(Math.Clamp(take, 1, 2000)).ToListAsync(ct);
        return rows.Select(x => MapPayment(x, x.Player)).ToList();
    }

    private string? GetBeerPaymentDisabledMessage()
    {
        var raw = _configuration["KeoBia:Payment:DisabledUntil"];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var disabledUntil)) return null;
        return DateTimeOffset.UtcNow < disabledUntil.ToUniversalTime()
            ? "Tính năng nộp cốc bia và gói lạc tạm đóng đến 09/07/2026."
            : null;
    }

    public async Task<IReadOnlyList<KeoBiaTelegramResultNotificationDto>> GetTelegramResultNotificationsAsync(int take = 500, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);

        var query = _db.KeoBiaBets.AsNoTracking()
            .Where(x =>
                x.Player.TelegramUserId != null &&
                !x.Player.IsBlocked &&
                x.IsSettled &&
                x.IsCorrect != null &&
                x.SettledAt != null &&
                x.Match.Status == KeoBiaMatchStatus.Finished &&
                x.Match.ResultChoice != null);

        var latestMatchId = await query
            .OrderByDescending(x => x.Match.ResultUpdatedAt ?? x.SettledAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.Match.KickoffAt)
            .Select(x => (Guid?)x.MatchId)
            .FirstOrDefaultAsync(ct);

        if (!latestMatchId.HasValue)
            return [];

        return await query
            .Where(x => x.MatchId == latestMatchId.Value)
            .OrderByDescending(x => x.SettledAt)
            .Take(take)
            .Select(x => new KeoBiaTelegramResultNotificationDto(
                x.Id,
                x.Player.TelegramUserId.GetValueOrDefault(),
                x.Player.DisplayName,
                x.MatchId,
                x.Match.Stage,
                x.Match.HomeName,
                x.Match.HomeCode,
                x.Match.AwayName,
                x.Match.AwayCode,
                x.Match.KickoffAt,
                x.Match.Venue,
                x.Match.HomeScore,
                x.Match.AwayScore,
                x.Match.ResultHomeScore,
                x.Match.ResultAwayScore,
                x.Match.PenaltyHomeScore,
                x.Match.PenaltyAwayScore,
                x.Match.ResultChoice!,
                x.Match.ResultChoice == KeoBiaBetChoice.Home ? x.Match.HomeCode : x.Match.ResultChoice == KeoBiaBetChoice.Away ? x.Match.AwayCode : "Hòa",
                x.Choice,
                x.Choice == KeoBiaBetChoice.Home ? x.Match.HomeCode : x.Choice == KeoBiaBetChoice.Away ? x.Match.AwayCode : "Hòa",
                x.Cups,
                x.IsCorrect == true,
                x.SettledAt ?? DateTime.MinValue,
                x.StarType,
                x.PredictedHomeScore,
                x.PredictedAwayScore,
                x.CorrectScoreOdds))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<long>> GetMatchBettorTelegramIdsAsync(Guid matchId, CancellationToken ct = default)
    {
        return await _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.MatchId == matchId && x.Player.TelegramUserId != null && !x.Player.IsBlocked)
            .Select(x => x.Player.TelegramUserId!.Value)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<long>> GetAllTelegramUserIdsAsync(CancellationToken ct = default)
    {
        return await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.TelegramUserId != null && !x.IsBlocked)
            .Select(x => x.TelegramUserId!.Value)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<Result<KeoBiaUnitTransitionNoticeDto>> GetUnitTransitionNoticeAsync(
        string publicKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
            return Result<KeoBiaUnitTransitionNoticeDto>.Failure("Thiếu hồ sơ người chơi.");

        var player = await _db.KeoBiaPlayers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PublicKey == publicKey.Trim(), ct);
        if (player is null)
            return Result<KeoBiaUnitTransitionNoticeDto>.Failure("Không tìm thấy người chơi.");

        var shouldShow = DateTime.UtcNow >= KeoBiaUnitRules.PeanutSwitchAtUtc
            && player.TelegramUserId != null
            && !player.IsBlocked
            && player.UnitTransitionNoticeAcknowledgedAt is null;
        return Result<KeoBiaUnitTransitionNoticeDto>.Success(new(
            shouldShow,
            player.IsBlocked,
            player.StoppedPlayingAt.HasValue,
            player.UnitTransitionNoticeAcknowledgedAt));
    }

    public async Task<Result<KeoBiaUnitTransitionDecisionDto>> RespondUnitTransitionNoticeAsync(
        string publicKey,
        bool stopPlaying,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
            return Result<KeoBiaUnitTransitionDecisionDto>.Failure("Thiếu hồ sơ người chơi.");

        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.PublicKey == publicKey.Trim(), ct);
        return await RespondUnitTransitionNoticeAsync(player, stopPlaying, ct);
    }

    public async Task<Result<KeoBiaUnitTransitionDecisionDto>> RespondUnitTransitionNoticeByTelegramIdAsync(
        long telegramUserId,
        bool stopPlaying,
        CancellationToken ct = default)
    {
        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);
        return await RespondUnitTransitionNoticeAsync(player, stopPlaying, ct);
    }

    public async Task<IReadOnlyList<long>> GetPendingUnitTransitionTelegramUserIdsAsync(
        int take = 200,
        CancellationToken ct = default)
    {
        if (DateTime.UtcNow < KeoBiaUnitRules.PeanutSwitchAtUtc) return [];
        take = Math.Clamp(take, 1, 1000);
        return await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.TelegramUserId != null
                && !x.IsBlocked
                && x.UnitTransitionNoticeAcknowledgedAt == null
                && x.UnitTransitionTelegramSentAt == null)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.TelegramUserId!.Value)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<Result> MarkUnitTransitionTelegramSentAsync(
        long telegramUserId,
        CancellationToken ct = default)
    {
        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);
        if (player is null) return Result.Failure("Không tìm thấy người chơi Telegram.");

        player.UnitTransitionTelegramSentAt ??= DateTime.UtcNow;
        player.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result<KeoBiaUnitTransitionDecisionDto>> RespondUnitTransitionNoticeAsync(
        KeoBiaPlayer? player,
        bool stopPlaying,
        CancellationToken ct)
    {
        if (player is null)
            return Result<KeoBiaUnitTransitionDecisionDto>.Failure("Không tìm thấy người chơi.");
        if (DateTime.UtcNow < KeoBiaUnitRules.PeanutSwitchAtUtc)
            return Result<KeoBiaUnitTransitionDecisionDto>.Failure("Chưa đến thời điểm chuyển sang gói lạc.");

        if (player.StoppedPlayingAt.HasValue ||
            (player.UnitTransitionNoticeAcknowledgedAt.HasValue && !stopPlaying))
        {
            return Result<KeoBiaUnitTransitionDecisionDto>.Success(new(
                player.IsBlocked,
                player.StoppedPlayingAt.HasValue,
                player.UnitTransitionNoticeAcknowledgedAt ?? DateTime.UtcNow));
        }

        var now = DateTime.UtcNow;
        player.UnitTransitionNoticeAcknowledgedAt ??= now;
        player.UpdatedAt = now;

        if (stopPlaying)
        {
            await StopPlayerAsync(player, now, ct);
        }

        await _db.SaveChangesAsync(ct);
        if (stopPlaying)
            await RecalculatePlayersAsync([player.Id], ct);

        return Result<KeoBiaUnitTransitionDecisionDto>.Success(new(
            player.IsBlocked,
            player.StoppedPlayingAt.HasValue,
            player.UnitTransitionNoticeAcknowledgedAt.Value));
    }

    public async Task<Result> StopPlayerAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.Id == playerId, ct);
        if (player is null) return Result.Failure("Không tìm thấy người chơi.");

        var now = DateTime.UtcNow;
        // Admin lock follows the same terminal flow as the player's "Dừng tại đây" action.
        await StopPlayerAsync(player, now, ct);
        player.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        await RecalculatePlayersAsync([playerId], ct);
        return Result.Success();
    }

    public async Task<Result> TogglePlayerBlockAsync(Guid playerId, bool isBlocked, CancellationToken ct = default)
    {
        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.Id == playerId, ct);
        if (player is null) return Result.Failure("Không tìm thấy người chơi.");

        player.IsBlocked = isBlocked;
        if (!isBlocked)
            player.StoppedPlayingAt = null;
        await _db.SaveChangesAsync(ct);
        // Blocking exempts a player from the missed-match penalty, so re-journal their
        // loss: blocking writes missed_match reversals, unblocking re-accrues them.
        await RecalculatePlayersAsync([playerId], ct);
        return Result.Success();
    }

    private async Task StopPlayerAsync(KeoBiaPlayer player, DateTime stoppedAt, CancellationToken ct)
    {
        player.IsBlocked = true;
        player.StoppedPlayingAt = stoppedAt;

        var futureBets = await _db.KeoBiaBets
            .Where(x => x.PlayerId == player.Id
                && !x.IsSettled
                && x.Match.KickoffAt > stoppedAt)
            .ToListAsync(ct);
        if (futureBets.Count > 0)
            _db.KeoBiaBets.RemoveRange(futureBets);
    }

    public async Task<IReadOnlyList<KeoBiaChangelogDto>> GetChangelogsAsync(int take = 3, CancellationToken ct = default)
    {
        return await _db.KeoBiaChangelogs.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new KeoBiaChangelogDto(x.Version, x.Title, x.Content, x.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KeoBiaCupLogDto>> GetPlayerCupLogsAsync(Guid playerId, int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        var rows = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == playerId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.ChangeType,
                x.Cups,
                x.Reason,
                x.BalanceAfter,
                x.CreatedAt,
                MatchLabel = x.Match != null ? x.Match.HomeName + " vs " + x.Match.AwayName : null,
                MatchStage = x.Match != null ? x.Match.Stage : null,
                MatchKickoffAt = x.Match != null ? (DateTime?)x.Match.KickoffAt : null
            })
            .ToListAsync(ct);

        var running = UnitBalance.Empty;
        var mapped = new List<KeoBiaCupLogDto>(rows.Count);
        foreach (var row in rows)
        {
            var unitCode = ResolveCupLogUnitCode(
                row.Reason, row.MatchStage, row.MatchKickoffAt ?? row.CreatedAt);
            running = AddUnit(running, row.Cups, unitCode);
            running = ReconcileUnitBalance(
                row.BalanceAfter, running.BeerCups, running.PeanutPacks);
            mapped.Add(new KeoBiaCupLogDto(
                row.Id, row.ChangeType, row.Cups, row.Reason, row.BalanceAfter,
                row.CreatedAt, row.MatchLabel, unitCode,
                running.BeerCups, running.PeanutPacks));
        }

        return mapped
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToList();
    }

    private static string ResolveCupLogUnitCode(string? reason, string? matchStage, DateTime occurredAt)
    {
        var hasBeer = reason?.Contains("cốc bia", StringComparison.OrdinalIgnoreCase) == true;
        var hasPeanut = reason?.Contains("gói lạc", StringComparison.OrdinalIgnoreCase) == true;
        if (hasBeer && !hasPeanut) return KeoBiaUnitCode.Beer;
        if (hasPeanut && !hasBeer) return KeoBiaUnitCode.Peanut;
        return KeoBiaUnitRules.GetUnitCode(matchStage, occurredAt);
    }

    public async Task<KeoBiaPlayerListItemDto?> GetPlayerAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.Id == playerId)
            .Select(x => new KeoBiaPlayerListItemDto(
                x.Id, x.PublicKey, x.DisplayName, x.AvatarUrl, x.CreatedAt, x.LastSeenAt,
                x.TotalBets, x.TotalCups, x.CorrectBets, x.WrongBets, x.IsBlocked,
                x.HopeStars, x.DevilStars, x.PenaltyCups, x.MissedMatchCredit, x.SharedCups, x.TelegramUserId)
            {
                StoppedPlayingAt = x.StoppedPlayingAt
            })
            .FirstOrDefaultAsync(ct);
        if (player is null) return null;

        var enriched = await EnrichPlayerUnitStatsAsync([player], ct);
        return enriched[0];
    }

    public Task<Result> AddMissedMatchCreditAsync(Guid playerId, int cups, CancellationToken ct = default)
    {
        if (cups < 1 || cups > 999)
            return Task.FromResult(Result.Failure("Số lượt miễn phạt miss phải từ 1 đến 999."));

        return AddMissedMatchCreditAsync(playerId, cups, "Admin miễn phạt", ct);
    }

    private async Task<Result> AddMissedMatchCreditAsync(
        Guid playerId, int cups, string creditReason, CancellationToken ct)
    {
        if (cups < 1) return Result.Failure("Số lượt miễn phạt miss phải lớn hơn 0.");

        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.Id == playerId, ct);
        if (player is null) return Result.Failure("Không tìm thấy người chơi.");
        if (player.PenaltyCups <= 0) return Result.Failure("Người chơi không còn lượt miss cần miễn phạt.");

        var missedRows = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.MissedMatch)
            .Select(x => new
            {
                x.Cups,
                x.Reason,
                x.CreatedAt,
                MatchStage = x.Match != null ? x.Match.Stage : null,
                MatchKickoffAt = x.Match != null ? (DateTime?)x.Match.KickoffAt : null
            })
            .ToListAsync(ct);
        var attributedMissed = UnitBalance.Empty;
        foreach (var row in missedRows)
        {
            var unitCode = ResolveCupLogUnitCode(
                row.Reason, row.MatchStage, row.MatchKickoffAt ?? row.CreatedAt);
            attributedMissed = AddUnit(attributedMissed, row.Cups, unitCode);
        }

        var missedBalance = ReconcileUnitBalance(
            player.PenaltyCups, attributedMissed.BeerCups, attributedMissed.PeanutPacks);
        var applied = Math.Min(cups, player.PenaltyCups);
        var appliedUnits = KeoBiaUnitRules.TakeOldestFirst(
            applied, missedBalance.BeerCups, missedBalance.PeanutPacks);
        player.MissedMatchCredit += applied;
        player.PenaltyCups -= applied;

        var balance = await ComputeLossBalanceAsync(player, ct);
        if (appliedUnits.BeerCups > 0)
        {
            AddCupLog(player, null, KeoBiaCupChangeType.MissedMatchCredit, -appliedUnits.BeerCups,
                $"{creditReason} {KeoBiaUnitRules.FormatCount(appliedUnits.BeerCups, KeoBiaUnitCode.Beer)} do kèo miss",
                balance);
        }
        if (appliedUnits.PeanutPacks > 0)
        {
            AddCupLog(player, null, KeoBiaCupChangeType.MissedMatchCredit, -appliedUnits.PeanutPacks,
                $"{creditReason} {KeoBiaUnitRules.FormatCount(appliedUnits.PeanutPacks, KeoBiaUnitCode.Peanut)} do kèo miss",
                balance);
        }

        await _db.SaveChangesAsync(ct);
        await RecomputeCupLogBalancesAsync([player.Id], ct);
        return Result.Success();
    }

    public async Task<Result<KeoBiaBalanceCreditResultDto>> AddBalanceCreditAsync(Guid playerId, int cups, CancellationToken ct = default)
    {
        if (cups < 1 || cups > 999)
            return Result<KeoBiaBalanceCreditResultDto>.Failure("Số lượng giảm nợ phải từ 1 đến 999.");

        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.Id == playerId, ct);
        if (player is null) return Result<KeoBiaBalanceCreditResultDto>.Failure("Không tìm thấy người chơi.");

        var balance = await ComputeLossBalanceAsync(player, ct);
        if (balance <= 0) return Result<KeoBiaBalanceCreditResultDto>.Failure("Người chơi không còn bia hoặc lạc cần giảm.");

        var applied = Math.Min(cups, balance);
        var before = await ComputeLossUnitBalanceAsync(player.Id, balance, ct);
        var appliedUnits = KeoBiaUnitRules.TakeOldestFirst(applied, before.BeerCups, before.PeanutPacks);
        var afterBeer = Math.Max(0, before.BeerCups - appliedUnits.BeerCups);
        var afterPeanut = Math.Max(0, before.PeanutPacks - appliedUnits.PeanutPacks);
        var balanceAfter = Math.Max(0, balance - applied);
        if (appliedUnits.BeerCups > 0)
        {
            AddCupLog(player, null, KeoBiaCupChangeType.ReceivedBeer, -appliedUnits.BeerCups,
                $"Admin giảm nợ {KeoBiaUnitRules.FormatCount(appliedUnits.BeerCups, KeoBiaUnitCode.Beer)}",
                balanceAfter);
        }
        if (appliedUnits.PeanutPacks > 0)
        {
            AddCupLog(player, null, KeoBiaCupChangeType.ReceivedBeer, -appliedUnits.PeanutPacks,
                $"Admin giảm nợ {KeoBiaUnitRules.FormatCount(appliedUnits.PeanutPacks, KeoBiaUnitCode.Peanut)}",
                balanceAfter);
        }

        await _db.SaveChangesAsync(ct);
        await RecomputeCupLogBalancesAsync([player.Id], ct);
        return Result<KeoBiaBalanceCreditResultDto>.Success(new(
            applied,
            appliedUnits.BeerCups,
            appliedUnits.PeanutPacks,
            afterBeer,
            afterPeanut));
    }

    public async Task<Result> UpdatePlayerStarsAsync(Guid playerId, int? hopeStars, int? devilStars, CancellationToken ct = default)
    {
        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.Id == playerId, ct);
        if (player is null) return Result.Failure("Không tìm thấy người chơi.");
        var adminBalance = await ComputeLossBalanceAsync(player, ct);
        if (hopeStars.HasValue)
        {
            var newHope = Math.Max(0, hopeStars.Value);
            if (newHope != player.HopeStars)
            {
                AddCupLog(player, null, KeoBiaCupChangeType.AdminAdjust, 0,
                    $"Admin chỉnh Ngôi Sao Hi Vọng: {player.HopeStars} → {newHope}", adminBalance);
                player.HopeStars = newHope;
            }
        }
        if (devilStars.HasValue)
        {
            var newDevil = Math.Max(0, devilStars.Value);
            if (newDevil != player.DevilStars)
            {
                AddCupLog(player, null, KeoBiaCupChangeType.AdminAdjust, 0,
                    $"Admin chỉnh Ngôi Sao Ma Quỷ: {player.DevilStars} → {newDevil}", adminBalance);
                player.DevilStars = newDevil;
            }
        }
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> BulkAddStarAsync(string starType, int amount, CancellationToken ct = default)
    {
        if (amount < 1) return Result.Failure("Số lượng phải >= 1.");
        if (starType != "hope" && starType != "devil") return Result.Failure("Loại sao không hợp lệ.");
        var players = await _db.KeoBiaPlayers.Where(x => x.TelegramUserId != null).ToListAsync(ct);
        var label = starType == "hope" ? "Ngôi Sao Hi Vọng" : "Ngôi Sao Ma Quỷ";
        foreach (var p in players)
        {
            if (starType == "hope") p.HopeStars += amount;
            else p.DevilStars += amount;
            var balance = await ComputeLossBalanceAsync(p, ct);
            AddCupLog(p, null, KeoBiaCupChangeType.AdminAdjust, 0,
                $"Admin tặng {amount} {label} (hàng loạt)", balance);
        }
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // SiteId is taken from the player entity rather than relying on AppDbContext.StampSiteId,
    // because that auto-stamp is bypassed in admin cross-site mode (BypassSiteScope) and is a
    // no-op when there is no resolved request site (background jobs, startup seed). Stamping it
    // explicitly keeps every cup log visible behind the public site query filter.
    private void AddCupLog(KeoBiaPlayer player, Guid? matchId, string changeType, int cups, string reason, int balanceAfter, DateTime? createdAt = null)
    {
        var now = DateTime.UtcNow;
        _db.KeoBiaCupLogs.Add(new KeoBiaCupLog
        {
            SiteId = player.SiteId,
            PlayerId = player.Id,
            MatchId = matchId,
            ChangeType = changeType,
            Cups = cups,
            Reason = reason,
            BalanceAfter = balanceAfter,
            CreatedAt = createdAt ?? now,
            UpdatedAt = now
        });
    }

    public async Task SeedCupLogsAsync(CancellationToken ct = default)
    {
        // One-time backfill of the per-match journal for losses that predate live logging.
        // This runs at startup with no resolved request site, so it looks across every site
        // (IgnoreQueryFilters) and drives RecalculatePlayersAsync once per site with that
        // site's context set — Recalculate's own reads honour the global site query filter,
        // so the context must point at the site being backfilled. RecalculatePlayersAsync is
        // idempotent (reconciles by delta), so any redundant run is harmless.
        if (_currentSite is null) return;

        // Capture the journal state before the promotion backfill. The promotion recalc may
        // create per-match rows itself, but a genuinely empty journal still needs the full
        // historical backfill below.
        var alreadyJournaled = await _db.KeoBiaCupLogs.IgnoreQueryFilters()
            .AnyAsync(x => x.MatchId != null
                && (x.ChangeType == KeoBiaCupChangeType.WrongBet || x.ChangeType == KeoBiaCupChangeType.MissedMatch), ct);
        await BackfillRegistrationMissedMatchPromotionAsync(ct);
        if (alreadyJournaled) return;

        // Drop only the legacy lump rows that the per-match reconcile below WILL re-create
        // (matchId-less wrong_bet / missed_match), so it cannot double-count the same loss.
        // missed_credit is intentionally NOT touched: it is never re-journaled at runtime, so
        // deleting it would silently inflate the journalled loss by the credited amount.
        var legacyLumpLogs = await _db.KeoBiaCupLogs.IgnoreQueryFilters()
            .Where(x => x.MatchId == null
                && (x.ChangeType == KeoBiaCupChangeType.WrongBet
                    || x.ChangeType == KeoBiaCupChangeType.MissedMatch))
            .ToListAsync(ct);
        if (legacyLumpLogs.Count > 0)
        {
            _db.KeoBiaCupLogs.RemoveRange(legacyLumpLogs);
            await _db.SaveChangesAsync(ct);
        }

        var siteIds = await _db.KeoBiaPlayers.IgnoreQueryFilters()
            .Where(x => x.TelegramUserId != null)
            .Select(x => x.SiteId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var siteId in siteIds)
        {
            _currentSite.Set(siteId, _currentSite.Slug, _currentSite.Theme);
            var playerIds = await _db.KeoBiaPlayers
                .Where(x => x.TelegramUserId != null)
                .Select(x => x.Id)
                .ToListAsync(ct);
            await RecalculatePlayersAsync(playerIds, ct);
        }
    }

    public async Task<int> RejournalCupLogsAsync(CancellationToken ct = default)
    {
        if (_currentSite is null) return 0;

        var perMatchTypes = new[]
        {
            KeoBiaCupChangeType.WrongBet,
            KeoBiaCupChangeType.MissedMatch,
            KeoBiaCupChangeType.HopeStarWin,
            KeoBiaCupChangeType.HopeStarLose,
            KeoBiaCupChangeType.DevilStarWin,
            KeoBiaCupChangeType.DevilStarLose
        };

        var toDelete = await _db.KeoBiaCupLogs.IgnoreQueryFilters()
            .Where(x => x.MatchId != null && perMatchTypes.Contains(x.ChangeType))
            .ToListAsync(ct);

        if (toDelete.Count == 0) return 0;
        _db.KeoBiaCupLogs.RemoveRange(toDelete);
        await _db.SaveChangesAsync(ct);

        var siteIds = await _db.KeoBiaPlayers.IgnoreQueryFilters()
            .Where(x => x.TelegramUserId != null)
            .Select(x => x.SiteId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var siteId in siteIds)
        {
            _currentSite.Set(siteId, _currentSite.Slug, _currentSite.Theme);
            var playerIds = await _db.KeoBiaPlayers
                .Where(x => x.TelegramUserId != null)
                .Select(x => x.Id)
                .ToListAsync(ct);
            await RecalculatePlayersAsync(playerIds, ct);
        }

        return toDelete.Count;
    }

    public async Task<Result<int>> CleanupOrphanedPlaceholderMatchesAsync(CancellationToken ct = default)
    {
        if (_currentSite is null) return Result<int>.Failure("Không xác định được site.");

        var matches = await _db.KeoBiaMatches
            .IgnoreQueryFilters()
            .Include(x => x.Bets)
            .Where(x => x.SiteId == _currentSite.SiteId && !x.IsDeleted)
            .ToListAsync(ct);

        var orphaned = matches.Where(m =>
            (HasPlaceholderTeams(m) || m.Status == KeoBiaMatchStatus.Cancelled)
            && !m.ExternalId.StartsWith("fdwc-"))
            .ToList();

        if (orphaned.Count == 0)
            return Result<int>.Success(0);

        foreach (var m in orphaned)
        {
            if (m.Bets.Count > 0)
            {
                var cupLogs = await _db.KeoBiaCupLogs.IgnoreQueryFilters()
                    .Where(x => x.MatchId == m.Id)
                    .ToListAsync(ct);
                _db.KeoBiaCupLogs.RemoveRange(cupLogs);

                var affectedPlayerIds = m.Bets.Select(x => x.PlayerId).Distinct().ToList();
                _db.KeoBiaBets.RemoveRange(m.Bets);

                m.IsDeleted = true;
                m.DeletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                await RecalculatePlayersAsync(affectedPlayerIds, ct);
            }
            else
            {
                m.IsDeleted = true;
                m.DeletedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
        return Result<int>.Success(orphaned.Count);
    }

    // ================= Quick Q&A (Hỏi đáp nhanh) =================

    private static readonly JsonSerializerOptions QuizJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions ChatRouteJsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxQuizRewardCups = 99;
    private const int MaxQuizPenaltyCups = 99;

    private static IReadOnlyList<KeoBiaQuestionChoiceDto> ParseChoices(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<KeoBiaQuestionChoiceDto>>(json, QuizJsonOptions)
                ?? new List<KeoBiaQuestionChoiceDto>();
        }
        catch { return new List<KeoBiaQuestionChoiceDto>(); }
    }

    private static bool IsQuestionOpenForVoting(KeoBiaQuestion q, DateTime nowUtc) =>
        q.Status == KeoBiaQuestionStatus.Open && (q.ClosesAt == null || q.ClosesAt > nowUtc);

    public async Task<IReadOnlyList<KeoBiaQuestionAdminDto>> GetQuestionsAsync(CancellationToken ct = default)
    {
        var rows = await _db.KeoBiaQuestions.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id, x.Text, x.ChoicesJson, x.RewardCups, x.PenaltyCups, x.CorrectChoiceKey,
                x.Status, x.ClosesAt, x.RevealedAt, x.CreatedAt, x.TelegramMessageId,
                VoteCount = x.Votes.Count
            })
            .ToListAsync(ct);

        return rows.Select(x => new KeoBiaQuestionAdminDto(
            x.Id, x.Text, ParseChoices(x.ChoicesJson), x.RewardCups, x.PenaltyCups, x.CorrectChoiceKey,
            x.Status, x.ClosesAt, x.RevealedAt, x.CreatedAt, x.VoteCount,
            x.TelegramMessageId != null)).ToList();
    }

    public async Task<Result<KeoBiaQuestionAdminDto>> CreateQuestionAsync(KeoBiaCreateQuestionDto dto, CancellationToken ct = default)
    {
        var text = dto.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return Result<KeoBiaQuestionAdminDto>.Failure("Vui lòng nhập nội dung câu hỏi.");
        if (text.Length > 400)
            return Result<KeoBiaQuestionAdminDto>.Failure("Câu hỏi tối đa 400 ký tự.");

        var choices = (dto.Choices ?? Array.Empty<KeoBiaQuestionChoiceDto>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Key) && !string.IsNullOrWhiteSpace(c.Label))
            .Select(c => new KeoBiaQuestionChoiceDto(c.Key.Trim(), c.Label.Trim()))
            .ToList();
        if (choices.Count < 2)
            return Result<KeoBiaQuestionAdminDto>.Failure("Cần ít nhất 2 lựa chọn.");
        if (choices.Any(c => c.Key.Contains(':')))
            return Result<KeoBiaQuestionAdminDto>.Failure("Mã lựa chọn không được chứa dấu ':'.");
        if (choices.Any(c => c.Key.Length > 16))
            return Result<KeoBiaQuestionAdminDto>.Failure("Mã lựa chọn tối đa 16 ký tự.");
        if (choices.Select(c => c.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != choices.Count)
            return Result<KeoBiaQuestionAdminDto>.Failure("Mã lựa chọn bị trùng.");
        var quizUnitLabel = KeoBiaUnitRules.FullLabel(KeoBiaUnitRules.GetCurrentUnitCode());
        if (dto.RewardCups < 1 || dto.RewardCups > MaxQuizRewardCups)
            return Result<KeoBiaQuestionAdminDto>.Failure($"Số {quizUnitLabel} thưởng phải từ 1 đến {MaxQuizRewardCups}.");
        if (dto.PenaltyCups < 0 || dto.PenaltyCups > MaxQuizPenaltyCups)
            return Result<KeoBiaQuestionAdminDto>.Failure($"Số {quizUnitLabel} phạt phải từ 0 đến {MaxQuizPenaltyCups}.");

        var entity = new KeoBiaQuestion
        {
            Text = text,
            ChoicesJson = JsonSerializer.Serialize(choices, QuizJsonOptions),
            RewardCups = dto.RewardCups,
            PenaltyCups = dto.PenaltyCups,
            Status = KeoBiaQuestionStatus.Open,
            ClosesAt = dto.ClosesAt
        };
        _db.KeoBiaQuestions.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Result<KeoBiaQuestionAdminDto>.Success(new KeoBiaQuestionAdminDto(
            entity.Id, entity.Text, choices, entity.RewardCups, entity.PenaltyCups, entity.CorrectChoiceKey,
            entity.Status, entity.ClosesAt, entity.RevealedAt, entity.CreatedAt, 0, false));
    }

    public async Task SetQuestionTelegramMessageAsync(Guid questionId, string chatId, long messageId, CancellationToken ct = default)
    {
        var q = await _db.KeoBiaQuestions.FirstOrDefaultAsync(x => x.Id == questionId, ct);
        if (q is null) return;
        q.TelegramChatId = chatId;
        q.TelegramMessageId = messageId;
        q.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Result> CloseQuestionAsync(Guid questionId, CancellationToken ct = default)
    {
        var q = await _db.KeoBiaQuestions.FirstOrDefaultAsync(x => x.Id == questionId, ct);
        if (q is null) return Result.Failure("Không tìm thấy câu hỏi.");
        if (q.Status == KeoBiaQuestionStatus.Open)
        {
            q.Status = KeoBiaQuestionStatus.Closed;
            q.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    public async Task<Result> DeleteQuestionAsync(Guid questionId, CancellationToken ct = default)
    {
        var q = await _db.KeoBiaQuestions.FirstOrDefaultAsync(x => x.Id == questionId, ct);
        if (q is null) return Result.Failure("Không tìm thấy câu hỏi.");
        q.IsDeleted = true;
        q.DeletedAt = DateTime.UtcNow;
        q.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<KeoBiaRevealResultDto>> RevealQuestionAnswerAsync(Guid questionId, string correctChoiceKey, CancellationToken ct = default)
    {
        var q = await _db.KeoBiaQuestions.FirstOrDefaultAsync(x => x.Id == questionId, ct);
        if (q is null) return Result<KeoBiaRevealResultDto>.Failure("Không tìm thấy câu hỏi.");

        var key = correctChoiceKey?.Trim();
        var choices = ParseChoices(q.ChoicesJson);
        var match = string.IsNullOrWhiteSpace(key)
            ? null
            : choices.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return Result<KeoBiaRevealResultDto>.Failure("Đáp án đúng không hợp lệ.");

        var revealedAt = q.RevealedAt ?? DateTime.UtcNow;
        var unitCode = KeoBiaUnitRules.GetUnitCode(null, revealedAt);
        q.CorrectChoiceKey = match.Key;
        q.Status = KeoBiaQuestionStatus.Revealed;
        q.RevealedAt = revealedAt;
        q.UpdatedAt = DateTime.UtcNow;

        var votes = await _db.KeoBiaQuestionVotes
            .Include(v => v.Player)
            .Where(v => v.QuestionId == questionId)
            .ToListAsync(ct);
        var playerIds = votes.Select(v => v.PlayerId).Distinct().ToList();

        // Per-player base loss EXCLUDING this quiz's signed delta = the clamp ceiling.
        // Independent of quiz state, so a re-reveal is deterministic.
        var lostBetByPlayer = (await _db.KeoBiaBets.AsNoTracking()
            .Where(b => playerIds.Contains(b.PlayerId) && b.IsSettled && b.IsCorrect == false)
            .GroupBy(b => b.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = g.Sum(x => x.Cups) })
            .ToListAsync(ct)).ToDictionary(x => x.PlayerId, x => x.Cups);
        var receivedByPlayer = (await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(l => playerIds.Contains(l.PlayerId) && l.ChangeType == KeoBiaCupChangeType.ReceivedBeer)
            .GroupBy(l => l.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = -g.Sum(x => x.Cups) })
            .ToListAsync(ct)).ToDictionary(x => x.PlayerId, x => x.Cups);
        var quizRewardByPlayer = (await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(l => playerIds.Contains(l.PlayerId) && l.ChangeType == KeoBiaCupChangeType.QuizCorrect)
            .GroupBy(l => l.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = -g.Sum(x => x.Cups) })
            .ToListAsync(ct)).ToDictionary(x => x.PlayerId, x => x.Cups);
        var quizPenaltyByPlayer = (await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(l => playerIds.Contains(l.PlayerId) && l.ChangeType == KeoBiaCupChangeType.QuizWrong)
            .GroupBy(l => l.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = g.Sum(x => x.Cups) })
            .ToListAsync(ct)).ToDictionary(x => x.PlayerId, x => x.Cups);
        var paidBeerByPlayer = (await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(l => playerIds.Contains(l.PlayerId) && l.ChangeType == KeoBiaCupChangeType.PaidBeer)
            .GroupBy(l => l.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = -g.Sum(x => x.Cups) })
            .ToListAsync(ct)).ToDictionary(x => x.PlayerId, x => x.Cups);

        int rewarded = 0, totalCorrect = 0, totalRewardCups = 0, penalized = 0, totalPenaltyCups = 0;
        foreach (var vote in votes)
        {
            var isCorrect = string.Equals(vote.ChoiceKey, match.Key, StringComparison.OrdinalIgnoreCase);
            vote.IsCorrect = isCorrect;
            vote.IsSettled = true;
            vote.SettledAt = DateTime.UtcNow;
            if (isCorrect) totalCorrect++;

            var lostBet = lostBetByPlayer.TryGetValue(vote.PlayerId, out var lb) ? lb : 0;
            var received = receivedByPlayer.TryGetValue(vote.PlayerId, out var rc) ? rc : 0;
            var quizReward = quizRewardByPlayer.TryGetValue(vote.PlayerId, out var qr) ? qr : 0;
            var quizPenalty = quizPenaltyByPlayer.TryGetValue(vote.PlayerId, out var qp) ? qp : 0;
            var paidBeer = paidBeerByPlayer.TryGetValue(vote.PlayerId, out var pb) ? pb : 0;
            var ownPriorGrant = Math.Max(0, -vote.RewardedCups);
            var ownPriorPenalty = Math.Max(0, vote.RewardedCups);
            var currentLossExcludingThisQuestion = Math.Max(0,
                lostBet + vote.Player.PenaltyCups + vote.Player.SharedCups + Math.Max(0, quizPenalty - ownPriorPenalty) - received - paidBeer - Math.Max(0, quizReward - ownPriorGrant));

            // Clamp at 0 across all quiz rewards: no banking for future losses.
            var grant = isCorrect ? Math.Min(q.RewardCups, currentLossExcludingThisQuestion) : 0;
            var desiredSigned = isCorrect ? -grant : q.PenaltyCups;
            var delta = desiredSigned - vote.RewardedCups;
            if (delta != 0)
            {
                var amountText = KeoBiaUnitRules.FormatCount(Math.Abs(delta), unitCode);
                AddCupLog(vote.Player, null, delta < 0 ? KeoBiaCupChangeType.QuizCorrect : KeoBiaCupChangeType.QuizWrong, delta,
                    delta < 0 ? $"Trả lời đúng, giảm {amountText}: {q.Text}" : $"Trả lời sai, tăng {amountText}: {q.Text}",
                    Math.Max(0, currentLossExcludingThisQuestion + desiredSigned),
                    revealedAt);
            }
            vote.RewardedCups = desiredSigned;
            if (isCorrect && grant > 0) { rewarded++; totalRewardCups += grant; }
            if (!isCorrect && desiredSigned > 0) { penalized++; totalPenaltyCups += desiredSigned; }
        }

        await _db.SaveChangesAsync(ct);
        return Result<KeoBiaRevealResultDto>.Success(new(
            rewarded, totalCorrect, totalRewardCups, penalized, totalPenaltyCups, unitCode));
    }

    public async Task<Result<KeoBiaPublicQuestionDto>> SubmitQuizVoteAsync(string publicKey, Guid questionId, string choiceKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
            return Result<KeoBiaPublicQuestionDto>.Failure("Thiếu mã người chơi.");
        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.PublicKey == publicKey.Trim(), ct);
        if (player is null) return Result<KeoBiaPublicQuestionDto>.Failure("Không tìm thấy người chơi.");
        if (player.TelegramUserId is null)
            return Result<KeoBiaPublicQuestionDto>.Failure("Cần xác thực Telegram để bình chọn.");
        return await CastQuizVoteAsync(player, questionId, choiceKey, ct);
    }

    public async Task<Result<KeoBiaPublicQuestionDto>> SubmitQuizVoteByTelegramIdAsync(long telegramUserId, Guid questionId, string choiceKey, CancellationToken ct = default)
    {
        if (telegramUserId <= 0) return Result<KeoBiaPublicQuestionDto>.Failure("Thiếu ID Telegram hợp lệ.");
        var player = await FindPlayerByTelegramIdAsync(telegramUserId, ct);
        if (player is null) return Result<KeoBiaPublicQuestionDto>.Failure("Không tìm thấy người chơi Telegram.");
        return await CastQuizVoteAsync(player, questionId, choiceKey, ct);
    }

    private async Task<Result<KeoBiaPublicQuestionDto>> CastQuizVoteAsync(KeoBiaPlayer player, Guid questionId, string choiceKey, CancellationToken ct)
    {
        var key = choiceKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return Result<KeoBiaPublicQuestionDto>.Failure("Thiếu lựa chọn.");

        var q = await _db.KeoBiaQuestions.FirstOrDefaultAsync(x => x.Id == questionId, ct);
        if (q is null) return Result<KeoBiaPublicQuestionDto>.Failure("Không tìm thấy câu hỏi.");

        var choices = ParseChoices(q.ChoicesJson);
        var choice = choices.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (choice is null) return Result<KeoBiaPublicQuestionDto>.Failure("Lựa chọn không hợp lệ.");
        if (!IsQuestionOpenForVoting(q, DateTime.UtcNow))
            return Result<KeoBiaPublicQuestionDto>.Failure("Câu hỏi đã đóng bình chọn.");

        if (await _db.KeoBiaQuestionVotes.AnyAsync(v => v.QuestionId == questionId && v.PlayerId == player.Id, ct))
            return Result<KeoBiaPublicQuestionDto>.Failure("Bạn đã bình chọn câu này rồi.");

        _db.KeoBiaQuestionVotes.Add(new KeoBiaQuestionVote
        {
            SiteId = player.SiteId,
            QuestionId = questionId,
            PlayerId = player.Id,
            ChoiceKey = choice.Key
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the race against the unique (Site, Question, Player) index.
            return Result<KeoBiaPublicQuestionDto>.Failure("Bạn đã bình chọn câu này rồi.");
        }

        return Result<KeoBiaPublicQuestionDto>.Success(await BuildPublicQuestionAsync(q, player.Id, ct));
    }

    private async Task<KeoBiaPublicQuestionDto> BuildPublicQuestionAsync(KeoBiaQuestion q, Guid? playerId, CancellationToken ct)
    {
        var choices = ParseChoices(q.ChoicesJson);
        var counts = await _db.KeoBiaQuestionVotes.AsNoTracking()
            .Where(v => v.QuestionId == q.Id)
            .GroupBy(v => v.ChoiceKey)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countByKey = counts.ToDictionary(x => x.Key, x => x.Count, StringComparer.OrdinalIgnoreCase);

        string? myChoice = null;
        if (playerId is { } pid)
            myChoice = await _db.KeoBiaQuestionVotes.AsNoTracking()
                .Where(v => v.QuestionId == q.Id && v.PlayerId == pid)
                .Select(v => v.ChoiceKey)
                .FirstOrDefaultAsync(ct);

        var publicChoices = choices
            .Select(c => new KeoBiaPublicQuestionChoiceDto(c.Key, c.Label,
                countByKey.TryGetValue(c.Key, out var n) ? n : 0))
            .ToList();

        return new KeoBiaPublicQuestionDto(
            q.Id, q.Text, q.RewardCups, q.PenaltyCups, q.Status, q.ClosesAt, q.CorrectChoiceKey,
            myChoice != null, myChoice, publicChoices,
            KeoBiaUnitRules.GetUnitCode(null, q.RevealedAt ?? DateTime.UtcNow));
    }

    public async Task<IReadOnlyList<KeoBiaPublicQuestionDto>> GetActiveQuestionsForPublicAsync(string? publicKey, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var open = await _db.KeoBiaQuestions.AsNoTracking()
            .Where(x => x.Status == KeoBiaQuestionStatus.Open && (x.ClosesAt == null || x.ClosesAt > now))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        if (open.Count == 0) return Array.Empty<KeoBiaPublicQuestionDto>();

        Guid? playerId = null;
        if (!string.IsNullOrWhiteSpace(publicKey))
            playerId = await _db.KeoBiaPlayers.AsNoTracking()
                .Where(p => p.PublicKey == publicKey.Trim())
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(ct);

        var openIds = open.Select(o => o.Id).ToList();
        var votedQuestionIds = playerId is { } pid
            ? (await _db.KeoBiaQuestionVotes.AsNoTracking()
                .Where(v => v.PlayerId == pid && openIds.Contains(v.QuestionId))
                .Select(v => v.QuestionId).ToListAsync(ct)).ToHashSet()
            : new HashSet<Guid>();

        var result = new List<KeoBiaPublicQuestionDto>();
        foreach (var q in open)
            result.Add(await BuildPublicQuestionAsync(q, playerId, ct));
        return result;
    }

    public async Task<Result<KeoBiaQuestionVotesDto>> GetQuestionVotesAsync(Guid questionId, CancellationToken ct = default)
    {
        var q = await _db.KeoBiaQuestions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == questionId, ct);
        if (q is null) return Result<KeoBiaQuestionVotesDto>.Failure("Không tìm thấy câu hỏi.");

        var choices = ParseChoices(q.ChoicesJson);
        var voters = await _db.KeoBiaQuestionVotes.AsNoTracking()
            .Where(v => v.QuestionId == questionId)
            .OrderBy(v => v.CreatedAt)
            .Select(v => new KeoBiaQuestionVoterDto(v.Player.DisplayName, v.Player.AvatarUrl, v.ChoiceKey, v.CreatedAt))
            .ToListAsync(ct);

        var countByKey = voters.GroupBy(v => v.ChoiceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var publicChoices = choices
            .Select(c => new KeoBiaPublicQuestionChoiceDto(c.Key, c.Label,
                countByKey.TryGetValue(c.Key, out var n) ? n : 0))
            .ToList();

        return Result<KeoBiaQuestionVotesDto>.Success(new KeoBiaQuestionVotesDto(
            q.Id, q.Text, q.RewardCups, q.PenaltyCups, q.Status, q.CorrectChoiceKey, publicChoices, voters,
            KeoBiaUnitRules.GetUnitCode(null, q.RevealedAt ?? DateTime.UtcNow)));
    }

    public async Task<string> BuildKeoBiaContextAsync(long? telegramUserId = null, CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        // Matches
        var matches = await _db.KeoBiaMatches.AsNoTracking()
            .Where(x => x.Status != KeoBiaMatchStatus.Cancelled)
            .OrderByDescending(x => x.KickoffAt)
            .Take(20)
            .Select(x => new { x.Id, x.HomeName, x.AwayName, x.HomeScore, x.AwayScore, x.Status, x.KickoffAt, x.ResultChoice })
            .ToListAsync(ct);

        var finished = matches.Where(m => m.Status == KeoBiaMatchStatus.Finished).ToList();
        var upcoming = matches.Where(m => m.Status == KeoBiaMatchStatus.Scheduled).ToList();

        sb.AppendLine("=== TRẬN ĐÃ KẾT THÚC ===");
        foreach (var m in finished.Take(10))
        {
            var score = m.HomeScore.HasValue ? $"{m.HomeScore}-{m.AwayScore}" : "N/A";
            var result = m.ResultChoice == "home" ? m.HomeName : m.ResultChoice == "away" ? m.AwayName : "Hòa";
            sb.AppendLine($"- {m.HomeName} vs {m.AwayName}: {score} (Thắng: {result})");
        }

        sb.AppendLine("\n=== TRẬN SẮP DIỄN RA ===");
        foreach (var m in upcoming.Take(10))
        {
            sb.AppendLine($"- {m.HomeName} vs {m.AwayName}: {m.KickoffAt:dd/MM HH:mm} UTC");
        }

        var matchIds = matches.Select(x => x.Id).ToArray();
        var scorePicks = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId)
                && x.PredictedHomeScore != null
                && x.PredictedAwayScore != null
                && x.Player.TelegramUserId != null)
            .OrderBy(x => x.Match.KickoffAt)
            .ThenBy(x => x.Player.DisplayName)
            .Select(x => new
            {
                x.Match.HomeName,
                x.Match.AwayName,
                x.Player.DisplayName,
                x.PredictedHomeScore,
                x.PredictedAwayScore,
                x.Choice
            })
            .ToListAsync(ct);

        sb.AppendLine("\n=== DỰ ĐOÁN TỈ SỐ 90 PHÚT CỦA NGƯỜI CHƠI ===");
        if (scorePicks.Count == 0)
        {
            sb.AppendLine("Chưa có người chơi nào dự đoán tỉ số.");
        }
        else
        {
            foreach (var group in scorePicks.GroupBy(x => $"{x.HomeName} vs {x.AwayName}"))
            {
                sb.AppendLine($"- {group.Key}:");
                foreach (var pick in group)
                    sb.AppendLine($"  + {pick.DisplayName}: {pick.PredictedHomeScore}-{pick.PredictedAwayScore} ({pick.Choice})");
            }
        }

        // Leaderboard top 10
        var board = await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.TelegramUserId != null && !x.IsBlocked)
            .OrderByDescending(x => x.TotalCups)
            .Take(10)
            .Select(x => new KeoBiaPlayerListItemDto(
                x.Id, x.PublicKey, x.DisplayName, x.AvatarUrl, x.CreatedAt, x.LastSeenAt,
                x.TotalBets, x.TotalCups, x.CorrectBets, x.WrongBets, x.IsBlocked,
                x.HopeStars, x.DevilStars, x.PenaltyCups, x.MissedMatchCredit, x.SharedCups, x.TelegramUserId)
            {
                StoppedPlayingAt = x.StoppedPlayingAt
            })
            .ToListAsync(ct);
        board = await EnrichPlayerUnitStatsAsync(board, ct);

        sb.AppendLine("\n=== BẢNG XẾP HẠNG TOP 10 ===");
        int rank = 1;
        foreach (var p in board)
        {
            sb.AppendLine($"{rank}. {p.DisplayName}: đã dự đoán {KeoBiaUnitRules.FormatBreakdown(p.TotalBeerCups, p.TotalPeanutPacks)}, đang thiếu {KeoBiaUnitRules.FormatBreakdown(p.OutstandingBeerCups, p.OutstandingPeanutPacks)}, {p.CorrectBets} đúng, {p.WrongBets} sai");
            rank++;
        }

        // Stats
        var totalPlayers = await _db.KeoBiaPlayers.AsNoTracking().CountAsync(x => x.TelegramUserId != null, ct);
        var totalBets = await _db.KeoBiaBets.AsNoTracking().CountAsync(ct);
        sb.AppendLine($"\nTổng: {totalPlayers} người chơi, {totalBets} lượt dự đoán, {finished.Count} trận đã kết thúc, {upcoming.Count} trận sắp diễn ra.");

        // Current player
        if (telegramUserId.HasValue)
        {
            var player = await _db.KeoBiaPlayers.AsNoTracking()
                .Where(x => x.TelegramUserId == telegramUserId.Value)
                .Select(x => new KeoBiaPlayerListItemDto(
                    x.Id, x.PublicKey, x.DisplayName, x.AvatarUrl, x.CreatedAt, x.LastSeenAt,
                    x.TotalBets, x.TotalCups, x.CorrectBets, x.WrongBets, x.IsBlocked,
                    x.HopeStars, x.DevilStars, x.PenaltyCups, x.MissedMatchCredit, x.SharedCups, x.TelegramUserId))
                .FirstOrDefaultAsync(ct);

            if (player is not null)
            {
                player = (await EnrichPlayerUnitStatsAsync([player], ct))[0];
                var recentBets = await _db.KeoBiaBets.AsNoTracking()
                    .Where(x => x.Player.TelegramUserId == telegramUserId.Value)
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(5)
                    .Select(x => new { x.Match.HomeName, x.Match.AwayName, x.Choice, x.IsSettled, x.IsCorrect })
                    .ToListAsync(ct);

                sb.AppendLine($"\n=== NGƯỜI CHƠI HIỆN TẠI: {player.DisplayName} ===");
                sb.AppendLine($"Tổng: {player.TotalBets} kèo, đã dự đoán {KeoBiaUnitRules.FormatBreakdown(player.TotalBeerCups, player.TotalPeanutPacks)}, đang thiếu {KeoBiaUnitRules.FormatBreakdown(player.OutstandingBeerCups, player.OutstandingPeanutPacks)}, đã tặng {KeoBiaUnitRules.FormatBreakdown(player.SharedBeerCups, player.SharedPeanutPacks)}, {player.CorrectBets} đúng, {player.WrongBets} sai");
                sb.AppendLine("Gần đây:");
                foreach (var b in recentBets)
                {
                    var r = b.IsSettled ? (b.IsCorrect == true ? "đúng" : "sai") : "chưa có kết quả";
                    sb.AppendLine($"- {b.HomeName} vs {b.AwayName}: {b.Choice} ({r})");
                }
            }
        }

        return sb.ToString();
    }

    public async Task<string> ChatWithAiAsync(long telegramUserId, string userMessage, CancellationToken ct = default)
    {
        if (_aiChatClient is null)
            return "AI chưa được cấu hình. Liên hệ admin để bật tính năng này.";

        var connection = await _db.AiConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsDefault && x.IsActive && !x.IsDeleted, ct);

        if (connection is null)
            return "Chưa có kết nối AI khả dụng.";

        string apiKey;
        try
        {
            if (_dataProtection is null) return "AI chưa được cấu hình đúng.";
            var protector = _dataProtection.CreateProtector("NewsCMS.Ai.ApiKey");
            apiKey = protector.Unprotect(connection.ApiKeyEncrypted);
        }
        catch
        {
            return "Không giải mã được API key AI.";
        }

        await EnsureSiteForTelegramUserAsync(telegramUserId, ct);

        var messages = new List<(string Role, string Content)>
        {
            ("system", BuildKeoBiaChatSystemPrompt()),
            ("user", $"DỮ LIỆU NỘI BỘ BIA VUI 2026, dùng như nguồn sự thật khi hỏi về người chơi/kèo/tỉ số:\n{await BuildKeoBiaContextAsync(telegramUserId, ct)}"),
            ("user", userMessage)
        };

        var route = await ClassifyChatRouteAsync(connection.BaseUrl, apiKey, connection.DefaultModel, userMessage, connection.TimeoutSeconds, ct);
        var allTools = FilterToolsByRoute(await BuildKeoBiaChatToolsAsync(ct), route);
        var webSearchError = await PrimeWebSearchAsync(messages, allTools, route, userMessage, ct);
        if (webSearchError is not null)
            return webSearchError;

        _logger?.LogInformation("KeoBia AI chat route {Route} tools sent to model: {Tools}", route.ToolPolicy, string.Join(", ", allTools.Select(x => x.Name)));

        try
        {
            var result = await _aiChatClient.CompleteAsync(
                connection.BaseUrl,
                apiKey,
                connection.DefaultModel,
                messages,
                allTools,
                0.7,
                800,
                60,
                ct);

            if (!result.Succeeded)
                return $"Lỗi AI: {result.Error}";

            if (IsImageRequest(userMessage) && LooksLikeMissingImageToolResponse(result.Value!))
            {
                var fallbackImage = await TryGenerateImageDirectlyAsync(userMessage, ct);
                if (fallbackImage is not null)
                    return fallbackImage;
            }

            return result.Value!;
        }
        catch (Exception ex)
        {
            return $"Lỗi kết nối AI: {ex.Message}";
        }
    }

    public async Task<string> ChatWithAiStreamingAsync(long telegramUserId, string userMessage, Func<string, CancellationToken, Task> onDelta, CancellationToken ct = default)
    {
        if (_aiChatClient is null)
            return "AI chưa được cấu hình. Liên hệ admin để bật tính năng này.";

        var connection = await _db.AiConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsDefault && x.IsActive && !x.IsDeleted, ct);
        if (connection is null)
            return "Chưa có kết nối AI khả dụng.";

        string apiKey;
        try
        {
            if (_dataProtection is null) return "AI chưa được cấu hình đúng.";
            apiKey = _dataProtection.CreateProtector("NewsCMS.Ai.ApiKey").Unprotect(connection.ApiKeyEncrypted);
        }
        catch
        {
            return "Không giải mã được API key AI.";
        }

        await EnsureSiteForTelegramUserAsync(telegramUserId, ct);

        var messages = new List<(string Role, string Content)>
        {
            ("system", BuildKeoBiaChatSystemPrompt()),
            ("user", $"DỮ LIỆU NỘI BỘ BIA VUI 2026, dùng như nguồn sự thật khi hỏi về người chơi/kèo/tỉ số:\n{await BuildKeoBiaContextAsync(telegramUserId, ct)}"),
            ("user", userMessage)
        };
        var route = await ClassifyChatRouteAsync(connection.BaseUrl, apiKey, connection.DefaultModel, userMessage, connection.TimeoutSeconds, ct);
        var allTools = FilterToolsByRoute(await BuildKeoBiaChatToolsAsync(ct), route);
        var webSearchError = await PrimeWebSearchAsync(messages, allTools, route, userMessage, ct);
        if (webSearchError is not null)
            return webSearchError;

        _logger?.LogInformation("KeoBia AI streaming chat route {Route} tools sent to model: {Tools}", route.ToolPolicy, string.Join(", ", allTools.Select(x => x.Name)));

        try
        {
            var result = await _aiChatClient.CompleteStreamingAsync(
                connection.BaseUrl,
                apiKey,
                connection.DefaultModel,
                messages,
                allTools,
                onDelta,
                0.7,
                800,
                connection.TimeoutSeconds,
                ct);

            if (!result.Succeeded)
                return $"Lỗi AI: {result.Error}";

            if (IsImageRequest(userMessage) && LooksLikeMissingImageToolResponse(result.Value!))
            {
                var fallbackImage = await TryGenerateImageDirectlyAsync(userMessage, ct);
                if (fallbackImage is not null)
                    return fallbackImage;
            }

            return result.Value!;
        }
        catch (Exception ex)
        {
            return $"Lỗi kết nối AI: {ex.Message}";
        }
    }

    // Luồng /chat chạy trong Task.Run với DI scope MỚI → CurrentSiteAccessor rỗng (SiteId=Empty),
    // khiến global query filter (SiteId == CurrentSiteId) lọc sạch trận/kèo và mọi tool trả "không tìm thấy".
    // Resolve site từ chính player Telegram (bỏ qua filter) rồi Set lại cho scope này.
    private async Task EnsureSiteForTelegramUserAsync(long telegramUserId, CancellationToken ct)
    {
        if (_currentSite is null || _currentSite.SiteId != Guid.Empty)
            return;

        var siteId = await _db.KeoBiaPlayers.IgnoreQueryFilters()
            .Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => x.SiteId)
            .FirstOrDefaultAsync(ct);

        if (siteId != Guid.Empty)
        {
            _currentSite.Set(siteId, _currentSite.Slug, _currentSite.Theme);
            _logger?.LogInformation("KeoBia chat resolved site {SiteId} for telegram user {User}", siteId, telegramUserId);
        }
        else
        {
            _logger?.LogWarning("KeoBia chat could not resolve site for telegram user {User}", telegramUserId);
        }
    }

    private async Task<List<AiToolDefinition>> BuildKeoBiaChatToolsAsync(CancellationToken ct)
    {
        var allTools = new List<AiToolDefinition>();
        if (_aiTools is not null)
            allTools.AddRange(_aiTools.GetTools());
        if (_externalTools is null || _dataProtection is null)
            return allTools;

        var protector = _dataProtection.CreateProtector("NewsCMS.Ai.ApiKey");
        var externalToolSkills = await _db.AiSkills.AsNoTracking()
            .Where(x => x.ToolType != null && (x.ToolType.StartsWith("firecrawl") || x.ToolType.StartsWith("ninerouter") || x.ToolType == "image_generate") && x.IsActive && !x.IsDeleted)
            .ToListAsync(ct);

        foreach (var tool in _externalTools
            .Where(t => t.Key.StartsWith("firecrawl") || t.Key.StartsWith("ninerouter") || t.Key == "image_generate")
            .OrderBy(t => t.Key == "image_generate" ? 0 : 1))
        {
            var skill = externalToolSkills.FirstOrDefault(s => s.ToolType == tool.Key);
            string? decryptedKey = null;
            if (skill is not null && !string.IsNullOrWhiteSpace(skill.ApiKeyEncrypted))
            {
                try { decryptedKey = protector.Unprotect(skill.ApiKeyEncrypted); }
                catch { }
            }

            var ctx = new AiToolContext(decryptedKey, skill?.BaseUrl, skill?.ConfigJson);
            allTools.Add(new AiToolDefinition(tool.Key, tool.Description, tool.ParametersJsonSchema, (args, toolCt) => tool.ExecuteAsync(args, ctx, toolCt)));
        }

        return allTools;
    }

    private async Task<ChatRoute> ClassifyChatRouteAsync(string baseUrl, string apiKey, string model, string userMessage, int timeoutSeconds, CancellationToken ct)
    {
        if (_aiChatClient is null)
            return ChatRoute.All;
        if (IsImageRequest(userMessage))
            return new ChatRoute("image", "image_only", 1, Array.Empty<string>());

        var messages = new List<(string Role, string Content)>
        {
            ("system", BuildChatRouterPrompt()),
            ("user", userMessage)
        };

        try
        {
            var result = await _aiChatClient.CompleteAsync(baseUrl, apiKey, model, messages, Array.Empty<AiToolDefinition>(), 0, 500, Math.Min(timeoutSeconds, 20), ct);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Value))
                return ChatRoute.All;

            var json = ExtractJsonObject(result.Value!);
            var route = JsonSerializer.Deserialize<ChatRoute>(json, ChatRouteJsonOptions);
            return route is null || !IsAllowedToolPolicy(route.ToolPolicy)
                ? ChatRoute.All
                : route;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "KeoBia AI chat router failed; falling back to all tools.");
            return ChatRoute.All;
        }
    }

    private sealed record ChatRoute(
        [property: JsonPropertyName("intent")] string Intent,
        [property: JsonPropertyName("tool_policy")] string ToolPolicy,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("search_queries")] string[] SearchQueries)
    {
        public static ChatRoute All { get; } = new("general", "all", 0, Array.Empty<string>());
    }

    private async Task<string?> PrimeWebSearchAsync(List<(string Role, string Content)> messages, IReadOnlyList<AiToolDefinition> tools, ChatRoute route, string userMessage, CancellationToken ct)
    {
        if (route.ToolPolicy != "web_only")
            return null;

        var searchTool = tools.FirstOrDefault(x => x.Name == "ninerouter_search")
            ?? tools.FirstOrDefault(x => x.Name == "firecrawl_search");
        if (searchTool is null)
            return "Web search chưa được cấu hình. Vào Admin AI Skills, bật Firecrawl Search hoặc 9Router Search.";

        var query = route.SearchQueries.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? BuildFallbackWebSearchQuery(userMessage);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { query, max_results = 6, search_type = "web" }));
        var searchResult = await searchTool.Invoke(doc.RootElement, ct);
        _logger?.LogInformation("KeoBia AI forced web search via {Tool}: {Query}", searchTool.Name, query);

        messages.Add(("user",
            $"Kết quả web search bắt buộc cho câu hỏi trên:\n" +
            $"Tool: {searchTool.Name}\n" +
            $"Query: {query}\n" +
            $"Result:\n{searchResult}\n\n" +
            "Hãy tổng hợp từ kết quả này. Không nhắc tên tool, provider, ninerouter_search, firecrawl_search, hoặc câu 'dựa vào kết quả web search'. Nếu kết quả lỗi hoặc thiếu dữ liệu, nói rõ thiếu nguồn; không tự nhận là không thể truy cập realtime nếu tool đã trả dữ liệu."));
        return null;
    }

    private static string BuildFallbackWebSearchQuery(string userMessage)
    {
        var normalized = RemoveDiacritics(userMessage).ToLowerInvariant();
        if ((normalized.Contains("knock") || normalized.Contains("loai truc tiep")) &&
            (normalized.Contains("ghi ban") || normalized.Contains("ban thang")) &&
            (normalized.Contains("pen") || normalized.Contains("penalty")))
        {
            return "World Cup 2026 knockout top scorers excluding penalties";
        }

        return $"current football stats {userMessage}";
    }

    private static IReadOnlyList<AiToolDefinition> FilterToolsByRoute(IReadOnlyList<AiToolDefinition> tools, ChatRoute route)
    {
        var filtered = route.ToolPolicy switch
        {
            "keobia_only" => tools.Where(IsKeoBiaTool).ToList(),
            "web_only" => tools.Where(IsWebTool).ToList(),
            "image_only" => tools.Where(x => x.Name == "image_generate").ToList(),
            _ => tools.ToList()
        };

        return filtered.Count == 0 ? tools : filtered;
    }

    private static bool IsKeoBiaTool(AiToolDefinition tool) => tool.Name is
        "search_matches" or
        "search_players" or
        "get_leaderboard" or
        "get_player_history" or
        "get_match_predictions" or
        "get_match_detail" or
        "get_quiz_results" or
        "get_stats";

    private static bool IsWebTool(AiToolDefinition tool) =>
        tool.Name.StartsWith("firecrawl", StringComparison.OrdinalIgnoreCase) ||
        tool.Name.StartsWith("ninerouter", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedToolPolicy(string? policy) => policy is "keobia_only" or "web_only" or "image_only" or "all";

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end >= start ? text[start..(end + 1)] : text;
    }

    private static string BuildChatRouterPrompt() => @"Bạn là router cho chatbot Bia Vui 2026.
Chỉ trả JSON hợp lệ, không markdown, không giải thích.

Phân loại câu hỏi:
- keobia_internal: hỏi người chơi Bia Vui, cốc bia, gói lạc, kèo, bet tỉ số, ai chọn tỉ số nào, lịch sử dự đoán, lịch/trận đã lưu trong game, BXH game, quiz/bình chọn.
- football_web: hỏi bóng đá ngoài dữ liệu game, tin tức mới, cầu thủ bóng đá, đội tuyển, bàn thắng, vua phá lưới, kiến tạo, thẻ phạt, chấn thương, đội hình, FIFA, World Cup, odds, thống kê hiện tại.
- image: yêu cầu tạo ảnh/vẽ ảnh/sinh ảnh/generate image.
- general: câu hỏi chung.

Quy tắc ưu tiên:
- Nếu hỏi cầu thủ, ghi bàn, vua phá lưới, đội tuyển, FIFA hoặc World Cup stats thì football_web, kể cả có chữ BXH.
- Nếu hỏi người chơi, cốc bia, gói lạc, kèo của tôi, tôi dự đoán, ai bet/chọn tỉ số, miss, tặng cốc bia hoặc gói lạc thì keobia_internal.
- Nếu mơ hồ giữa Bia Vui và bóng đá ngoài đời thì tool_policy all.

Schema:
{
  ""intent"": ""keobia_internal|football_web|image|general"",
  ""tool_policy"": ""keobia_only|web_only|image_only|all"",
  ""confidence"": 0.0,
  ""search_queries"": []
}";

    private static string BuildKeoBiaChatSystemPrompt() => @"Bạn là trợ lý AI của Bia Vui 2026 - game dự đoán World Cup 2026: trước tứ kết dùng cốc bia, từ tứ kết ngày 10/07 dùng gói lạc.

BẮT BUỘC: Luôn dùng tool trước khi trả lời. Không bao giờ nói 'không có dữ liệu' mà chưa gọi tool.
BẮT BUỘC: Nếu tool trả về dòng bắt đầu bằng '✅ TÌM THẤY TRẬN' thì trận CÓ trong hệ thống — TUYỆT ĐỐI không được nói trận không tồn tại/chưa được thêm. Nếu 'Số người dự đoán tỉ số 90 phút' = 0, nói thẳng: trận có nhưng chưa ai đoán tỉ số 90 phút (đừng nói trận không có).
BẮT BUỘC: Nếu tool trả về 'KHÔNG KHỚP tên đội' kèm danh sách gợi ý, PHẢI gọi lại tool với tên đội đúng lấy từ danh sách đó trước khi kết luận. Không tự nhận là không tìm thấy khi chưa thử tên gợi ý.

Quy tắc:
1. Hỏi về trận đấu, tỷ số, lịch thi đấu → gọi search_matches hoặc get_match_detail
2. Hỏi về người chơi game Bia Vui, kèo, cốc bia, gói lạc hoặc BXH → gọi search_players hoặc get_leaderboard
3. Hỏi về cầu thủ bóng đá, đội tuyển, bàn thắng, vua phá lưới, kiến tạo, thẻ phạt, thống kê FIFA/World Cup hoặc BXH cầu thủ → gọi firecrawl_search hoặc ninerouter_search NGAY
4. Hỏi tin tức realtime (ghi bàn, đội hình, chấn thương) → gọi firecrawl_search hoặc ninerouter_search NGAY
5. Người dùng yêu cầu tạo ảnh/vẽ ảnh/sinh ảnh/make image/generate image → gọi image_generate NGAY. Sau khi tool trả IMAGE_URL, trả đúng IMAGE_URL và CAPTION, không bịa link.
   Với yêu cầu tạo ảnh, CHỈ dùng image_generate. Không dùng firecrawl_search, firecrawl_scrape, ninerouter_search hoặc ninerouter_fetch, kể cả khi prompt nhắc người nổi tiếng.
6. Hỏi về lịch sử dự đoán → gọi get_player_history. Hỏi ai đang bet/chọn tỉ số trận nào → gọi get_match_predictions hoặc dùng mục DỰ ĐOÁN TỈ SỐ 90 PHÚT trong dữ liệu nội bộ.
7. Hỏi về kết quả bình chọn/câu hỏi nhanh/quiz → gọi get_quiz_results
8. Hỏi tổng quan game Bia Vui → gọi get_stats

Với câu hỏi tin tức hoặc thống kê bóng đá realtime, BẮT BUỘC gọi firecrawl_search hoặc ninerouter_search với query tiếng Anh (ví dụ: 'World Cup 2026 knockout top scorers excluding penalties', 'World Cup 2026 live score today', 'Messi goal World Cup 2026'). Nếu firecrawl_search lỗi hoặc hết credits, dùng ninerouter_search thay thế.
Với yêu cầu tạo ảnh, sau khi gọi image_generate thành công, trả output dạng:
IMAGE_URL: <url>
CAPTION: <mô tả ngắn tiếng Việt>
Không nhắc tên tool, provider, ninerouter_search, firecrawl_search hoặc cách bạn lấy dữ liệu. Không nói ""không có tool"" cho dữ liệu Bia Vui: dữ liệu nội bộ đã được cung cấp trong prompt và qua tool. Trả lời thẳng nội dung người dùng hỏi.
PHONG CÁCH TRẢ LỜI (quan trọng):
- Vào thẳng nội dung. KHÔNG mở đầu bằng 'Dựa vào dữ liệu nội bộ', 'Theo hệ thống', 'Tôi sẽ tìm...' hay câu dẫn tương tự.
- KHÔNG lộ thuật ngữ kỹ thuật. Dịch sang lời thường: home = 'thắng <đội nhà>', away = 'thắng <đội khách>', draw = 'hòa'. Tuyệt đối không viết chữ 'home', 'away', 'draw', 'choice', 'matchId', 'id='.
- KHÔNG lặp lại. Đã liệt kê danh sách thì đừng thêm đoạn văn nhắc lại đúng thông tin vừa liệt kê. Nếu muốn chốt, chỉ 1 câu nhận xét ngắn.
- Mỗi người/mục 1 dòng gọn: tên — dự đoán (— ghi chú ngắn nếu cần).
- Độ dài: 2-5 dòng cho câu hỏi thường, chỉ dài hơn khi thật sự nhiều dữ liệu.
- Tiếng Việt tự nhiên, thân thiện, 1-3 emoji là đủ. KHÔNG markdown (**bold**, *italic*, ```code```), chỉ text thuần + emoji.

Ví dụ ĐÚNG cho câu 'ai bet tỉ số trận Argentina vs Cape Verde Islands':
⚽ Argentina vs Cape Verde Islands (sắp đá)
2 người đã đoán tỉ số:
- Alexander Dang Mammoth: 3-0
- vothanhdat_dgs: 2-0
Cả hai đều tin Argentina thắng. Trận chưa đá nên chưa có kết quả 🍺

Ví dụ SAI (tránh): mở đầu 'Dựa vào dữ liệu nội bộ...', ghi '(home/thắng nhà)', rồi lặp lại 'Alexander dự đoán 3 bàn còn vothanhdat dự đoán 2 bàn'.";

    private async Task<string?> TryGenerateImageDirectlyAsync(string userMessage, CancellationToken ct)
    {
        if (!IsImageRequest(userMessage))
            return null;
        if (_externalTools is null || _dataProtection is null)
            return "Tool tạo ảnh chưa được cấu hình.";

        var imageTool = _externalTools.FirstOrDefault(x => x.Key == "image_generate");
        if (imageTool is null)
            return "Tool tạo ảnh chưa được đăng ký.";

        var skill = await _db.AiSkills.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ToolType == "image_generate" && x.IsActive && !x.IsDeleted, ct);
        if (skill is null)
            return "Tool tạo ảnh chưa được bật. Vào Admin AI Skills, bật tool_image_generate và cấu hình API key.";
        if (string.IsNullOrWhiteSpace(skill.ApiKeyEncrypted))
            return "Tool tạo ảnh thiếu API key. Hãy cấu hình key trong Admin AI Skills.";

        string apiKey;
        try
        {
            apiKey = _dataProtection.CreateProtector("NewsCMS.Ai.ApiKey").Unprotect(skill.ApiKeyEncrypted);
        }
        catch
        {
            return "Không giải mã được API key tạo ảnh. Hãy nhập lại key trong Admin AI Skills.";
        }

        var prompt = ExtractImagePrompt(userMessage);
        if (string.IsNullOrWhiteSpace(prompt))
            return "Bạn muốn tạo ảnh gì? Ví dụ: /chat tạo ảnh mèo đội mũ.";

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { prompt }));
        return await imageTool.ExecuteAsync(doc.RootElement, new AiToolContext(apiKey, skill.BaseUrl, skill.ConfigJson), ct);
    }

    private static bool IsImageRequest(string text)
    {
        var value = RemoveDiacritics(text).ToLowerInvariant();
        return value.Contains("tao anh")
            || value.Contains("ve anh")
            || value.Contains("sinh anh")
            || value.Contains("generate image")
            || value.Contains("make image")
            || value.Contains("draw image");
    }

    private static string ExtractImagePrompt(string text)
    {
        var prompt = Regex.Replace(text, @"(?i)\b(generate image|make image|draw image)\b", " ");
        prompt = Regex.Replace(prompt, @"(?i)\b(tạo|tao|vẽ|ve|sinh)\s+(ảnh|anh|hình|hinh)\b", " ");
        return prompt.Trim(' ', ':', '-', '–');
    }

    private static bool LooksLikeMissingImageToolResponse(string text)
    {
        var value = RemoveDiacritics(text).ToLowerInvariant();
        return (value.Contains("khong co tool")
            || value.Contains("khong co cong cu")
            || value.Contains("khong the tao anh")
            || value.Contains("khong the sinh anh"))
            && (value.Contains("tao anh") || value.Contains("sinh anh") || value.Contains("image"));
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public async Task<Result<KeoBiaShareBeerResultDto>> ShareBeerAsync(KeoBiaShareBeerDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.FromPublicKey))
            return Result<KeoBiaShareBeerResultDto>.Failure("Thiếu mã người tặng.");
        if (string.IsNullOrWhiteSpace(dto.ToPublicKey))
            return Result<KeoBiaShareBeerResultDto>.Failure("Thiếu mã người nhận.");
        if (dto.Cups < 1 || dto.Cups > 99)
            return Result<KeoBiaShareBeerResultDto>.Failure("Số lượng không hợp lệ (1-99).");

        var fromKey = dto.FromPublicKey.Trim();
        var toKey = dto.ToPublicKey.Trim();
        if (fromKey == toKey)
            return Result<KeoBiaShareBeerResultDto>.Failure("Không thể tự tặng cho chính mình.");

        var fromPlayer = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.PublicKey == fromKey, ct);
        if (fromPlayer is null) return Result<KeoBiaShareBeerResultDto>.Failure("Không tìm thấy người tặng.");

        var toPlayer = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.PublicKey == toKey, ct);
        if (toPlayer is null) return Result<KeoBiaShareBeerResultDto>.Failure("Không tìm thấy người nhận.");

        // Lost-bet cups are unchanged by a gift, so query them up front to stamp a real
        // BalanceAfter on each cup log instead of a meaningless 0.
        var fromLostBetCups = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.PlayerId == fromPlayer.Id && x.IsSettled && x.IsCorrect == false)
            .SumAsync(x => (int?)x.Cups, ct) ?? 0;

        var toLostBetCups = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.PlayerId == toPlayer.Id && x.IsSettled && x.IsCorrect == false)
            .SumAsync(x => (int?)x.Cups, ct) ?? 0;

        fromPlayer.SharedCups += dto.Cups;
        var unitCode = KeoBiaUnitRules.GetCurrentUnitCode();
        var unitText = KeoBiaUnitRules.FormatCount(dto.Cups, unitCode);

        var reduce = Math.Min(toPlayer.PenaltyCups, dto.Cups);
        toPlayer.PenaltyCups -= reduce;

        AddCupLog(fromPlayer, null, KeoBiaCupChangeType.SharedBeer, dto.Cups,
            $"Tặng {unitText} cho {toPlayer.DisplayName}",
            fromLostBetCups + fromPlayer.PenaltyCups + fromPlayer.SharedCups);
        AddCupLog(toPlayer, null, KeoBiaCupChangeType.ReceivedBeer, -dto.Cups,
            $"Nhận {unitText} từ {fromPlayer.DisplayName}",
            toLostBetCups + toPlayer.PenaltyCups);

        await _db.SaveChangesAsync(ct);

        AddActivity(new KeoBiaActivity
        {
            PlayerId = fromPlayer.Id,
            PlayerName = fromPlayer.DisplayName,
            AvatarUrl = fromPlayer.AvatarUrl,
            MatchId = null,
            ActivityType = "share",
            Text = fromPlayer.DisplayName + " đã tặng " + unitText + " cho " + toPlayer.DisplayName,
            Choice = "share",
            ChoiceLabel = unitText,
            Cups = dto.Cups,
            Badge = "sports_bar"
        });

        var fromNewLostCups = await ComputeLossBalanceAsync(fromPlayer, ct);
        var toNewLostCups = await ComputeLossBalanceAsync(toPlayer, ct);
        var fromUnits = await ComputeLossUnitBalanceAsync(fromPlayer.Id, fromNewLostCups, ct);
        var toUnits = await ComputeLossUnitBalanceAsync(toPlayer.Id, toNewLostCups, ct);

        return Result<KeoBiaShareBeerResultDto>.Success(new KeoBiaShareBeerResultDto(
            fromPlayer.DisplayName,
            toPlayer.DisplayName,
            dto.Cups,
            fromNewLostCups,
            toNewLostCups,
            fromUnits.BeerCups,
            fromUnits.PeanutPacks,
            toUnits.BeerCups,
            toUnits.PeanutPacks));
    }

    public async Task<Result<KeoBiaDeleteBetResultDto>> DeletePlayerBetAsync(string publicKey, Guid betId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
            return Result<KeoBiaDeleteBetResultDto>.Failure("Thiếu mã publicKey.");

        var key = publicKey.Trim();
        var bet = await _db.KeoBiaBets
            .Include(x => x.Player)
            .Include(x => x.Match)
            .FirstOrDefaultAsync(x => x.Id == betId, ct);

        if (bet is null)
            return Result<KeoBiaDeleteBetResultDto>.Failure("Không tìm thấy lượt dự đoán.");

        if (!string.Equals(bet.Player.PublicKey, key, StringComparison.OrdinalIgnoreCase))
            return Result<KeoBiaDeleteBetResultDto>.Failure("Bạn không có quyền xoá lượt dự đoán này.");

        if (bet.IsSettled || bet.Match.Status == KeoBiaMatchStatus.Finished)
            return Result<KeoBiaDeleteBetResultDto>.Failure("Trận đã có kết quả, không thể xoá.");

        if (bet.Match.Status != KeoBiaMatchStatus.Scheduled)
            return Result<KeoBiaDeleteBetResultDto>.Failure("Trận không còn mở, không thể xoá.");

        if (DateTime.UtcNow >= bet.Match.KickoffAt.Add(-VoteLockWindow))
            return Result<KeoBiaDeleteBetResultDto>.Failure("Chỉ được xoá trước giờ bóng lăn tối thiểu 20 phút.");

        var activityWindowStart = bet.CreatedAt.AddMinutes(-2);
        var activityWindowEnd = bet.CreatedAt.AddMinutes(2);
        var activityCandidates = await _db.KeoBiaActivities
            .Where(x =>
                x.PlayerId == bet.PlayerId &&
                x.MatchId == bet.MatchId &&
                x.ActivityType == KeoBiaActivityType.Prediction &&
                x.Choice == bet.Choice &&
                x.Cups == bet.Cups &&
                x.CreatedAt >= activityWindowStart &&
                x.CreatedAt <= activityWindowEnd)
            .ToListAsync(ct);

        var activitiesToRemove = activityCandidates
            .OrderBy(x => Math.Abs((x.CreatedAt - bet.CreatedAt).Ticks))
            .Take(1)
            .ToList();
        var activityIds = activitiesToRemove.Select(x => x.Id).ToList();

        if (activitiesToRemove.Count > 0)
            _db.KeoBiaActivities.RemoveRange(activitiesToRemove);

        var playerId = bet.PlayerId;
        var matchId = bet.MatchId;
        var choice = bet.Choice;
        var cups = bet.Cups;

        if (bet.StarType == "hope")
            bet.Player.HopeStars += 1;
        else if (bet.StarType == "devil")
            bet.Player.DevilStars += 1;

        if (bet.StarType != null)
        {
            var starLogs = await _db.KeoBiaCupLogs
                .Where(x => x.PlayerId == playerId && x.MatchId == matchId
                    && (x.ChangeType == KeoBiaCupChangeType.UsedHopeStar
                     || x.ChangeType == KeoBiaCupChangeType.UsedDevilStar))
                .ToListAsync(ct);
            if (starLogs.Count > 0)
                _db.KeoBiaCupLogs.RemoveRange(starLogs);
        }

        _db.KeoBiaBets.Remove(bet);
        await _db.SaveChangesAsync(ct);

        var history = await GetPlayerHistoryAsync(key, 100, ct);
        if (!history.Succeeded || history.Value is null)
            return Result<KeoBiaDeleteBetResultDto>.Failure(history.Error ?? "Không tải được lịch sử sau khi xoá.");

        return Result<KeoBiaDeleteBetResultDto>.Success(new KeoBiaDeleteBetResultDto(
            betId,
            matchId,
            choice,
            cups,
            activityIds,
            history.Value));
    }

    public async Task<IReadOnlyList<KeoBiaImportJobDto>> GetRecentImportJobsAsync(int take = 10, CancellationToken ct = default) =>
        await _db.KeoBiaImportJobs.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new KeoBiaImportJobDto(x.Id, x.Source, x.CreatedAt, x.TotalRows, x.ImportedRows, x.SkippedRows, x.ErrorRows, x.ErrorLog))
            .ToListAsync(ct);

    public async Task<Result<Guid>> UpsertMatchAsync(KeoBiaMatchUpsertDto dto, CancellationToken ct = default)
    {
        var validation = ValidateMatch(dto);
        if (validation != null) return Result<Guid>.Failure(validation);

        var externalId = dto.ExternalId.Trim();
        var entity = dto.Id.HasValue
            ? await _db.KeoBiaMatches.FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct)
            : await _db.KeoBiaMatches.FirstOrDefaultAsync(x => x.ExternalId == externalId, ct);

        if (entity == null)
        {
            entity = new KeoBiaMatch { ExternalId = externalId };
            _db.KeoBiaMatches.Add(entity);
        }
        else if (await _db.KeoBiaMatches.AnyAsync(x => x.Id != entity.Id && x.ExternalId == externalId, ct))
        {
            return Result<Guid>.Failure("Mã trận đã tồn tại.");
        }

        ApplyMatch(entity, dto);
        await _db.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }

    public async Task<Result> UpdateResultAsync(Guid id, int? homeScore, int? awayScore, string status, CancellationToken ct = default, int? penaltyHomeScore = null, int? penaltyAwayScore = null)
    {
        status = NormalizeStatus(status);
        var match = await _db.KeoBiaMatches
            .Include(x => x.Bets)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (match == null) return Result.Failure("Không tìm thấy trận đấu.");

        match.HomeScore = homeScore;
        match.AwayScore = awayScore;
        match.PenaltyHomeScore = penaltyHomeScore;
        match.PenaltyAwayScore = penaltyAwayScore;
        match.Status = status;
        match.ResultChoice = status == KeoBiaMatchStatus.Finished && homeScore.HasValue && awayScore.HasValue
            ? ResolveChoice(homeScore.Value, awayScore.Value)
            : null;
        match.ResultHomeScore = match.ResultChoice == null ? null : homeScore;
        match.ResultAwayScore = match.ResultChoice == null ? null : awayScore;
        match.ResultUpdatedAt = DateTime.UtcNow;
        match.UpdatedAt = DateTime.UtcNow;

        var affectedPlayers = match.Bets.Select(x => x.PlayerId).Distinct().ToHashSet();
        foreach (var bet in match.Bets)
        {
            bet.IsSettled = match.ResultChoice != null;
            bet.IsCorrect = match.ResultChoice == null ? null : bet.Choice == match.ResultChoice;
            bet.SettledAt = match.ResultChoice == null ? null : DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        var verifiedPlayerIds = await GetVerifiedPlayerIdsAsync(ct);
        affectedPlayers.UnionWith(verifiedPlayerIds);
        await RecalculatePlayersAsync(affectedPlayers, ct);
        return Result.Success();
    }

    public async Task<Result> UpdateCorrectScoreOddsAsync(Guid id, string? oddsJson, CancellationToken ct = default)
    {
        var match = await _db.KeoBiaMatches.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (match is null) return Result.Failure("Không tìm thấy trận đấu.");

        var normalized = NormalizeCorrectScoreOddsJson(oddsJson, out var error);
        if (error is not null) return Result.Failure(error);

        match.CorrectScoreOddsJson = normalized;
        match.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<KeoBiaImportResultDto>> ImportScheduleCsvAsync(string csv, string source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return Result<KeoBiaImportResultDto>.Failure("CSV đang trống.");

        var rows = ReadCsv(csv);
        if (rows.Count == 0)
            return Result<KeoBiaImportResultDto>.Failure("Không đọc được dòng dữ liệu nào.");

        var imported = 0;
        var skipped = 0;
        var errors = new StringBuilder();

        foreach (var row in rows)
        {
            try
            {
                var dto = RowToMatchDto(row);
                var result = await UpsertMatchAsync(dto, ct);
                if (result.Succeeded) imported++;
                else
                {
                    skipped++;
                    errors.AppendLine($"{row.RowNumber}: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                skipped++;
                errors.AppendLine($"{row.RowNumber}: {ex.Message}");
            }
        }

        var job = new KeoBiaImportJob
        {
            Source = string.IsNullOrWhiteSpace(source) ? "Admin CSV" : source.Trim(),
            TotalRows = rows.Count,
            ImportedRows = imported,
            SkippedRows = skipped,
            ErrorRows = skipped,
            ErrorLog = errors.Length == 0 ? null : errors.ToString()
        };
        _db.KeoBiaImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        return Result<KeoBiaImportResultDto>.Success(new KeoBiaImportResultDto(rows.Count, imported, skipped, skipped, job.ErrorLog));
    }

    public async Task<Result<KeoBiaImportResultDto>> SyncOpenFootballWorldCupAsync(string json, string source, bool applyResults = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result<KeoBiaImportResultDto>.Failure("JSON đang trống.");

        using var doc = ParseJson(json);
        if (doc == null)
            return Result<KeoBiaImportResultDto>.Failure("JSON không hợp lệ.");

        if (!doc.RootElement.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array)
            return Result<KeoBiaImportResultDto>.Failure("JSON không có mảng matches.");

        var total = matches.GetArrayLength();
        var imported = 0;
        var skipped = 0;
        var errors = new StringBuilder();
        var affectedPlayerIds = new HashSet<Guid>();
        var rowNumber = 0;

        foreach (var item in matches.EnumerateArray())
        {
            rowNumber++;
            try
            {
                var parsed = OpenFootballMatchToDto(item, rowNumber);
                var validation = ValidateMatch(parsed.Match);
                if (validation != null)
                {
                    skipped++;
                    errors.AppendLine($"{rowNumber}: {validation}");
                    continue;
                }

                var externalId = parsed.Match.ExternalId.Trim();
                var match = await _db.KeoBiaMatches
                    .Include(x => x.Bets)
                    .FirstOrDefaultAsync(x => x.ExternalId == externalId, ct);

                var previousStatus = match?.Status;
                if (match == null)
                {
                    match = new KeoBiaMatch { ExternalId = externalId };
                    _db.KeoBiaMatches.Add(match);
                }

                // Schedule-only mode (applyResults=false): OpenFootball is now only the
                // fixture source. Football-Data is the result authority, so don't let
                // OpenFootball mark a match Finished or settle bets.
                var matchToApply = applyResults || parsed.Match.Status != KeoBiaMatchStatus.Finished
                    ? parsed.Match
                    : parsed.Match with { Status = KeoBiaMatchStatus.Scheduled };
                ApplyMatch(match, matchToApply);

                if (applyResults && parsed.HomeScore.HasValue && parsed.AwayScore.HasValue)
                {
                    ApplyResult(match, parsed.HomeScore.Value, parsed.AwayScore.Value, KeoBiaMatchStatus.Finished);
                    foreach (var playerId in match.Bets.Select(x => x.PlayerId).Distinct())
                        affectedPlayerIds.Add(playerId);
                }
                else if (!string.IsNullOrWhiteSpace(previousStatus) && previousStatus != KeoBiaMatchStatus.Scheduled)
                {
                    // Preserve a result already settled by Football-Data across schedule syncs.
                    match.Status = previousStatus;
                }

                imported++;
            }
            catch (Exception ex)
            {
                skipped++;
                errors.AppendLine($"{rowNumber}: {ex.Message}");
            }
        }

        var job = new KeoBiaImportJob
        {
            Source = string.IsNullOrWhiteSpace(source) ? "OpenFootball World Cup 2026" : source.Trim(),
            TotalRows = total,
            ImportedRows = imported,
            SkippedRows = skipped,
            ErrorRows = skipped,
            ErrorLog = errors.Length == 0 ? null : errors.ToString()
        };
        _db.KeoBiaImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        if (affectedPlayerIds.Count > 0)
        {
            var verifiedPlayerIds = await GetVerifiedPlayerIdsAsync(ct);
            affectedPlayerIds.UnionWith(verifiedPlayerIds);
        }
        await RecalculatePlayersAsync(affectedPlayerIds, ct);

        return Result<KeoBiaImportResultDto>.Success(new KeoBiaImportResultDto(total, imported, skipped, skipped, job.ErrorLog));
    }

    // Football-Data.org is now the single source of truth for both the fixture schedule
    // (incl. knockout teams that resolve only after the group stage) and match results /
    // bia settlement. Matches are reconciled against existing rows by exact kickoff time
    // (the official schedule is identical across sources), with team identity used to
    // disambiguate the simultaneous final-round group games. This keeps existing bets
    // attached to their match instead of creating duplicate rows.
    public async Task<Result<KeoBiaImportResultDto>> SyncFootballDataAsync(string json, string source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result<KeoBiaImportResultDto>.Failure("JSON đang trống.");

        using var doc = ParseJson(json);
        if (doc == null)
            return Result<KeoBiaImportResultDto>.Failure("JSON không hợp lệ.");

        if (!doc.RootElement.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array)
            return Result<KeoBiaImportResultDto>.Failure("JSON không có mảng matches.");

        var total = matches.GetArrayLength();
        var feed = new List<FootballDataMatch>(total);
        foreach (var item in matches.EnumerateArray())
        {
            var parsed = TryParseFootballDataMatch(item);
            if (parsed != null) feed.Add(parsed);
        }

        // Apply earliest first so an already-adopted row can't be re-adopted by a later fixture.
        feed.Sort((a, b) => a.KickoffUtc.CompareTo(b.KickoffUtc));

        var dbMatches = await _db.KeoBiaMatches
            .Include(x => x.Bets)
            .Where(x => x.Status != KeoBiaMatchStatus.Cancelled)
            .ToListAsync(ct);
        var byKickoff = dbMatches
            .GroupBy(x => x.KickoffAt)
            .ToDictionary(g => g.Key, g => g.ToList());
        var adoptedDbIds = new HashSet<Guid>();

        var imported = 0;
        var skipped = 0;
        var errors = new StringBuilder();
        var affectedPlayerIds = new HashSet<Guid>();

        foreach (var fd in feed)
        {
            try
            {
                var match = ResolveFootballDataMatch(fd, dbMatches, byKickoff, adoptedDbIds, out var reason);
                if (match == null)
                {
                    // A brand-new fixture with concrete teams that isn't on the calendar yet — create it.
                    if (fd.HasConcreteTeams)
                    {
                        match = new KeoBiaMatch { ExternalId = $"fdwc-{fd.Id}" };
                        _db.KeoBiaMatches.Add(match);
                        ApplyMatch(match, BuildFootballDataMatchDto(fd, $"fdwc-{fd.Id}"));
                    }
                    else
                    {
                        // Unresolved knockout slot with no calendar match — nothing to do yet.
                        if (fd.IsFinished)
                        {
                            skipped++;
                            errors.AppendLine($"{fd.Label} ({fd.KickoffUtc:yyyy-MM-dd HH:mm}): {reason}");
                        }
                        continue;
                    }
                }
                else
                {
                    adoptedDbIds.Add(match.Id);
                }

                var changed = UpdateScheduleFromFootballData(match, fd);

                var settled = false;
                if (fd.IsFinished && fd.HomeScore.HasValue && fd.AwayScore.HasValue && fd.ResultHomeScore.HasValue && fd.ResultAwayScore.HasValue)
                {
                    var resultChoice = ResolveChoice(fd.ResultHomeScore.Value, fd.ResultAwayScore.Value);
                    var alreadySettled = match.Status == KeoBiaMatchStatus.Finished
                        && match.ResultChoice == resultChoice
                        && match.HomeScore == fd.HomeScore
                        && match.AwayScore == fd.AwayScore
                        && match.PenaltyHomeScore == fd.PenaltyHomeScore
                        && match.PenaltyAwayScore == fd.PenaltyAwayScore;
                    if (!alreadySettled)
                    {
                        ApplyResult(match, fd.HomeScore.Value, fd.AwayScore.Value, KeoBiaMatchStatus.Finished, resultChoice, fd.ResultHomeScore, fd.ResultAwayScore, fd.UpdatedAtUtc);
                        match.PenaltyHomeScore = fd.PenaltyHomeScore;
                        match.PenaltyAwayScore = fd.PenaltyAwayScore;
                        foreach (var playerId in match.Bets.Select(x => x.PlayerId).Distinct())
                            affectedPlayerIds.Add(playerId);
                        settled = true;
                    }
                }

                if (changed || settled) imported++;
            }
            catch (Exception ex)
            {
                skipped++;
                errors.AppendLine($"{fd.Label}: {ex.Message}");
            }
        }

        var job = new KeoBiaImportJob
        {
            Source = string.IsNullOrWhiteSpace(source) ? "Football-Data World Cup 2026" : source.Trim(),
            TotalRows = total,
            ImportedRows = imported,
            SkippedRows = skipped,
            ErrorRows = skipped,
            ErrorLog = errors.Length == 0 ? null : errors.ToString()
        };
        _db.KeoBiaImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        // Recompute every verified player (not just those with bets on settled matches): missed
        // matches auto-count as one lost cup as soon as they kick off.
        var verifiedPlayerIds = await GetVerifiedPlayerIdsAsync(ct);
        affectedPlayerIds.UnionWith(verifiedPlayerIds);
        await RecalculatePlayersAsync(affectedPlayerIds, ct);

        return Result<KeoBiaImportResultDto>.Success(new KeoBiaImportResultDto(total, imported, skipped, skipped, job.ErrorLog));
    }

    // Map a Football-Data fixture onto an existing match row.
    // 1) exact kickoff slot — disambiguated by team identity when several games kick off together;
    // 2) same calendar day fallback (guards against a one-sided time correction) requiring both teams.
    private KeoBiaMatch? ResolveFootballDataMatch(
        FootballDataMatch fd,
        List<KeoBiaMatch> all,
        Dictionary<DateTime, List<KeoBiaMatch>> byKickoff,
        HashSet<Guid> adopted,
        out string reason)
    {
        reason = "";

        var atKickoff = byKickoff.TryGetValue(fd.KickoffUtc, out var slot)
            ? slot.Where(x => !adopted.Contains(x.Id)).ToList()
            : new List<KeoBiaMatch>();

        if (atKickoff.Count == 1 && (!fd.HasConcreteTeams || FootballDataSideScore(fd, atKickoff[0]) >= 1 || HasPlaceholderTeams(atKickoff[0])))
            return atKickoff[0];

        if (atKickoff.Count > 0)
        {
            var picked = PickBestFootballDataMatch(fd, atKickoff, requireBoth: atKickoff.Count > 1);
            if (picked != null) return picked;
        }

        if (fd.HasConcreteTeams)
        {
            var sameDay = all.Where(x => !adopted.Contains(x.Id) && x.KickoffAt.Date == fd.KickoffUtc.Date).ToList();
            var picked = PickBestFootballDataMatch(fd, sameDay, requireBoth: true);
            if (picked != null) return picked;
        }

        // Same-day stage-aware fallback: when Football-Data has no concrete teams yet
        // (unresolved knockout slot) and the kickoff time doesn't match exactly (timezone
        // rounding differences between sources), try to find a same-day DB match with
        // placeholder teams in the same stage. Allow ±1 day tolerance for timezone drift.
        if (!fd.HasConcreteTeams)
        {
            var fdStage = MapFootballDataStage(fd.Stage, fd.Group);
            if (!string.IsNullOrWhiteSpace(fdStage))
            {
                var nearDayPlaceholder = all
                    .Where(x => !adopted.Contains(x.Id)
                        && Math.Abs((x.KickoffAt.Date - fd.KickoffUtc.Date).TotalDays) <= 1
                        && HasPlaceholderTeams(x)
                        && string.Equals(x.Stage, fdStage, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (nearDayPlaceholder.Count == 1)
                    return nearDayPlaceholder[0];
            }
        }

        reason = atKickoff.Count == 0
                ? "không tìm thấy trận trống giờ"
            : "giờ trống nhưng không khớp được đội";
        return null;
    }

    private static KeoBiaMatch? PickBestFootballDataMatch(FootballDataMatch fd, List<KeoBiaMatch> candidates, bool requireBoth)
    {
        var bestScore = 0;
        KeoBiaMatch? best = null;
        KeoBiaMatch? placeholderFallback = null;
        var tie = false;
        foreach (var c in candidates)
        {
            var score = FootballDataSideScore(fd, c);
            if (score == 0)
            {
                // A zero-score candidate with placeholder teams is still usable
                // when no concrete match exists — the placeholders get replaced
                // by UpdateScheduleFromFootballData after adoption.
                if (placeholderFallback == null && fd.HasConcreteTeams && HasPlaceholderTeams(c))
                    placeholderFallback = c;
                continue;
            }
            if (requireBoth && score < 2) continue;
            if (score > bestScore) { bestScore = score; best = c; tie = false; }
            else if (score == bestScore) tie = true;
        }
        if (best != null && !tie) return best;
        return placeholderFallback;
    }

    private static int FootballDataSideScore(FootballDataMatch fd, KeoBiaMatch c)
    {
        var home = FootballDataSideMatches(fd.HomeTla, fd.HomeName, fd.HomeShortName, c.HomeCode, c.HomeName);
        var away = FootballDataSideMatches(fd.AwayTla, fd.AwayName, fd.AwayShortName, c.AwayCode, c.AwayName);
        return (home ? 1 : 0) + (away ? 1 : 0);
    }

    private static bool FootballDataSideMatches(string? tla, string? name, string? shortName, string dbCode, string dbName)
    {
        var fdCode = CanonTeamCode(tla);
        if (fdCode.Length > 0 && fdCode == CanonTeamCode(dbCode)) return true;

        var dbTokens = TeamNameTokens(dbName);
        if (dbTokens.Count == 0) return false;
        return TeamNameTokens(name).Overlaps(dbTokens) || TeamNameTokens(shortName).Overlaps(dbTokens);
    }

    // Detect bracket placeholder names like "2A", "1B", "TBD", "W74", "3A/B/C/D/F", etc.
    // These are used by OpenFootball / Football-Data for knockout slots before groups are settled.
    private static bool HasPlaceholderTeams(KeoBiaMatch m) =>
        IsPlaceholderTeam(m.HomeCode, m.HomeName) || IsPlaceholderTeam(m.AwayCode, m.AwayName);

    private static bool IsPlaceholderTeam(string code, string name)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        if (c is "TBD" or "TBA" or "TBC") return true;
        // Bracket position: digit + letter(s), e.g. "2A", "1B", "3BC"
        if (c.Length is >= 2 and <= 4 && char.IsDigit(c[0]) && c.Skip(1).All(char.IsLetter)) return true;
        // Winner-of-match reference: letter(s) + digits, e.g. "W74", "W89"
        if (c.Length is >= 2 and <= 4 && char.IsLetter(c[0]) && c.Skip(1).All(char.IsDigit)) return true;

        var n = (name ?? "").Trim();
        if (n.Equals("TBD", StringComparison.OrdinalIgnoreCase)
            || n.Equals("TBA", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Winner Group", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Winner of Group", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Runner-up Group", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Runner up Group", StringComparison.OrdinalIgnoreCase))
            return true;
        // Multi-group 3rd-place placeholder, e.g. "3A/B/C/D/F"
        if (n.Contains('/')) return true;
        // Short bracket code as name, e.g. "2A", "1B", "W74"
        if (n.Length is >= 2 and <= 4 && char.IsDigit(n[0]) && n.Skip(1).All(char.IsLetterOrDigit)) return true;
        if (n.Length is >= 2 and <= 4 && char.IsLetter(n[0]) && n.Skip(1).All(char.IsDigit)) return true;
        return false;
    }

    private static string CanonTeamCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "" : new string(code.Where(char.IsLetter).ToArray()).ToUpperInvariant();

    private static HashSet<string> TeamNameTokens(string? name)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(name)) return set;
        foreach (var token in ToSlugToken(name).Split('-', StringSplitOptions.RemoveEmptyEntries))
            if (token.Length >= 2) set.Add(token);
        return set;
    }

    // Refresh schedule fields (notably knockout teams that only resolve after the group stage).
    // Returns true when something actually changed so we don't churn unchanged rows.
    private static bool UpdateScheduleFromFootballData(KeoBiaMatch match, FootballDataMatch fd)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(fd.HomeName))
        {
            var name = fd.HomeName!.Trim();
            var code = (!string.IsNullOrWhiteSpace(fd.HomeTla) ? fd.HomeTla! : ToTeamCode(name)).Trim().ToUpperInvariant();
            if (match.HomeName != name) { match.HomeName = name; changed = true; }
            if (match.HomeCode != code) { match.HomeCode = code; changed = true; }
        }
        if (!string.IsNullOrWhiteSpace(fd.AwayName))
        {
            var name = fd.AwayName!.Trim();
            var code = (!string.IsNullOrWhiteSpace(fd.AwayTla) ? fd.AwayTla! : ToTeamCode(name)).Trim().ToUpperInvariant();
            if (match.AwayName != name) { match.AwayName = name; changed = true; }
            if (match.AwayCode != code) { match.AwayCode = code; changed = true; }
        }

        var stage = MapFootballDataStage(fd.Stage, fd.Group);
        if (!string.IsNullOrWhiteSpace(stage) && match.Stage != stage) { match.Stage = stage; changed = true; }

        if (fd.HasConcreteTeams)
        {
            var hot = IsOpenFootballHotMatch(match.HomeName, match.AwayName);
            if (match.IsHot != hot)
            {
                match.IsHot = hot;
                match.HotLabel = hot ? BuildOpenFootballHotLabel(match.HomeName, match.AwayName) : null;
                changed = true;
            }
        }

        if (changed) match.UpdatedAt = DateTime.UtcNow;
        return changed;
    }

    private static KeoBiaMatchUpsertDto BuildFootballDataMatchDto(FootballDataMatch fd, string externalId)
    {
        var homeName = (fd.HomeName ?? "TBD").Trim();
        var awayName = (fd.AwayName ?? "TBD").Trim();
        var homeCode = (!string.IsNullOrWhiteSpace(fd.HomeTla) ? fd.HomeTla! : ToTeamCode(homeName)).Trim().ToUpperInvariant();
        var awayCode = (!string.IsNullOrWhiteSpace(fd.AwayTla) ? fd.AwayTla! : ToTeamCode(awayName)).Trim().ToUpperInvariant();
        var stage = MapFootballDataStage(fd.Stage, fd.Group);
        if (string.IsNullOrWhiteSpace(stage)) stage = "World Cup 2026";
        var isHot = fd.HasConcreteTeams && IsOpenFootballHotMatch(homeName, awayName);

        return new KeoBiaMatchUpsertDto(
            null, externalId, stage, homeName, homeCode, "#1f7a3a", "#176030",
            awayName, awayCode, "#1565c0", "#0a335f",
            fd.KickoffUtc, "World Cup 2026", isHot, isHot ? BuildOpenFootballHotLabel(homeName, awayName) : null,
            KeoBiaMatchStatus.Scheduled, 40, 20, 40, 0, 0, 0, null, 1);
    }

    private static string MapFootballDataStage(string? stage, string? group)
    {
        var s = (stage ?? "").Trim().ToUpperInvariant();
        switch (s)
        {
            case "LAST_32": return "Round of 32";
            case "LAST_16": return "Round of 16";
            case "QUARTER_FINALS": case "QUARTER_FINAL": return "Quarter-final";
            case "SEMI_FINALS": case "SEMI_FINAL": return "Semi-final";
            case "THIRD_PLACE": case "3RD_PLACE": return "Match for third place";
            case "FINAL": return "Final";
        }

        var g = (group ?? "").Trim();
        if (g.StartsWith("GROUP_", StringComparison.OrdinalIgnoreCase))
            return "Group " + g[6..].ToUpperInvariant();
        if (g.StartsWith("GROUP ", StringComparison.OrdinalIgnoreCase))
            return "Group " + g[6..].Trim().ToUpperInvariant();
        return "";
    }

    private static FootballDataMatch? TryParseFootballDataMatch(JsonElement item)
    {
        if (!TryGetProperty(item, out var home, "homeTeam") || !TryGetProperty(item, out var away, "awayTeam"))
            return null;

        var dateRaw = ReadString(item, "utcDate", "date");
        if (string.IsNullOrWhiteSpace(dateRaw)
            || !DateTime.TryParse(dateRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var kickoff))
            return null;
        kickoff = DateTime.SpecifyKind(kickoff, DateTimeKind.Utc);

        long.TryParse(ReadString(item, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
        var updatedRaw = ReadString(item, "lastUpdated", "updatedAt");
        DateTime? updatedAt = null;
        if (!string.IsNullOrWhiteSpace(updatedRaw)
            && DateTime.TryParse(updatedRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedUpdatedAt))
        {
            updatedAt = DateTime.SpecifyKind(parsedUpdatedAt, DateTimeKind.Utc);
        }

        var status = (ReadString(item, "status") ?? "").Trim().ToUpperInvariant();
        var isFinished = status is "FINISHED" or "AWARDED";

        int? homeScore = null, awayScore = null, resultHomeScore = null, resultAwayScore = null, penaltyHomeScore = null, penaltyAwayScore = null;
        if (isFinished
            && TryGetProperty(item, out var score, "score")
            && TryGetProperty(score, out var fullTime, "fullTime", "ft"))
        {
            var duration = (ReadString(score, "duration") ?? ReadString(item, "duration") ?? "").Trim().ToUpperInvariant();
            var needsRegularTime = duration is "EXTRA_TIME" or "PENALTY_SHOOTOUT" or "PENALTIES";

            if (TryReadInt(fullTime, out var hs, "home", "homeTeam", "score1")) homeScore = hs;
            if (TryReadInt(fullTime, out var as_, "away", "awayTeam", "score2")) awayScore = as_;

            if (TryGetProperty(score, out var regularTime, "regularTime", "regular"))
            {
                if (TryReadInt(regularTime, out var rhs, "home", "homeTeam", "score1")) resultHomeScore = rhs;
                if (TryReadInt(regularTime, out var ras, "away", "awayTeam", "score2")) resultAwayScore = ras;
            }

            if (needsRegularTime
                && (!resultHomeScore.HasValue || !resultAwayScore.HasValue)
                && TryGetProperty(score, out var extraTime, "extraTime", "et")
                && TryReadInt(extraTime, out var eth, "home", "homeTeam", "score1")
                && TryReadInt(extraTime, out var eta, "away", "awayTeam", "score2")
                && homeScore.HasValue
                && awayScore.HasValue
                && homeScore.Value >= eth
                && awayScore.Value >= eta)
            {
                resultHomeScore = homeScore.Value - eth;
                resultAwayScore = awayScore.Value - eta;
            }

            if (needsRegularTime)
            {
                // Football-Data reports shootout winner score in fullTime for WC feed
                // (example SUI-COL: fullTime 4-3, penalties 3-3).
                if (duration is "PENALTY_SHOOTOUT" or "PENALTIES")
                {
                    penaltyHomeScore = homeScore;
                    penaltyAwayScore = awayScore;
                }

                if (KeoBiaStage.AllowsDraw(kickoff) && resultHomeScore.HasValue && resultAwayScore.HasValue)
                {
                    homeScore = resultHomeScore;
                    awayScore = resultAwayScore;
                }
                else if (!KeoBiaStage.AllowsDraw(kickoff))
                {
                    resultHomeScore = homeScore;
                    resultAwayScore = awayScore;
                }
                else
                {
                    homeScore = null;
                    awayScore = null;
                }
            }
            else if (!resultHomeScore.HasValue || !resultAwayScore.HasValue)
            {
                resultHomeScore = homeScore;
                resultAwayScore = awayScore;
            }
        }

        return new FootballDataMatch(
            id,
            kickoff,
            ReadString(home, "name"),
            ReadString(home, "shortName"),
            ReadString(home, "tla"),
            ReadString(away, "name"),
            ReadString(away, "shortName"),
            ReadString(away, "tla"),
            ReadString(item, "stage"),
            ReadString(item, "group"),
            isFinished,
            homeScore,
            awayScore,
            resultHomeScore,
            resultAwayScore,
            penaltyHomeScore,
            penaltyAwayScore,
            updatedAt);
    }

    public async Task<Result<KeoBiaPlayerProfileUpsertResultDto>> UpsertPlayerAsync(KeoBiaPlayerUpsertDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.PublicKey))
            return Result<KeoBiaPlayerProfileUpsertResultDto>.Failure("Thiếu mã publicKey.");

        var ensured = await EnsurePlayerAsync(dto, ct);
        if (!string.IsNullOrWhiteSpace(ensured.Error))
            return Result<KeoBiaPlayerProfileUpsertResultDto>.Failure(ensured.Error);

        if (ensured.ClaimCandidate != null)
        {
            return Result<KeoBiaPlayerProfileUpsertResultDto>.Success(new KeoBiaPlayerProfileUpsertResultDto(
                null,
                true,
                false,
                new KeoBiaPlayerClaimCandidateDto(
                    ensured.ClaimCandidate.Id,
                    ensured.ClaimCandidate.PublicKey,
                    ensured.ClaimCandidate.DisplayName,
                    ensured.ClaimCandidate.AvatarUrl)));
        }

        var player = ensured.Player!;
        if (ensured.IsNewPlayer)
        {
            AddActivity(new KeoBiaActivity
            {
                PlayerId = player.Id,
                ActivityType = KeoBiaActivityType.Join,
                PlayerName = player.DisplayName,
                AvatarUrl = player.AvatarUrl,
                Text = $"{player.DisplayName} vừa vào bàn vui.",
                Badge = "waving_hand",
                Choice = KeoBiaBetChoice.Draw,
                ChoiceLabel = "Chào mừng"
            });
        }
        await _db.SaveChangesAsync(ct);
        await RecalculatePlayersAsync([player.Id], ct);

        return Result<KeoBiaPlayerProfileUpsertResultDto>.Success(new KeoBiaPlayerProfileUpsertResultDto(
            new KeoBiaPlayerPresenceDto(
                player.Id,
                player.PublicKey,
                player.DisplayName,
                player.AvatarUrl,
                ensured.IsNewPlayer,
                player.TelegramUserId,
                player.TelegramUsername),
            false,
            ensured.IsClaimedPlayer,
            null));
    }

    public async Task<Result<KeoBiaPlayerPresenceDto>> LinkTelegramAsync(KeoBiaTelegramLinkDto dto, CancellationToken ct = default)
    {
        if (!_telegram.IsConfigured)
            return Result<KeoBiaPlayerPresenceDto>.Failure("Tính năng telegram chưa được cấu hình.");
        if (string.IsNullOrWhiteSpace(dto.PublicKey))
            return Result<KeoBiaPlayerPresenceDto>.Failure("Thiếu mã publicKey.");

        if (!VerifyTelegramHash(dto.Fields))
            return Result<KeoBiaPlayerPresenceDto>.Failure("Xác thực Telegram thất bại (chưa đăng nhập).");

        if (!dto.Fields.TryGetValue("id", out var idRaw) ||
            !long.TryParse(idRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var telegramId))
            return Result<KeoBiaPlayerPresenceDto>.Failure("Dữ liệu Telegram thiếu ID hợp lệ.");

        if (dto.Fields.TryGetValue("auth_date", out var authRaw) &&
            long.TryParse(authRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authUnix))
        {
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(authUnix);
            if (age > TimeSpan.FromSeconds(_telegram.MaxAuthAgeSeconds))
                return Result<KeoBiaPlayerPresenceDto>.Failure("Phiên xác thực đăng nhập Telegram đã hết hạn, hãy thử lại.");
        }

        return await AttachTelegramIdentityAsync(
            dto.PublicKey, dto.DisplayName, dto.AvatarUrl, dto.IpAddress, dto.UserAgent,
            telegramId,
            GetField(dto.Fields, "username"),
            GetField(dto.Fields, "first_name"),
            GetField(dto.Fields, "last_name"),
            GetField(dto.Fields, "photo_url"),
            ct);
    }

    public async Task<Result<KeoBiaPlayerPresenceDto>> LinkTelegramViaOidcAsync(KeoBiaTelegramOidcLinkDto dto, CancellationToken ct = default)
    {
        if (!_telegram.IsOidcConfigured)
            return Result<KeoBiaPlayerPresenceDto>.Failure("Đăng nhập Telegram (OIDC) chưa được cấu hình.");
        if (string.IsNullOrWhiteSpace(dto.PublicKey))
            return Result<KeoBiaPlayerPresenceDto>.Failure("Thiếu mã publicKey.");
        if (dto.TelegramUserId <= 0)
            return Result<KeoBiaPlayerPresenceDto>.Failure("Dữ liệu Telegram thiếu ID hợp lệ.");

        return await AttachTelegramIdentityAsync(
            dto.PublicKey, dto.DisplayName, dto.AvatarUrl, dto.IpAddress, dto.UserAgent,
            dto.TelegramUserId, dto.Username, dto.FirstName, dto.LastName, dto.PhotoUrl, ct);
    }

    public async Task<Result<KeoBiaPlayerPresenceDto>> EnsureTelegramPlayerAsync(KeoBiaTelegramBotIdentityDto dto, CancellationToken ct = default)
    {
        if (!_telegram.IsConfigured)
            return Result<KeoBiaPlayerPresenceDto>.Failure("Tính năng Telegram chưa được cấu hình.");
        if (dto.TelegramUserId <= 0)
            return Result<KeoBiaPlayerPresenceDto>.Failure("Dữ liệu Telegram thiếu ID hợp lệ.");

        var displayName = GetField2(dto.Username) ?? GetField2(dto.FirstName) ?? "Bia thủ";
        return await AttachTelegramIdentityAsync(
            $"tg-{dto.TelegramUserId}",
            displayName,
            null,
            null,
            null,
            dto.TelegramUserId,
            dto.Username,
            dto.FirstName,
            dto.LastName,
            dto.PhotoUrl,
            ct);
    }

    // Shared attach core for both the Login Widget (HMAC) and the OIDC redirect flow.
    // Caller is responsible for having already verified the Telegram identity.
    //
    // Telegram id is the stable identity. So we look it up FIRST: if a player already owns
    // this Telegram id, we adopt that player directly (and absorb the current browser's
    // anonymous player into it). Only when the Telegram id is brand new do we fall back to
    // ensuring/creating a player from the browser's publicKey + display name.
    private async Task<Result<KeoBiaPlayerPresenceDto>> AttachTelegramIdentityAsync(
        string publicKey, string displayName, string? avatarUrl, string? ipAddress, string? userAgent,
        long telegramId, string? username, string? firstName, string? lastName, string? photoUrl,
        CancellationToken ct)
    {
        var existing = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramId, ct);

        KeoBiaPlayer target;
        bool isNewPlayer;
        if (existing != null)
        {
            if (existing.IsBlocked)
                return Result<KeoBiaPlayerPresenceDto>.Failure("Người chơi đang bị chặn.");

            // Absorb the current browser's (pre-Telegram) player into the Telegram account.
            if (!string.IsNullOrWhiteSpace(publicKey))
            {
                var current = await _db.KeoBiaPlayers.FirstOrDefaultAsync(
                    x => x.PublicKey == publicKey && x.Id != existing.Id, ct);
                if (current != null)
                    await MergePlayersAsync(source: current, target: existing, ct);
            }

            target = existing;
            isNewPlayer = false;
        }
        else
        {
            var ensured = await EnsurePlayerAsync(
                new KeoBiaPlayerUpsertDto(publicKey, displayName, avatarUrl, ipAddress, userAgent, false, null), ct);
            if (!string.IsNullOrWhiteSpace(ensured.Error))
                return Result<KeoBiaPlayerPresenceDto>.Failure(ensured.Error);

            if (ensured.ClaimCandidate != null)
            {
                // The display name is taken by another player. Telegram is a verified
                // identity, so auto-claim that player — unless it's already linked to a
                // DIFFERENT Telegram account (then it's a genuine conflict).
                if (ensured.ClaimCandidate.TelegramUserId != null)
                    return Result<KeoBiaPlayerPresenceDto>.Failure("Tên hiển thị đã thuộc về một tài khoản Telegram khác. Hãy đổi tên hiển thị.");
                target = ensured.ClaimCandidate;
                target.PublicKey = publicKey;
                isNewPlayer = false;
            }
            else
            {
                target = ensured.Player!;
                isNewPlayer = ensured.IsNewPlayer;
            }

            if (target.IsBlocked)
                return Result<KeoBiaPlayerPresenceDto>.Failure("Người chơi đang bị chặn.");
        }

        target.TelegramUserId = telegramId;
        target.TelegramUsername = Truncate(GetField2(username), 64);
        target.TelegramFirstName = Truncate(JoinName(GetField2(firstName), GetField2(lastName)), 128);
        target.TelegramPhotoUrl = NormalizeAvatarUrl(GetField2(photoUrl));
        // Adopt the Telegram identity as the display name (username preferred, else full name).
        target.DisplayName = NormalizeDisplayName(
            target.TelegramUsername ?? target.TelegramFirstName ?? target.DisplayName);
        // Stamp first verification only; the late-verification penalty is keyed off this date,
        // so re-logins must not push it forward.
        target.TelegramVerifiedAt ??= DateTime.UtcNow;
        target.LastSeenAt = DateTime.UtcNow;
        target.UpdatedAt = DateTime.UtcNow;

        // Pull the Telegram avatar down and re-host it on our own storage so the
        // profile picture survives even if Telegram rotates/expires the CDN URL.
        if (!string.IsNullOrWhiteSpace(target.TelegramPhotoUrl))
        {
            var hosted = await DownloadTelegramAvatarAsync(target.TelegramPhotoUrl!, ct);
            if (!string.IsNullOrWhiteSpace(hosted))
                target.AvatarUrl = hosted;
            else if (string.IsNullOrWhiteSpace(target.AvatarUrl))
                target.AvatarUrl = target.TelegramPhotoUrl;
        }

        await _db.SaveChangesAsync(ct);
        await RecalculatePlayersAsync([target.Id], ct);

        return Result<KeoBiaPlayerPresenceDto>.Success(new KeoBiaPlayerPresenceDto(
            target.Id, target.PublicKey, target.DisplayName, target.AvatarUrl, isNewPlayer,
            target.TelegramUserId, target.TelegramUsername));
    }

    // Move all of <paramref name="source"/>'s bets, chat and activity onto
    // <paramref name="target"/>, then delete the source player. Used when a Telegram
    // login resolves to an account that already exists under a different publicKey.
    private async Task MergePlayersAsync(KeoBiaPlayer source, KeoBiaPlayer target, CancellationToken ct)
    {
        if (source.Id == target.Id)
            return;

        // Flush any pending tracked changes so the bulk reassignments below see every row.
        await _db.SaveChangesAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await _db.KeoBiaBets
            .Where(x => x.PlayerId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlayerId, target.Id), ct);

        await _db.KeoBiaChatMessages
            .Where(x => x.PlayerId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlayerId, target.Id), ct);

        await _db.KeoBiaActivities
            .Where(x => x.PlayerId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlayerId, (Guid?)target.Id), ct);

        // Detach the tracked source so EF doesn't try to cascade-delete the rows we just moved.
        _db.Entry(source).State = EntityState.Detached;
        await _db.KeoBiaPlayers
            .Where(x => x.Id == source.Id)
            .ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
    }

    private static string? GetField2(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Task<KeoBiaPlayer?> FindPlayerByTelegramIdAsync(long telegramUserId, CancellationToken ct) =>
        _db.KeoBiaPlayers.AsNoTracking().FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);

    private bool VerifyTelegramHash(IReadOnlyDictionary<string, string> fields)
    {
        if (fields is null || !fields.TryGetValue("hash", out var hash) || string.IsNullOrWhiteSpace(hash))
            return false;

        // Telegram Login Widget check: HMAC-SHA256 of the sorted "key=value" lines,
        // keyed by SHA256(bot_token). See https://core.telegram.org/widgets/login#checking-authorization
        var dataCheckString = string.Join("\n", fields
            .Where(kv => kv.Key != "hash")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(_telegram.BotToken));
        var computed = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(hash.Trim().ToLowerInvariant()));
    }

    private static string? GetField(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string? JoinName(string? first, string? last)
    {
        var joined = string.Join(' ', new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : (value.Length > max ? value[..max] : value);

    public async Task<Result<string>> UploadAvatarAsync(Stream stream, string fileName, string contentType, long size, CancellationToken ct = default)
    {
        if (size <= 0)
            return Result<string>.Failure("Chưa chọn ảnh avatar.");

        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return Result<string>.Failure("Avatar phải là file ảnh.");

        if (size > MaxAvatarSizeBytes)
            return Result<string>.Failure("Avatar vượt quá 5MB.");

        using var input = new MemoryStream();
        await stream.CopyToAsync(input, ct);
        if (input.Length == 0)
            return Result<string>.Failure("Ảnh avatar bị rỗng.");

        input.Position = 0;

        try
        {
            using var image = await Image.LoadAsync(input, ct);
            if (image.Width > MaxAvatarWidth || image.Height > MaxAvatarHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxAvatarWidth, MaxAvatarHeight),
                    Mode = ResizeMode.Max
                }));
            }

            using var output = new MemoryStream();
            await image.SaveAsync(output, new PngEncoder(), ct);
            output.Position = 0;

            var key = await _storage.SaveAsync(output, "keobia-avatar.png", AvatarSubFolder, ct);
            return Result<string>.Success(_storage.GetPublicUrl(key));
        }
        catch (UnknownImageFormatException)
        {
            return Result<string>.Failure("Ảnh avatar không hợp lệ.");
        }
    }

    public async Task<Result<string>> UploadChatImageAsync(Stream stream, string fileName, string contentType, long size, CancellationToken ct = default)
    {
        if (size <= 0)
            return Result<string>.Failure("Chưa chọn ảnh chat.");

        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return Result<string>.Failure("Ảnh chat phải là file ảnh.");

        if (size > MaxChatImageSizeBytes)
            return Result<string>.Failure("Ảnh chat vượt quá 6MB.");

        using var input = new MemoryStream();
        await stream.CopyToAsync(input, ct);
        if (input.Length == 0)
            return Result<string>.Failure("Ảnh chat bị rỗng.");

        input.Position = 0;

        try
        {
            using var image = await Image.LoadAsync(input, ct);
            if (image.Width > MaxChatImageWidth || image.Height > MaxChatImageHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxChatImageWidth, MaxChatImageHeight),
                    Mode = ResizeMode.Max
                }));
            }

            using var output = new MemoryStream();
            await image.SaveAsync(output, new PngEncoder(), ct);
            output.Position = 0;

            var key = await _storage.SaveAsync(output, "keobia-chat.png", ChatImageSubFolder, ct);
            return Result<string>.Success(_storage.GetPublicUrl(key));
        }
        catch (UnknownImageFormatException)
        {
            return Result<string>.Failure("Ảnh chat không hợp lệ.");
        }
    }

    public async Task<Result<KeoBiaSubmitBetResultDto>> SubmitBetAsync(KeoBiaSubmitBetDto dto, CancellationToken ct = default)
    {
        var choice = NormalizeChoice(dto.Choice);
        if (choice == null) return Result<KeoBiaSubmitBetResultDto>.Failure("Cửa cược không hợp lệ.");
        if (dto.Cups < 1 || dto.Cups > MaxCups) return Result<KeoBiaSubmitBetResultDto>.Failure($"Số lượng phải nằm trong khoảng 1-{MaxCups}.");
        var scoreValidation = ValidateCorrectScore(choice, dto.PredictedHomeScore, dto.PredictedAwayScore);
        if (scoreValidation is not null) return Result<KeoBiaSubmitBetResultDto>.Failure(scoreValidation);

        var match = await _db.KeoBiaMatches.FirstOrDefaultAsync(x => x.Id == dto.MatchId, ct);
        if (match == null) return Result<KeoBiaSubmitBetResultDto>.Failure("Không tìm thấy trận đấu.");
        if (match.Status is KeoBiaMatchStatus.Finished or KeoBiaMatchStatus.Cancelled)
            return Result<KeoBiaSubmitBetResultDto>.Failure("Trận đã khóa kèo.");
        decimal? scoreOdds = null;
        var now = DateTime.UtcNow;
        if (now < match.KickoffAt.Add(-VoteOpenWindow))
            return Result<KeoBiaSubmitBetResultDto>.Failure("Trận chưa mở kèo (chỉ mở trước giờ bóng lăn 1 ngày).");
        if (now >= match.KickoffAt.Add(-VoteLockWindow))
            return Result<KeoBiaSubmitBetResultDto>.Failure("Đã đóng kèo (khóa trước giờ bóng lăn 20 phút).");

        var ensured = await EnsurePlayerAsync(
            new KeoBiaPlayerUpsertDto(dto.PublicKey, dto.DisplayName, dto.AvatarUrl, dto.IpAddress, dto.UserAgent, false, null), ct);
        if (!string.IsNullOrWhiteSpace(ensured.Error))
            return Result<KeoBiaSubmitBetResultDto>.Failure(ensured.Error);
        if (ensured.ClaimCandidate != null)
            return Result<KeoBiaSubmitBetResultDto>.Failure("Tài khoản đã tồn tại. Hãy mở hồ sơ để xác nhận đúng người chơi này.");

        var player = ensured.Player!;
        if (player.IsBlocked) return Result<KeoBiaSubmitBetResultDto>.Failure("Người chơi đang bị chặn.");
        if (player.TelegramUserId == null)
            return Result<KeoBiaSubmitBetResultDto>.Failure("Bạn cần xác thực Telegram trước khi đặt kèo.");

        return await SubmitBetForPlayerAsync(player, match, choice, dto.Cups, ensured.IsNewPlayer, dto.StarType, dto.PredictedHomeScore, dto.PredictedAwayScore, scoreOdds, ct);
    }

    public async Task<Result<KeoBiaSubmitBetResultDto>> SubmitBetByTelegramIdAsync(long telegramUserId, Guid matchId, string choice, int cups, string? starType = null, int? predictedHomeScore = null, int? predictedAwayScore = null, decimal? correctScoreOdds = null, CancellationToken ct = default)
    {
        if (telegramUserId <= 0)
            return Result<KeoBiaSubmitBetResultDto>.Failure("Dữ liệu Telegram thiếu ID hợp lệ.");

        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);
        var isNewPlayer = false;
        if (player is null)
        {
            var ensured = await EnsureTelegramPlayerAsync(new KeoBiaTelegramBotIdentityDto(
                telegramUserId,
                null,
                null,
                null,
                null), ct);
            if (!ensured.Succeeded || ensured.Value is null)
                return Result<KeoBiaSubmitBetResultDto>.Failure(ensured.Error ?? "Không tạo được người chơi Telegram.");

            isNewPlayer = ensured.Value.IsNewPlayer;
            player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);
            if (player is null)
                return Result<KeoBiaSubmitBetResultDto>.Failure("Không tìm thấy hồ sơ Telegram sau khi tạo.");
        }

        var normalizedChoice = NormalizeChoice(choice);
        if (normalizedChoice == null) return Result<KeoBiaSubmitBetResultDto>.Failure("Cửa cược không hợp lệ.");
        if (cups < 1 || cups > MaxCups) return Result<KeoBiaSubmitBetResultDto>.Failure($"Số lượng phải nằm trong khoảng 1-{MaxCups}.");
        var scoreValidation = ValidateCorrectScore(normalizedChoice, predictedHomeScore, predictedAwayScore);
        if (scoreValidation is not null) return Result<KeoBiaSubmitBetResultDto>.Failure(scoreValidation);

        var match = await _db.KeoBiaMatches.FirstOrDefaultAsync(x => x.Id == matchId, ct);
        if (match == null) return Result<KeoBiaSubmitBetResultDto>.Failure("Không tìm thấy trận đấu.");
        if (match.Status is KeoBiaMatchStatus.Finished or KeoBiaMatchStatus.Cancelled)
            return Result<KeoBiaSubmitBetResultDto>.Failure("Trận đã khóa kèo.");
        decimal? scoreOdds = null;
        var now = DateTime.UtcNow;
        if (now < match.KickoffAt.Add(-VoteOpenWindow))
            return Result<KeoBiaSubmitBetResultDto>.Failure("Trận chưa mở kèo (chỉ mở trước giờ bóng lăn 1 ngày).");
        if (now >= match.KickoffAt.Add(-VoteLockWindow))
            return Result<KeoBiaSubmitBetResultDto>.Failure("Đã đóng kèo (khóa trước giờ bóng lăn 20 phút).");
        if (player.IsBlocked) return Result<KeoBiaSubmitBetResultDto>.Failure("Người chơi đang bị chặn.");

        return await SubmitBetForPlayerAsync(player, match, normalizedChoice, cups, isNewPlayer, starType, predictedHomeScore, predictedAwayScore, scoreOdds, ct);
    }

    private async Task<Result<KeoBiaSubmitBetResultDto>> SubmitBetForPlayerAsync(
        KeoBiaPlayer player,
        KeoBiaMatch match,
        string choice,
        int cups,
        bool isNewPlayer,
        string? starType,
        int? predictedHomeScore,
        int? predictedAwayScore,
        decimal? correctScoreOdds,
        CancellationToken ct)
    {
        var choiceLabel = GetActivityChoiceLabel(match, choice);
        var existingBet = await _db.KeoBiaBets
            .FirstOrDefaultAsync(x => x.PlayerId == player.Id && x.MatchId == match.Id, ct);

        bool isChange = existingBet != null;
        string oldChoice = "";
        string? oldStarType = null;
        int? oldPredictedHomeScore = null;
        int? oldPredictedAwayScore = null;

        if (isChange)
        {
            oldChoice = existingBet!.Choice;
            oldStarType = existingBet.StarType;
            oldPredictedHomeScore = existingBet.PredictedHomeScore;
            oldPredictedAwayScore = existingBet.PredictedAwayScore;
            if (oldChoice == choice && (oldStarType ?? "") == (starType ?? "") && oldPredictedHomeScore == predictedHomeScore && oldPredictedAwayScore == predictedAwayScore)
                return Result<KeoBiaSubmitBetResultDto>.Failure("Bạn đã chọn cửa này rồi.");
            existingBet.Choice = choice;
            existingBet.StarType = starType;
            existingBet.PredictedHomeScore = predictedHomeScore;
            existingBet.PredictedAwayScore = predictedAwayScore;
            existingBet.CorrectScoreOdds = correctScoreOdds;
        }
        else
        {
            _db.KeoBiaBets.Add(new KeoBiaBet
            {
                PlayerId = player.Id,
                MatchId = match.Id,
                Choice = choice,
                Cups = cups,
                StarType = starType,
                PredictedHomeScore = predictedHomeScore,
                PredictedAwayScore = predictedAwayScore,
                CorrectScoreOdds = correctScoreOdds
            });
        }

        if (starType == "devil")
        {
            var aiHome = match.AiHome;
            var aiDraw = match.AiDraw;
            var aiAway = match.AiAway;
            var maxDiff = Math.Max(Math.Abs(aiHome - aiDraw), Math.Max(Math.Abs(aiHome - aiAway), Math.Abs(aiDraw - aiAway)));
            if (maxDiff > 10)
            {
                var isHomeFavored = aiHome > aiAway;
                var isAwayFavored = aiAway > aiHome;
                var isChoosingFavored = (isHomeFavored && choice == KeoBiaBetChoice.Home)
                                     || (isAwayFavored && choice == KeoBiaBetChoice.Away);
                if (isChoosingFavored)
                    return Result<KeoBiaSubmitBetResultDto>.Failure(
                        "Ngôi Sao Ma Quỷ: kèo chênh lệch, chỉ được chọn Hòa hoặc đội yếu hơn.");
            }
        }

        var starChanged = !isChange || (oldStarType ?? "") != (starType ?? "");
        if (starChanged)
        {
            if (starType == "hope" && player.HopeStars <= 0)
                return Result<KeoBiaSubmitBetResultDto>.Failure("Bạn không còn Ngôi Sao Hi Vọng.");
            if (starType == "devil" && player.DevilStars <= 0)
                return Result<KeoBiaSubmitBetResultDto>.Failure("Bạn không còn Ngôi Sao Ma Quỷ.");
        }
        if (starChanged)
        {
            // Audit the star spend (does not change cups itself; the win/lose effect on
            // cups is journaled later by RecalculatePlayersAsync once the match settles).
            // Clamp at 0 defensively: the <= 0 guard above already blocks spending a star you
            // don't have, but never let the balance go negative (an older build's < 0 guard
            // left some players stuck at -1).
            var starBalance = await ComputeLossBalanceAsync(player, ct);
            if (starType == "hope")
            {
                player.HopeStars = Math.Max(0, player.HopeStars - 1);
                AddCupLog(player, match.Id, KeoBiaCupChangeType.UsedHopeStar, 0,
                    $"Dùng Ngôi Sao Hi Vọng: {match.HomeName} vs {match.AwayName}", starBalance);
            }
            if (starType == "devil")
            {
                player.DevilStars = Math.Max(0, player.DevilStars - 1);
                AddCupLog(player, match.Id, KeoBiaCupChangeType.UsedDevilStar, 0,
                    $"Dùng Ngôi Sao Ma Quỷ: {match.HomeName} vs {match.AwayName}", starBalance);
            }

            if (isChange && oldStarType != null && starType == null)
            {
                if (oldStarType == "hope") player.HopeStars++;
                if (oldStarType == "devil") player.DevilStars++;
            }
        }

        var starSuffix = starType == "hope" ? " ⭐Hi vọng" : starType == "devil" ? " 😈Ma quỷ" : "";
        var scoreSuffix = predictedHomeScore.HasValue && predictedAwayScore.HasValue ? $" · tỉ số 90p {predictedHomeScore}-{predictedAwayScore}" : "";
        var unitText = KeoBiaUnitRules.FormatCount(cups, KeoBiaUnitRules.GetUnitCode(match.Stage, match.KickoffAt));
        var activityText = isChange
            ? $"{player.DisplayName} đã đổi kèo{starSuffix} sang {choiceLabel}{scoreSuffix} trận {match.HomeName} vs {match.AwayName}."
            : $"{player.DisplayName} vừa gửi {unitText}{starSuffix} cho {choiceLabel}{scoreSuffix} trận {match.HomeName} vs {match.AwayName}.";

        var activity = new KeoBiaActivity
        {
            PlayerId = player.Id,
            MatchId = match.Id,
            ActivityType = KeoBiaActivityType.Prediction,
            PlayerName = player.DisplayName,
            AvatarUrl = player.AvatarUrl,
            Text = activityText,
            Badge = starType == "hope" ? "star" : starType == "devil" ? "local_fire_department" : "sports_bar",
            Choice = choice,
            ChoiceLabel = choiceLabel + starSuffix + scoreSuffix,
            Cups = cups
        };

        AddActivity(activity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (!isChange)
        {
            foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified).ToList())
                entry.State = EntityState.Detached;

            var existing = await _db.KeoBiaBets.AsNoTracking()
                .AnyAsync(x => x.PlayerId == player.Id && x.MatchId == match.Id, ct);
            if (!existing) throw;

            return Result<KeoBiaSubmitBetResultDto>.Failure("Bạn đã có kèo cho trận này. Hãy tải lại trước khi đổi cửa.");
        }

        await RecalculatePlayersAsync([player.Id], ct);

        return Result<KeoBiaSubmitBetResultDto>.Success(new KeoBiaSubmitBetResultDto(
            new KeoBiaPlayerPresenceDto(
                player.Id,
                player.PublicKey,
                player.DisplayName,
                player.AvatarUrl,
                isNewPlayer,
                player.TelegramUserId,
                player.TelegramUsername),
            MapActivity(activity)));
    }

    // Run once the 20-minute lock has closed: every verified, non-blocked player who did not
    // pick gets an auto bet on the crowd-favourite side (most distinct pickers). On a tie or
    // when nobody picked, fall back to the AI-favoured side. The bet is a normal KeoBiaBet —
    // it clears the missed-match penalty and only costs the match's beer/peanut amount if that side loses. Idempotent:
    // a re-run finds no missing players and does nothing. Returns the auto-assigned players that
    // carry a Telegram id so the caller can notify them.
    public async Task<KeoBiaAutoAssignResultDto> AutoAssignMissingBetsAsync(Guid matchId, CancellationToken ct = default)
    {
        var empty = new KeoBiaAutoAssignResultDto(
            Array.Empty<KeoBiaAutoAssignedBetDto>(),
            Array.Empty<KeoBiaActivityFeedDto>());

        var match = await _db.KeoBiaMatches.FirstOrDefaultAsync(x => x.Id == matchId, ct);
        if (match == null) return empty;
        if (match.Status is KeoBiaMatchStatus.Finished or KeoBiaMatchStatus.Cancelled) return empty;
        if (match.ResultChoice != null) return empty;
        if (DateTime.UtcNow < match.KickoffAt.Add(-VoteLockWindow)) return empty;

        var roster = await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.TelegramUserId != null && !x.IsBlocked)
            .Select(x => new { x.Id, x.DisplayName, x.AvatarUrl, x.TelegramUserId })
            .ToListAsync(ct);
        if (roster.Count == 0) return empty;

        var existingBets = await _db.KeoBiaBets.AsNoTracking()
            .Where(b => b.MatchId == matchId)
            .Select(b => new { b.PlayerId, b.Choice })
            .ToListAsync(ct);
        var bettorIds = existingBets.Select(b => b.PlayerId).ToHashSet();

        var missing = roster.Where(p => !bettorIds.Contains(p.Id)).ToList();
        if (missing.Count == 0) return empty;

        var counts = existingBets
            .GroupBy(b => b.Choice)
            .ToDictionary(g => g.Key, g => g.Count());

        string choice;
        if (counts.Count == 0)
        {
            choice = ResolveAiFavoredChoice(match) ?? KeoBiaBetChoice.Home;
        }
        else
        {
            var ranked = counts.OrderByDescending(kv => kv.Value).ToList();
            var tie = ranked.Count > 1 && ranked[0].Value == ranked[1].Value;
            choice = tie ? ResolveAiFavoredChoice(match) ?? ranked[0].Key : ranked[0].Key;
        }

        var choiceLabel = GetActivityChoiceLabel(match, choice);
        var cups = match.DefaultCups < 1 ? 1 : match.DefaultCups;
        var notifications = new List<KeoBiaAutoAssignedBetDto>(missing.Count);
        var activities = new List<KeoBiaActivity>(missing.Count);

        foreach (var player in missing)
        {
            _db.KeoBiaBets.Add(new KeoBiaBet
            {
                PlayerId = player.Id,
                MatchId = match.Id,
                Choice = choice,
                Cups = cups,
                StarType = null
            });

            var activity = new KeoBiaActivity
            {
                PlayerId = player.Id,
                MatchId = match.Id,
                ActivityType = KeoBiaActivityType.Prediction,
                PlayerName = player.DisplayName,
                AvatarUrl = player.AvatarUrl,
                Text = $"{player.DisplayName} được hệ thống tự chọn {choiceLabel} (cửa đông người nhất) trận {match.HomeName} vs {match.AwayName}.",
                Badge = "sports_bar",
                Choice = choice,
                ChoiceLabel = choiceLabel,
                Cups = cups
            };
            AddActivity(activity);
            activities.Add(activity);

            notifications.Add(new KeoBiaAutoAssignedBetDto(
                player.TelegramUserId!.Value,
                player.DisplayName,
                match.Id,
                match.HomeName,
                match.AwayName,
                choice,
                choiceLabel,
                KeoBiaUnitRules.GetUnitCode(match.Stage, match.KickoffAt)));
        }

        await _db.SaveChangesAsync(ct);
        await RecalculatePlayersAsync(missing.Select(p => p.Id), ct);

        return new KeoBiaAutoAssignResultDto(
            notifications,
            activities.Select(MapActivity).ToList());
    }

    private static string? ResolveAiFavoredChoice(KeoBiaMatch match)
    {
        if (match.AiHome <= 0 && match.AiDraw <= 0 && match.AiAway <= 0) return null;
        var max = Math.Max(match.AiHome, Math.Max(match.AiDraw, match.AiAway));
        if (max == match.AiHome) return KeoBiaBetChoice.Home;
        if (max == match.AiAway) return KeoBiaBetChoice.Away;
        return KeoBiaBetChoice.Draw;
    }

    public async Task<Result<KeoBiaChatMessageDto>> SendChatMessageAsync(KeoBiaChatMessageCreateDto dto, CancellationToken ct = default)
    {
        var text = NormalizeChatMessage(dto.Message);
        var imageUrl = NormalizeAvatarUrl(dto.ImageUrl);
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(imageUrl))
            return Result<KeoBiaChatMessageDto>.Failure("Tin nhắn cần có nội dung hoặc ảnh.");

        var ensured = await EnsurePlayerAsync(
            new KeoBiaPlayerUpsertDto(dto.PublicKey, dto.DisplayName, dto.AvatarUrl, dto.IpAddress, dto.UserAgent, false, null), ct);
        if (!string.IsNullOrWhiteSpace(ensured.Error))
            return Result<KeoBiaChatMessageDto>.Failure(ensured.Error);
        if (ensured.ClaimCandidate != null)
            return Result<KeoBiaChatMessageDto>.Failure("Tài khoản đã tồn tại. Hãy mở hồ sơ để xác nhận đúng người chơi này.");

        var player = ensured.Player!;
        if (player.IsBlocked)
            return Result<KeoBiaChatMessageDto>.Failure("Người chơi đang bị chặn.");

        var entity = new KeoBiaChatMessage
        {
            PlayerId = player.Id,
            PlayerName = player.DisplayName,
            AvatarUrl = player.AvatarUrl,
            Message = text,
            ImageUrl = imageUrl
        };

        _db.KeoBiaChatMessages.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Result<KeoBiaChatMessageDto>.Success(new KeoBiaChatMessageDto(
            entity.Id,
            player.Id,
            player.PublicKey,
            entity.PlayerName,
            entity.AvatarUrl,
            entity.Message,
            entity.ImageUrl,
            entity.CreatedAt));
    }

    private IQueryable<KeoBiaBetFeedProjection> GetRecentFeedQuery() =>
        _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.Player.TelegramUserId != null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new KeoBiaBetFeedProjection(
                x.Id,
                x.PlayerId,
                x.MatchId,
                x.Player.DisplayName,
                x.Player.AvatarUrl,
                x.Match.HomeName + " vs " + x.Match.AwayName,
                x.Choice,
                x.Choice == KeoBiaBetChoice.Home ? x.Match.HomeCode : x.Choice == KeoBiaBetChoice.Away ? x.Match.AwayCode : "Hòa",
                x.Cups,
                x.CreatedAt,
                x.IsSettled,
                x.IsCorrect,
                x.Match.Stage,
                x.Match.KickoffAt));

    private IQueryable<KeoBiaActivityFeedDto> GetRecentActivityQuery() =>
        _db.KeoBiaActivities.AsNoTracking()
            .Where(x => x.ActivityType != KeoBiaActivityType.Prediction ||
                (x.PlayerId.HasValue && _db.KeoBiaPlayers.Any(p => p.Id == x.PlayerId.Value && p.TelegramUserId != null)))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new KeoBiaActivityFeedDto(
                x.Id,
                x.ActivityType,
                x.PlayerName,
                x.AvatarUrl,
                x.Text,
                x.Badge,
                x.Choice ?? KeoBiaBetChoice.Draw,
                x.ChoiceLabel ?? "Hoạt động",
                x.Cups,
                x.CreatedAt));

    private static KeoBiaActivityFeedDto MapActivity(KeoBiaActivity activity) =>
        new(
            activity.Id,
            activity.ActivityType,
            activity.PlayerName,
            activity.AvatarUrl,
            activity.Text,
            activity.Badge,
            activity.Choice ?? KeoBiaBetChoice.Draw,
            activity.ChoiceLabel ?? "Hoạt động",
            activity.Cups,
            activity.CreatedAt);

    private async Task<EnsurePlayerResult> EnsurePlayerAsync(KeoBiaPlayerUpsertDto dto, CancellationToken ct)
    {
        var key = dto.PublicKey.Trim();
        var name = NormalizeDisplayName(dto.DisplayName);
        var avatarUrl = NormalizeAvatarUrl(dto.AvatarUrl);

        var player = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.PublicKey == key, ct);
        if (player == null)
        {
            var claimedPlayer = await _db.KeoBiaPlayers.FirstOrDefaultAsync(x => x.DisplayName == name, ct);
            if (claimedPlayer != null)
            {
                if (!dto.ClaimExisting || !dto.ClaimPlayerId.HasValue || dto.ClaimPlayerId.Value != claimedPlayer.Id)
                    return EnsurePlayerResult.RequireClaim(claimedPlayer);

                claimedPlayer.PublicKey = key;
                ApplyPlayerIdentity(claimedPlayer, name, avatarUrl, dto.IpAddress, dto.UserAgent);
                return EnsurePlayerResult.Success(claimedPlayer, false, true);
            }

            player = new KeoBiaPlayer
            {
                PublicKey = key,
                CreatedAt = DateTime.UtcNow
            };
            _db.KeoBiaPlayers.Add(player);
            ApplyPlayerIdentity(player, name, avatarUrl, dto.IpAddress, dto.UserAgent);
            return EnsurePlayerResult.Success(player, true, false);
        }

        if (string.Equals(player.DisplayName, name, StringComparison.Ordinal))
        {
            ApplyPlayerIdentity(player, name, avatarUrl, dto.IpAddress, dto.UserAgent);
            return EnsurePlayerResult.Success(player, false, false);
        }

        var duplicate = await _db.KeoBiaPlayers.FirstOrDefaultAsync(
            x => x.DisplayName == name && x.Id != player.Id,
            ct);
        if (duplicate != null)
            return EnsurePlayerResult.Fail("Tài khoản đã tồn tại. Vui lòng chọn tên khác.");

        ApplyPlayerIdentity(player, name, avatarUrl, dto.IpAddress, dto.UserAgent);
        return EnsurePlayerResult.Success(player, false, false);
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "Bia thủ mới" : displayName.Trim();
        return name.Length > 80 ? name[..80] : name;
    }

    private static void ApplyPlayerIdentity(
        KeoBiaPlayer player,
        string displayName,
        string? avatarUrl,
        string? ipAddress,
        string? userAgent)
    {
        player.DisplayName = displayName;
        player.AvatarUrl = avatarUrl ?? player.AvatarUrl;
        player.IpAddress = ipAddress;
        player.UserAgent = userAgent;
        player.LastSeenAt = DateTime.UtcNow;
        player.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<string?> DownloadTelegramAvatarAsync(string photoUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient(TelegramAvatarHttpClientName);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(contentType) ||
                !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            await using var remote = await response.Content.ReadAsStreamAsync(ct);
            using var input = new MemoryStream();
            await remote.CopyToAsync(input, ct);
            if (input.Length == 0 || input.Length > MaxAvatarSizeBytes)
                return null;

            input.Position = 0;

            using var image = await Image.LoadAsync(input, ct);
            if (image.Width > MaxAvatarWidth || image.Height > MaxAvatarHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxAvatarWidth, MaxAvatarHeight),
                    Mode = ResizeMode.Max
                }));
            }

            using var output = new MemoryStream();
            await image.SaveAsync(output, new PngEncoder(), ct);
            output.Position = 0;

            var key = await _storage.SaveAsync(output, "keobia-avatar.png", AvatarSubFolder, ct);
            return _storage.GetPublicUrl(key);
        }
        catch
        {
            // Network/format failures are non-fatal: the caller falls back to the raw Telegram URL.
            return null;
        }
    }

    private static string? NormalizeAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return null;

        var value = avatarUrl.Trim();
        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return null;

        if (value.StartsWith("/", StringComparison.Ordinal))
            return value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return value;

        return null;
    }

    private static string? NormalizeChatMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var value = Regex.Replace(message.Trim(), @"\s+", " ");
        if (value.Length > 600)
            value = value[..600];

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task<Result<KeoBiaBeerPaymentDto>> MarkBeerPaymentPaidInternalAsync(KeoBiaBeerPayment payment, string? providerTransactionId, string? payloadJson, CancellationToken ct)
    {
        if (payment.Status == KeoBiaBeerPaymentStatus.Paid)
            return Result<KeoBiaBeerPaymentDto>.Success(MapPayment(payment, payment.Player));
        if (payment.Status != KeoBiaBeerPaymentStatus.Pending)
            return Result<KeoBiaBeerPaymentDto>.Failure("Chỉ giao dịch pending mới mark paid được.");

        var now = DateTime.UtcNow;
        var currentBalance = await ComputeLossBalanceAsync(payment.Player, ct);
        var paidCups = Math.Min(payment.Cups, currentBalance);
        payment.Status = KeoBiaBeerPaymentStatus.Paid;
        payment.ProviderTransactionId = string.IsNullOrWhiteSpace(providerTransactionId) ? payment.ProviderTransactionId : providerTransactionId.Trim();
        payment.ProviderPayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? payment.ProviderPayloadJson : payloadJson;
        payment.PaidAt = now;
        payment.UpdatedAt = now;

        if (paidCups > 0)
        {
            var breakdown = KeoBiaUnitRules.DecodePaymentBreakdown(payment.Cups, payment.CoinAmount);
            var applied = KeoBiaUnitRules.TakeOldestFirst(
                paidCups, breakdown.BeerCups, breakdown.PeanutPacks);
            var balanceAfter = Math.Max(0, currentBalance - paidCups);
            if (applied.BeerCups > 0)
            {
                AddCupLog(payment.Player, null, KeoBiaCupChangeType.PaidBeer, -applied.BeerCups,
                    $"Đã nộp {KeoBiaUnitRules.FormatCount(applied.BeerCups, KeoBiaUnitCode.Beer)}: {payment.TransferContent}",
                    balanceAfter);
            }
            if (applied.PeanutPacks > 0)
            {
                AddCupLog(payment.Player, null, KeoBiaCupChangeType.PaidBeer, -applied.PeanutPacks,
                    $"Đã nộp {KeoBiaUnitRules.FormatCount(applied.PeanutPacks, KeoBiaUnitCode.Peanut)}: {payment.TransferContent}",
                    balanceAfter);
            }
        }

        await _db.SaveChangesAsync(ct);
        return Result<KeoBiaBeerPaymentDto>.Success(MapPayment(payment, payment.Player));
    }

    private static KeoBiaBeerPaymentDto MapPayment(KeoBiaBeerPayment x, KeoBiaPlayer player)
    {
        var breakdown = KeoBiaUnitRules.DecodePaymentBreakdown(x.Cups, x.CoinAmount);
        return new KeoBiaBeerPaymentDto(
            x.Id,
            x.Code,
            player.DisplayName,
            player.TelegramUsername,
            player.TelegramUserId,
            x.Cups,
            x.CoinAmount,
            x.Status,
            x.QrUrl,
            x.TransferContent,
            x.ProviderTransactionId,
            x.PaidAt,
            x.CreatedAt,
            breakdown.BeerCups,
            breakdown.PeanutPacks);
    }

    private static bool ContainsTransferContent(string content, string transferContent)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(transferContent)) return false;
        return Regex.IsMatch(content, $@"(?<!\w){Regex.Escape(transferContent)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static (string? ProviderId, string? Content, int? Amount) ParsePaymentWebhook(string payloadJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
        string? providerId = null, content = null;
        int? amount = null;
        foreach (var p in FlattenJson(doc.RootElement))
        {
            var name = p.Name.ToLowerInvariant().Replace("_", "", StringComparison.Ordinal);
            if ((name is "id" or "transactionid" or "reference" or "code") && providerId is null)
                providerId = ReadJsonString(p.Value);
            if ((name is "content" or "description" or "transfercontent" or "transactioncontent") && content is null)
                content = ReadJsonString(p.Value);
            if ((name is "amount" or "transferamount" or "money" or "value") && amount is null)
                amount = ReadJsonInt(p.Value);
        }
        return (providerId?.Trim(), content?.Trim(), amount);
    }

    private static IEnumerable<JsonProperty> FlattenJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) yield break;
        foreach (var p in element.EnumerateObject())
        {
            yield return p;
            foreach (var child in FlattenJson(p.Value)) yield return child;
        }
    }

    private static string? ReadJsonString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    private static int? ReadJsonInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)) return i;
        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = new string((value.GetString() ?? string.Empty).Where(char.IsDigit).ToArray());
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
        }
        return null;
    }

    private void AddActivity(KeoBiaActivity activity)
    {
        _db.KeoBiaActivities.Add(activity);
    }

    private static string GetActivityChoiceLabel(KeoBiaMatch match, string choice) =>
        choice == KeoBiaBetChoice.Home
            ? match.HomeCode
            : choice == KeoBiaBetChoice.Away
                ? match.AwayCode
                : "Hòa";

    private readonly record struct MissedMatchInfo(
        Guid MatchId,
        string Label,
        string Stage,
        DateTime OccurredAt);

    // Auto-loss beer/peanut amounts: after Telegram verification, every match
    // that has kicked off without a bet from the player counts as one lost cup. Returns
    // the per-match detail so callers can both size the penalty (list count) and journal
    // one missed_match cup log per match.
    private async Task<Dictionary<Guid, List<MissedMatchInfo>>> ComputeMissedMatchLossCupsAsync(
        IReadOnlyList<KeoBiaPlayer> players, CancellationToken ct)
    {
        var result = new Dictionary<Guid, List<MissedMatchInfo>>();
        if (players.Count == 0) return result;

        var now = DateTime.UtcNow;
        var eligible = await _db.KeoBiaMatches.AsNoTracking()
            .Where(m => m.Status != KeoBiaMatchStatus.Cancelled && m.KickoffAt <= now)
            .OrderBy(m => m.KickoffAt).ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.HomeName, m.AwayName, m.Stage, m.KickoffAt })
            .ToListAsync(ct);
        if (eligible.Count == 0) return result;

        var playerIds = players.Select(p => p.Id).ToArray();
        var betMatches = await _db.KeoBiaBets.AsNoTracking()
            .Where(b => playerIds.Contains(b.PlayerId))
            .Select(b => new { b.PlayerId, b.MatchId })
            .Distinct()
            .ToListAsync(ct);
        var betsByPlayer = betMatches
            .GroupBy(x => x.PlayerId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.MatchId).ToHashSet());

        foreach (var player in players)
        {
            if (player.IsBlocked && player.StoppedPlayingAt is null) continue;

            var bets = betsByPlayer.TryGetValue(player.Id, out var set) ? set : null;
            var missed = eligible
                .Where(match => !player.StoppedPlayingAt.HasValue || match.KickoffAt < player.StoppedPlayingAt.Value)
                .Where(match => bets is null || !bets.Contains(match.Id))
                .Select(match => new MissedMatchInfo(
                    match.Id,
                    match.HomeName + " vs " + match.AwayName,
                    match.Stage,
                    match.KickoffAt))
                .ToList();

            if (missed.Count > 0) result[player.Id] = missed;
        }

        return result;
    }

    // Reconciles append-only per-match cup logs of one change type to a desired state.
    // Writes a delta entry (desired - existing net) per match: positive for newly-incurred
    // cups, negative to reverse cups that no longer apply (deleted bet, un-finished match,
    // unblocked player). Idempotent — a re-run with no change writes nothing.
    private void ReconcilePerMatchCupLog(
        KeoBiaPlayer player,
        string changeType,
        IReadOnlyDictionary<Guid, (int Cups, string Reason, DateTime? OccurredAt)> desired,
        IReadOnlyDictionary<Guid, int> existingNet,
        string reversalReason,
        int balanceAfter)
    {
        var matchIds = new HashSet<Guid>(desired.Keys);
        matchIds.UnionWith(existingNet.Keys);

        foreach (var matchId in matchIds)
        {
            var want = desired.TryGetValue(matchId, out var d) ? d.Cups : 0;
            var have = existingNet.TryGetValue(matchId, out var net) ? net : 0;
            if (want == have) continue;

            if (want != 0 && have != 0 && Math.Sign(want) != Math.Sign(have))
            {
                AddCupLog(player, matchId, changeType, -have, reversalReason, balanceAfter, d.OccurredAt);
                AddCupLog(player, matchId, changeType, want, d.Reason, balanceAfter, d.OccurredAt);
                continue;
            }

            var delta = want - have;
            if (delta == 0) continue;

            var reason = want != 0 && desired.TryGetValue(matchId, out var dd) && Math.Abs(want) > Math.Abs(have)
                ? dd.Reason
                : reversalReason;
            var occurredAt = desired.TryGetValue(matchId, out var when) ? when.OccurredAt : null;
            AddCupLog(player, matchId, changeType, delta, reason, balanceAfter, occurredAt);
        }
    }

    private async Task<HashSet<Guid>> GetVerifiedPlayerIdsAsync(CancellationToken ct) =>
        (await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.TelegramUserId != null && !x.IsBlocked)
            .Select(x => x.Id)
            .ToListAsync(ct))
        .ToHashSet();

    private async Task RecalculatePlayersAsync(IEnumerable<Guid> playerIds, CancellationToken ct)
    {
        var ids = playerIds.Distinct().ToArray();
        if (ids.Length == 0) return;

        var stats = await _db.KeoBiaBets
            .Where(x => ids.Contains(x.PlayerId) && x.Player.TelegramUserId != null)
            .GroupBy(x => x.PlayerId)
            .Select(g => new
            {
                PlayerId = g.Key,
                TotalBets = g.Count(),
                TotalCups = g.Sum(x => x.Cups),
                CorrectBets = g.Count(x => x.IsSettled && x.IsCorrect == true),
                WrongBets = g.Count(x => x.IsSettled && x.IsCorrect == false),
                StarWinBonus = g.Count(x => x.IsSettled && x.IsCorrect == true && (x.StarType == "hope" || x.StarType == "devil")),
                StarLoseBonus = g.Count(x => x.IsSettled && x.IsCorrect == false && (x.StarType == "hope" || x.StarType == "devil"))
            })
            .ToListAsync(ct);

        var players = await _db.KeoBiaPlayers.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var playersById = players.ToDictionary(x => x.Id);
        var missedMatchLosses = await ComputeMissedMatchLossCupsAsync(players, ct);
        await ApplyRegistrationMissedMatchPromotionAsync(players, missedMatchLosses, ct);

        // Current truth: settled wrong bets per (player, match), aggregated cups.
        var wrongBets = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && x.IsSettled && x.IsCorrect == false && x.Player.TelegramUserId != null)
            .Select(x => new { x.PlayerId, x.MatchId, x.Cups, x.Match.HomeName, x.Match.AwayName, x.SettledAt, x.Match.ResultUpdatedAt })
            .ToListAsync(ct);
        var wrongBetsByPlayer = wrongBets
            .GroupBy(x => x.PlayerId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.MatchId).ToDictionary(
                    mg => mg.Key,
                     mg => (Cups: mg.Sum(x => x.Cups),
                            Reason: $"Thua kèo: {mg.First().HomeName} vs {mg.First().AwayName}",
                            OccurredAt: mg.First().ResultUpdatedAt ?? mg.First().SettledAt)));

        var scoreBets = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId)
                && x.IsSettled
                && x.PredictedHomeScore != null
                && x.PredictedAwayScore != null
                && x.Match.ResultHomeScore != null
                && x.Match.ResultAwayScore != null
                && x.Player.TelegramUserId != null)
            .Select(x => new { x.PlayerId, x.MatchId, x.Match.HomeName, x.Match.AwayName, x.PredictedHomeScore, x.PredictedAwayScore, x.Match.ResultHomeScore, x.Match.ResultAwayScore, x.CorrectScoreOdds, x.SettledAt, x.Match.ResultUpdatedAt })
            .ToListAsync(ct);
        var scoreChangesByPlayer = scoreBets
            .GroupBy(x => x.PlayerId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.MatchId).ToDictionary(
                    mg => mg.Key,
                     mg =>
                     {
                         var first = mg.First();
                         var isExact = first.PredictedHomeScore == first.ResultHomeScore && first.PredictedAwayScore == first.ResultAwayScore;
                         return isExact
                               ? (Cups: -3, Reason: $"Đúng tỉ số 90p {first.PredictedHomeScore}-{first.PredictedAwayScore}: {first.HomeName} vs {first.AwayName}", OccurredAt: first.ResultUpdatedAt ?? first.SettledAt)
                              : (Cups: 1, Reason: $"Sai tỉ số 90p {first.PredictedHomeScore}-{first.PredictedAwayScore}, kết quả {first.ResultHomeScore}-{first.ResultAwayScore}: {first.HomeName} vs {first.AwayName}", OccurredAt: first.ResultUpdatedAt ?? first.SettledAt);
                     }));

        // Existing per-match cup logs to reconcile against (wrong_bet + missed_match are
        // reconciled by delta; the four star types are looked up for once-per-match dedup).
        var existingLogs = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && x.MatchId != null
                && (x.ChangeType == KeoBiaCupChangeType.WrongBet
                    || x.ChangeType == KeoBiaCupChangeType.MissedMatch
                    || x.ChangeType == KeoBiaCupChangeType.CorrectScore
                    || x.ChangeType == KeoBiaCupChangeType.HopeStarWin
                    || x.ChangeType == KeoBiaCupChangeType.HopeStarLose
                    || x.ChangeType == KeoBiaCupChangeType.DevilStarWin
                    || x.ChangeType == KeoBiaCupChangeType.DevilStarLose))
            .Select(x => new { x.PlayerId, x.ChangeType, MatchId = x.MatchId!.Value, x.Cups })
            .ToListAsync(ct);

        var existingNetMap = existingLogs
            .GroupBy(x => (x.ChangeType, x.PlayerId))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.MatchId).ToDictionary(mg => mg.Key, mg => mg.Sum(x => x.Cups)));
        IReadOnlyDictionary<Guid, int> ExistingNet(string changeType, Guid playerId) =>
            existingNetMap.TryGetValue((changeType, playerId), out var m) ? m : EmptyIntMap;

        var existingStarKeys = existingLogs
            .Where(x => x.ChangeType is KeoBiaCupChangeType.HopeStarWin or KeoBiaCupChangeType.HopeStarLose
                                     or KeoBiaCupChangeType.DevilStarWin or KeoBiaCupChangeType.DevilStarLose)
            .Select(x => (x.PlayerId, x.MatchId))
            .ToHashSet();

        // Pre-compute received beer and quiz reward cups per player so balanceAfter
        // matches the summary lostCups formula (which subtracts these credits).
        var receivedByPlayer = (await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && x.ChangeType == KeoBiaCupChangeType.ReceivedBeer)
            .GroupBy(x => x.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = -g.Sum(x => x.Cups) })
            .ToListAsync(ct)).ToDictionary(x => x.PlayerId, x => x.Cups);

        var quizRewardByPlayer = (await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && x.ChangeType == KeoBiaCupChangeType.QuizCorrect)
            .GroupBy(x => x.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = -g.Sum(x => x.Cups) })
            .ToListAsync(ct)).ToDictionary(x => x.PlayerId, x => x.Cups);

        var quizPenaltyByPlayer = (await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && x.ChangeType == KeoBiaCupChangeType.QuizWrong)
            .GroupBy(x => x.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = g.Sum(x => x.Cups) })
            .ToListAsync(ct)).ToDictionary(x => x.PlayerId, x => x.Cups);

        var paidBeerByPlayer = (await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && x.ChangeType == KeoBiaCupChangeType.PaidBeer)
            .GroupBy(x => x.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = -g.Sum(x => x.Cups) })
            .ToListAsync(ct)).ToDictionary(x => x.PlayerId, x => x.Cups);

        // Pre-compute PenaltyCups and balanceAfter per player so star entries
        // get the correct balance instead of 0.
        var penaltyByPlayer = new Dictionary<Guid, int>();
        var balanceAfterMap = new Dictionary<Guid, int>();
        foreach (var p in players)
        {
            var st = stats.FirstOrDefault(x => x.PlayerId == p.Id);
            var ms = missedMatchLosses.TryGetValue(p.Id, out var ml2) ? ml2 : null;
            var rawP = ms?.Count ?? 0;
            var starB = -(st?.StarWinBonus ?? 0) + (st?.StarLoseBonus ?? 0);
            penaltyByPlayer[p.Id] = Math.Max(0, rawP - p.MissedMatchCredit);

            var wdPre = wrongBetsByPlayer.TryGetValue(p.Id, out var wdi)
                ? wdi
                : new Dictionary<Guid, (int Cups, string Reason, DateTime? OccurredAt)>();
            var lb = wdPre.Values.Sum(x => x.Cups);
            var rc = receivedByPlayer.TryGetValue(p.Id, out var rcV) ? rcV : 0;
            var qr = quizRewardByPlayer.TryGetValue(p.Id, out var qrV) ? qrV : 0;
            var qp = quizPenaltyByPlayer.TryGetValue(p.Id, out var qpV) ? qpV : 0;
            var pb = paidBeerByPlayer.TryGetValue(p.Id, out var pbV) ? pbV : 0;
            var scoreNet = scoreChangesByPlayer.TryGetValue(p.Id, out var scoreV) ? scoreV.Values.Sum(x => x.Cups) : 0;
            balanceAfterMap[p.Id] = Math.Max(0, lb + penaltyByPlayer[p.Id] + p.SharedCups + starB + qp + scoreNet - rc - qr - pb);
        }

        // Star win/lose effect: logged exactly once per (player, match) — not reconciled,
        // because the underlying bet result for a settled match does not flip back.
        var starBetsNeedingLog = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && x.IsSettled && x.StarType != null)
            .Select(x => new { x.PlayerId, x.MatchId, x.StarType, x.IsCorrect, x.Match.HomeName, x.Match.AwayName })
            .ToListAsync(ct);

        foreach (var sb in starBetsNeedingLog)
        {
            if (!playersById.TryGetValue(sb.PlayerId, out var starPlayer)) continue;
            if (existingStarKeys.Contains((sb.PlayerId, sb.MatchId))) continue;

            var isWin = sb.IsCorrect == true;
            var changeType = sb.StarType == "hope"
                ? (isWin ? KeoBiaCupChangeType.HopeStarWin : KeoBiaCupChangeType.HopeStarLose)
                : (isWin ? KeoBiaCupChangeType.DevilStarWin : KeoBiaCupChangeType.DevilStarLose);
            var cups = isWin ? -1 : 1;
            var label = sb.StarType == "hope" ? "Ngôi Sao Hi Vọng" : "Ngôi Sao Ma Quỷ";
            var result = isWin ? "thắng" : "thua";
            var starBalance = balanceAfterMap.TryGetValue(sb.PlayerId, out var sbal) ? sbal : 0;
            AddCupLog(starPlayer, sb.MatchId, changeType, cups,
                $"{label} {result}: {sb.HomeName} vs {sb.AwayName}", starBalance);
        }

        foreach (var player in players)
        {
            var stat = stats.FirstOrDefault(x => x.PlayerId == player.Id);
            player.TotalBets = stat?.TotalBets ?? 0;
            player.TotalCups = stat?.TotalCups ?? 0;
            player.CorrectBets = stat?.CorrectBets ?? 0;
            player.WrongBets = stat?.WrongBets ?? 0;
            var missed = missedMatchLosses.TryGetValue(player.Id, out var ml) ? ml : null;
            player.PenaltyCups = penaltyByPlayer.TryGetValue(player.Id, out var penVal) ? penVal : 0;
            player.UpdatedAt = DateTime.UtcNow;

            // Journal the two largest loss drivers per match so KeoBiaCupLogs always
            // reconciles to the computed loss — including reversals when a bet is deleted,
            // a result is undone, or a player is unblocked.
            var wrongDesired = wrongBetsByPlayer.TryGetValue(player.Id, out var wd)
                ? wd
                : new Dictionary<Guid, (int Cups, string Reason, DateTime? OccurredAt)>();
            var balanceAfter = balanceAfterMap.TryGetValue(player.Id, out var baVal) ? baVal : 0;

            ReconcilePerMatchCupLog(player, KeoBiaCupChangeType.WrongBet,
                wrongDesired, ExistingNet(KeoBiaCupChangeType.WrongBet, player.Id),
                "Hoàn cốc bia hoặc gói lạc: kèo bị xoá hoặc kết quả thay đổi", balanceAfter);

            var missedDesired = missed is null
                ? new Dictionary<Guid, (int Cups, string Reason, DateTime? OccurredAt)>()
                : missed.ToDictionary(m => m.MatchId, m => (Cups: 1, Reason: $"Không tham gia: {m.Label}", OccurredAt: (DateTime?)m.OccurredAt));
            ReconcilePerMatchCupLog(player, KeoBiaCupChangeType.MissedMatch,
                missedDesired, ExistingNet(KeoBiaCupChangeType.MissedMatch, player.Id),
                "Hoàn cốc bia hoặc gói lạc: trận bị huỷ hoặc người chơi được mở khoá", balanceAfter);

            var scoreDesired = scoreChangesByPlayer.TryGetValue(player.Id, out var sd)
                ? sd
                : new Dictionary<Guid, (int Cups, string Reason, DateTime? OccurredAt)>();
            ReconcilePerMatchCupLog(player, KeoBiaCupChangeType.CorrectScore,
                scoreDesired, ExistingNet(KeoBiaCupChangeType.CorrectScore, player.Id),
                "Hoàn/thu hồi: tỉ số 90p thay đổi", balanceAfter);
        }

        await _db.SaveChangesAsync(ct);
        await RecomputeCupLogBalancesAsync(ids, ct);
    }

    private async Task BackfillRegistrationMissedMatchPromotionAsync(CancellationToken ct)
    {
        var siteIds = await _db.KeoBiaPlayers.IgnoreQueryFilters()
            .Where(x => x.TelegramUserId != null
                && x.CreatedAt >= KeoBiaUnitRules.PeanutSwitchAtUtc)
            .Select(x => x.SiteId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var siteId in siteIds)
        {
            _currentSite!.Set(siteId, _currentSite.Slug, _currentSite.Theme);
            var playerIds = await _db.KeoBiaPlayers
                .Where(x => x.TelegramUserId != null
                    && x.CreatedAt >= KeoBiaUnitRules.PeanutSwitchAtUtc)
                .Select(x => x.Id)
                .ToListAsync(ct);
            await RecalculatePlayersAsync(playerIds, ct);
        }
    }

    private async Task ApplyRegistrationMissedMatchPromotionAsync(
        IReadOnlyList<KeoBiaPlayer> players,
        IReadOnlyDictionary<Guid, List<MissedMatchInfo>> missedMatchLosses,
        CancellationToken ct)
    {
        var eligible = players
            .Where(x => x.CreatedAt >= KeoBiaUnitRules.PeanutSwitchAtUtc)
            .ToList();
        if (eligible.Count == 0) return;

        var ids = eligible.Select(x => x.Id).ToArray();
        var existingPromotionRows = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId)
                && x.ChangeType == KeoBiaCupChangeType.MissedMatchCredit
                && x.Reason.StartsWith("Khuyến mãi đăng ký miễn phạt"))
            .Select(x => new { x.PlayerId, x.Cups, x.Reason, x.CreatedAt })
            .ToListAsync(ct);

        foreach (var player in eligible)
        {
            var missed = missedMatchLosses.GetValueOrDefault(player.Id) ?? [];
            var rawBeerMisses = missed.Count(x => !KeoBiaUnitRules.UsesPeanut(x.Stage, x.OccurredAt));
            var rawPeanutMisses = missed.Count - rawBeerMisses;
            var playerPromotionRows = existingPromotionRows.Where(x => x.PlayerId == player.Id).ToList();
            var existingBeerCredit = -playerPromotionRows
                .Where(x => ResolveCupLogUnitCode(x.Reason, null, x.CreatedAt) == KeoBiaUnitCode.Beer)
                .Sum(x => x.Cups);
            var existingPeanutCredit = -playerPromotionRows
                .Where(x => ResolveCupLogUnitCode(x.Reason, null, x.CreatedAt) == KeoBiaUnitCode.Peanut)
                .Sum(x => x.Cups);
            var existingPromotionCredit = existingBeerCredit + existingPeanutCredit;
            var nonPromotionCredit = Math.Max(0, player.MissedMatchCredit - existingPromotionCredit);
            var promotionBudget = Math.Max(
                0,
                rawBeerMisses - KeoBiaUnitRules.RegistrationMissedMatchPenaltyCapCups + rawPeanutMisses - nonPromotionCredit);
            var desiredPeanutCredit = Math.Min(rawPeanutMisses, promotionBudget);
            var desiredBeerCredit = Math.Min(
                Math.Max(0, rawBeerMisses - KeoBiaUnitRules.RegistrationMissedMatchPenaltyCapCups),
                promotionBudget - desiredPeanutCredit);
            var beerDelta = desiredBeerCredit - existingBeerCredit;
            var peanutDelta = desiredPeanutCredit - existingPeanutCredit;
            if (beerDelta == 0 && peanutDelta == 0) continue;

            player.MissedMatchCredit = Math.Max(0, player.MissedMatchCredit + beerDelta + peanutDelta);
            if (beerDelta != 0)
            {
                AddCupLog(
                    player,
                    null,
                    KeoBiaCupChangeType.MissedMatchCredit,
                    -beerDelta,
                    $"Khuyến mãi đăng ký miễn phạt điều chỉnh {Math.Abs(beerDelta)} cốc bia",
                    0,
                    KeoBiaUnitRules.PeanutSwitchAtUtc.AddTicks(-1));
            }
            if (peanutDelta != 0)
            {
                AddCupLog(
                    player,
                    null,
                    KeoBiaCupChangeType.MissedMatchCredit,
                    -peanutDelta,
                    $"Khuyến mãi đăng ký miễn phạt điều chỉnh {Math.Abs(peanutDelta)} gói lạc",
                    0,
                    KeoBiaUnitRules.PeanutSwitchAtUtc);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task RecomputeCupLogBalancesAsync(IEnumerable<Guid> playerIds, CancellationToken ct)
    {
        var ids = playerIds.Distinct().ToArray();
        if (ids.Length == 0) return;

        var logs = await _db.KeoBiaCupLogs
            .Where(x => ids.Contains(x.PlayerId))
            .OrderBy(x => x.PlayerId)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        if (logs.Count == 0) return;

        Guid? currentPlayerId = null;
        var running = 0;
        foreach (var log in logs)
        {
            if (currentPlayerId != log.PlayerId)
            {
                currentPlayerId = log.PlayerId;
                running = 0;
            }

            running += log.Cups;
            log.BalanceAfter = Math.Max(0, running);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static readonly Dictionary<Guid, int> EmptyIntMap = new();

    private static readonly string[] UnitAttributedChangeTypes =
    [
        KeoBiaCupChangeType.WrongBet,
        KeoBiaCupChangeType.MissedMatch,
        KeoBiaCupChangeType.MissedMatchCredit,
        KeoBiaCupChangeType.SharedBeer,
        KeoBiaCupChangeType.QuizWrong,
        KeoBiaCupChangeType.HopeStarWin,
        KeoBiaCupChangeType.HopeStarLose,
        KeoBiaCupChangeType.DevilStarWin,
        KeoBiaCupChangeType.DevilStarLose,
        KeoBiaCupChangeType.CorrectScore
    ];

    private static readonly string[] UnitEffectChangeTypes =
    [
        KeoBiaCupChangeType.SharedBeer,
        KeoBiaCupChangeType.ReceivedBeer,
        KeoBiaCupChangeType.MissedMatchCredit,
        KeoBiaCupChangeType.QuizCorrect,
        KeoBiaCupChangeType.QuizWrong,
        KeoBiaCupChangeType.HopeStarWin,
        KeoBiaCupChangeType.HopeStarLose,
        KeoBiaCupChangeType.DevilStarWin,
        KeoBiaCupChangeType.DevilStarLose,
        KeoBiaCupChangeType.CorrectScore
    ];

    private async Task<Dictionary<Guid, PlayerUnitEffects>> GetPlayerUnitEffectsAsync(
        IEnumerable<Guid> playerIds,
        CancellationToken ct)
    {
        var ids = playerIds.Distinct().ToArray();
        if (ids.Length == 0) return [];

        var rows = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && UnitEffectChangeTypes.Contains(x.ChangeType))
            .Select(x => new
            {
                x.PlayerId,
                x.ChangeType,
                x.Cups,
                x.Reason,
                x.CreatedAt,
                MatchStage = x.Match != null ? x.Match.Stage : null,
                MatchKickoffAt = x.Match != null ? (DateTime?)x.Match.KickoffAt : null
            })
            .ToListAsync(ct);

        var result = ids.ToDictionary(x => x, _ => new PlayerUnitEffects());
        foreach (var row in rows)
        {
            var effects = result[row.PlayerId];
            var unitCode = ResolveCupLogUnitCode(
                row.Reason, row.MatchStage, row.MatchKickoffAt ?? row.CreatedAt);
            switch (row.ChangeType)
            {
                case KeoBiaCupChangeType.SharedBeer:
                    effects.Shared = AddUnit(effects.Shared, row.Cups, unitCode);
                    break;
                case KeoBiaCupChangeType.ReceivedBeer:
                    effects.Received = AddUnit(effects.Received, -row.Cups, unitCode);
                    break;
                case KeoBiaCupChangeType.MissedMatchCredit:
                    effects.Promo = AddUnit(effects.Promo, -row.Cups, unitCode);
                    break;
                case KeoBiaCupChangeType.QuizCorrect:
                    effects.QuizReward = AddUnit(effects.QuizReward, -row.Cups, unitCode);
                    break;
                case KeoBiaCupChangeType.QuizWrong:
                    effects.QuizPenalty = AddUnit(effects.QuizPenalty, row.Cups, unitCode);
                    break;
                case KeoBiaCupChangeType.HopeStarWin:
                case KeoBiaCupChangeType.HopeStarLose:
                    effects.HopeStar = AddUnit(effects.HopeStar, row.Cups, unitCode);
                    effects.Star = AddUnit(effects.Star, row.Cups, unitCode);
                    break;
                case KeoBiaCupChangeType.DevilStarWin:
                case KeoBiaCupChangeType.DevilStarLose:
                    effects.DevilStar = AddUnit(effects.DevilStar, row.Cups, unitCode);
                    effects.Star = AddUnit(effects.Star, row.Cups, unitCode);
                    break;
                case KeoBiaCupChangeType.CorrectScore when row.Cups < 0:
                    effects.CorrectScoreReward = AddUnit(
                        effects.CorrectScoreReward, -row.Cups, unitCode);
                    break;
                case KeoBiaCupChangeType.CorrectScore when row.Cups > 0:
                    effects.CorrectScorePenalty = AddUnit(
                        effects.CorrectScorePenalty, row.Cups, unitCode);
                    break;
            }
        }

        return result;
    }

    private static UnitBalance AddUnit(UnitBalance current, int cups, string? unitCode) =>
        string.Equals(unitCode, KeoBiaUnitCode.Peanut, StringComparison.OrdinalIgnoreCase)
            ? current with { PeanutPacks = current.PeanutPacks + cups }
            : current with { BeerCups = current.BeerCups + cups };

    private async Task<List<KeoBiaPlayerListItemDto>> EnrichPlayerUnitStatsAsync(
        IReadOnlyList<KeoBiaPlayerListItemDto> players,
        CancellationToken ct)
    {
        if (players.Count == 0) return [];

        var ids = players.Select(x => x.Id).ToArray();
        var betRows = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId))
            .Select(x => new { x.PlayerId, x.Cups, x.Match.Stage, x.Match.KickoffAt })
            .ToListAsync(ct);
        var totalAttributed = ids.ToDictionary(x => x, _ => UnitBalance.Empty);
        foreach (var row in betRows)
        {
            var current = totalAttributed[row.PlayerId];
            totalAttributed[row.PlayerId] = KeoBiaUnitRules.UsesPeanut(row.Stage, row.KickoffAt)
                ? current with { PeanutPacks = current.PeanutPacks + row.Cups }
                : current with { BeerCups = current.BeerCups + row.Cups };
        }

        var currentBalances = await ComputePlayerLossBalancesAsync(players, ct);
        var outstandingUnits = await ComputeLossUnitBalancesAsync(currentBalances, ct);
        var unitEffects = await GetPlayerUnitEffectsAsync(ids, ct);

        return players.Select(player =>
        {
            var totalRaw = totalAttributed[player.Id];
            var totalUnits = new UnitBalance(
                Math.Max(0, totalRaw.BeerCups),
                Math.Max(0, totalRaw.PeanutPacks));
            var effects = unitEffects.GetValueOrDefault(player.Id, PlayerUnitEffects.Empty);
            var sharedUnits = ReconcileUnitBalance(
                player.SharedCups, effects.Shared.BeerCups, effects.Shared.PeanutPacks);
            var missedCreditUnits = ReconcileUnitBalance(
                player.MissedMatchCredit, effects.Promo.BeerCups, effects.Promo.PeanutPacks);
            var outstanding = outstandingUnits.GetValueOrDefault(player.Id, UnitBalance.Empty);
            return player with
            {
                TotalBeerCups = totalUnits.BeerCups,
                TotalPeanutPacks = totalUnits.PeanutPacks,
                OutstandingBeerCups = outstanding.BeerCups,
                OutstandingPeanutPacks = outstanding.PeanutPacks,
                SharedBeerCups = sharedUnits.BeerCups,
                SharedPeanutPacks = sharedUnits.PeanutPacks,
                MissedCreditBeerCups = missedCreditUnits.BeerCups,
                MissedCreditPeanutPacks = missedCreditUnits.PeanutPacks
            };
        }).ToList();
    }

    private async Task<Dictionary<Guid, int>> ComputePlayerLossBalancesAsync(
        IReadOnlyList<KeoBiaPlayerListItemDto> players,
        CancellationToken ct)
    {
        var ids = players.Select(x => x.Id).ToArray();
        var lostBetRows = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && x.IsSettled && x.IsCorrect == false)
            .GroupBy(x => x.PlayerId)
            .Select(g => new { PlayerId = g.Key, Cups = g.Sum(x => x.Cups) })
            .ToListAsync(ct);
        var logRows = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId))
            .GroupBy(x => x.PlayerId)
            .Select(g => new
            {
                PlayerId = g.Key,
                ReceivedCups = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.ReceivedBeer).Sum(x => x.Cups),
                QuizRewardCups = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.QuizCorrect).Sum(x => x.Cups),
                QuizPenaltyCups = g.Where(x => x.ChangeType == KeoBiaCupChangeType.QuizWrong).Sum(x => x.Cups),
                PaidCups = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.PaidBeer).Sum(x => x.Cups),
                StarCupEffect = g.Where(x =>
                    x.ChangeType == KeoBiaCupChangeType.HopeStarWin ||
                    x.ChangeType == KeoBiaCupChangeType.HopeStarLose ||
                    x.ChangeType == KeoBiaCupChangeType.DevilStarWin ||
                    x.ChangeType == KeoBiaCupChangeType.DevilStarLose).Sum(x => x.Cups),
                CorrectScoreReward = -g.Where(x => x.ChangeType == KeoBiaCupChangeType.CorrectScore).Sum(x => x.Cups)
            })
            .ToListAsync(ct);

        var lostByPlayer = lostBetRows.ToDictionary(x => x.PlayerId, x => x.Cups);
        var logsByPlayer = logRows.ToDictionary(x => x.PlayerId);
        return players.ToDictionary(player => player.Id, player =>
        {
            lostByPlayer.TryGetValue(player.Id, out var lostBetCups);
            logsByPlayer.TryGetValue(player.Id, out var log);
            return Math.Max(0,
                lostBetCups + player.PenaltyCups + player.SharedCups +
                (log?.StarCupEffect ?? 0) + (log?.QuizPenaltyCups ?? 0) -
                (log?.ReceivedCups ?? 0) - (log?.QuizRewardCups ?? 0) -
                (log?.PaidCups ?? 0) - (log?.CorrectScoreReward ?? 0));
        });
    }

    private async Task<UnitBalance> ComputeLossUnitBalanceAsync(Guid playerId, int currentBalance, CancellationToken ct)
    {
        var balances = await ComputeLossUnitBalancesAsync(
            new Dictionary<Guid, int> { [playerId] = currentBalance }, ct);
        return balances.GetValueOrDefault(playerId, UnitBalance.Empty);
    }

    private async Task<Dictionary<Guid, UnitBalance>> ComputeLossUnitBalancesAsync(
        IReadOnlyDictionary<Guid, int> currentBalances,
        CancellationToken ct)
    {
        var ids = currentBalances.Keys.ToArray();
        if (ids.Length == 0) return [];

        var rows = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && UnitAttributedChangeTypes.Contains(x.ChangeType))
            .Select(x => new
            {
                x.PlayerId,
                x.Cups,
                x.CreatedAt,
                MatchStage = x.Match != null ? x.Match.Stage : null,
                MatchKickoffAt = x.Match != null ? (DateTime?)x.Match.KickoffAt : null
            })
            .ToListAsync(ct);

        var beerByPlayer = ids.ToDictionary(x => x, _ => 0);
        var peanutByPlayer = ids.ToDictionary(x => x, _ => 0);
        foreach (var row in rows)
        {
            var occurredAt = row.MatchKickoffAt ?? row.CreatedAt;
            if (KeoBiaUnitRules.UsesPeanut(row.MatchStage, occurredAt))
                peanutByPlayer[row.PlayerId] += row.Cups;
            else
                beerByPlayer[row.PlayerId] += row.Cups;
        }

        return currentBalances.ToDictionary(
            x => x.Key,
            x => ReconcileUnitBalance(x.Value, beerByPlayer[x.Key], peanutByPlayer[x.Key]));
    }

    private static UnitBalance ReconcileUnitBalance(int currentBalance, int attributedBeerCups, int attributedPeanutPacks)
    {
        var balance = KeoBiaUnitRules.ReconcileBalance(
            currentBalance, attributedBeerCups, attributedPeanutPacks);
        return new UnitBalance(balance.BeerCups, balance.PeanutPacks);
    }

    private async Task<UnitBalance> GetPaidUnitBreakdownAsync(Guid playerId, int paidUnits, CancellationToken ct)
    {
        var balances = await GetPaidUnitBreakdownsAsync(
            new Dictionary<Guid, int> { [playerId] = paidUnits }, ct);
        return balances.GetValueOrDefault(playerId, UnitBalance.Empty);
    }

    private async Task<Dictionary<Guid, UnitBalance>> GetPaidUnitBreakdownsAsync(
        IReadOnlyDictionary<Guid, int> paidUnitsByPlayer,
        CancellationToken ct)
    {
        var ids = paidUnitsByPlayer.Keys.ToArray();
        if (ids.Length == 0) return [];

        var rows = await _db.KeoBiaBeerPayments.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && x.Status == KeoBiaBeerPaymentStatus.Paid)
            .Select(x => new { x.PlayerId, x.Cups, x.CoinAmount })
            .ToListAsync(ct);

        var beerByPlayer = ids.ToDictionary(x => x, _ => 0);
        var peanutByPlayer = ids.ToDictionary(x => x, _ => 0);
        foreach (var row in rows)
        {
            var breakdown = KeoBiaUnitRules.DecodePaymentBreakdown(row.Cups, row.CoinAmount);
            beerByPlayer[row.PlayerId] += breakdown.BeerCups;
            peanutByPlayer[row.PlayerId] += breakdown.PeanutPacks;
        }

        var result = new Dictionary<Guid, UnitBalance>(ids.Length);
        foreach (var (playerId, paidUnits) in paidUnitsByPlayer)
        {
            var beerCups = beerByPlayer[playerId];
            var peanutPacks = peanutByPlayer[playerId];
            if (beerCups + peanutPacks < paidUnits)
                beerCups += paidUnits - beerCups - peanutPacks;

            beerCups = Math.Min(beerCups, paidUnits);
            peanutPacks = Math.Min(peanutPacks, paidUnits - beerCups);
            result[playerId] = new UnitBalance(beerCups, peanutPacks);
        }

        return result;
    }

    private async Task<int> ComputeLossBalanceAsync(KeoBiaPlayer player, CancellationToken ct)
    {
        var lostBetCups = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.IsSettled && x.IsCorrect == false)
            .SumAsync(x => (int?)x.Cups, ct) ?? 0;

        var receivedCups = -(await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.ReceivedBeer)
            .SumAsync(x => x.Cups, ct));

        var quizRewardCups = -(await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.QuizCorrect)
            .SumAsync(x => x.Cups, ct));

        var quizPenaltyCups = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.QuizWrong)
            .SumAsync(x => x.Cups, ct);

        var paidBeerCups = -(await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.PaidBeer)
            .SumAsync(x => x.Cups, ct));

        var starCupEffect = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && (x.ChangeType == KeoBiaCupChangeType.HopeStarWin || x.ChangeType == KeoBiaCupChangeType.HopeStarLose || x.ChangeType == KeoBiaCupChangeType.DevilStarWin || x.ChangeType == KeoBiaCupChangeType.DevilStarLose))
            .SumAsync(x => x.Cups, ct);

        var correctScoreReward = -(await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => x.PlayerId == player.Id && x.ChangeType == KeoBiaCupChangeType.CorrectScore)
            .SumAsync(x => x.Cups, ct));

        return Math.Max(0, lostBetCups + player.PenaltyCups + player.SharedCups + starCupEffect + quizPenaltyCups - receivedCups - quizRewardCups - paidBeerCups - correctScoreReward);
    }

    private sealed record UnitBalance(int BeerCups, int PeanutPacks)
    {
        public static UnitBalance Empty { get; } = new(0, 0);
    }

    private sealed class PlayerUnitEffects
    {
        public static PlayerUnitEffects Empty { get; } = new();
        public UnitBalance Shared { get; set; } = UnitBalance.Empty;
        public UnitBalance Received { get; set; } = UnitBalance.Empty;
        public UnitBalance Promo { get; set; } = UnitBalance.Empty;
        public UnitBalance Star { get; set; } = UnitBalance.Empty;
        public UnitBalance HopeStar { get; set; } = UnitBalance.Empty;
        public UnitBalance DevilStar { get; set; } = UnitBalance.Empty;
        public UnitBalance QuizReward { get; set; } = UnitBalance.Empty;
        public UnitBalance QuizPenalty { get; set; } = UnitBalance.Empty;
        public UnitBalance CorrectScoreReward { get; set; } = UnitBalance.Empty;
        public UnitBalance CorrectScorePenalty { get; set; } = UnitBalance.Empty;
    }

    private async Task<Dictionary<Guid, MatchStats>> GetMatchStatsAsync(IEnumerable<Guid> matchIds, CancellationToken ct)
    {
        var ids = matchIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, MatchStats>();

        var rows = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => ids.Contains(x.MatchId) && x.Player.TelegramUserId != null && !x.Player.IsBlocked)
            .GroupBy(x => new { x.MatchId, x.Choice })
            .Select(g => new
            {
                g.Key.MatchId,
                g.Key.Choice,
                BetCount = g.Count(),
                CupCount = g.Sum(x => x.Cups),
                PlayerCount = g.Select(x => x.PlayerId).Distinct().Count()
            })
            .ToListAsync(ct);

        var stats = ids.ToDictionary(x => x, _ => MatchStats.Empty);
        foreach (var row in rows)
        {
            var current = stats[row.MatchId];
            current.BetCount += row.BetCount;
            current.CupCount += row.CupCount;
            if (row.Choice == KeoBiaBetChoice.Home) { current.HomeCups += row.CupCount; current.HomeBettors += row.PlayerCount; }
            else if (row.Choice == KeoBiaBetChoice.Draw) { current.DrawCups += row.CupCount; current.DrawBettors += row.PlayerCount; }
            else if (row.Choice == KeoBiaBetChoice.Away) { current.AwayCups += row.CupCount; current.AwayBettors += row.PlayerCount; }
            stats[row.MatchId] = current;
        }

        return stats;
    }

    private static KeoBiaPublicMatchDto MapPublicMatch(KeoBiaMatch match, IReadOnlyDictionary<Guid, MatchStats> stats)
    {
        var stat = stats.TryGetValue(match.Id, out var s) ? s : MatchStats.Empty;
        var weights = NormalizePercents(
            Math.Max(1, match.BaseHomeWeight + stat.HomeCups * 4),
            Math.Max(1, match.BaseDrawWeight + stat.DrawCups * 4),
            Math.Max(1, match.BaseAwayWeight + stat.AwayCups * 4));
        var aiWeights = ResolveAiPercents(match);
        // No AI analysis yet — fall back to the static base-weight prior so the
        // "AI dự đoán" bar always has sensible, bet-independent values.
        if (aiWeights.Home == 0 && aiWeights.Draw == 0 && aiWeights.Away == 0)
            aiWeights = NormalizePercents(
                Math.Max(1, match.BaseHomeWeight),
                Math.Max(1, match.BaseDrawWeight),
                Math.Max(1, match.BaseAwayWeight));

        return new KeoBiaPublicMatchDto(
            match.Id, match.ExternalId, match.Stage, match.HomeName, match.HomeCode,
            match.HomePrimary, match.HomeSecondary, match.AwayName, match.AwayCode,
            match.AwayPrimary, match.AwaySecondary, match.KickoffAt, match.Venue,
            match.IsHot, match.HotLabel ?? string.Empty,
            weights.Home, weights.Draw, weights.Away,
            aiWeights.Home, aiWeights.Draw, aiWeights.Away, match.AiSummary ?? string.Empty,
            match.DefaultCups, match.Status, match.HomeScore, match.AwayScore, match.ResultHomeScore, match.ResultAwayScore, match.PenaltyHomeScore, match.PenaltyAwayScore, match.ResultChoice,
            stat.HomeBettors, stat.DrawBettors, stat.AwayBettors, NormalizeCorrectScoreOddsJson(match.CorrectScoreOddsJson, out _));
    }

    private static (int Home, int Draw, int Away) ResolveAiPercents(KeoBiaMatch match)
    {
        var fromJson = TryReadAiProbability(match.AiAnalysisProbabilityJson);
        if (fromJson is not null)
        {
            return fromJson.Value;
        }

        if (match.AiHome > 0 || match.AiDraw > 0 || match.AiAway > 0)
        {
            return NormalizePercents(match.AiHome, match.AiDraw, match.AiAway);
        }

        return (0, 0, 0);
    }

    private static (int Home, int Draw, int Away)? TryReadAiProbability(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var home = ReadInt(root, "home");
            var draw = ReadInt(root, "draw");
            var away = ReadInt(root, "away");
            if (home is null || draw is null || away is null)
            {
                return null;
            }

            if (home.Value <= 0 && draw.Value <= 0 && away.Value <= 0)
            {
                return null;
            }

            return NormalizePercents(home.Value, draw.Value, away.Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static (int Home, int Draw, int Away) NormalizePercents(int home, int draw, int away)
    {
        var total = Math.Max(1, home + draw + away);
        var h = (int)Math.Floor(home * 100m / total);
        var d = (int)Math.Floor(draw * 100m / total);
        var a = (int)Math.Floor(away * 100m / total);
        var rest = 100 - h - d - a;
        while (rest-- > 0)
        {
            if (home >= draw && home >= away) h++;
            else if (away >= draw) a++;
            else d++;
        }
        return (h, d, a);
    }

    private static void ApplyMatch(KeoBiaMatch entity, KeoBiaMatchUpsertDto dto)
    {
        entity.ExternalId = dto.ExternalId.Trim();
        entity.Stage = dto.Stage.Trim();
        entity.HomeName = dto.HomeName.Trim();
        entity.HomeCode = dto.HomeCode.Trim().ToUpperInvariant();
        entity.HomePrimary = string.IsNullOrWhiteSpace(dto.HomePrimary) ? "#1f7a3a" : dto.HomePrimary.Trim();
        entity.HomeSecondary = string.IsNullOrWhiteSpace(dto.HomeSecondary) ? "#176030" : dto.HomeSecondary.Trim();
        entity.AwayName = dto.AwayName.Trim();
        entity.AwayCode = dto.AwayCode.Trim().ToUpperInvariant();
        entity.AwayPrimary = string.IsNullOrWhiteSpace(dto.AwayPrimary) ? "#1565c0" : dto.AwayPrimary.Trim();
        entity.AwaySecondary = string.IsNullOrWhiteSpace(dto.AwaySecondary) ? "#0a335f" : dto.AwaySecondary.Trim();
        entity.KickoffAt = dto.KickoffAt;
        entity.Venue = dto.Venue.Trim();
        entity.IsHot = dto.IsHot;
        entity.HotLabel = dto.HotLabel?.Trim();
        entity.Status = NormalizeStatus(dto.Status);
        entity.BaseHomeWeight = Math.Clamp(dto.BaseHomeWeight, 1, 98);
        entity.BaseDrawWeight = Math.Clamp(dto.BaseDrawWeight, 1, 98);
        entity.BaseAwayWeight = Math.Clamp(dto.BaseAwayWeight, 1, 98);
        var hasStoredAnalysis = !string.IsNullOrWhiteSpace(entity.AiAnalysisContent)
            || !string.IsNullOrWhiteSpace(entity.AiAnalysisProbabilityJson);
        var dtoHasAiWeights = dto.AiHome > 0 || dto.AiDraw > 0 || dto.AiAway > 0;
        if (!hasStoredAnalysis || dtoHasAiWeights)
        {
            entity.AiHome = Math.Clamp(dto.AiHome, 0, 100);
            entity.AiDraw = Math.Clamp(dto.AiDraw, 0, 100);
            entity.AiAway = Math.Clamp(dto.AiAway, 0, 100);
            entity.AiSummary = string.IsNullOrWhiteSpace(dto.AiSummary) ? null : dto.AiSummary.Trim();
        }
        entity.DefaultCups = Math.Clamp(dto.DefaultCups, 1, MaxCups);
        entity.CorrectScoreOddsJson = string.IsNullOrWhiteSpace(dto.CorrectScoreOddsJson) ? null : dto.CorrectScoreOddsJson.Trim();
        entity.UpdatedAt = DateTime.UtcNow;
    }

    private static string? ValidateMatch(KeoBiaMatchUpsertDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ExternalId)) return "Thiếu mã trận.";
        if (string.IsNullOrWhiteSpace(dto.Stage)) return "Thiếu bảng/vòng đấu.";
        if (string.IsNullOrWhiteSpace(dto.HomeName) || string.IsNullOrWhiteSpace(dto.AwayName)) return "Thiếu tên đội.";
        if (string.IsNullOrWhiteSpace(dto.HomeCode) || string.IsNullOrWhiteSpace(dto.AwayCode)) return "Thiếu mã đội.";
        if (string.IsNullOrWhiteSpace(dto.Venue)) return "Thiếu sân đấu.";
        return null;
    }

    private static List<CsvRow> ReadCsv(string csv)
    {
        using var reader = new StringReader(csv);
        using var parser = new TextFieldParser(reader)
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",", ";", "\t");

        if (parser.EndOfData) return [];
        var headers = parser.ReadFields() ?? [];
        var map = headers
            .Select((h, i) => new { Name = h.Trim().ToLowerInvariant(), Index = i })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);

        var rows = new List<CsvRow>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new CsvRow(parser.LineNumber, map, fields));
        }
        return rows;
    }

    private static KeoBiaMatchUpsertDto RowToMatchDto(CsvRow row)
    {
        var kickoffRaw = row.Get("kickoffAt", "kickoff", "time", "date");
        if (!DateTime.TryParse(kickoffRaw, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.AssumeLocal, out var kickoff)
            && !DateTime.TryParse(kickoffRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out kickoff))
        {
            throw new InvalidOperationException("KickoffAt không hợp lệ.");
        }

        return new KeoBiaMatchUpsertDto(
            null,
            row.Get("externalId", "id", "matchId") ?? string.Empty,
            row.Get("stage", "group") ?? string.Empty,
            row.Get("homeName", "home") ?? string.Empty,
            row.Get("homeCode") ?? string.Empty,
            row.Get("homePrimary", defaultValue: "#1f7a3a") ?? "#1f7a3a",
            row.Get("homeSecondary", defaultValue: "#176030") ?? "#176030",
            row.Get("awayName", "away") ?? string.Empty,
            row.Get("awayCode") ?? string.Empty,
            row.Get("awayPrimary", defaultValue: "#1565c0") ?? "#1565c0",
            row.Get("awaySecondary", defaultValue: "#0a335f") ?? "#0a335f",
            kickoff,
            row.Get("venue", defaultValue: "World Cup 2026") ?? "World Cup 2026",
            row.GetBool("isHot"),
            row.Get("hotLabel", defaultValue: null),
            row.Get("status", defaultValue: KeoBiaMatchStatus.Scheduled) ?? KeoBiaMatchStatus.Scheduled,
            row.GetInt("baseHomeWeight", 40),
            row.GetInt("baseDrawWeight", 20),
            row.GetInt("baseAwayWeight", 40),
            row.GetInt("aiHome", 0),
            row.GetInt("aiDraw", 0),
            row.GetInt("aiAway", 0),
            row.Get("aiSummary", defaultValue: null),
            row.GetInt("defaultCups", 1));
    }

    private static JsonDocument? ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OpenFootballParsedMatch OpenFootballMatchToDto(JsonElement item, int rowNumber)
    {
        var home = ReadString(item, "team1", "home", "homeName");
        var away = ReadString(item, "team2", "away", "awayName");
        var date = ReadString(item, "date");
        var time = ReadString(item, "time");

        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
            throw new InvalidOperationException("Thiếu team1/team2.");
        if (!TryParseOpenFootballKickoff(date, time, out var kickoff))
            throw new InvalidOperationException("date/time không hợp lệ.");
        var homeName = home.Trim();
        var awayName = away.Trim();

        var score = TryReadOpenFootballScore(item, out var homeScore, out var awayScore);
        var sourceStatus = ReadString(item, "status", "state");
        var status = score ? KeoBiaMatchStatus.Finished : NormalizeOpenFootballStatus(sourceStatus);
        var group = ReadString(item, "group");
        var round = ReadString(item, "round", "stage");
        var stage = !string.IsNullOrWhiteSpace(group) ? group.Trim() : round?.Trim();
        if (string.IsNullOrWhiteSpace(stage)) stage = "World Cup 2026";

        var venue = ReadString(item, "ground", "venue", "stadium");
        if (string.IsNullOrWhiteSpace(venue)) venue = "World Cup 2026";
        else venue = venue.Trim();

        var externalId = ReadString(item, "id", "externalId", "matchId");
        if (string.IsNullOrWhiteSpace(externalId))
            externalId = BuildOpenFootballExternalId(kickoff, homeName, awayName, rowNumber);
        else
            externalId = externalId.Trim();

        var isHot = IsOpenFootballHotMatch(homeName, awayName);
        var hotLabel = isHot ? BuildOpenFootballHotLabel(homeName, awayName) : null;

        var match = new KeoBiaMatchUpsertDto(
            null,
            externalId!,
            stage!,
            homeName,
            ToTeamCode(homeName),
            "#1f7a3a",
            "#176030",
            awayName,
            ToTeamCode(awayName),
            "#1565c0",
            "#0a335f",
            kickoff,
            venue!,
            isHot,
            hotLabel,
            status,
            40,
            20,
            40,
            0,
            0,
            0,
            null,
            1);

        return new OpenFootballParsedMatch(match, score ? homeScore : null, score ? awayScore : null);
    }

    private static bool IsOpenFootballHotMatch(string homeName, string awayName)
    {
        return IsOpenFootballHeadlineTeam(homeName) || IsOpenFootballHeadlineTeam(awayName);
    }

    private static bool IsOpenFootballHeadlineTeam(string teamName) =>
        !string.IsNullOrWhiteSpace(teamName) && OpenFootballHeadlineTeamRegex.IsMatch(teamName.Trim());

    private static string BuildOpenFootballHotLabel(string homeName, string awayName)
    {
        if (IsOpenFootballHeadlineTeam(homeName) && IsOpenFootballHeadlineTeam(awayName)) return "Đại chiến";
        return "Cầu đinh";
    }

    private static bool TryParseOpenFootballKickoff(string? dateRaw, string? timeRaw, out DateTime kickoffUtc)
    {
        kickoffUtc = default;
        if (string.IsNullOrWhiteSpace(dateRaw)) return false;

        if (!DateOnly.TryParseExact(dateRaw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            if (!DateTime.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
                return false;
            date = DateOnly.FromDateTime(parsedDate);
        }

        var time = string.IsNullOrWhiteSpace(timeRaw) ? "00:00" : timeRaw.Trim();
        var match = Regex.Match(time, @"^(?<hour>\d{1,2}):(?<minute>\d{2})(?:\s*(?:UTC|GMT)\s*(?<offset>[+-]\d{1,2})(?::?(?<offsetMinute>\d{2}))?)?$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
            var minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
            if (hour is < 0 or > 23 || minute is < 0 or > 59) return false;

            var local = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Unspecified);
            if (!match.Groups["offset"].Success)
            {
                kickoffUtc = DateTime.SpecifyKind(local, DateTimeKind.Utc);
                return true;
            }

            var offsetHour = int.Parse(match.Groups["offset"].Value, CultureInfo.InvariantCulture);
            var offsetMinute = match.Groups["offsetMinute"].Success
                ? int.Parse(match.Groups["offsetMinute"].Value, CultureInfo.InvariantCulture)
                : 0;
            var sign = offsetHour < 0 ? -1 : 1;
            var offset = new TimeSpan(offsetHour, sign * offsetMinute, 0);
            kickoffUtc = new DateTimeOffset(local, offset).UtcDateTime;
            return true;
        }

        return DateTime.TryParse($"{dateRaw} {timeRaw}", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out kickoffUtc);
    }

    private static bool TryReadOpenFootballScore(JsonElement item, out int homeScore, out int awayScore)
    {
        homeScore = 0;
        awayScore = 0;

        if (TryReadInt(item, out homeScore, "score1", "homeScore", "team1Score")
            && TryReadInt(item, out awayScore, "score2", "awayScore", "team2Score"))
        {
            return true;
        }

        if (!TryGetProperty(item, out var score, "score")) return false;

        if (TryReadIntPair(score, out homeScore, out awayScore)) return true;
        if (score.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(score, out var fullTime, "ft", "fulltime", "fullTime", "regular")
                && TryReadIntPair(fullTime, out homeScore, out awayScore))
            {
                return true;
            }

            return TryReadInt(score, out homeScore, "team1", "home", "score1")
                && TryReadInt(score, out awayScore, "team2", "away", "score2");
        }

        if (score.ValueKind == JsonValueKind.String)
            return TryReadScoreText(score.GetString(), out homeScore, out awayScore);

        return false;
    }

    private static bool TryReadIntPair(JsonElement value, out int first, out int second)
    {
        first = 0;
        second = 0;
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() >= 2)
        {
            var items = value.EnumerateArray().Take(2).ToArray();
            return TryElementInt(items[0], out first) && TryElementInt(items[1], out second);
        }

        if (value.ValueKind == JsonValueKind.String)
            return TryReadScoreText(value.GetString(), out first, out second);

        return false;
    }

    private static bool TryReadScoreText(string? value, out int first, out int second)
    {
        first = 0;
        second = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var match = Regex.Match(value.Trim(), @"^(?<first>\d+)\s*[-:]\s*(?<second>\d+)$");
        if (!match.Success) return false;

        first = int.Parse(match.Groups["first"].Value, CultureInfo.InvariantCulture);
        second = int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryReadInt(JsonElement item, out int value, params string[] names)
    {
        value = 0;
        return TryGetProperty(item, out var property, names) && TryElementInt(property, out value);
    }

    private static bool TryElementInt(JsonElement value, out int number)
    {
        number = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out number),
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number),
            _ => false
        };
    }

    private static string? ReadString(JsonElement item, params string[] names)
    {
        if (!TryGetProperty(item, out var property, names)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim(),
            JsonValueKind.Number => property.ToString(),
            _ => null
        };
    }

    private static bool TryGetProperty(JsonElement item, out JsonElement property, params string[] names)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (item.TryGetProperty(name, out property)) return true;
                foreach (var candidate in item.EnumerateObject())
                {
                    if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        property = candidate.Value;
                        return true;
                    }
                }
            }
        }

        property = default;
        return false;
    }

    private static string NormalizeOpenFootballStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return KeoBiaMatchStatus.Scheduled;
        var value = status.Trim().ToLowerInvariant();
        if (value.Contains("cancel") || value.Contains("postpon")) return KeoBiaMatchStatus.Cancelled;
        if (value.Contains("live") || value.Contains("playing")) return KeoBiaMatchStatus.Live;
        if (value.Contains("finish") || value.Contains("full")) return KeoBiaMatchStatus.Finished;
        return KeoBiaMatchStatus.Scheduled;
    }

    private static string BuildOpenFootballExternalId(DateTime kickoffUtc, string home, string away, int rowNumber)
    {
        var value = $"ofwc2026-{kickoffUtc:yyyyMMddHHmm}-{ToSlugToken(home)}-{ToSlugToken(away)}";
        if (value.Length <= 120) return value;
        return $"ofwc2026-{kickoffUtc:yyyyMMddHHmm}-{rowNumber}";
    }

    private static string ToSlugToken(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        var previousDash = false;
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                previousDash = false;
            }
            else if (!previousDash)
            {
                sb.Append('-');
                previousDash = true;
            }
        }

        return sb.ToString().Trim('-');
    }

    private static string ToTeamCode(string team)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Argentina"] = "ARG",
            ["Australia"] = "AUS",
            ["Austria"] = "AUT",
            ["Belgium"] = "BEL",
            ["Brazil"] = "BRA",
            ["Canada"] = "CAN",
            ["Chile"] = "CHI",
            ["Colombia"] = "COL",
            ["Costa Rica"] = "CRC",
            ["Croatia"] = "CRO",
            ["Czech Republic"] = "CZE",
            ["Denmark"] = "DEN",
            ["Ecuador"] = "ECU",
            ["England"] = "ENG",
            ["France"] = "FRA",
            ["Germany"] = "GER",
            ["Ghana"] = "GHA",
            ["Iran"] = "IRN",
            ["Italy"] = "ITA",
            ["Japan"] = "JPN",
            ["Mexico"] = "MEX",
            ["Morocco"] = "MAR",
            ["Netherlands"] = "NED",
            ["New Zealand"] = "NZL",
            ["Nigeria"] = "NGA",
            ["Norway"] = "NOR",
            ["Poland"] = "POL",
            ["Portugal"] = "POR",
            ["Qatar"] = "QAT",
            ["Saudi Arabia"] = "KSA",
            ["Scotland"] = "SCO",
            ["Senegal"] = "SEN",
            ["Serbia"] = "SRB",
            ["South Africa"] = "RSA",
            ["South Korea"] = "KOR",
            ["Spain"] = "ESP",
            ["Switzerland"] = "SUI",
            ["Tunisia"] = "TUN",
            ["Ukraine"] = "UKR",
            ["United States"] = "USA",
            ["Uruguay"] = "URU",
            ["Wales"] = "WAL"
        };

        if (map.TryGetValue(team.Trim(), out var code)) return code;

        var words = Regex.Split(ToSlugToken(team).ToUpperInvariant(), "-")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        if (words.Length == 0) return "TBD";
        if (words.Length == 1) return words[0].Length <= 3 ? words[0] : words[0][..3];

        var initials = string.Concat(words.Select(x => x[0]));
        if (initials.Length >= 3) return initials[..3];
        var last = words[^1];
        return (initials + last).Length <= 3 ? initials + last : (initials + last)[..3];
    }

    private static void ApplyResult(KeoBiaMatch match, int? homeScore, int? awayScore, string status, string? resultChoice = null, int? resultHomeScore = null, int? resultAwayScore = null, DateTime? resultUpdatedAt = null)
    {
        status = NormalizeStatus(status);
        var now = resultUpdatedAt ?? DateTime.UtcNow;
        match.HomeScore = homeScore;
        match.AwayScore = awayScore;
        match.Status = status;
        match.ResultChoice = resultChoice ?? (status == KeoBiaMatchStatus.Finished && homeScore.HasValue && awayScore.HasValue
            ? ResolveChoice(homeScore.Value, awayScore.Value)
            : null);
        match.ResultHomeScore = match.ResultChoice == null ? null : resultHomeScore ?? homeScore;
        match.ResultAwayScore = match.ResultChoice == null ? null : resultAwayScore ?? awayScore;
        match.ResultUpdatedAt = now;
        match.UpdatedAt = now;

        foreach (var bet in match.Bets)
        {
            bet.IsSettled = match.ResultChoice != null;
            bet.IsCorrect = match.ResultChoice == null ? null : bet.Choice == match.ResultChoice;
            bet.SettledAt = match.ResultChoice == null ? null : now;
        }
    }

    private static string? ValidateCorrectScore(string choice, int? homeScore, int? awayScore)
    {
        if (homeScore.HasValue != awayScore.HasValue)
            return "Nếu dự đoán tỉ số, cần nhập đủ tỉ số hai đội.";
        if (homeScore is < 0 or > 20 || awayScore is < 0 or > 20)
            return "Tỉ số dự đoán chỉ nhận từ 0 đến 20.";
        if (!homeScore.HasValue || !awayScore.HasValue)
            return null;

        var scoreChoice = homeScore > awayScore
            ? KeoBiaBetChoice.Home
            : homeScore < awayScore
                ? KeoBiaBetChoice.Away
                : KeoBiaBetChoice.Draw;
        if (scoreChoice != choice)
            return choice switch
            {
                KeoBiaBetChoice.Home => "Bạn đã chọn đội nhà thắng, tỉ số dự đoán phải là đội nhà thắng.",
                KeoBiaBetChoice.Draw => "Bạn đã chọn hòa, tỉ số dự đoán phải là hòa.",
                KeoBiaBetChoice.Away => "Bạn đã chọn đội khách thắng, tỉ số dự đoán phải là đội khách thắng.",
                _ => "Tỉ số dự đoán không khớp cửa đã chọn."
            };
        return null;
    }

    private static string? NormalizeCorrectScoreOddsJson(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Odds tỉ số phải là JSON object.";
                return null;
            }

            var odds = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var key = property.Name.Trim().ToLowerInvariant().Replace(" ", "_");
                if (!Regex.IsMatch(key, @"^(\d{1,2}-\d{1,2}|other|home_other|away_other|draw_other)$", RegexOptions.CultureInvariant))
                {
                    error = $"Key odds không hợp lệ: {property.Name}. Dùng dạng 1-0, other, home_other, away_other, draw_other.";
                    return null;
                }

                decimal value;
                if (property.Value.ValueKind == JsonValueKind.Number)
                    property.Value.TryGetDecimal(out value);
                else if (property.Value.ValueKind == JsonValueKind.String)
                    decimal.TryParse(property.Value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
                else
                    value = 0;

                if (value < 1 || value > 999)
                {
                    error = $"Odds của {property.Name} phải từ 1 đến 999.";
                    return null;
                }

                odds[key] = ClampCorrectScoreOdds(value);
            }

            EnsureOtherOdds(odds);
            return odds.Count == 0 ? null : JsonSerializer.Serialize(odds);
        }
        catch (JsonException ex)
        {
            error = $"JSON odds không hợp lệ: {ex.Message}";
            return null;
        }
    }

    private static void EnsureOtherOdds(Dictionary<string, decimal> odds)
    {
        if (odds.Count == 0) return;
        if (!odds.ContainsKey("other"))
        {
            var exactOdds = odds
                .Where(x => Regex.IsMatch(x.Key, @"^\d{1,2}-\d{1,2}$", RegexOptions.CultureInvariant))
                .Select(x => x.Value)
                .ToArray();
            if (exactOdds.Length > 0)
                odds["other"] = ClampCorrectScoreOdds(Math.Max(21, exactOdds.Max() * 1.5m));
        }

        ExpandCommonScoreOdds(odds);
    }

    private static void ExpandCommonScoreOdds(Dictionary<string, decimal> odds)
    {
        if (odds.Count(x => Regex.IsMatch(x.Key, @"^\d{1,2}-\d{1,2}$", RegexOptions.CultureInvariant)) >= 10)
            return;

        foreach (var score in CommonCorrectScores)
        {
            if (odds.ContainsKey(score)) continue;
            var parts = score.Split('-');
            var home = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var away = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var bucket = home == away ? "draw_other" : home > away ? "home_other" : "away_other";
            if (odds.TryGetValue(bucket, out var bucketOdds) || odds.TryGetValue("other", out bucketOdds))
                odds[score] = ClampCorrectScoreOdds(bucketOdds);

            if (odds.Count(x => Regex.IsMatch(x.Key, @"^\d{1,2}-\d{1,2}$", RegexOptions.CultureInvariant)) >= 10)
                return;
        }
    }

    private static readonly string[] CommonCorrectScores =
    [
        "0-0", "1-0", "0-1", "1-1", "2-0", "0-2", "2-1", "1-2", "2-2", "3-0", "0-3", "3-1", "1-3", "3-2", "2-3", "3-3", "4-0", "0-4", "4-1", "1-4", "4-2", "2-4"
    ];

    private static decimal? ResolveCorrectScoreOdds(KeoBiaMatch match, int? homeScore, int? awayScore)
    {
        if (!homeScore.HasValue && !awayScore.HasValue)
            return null;
        if (!homeScore.HasValue || !awayScore.HasValue || string.IsNullOrWhiteSpace(match.CorrectScoreOddsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(match.CorrectScoreOddsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            var key = $"{homeScore.Value}-{awayScore.Value}";
            if (doc.RootElement.TryGetProperty(key, out var value) && value.TryGetDecimal(out var odds))
                return ClampCorrectScoreOdds(odds);
            var otherKey = homeScore.Value == awayScore.Value
                ? "draw_other"
                : homeScore.Value > awayScore.Value
                    ? "home_other"
                    : "away_other";
            if (doc.RootElement.TryGetProperty(otherKey, out value) && value.TryGetDecimal(out odds))
                return ClampCorrectScoreOdds(odds);
            if (doc.RootElement.TryGetProperty("other", out value) && value.TryGetDecimal(out odds))
                return ClampCorrectScoreOdds(odds);
        }
        catch
        {
        }

        return null;
    }

    private static decimal ClampCorrectScoreOdds(decimal odds)
    {
        var scaled = odds <= MaxCorrectScoreOdds ? odds : odds / MaxCorrectScoreOdds;
        return Math.Round(Math.Clamp(scaled, 1m, MaxCorrectScoreOdds), 2);
    }

    private static int ResolveCorrectScoreRewardCups(decimal odds) => Math.Max(1, (int)Math.Floor(ClampCorrectScoreOdds(odds)));

    private static void NormalizePaging(ref int page, ref int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;
    }

    private static string NormalizeStatus(string? status)
    {
        if (KeoBiaMatchStatus.All.Contains(status, StringComparer.OrdinalIgnoreCase))
            return KeoBiaMatchStatus.All.First(x => x.Equals(status, StringComparison.OrdinalIgnoreCase));
        return KeoBiaMatchStatus.Scheduled;
    }

    private static string? NormalizeChoice(string? choice)
    {
        if (KeoBiaBetChoice.All.Contains(choice, StringComparer.OrdinalIgnoreCase))
            return KeoBiaBetChoice.All.First(x => x.Equals(choice, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    private static string ResolveChoice(int homeScore, int awayScore)
    {
        if (homeScore > awayScore) return KeoBiaBetChoice.Home;
        if (awayScore > homeScore) return KeoBiaBetChoice.Away;
        return KeoBiaBetChoice.Draw;
    }

    private struct MatchStats
    {
        public static MatchStats Empty => new();
        public int BetCount { get; set; }
        public int CupCount { get; set; }
        public int HomeCups { get; set; }
        public int DrawCups { get; set; }
        public int AwayCups { get; set; }
        public int HomeBettors { get; set; }
        public int DrawBettors { get; set; }
        public int AwayBettors { get; set; }
    }

    private sealed record OpenFootballParsedMatch(KeoBiaMatchUpsertDto Match, int? HomeScore, int? AwayScore);

    private sealed record FootballDataMatch(
        long Id,
        DateTime KickoffUtc,
        string? HomeName,
        string? HomeShortName,
        string? HomeTla,
        string? AwayName,
        string? AwayShortName,
        string? AwayTla,
        string? Stage,
        string? Group,
        bool IsFinished,
        int? HomeScore,
        int? AwayScore,
        int? ResultHomeScore,
        int? ResultAwayScore,
        int? PenaltyHomeScore,
        int? PenaltyAwayScore,
        DateTime? UpdatedAtUtc)
    {
        public bool HasConcreteTeams =>
            !string.IsNullOrWhiteSpace(HomeName) && !string.IsNullOrWhiteSpace(AwayName);

        public string Label => $"{HomeName ?? "TBD"} vs {AwayName ?? "TBD"}";
    }

    private sealed record KeoBiaBetFeedProjection(
        Guid Id,
        Guid PlayerId,
        Guid MatchId,
        string PlayerName,
        string? AvatarUrl,
        string MatchLabel,
        string Choice,
        string ChoiceLabel,
        int Cups,
        DateTime CreatedAt,
        bool IsSettled,
        bool? IsCorrect,
        string ProjectedMatchStage,
        DateTime ProjectedMatchKickoffAt) : KeoBiaBetFeedDto(
            Id, PlayerName, AvatarUrl, MatchLabel, Choice, ChoiceLabel, Cups, CreatedAt, IsSettled, IsCorrect,
            ProjectedMatchStage, ProjectedMatchKickoffAt);

    private sealed record EnsurePlayerResult(
        KeoBiaPlayer? Player,
        bool IsNewPlayer,
        bool IsClaimedPlayer,
        string? Error,
        KeoBiaPlayer? ClaimCandidate)
    {
        public static EnsurePlayerResult Success(KeoBiaPlayer player, bool isNewPlayer, bool isClaimedPlayer) =>
            new(player, isNewPlayer, isClaimedPlayer, null, null);

        public static EnsurePlayerResult Fail(string error) =>
            new(null, false, false, error, null);

        public static EnsurePlayerResult RequireClaim(KeoBiaPlayer candidate) =>
            new(null, false, false, null, candidate);
    }

    private sealed class CsvRow
    {
        private readonly IReadOnlyDictionary<string, int> _map;
        private readonly string[] _fields;

        public CsvRow(long lineNumber, IReadOnlyDictionary<string, int> map, string[] fields)
        {
            RowNumber = lineNumber;
            _map = map;
            _fields = fields;
        }

        public long RowNumber { get; }

        public string Get(params string[] names) => Get(names, string.Empty) ?? string.Empty;

        public string? Get(string name, string? defaultValue = "")
        {
            return Get([name], defaultValue);
        }

        public string? Get(string first, string second, string? defaultValue = "")
        {
            return Get([first, second], defaultValue);
        }

        public string? Get(string first, string second, string third, string? defaultValue = "")
        {
            return Get([first, second, third], defaultValue);
        }

        private string? Get(string[] names, string? defaultValue)
        {
            foreach (var name in names)
            {
                if (_map.TryGetValue(name.ToLowerInvariant(), out var index) && index < _fields.Length)
                    return string.IsNullOrWhiteSpace(_fields[index]) ? defaultValue : _fields[index].Trim();
            }
            return defaultValue;
        }

        public int GetInt(string name, int defaultValue)
        {
            var raw = Get(name, defaultValue.ToString(CultureInfo.InvariantCulture));
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;
        }

        public bool GetBool(string name)
        {
            var raw = Get(name, "false");
            return bool.TryParse(raw, out var value) ? value : raw is "1" or "yes" or "y";
        }
    }
}
