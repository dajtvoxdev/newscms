using NewsCMS.Application.Ai.PromptLibrary;

namespace NewsCMS.Application.VideoStudio;

/// <summary>
/// Luật chung cho mọi mẫu — mẫu quản trị viết tay lẫn mẫu AI sinh ra đều qua đây.
/// </summary>
/// <remarks>
/// Mẫu AI sinh không có người đọc trước khi hiện (trừ khi bật duyệt), nên luật phải tự chặn: cụm
/// quảng cáo tuyệt đối/cam kết, ngành nhạy cảm (<see cref="AdContentRules"/>, dùng chung với kho ảnh),
/// và mẫu không dùng được với form (khung hình lạ, thời lượng ngoài khoảng AdVideo nhận).
/// </remarks>
public static class VideoPromptTemplateRules
{
    public static readonly IReadOnlyList<string> Categories =
    [
        "Ăn uống", "Mỹ phẩm & làm đẹp", "Thời trang", "Công nghệ", "Nhà cửa & đời sống", "Mẹ & bé",
        "Du lịch & lưu trú", "Giáo dục", "Dịch vụ", "Sự kiện & khuyến mãi", "Khác",
    ];

    public static readonly IReadOnlyList<string> AspectRatios = ["9:16", "1:1", "16:9"];

    public const int MinDuration = 6;
    public const int MaxDuration = 60;

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

        errors.AddRange(AdContentRules.Check(string.Join(" \n ", input.Title, input.Description, input.ScenePrompt, input.ScriptTemplate)));

        return errors;
    }

    /// <summary>Khoá so trùng: bỏ dấu, chữ thường, gộp khoảng trắng.</summary>
    public static string NormalizeTitle(string title) => AdContentRules.NormalizeTitle(title);

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
}
