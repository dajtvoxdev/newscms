using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsCMS.Application.KeoBia;
using NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026.Hubs;

namespace NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026.Controllers;

[Area("KeoBia2026")]
public sealed class HomeController : Controller
{
    private static readonly TimeSpan VietnamTimeOffset = TimeSpan.FromHours(7);

    private const string OidcStateCachePrefix = "keobia:tg-oidc:";
    private static readonly TimeSpan OidcStateTtl = TimeSpan.FromMinutes(10);

    private readonly IKeoBiaService _keoBia;
    private readonly IKeoBiaAnalysisService _analysis;
    private readonly IKeoBiaTelegramOidcService _oidc;
    private readonly IHubContext<KeoBiaRealtimeHub> _hubContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HomeController> _logger;
    private readonly KeoBiaTelegramOptions _telegram;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly string _clientResetToken;

    public HomeController(
        IKeoBiaService keoBia,
        IKeoBiaAnalysisService analysis,
        IKeoBiaTelegramOidcService oidc,
        IHubContext<KeoBiaRealtimeHub> hubContext,
        IMemoryCache cache,
        ILogger<HomeController> logger,
        IOptions<KeoBiaTelegramOptions> telegram,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _keoBia = keoBia;
        _analysis = analysis;
        _oidc = oidc;
        _hubContext = hubContext;
        _cache = cache;
        _logger = logger;
        _telegram = telegram.Value;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _clientResetToken = configuration["KeoBia:ClientResetToken"] ?? string.Empty;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var data = await _keoBia.GetPublicHomeAsync(3, ct);
        var lossLeaderboard = await _keoBia.GetLossLeaderboardAsync(1000, ct);
        ViewData["TelegramBot"] = _telegram.BotUsername;
        ViewData["TelegramEnabled"] = _telegram.IsConfigured;
        ViewData["TelegramOidc"] = _oidc.IsConfigured;
        ViewData["ClientResetToken"] = _clientResetToken;

        var version = _configuration["KeoBia:Version"] ?? "v0.1";
        return View(BuildModel(data, lossLeaderboard, version));
    }

