using Slugify;

namespace NewsCMS.Infrastructure.Content;

/// <summary>
/// Sinh slug thân thiện URL cho tiếng Việt: bỏ dấu, đổi sang chữ thường, đổi space + ký tự đặc biệt thành dấu gạch ngang.
/// </summary>
public class SlugHelper
{
    private readonly ISlugHelper _impl;

    public SlugHelper()
    {
        var config = new SlugHelperConfiguration
        {
            ForceLowerCase = true,
            CollapseWhiteSpace = true,
            TrimWhitespace = true,
            CollapseDashes = true,
            // Một số ký tự VN đặc biệt
            StringReplacements = { ["đ"] = "d", ["Đ"] = "d", ["'"] = "", ["\""] = "" }
        };
        _impl = new Slugify.SlugHelper(config);
    }

    public string Generate(string input) => _impl.GenerateSlug(input);
}
