using System.Text.RegularExpressions;
using NewsCMS.Application.Ai.PromptLibrary;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Application.ImageStudio;

/// <summary>Tên mục đích ảnh dùng trong URL/JSON (<c>post-cover</c>…) ↔ enum.</summary>
public static class ImagePurposes
{
    private static readonly (ImagePurpose Purpose, string Key, string Label)[] All =
    [
        (ImagePurpose.Free, "free", "Tự do"),
        (ImagePurpose.PostCover, "post-cover", "Ảnh bìa bài viết"),
        (ImagePurpose.PostInline, "post-inline", "Ảnh trong bài"),
        (ImagePurpose.ProductMain, "product-main", "Ảnh sản phẩm chính"),
        (ImagePurpose.ProductGallery, "product-gallery", "Ảnh sản phẩm bổ sung"),
        (ImagePurpose.Banner, "banner", "Banner"),
        (ImagePurpose.Social, "social", "Mạng xã hội"),
    ];

    public static IReadOnlyList<(ImagePurpose Purpose, string Key, string Label)> List => All;

    public static string Key(ImagePurpose purpose) => All.FirstOrDefault(x => x.Purpose == purpose).Key ?? "free";

    public static string Label(ImagePurpose purpose) => All.FirstOrDefault(x => x.Purpose == purpose).Label ?? "Tự do";

    /// <summary>Nhận cả <c>post-cover</c>, <c>post_cover</c> lẫn <c>PostCover</c>.</summary>
    public static ImagePurpose? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string v = value.Trim().Replace('_', '-').ToLowerInvariant();

        foreach ((ImagePurpose purpose, string key, _) in All)
        {
            if (key == v || purpose.ToString().ToLowerInvariant() == v.Replace("-", ""))
            {
                return purpose;
            }
        }

        return null;
    }
}

/// <summary>Chỗ giữ trong mẫu prompt ảnh — modal điền từ ngữ cảnh hoặc cho người dùng gõ.</summary>
public static partial class ImagePromptPlaceholders
{
    public static readonly IReadOnlyList<(string Token, string Label)> Known =
    [
        (ImagePromptTemplate.TopicPlaceholder, "Chủ đề"),
        (ImagePromptTemplate.ProductPlaceholder, "Tên sản phẩm"),
        (ImagePromptTemplate.DescriptionPlaceholder, "Mô tả"),
        (ImagePromptTemplate.BrandPlaceholder, "Thương hiệu"),
    ];

    /// <summary>Chỗ giữ đã biết có trong <paramref name="text"/>, theo thứ tự <see cref="Known"/>.</summary>
    public static List<string> FindKnown(string? text) =>
        Known.Where(k => text?.Contains(k.Token, StringComparison.Ordinal) == true).Select(k => k.Token).ToList();

    /// <summary>Dạng <c>{chu}</c> không nằm trong danh sách — thường là gõ sai tên chỗ giữ.</summary>
    public static List<string> FindUnknown(string? text) =>
        Token().Matches(text ?? string.Empty)
            .Select(m => m.Value)
            .Where(t => Known.All(k => k.Token != t))
            .Distinct()
            .ToList();

    public static string Label(string token) => Known.FirstOrDefault(k => k.Token == token).Label ?? token;

    /// <summary>Thay chỗ giữ có giá trị (không rỗng); chỗ không có giá trị giữ nguyên.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string?> values)
    {
        string result = text;

        foreach ((string token, string? value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                result = result.Replace(token, value.Trim(), StringComparison.Ordinal);
            }
        }

        return result;
    }

    [GeneratedRegex(@"\{[a-z_]{2,30}\}")]
    private static partial Regex Token();
}