    public IActionResult Policy() => View();

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UnitTransitionNotice(
        [FromBody] UnitTransitionNoticeRequest request,
        CancellationToken ct)
    {
        var result = await _keoBia.GetUnitTransitionNoticeAsync(request.PublicKey, ct);
        if (!result.Succeeded || result.Value is null)
            return BadRequest(new { ok = false, error = result.Error });

        return Ok(new
        {
            ok = true,
            shouldShow = result.Value.ShouldShow,
            isBlocked = result.Value.IsBlocked,
            hasStopped = result.Value.HasStopped
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UnitTransitionDecision(
        [FromBody] UnitTransitionDecisionRequest request,
        CancellationToken ct)
    {
        var result = await _keoBia.RespondUnitTransitionNoticeAsync(
            request.PublicKey,
            request.StopPlaying,
            ct);
        if (!result.Succeeded || result.Value is null)
            return BadRequest(new { ok = false, error = result.Error });

        return Ok(new
        {
            ok = true,
            isBlocked = result.Value.IsBlocked,
            hasStopped = result.Value.HasStopped
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ShareBeer([FromBody] KeoBiaShareBeerDto request, CancellationToken ct)
    {
        var result = await _keoBia.ShareBeerAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(new { ok = false, error = result.Error });

        var data = result.Value!;
        var unitCode = KeoBiaUnitRules.GetCurrentUnitCode();
        var unitText = KeoBiaUnitRules.FormatCount(data.Cups, unitCode);
        var message = "Cảm ơn " + data.FromDisplayName + " đã tặng " + unitText + " cho giấc mơ World Cup của anh em. Chúc " + data.FromDisplayName + " sáng ngủ dậy thấy tài khoản tăng số, chiều đi làm gặp quý nhân, tối xem bóng đội mình đặt đều thắng. Nếu có kiếp sau, mong vẫn được làm bạn với đại gia! 😆⚽";

        await _hubContext.Clients.All.SendAsync("share-beer", new
        {
            fromPublicKey = request.FromPublicKey,
            toPublicKey = request.ToPublicKey,
            fromName = data.FromDisplayName,
            toName = data.ToDisplayName,
            cups = data.Cups,
            unitCode,
            fromNewLostCups = data.FromNewLostCups,
            toNewLostCups = data.ToNewLostCups,
            fromLostBeerCups = data.FromLostBeerCups,
            fromLostPeanutPacks = data.FromLostPeanutPacks,
            toLostBeerCups = data.ToLostBeerCups,
            toLostPeanutPacks = data.ToLostPeanutPacks,
            message
        }, ct);

        await _hubContext.Clients.All.SendAsync("activityAdded", new
        {
            id = Guid.NewGuid().ToString(),
            playerName = data.FromDisplayName,
            avatarUrl = (string?)null,
            text = data.FromDisplayName + " đã tặng " + unitText + " cho " + data.ToDisplayName,
            choice = "share",
            choiceLabel = unitText,
            badge = "sports_bar"
        }, ct);

        _ = SendShareBeerTelegramAsync(data, ct);

        return Ok(new
        {
            data.FromDisplayName,
            data.ToDisplayName,
            data.Cups,
            data.FromNewLostCups,
            data.ToNewLostCups,
            data.FromLostBeerCups,
            data.FromLostPeanutPacks,
            data.ToLostBeerCups,
            data.ToLostPeanutPacks,
            unitCode
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Profile([FromBody] ProfileRequest request, CancellationToken ct)
    {
        var result = await _keoBia.UpsertPlayerAsync(new KeoBiaPlayerUpsertDto(
            request.PublicKey,
            request.DisplayName,
            request.AvatarUrl,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            request.ClaimExisting,
            request.ClaimPlayerId), ct);

        if (!result.Succeeded)
            return BadRequest(new { ok = false, error = result.Error });

        var payload = result.Value!;
        if (payload.RequiresClaimConfirmation && payload.ClaimCandidate != null)
        {
            return Ok(new
            {
                ok = false,
                requiresClaimConfirmation = true,
                claimCandidate = new
                {
                    playerId = payload.ClaimCandidate.PlayerId,
                    publicKey = payload.ClaimCandidate.PublicKey,
                    displayName = payload.ClaimCandidate.DisplayName,
                    avatarUrl = payload.ClaimCandidate.AvatarUrl
                }
            });
        }

        if (payload.Player is null)
            return BadRequest(new { ok = false, error = "Không xác nhận được hồ sơ người chơi." });

        await NotifyPlayerJoinedAsync(payload.Player, ct);
        return Ok(new
        {
            ok = true,
            playerId = payload.Player.PlayerId,
            publicKey = payload.Player.PublicKey,
            displayName = payload.Player.DisplayName,
            avatarUrl = payload.Player.AvatarUrl,
            telegramUserId = payload.Player.TelegramUserId,
            telegramUsername = payload.Player.TelegramUsername,
            isClaimedPlayer = payload.IsClaimedPlayer
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Telegram([FromBody] TelegramRequest request, CancellationToken ct)
    {
        if (request?.Telegram is null || request.Telegram.Count == 0)
            return BadRequest(new { ok = false, error = "Thiếu dữ liệu Telegram." });

        var result = await _keoBia.LinkTelegramAsync(new KeoBiaTelegramLinkDto(
            request.PublicKey,
            request.DisplayName,
            request.AvatarUrl,
            request.Telegram,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString()), ct);

        if (!result.Succeeded || result.Value is null)
            return BadRequest(new { ok = false, error = result.Error });

        var player = result.Value;
        await NotifyPlayerJoinedAsync(player, ct);
        return Ok(new
        {
            ok = true,
            playerId = player.PlayerId,
            publicKey = player.PublicKey,
            displayName = player.DisplayName,
            avatarUrl = player.AvatarUrl,
            telegramUserId = player.TelegramUserId,
            telegramUsername = player.TelegramUsername
        });
    }

    // OIDC redirect flow �?" opened in a popup. Starts the Telegram OpenID Connect
    // authorization-code (+ PKCE) flow against oauth.telegram.org.
    [HttpGet]
    public async Task<IActionResult> TelegramLogin(string? publicKey, string? displayName, string? avatarUrl, CancellationToken ct)
    {
        if (!_oidc.IsConfigured)
            return RenderPopupResult(false, "Đăng nhập Telegram (OIDC) chưa được cấu hình.", null);
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(displayName))
            return RenderPopupResult(false, "Thiếu hồ sơ người chơi. Hãy tạo tài khoản trước.", null);

        var state = RandomToken(32);
        var nonce = RandomToken(32);
        var codeVerifier = RandomToken(32);
        var codeChallenge = Base64UrlSha256(codeVerifier);
        var redirectUri = ResolveRedirectUri();

        _cache.Set(OidcStateCachePrefix + state,
            new OidcStateEntry(publicKey.Trim(), displayName.Trim(), avatarUrl?.Trim(), nonce, codeVerifier, redirectUri),
            OidcStateTtl);

        var urlResult = await _oidc.BuildAuthorizationUrlAsync(redirectUri, state, nonce, codeChallenge, ct);
        if (!urlResult.Succeeded || string.IsNullOrWhiteSpace(urlResult.Value))
            return RenderPopupResult(false, urlResult.Error ?? "Không khởi tạo được đường dẫn đăng nhập.", null);

        return Redirect(urlResult.Value);
    }

    // OIDC redirect callback?" exchanges the code, validates the id_token, links the player.
    [HttpGet]
    public async Task<IActionResult> TelegramCallback(string? code, string? state, string? error, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return RenderPopupResult(false, $"Telegram từ chối đăng nhập ({error}).", null);
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
            return RenderPopupResult(false, "Phản hồi đăng nhập thiếu state hoặc code.", null);

        if (!_cache.TryGetValue(OidcStateCachePrefix + state, out OidcStateEntry? entry) || entry is null)
            return RenderPopupResult(false, "Phiên đăng nhập đã hết hạn, hãy thử lại.", null);
        _cache.Remove(OidcStateCachePrefix + state);

        var exchange = await _oidc.ExchangeCodeAsync(code, entry.CodeVerifier, entry.RedirectUri, entry.Nonce, ct);
        if (!exchange.Succeeded || exchange.Value is null)
            return RenderPopupResult(false, exchange.Error ?? "Xác thực Telegram thất bại.", null);

        var identity = exchange.Value;
        var link = await _keoBia.LinkTelegramViaOidcAsync(new KeoBiaTelegramOidcLinkDto(
            entry.PublicKey,
            entry.DisplayName,
            entry.AvatarUrl,
            identity.TelegramUserId,
            identity.Username,
            identity.FirstName,
            identity.LastName,
            identity.PhotoUrl,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString()), ct);

        if (!link.Succeeded || link.Value is null)
            return RenderPopupResult(false, link.Error ?? "Liên kết Telegram thất bại.", null);

        var player = link.Value;
        await NotifyPlayerJoinedAsync(player, ct);
        return RenderPopupResult(true, null, player);
    }

    private string ResolveRedirectUri()
    {
        if (!string.IsNullOrWhiteSpace(_telegram.RedirectUri))
            return _telegram.RedirectUri.Trim();
        return $"{Request.Scheme}://{Request.Host}/keobia/telegram/callback";
    }

    // Renders a tiny HTML page that posts the result back to the opener window and self-closes.
    private ContentResult RenderPopupResult(bool ok, string? error, KeoBiaPlayerPresenceDto? player)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            source = "keobia-telegram-oidc",
            ok,
            error,
            playerId = player?.PlayerId,
            publicKey = player?.PublicKey,
            displayName = player?.DisplayName,
            avatarUrl = player?.AvatarUrl,
            telegramUserId = player?.TelegramUserId,
            telegramUsername = player?.TelegramUsername
        });

        var origin = $"{Request.Scheme}://{Request.Host}";
        var html = $@"<!doctype html><html><head><meta charset=""utf-8""><title>Telegram</title></head>
<body style=""font-family:system-ui;background:#0b1120;color:#e2e8f0;display:flex;align-items:center;justify-content:center;height:100vh;margin:0"">
<p>Dang xử lý đăng nhập...</p>
<script>
(function(){{
  var payload = {json};
  try {{ if (window.opener) window.opener.postMessage(payload, {System.Text.Json.JsonSerializer.Serialize(origin)}); }} catch (e) {{}}
  setTimeout(function(){{ window.close(); }}, 200);
}})();
</script></body></html>";

        return Content(html, "text/html");
    }

    private static string RandomToken(int bytes) => Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64UrlSha256(string value) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(value)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record OidcStateEntry(
        string PublicKey,
        string DisplayName,
        string? AvatarUrl,
        string Nonce,
        string CodeVerifier,
        string RedirectUri);

    [HttpPost]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(6L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6L * 1024 * 1024)]
    public async Task<IActionResult> Avatar([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { ok = false, error = "Chưa chọn ảnh avatar." });

        await using var stream = file.OpenReadStream();
        var result = await _keoBia.UploadAvatarAsync(stream, file.FileName, file.ContentType, file.Length, ct);
        return result.Succeeded
            ? Ok(new { ok = true, location = result.Value })
            : BadRequest(new { ok = false, error = result.Error });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(7L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 7L * 1024 * 1024)]
    public async Task<IActionResult> ChatImage([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { ok = false, error = "Chưa chọn ảnh chat." });

        await using var stream = file.OpenReadStream();
        var result = await _keoBia.UploadChatImageAsync(stream, file.FileName, file.ContentType, file.Length, ct);
        return result.Succeeded
            ? Ok(new { ok = true, location = result.Value })
            : BadRequest(new { ok = false, error = result.Error });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Analysis([FromBody] AnalysisRequest request, CancellationToken ct)
    {
        var result = await _analysis.AnalyzeMatchAsync(request.MatchId, request.Force, ct);
        if (!result.Succeeded || result.Value is null)
            return NotFound(new { ok = false, error = result.Error ?? "Không tìm thấy trận đấu." });

        var payload = result.Value;
        return Ok(new
        {
            ok = true,
            source = payload.Source,
            title = payload.Title,
            notice = "",
            content = payload.Content,
            fromCache = payload.FromCache,
            generatedAt = payload.GeneratedAt,
            probability = payload.Probability is null
                ? null
                : new
                {
                    home = payload.Probability.Home,
                    draw = payload.Probability.Draw,
                    away = payload.Probability.Away,
                    homeLabel = payload.Probability.HomeLabel,
                    drawLabel = payload.Probability.DrawLabel,
                    awayLabel = payload.Probability.AwayLabel
                }
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Bet([FromBody] BetRequest request, CancellationToken ct)
    {
        var result = await _keoBia.SubmitBetAsync(new KeoBiaSubmitBetDto(
            request.PublicKey,
            request.DisplayName,
            request.AvatarUrl,
            request.MatchId,
            request.Choice,
            request.Cups,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            request.StarType,
            request.PredictedHomeScore,
            request.PredictedAwayScore,
            request.CorrectScoreOdds), ct);

        if (!result.Succeeded)
            return BadRequest(new { ok = false, error = result.Error });

        var payload = result.Value!;
        await NotifyPlayerJoinedAsync(payload.Player, ct);
        await NotifyActivityAsync(payload.Activity, ct);

        var player = await _keoBia.SearchPlayersAsync(null, 1, 2000, ct);
        var me = player.Items.FirstOrDefault(x => x.PublicKey == request.PublicKey);

        return Ok(new {
            ok = true,
            activity = ToActivityPayload(payload.Activity),
            hopeStars = me?.HopeStars ?? 0,
            devilStars = me?.DevilStars ?? 0
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> QuizActive([FromBody] QuizActiveRequest request, CancellationToken ct)
    {
        var items = await _keoBia.GetActiveQuestionsForPublicAsync(request?.PublicKey, ct);
        return Ok(new { ok = true, questions = items.Select(ToQuizPayload).ToList() });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> QuizVote([FromBody] QuizVoteRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { ok = false, error = "Thiếu dữ liệu bình chọn." });

        var result = await _keoBia.SubmitQuizVoteAsync(request.PublicKey, request.QuestionId, request.ChoiceKey, ct);
        if (!result.Succeeded || result.Value is null)
            return BadRequest(new { ok = false, error = result.Error });
        return Ok(new { ok = true, question = ToQuizPayload(result.Value) });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> QuizVotes([FromBody] QuizVotesRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { ok = false, error = "Thiếu dữ liệu câu hỏi." });

        var result = await _keoBia.GetQuestionVotesAsync(request.QuestionId, ct);
        if (!result.Succeeded || result.Value is null)
            return BadRequest(new { ok = false, error = result.Error });

        var v = result.Value;
        return Ok(new
        {
            ok = true,
            questionId = v.QuestionId,
            text = v.Text,
            rewardCups = v.RewardCups,
            penaltyCups = v.PenaltyCups,
            unitCode = v.UnitCode,
            status = v.Status,
            correctChoiceKey = v.CorrectChoiceKey,
            choices = v.Choices.Select(c => new { key = c.Key, label = c.Label, votes = c.VoteCount }),
            voters = v.Voters.Select(x => new { name = x.DisplayName, avatar = x.AvatarUrl, choiceKey = x.ChoiceKey })
        });
    }

    private static object ToQuizPayload(KeoBiaPublicQuestionDto q) => new
    {
        id = q.Id,
        text = q.Text,
        rewardCups = q.RewardCups,
        penaltyCups = q.PenaltyCups,
        unitCode = q.UnitCode,
        status = q.Status,
        closesAt = q.ClosesAt,
        correctChoiceKey = q.CorrectChoiceKey,
        hasVoted = q.HasVoted,
        myChoiceKey = q.MyChoiceKey,
        choices = q.Choices.Select(c => new { key = c.Key, label = c.Label, votes = c.VoteCount })
    };

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeleteBet([FromBody] DeleteBetRequest request, CancellationToken ct)
    {
        var result = await _keoBia.DeletePlayerBetAsync(request.PublicKey, request.BetId, ct);
        if (!result.Succeeded || result.Value is null)
            return BadRequest(new { ok = false, error = result.Error });

        var payload = result.Value;
        await NotifyActivitiesRemovedAsync(payload.ActivityIds, ct);

        return Ok(new
        {
            ok = true,
            deletedBetId = payload.BetId,
            matchId = payload.MatchId,
            choice = payload.Choice,
            cups = payload.Cups,
            activityIds = payload.ActivityIds,
            history = ToPlayerHistoryPayload(payload.History)
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> MyPredictions([FromBody] MyPredictionsRequest request, CancellationToken ct)
    {
        var items = await _keoBia.GetPlayerMatchPredictionsAsync(request.PublicKey, ct);

        var player = await _keoBia.SearchPlayersAsync(null, 1, 2000, ct);
        var me = player.Items.FirstOrDefault(x => x.PublicKey == request.PublicKey);

        return Ok(new
        {
            ok = true,
            hopeStars = me?.HopeStars ?? 0,
            devilStars = me?.DevilStars ?? 0,
            items = items.Select(x => new
            {
                matchId = x.MatchId,
                choice = x.Choice,
                choiceLabel = x.ChoiceLabel,
                cups = x.Cups,
                starType = x.StarType,
                predictedHomeScore = x.PredictedHomeScore,
                predictedAwayScore = x.PredictedAwayScore,
                correctScoreOdds = x.CorrectScoreOdds
            })
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PlayerHistory([FromBody] PlayerHistoryRequest request, CancellationToken ct)
    {
        var result = await _keoBia.GetPlayerHistoryAsync(request.PublicKey, 100, ct);
        if (!result.Succeeded || result.Value is null)
            return NotFound(new { ok = false, error = result.Error ?? "Không tìm thấy người chơi." });

        var history = result.Value;
        var cupLogs = await _keoBia.GetPlayerCupLogsAsync(history.PlayerId, 50, ct);
        return Ok(new
        {
            summary = new
            {
                totalBets = history.TotalBets,
                totalCups = history.TotalCups,
                wonCups = history.WonCups,
                lostCups = history.LostCups,
                pendingCups = history.PendingCups,
                correctBets = history.CorrectBets,
                wrongBets = history.WrongBets,
                paidBeerCups = history.PaidBeerCups,
                quizRewardCups = history.QuizRewardCups,
                missedMatches = history.MissedMatches,
                promoCups = history.PromoCups,
                hopeStarCupEffect = history.HopeStarCupEffect,
                devilStarCupEffect = history.DevilStarCupEffect,
                quizPenaltyCups = history.QuizPenaltyCups,
                correctScorePenaltyCups = history.CorrectScorePenaltyCups,
                correctScoreRewardCups = history.CorrectScoreRewardCups,
                outstandingBeerCups = history.OutstandingBeerCups,
                outstandingPeanutPacks = history.OutstandingPeanutPacks,
                paidLegacyBeerCups = history.PaidLegacyBeerCups,
                paidPeanutPacks = history.PaidPeanutPacks,
                totalBeerCups = history.TotalBeerCups,
                totalPeanutPacks = history.TotalPeanutPacks,
                pendingBeerCups = history.PendingBeerCups,
                pendingPeanutPacks = history.PendingPeanutPacks,
                promoBeerCups = history.PromoBeerCups,
                promoPeanutPacks = history.PromoPeanutPacks,
                hopeStarBeerCupEffect = history.HopeStarBeerCupEffect,
                hopeStarPeanutPackEffect = history.HopeStarPeanutPackEffect,
                devilStarBeerCupEffect = history.DevilStarBeerCupEffect,
                devilStarPeanutPackEffect = history.DevilStarPeanutPackEffect,
                quizRewardBeerCups = history.QuizRewardBeerCups,
                quizRewardPeanutPacks = history.QuizRewardPeanutPacks,
                quizPenaltyBeerCups = history.QuizPenaltyBeerCups,
                quizPenaltyPeanutPacks = history.QuizPenaltyPeanutPacks,
                correctScorePenaltyBeerCups = history.CorrectScorePenaltyBeerCups,
                correctScorePenaltyPeanutPacks = history.CorrectScorePenaltyPeanutPacks,
                correctScoreRewardBeerCups = history.CorrectScoreRewardBeerCups,
                correctScoreRewardPeanutPacks = history.CorrectScoreRewardPeanutPacks
            },
            items = history.Items.Select(x => new
            {
                id = x.Id,
                matchId = x.MatchId,
                matchLabel = x.MatchLabel,
                choice = x.Choice,
                choiceLabel = x.ChoiceLabel,
                cups = x.Cups,
                createdAt = x.CreatedAt,
                matchStatus = x.MatchStatus,
                homeScore = x.HomeScore,
                awayScore = x.AwayScore,
                resultChoice = x.ResultChoice,
                resultLabel = x.ResultLabel,
                isSettled = x.IsSettled,
                isCorrect = x.IsCorrect,
                canDelete = x.CanDelete,
                deleteLockedReason = x.DeleteLockedReason,
                isMissed = x.IsMissed,
                starType = x.StarType,
                predictedHomeScore = x.PredictedHomeScore,
                predictedAwayScore = x.PredictedAwayScore,
                correctScoreOdds = x.CorrectScoreOdds,
                unitCode = x.UnitCode
            }),
            cupLogs = cupLogs.Select(x => new
            {
                id = x.Id,
                changeType = x.ChangeType,
                cups = x.Cups,
                reason = x.Reason,
                balanceAfter = x.BalanceAfter,
                balanceAfterBeerCups = x.BalanceAfterBeerCups,
                balanceAfterPeanutPacks = x.BalanceAfterPeanutPacks,
                createdAt = x.CreatedAt,
                matchLabel = x.MatchLabel,
                unitCode = x.UnitCode
            })
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> MatchHistory([FromBody] MatchHistoryRequest request, CancellationToken ct)
    {
        var result = await _keoBia.GetMatchPredictionHistoryAsync(request.MatchId, request.Choice, 2000, ct);
        if (!result.Succeeded || result.Value is null)
            return NotFound(new { ok = false, error = result.Error ?? "Không tìm thấy trận đấu." });

        var history = result.Value;
        return Ok(new
        {
            ok = true,
            matchId = history.MatchId,
            matchLabel = history.MatchLabel,
            totalEntries = history.TotalEntries,
            totalCups = history.TotalCups,
            unitCode = history.UnitCode,
            items = history.Items.Select(x => new
            {
                id = x.Id,
                playerName = x.PlayerName,
                avatarUrl = x.AvatarUrl,
                choice = x.Choice,
                choiceLabel = x.ChoiceLabel,
                cups = x.Cups,
                createdAt = ToUtcIso(x.CreatedAt),
                starType = x.StarType,
                predictedHomeScore = x.PredictedHomeScore,
                predictedAwayScore = x.PredictedAwayScore,
                correctScoreOdds = x.CorrectScoreOdds,
                unitCode = x.UnitCode
            })
        });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken ct)
    {
        var result = await _keoBia.SendChatMessageAsync(new KeoBiaChatMessageCreateDto(
            request.PublicKey,
            request.DisplayName,
            request.AvatarUrl,
            request.Message,
            request.ImageUrl,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString()), ct);

        if (!result.Succeeded)
            return BadRequest(new { ok = false, error = result.Error });

        await NotifyChatMessageAsync(result.Value!, ct);
        return Ok(new
        {
            ok = true,
            message = ToChatPayload(result.Value!)
        });
    }

    private async Task NotifyPlayerJoinedAsync(KeoBiaPlayerPresenceDto player, CancellationToken ct)
    {
        if (!player.IsNewPlayer)
            return;

        try
        {
            await _hubContext.Clients.All.SendAsync(KeoBiaRealtimeHub.PlayerJoinedMethod, new
            {
                playerId = player.PlayerId,
                publicKey = player.PublicKey,
                displayName = player.DisplayName,
                avatarUrl = player.AvatarUrl
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast join toast for player {PlayerId}", player.PlayerId);
        }
    }

    private async Task NotifyChatMessageAsync(KeoBiaChatMessageDto message, CancellationToken ct)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(KeoBiaRealtimeHub.ChatMessageMethod, ToChatPayload(message), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast chat message {MessageId}", message.Id);
        }
    }

    private async Task NotifyActivityAsync(KeoBiaActivityFeedDto activity, CancellationToken ct)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(KeoBiaRealtimeHub.ActivityAddedMethod, ToActivityPayload(activity), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast KeoBia activity {ActivityId}", activity.Id);
        }
    }

    private async Task NotifyActivitiesRemovedAsync(IReadOnlyList<Guid> activityIds, CancellationToken ct)
    {
        if (activityIds.Count == 0)
            return;

        try
        {
            await _hubContext.Clients.All.SendAsync(KeoBiaRealtimeHub.ActivityRemovedMethod, new
            {
                ids = activityIds.Select(x => x.ToString()).ToArray()
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast removed KeoBia activities");
        }
    }

    private static object ToPlayerHistoryPayload(KeoBiaPlayerHistoryDto history) => new
    {
        ok = true,
        player = new
        {
            playerId = history.PlayerId,
            publicKey = history.PublicKey,
            displayName = history.DisplayName,
            avatarUrl = history.AvatarUrl
        },
        summary = new
        {
            totalBets = history.TotalBets,
            totalCups = history.TotalCups,
            wonCups = history.WonCups,
            lostCups = history.LostCups,
            pendingCups = history.PendingCups,
            correctBets = history.CorrectBets,
            wrongBets = history.WrongBets,
            paidBeerCups = history.PaidBeerCups,
            quizRewardCups = history.QuizRewardCups,
            missedMatches = history.MissedMatches,
            promoCups = history.PromoCups,
            hopeStarCupEffect = history.HopeStarCupEffect,
            devilStarCupEffect = history.DevilStarCupEffect,
            quizPenaltyCups = history.QuizPenaltyCups,
            correctScorePenaltyCups = history.CorrectScorePenaltyCups,
            correctScoreRewardCups = history.CorrectScoreRewardCups,
            outstandingBeerCups = history.OutstandingBeerCups,
            outstandingPeanutPacks = history.OutstandingPeanutPacks,
            paidLegacyBeerCups = history.PaidLegacyBeerCups,
            paidPeanutPacks = history.PaidPeanutPacks,
            totalBeerCups = history.TotalBeerCups,
            totalPeanutPacks = history.TotalPeanutPacks,
            pendingBeerCups = history.PendingBeerCups,
            pendingPeanutPacks = history.PendingPeanutPacks,
            promoBeerCups = history.PromoBeerCups,
            promoPeanutPacks = history.PromoPeanutPacks,
            hopeStarBeerCupEffect = history.HopeStarBeerCupEffect,
            hopeStarPeanutPackEffect = history.HopeStarPeanutPackEffect,
            devilStarBeerCupEffect = history.DevilStarBeerCupEffect,
            devilStarPeanutPackEffect = history.DevilStarPeanutPackEffect,
            quizRewardBeerCups = history.QuizRewardBeerCups,
            quizRewardPeanutPacks = history.QuizRewardPeanutPacks,
            quizPenaltyBeerCups = history.QuizPenaltyBeerCups,
            quizPenaltyPeanutPacks = history.QuizPenaltyPeanutPacks,
            correctScorePenaltyBeerCups = history.CorrectScorePenaltyBeerCups,
            correctScorePenaltyPeanutPacks = history.CorrectScorePenaltyPeanutPacks,
            correctScoreRewardBeerCups = history.CorrectScoreRewardBeerCups,
            correctScoreRewardPeanutPacks = history.CorrectScoreRewardPeanutPacks
        },
        items = history.Items.Select(x => new
        {
            id = x.Id,
            matchId = x.MatchId,
            matchLabel = x.MatchLabel,
            choice = x.Choice,
            choiceLabel = x.ChoiceLabel,
            cups = x.Cups,
            createdAt = ToUtcIso(x.CreatedAt),
            matchStatus = x.MatchStatus,
            homeScore = x.HomeScore,
            awayScore = x.AwayScore,
            resultChoice = x.ResultChoice,
            resultLabel = x.ResultLabel,
            isSettled = x.IsSettled,
            isCorrect = x.IsCorrect,
            canDelete = x.CanDelete,
            deleteLockedReason = x.DeleteLockedReason,
            predictedHomeScore = x.PredictedHomeScore,
            predictedAwayScore = x.PredictedAwayScore,
            correctScoreOdds = x.CorrectScoreOdds,
            unitCode = x.UnitCode
        })
    };

    private static string ToUtcIso(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return utc.ToString("O");
    }

    private static object ToActivityPayload(KeoBiaActivityFeedDto activity) => new
    {
        id = activity.Id,
        activityType = activity.ActivityType,
        playerName = activity.PlayerName,
        avatarUrl = activity.AvatarUrl,
        text = activity.Text,
        badge = activity.Badge,
        choice = activity.Choice,
        choiceLabel = activity.ChoiceLabel,
        cups = activity.Cups,
        createdAt = activity.CreatedAt
    };

    private static object ToChatPayload(KeoBiaChatMessageDto message) => new
    {
        id = message.Id,
        playerId = message.PlayerId,
        publicKey = message.PublicKey,
        playerName = message.PlayerName,
        avatarUrl = message.AvatarUrl,
        message = message.Message,
        imageUrl = message.ImageUrl,
        createdAt = message.CreatedAt
    };

    private static HomeViewModel BuildModel(
        KeoBiaPublicHomeDto data,
        IReadOnlyList<KeoBiaPlayerLossLeaderboardDto> lossLeaderboard,
        string version)
    {
        var featured = data.FeaturedMatches.Select(MapMatch).ToList();
        var schedule = data.ScheduleMatches.Select(MapMatch).ToList();

        return new HomeViewModel(
            [
                new(data.Leaderboard.Count.ToString(), "Bia thủ đã vào bàn", "groups"),
                new(data.RecentFeed.Count.ToString(), "Hoạt động mới", "sports_soccer"),
                new("1:1", "Avatar crop chuẩn", "verified"),
                new(schedule.Count.ToString(), "Trận đấu", "bolt")
            ],
            [
                new("Không tiền thật", "Bia cũ · lạc từ tứ kết", "amber"),
                new("Realtime", "Ghi đè vào DB", "sky"),
                new("Admin", "Import lịch & cập nhật kết quả", "mint")
            ],
            featured,
            schedule,
            data.RecentFeed.Select(x => new FeedItemViewModel(
                x.Id.ToString(),
                string.IsNullOrWhiteSpace(x.AvatarUrl) ? "mascot-cheers.svg" : x.AvatarUrl!,
                x.Text,
                ToRelativeTime(x.CreatedAt),
                x.Choice,
                x.ChoiceLabel,
                x.Badge, x.CreatedAt.ToString("o"))).ToList(),
            data.CommunityChat.Select(x => new CommunityChatMessageViewModel(
                x.Id.ToString(),
                x.PlayerId.ToString(),
                x.PublicKey,
                x.PlayerName,
                string.IsNullOrWhiteSpace(x.AvatarUrl) ? "mascot-cheers.svg" : x.AvatarUrl!,
                x.Message,
                x.ImageUrl,
                ToChatTimeLabel(x.CreatedAt),
                x.CreatedAt.ToString("O"))).ToList(),
            lossLeaderboard.Select((x, index) => new LossLeaderboardEntryViewModel(
                index + 1,
                x.Id.ToString(),
                x.PublicKey,
                x.DisplayName,
                string.IsNullOrWhiteSpace(x.AvatarUrl) ? "mascot-cheers.svg" : x.AvatarUrl!,
                x.TelegramUsername,
                x.HistoricalLostCups,
                x.WrongBets,
                x.TotalBets,
                x.TotalCups,
                x.MissedMatches,
                x.SharedCups,
                x.ReceivedCups,
                x.StarItemsUsed,
                x.PromoCups,
                x.StarCupEffect,
                x.QuizRewardCups,
                x.PaidBeerCups,
                x.HistoricalLostBeerCups,
                x.HistoricalLostPeanutPacks,
                x.PaidLegacyBeerCups,
                x.PaidPeanutPacks,
                x.SharedBeerCups,
                x.SharedPeanutPacks,
                x.ReceivedBeerCups,
                x.ReceivedPeanutPacks,
                x.PromoBeerCups,
                x.PromoPeanutPacks,
                x.StarBeerCupEffect,
                x.StarPeanutPackEffect,
                x.QuizRewardBeerCups,
                x.QuizRewardPeanutPacks,
                x.QuizPenaltyBeerCups,
                x.QuizPenaltyPeanutPacks,
                x.CorrectScoreRewardBeerCups,
                x.CorrectScoreRewardPeanutPacks,
                x.CorrectScorePenaltyBeerCups,
                x.CorrectScorePenaltyPeanutPacks,
                x.StoppedPlayingAt)).ToList(),
            data.Leaderboard.Select((x, index) => new LeaderboardEntryViewModel(
                index + 1,
                x.DisplayName,
                string.IsNullOrWhiteSpace(x.AvatarUrl) ? "mascot-cheers.svg" : x.AvatarUrl!,
                x.TotalBets == 0 ? 0 : (int)Math.Round(x.CorrectBets * 100m / Math.Max(1, x.CorrectBets + x.WrongBets)),
                x.CorrectBets,
                x.TotalCups)).ToList(),
            version);
    }

    private static MatchCardViewModel MapMatch(KeoBiaPublicMatchDto x) =>
        new(
            x.Id.ToString(),
            x.Stage,
            x.HomeName,
            x.HomeCode,
            x.HomePrimary,
            x.HomeSecondary,
            x.AwayName,
            x.AwayCode,
            x.AwayPrimary,
            x.AwaySecondary,
            FormatVietnamKickoff(x.KickoffAt),
            x.Venue,
            x.IsHot,
            x.HotLabel,
            x.CommunityHome,
            x.CommunityDraw,
            x.CommunityAway,
            x.AiHome,
            x.AiDraw,
            x.AiAway,
            x.AiSummary,
            x.DefaultCups,
            DateTime.SpecifyKind(x.KickoffAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(x.KickoffAt, DateTimeKind.Utc).ToString("O"),
            x.Status,
            x.HomeBettors,
            x.DrawBettors,
            x.AwayBettors,
                x.HomeScore,
                x.AwayScore,
                x.ResultHomeScore,
                x.ResultAwayScore,
                x.PenaltyHomeScore,
                x.PenaltyAwayScore,
                x.ResultChoice,
            x.AllowsDraw,
            x.CorrectScoreOddsJson);

    private static string FormatVietnamKickoff(DateTime value) =>
        ToVietnamTime(value).ToString("HH:mm - dd/MM");

    private static DateTime ToVietnamTime(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.Add(VietnamTimeOffset);
    }

    private static string ToRelativeTime(DateTime value)
    {
        var diff = DateTime.UtcNow - value;
        if (diff.TotalMinutes < 1) return "Vừa xong";
        if (diff.TotalHours < 1) return $"{Math.Max(1, (int)diff.TotalMinutes)} phút trước";
        if (diff.TotalDays < 1) return $"{Math.Max(1, (int)diff.TotalHours)} giờ trước";
        return $"{Math.Max(1, (int)diff.TotalDays)} ngày trước";
    }

    private static string ToChatTimeLabel(DateTime value)
    {
        var local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
        return local.Date == DateTime.Now.Date
            ? local.ToString("HH:mm")
            : local.ToString("dd/MM HH:mm");
    }

    public sealed record ProfileRequest(string PublicKey, string DisplayName, string? AvatarUrl, bool ClaimExisting, Guid? ClaimPlayerId);
    public sealed record UnitTransitionNoticeRequest(string PublicKey);
    public sealed record UnitTransitionDecisionRequest(string PublicKey, bool StopPlaying);
    public sealed record TelegramRequest(string PublicKey, string DisplayName, string? AvatarUrl, Dictionary<string, string> Telegram);
    public sealed record BetRequest(string PublicKey, string DisplayName, string? AvatarUrl, Guid MatchId, string Choice, int Cups, string? StarType, int? PredictedHomeScore, int? PredictedAwayScore, decimal? CorrectScoreOdds);
    public sealed record DeleteBetRequest(string PublicKey, Guid BetId);
    public sealed record QuizActiveRequest(string? PublicKey);
    public sealed record QuizVoteRequest(string PublicKey, Guid QuestionId, string ChoiceKey);
    public sealed record QuizVotesRequest(Guid QuestionId);
    private async Task SendShareBeerTelegramAsync(KeoBiaShareBeerResultDto data, CancellationToken ct)
    {
        try
        {
            var chatId = _configuration["KeoBia:Telegram:ShareBeer:GroupChatId"];
            if (string.IsNullOrWhiteSpace(chatId)) return;
            var botToken = _configuration["KeoBia:Telegram:BotToken"];
            if (string.IsNullOrWhiteSpace(botToken)) return;
            var unitCode = KeoBiaUnitRules.GetCurrentUnitCode();
            var unitText = KeoBiaUnitRules.FormatCount(data.Cups, unitCode);
            var title = unitCode == KeoBiaUnitCode.Peanut ? "TẶNG GÓI LẠC" : "TẶNG BIA";
            var text = KeoBiaUnitRules.Emoji(unitCode) + " *" + title + "!*\n\nCảm ơn *" + EscapeMarkdown(data.FromDisplayName) + "* đã tài trợ " + unitText + " cho *" + EscapeMarkdown(data.ToDisplayName) + "*!\n\nChúc " + EscapeMarkdown(data.FromDisplayName) + " sáng ngủ dậy thấy tài khoản tăng số, chiều đi làm gặp quý nhân, tối xem bóng đội mình đặt đều thắng. Nếu có kiếp sau, mong vẫn được làm bạn với đại gia! 😆⚽";
            var url = "https://api.telegram.org/bot" + botToken + "/sendMessage";
            using var client = _httpClientFactory.CreateClient();
            var payload = new { chat_id = chatId, text, parse_mode = "Markdown" };
            await client.PostAsJsonAsync(url, payload, ct);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to send share-beer Telegram notification."); }
    }

    private static string EscapeMarkdown(string text) =>
        text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[").Replace("`", "\\`");

    public sealed record MyPredictionsRequest(string PublicKey);
    public sealed record PlayerHistoryRequest(string PublicKey);
    public sealed record MatchHistoryRequest(Guid MatchId, string? Choice);
    public sealed record ChatRequest(string PublicKey, string DisplayName, string? AvatarUrl, string? Message, string? ImageUrl);
    public sealed record AnalysisRequest(Guid MatchId, bool Force = false);
}
