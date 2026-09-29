using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NewsCMS.Application.Ai.PromptLibrary;

/// <summary>
/// Luật nội dung dùng chung cho mọi kho mẫu prompt (video, ảnh): mẫu quản trị viết tay lẫn mẫu AI sinh
/// theo trend đều qua đây.
/// </summary>
/// <remarks>
/// Mẫu AI sinh không có người đọc trước khi hiện (trừ khi bật duyệt), nên luật phải tự chặn: cụm quảng
/// cáo tuyệt đối/cam kết (Luật Quảng cáo cấm khi không có bằng chứng) và ngành không làm quảng cáo tự động.
/// </remarks>
public static partial class AdContentRules
{
    /// <summary>Cụm quảng cáo tuyệt đối / cam kết. So khớp theo từ, không phân biệt hoa thường.</summary>
    public static readonly IReadOnlyList<string> BannedPhrases =
    [
        // Không chặn "nhất" trơn: "thống nhất", "nhất định" là tiếng Việt bình thường.
        "tốt nhất", "rẻ nhất", "đẹp nhất", "ngon nhất", "hay nhất", "chất lượng nhất", "nhất thị trường",
        "hàng đầu", "số 1", "số một", "duy nhất", "100%", "cam kết", "đảm bảo", "bảo đảm", "chữa khỏi", "chữa bệnh",
        "trị dứt điểm", "vĩnh viễn", "thần kỳ", "thần dược",
    ];

    /// <summary>Ngành không làm quảng cáo tự động.</summary>
    public static readonly IReadOnlyList<string> SensitiveTopics =
    [
        "cá cược", "cờ bạc", "casino", "rượu", "bia", "thuốc lá", "vape", "thuốc lá điện tử", "thuốc kê đơn", "vay tiền",
    ];

    /// <summary>Lỗi cho mọi cụm quảng cáo tuyệt đối và ngành nhạy cảm có trong <paramref name="text"/>.</summary>
    public static IEnumerable<string> Check(string text)
    {
        foreach (string phrase in FindPhrases(text, BannedPhrases))
        {
            yield return $"Có cụm quảng cáo tuyệt đối/cam kết \"{phrase}\" — dễ vi phạm Luật Quảng cáo.";
        }

        foreach (string topic in FindPhrases(text, SensitiveTopics))
        {
            yield return $"Nhắc tới \"{topic}\" — ngành không làm quảng cáo tự động.";
        }
    }

    /// <summary>Các cụm trong <paramref name="phrases"/> xuất hiện như một từ/cụm từ trọn vẹn trong <paramref name="text"/>.</summary>
    public static IEnumerable<string> FindPhrases(string text, IEnumerable<string> phrases)
    {
        string lower = text.ToLowerInvariant();

        foreach (string phrase in phrases)
        {
            string p = phrase.Trim().ToLowerInvariant();

            if (p.Length == 0)
            {
                continue;
            }

            // Ranh giới từ tự làm: \b của .NET coi chữ có dấu là chữ, nhưng "%" thì không phải chữ.
            string pattern = $"(?<![\\p{{L}}\\p{{N}}]){Regex.Escape(p)}(?![\\p{{L}}\\p{{N}}])";

            if (Regex.IsMatch(lower, pattern))
            {
                yield return phrase.Trim();
            }
        }
    }

    /// <summary>Khoá so trùng tiêu đề: bỏ dấu, chữ thường, gộp khoảng trắng.</summary>
    public static string NormalizeTitle(string title)
    {
        string decomposed = title.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
        }

        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
