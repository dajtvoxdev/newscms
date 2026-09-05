using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.KeoBia;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.KeoBia;

public sealed class KeoBiaAnalysisService : IKeoBiaAnalysisService
{
    private static readonly TimeSpan VietnamTimeOffset = TimeSpan.FromHours(7);
    private static readonly ConcurrentDictionary<AnalysisQueueKey, Lazy<Task<Result<KeoBiaMatchAnalysisDto>>>> InflightAnalyses = new();

    private static readonly Regex ProbabilityJsonLineRegex = new(
        @"(?im)^\s*JSON_XAC_SUAT\s*:\s*(?<json>\{.*\})\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CorrectScoreOddsJsonLineRegex = new(
        @"(?im)^\s*ODDS_TY_SO_CHINH_XAC\s*:\s*(?<json>\{.*\})\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PercentRegex = new(
        @"(?<value>\d{1,3})\s*%",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SourceSectionRegex = new(
        @"(?ims)(^|\r?\n)(?<section>Nguồn live đã kiểm tra:\s*.*?)(?=(\r?\n)\S[^:\r\n]{0,80}:\s*(\r?\n|$)|\z)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const decimal MaxCorrectScoreOdds = 3m;

    private readonly AppDbContext _db;
    private readonly IAiCompletionService _aiCompletion;
    private readonly IAiConnectionService _aiConnections;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeoBiaAnalysisService> _logger;
    private readonly IDataProtectionProvider? _dataProtection;
    private readonly IEnumerable<IAiTool>? _externalTools;

    public KeoBiaAnalysisService(
        AppDbContext db,
        IAiCompletionService aiCompletion,
        IAiConnectionService aiConnections,
        IConfiguration configuration,
        ILogger<KeoBiaAnalysisService> logger,
        IDataProtectionProvider? dataProtection = null,
        IEnumerable<IAiTool>? externalTools = null)
    {
        _db = db;
        _aiCompletion = aiCompletion;
        _aiConnections = aiConnections;
        _configuration = configuration;
        _logger = logger;
        _dataProtection = dataProtection;
        _externalTools = externalTools;
    }

    public async Task<Result<KeoBiaMatchAnalysisDto>> AnalyzeMatchAsync(Guid matchId, bool force = false, CancellationToken ct = default)
    {
        var key = new AnalysisQueueKey(matchId, force);
        var queued = new Lazy<Task<Result<KeoBiaMatchAnalysisDto>>>(
            () => RunQueuedAnalysisAsync(key, matchId, force),
            LazyThreadSafetyMode.ExecutionAndPublication);

        var active = InflightAnalyses.GetOrAdd(key, queued);
        if (!ReferenceEquals(active, queued))
        {
            _logger.LogInformation("Joining queued KeoBia analysis for match {MatchId}.", matchId);
        }

        return await active.Value;
    }

    private async Task<Result<KeoBiaMatchAnalysisDto>> RunQueuedAnalysisAsync(AnalysisQueueKey key, Guid matchId, bool force)
    {
        try
        {
            return await AnalyzeMatchCoreAsync(matchId, force, CancellationToken.None);
        }
        finally
        {
            InflightAnalyses.TryRemove(key, out _);
        }
    }

    private async Task<Result<KeoBiaMatchAnalysisDto>> AnalyzeMatchCoreAsync(Guid matchId, bool force, CancellationToken ct)
    {
        var match = await _db.KeoBiaMatches.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == matchId && x.Status != KeoBiaMatchStatus.Cancelled, ct);
        if (match is null)
            return Result<KeoBiaMatchAnalysisDto>.Failure("Không tìm thấy trận đấu.");

        var now = DateTime.UtcNow;
        var title = $"{match.HomeName} vs {match.AwayName}";

        if (!force && IsCacheUsable(match, now))
        {
            return Result<KeoBiaMatchAnalysisDto>.Success(new KeoBiaMatchAnalysisDto(
                title,
                NormalizeAnalysisContent(match.AiAnalysisContent!),
                "cache",
                true,
                match.AiAnalysisGeneratedAt,
                ParseStoredProbability(match)));
        }

        var connection = await ResolveConnectionAsync(ct);
        if (connection is null)
        {
            return Result<KeoBiaMatchAnalysisDto>.Success(new KeoBiaMatchAnalysisDto(
                title,
                BuildLocalAnalysis(match),
                "local",
                false,
                null,
                BuildStoredProbability(match)));
        }

        var community = await GetCommunityPercentsAsync(match, ct);
        var liveContext = await BuildLiveResearchContextAsync(match, ct);
        var result = await _aiCompletion.GenerateAsync(new AiGenerationRequest(
            AiTaskKeys.KeoBiaExpertAnalysis,
            AiGenerationStyle.Plain,
            title,
            BuildAnalysisPrompt(match, community, liveContext),
            null,
            "vi",
            connection.Id), ct);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Value?.Content))
        {
            _logger.LogWarning(
                "KeoBia analysis failed for match {MatchId}: {Error}",
                match.Id,
                result.Error ?? "Empty content");

            return Result<KeoBiaMatchAnalysisDto>.Success(new KeoBiaMatchAnalysisDto(
                title,
                BuildLocalAnalysis(match),
                "local",
                false,
                null,
                BuildStoredProbability(match)));
        }

