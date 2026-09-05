using System.Text;
using NewsCMS.Application.KeoBia;
using Telegram.Bot.Types.ReplyMarkups;

namespace NewsCMS.Theme.KeoBia2026.Areas.KeoBia2026;

internal static class TelegramBotText
{
    public const string ScheduledStatus = "Scheduled";
    public const string LiveStatus = "Live";
    public const string FinishedStatus = "Finished";
    public const string HomeChoice = "home";
    public const string DrawChoice = "draw";
    public const string AwayChoice = "away";

    private static readonly TimeSpan VoteOpenWindow = TimeSpan.FromDays(1);
    private static readonly TimeSpan VoteLockWindow = TimeSpan.FromMinutes(20);
    private const int TelegramTextLimit = 3900;

    private static string CurrentUnitCode => KeoBiaUnitRules.GetCurrentUnitCode();
    private static string UnitCount(int count, string? unitCode) => KeoBiaUnitRules.FormatCount(count, unitCode);

    private static readonly IReadOnlyDictionary<string, string> TeamCountryCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARG"] = "AR",
            ["AUS"] = "AU",
            ["BEL"] = "BE",
            ["BRA"] = "BR",
            ["CAN"] = "CA",
            ["CHI"] = "CL",
            ["CHN"] = "CN",
            ["CIV"] = "CI",
            ["CMR"] = "CM",
            ["COL"] = "CO",
            ["CRC"] = "CR",
            ["CRO"] = "HR",
            ["CZE"] = "CZ",
            ["DEN"] = "DK",
            ["ECU"] = "EC",
            ["EGY"] = "EG",
            ["ENG"] = "GB",
            ["ESP"] = "ES",
            ["FIN"] = "FI",
            ["FRA"] = "FR",
            ["GER"] = "DE",
            ["GHA"] = "GH",
            ["IRN"] = "IR",
            ["IRQ"] = "IQ",
            ["ITA"] = "IT",
            ["JPN"] = "JP",
            ["KOR"] = "KR",
            ["KSA"] = "SA",
            ["MAR"] = "MA",
            ["MEX"] = "MX",
            ["NED"] = "NL",
            ["NGA"] = "NG",
            ["NOR"] = "NO",
            ["NZL"] = "NZ",
            ["PER"] = "PE",
            ["POL"] = "PL",
            ["POR"] = "PT",
            ["QAT"] = "QA",
            ["RSA"] = "ZA",
            ["SCO"] = "GB",
            ["SEN"] = "SN",
            ["SUI"] = "CH",
            ["SWE"] = "SE",
            ["TUN"] = "TN",
            ["UAE"] = "AE",
            ["URU"] = "UY",
            ["USA"] = "US",
            ["WAL"] = "GB"
        };

    public static InlineKeyboardMarkup MainMenuKeyboard() => new(
        [
            [
                InlineKeyboardButton.WithCallbackData("🎯 Đặt kèo", "cmd:datkeo"),
                InlineKeyboardButton.WithCallbackData("🎟️ Kèo của tôi", "cmd:keo")
            ],
            [
                InlineKeyboardButton.WithCallbackData("📅 Lịch", "cmd:lich"),
                InlineKeyboardButton.WithCallbackData("🏆 BXH", "cmd:bxh")
            ],
            [
                InlineKeyboardButton.WithCallbackData("📊 Bình chọn", "cmd:quiz")
            ],
            [
                InlineKeyboardButton.WithCallbackData($"{KeoBiaUnitRules.Emoji(CurrentUnitCode)} Nộp cốc bia và gói lạc", "cmd:nopbia"),
                InlineKeyboardButton.WithCallbackData("🧾 Lịch sử nộp", "cmd:lichsunopbia")
            ],
            [
                InlineKeyboardButton.WithCallbackData("🛑 Tôi muốn dừng lại", "cmd:dunglai")
            ]
        ]);

    public static string StartText(int beerCups = 0, int peanutPacks = 0) =>
        beerCups > 0 || peanutPacks > 0
            ? $"🍺 Keo Bia 2026\nĐã gom: {KeoBiaUnitRules.FormatBreakdown(beerCups, peanutPacks)}\nChọn một mục để tiếp tục."
            : "🍺 Keo Bia 2026\nChọn một mục để tiếp tục.";

    public static string BuildUnitTransitionText() =>
        "🥜 Từ vòng tứ kết ngày 10/07, phần chơi mới chuyển từ cốc bia sang gói lạc.\n\n" +
        "Lịch sử cốc bia trước đó vẫn được giữ nguyên. Bạn có thể tiếp tục chơi, hoặc dừng tại đây.\n\n" +
        "Nếu dừng, tài khoản sẽ bị khóa khỏi các kèo tiếp theo và không bị tính kèo miss sau thời điểm dừng.";

    public static string BuildReminderText(KeoBiaMatchListItemDto match) =>
        new StringBuilder()
            .AppendLine("🔔 Kèo đang mở!")
            .AppendLine()
            .AppendLine($"⚽ {TeamLabel(match.HomeName, match.HomeCode)} vs {TeamLabel(match.AwayName, match.AwayCode)}")
            .AppendLine($"🕘 Giờ VN: {FormatVietnamTime(match.KickoffAt)}")
            .AppendLine()
            .AppendLine("Bạn chưa đặt cược trận này!")
            .Append("Nhấn nút bên dưới để dự đoán ngay.")
            .ToString();

    public static string BuildAutoAssignText(KeoBiaAutoAssignedBetDto bet) =>
        new StringBuilder()
            .AppendLine("⏰ Đã khóa kèo!")
            .AppendLine()
            .AppendLine($"⚽ {bet.HomeName} vs {bet.AwayName}")
            .AppendLine()
            .AppendLine($"Bạn chưa chọn nên hệ thống đã tự đặt giúp bạn vào cửa đông người chọn nhất: 🎯 {bet.ChoiceLabel}.")
            .Append($"Cửa này thua thì bạn mới mất {KeoBiaUnitRules.FullLabel(bet.UnitCode)} nhé. Chúc may mắn! {KeoBiaUnitRules.Emoji(bet.UnitCode)}")
            .ToString();

    public static string MatchButtonLabel(KeoBiaMatchListItemDto match) =>
        $"⚽ {TeamShort(match.HomeCode)} vs {TeamShort(match.AwayCode)} · {FormatVietnamTime(match.KickoffAt)}";

    public static string ChoiceButtonLabel(KeoBiaPublicMatchDto match, string choice) =>
        choice switch
        {
            HomeChoice => TeamShort(match.HomeCode),
            AwayChoice => TeamShort(match.AwayCode),
            DrawChoice => "🤝 Hòa",
            _ => choice
        };

    public static string BuildCupChoiceText(KeoBiaPublicMatchDto match, string choice) =>
        new StringBuilder()
            .AppendLine($"{TeamLabel(match.HomeName, match.HomeCode)} vs {TeamLabel(match.AwayName, match.AwayCode)}")
            .AppendLine($"🎯 Cửa đã chọn: {ChoiceLabel(match, choice)}")
            .Append($"{KeoBiaUnitRules.Emoji(match.UnitCode)} Chọn số {match.UnitShortLabel}:")
            .ToString();

    public static string BuildMatchDetailText(KeoBiaPublicMatchDto match)
    {
        var totalBettors = match.HomeBettors + match.DrawBettors + match.AwayBettors;
        var sb = new StringBuilder()
            .AppendLine($"⚽ {TeamLabel(match.HomeName, match.HomeCode)} vs {TeamLabel(match.AwayName, match.AwayCode)}")
            .AppendLine($"🕘 Giờ VN: {FormatVietnamTime(match.KickoffAt)}")
            .AppendLine($"🏟️ Sân: {match.Venue}");
        if (match.AllowsDraw)
        {
            sb.AppendLine($"🤖 AI: {TeamShort(match.HomeCode)} {match.AiHome}% | 🤝 Hòa {match.AiDraw}% | {TeamShort(match.AwayCode)} {match.AiAway}%")
              .AppendLine($"👥 Cộng đồng: {TeamShort(match.HomeCode)} {match.CommunityHome}% | 🤝 Hòa {match.CommunityDraw}% | {TeamShort(match.AwayCode)} {match.CommunityAway}%");
        }
        else
        {
            sb.AppendLine($"🤖 AI: {TeamShort(match.HomeCode)} {match.AiHome}% | {TeamShort(match.AwayCode)} {match.AiAway}%")
              .AppendLine($"👥 Cộng đồng: {TeamShort(match.HomeCode)} {match.CommunityHome}% | {TeamShort(match.AwayCode)} {match.CommunityAway}%");
        }
        sb.AppendLine($"🍺 Người đặt: {totalBettors}");

        if (match.HomeScore.HasValue && match.AwayScore.HasValue)
            sb.AppendLine($"🏁 Tỷ số full trận: {match.HomeScore}-{match.AwayScore}");

        return sb.Append(StatusLine(match)).ToString();
    }

    public static string BuildAnalysisText(KeoBiaMatchAnalysisDto analysis)
    {
        var content = (analysis.Content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(content))
            content = "Chưa có nội dung phân tích cho trận này.";

        var text = new StringBuilder()
            .AppendLine("🧠 Phân tích AI")
            .AppendLine(analysis.Title)
            .AppendLine()
            .Append(content)
            .ToString();

        if (text.Length <= TelegramTextLimit)
            return text;

        var suffix = "\n\n… Nội dung đầy đủ xem ở nút Phân tích trên web.";
        var take = Math.Max(0, TelegramTextLimit - suffix.Length);
        return text[..take].TrimEnd() + suffix;
    }

    public static string BuildHistoryLine(KeoBiaPlayerPredictionHistoryItemDto item)
    {
        var sb = new StringBuilder()
            .Append("• ")
            .AppendLine(item.MatchLabel)
            .Append("  🎯 ")
            .Append(DecorateChoiceLabel(item.ChoiceLabel))
            .Append(" · ")
            .Append(KeoBiaUnitRules.Emoji(item.UnitCode))
            .Append(' ')
            .Append(UnitCount(item.Cups, item.UnitCode))
            .Append(" · ")
            .Append(HistoryStatus(item));

        if (item.HomeScore.HasValue && item.AwayScore.HasValue)
        {
            sb.Append(" · 🏁 ")
                .Append(item.HomeScore)
                .Append('-')
                .Append(item.AwayScore);
        }

        return sb.ToString();
    }

    public static string ScheduleLine(KeoBiaPublicMatchDto match) =>
        $"• {TeamShort(match.HomeCode)} vs {TeamShort(match.AwayCode)} · {FormatVietnamTime(match.KickoffAt)}";

    public static string ResultLine(KeoBiaPublicMatchDto match) =>
        $"• {TeamShort(match.HomeCode)} {match.HomeScore}-{match.AwayScore}{PenaltySuffix(match.PenaltyHomeScore, match.PenaltyAwayScore)} {TeamShort(match.AwayCode)}";

    public static string BuildResultNotificationText(KeoBiaTelegramResultNotificationDto item)
    {
        var finalScore = item.HomeScore.HasValue && item.AwayScore.HasValue
            ? $"{item.HomeScore}-{item.AwayScore}"
            : "chưa có tỷ số";
        var hasRegularScore = item.ResultHomeScore.HasValue && item.ResultAwayScore.HasValue;
        var regularScore = hasRegularScore ? $"{item.ResultHomeScore}-{item.ResultAwayScore}" : "";
        var resultLine = !hasRegularScore
            ? $"📌 Kết quả: {DecorateChoiceLabel(item.ResultChoiceLabel)}"
            : $"📌 90p: {TeamLabel(item.HomeName, item.HomeCode)} {regularScore} {TeamLabel(item.AwayName, item.AwayCode)}";
        var starIcon = item.StarType == "hope" ? " ⭐" : item.StarType == "devil" ? " 😈" : "";
        var starEffect = "";
        if (item.StarType == "hope" || item.StarType == "devil")
        {
            starEffect = item.IsCorrect
                ? $"\n{starIcon} Star: -1 {KeoBiaUnitRules.ShortLabel(item.UnitCode)} đã mất"
                : $"\n{starIcon} Star: +1 {KeoBiaUnitRules.ShortLabel(item.UnitCode)} đã mất";
        }
        var verdict = item.IsCorrect
            ? "✅ Đúng kèo"
            : $"❌ Sai kèo -{UnitCount(item.Cups, item.UnitCode)}";
        var scoreVerdict = BuildCorrectScoreVerdict(item);

        return new StringBuilder()
            .AppendLine("🏁 Kết quả trận đấu")
            .AppendLine($"Chung cuộc: {TeamLabel(item.HomeName, item.HomeCode)} {finalScore}{PenaltySuffix(item.PenaltyHomeScore, item.PenaltyAwayScore)} {TeamLabel(item.AwayName, item.AwayCode)}")
            .AppendLine($"🕘 Giờ VN: {FormatVietnamTime(item.KickoffAt)}")
            .AppendLine($"🏟️ Sân: {item.Venue}")
            .AppendLine()
            .AppendLine($"🎟️ Vé của bạn: {DecorateChoiceLabel(item.ChoiceLabel)}{starIcon} · {KeoBiaUnitRules.Emoji(item.UnitCode)} {UnitCount(item.Cups, item.UnitCode)}")
            .AppendLine(resultLine)
            .Append(scoreVerdict)
            .Append(verdict)
            .Append(starEffect)
            .ToString();
    }

    private static string BuildCorrectScoreVerdict(KeoBiaTelegramResultNotificationDto item)
    {
        if (!item.PredictedHomeScore.HasValue || !item.PredictedAwayScore.HasValue)
            return "";

        var predicted = $"{item.PredictedHomeScore}-{item.PredictedAwayScore}";
        var exact = item.ResultHomeScore.HasValue && item.ResultAwayScore.HasValue
            && item.PredictedHomeScore == item.ResultHomeScore
            && item.PredictedAwayScore == item.ResultAwayScore;
        if (!exact)
            return $"🎯 Tỉ số 90p bạn chọn: {predicted} · ❌ đoán sai mất {UnitCount(1, item.UnitCode)}\n";

        return $"🎯 Tỉ số 90p bạn chọn: {predicted} · ✅ đoán đúng được giảm {UnitCount(3, item.UnitCode)}\n";
    }

    private static string PenaltySuffix(int? home, int? away) => home.HasValue && away.HasValue ? $" (pen {home}-{away})" : string.Empty;

    public static string ChoiceLabel(KeoBiaPublicMatchDto match, string choice) =>
        choice switch
        {
            HomeChoice => TeamShort(match.HomeCode),
            AwayChoice => TeamShort(match.AwayCode),
            DrawChoice => "🤝 Hòa",
            _ => choice
        };

    public static string HistoryStatus(KeoBiaPlayerPredictionHistoryItemDto item)
    {
        if (!item.IsSettled)
            return "⏳ đang chờ";
        return item.IsCorrect == true ? "✅ đúng" : "❌ sai";
    }

    public static bool IsOpenForVoting(KeoBiaMatchListItemDto match) =>
        match.Status == ScheduledStatus &&
        DateTime.UtcNow >= match.KickoffAt.Add(-VoteOpenWindow) &&
        DateTime.UtcNow < match.KickoffAt.Add(-VoteLockWindow);

    public static bool IsOpenForVoting(KeoBiaPublicMatchDto match) =>
        match.Status == ScheduledStatus &&
        DateTime.UtcNow >= match.KickoffAt.Add(-VoteOpenWindow) &&
        DateTime.UtcNow < match.KickoffAt.Add(-VoteLockWindow);

    public static string FormatVietnamTime(DateTime utc)
    {
        var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7);
        return value.ToString("HH:mm dd/MM");
    }

    private static string StatusLine(KeoBiaPublicMatchDto match) =>
        match.Status switch
        {
            ScheduledStatus when IsOpenForVoting(match) => "🔓 Trạng thái: đang mở kèo",
            ScheduledStatus => "🔒 Trạng thái: chưa mở hoặc đã khóa kèo",
            LiveStatus => "🟢 Trạng thái: đang đá",
            FinishedStatus => "🏁 Trạng thái: đã kết thúc",
            _ => "⛔ Trạng thái: không mở kèo"
        };

    private static string TeamLabel(string name, string code) =>
        $"{TeamShort(code)} {name}".Trim();

    private static string TeamShort(string code)
    {
        code = string.IsNullOrWhiteSpace(code) ? "???" : code.Trim().ToUpperInvariant();
        return $"{Flag(code)} {code}";
    }

    private static string DecorateChoiceLabel(string label)
    {
        if (string.Equals(label, "Hòa", StringComparison.OrdinalIgnoreCase))
            return "🤝 Hòa";

        return label.Length is 2 or 3
            ? TeamShort(label)
            : label;
    }

    private static string Flag(string code)
    {
        if (code.Length == 3 && TeamCountryCodes.TryGetValue(code, out var countryCode))
            code = countryCode;

        if (code.Length != 2 || !code.All(char.IsAsciiLetter))
            return "🏳️";

        var upper = code.ToUpperInvariant();
        return string.Concat(upper.Select(ch => char.ConvertFromUtf32(0x1F1E6 + ch - 'A')));
    }
}
