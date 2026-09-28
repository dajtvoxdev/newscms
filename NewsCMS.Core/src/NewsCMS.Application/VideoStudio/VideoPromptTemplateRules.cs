using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NewsCMS.Application.VideoStudio;

/// <summary>
/// Luật chung cho mọi mẫu — mẫu quản trị viết tay lẫn mẫu AI sinh ra đều qua đây.
/// </summary>
/// <remarks>
/// Mẫu AI sinh không có người đọc trước khi hiện (trừ khi bật duyệt), nên luật phải tự chặn: cụm
/// quảng cáo tuyệt đối/cam kết (Luật Quảng cáo cấm khi không có bằng chứng), ngành nhạy cảm, và mẫu
/// không dùng được với form (khung hình lạ, thời lượng ngoài khoảng AdVideo nhận).
/// </remarks>
public static partial class VideoPromptTemplateRules
{
    public static readonly IReadOnlyList<string> Categories =
    [
        "Ăn uống", "Mỹ phẩm & làm đẹp", "Thời trang", "Công nghệ", "Nhà cửa & đời sống", "Mẹ & bé",
        "Du lịch & lưu trú", "Giáo dục", "Dịch vụ", "Sự kiện & khuyến mãi", "Khác",
    ];

    public static readonly IReadOnlyList<string> AspectRatios = ["9:16", "1:1", "16:9"];

    public const int MinDuration = 6;
    public const int MaxDuration = 60;

    /// <summary>Cụm quảng cáo tuyệt đối / cam kết. So khớp theo từ, không phân biệt hoa thường.</summary>
    private static readonly string[] BannedPhrases =
    [
        // Không chặn "nhất" trơn: "thống nhất", "nhất định" là tiếng Việt bình thường.
        "tốt nhất", "rẻ nhất", "đẹp nhất", "ngon nhất", "hay nhất", "chất lượng nhất", "nhất thị trường",
        "hàng đầu", "số 1", "số một", "duy nhất", "100%", "cam kết", "đảm bảo", "bảo đảm", "chữa khỏi", "chữa bệnh",
        "trị dứt điểm", "vĩnh viễn", "thần kỳ", "thần dược",
    ];

    /// <summary>Ngành không làm quảng cáo tự động.</summary>
    private static readonly string[] SensitiveTopics =
    [
        "cá cược", "cờ bạc", "casino", "rượu", "bia", "thuốc lá", "vape", "thuốc lá điện tử", "thuốc kê đơn", "vay tiền",
    ];

    /// <summary>Danh sách lỗi; rỗng = hợp lệ.</summary>
    public static List<string> Validate(VideoPromptTemplateInput input)
    {
        var errors = new List<string>();

        Require(input.Title, "Tiêu đề", 200, errors);
        Require(input.Category, "Ngành hàng", 100, errors);
        Require(input.ScenePrompt, "Cảnh quay", 2000, errors);
        Require(input.ScriptTemplate, "Lời thoại", 5000, errors);

        if (input.Description?.Length > 500)
        {
            errors.Add("Mô tả dài quá 500 ký tự.");
        }

        if (!AspectRatios.Contains(input.AspectRatio ?? ""))
        {
            errors.Add($"Khung hình phải là {string.Join(", ", AspectRatios)}.");
        }

        if (input.DurationSeconds is < MinDuration or > MaxDuration)
        {
            errors.Add($"Thời lượng {MinDuration}–{MaxDuration} giây.");
        }

        string text = string.Join(" \n ", input.Title, input.Description, input.ScenePrompt, input.ScriptTemplate);

        foreach (string phrase in FindPhrases(text, BannedPhrases))
        {
            errors.Add($"Có cụm quảng cáo tuyệt đối/cam kết \"{phrase}\" — dễ vi phạm Luật Quảng cáo.");
        }

        foreach (string topic in FindPhrases(text, SensitiveTopics))
        {
            errors.Add($"Nhắc tới \"{topic}\" — ngành không làm quảng cáo tự động.");
        }

        return errors;
    }

    /// <summary>Khoá so trùng: bỏ dấu, chữ thường, gộp khoảng trắng.</summary>
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

    private static IEnumerable<string> FindPhrases(string text, IEnumerable<string> phrases)
    {
        string lower = text.ToLowerInvariant();

        foreach (string phrase in phrases)
        {
            // Ranh giới từ tự làm: \b của .NET coi chữ có dấu là chữ, nhưng "%" thì không phải chữ.
            string pattern = $"(?<![\\p{{L}}\\p{{N}}]){Regex.Escape(phrase)}(?![\\p{{L}}\\p{{N}}])";

            if (Regex.IsMatch(lower, pattern))
            {
                yield return phrase;
            }
        }
    }

    private static void Require(string? value, string name, int max, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} là bắt buộc.");
        }
        else if (value.Length > max)
        {
            errors.Add($"{name} dài quá {max} ký tự.");
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
