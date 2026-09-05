using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Ai;
using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.KeoBia;

public sealed class KeoBiaAiTools
{
    private sealed record QuizChoice(string Key, string Label);

    private static readonly JsonSerializerOptions QuizJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] OutstandingAttributedChangeTypes =
    [
        KeoBiaCupChangeType.WrongBet,
        KeoBiaCupChangeType.MissedMatch,
        KeoBiaCupChangeType.SharedBeer,
        KeoBiaCupChangeType.QuizWrong,
        KeoBiaCupChangeType.HopeStarWin,
        KeoBiaCupChangeType.HopeStarLose,
        KeoBiaCupChangeType.DevilStarWin,
        KeoBiaCupChangeType.DevilStarLose,
        KeoBiaCupChangeType.CorrectScore
    ];

    private readonly AppDbContext _db;

    public KeoBiaAiTools(AppDbContext db) => _db = db;

    public IReadOnlyList<AiToolDefinition> GetTools() => new[]
    {
        SearchMatchesTool(),
        SearchPlayersTool(),
        GetLeaderboardTool(),
        GetPlayerHistoryTool(),
        GetMatchPredictionsTool(),
        GetMatchDetailTool(),
        GetQuizResultsTool(),
        GetStatsTool()
    };

    private AiToolDefinition SearchMatchesTool() => new(
        "search_matches",
        "Tìm trận đấu theo tên đội, trạng thái (Scheduled/Live/Finished), hoặc ngày. Trả về danh sách trận với tỷ số, thời gian.",
        """{"type":"object","properties":{"status":{"type":"string","enum":["Scheduled","Live","Finished","All"],"description":"Trạng thái trận"},"team":{"type":"string","description":"Tên đội bóng"},"limit":{"type":"integer","default":10}},"required":[]}""",
        (args, ct) => ExecuteSearchMatches(args, ct));

    private AiToolDefinition SearchPlayersTool() => new(
        "search_players",
        "Tìm người chơi theo tên. Trả về thống kê tách riêng cốc bia và gói lạc: đã dự đoán, đang thiếu, đã tặng, đúng và sai.",
        """{"type":"object","properties":{"name":{"type":"string","description":"Tên người chơi"}},"required":["name"]}""",
        (args, ct) => ExecuteSearchPlayers(args, ct));

    private AiToolDefinition GetLeaderboardTool() => new(
        "get_leaderboard",
        "Lấy bảng xếp hạng theo phần đang thiếu, tách riêng cốc bia cũ và gói lạc từ tứ kết.",
        """{"type":"object","properties":{"limit":{"type":"integer","default":10}}}""",
        (args, ct) => ExecuteGetLeaderboard(args, ct));

    private AiToolDefinition GetPlayerHistoryTool() => new(
        "get_player_history",
        "Lấy lịch sử dự đoán của 1 người chơi: các kèo đã đặt, kết quả, đúng/sai.",
        """{"type":"object","properties":{"name":{"type":"string","description":"Tên người chơi"}},"required":["name"]}""",
        (args, ct) => ExecuteGetPlayerHistory(args, ct));

    private AiToolDefinition GetMatchPredictionsTool() => new(
        "get_match_predictions",
        "Lấy tất cả dự đoán cho 1 trận đấu: ai đã chọn cửa nào, bao nhiêu cốc bia hoặc gói lạc, có dự đoán tỉ số 90 phút nào không.",
        """{"type":"object","properties":{"matchId":{"type":"string","description":"ID trận đấu"},"team":{"type":"string","description":"Tên đội (tự tìm matchId)"}},"required":[]}""",
        (args, ct) => ExecuteGetMatchPredictions(args, ct));

    private AiToolDefinition GetMatchDetailTool() => new(
        "get_match_detail",
        "Lấy chi tiết 1 trận: tỷ số, kết quả, AI analysis, số người dự đoán mỗi cửa.",
        """{"type":"object","properties":{"matchId":{"type":"string","description":"ID trận đấu"},"team":{"type":"string","description":"Tên đội (tự tìm matchId)"}},"required":[]}""",
        (args, ct) => ExecuteGetMatchDetail(args, ct));

    private AiToolDefinition GetStatsTool() => new(
        "get_stats",
        "Lấy thống kê tổng quan: tổng người chơi, tổng kèo, tổng cốc bia, tổng gói lạc, trận đã kết thúc và trận sắp diễn ra.",
        """{"type":"object","properties":{}}""",
        (args, ct) => ExecuteGetStats(args, ct));

    private AiToolDefinition GetQuizResultsTool() => new(
        "get_quiz_results",
        "Lấy kết quả bình chọn câu hỏi nhanh: câu hỏi, đáp án, số vote, ai chọn gì.",
        """{"type":"object","properties":{"query":{"type":"string","description":"Một phần nội dung câu hỏi, bỏ trống để lấy câu mới nhất"},"limit":{"type":"integer","default":3}}}""",
        (args, ct) => ExecuteGetQuizResults(args, ct));

    // --- Implementations ---

    private async Task<string> ExecuteSearchMatches(JsonElement args, CancellationToken ct)
    {
        var status = args.TryGetProperty("status", out var s) ? s.GetString() : "All";
        var team = args.TryGetProperty("team", out var t) ? t.GetString() : null;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 10;
        var now = DateTime.UtcNow;

        var query = _db.KeoBiaMatches.AsNoTracking().Where(x => x.Status != KeoBiaMatchStatus.Cancelled);

        if (status == "Scheduled") query = query.Where(x => x.Status == KeoBiaMatchStatus.Scheduled);
        else if (status == "Finished") query = query.Where(x => x.Status == KeoBiaMatchStatus.Finished);
        else if (status == "Live") query = query.Where(x => x.Status == KeoBiaMatchStatus.Live);

        var candidates = await query.OrderByDescending(x => x.KickoffAt)
            .Select(x => new { x.Id, x.HomeName, x.HomeCode, x.AwayName, x.AwayCode, x.HomeScore, x.AwayScore, x.Status, x.KickoffAt, x.ResultChoice, x.Stage })
            .ToListAsync(ct);

        var matches = string.IsNullOrWhiteSpace(team)
            ? candidates.Take(limit).ToList()
            : candidates
                .Select(x => new { Match = x, Score = ScoreMatchMention(team, x.HomeName, x.HomeCode, x.AwayName, x.AwayCode) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Match.KickoffAt)
                .Take(limit)
                .Select(x => x.Match)
                .ToList();

        if (matches.Count == 0) return "Không tìm thấy trận nào.";

        var sb = new StringBuilder();
        sb.AppendLine($"Thời gian hiện tại: {VnTime(now)}");
        foreach (var m in matches)
        {
            var score = m.HomeScore.HasValue ? $"{m.HomeScore}-{m.AwayScore}" : "Chưa đá";
            var result = m.ResultChoice == "home" ? m.HomeName : m.ResultChoice == "away" ? m.AwayName : m.ResultChoice == "draw" ? "Hòa" : "";
            var isLive = m.Status == "Live" || (m.Status == "Scheduled" && now >= m.KickoffAt && now < m.KickoffAt.AddHours(2));
            var statusTag = isLive ? "🔴 ĐANG ĐÁ" : m.Status == "Finished" ? "✅ Đã kết thúc" : "📅 Sắp diễn ra";
            sb.AppendLine($"[{statusTag}] {m.HomeName} vs {m.AwayName}: {score} | {VnTime(m.KickoffAt)} | {m.Stage}{(result != "" ? $" | Thắng: {result}" : "")}");
        }
        return sb.ToString();
    }

    private async Task<string> ExecuteSearchPlayers(JsonElement args, CancellationToken ct)
    {
        var name = args.GetProperty("name").GetString() ?? "";
        var normalizedName = RemoveDiacritics(name);
        var players = await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.TelegramUserId != null && (
                x.DisplayName.Contains(name) ||
                x.DisplayName.Contains(normalizedName) ||
                (x.TelegramFirstName != null && x.TelegramFirstName.Contains(name)) ||
                (x.TelegramUsername != null && x.TelegramUsername.Contains(name))))
            .Select(x => new PlayerUnitSeed(
                x.Id, x.DisplayName, x.TotalBets, x.TotalCups, x.CorrectBets, x.WrongBets,
                x.PenaltyCups, x.SharedCups, x.TelegramUsername))
            .Take(5).ToListAsync(ct);

        if (players.Count == 0) return $"Không tìm thấy người chơi '{name}'.";

        var unitStats = await GetPlayerUnitStatsAsync(players, ct);
        var sb = new StringBuilder();
        foreach (var p in players)
        {
            var stats = unitStats[p.Id];
            var handle = !string.IsNullOrWhiteSpace(p.TelegramUsername) ? $" (@{p.TelegramUsername})" : "";
            sb.AppendLine($"{p.DisplayName}{handle}: {p.TotalBets} kèo, đã dự đoán {stats.TotalText}, đang thiếu {stats.OutstandingText}, đã tặng {stats.SharedText}, {p.CorrectBets} đúng, {p.WrongBets} sai.");
        }
        return sb.ToString();
    }

    // KickoffAt/now lưu ở UTC. VN cố định +7, không DST → AddHours(7) chuẩn & portable (không phụ thuộc tz OS).
    private static string VnTime(DateTime utc) => $"{utc.AddHours(7):HH:mm dd/MM} (giờ VN)";

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private async Task<string> ExecuteGetLeaderboard(JsonElement args, CancellationToken ct)
    {
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 10;
        var players = await _db.KeoBiaPlayers.AsNoTracking()
            .Where(x => x.TelegramUserId != null)
            .Select(x => new PlayerUnitSeed(
                x.Id, x.DisplayName, x.TotalBets, x.TotalCups, x.CorrectBets, x.WrongBets,
                x.PenaltyCups, x.SharedCups, x.TelegramUsername))
            .ToListAsync(ct);
        var unitStats = await GetPlayerUnitStatsAsync(players, ct);
        var board = players
            .OrderByDescending(x => unitStats[x.Id].OutstandingTotal)
            .ThenByDescending(x => x.WrongBets)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();

        var sb = new StringBuilder("BXH cốc bia và gói lạc đang thiếu:\n");
        int rank = 1;
        foreach (var p in board)
        {
            var stats = unitStats[p.Id];
            sb.AppendLine($"{rank}. {p.DisplayName}: đang thiếu {stats.OutstandingText}; đã dự đoán {stats.TotalText}; đã tặng {stats.SharedText}; {p.CorrectBets} đúng, {p.WrongBets} sai");
            rank++;
        }
        return sb.ToString();
    }

    private async Task<string> ExecuteGetPlayerHistory(JsonElement args, CancellationToken ct)
    {
        var name = args.GetProperty("name").GetString() ?? "";
        var player = await _db.KeoBiaPlayers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DisplayName.Contains(name) && x.TelegramUserId != null, ct);
        if (player is null) return $"Không tìm thấy người chơi '{name}'.";

        var bets = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.PlayerId == player.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new
            {
                x.Match.HomeName,
                x.Match.AwayName,
                x.Match.Stage,
                x.Match.KickoffAt,
                x.Choice,
                x.Cups,
                x.IsSettled,
                x.IsCorrect,
                x.Match.HomeScore,
                x.Match.AwayScore
            })
            .ToListAsync(ct);

        var sb = new StringBuilder($"Lịch sử {player.DisplayName} ({bets.Count} kèo gần nhất):\n");
        foreach (var b in bets)
        {
            var score = b.HomeScore.HasValue ? $"{b.HomeScore}-{b.AwayScore}" : "?";
            var r = b.IsSettled ? (b.IsCorrect == true ? "✅ đúng" : "❌ sai") : "⏳ chưa có kết quả";
            var unitCode = KeoBiaUnitRules.GetUnitCode(b.Stage, b.KickoffAt);
            sb.AppendLine($"- {b.HomeName} vs {b.AwayName} ({score}): {b.Choice} · {KeoBiaUnitRules.FormatCount(b.Cups, unitCode)} → {r}");
        }
        return sb.ToString();
    }

    private async Task<string> ExecuteGetMatchPredictions(JsonElement args, CancellationToken ct)
    {
        Guid? matchId = args.TryGetProperty("matchId", out var m) && Guid.TryParse(m.GetString(), out var mid) ? mid : null;
        var team = args.TryGetProperty("team", out var t) ? t.GetString() : null;

        if (!matchId.HasValue && !string.IsNullOrWhiteSpace(team))
        {
            var matches = await _db.KeoBiaMatches.AsNoTracking()
                .Where(x => x.Status != KeoBiaMatchStatus.Cancelled)
                .OrderByDescending(x => x.KickoffAt)
                .Select(x => new { x.Id, x.HomeName, x.HomeCode, x.AwayName, x.AwayCode, x.KickoffAt })
                .ToListAsync(ct);

            matchId = matches
                .Select(x => new { x.Id, Score = ScoreMatchMention(team, x.HomeName, x.HomeCode, x.AwayName, x.AwayCode), x.KickoffAt })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.KickoffAt)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefault();

            if (!matchId.HasValue)
            {
                var suggestions = matches.Take(8)
                    .Select(x => $"- {x.HomeName} vs {x.AwayName} ({VnTime(x.KickoffAt)})");
                return $"KHÔNG KHỚP tên đội \"{team}\". Các trận đang có trong hệ thống:\n{string.Join("\n", suggestions)}\nHãy gọi lại tool với tên đội đúng từ danh sách trên.";
            }
        }

        if (!matchId.HasValue) return "Cần cung cấp matchId hoặc tên đội.";

        var match = await _db.KeoBiaMatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == matchId.Value, ct);
        if (match is null) return "Không tìm thấy trận theo matchId đã cho.";

        var bets = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => x.MatchId == matchId.Value)
            .Select(x => new { x.Player.DisplayName, x.Choice, x.Cups, x.PredictedHomeScore, x.PredictedAwayScore, x.CorrectScoreOdds })
            .ToListAsync(ct);

        var homeCount = bets.Count(b => b.Choice == "home");
        var drawCount = bets.Count(b => b.Choice == "draw");
        var awayCount = bets.Count(b => b.Choice == "away");
        var scorePreds = bets.Where(b => b.PredictedHomeScore.HasValue && b.PredictedAwayScore.HasValue).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"✅ TÌM THẤY TRẬN: {match.HomeName} vs {match.AwayName} (id={match.Id})");
        sb.AppendLine($"- Tổng người đặt kèo: {bets.Count} (home {homeCount}, draw {drawCount}, away {awayCount})");
        sb.AppendLine($"- Số người dự đoán tỉ số 90 phút: {scorePreds.Count}");

        if (bets.Count == 0)
        {
            sb.AppendLine("Trận CÓ trong hệ thống nhưng chưa ai đặt kèo.");
            return sb.ToString();
        }

        if (scorePreds.Count > 0)
        {
            sb.AppendLine("Chi tiết dự đoán tỉ số 90 phút:");
            foreach (var b in scorePreds)
                sb.AppendLine($"- {b.DisplayName}: {b.PredictedHomeScore}-{b.PredictedAwayScore}, cửa {b.Choice} · {KeoBiaUnitRules.FormatCount(b.Cups, KeoBiaUnitRules.GetUnitCode(match.Stage, match.KickoffAt))}");
        }
        else
        {
            sb.AppendLine("Chưa ai dự đoán tỉ số 90 phút. Danh sách cửa cược:");
            foreach (var b in bets)
                sb.AppendLine($"- {b.DisplayName}: {b.Choice} ({KeoBiaUnitRules.FormatCount(b.Cups, KeoBiaUnitRules.GetUnitCode(match.Stage, match.KickoffAt))})");
        }
        return sb.ToString();
    }

    private static int ScoreMatchMention(string question, string homeName, string homeCode, string awayName, string awayCode)
    {
        var normalizedQuestion = NormalizeSearchText(question);
        return ScoreTeamMention(normalizedQuestion, homeName, homeCode)
            + ScoreTeamMention(normalizedQuestion, awayName, awayCode);
    }

    private static int ScoreTeamMention(string normalizedQuestion, string teamName, string teamCode)
    {
        var score = 0;
        var normalizedName = NormalizeSearchText(teamName);
        var normalizedCode = NormalizeSearchText(teamCode);
        var acronym = string.Concat(normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0]));
        if (!string.IsNullOrWhiteSpace(normalizedCode) && normalizedQuestion.Contains(normalizedCode)) score += 3;
        if (!string.IsNullOrWhiteSpace(normalizedName) && normalizedQuestion.Contains(normalizedName)) score += 5;
        if (acronym.Length >= 2 && normalizedQuestion.Contains(acronym)) score += 2;
        foreach (var token in normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 3 && normalizedQuestion.Contains(token)) score += 2;
            else if (token.Length >= 4 && normalizedQuestion.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(q => token.StartsWith(q) || q.StartsWith(token))) score += 1;
        }
        return score;
    }

    private static string NormalizeSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(RemoveDiacritics(text).Length);
        foreach (var c in RemoveDiacritics(text).ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<string> ExecuteGetMatchDetail(JsonElement args, CancellationToken ct)
    {
        Guid? matchId = args.TryGetProperty("matchId", out var m) && Guid.TryParse(m.GetString(), out var mid) ? mid : null;
        var team = args.TryGetProperty("team", out var t) ? t.GetString() : null;

        if (!matchId.HasValue && !string.IsNullOrWhiteSpace(team))
        {
            var matches = await _db.KeoBiaMatches.AsNoTracking()
                .Where(x => x.Status != KeoBiaMatchStatus.Cancelled)
                .OrderByDescending(x => x.KickoffAt)
                .Select(x => new { x.Id, x.HomeName, x.HomeCode, x.AwayName, x.AwayCode, x.KickoffAt })
                .ToListAsync(ct);

            matchId = matches
                .Select(x => new { x.Id, Score = ScoreMatchMention(team, x.HomeName, x.HomeCode, x.AwayName, x.AwayCode), x.KickoffAt })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.KickoffAt)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefault();
        }

        if (!matchId.HasValue) return "Cần cung cấp matchId hoặc tên đội.";

        var match = await _db.KeoBiaMatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == matchId.Value, ct);
        if (match is null) return "Không tìm thấy trận.";

        var betCount = await _db.KeoBiaBets.AsNoTracking().CountAsync(x => x.MatchId == matchId.Value, ct);
        var homeBets = await _db.KeoBiaBets.AsNoTracking().CountAsync(x => x.MatchId == matchId.Value && x.Choice == "home", ct);
        var drawBets = await _db.KeoBiaBets.AsNoTracking().CountAsync(x => x.MatchId == matchId.Value && x.Choice == "draw", ct);
        var awayBets = await _db.KeoBiaBets.AsNoTracking().CountAsync(x => x.MatchId == matchId.Value && x.Choice == "away", ct);

        var analysis = await _db.KeoBiaMatches.AsNoTracking()
            .Where(x => x.Id == matchId.Value)
            .Select(x => x.AiSummary)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        var score = match.HomeScore.HasValue ? $"{match.HomeScore}-{match.AwayScore}" : "Chưa đá";
        var result = match.ResultChoice == "home" ? match.HomeName : match.ResultChoice == "away" ? match.AwayName : match.ResultChoice == "draw" ? "Hòa" : "";
        var isLive = match.Status == KeoBiaMatchStatus.Live || (match.Status == KeoBiaMatchStatus.Scheduled && now >= match.KickoffAt && now < match.KickoffAt.AddHours(2));
        var statusTag = isLive ? "🔴 ĐANG ĐÁ" : match.Status == KeoBiaMatchStatus.Finished ? "✅ Đã kết thúc" : "📅 Sắp diễn ra";

        var sb = new StringBuilder();
        sb.AppendLine($"Thời gian hiện tại: {VnTime(now)}");
        sb.AppendLine($"[{statusTag}] {match.HomeName} vs {match.AwayName}: {score}");
        sb.AppendLine($"Thời gian: {VnTime(match.KickoffAt)} | {match.Stage}");
        if (result != "") sb.AppendLine($"Kết quả: {result} thắng");
        sb.AppendLine($"Dự đoán: {betCount} người ({homeBets} home, {drawBets} draw, {awayBets} away)");
        if (!string.IsNullOrWhiteSpace(analysis))
            sb.AppendLine($"AI Analysis: {analysis[..Math.Min(500, analysis.Length)]}...");
        return sb.ToString();
    }

    private async Task<string> ExecuteGetQuizResults(JsonElement args, CancellationToken ct)
    {
        var query = args.TryGetProperty("query", out var q) ? q.GetString() : null;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 3;
        var questionsQuery = _db.KeoBiaQuestions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
            questionsQuery = questionsQuery.Where(x => x.Text.Contains(query));

        var questions = await questionsQuery
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.Text,
                x.ChoicesJson,
                x.Status,
                x.CorrectChoiceKey,
                x.RewardCups,
                x.PenaltyCups,
                x.RevealedAt,
                x.CreatedAt
            })
            .ToListAsync(ct);
        if (questions.Count == 0) return "Chưa có câu hỏi nhanh nào.";

        var ids = questions.Select(x => x.Id).ToList();
        var votes = await _db.KeoBiaQuestionVotes.AsNoTracking()
            .Where(x => ids.Contains(x.QuestionId))
            .Select(x => new { x.QuestionId, x.Player.DisplayName, x.ChoiceKey })
            .ToListAsync(ct);

        var sb = new StringBuilder("Kết quả bình chọn câu hỏi nhanh:\n");
        foreach (var question in questions)
        {
            var choices = JsonSerializer.Deserialize<List<QuizChoice>>(question.ChoicesJson, QuizJsonOptions) ?? new List<QuizChoice>();
            var questionVotes = votes.Where(v => v.QuestionId == question.Id).ToList();
            var unitCode = KeoBiaUnitRules.GetUnitCode(null, question.RevealedAt ?? DateTime.UtcNow);
            var penalty = question.PenaltyCups > 0
                ? $", sai mất {KeoBiaUnitRules.FormatCount(question.PenaltyCups, unitCode)}"
                : ", sai không mất";
            sb.AppendLine($"\n❓ {question.Text} ({question.Status}, đúng giảm {KeoBiaUnitRules.FormatCount(question.RewardCups, unitCode)}{penalty}, {questionVotes.Count} vote)");
            foreach (var choice in choices)
            {
                var voters = questionVotes.Where(v => string.Equals(v.ChoiceKey, choice.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.DisplayName)
                    .ToList();
                var mark = string.Equals(question.CorrectChoiceKey, choice.Key, StringComparison.OrdinalIgnoreCase) ? " ✅" : "";
                sb.AppendLine($"- {choice.Label}{mark}: {voters.Count}" + (voters.Count > 0 ? $" ({string.Join(", ", voters)})" : ""));
            }
        }
        return sb.ToString();
    }

    private async Task<string> ExecuteGetStats(JsonElement args, CancellationToken ct)
    {
        var totalPlayers = await _db.KeoBiaPlayers.AsNoTracking().CountAsync(x => x.TelegramUserId != null, ct);
        var totalBets = await _db.KeoBiaBets.AsNoTracking().CountAsync(ct);
        var betRows = await _db.KeoBiaBets.AsNoTracking()
            .Select(x => new { x.Cups, x.Match.Stage, x.Match.KickoffAt })
            .ToListAsync(ct);
        var totalUnits = SumUnits(betRows.Select(x => new UnitRow(x.Cups, x.Stage, x.KickoffAt)));
        var finished = await _db.KeoBiaMatches.AsNoTracking().CountAsync(x => x.Status == KeoBiaMatchStatus.Finished, ct);
        var upcoming = await _db.KeoBiaMatches.AsNoTracking().CountAsync(x => x.Status == KeoBiaMatchStatus.Scheduled, ct);
        var live = await _db.KeoBiaMatches.AsNoTracking().CountAsync(x => x.Status == KeoBiaMatchStatus.Live, ct);

        return $"Thống kê Bia Vui 2026:\n- {totalPlayers} người chơi\n- {totalBets} lượt dự đoán\n- {totalUnits.BeerCups} cốc bia đã dự đoán\n- {totalUnits.PeanutPacks} gói lạc đã dự đoán\n- {finished} trận đã kết thúc\n- {upcoming} trận sắp diễn ra\n- {live} trận đang đá";
    }

    private async Task<Dictionary<Guid, PlayerUnitStats>> GetPlayerUnitStatsAsync(
        IReadOnlyList<PlayerUnitSeed> players,
        CancellationToken ct)
    {
        var ids = players.Select(x => x.Id).ToArray();
        if (ids.Length == 0) return [];

        var betRows = await _db.KeoBiaBets.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId))
            .Select(x => new { x.PlayerId, x.Cups, x.Match.Stage, x.Match.KickoffAt, x.IsSettled, x.IsCorrect })
            .ToListAsync(ct);
        var logTotals = await _db.KeoBiaCupLogs.AsNoTracking()
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
        var attributedRows = await _db.KeoBiaCupLogs.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId) && OutstandingAttributedChangeTypes.Contains(x.ChangeType))
            .Select(x => new
            {
                x.PlayerId,
                x.ChangeType,
                x.Cups,
                x.CreatedAt,
                MatchStage = x.Match != null ? x.Match.Stage : null,
                MatchKickoffAt = x.Match != null ? (DateTime?)x.Match.KickoffAt : null
            })
            .ToListAsync(ct);

        var totalAttributed = ids.ToDictionary(x => x, _ => UnitTotals.Empty);
        var outstandingAttributed = ids.ToDictionary(x => x, _ => UnitTotals.Empty);
        var sharedAttributed = ids.ToDictionary(x => x, _ => UnitTotals.Empty);
        foreach (var row in betRows)
            AddUnit(totalAttributed, row.PlayerId, row.Cups, row.Stage, row.KickoffAt);
        foreach (var row in attributedRows)
        {
            var occurredAt = row.MatchKickoffAt ?? row.CreatedAt;
            AddUnit(outstandingAttributed, row.PlayerId, row.Cups, row.MatchStage, occurredAt);
            if (row.ChangeType == KeoBiaCupChangeType.SharedBeer)
                AddUnit(sharedAttributed, row.PlayerId, row.Cups, row.MatchStage, occurredAt);
        }

        var logByPlayer = logTotals.ToDictionary(x => x.PlayerId);
        return players.ToDictionary(player => player.Id, player =>
        {
            logByPlayer.TryGetValue(player.Id, out var log);
            var lostBetCups = betRows
                .Where(x => x.PlayerId == player.Id && x.IsSettled && x.IsCorrect == false)
                .Sum(x => x.Cups);
            var currentBalance = Math.Max(0,
                lostBetCups + player.PenaltyCups + player.SharedCups +
                (log?.StarCupEffect ?? 0) + (log?.QuizPenaltyCups ?? 0) -
                (log?.ReceivedCups ?? 0) - (log?.QuizRewardCups ?? 0) -
                (log?.PaidCups ?? 0) - (log?.CorrectScoreReward ?? 0));

            var total = totalAttributed[player.Id] with
            {
                BeerCups = Math.Max(0, totalAttributed[player.Id].BeerCups),
                PeanutPacks = Math.Max(0, totalAttributed[player.Id].PeanutPacks)
            };
            var outstanding = Reconcile(currentBalance, outstandingAttributed[player.Id]);
            var shared = Reconcile(player.SharedCups, sharedAttributed[player.Id]);
            return new PlayerUnitStats(total, outstanding, shared);
        });
    }

    private static UnitTotals SumUnits(IEnumerable<UnitRow> rows)
    {
        var total = UnitTotals.Empty;
        foreach (var row in rows)
        {
            total = KeoBiaUnitRules.UsesPeanut(row.Stage, row.OccurredAt)
                ? total with { PeanutPacks = total.PeanutPacks + row.Cups }
                : total with { BeerCups = total.BeerCups + row.Cups };
        }
        return total;
    }

    private static void AddUnit(
        IDictionary<Guid, UnitTotals> totals,
        Guid playerId,
        int cups,
        string? stage,
        DateTime occurredAt)
    {
        var current = totals[playerId];
        totals[playerId] = KeoBiaUnitRules.UsesPeanut(stage, occurredAt)
            ? current with { PeanutPacks = current.PeanutPacks + cups }
            : current with { BeerCups = current.BeerCups + cups };
    }

    private static UnitTotals Reconcile(int currentBalance, UnitTotals attributed)
    {
        var value = KeoBiaUnitRules.ReconcileBalance(
            currentBalance, attributed.BeerCups, attributed.PeanutPacks);
        return new UnitTotals(value.BeerCups, value.PeanutPacks);
    }

    private sealed record PlayerUnitSeed(
        Guid Id,
        string DisplayName,
        int TotalBets,
        int TotalCups,
        int CorrectBets,
        int WrongBets,
        int PenaltyCups,
        int SharedCups,
        string? TelegramUsername);

    private sealed record UnitRow(int Cups, string? Stage, DateTime OccurredAt);

    private sealed record UnitTotals(int BeerCups, int PeanutPacks)
    {
        public static UnitTotals Empty { get; } = new(0, 0);
        public int Total => BeerCups + PeanutPacks;
        public string Text => KeoBiaUnitRules.FormatBreakdown(BeerCups, PeanutPacks);
    }

    private sealed record PlayerUnitStats(UnitTotals Total, UnitTotals Outstanding, UnitTotals Shared)
    {
        public int OutstandingTotal => Outstanding.Total;
        public string TotalText => Total.Text;
        public string OutstandingText => Outstanding.Text;
        public string SharedText => Shared.Text;
    }
}