/// <summary>
/// Luật cho mẫu prompt ảnh — mẫu tự viết lẫn mẫu trend. Ngoài luật quảng cáo dùng chung
/// (<see cref="AdContentRules"/>) còn chặn từ khoá quản trị cấu hình (nhân vật có bản quyền, phong
/// cách nghệ sĩ, thương hiệu) và bắt mẫu phải có chỗ giữ đúng mục đích — không thì mẫu đang vẽ sản
/// phẩm/chủ đề của ai khác.
/// </summary>
public static class ImagePromptTemplateRules
{
    public static readonly IReadOnlyList<string> Categories =
    [
        "Tin tức & bài viết", "Sản phẩm", "Ăn uống", "Mỹ phẩm & làm đẹp", "Thời trang", "Công nghệ",
        "Nhà cửa & đời sống", "Du lịch & lưu trú", "Giáo dục", "Sự kiện & khuyến mãi", "Mạng xã hội", "Khác",
    ];

    /// <summary>Danh sách chặn mặc định khi seed — quản trị sửa được ở màn hình kho mẫu.</summary>
    public const string DefaultBlockedTerms =
        "Ghibli\nDisney\nPixar\nMarvel\nDC Comics\nPokemon\nPokémon\nDoraemon\nHello Kitty\nNaruto\nOne Piece\nBarbie\nLEGO\nCoca-Cola\nApple\nNike";

    private static readonly string[] TextInImageHints = ["dòng chữ", "chữ viết", "slogan", "khẩu hiệu", "tiêu đề trên ảnh", "logo", "text"];

    /// <summary>Chỗ giữ bắt buộc theo mục đích; null = không bắt buộc.</summary>
    public static string? RequiredPlaceholder(ImagePurpose purpose) => purpose switch
    {
        ImagePurpose.PostCover or ImagePurpose.PostInline => ImagePromptTemplate.TopicPlaceholder,
        ImagePurpose.ProductMain or ImagePurpose.ProductGallery => ImagePromptTemplate.ProductPlaceholder,
        _ => null,
    };

    public static List<string> ParseBlockedTerms(string? text) =>
        (text ?? string.Empty)
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length is > 1 and <= 100)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Lỗi (chặn lưu) và cảnh báo (vẫn lưu, báo cho quản trị biết).</summary>
    public static (List<string> Errors, List<string> Warnings) Validate(ImagePromptTemplateInput input, IReadOnlyCollection<string> blockedTerms)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        Require(input.Title, "Tiêu đề", 200, errors);
        Require(input.Category, "Nhóm", 100, errors);
        Require(input.Prompt, "Prompt", 2000, errors);

        if (input.Description?.Length > 500)
        {
            errors.Add("Mô tả dài quá 500 ký tự.");
        }

        if (!ImageStudioRules.AspectRatios.Contains(input.AspectRatio ?? string.Empty))
        {
            errors.Add($"Tỉ lệ khung phải là {string.Join(", ", ImageStudioRules.AspectRatios)}.");
        }

        if (!Enum.IsDefined(input.Purpose))
        {
            errors.Add("Mục đích ảnh không hợp lệ.");
        }

        string prompt = input.Prompt ?? string.Empty;

        if (!input.RequiresSourceImage && RequiredPlaceholder(input.Purpose) is { } required && !prompt.Contains(required, StringComparison.Ordinal))
        {
            errors.Add($"Mẫu \"{ImagePurposes.Label(input.Purpose)}\" phải có chỗ giữ {required} ({ImagePromptPlaceholders.Label(required)}).");
        }

        foreach (string unknown in ImagePromptPlaceholders.FindUnknown(prompt))
        {
            errors.Add($"Chỗ giữ {unknown} không được hỗ trợ. Dùng {string.Join(", ", ImagePromptPlaceholders.Known.Select(k => k.Token))}.");
        }

        string text = string.Join(" \n ", input.Title, input.Description, input.Prompt);
        errors.AddRange(AdContentRules.Check(text));

        foreach (string term in AdContentRules.FindPhrases(text, blockedTerms))
        {
            errors.Add($"Có từ bị chặn \"{term}\" (nhân vật/thương hiệu/phong cách có bản quyền).");
        }

        if (AdContentRules.FindPhrases(prompt, TextInImageHints).Any())
        {
            warnings.Add("Prompt yêu cầu chữ hoặc logo trong ảnh — model thường vẽ sai chữ tiếng Việt. Nên để chữ ngoài ảnh.");
        }

        return (errors, warnings);
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
}
