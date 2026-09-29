using System.Globalization;
using System.Text;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Application.ImageStudio;

/// <summary>Luật thuần hàm của Xưởng ảnh — không đụng DB hay mạng, test trực tiếp được.</summary>
public static class ImageStudioRules
{
    public const int MaxPromptLength = 4000;

    /// <summary>Trần cứng số biến thể mỗi lần tạo, bất kể model khai bao nhiêu.</summary>
    public const int MaxVariantsHardCap = 4;

    public static readonly IReadOnlyList<string> AspectRatios = ["1:1", "16:9", "9:16", "4:3", "3:4", "3:2", "2:3"];

    public static readonly IReadOnlyList<string> OutputFormats = ["png", "jpeg", "webp"];

    /// <summary>
    /// Chỉ https, hoặc http tới chính máy này (9Router chạy cùng máy). Giữ nguyên luật của tool
    /// <c>image_generate</c> cũ để kết nối đang chạy không bị chặn.
    /// </summary>
    public static bool IsAllowedBaseUrl(string? url, out string? error)
    {
        error = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            error = "Địa chỉ kết nối không hợp lệ.";
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        error = "Địa chỉ kết nối phải là https (http chỉ dùng cho localhost).";
        return false;
    }

    /// <summary>Tách danh sách kích thước (mỗi dòng hoặc dấu phẩy một giá trị); trả lỗi cho giá trị sai.</summary>
    public static List<string> ParseSizes(string? text, out List<string> invalid)
    {
        invalid = [];
        var sizes = new List<string>();

        foreach (string raw in (text ?? string.Empty).Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string value = raw.ToLowerInvariant();

            if (value == "auto" || TryRatio(value, out _))
            {
                if (!sizes.Contains(value))
                {
                    sizes.Add(value);
                }
            }
            else
            {
                invalid.Add(raw);
            }
        }

        return sizes;
    }

    /// <summary>
    /// Đổi tỉ lệ người dùng chọn sang giá trị model nhận: chọn kích thước có tỉ lệ gần nhất (so trên
    /// thang log, để 16:9 và 9:16 cách 1:1 như nhau). Model chỉ khai <c>auto</c> thì gửi <c>auto</c>.
    /// </summary>
    public static string ResolveSize(string aspectRatio, IReadOnlyList<string> supported)
    {
        if (!TryRatio(aspectRatio, out double target))
        {
            target = 1;
        }

        string? best = null;
        double bestDistance = double.MaxValue;

        foreach (string size in supported)
        {
            if (!TryRatio(size, out double ratio))
            {
                continue;
            }

            double distance = Math.Abs(Math.Log(ratio) - Math.Log(target));

            if (distance < bestDistance - 1e-9)
            {
                best = size;
                bestDistance = distance;
            }
        }

        return best ?? (supported.Contains("auto") ? "auto" : aspectRatio);
    }

    /// <summary><c>1536x1024</c> → 1.5; <c>16:9</c> → 1.777…; giá trị khác → false.</summary>
    public static bool TryRatio(string value, out double ratio)
    {
        ratio = 0;
        string[] parts = value.Split(['x', 'X', ':'], 2);

        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int w)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int h)
            || w <= 0 || h <= 0 || w > 10000 || h > 10000)
        {
            return false;
        }

        ratio = (double)w / h;
        return true;
    }

    /// <summary>
    /// Prompt thật gửi đi = mô tả của người dùng + chỉ dẫn theo mục đích + phong cách thương hiệu +
    /// lời dặn tránh chữ. Chữ tiếng Việt do model vẽ gần như chắc chắn sai dấu, nên mặc định dặn không
    /// chèn chữ — trừ khi chính mô tả yêu cầu.
    /// </summary>
    public static string BuildFinalPrompt(string userPrompt, ImagePurpose purpose, string? brandStyle)
    {
        var sb = new StringBuilder(userPrompt.Trim());

        string? purposeHint = purpose switch
        {
            ImagePurpose.PostCover => "Ảnh bìa bài viết: bố cục ngang, chủ thể rõ ràng, chừa khoảng trống an toàn ở mép để cắt khung.",
            ImagePurpose.PostInline => "Ảnh minh hoạ trong bài viết: rõ ý, bố cục gọn.",
            ImagePurpose.ProductMain => "Ảnh sản phẩm chính: sản phẩm ở trung tâm, sắc nét, nền gọn gàng, ánh sáng studio.",
            ImagePurpose.ProductGallery => "Ảnh sản phẩm bổ sung: thể hiện sản phẩm trong bối cảnh sử dụng thực tế.",
            ImagePurpose.Banner => "Ảnh banner: bố cục rộng, chừa khoảng trống để đặt chữ bên cạnh.",
            ImagePurpose.Social => "Ảnh đăng mạng xã hội: bắt mắt, màu sắc tươi, chủ thể nổi bật.",
            _ => null,
        };

        if (purposeHint is not null)
        {
            sb.Append("\n\n").Append(purposeHint);
        }

        if (!string.IsNullOrWhiteSpace(brandStyle))
        {
            sb.Append("\nPhong cách thương hiệu: ").Append(brandStyle.Trim());
        }

        sb.Append("\nKhông chèn chữ, logo hay watermark vào ảnh, trừ khi mô tả ở trên yêu cầu rõ.");

        return sb.ToString();
    }

    /// <summary>Tên file khi đưa vào thư viện media: dễ đọc, không dấu, không đụng nhau.</summary>
    public static string MediaFileName(DateTime createdAtUtc, int index, string extension) =>
        $"anh-ai-{createdAtUtc:yyyyMMdd-HHmmss}-{index + 1}.{extension.TrimStart('.')}";

    /// <summary>Alt mặc định khi người dùng không nhập: câu đầu của prompt, tối đa 200 ký tự.</summary>
    public static string DefaultAltText(string userPrompt)
    {
        string text = userPrompt.Trim().ReplaceLineEndings(" ");
        int stop = text.IndexOfAny(['.', '!', '?']);

        if (stop > 20)
        {
            text = text[..stop];
        }

        return text.Length <= 200 ? text : text[..200].TrimEnd() + "…";
    }
}