        var extracted = ExtractAnalysis(result.Value.Content, match);
        var probability = extracted.Probability;
        if (probability is not null)
        {
            match.AiHome = probability.Home;
            match.AiDraw = probability.Draw;
            match.AiAway = probability.Away;
            match.AiAnalysisProbabilityJson = JsonSerializer.Serialize(probability, JsonOptions);
        }

        var oddsJson = await GenerateCorrectScoreOddsJsonAsync(match, connection.Id, liveContext, ct)
            ?? extracted.CorrectScoreOddsJson;
        match.CorrectScoreOddsJson = oddsJson;

        match.AiAnalysisContent = extracted.Content;
        match.AiAnalysisSource = connection.Name;
        match.AiAnalysisGeneratedAt = now;
        match.AiAnalysisExpiresAt = ResolveCacheExpiry(match, now);
        match.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        return Result<KeoBiaMatchAnalysisDto>.Success(new KeoBiaMatchAnalysisDto(
            title,
            extracted.Content,
            "ai",
            false,
            now,
            probability ?? BuildStoredProbability(match)));
    }

    public async Task<Result<int>> WarmUpcomingMatchesAsync(DateTime utcNow, CancellationToken ct = default)
    {
        if (!ReadBool("KeoBia:AnalysisWarmup:Enabled", true))
            return Result<int>.Success(0);

        var lookAheadHours = Math.Clamp(ReadInt("KeoBia:AnalysisWarmup:LookAheadHours", 24), 1, 168);
        var batchSize = Math.Clamp(ReadInt("KeoBia:AnalysisWarmup:BatchSize", 4), 1, 24);
        var cutoff = utcNow.AddHours(lookAheadHours);

        var matchIds = await _db.KeoBiaMatches.AsNoTracking()
            .Where(x => x.Status == KeoBiaMatchStatus.Scheduled)
            .Where(x => x.KickoffAt > utcNow && x.KickoffAt <= cutoff)
            .Where(x => string.IsNullOrWhiteSpace(x.AiAnalysisContent)
                || !x.AiAnalysisExpiresAt.HasValue
                || x.AiAnalysisExpiresAt <= utcNow)
            .OrderBy(x => x.KickoffAt)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var generated = 0;
        foreach (var matchId in matchIds)
        {
            var result = await AnalyzeMatchAsync(matchId, force: false, ct);
            if (result.Succeeded && string.Equals(result.Value?.Source, "ai", StringComparison.OrdinalIgnoreCase))
                generated++;
        }

        return Result<int>.Success(generated);
    }

    private async Task<AiConnectionDto?> ResolveConnectionAsync(CancellationToken ct) =>
        (await _aiConnections.GetAllAsync(ct))
        .Where(x => x.IsActive && x.HasApiKey)
        .OrderByDescending(x => x.IsDefault)
        .ThenBy(x => x.Name)
        .FirstOrDefault();

    private async Task<(int Home, int Draw, int Away)> GetCommunityPercentsAsync(KeoBiaMatch match, CancellationToken ct)
    {
        // IgnoreQueryFilters: khi chạy từ scope nền (Force Analysis) site có thể rỗng.
        // Lọc theo MatchId đã duy nhất theo site nên bỏ filter vẫn đúng dữ liệu.
        var rows = await _db.KeoBiaBets.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.MatchId == match.Id)
            .GroupBy(x => x.Choice)
            .Select(g => new { Choice = g.Key, Cups = g.Sum(x => x.Cups) })
            .ToListAsync(ct);

        var home = rows.FirstOrDefault(x => x.Choice == KeoBiaBetChoice.Home)?.Cups ?? 0;
        var draw = rows.FirstOrDefault(x => x.Choice == KeoBiaBetChoice.Draw)?.Cups ?? 0;
        var away = rows.FirstOrDefault(x => x.Choice == KeoBiaBetChoice.Away)?.Cups ?? 0;

        return NormalizePercents(
            Math.Max(1, match.BaseHomeWeight + home * 4),
            Math.Max(1, match.BaseDrawWeight + draw * 4),
            Math.Max(1, match.BaseAwayWeight + away * 4));
    }

    private async Task<string> BuildLiveResearchContextAsync(KeoBiaMatch match, CancellationToken ct)
    {
        if (_externalTools is null || _dataProtection is null)
            return "Tool search/fetch chưa được cấu hình trong service.";

        var configuredTools = await BuildConfiguredExternalToolsAsync(ct);
        if (configuredTools.Count == 0)
            return "Không có Firecrawl/9Router tool đang active.";

        var query = $"{match.HomeName} vs {match.AwayName} latest team news injuries odds preview";
        var sb = new StringBuilder();
        sb.AppendLine("Nguồn live prefetch cho phân tích tự động:");

        var firecrawl = configuredTools.FirstOrDefault(x => x.Tool.Key == "firecrawl_search");
        if (firecrawl.Tool is not null)
        {
            var firecrawlResult = await ExecuteToolAsync(
                firecrawl.Tool,
                firecrawl.Context,
                JsonSerializer.Serialize(new { query, limit = 5, includeContent = true }),
                ct);

            if (IsUsableToolResult(firecrawlResult))
            {
                sb.AppendLine("Firecrawl search:");
                sb.AppendLine(TrimForPrompt(firecrawlResult, 9000));
                return sb.ToString();
            }

            sb.AppendLine("Firecrawl search lỗi/yếu, fallback sang 9Router:");
            sb.AppendLine(TrimForPrompt(firecrawlResult, 1200));
        }

        var nineSearch = configuredTools.FirstOrDefault(x => x.Tool.Key == "ninerouter_search");
        if (nineSearch.Tool is null)
            return sb.AppendLine("Không có 9Router search active để fallback.").ToString();

        var nineResult = await ExecuteToolAsync(
            nineSearch.Tool,
            nineSearch.Context,
            JsonSerializer.Serialize(new { query, max_results = 5, search_type = "news" }),
            ct);
        sb.AppendLine("9Router search:");
        sb.AppendLine(TrimForPrompt(nineResult, 5000));

        var nineFetch = configuredTools.FirstOrDefault(x => x.Tool.Key == "ninerouter_fetch");
        var firstUrl = ExtractFirstUrl(nineResult);
        if (nineFetch.Tool is not null && firstUrl is not null)
        {
            var fetchResult = await ExecuteToolAsync(
                nineFetch.Tool,
                nineFetch.Context,
                JsonSerializer.Serialize(new { url = firstUrl, format = "markdown" }),
                ct);
            sb.AppendLine("9Router fetch nguồn đầu tiên:");
            sb.AppendLine(TrimForPrompt(fetchResult, 6000));
        }

        return sb.ToString();
    }

    private async Task<string?> GenerateCorrectScoreOddsJsonAsync(KeoBiaMatch match, Guid connectionId, string existingLiveContext, CancellationToken ct)
    {
        var oddsContext = await BuildCorrectScoreOddsResearchContextAsync(match, ct);
        var content = string.Join("\n\n", new[]
        {
            $"Trận: {match.HomeName} ({match.HomeCode}) vs {match.AwayName} ({match.AwayCode})",
            $"Bảng/vòng: {match.Stage}",
            "Chỉ lấy Correct Score odds theo 90 phút chính thức.",
            oddsContext,
            "Ngữ cảnh phân tích đã có:",
            TrimForPrompt(existingLiveContext, 5000)
        });

        var result = await _aiCompletion.GenerateAsync(new AiGenerationRequest(
            AiTaskKeys.KeoBiaCorrectScoreOdds,
            AiGenerationStyle.Plain,
            $"Correct Score odds {match.HomeName} vs {match.AwayName}",
            content,
            null,
            "vi",
            connectionId), ct);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Value?.Content))
        {
            _logger.LogWarning("Correct score odds extraction failed for match {MatchId}: {Error}", match.Id, result.Error ?? "Empty content");
            return null;
        }

        return NormalizeCorrectScoreOddsJson(ExtractJsonObject(result.Value.Content));
    }

    private async Task<string> BuildCorrectScoreOddsResearchContextAsync(KeoBiaMatch match, CancellationToken ct)
    {
        if (_externalTools is null || _dataProtection is null)
            return "Tool search/fetch chưa được cấu hình trong service.";

        var configuredTools = await BuildConfiguredExternalToolsAsync(ct);
        if (configuredTools.Count == 0)
            return "Không có Firecrawl/9Router tool đang active.";

        var query = $"{match.HomeName} vs {match.AwayName} correct score odds 90 minutes";
        var sb = new StringBuilder();
        sb.AppendLine("Nguồn web cho Correct Score odds:");

        var nineSearch = configuredTools.FirstOrDefault(x => x.Tool.Key == "ninerouter_search");
        if (nineSearch.Tool is not null)
        {
            var nineResult = await ExecuteToolAsync(
                nineSearch.Tool,
                nineSearch.Context,
                JsonSerializer.Serialize(new { query, max_results = 8, search_type = "web" }),
                ct);
            sb.AppendLine("9Router search:");
            sb.AppendLine(TrimForPrompt(nineResult, 9000));
        }

        var firecrawl = configuredTools.FirstOrDefault(x => x.Tool.Key == "firecrawl_search");
        if (firecrawl.Tool is not null)
        {
            var firecrawlResult = await ExecuteToolAsync(
                firecrawl.Tool,
                firecrawl.Context,
                JsonSerializer.Serialize(new { query, limit = 8, includeContent = true }),
                ct);
            sb.AppendLine("Firecrawl search:");
            sb.AppendLine(TrimForPrompt(firecrawlResult, 9000));
        }

        return sb.ToString();
    }

    private async Task<List<(IAiTool Tool, AiToolContext Context)>> BuildConfiguredExternalToolsAsync(CancellationToken ct)
    {
        var protector = _dataProtection!.CreateProtector("NewsCMS.Ai.ApiKey");
        var skills = await _db.AiSkills.AsNoTracking()
            .Where(x => x.Kind == AiSkillKind.Tool
                && (x.ToolType!.StartsWith("firecrawl") || x.ToolType.StartsWith("ninerouter"))
                && x.IsActive
                && !x.IsDeleted)
            .ToListAsync(ct);

        var result = new List<(IAiTool Tool, AiToolContext Context)>();
        foreach (var tool in _externalTools!.Where(x => x.Key.StartsWith("firecrawl") || x.Key.StartsWith("ninerouter")))
        {
            var skill = skills.FirstOrDefault(x => x.ToolType == tool.Key);
            if (skill is null) continue;

            string? apiKey = null;
            if (!string.IsNullOrWhiteSpace(skill.ApiKeyEncrypted))
            {
                try { apiKey = protector.Unprotect(skill.ApiKeyEncrypted); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to decrypt API key for analysis tool {ToolKey}.", tool.Key); }
            }

            result.Add((tool, new AiToolContext(apiKey, skill.BaseUrl, skill.ConfigJson)));
        }

        return result;
    }

    private static async Task<string> ExecuteToolAsync(IAiTool tool, AiToolContext context, string argsJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            return await tool.ExecuteAsync(doc.RootElement, context, ct);
        }
        catch (Exception ex)
        {
            return $"{tool.Key} lỗi: {ex.Message}";
        }
    }

    private static bool IsUsableToolResult(string result) =>
        !string.IsNullOrWhiteSpace(result)
        && !result.Contains(" lỗi ", StringComparison.OrdinalIgnoreCase)
        && !result.StartsWith("Lỗi", StringComparison.OrdinalIgnoreCase)
        && !result.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase)
        && !result.Contains("chưa có", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractFirstUrl(string text)
    {
        var match = Regex.Match(text, @"https?://[^\s)]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Value.TrimEnd('.', ',', ';') : null;
    }

    private static string TrimForPrompt(string value, int maxChars) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";

    private static string BuildAnalysisPrompt(KeoBiaMatch match, (int Home, int Draw, int Away) community, string liveContext) =>
        string.Join("\n", new List<string>
        {
            $"Trận: {match.HomeName} ({match.HomeCode}) vs {match.AwayName} ({match.AwayCode})",
            $"Bảng/vòng: {match.Stage}",
            $"Thời gian Việt Nam: {ToVietnamTime(match.KickoffAt):HH:mm dd/MM/yyyy} UTC+7",
            $"Thời gian gốc: {AsUtc(match.KickoffAt):HH:mm dd/MM/yyyy} UTC",
            $"Sân: {match.Venue}",
            $"Trạng thái nổi bật: {(match.IsHot ? match.HotLabel : "Trận thường")}",
            $"Tỷ lệ cộng đồng: {match.HomeName} {community.Home}%, Hòa {community.Draw}%, {match.AwayName} {community.Away}%",
            $"Tỷ lệ AI đang lưu: {match.HomeName} {match.AiHome}%, Hòa {match.AiDraw}%, {match.AwayName} {match.AiAway}%",
            $"Ghi chú dữ liệu đang lưu: {match.AiSummary}",
            "Quy tắc kèo: kết quả tính theo 90 phút chính thức; hiệp phụ và penalty chỉ dùng để hiển thị tỷ số full trận.",
            "Bắt buộc tìm Correct Score odds/tỉ số chính xác từ nguồn web nếu có. Ưu tiên mở range rộng nhất có thể, ít nhất 10 lựa chọn nếu nguồn có: 0-0, 1-0, 0-1, 1-1, 2-0, 0-2, 2-1, 1-2, 2-2, 3-0, 0-3, 3-1, 1-3, 3-2, 2-3, 3-3, 4-0, 0-4, 4-1, 1-4, 4-2, 2-4. Nếu nguồn có nhóm khác hoặc chỉ có vài tỉ số cụ thể, thêm key other, home_other, away_other, draw_other. Cuối câu trả lời phải có đúng một dòng máy đọc được: ODDS_TY_SO_CHINH_XAC: {\"0-0\":7.5,\"1-0\":6.5,\"other\":21}. Nếu không tìm được odds thật, trả ODDS_TY_SO_CHINH_XAC: {}. Không tự bịa odds.",
            liveContext
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static DateTime ToVietnamTime(DateTime value) => AsUtc(value).Add(VietnamTimeOffset);

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static ExtractedAnalysis ExtractAnalysis(string raw, KeoBiaMatch match)
    {
        var content = raw.Trim();
        KeoBiaAnalysisProbabilityDto? probability = null;
        string? correctScoreOddsJson = null;

        var oddsMatch = CorrectScoreOddsJsonLineRegex.Match(content);
        if (oddsMatch.Success)
        {
            correctScoreOddsJson = NormalizeCorrectScoreOddsJson(oddsMatch.Groups["json"].Value);
            content = CorrectScoreOddsJsonLineRegex.Replace(content, string.Empty).Trim();
        }

        var jsonMatch = ProbabilityJsonLineRegex.Match(content);
        if (jsonMatch.Success)
        {
            probability = ParseProbabilityJson(jsonMatch.Groups["json"].Value, match);
            content = ProbabilityJsonLineRegex.Replace(content, string.Empty).Trim();
        }

        probability ??= ParseProbabilityFromText(content, match);
        content = NormalizeAnalysisContent(content);
        return new ExtractedAnalysis(content, probability, correctScoreOddsJson);
    }

    private static string? NormalizeCorrectScoreOddsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var odds = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var key = property.Name.Trim().ToLowerInvariant().Replace(" ", "_");
                if (!Regex.IsMatch(key, @"^(\d{1,2}-\d{1,2}|other|home_other|away_other|draw_other)$", RegexOptions.CultureInvariant))
                    continue;
                decimal value;
                if (property.Value.ValueKind == JsonValueKind.Number)
                    property.Value.TryGetDecimal(out value);
                else if (property.Value.ValueKind == JsonValueKind.String)
                    decimal.TryParse(property.Value.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value);
                else
                    continue;
                if (value >= 1 && value <= 999)
                    odds[key] = ClampCorrectScoreOdds(value);
            }
            EnsureOtherOdds(odds);
            return odds.Count == 0 ? null : JsonSerializer.Serialize(odds, JsonOptions);
        }
        catch
        {
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

    private static decimal ClampCorrectScoreOdds(decimal odds)
    {
        var scaled = odds <= MaxCorrectScoreOdds ? odds : odds / MaxCorrectScoreOdds;
        return Math.Round(Math.Clamp(scaled, 1m, MaxCorrectScoreOdds), 2);
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end >= start ? text[start..(end + 1)] : text;
    }

    private static string NormalizeAnalysisContent(string content)
    {
        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var source = SourceSectionRegex.Match(content);
        if (!source.Success)
            return content;

        var sourceSection = source.Groups["section"].Value.Trim();
        if (content.EndsWith(sourceSection, StringComparison.OrdinalIgnoreCase))
            return content;

        var withoutSource = content.Remove(source.Index, source.Length).Trim();
        return string.IsNullOrWhiteSpace(withoutSource)
            ? sourceSection
            : $"{withoutSource}\n\n{sourceSection}";
    }

    private static KeoBiaAnalysisProbabilityDto? ParseProbabilityJson(string? json, KeoBiaMatch match)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var home = ReadInt(root, "home", "homeWin", "homeProbability", "homePercent");
            var draw = ReadInt(root, "draw", "drawProbability", "drawPercent");
            var away = ReadInt(root, "away", "awayWin", "awayProbability", "awayPercent");
            if (!home.HasValue || !draw.HasValue || !away.HasValue)
                return null;

            var normalized = NormalizePercents(home.Value, draw.Value, away.Value);
            var homeLabel = ReadString(root, "homeLabel") ?? $"{match.HomeName} thắng";
            var drawLabel = ReadString(root, "drawLabel") ?? "Hòa";
            var awayLabel = ReadString(root, "awayLabel") ?? $"{match.AwayName} thắng";

            return new KeoBiaAnalysisProbabilityDto(normalized.Home, normalized.Draw, normalized.Away, homeLabel, drawLabel, awayLabel);
        }
        catch
        {
            return null;
        }
    }

    private static KeoBiaAnalysisProbabilityDto? ParseProbabilityFromText(string content, KeoBiaMatch match)
    {
        var section = ExtractSection(content, "Xác suất:");
        var matches = PercentRegex.Matches(string.IsNullOrWhiteSpace(section) ? content : section);
        if (matches.Count < 3)
            return null;

        var home = ReadPercent(matches[0].Groups["value"].Value);
        var draw = ReadPercent(matches[1].Groups["value"].Value);
        var away = ReadPercent(matches[2].Groups["value"].Value);
        if (!home.HasValue || !draw.HasValue || !away.HasValue)
            return null;

        var normalized = NormalizePercents(home.Value, draw.Value, away.Value);
        return new KeoBiaAnalysisProbabilityDto(
            normalized.Home,
            normalized.Draw,
            normalized.Away,
            $"{match.HomeName} thắng",
            "Hòa",
            $"{match.AwayName} thắng");
    }

    private static string ExtractSection(string content, string heading)
    {
        var start = content.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;

        start += heading.Length;
        var rest = content[start..];
        var nextHeading = Regex.Match(rest, @"\n\S[^:\n]{0,80}:", RegexOptions.CultureInvariant);
        return nextHeading.Success ? rest[..nextHeading.Index] : rest;
    }

    private static int? ReadInt(JsonElement root, params string[] names)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number))
                return number;

            if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out number))
                return number;
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }

        return null;
    }

    private static int? ReadPercent(string value) =>
        int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 0, 100) : null;

    private static KeoBiaAnalysisProbabilityDto? ParseStoredProbability(KeoBiaMatch match) =>
        ParseProbabilityJson(match.AiAnalysisProbabilityJson, match) ?? BuildStoredProbability(match);

    private static KeoBiaAnalysisProbabilityDto BuildStoredProbability(KeoBiaMatch match)
    {
        var hasAi = match.AiHome > 0 || match.AiDraw > 0 || match.AiAway > 0;
        var normalized = hasAi
            ? NormalizePercents(match.AiHome, match.AiDraw, match.AiAway)
            : NormalizePercents(match.BaseHomeWeight, match.BaseDrawWeight, match.BaseAwayWeight);
        var probability = new KeoBiaAnalysisProbabilityDto(
            normalized.Home,
            normalized.Draw,
            normalized.Away,
            $"{match.HomeName} thắng",
            "Hòa",
            $"{match.AwayName} thắng");
        return probability;
    }

    private static bool IsCacheUsable(KeoBiaMatch match, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(match.AiAnalysisContent))
            return false;

        if (match.Status == KeoBiaMatchStatus.Finished)
            return true;

        return match.AiAnalysisExpiresAt.HasValue && match.AiAnalysisExpiresAt.Value > now;
    }

    private static DateTime ResolveCacheExpiry(KeoBiaMatch match, DateTime now)
    {
        if (match.Status == KeoBiaMatchStatus.Finished)
            return now.AddDays(30);

        if (match.KickoffAt > now.AddHours(2))
            return match.KickoffAt.AddHours(-2);

        if (match.KickoffAt > now)
            return match.KickoffAt.AddHours(2);

        return now.AddHours(6);
    }

    private static string BuildLocalAnalysis(KeoBiaMatch match)
    {
        var probability = BuildStoredProbability(match);
        return string.Join("\n", [
            $"Tổng quan: Chưa có kết nối AI khả dụng nên hệ thống đang dùng dữ liệu xác suất đã lưu cho {match.HomeName} vs {match.AwayName}.",
            $"Xác suất: {match.HomeName} thắng {probability.Home}%, Hòa {probability.Draw}%, {match.AwayName} thắng {probability.Away}%.",
            "Value: Chưa có nguồn live và tỷ lệ thị trường/giá nhà cái để đối chiếu, nên chưa đủ dữ liệu kết luận value.",
            "Lưu ý: Đây là phân tích tham khảo từ dữ liệu nội bộ, không đảm bảo kết quả."
        ]);
    }

    private static (int Home, int Draw, int Away) NormalizePercents(int home, int draw, int away)
    {
        home = Math.Clamp(home, 0, 100);
        draw = Math.Clamp(draw, 0, 100);
        away = Math.Clamp(away, 0, 100);

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

    private bool ReadBool(string key, bool defaultValue)
    {
        var raw = _configuration[key];
        return string.IsNullOrWhiteSpace(raw) ? defaultValue : bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    private int ReadInt(string key, int defaultValue)
    {
        var raw = _configuration[key];
        return int.TryParse(raw, out var value) ? value : defaultValue;
    }

    private sealed record ExtractedAnalysis(string Content, KeoBiaAnalysisProbabilityDto? Probability, string? CorrectScoreOddsJson);
    private readonly record struct AnalysisQueueKey(Guid MatchId, bool Force);
}
