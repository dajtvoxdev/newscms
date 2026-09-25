using AdVideo.Core.Enums;

namespace AdVideo.Core.Media;

/// <summary>Vị trí nhãn AI trên khung hình.</summary>
public enum LabelPosition
{
    TopLeft = 0,
    TopRight = 1,
    BottomLeft = 2,
    BottomRight = 3,
}

/// <summary>Đặc tả nhãn AI để FFmpeg vẽ. Core quyết định CÁI GÌ, Infrastructure quyết định VẼ thế nào.</summary>
public sealed record AiLabelSpec(
    string OverlayText,
    LabelPosition Position,
    int FontSizePx,
    double StartSeconds,
    double DurationSeconds,
    double Opacity,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>0 nghĩa là hiện suốt video.</summary>
    public bool IsPermanent => DurationSeconds <= 0;
}

/// <summary>
/// Bước gắn nhãn AI (D9). Nhãn là MỘT BƯỚC TRONG PIPELINE, không phải một tuỳ chọn.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lớp này cố ý không có tham số <c>enabled</c>, cũng không có overload nào bỏ qua nhãn.</b>
/// Đó không phải thiếu sót mà là thiết kế: Luật TTNT 2025 (hiệu lực 1/3/2026) đặt nghĩa vụ gắn
/// nhãn lên <b>bên triển khai</b> — tức là dịch vụ này — với mức phạt tới 2 tỉ đồng cho tổ chức,
/// và nghĩa vụ đó <b>không chuyển sang khách bằng điều khoản được</b>. Một cờ tắt là một cách
/// để ai đó vô tình vi phạm; không có cờ tắt thì không thể vô tình.
/// </para>
/// <para>
/// Hình thức nhãn cụ thể (chữ gì, hiện bao lâu, metadata trường nào) vẫn chờ pháp chế chốt —
/// đó là Open Item trong tài liệu yêu cầu và nó <b>chặn Sprint 6, không chặn Sprint 1</b>.
/// Nên nội dung mặc định ở đây là giá trị hợp lý tạm thời, đọc từ
/// <see cref="Entities.PromptTemplate"/>/<see cref="Entities.SystemSetting"/> để đổi không cần deploy.
/// </para>
/// <para>
/// Thuần hàm — test được 100% mà không cần FFmpeg.
/// </para>
/// </remarks>
public static class AiLabelStamper
{
    /// <summary>Nội dung mặc định khi chưa có cấu hình. Ngắn để đọc được trên màn hình điện thoại.</summary>
    public const string DefaultOverlayText = "Nội dung tạo bằng AI";

    /// <summary>
    /// Metadata mặc định ghi vào container.
    /// </summary>
    /// <remarks>
    /// Ghi cả metadata chứ không chỉ chữ trên hình: chữ trên hình người xem thấy, còn metadata
    /// là thứ nền tảng phân phối và công cụ kiểm tra đọc được. Thiếu một trong hai thì chưa đủ.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> DefaultMetadata =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["comment"] = "AI-generated content",
            ["AI-Generated"] = "true",
            ["AI-Generator"] = "AdVideo",
        };

    /// <summary>
    /// Kích thước chữ nhỏ nhất còn đọc được trên màn hình điện thoại, tính theo chiều rộng 1080 px.
    /// </summary>
    public const int MinReadableFontPx = 34;

    /// <summary>Độ mờ tối thiểu để chữ không bị nuốt bởi nền sáng.</summary>
    public const double MinOpacity = 0.75;

    /// <summary>
    /// Dựng đặc tả nhãn cho một video.
    /// </summary>
    /// <param name="ratio">Tỉ lệ khung — quyết định cỡ chữ và vị trí để không bị UI nền tảng che.</param>
    /// <param name="videoDurationSeconds">Độ dài video cuối, đo thật.</param>
    /// <param name="overlayText">Null thì dùng <see cref="DefaultOverlayText"/>.</param>
    /// <param name="metadata">Null thì dùng <see cref="DefaultMetadata"/>. Truyền vào để gộp thêm trường riêng.</param>
    public static AiLabelSpec Build(
        AspectRatio ratio,
        double videoDurationSeconds,
        string? overlayText = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (videoDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(videoDurationSeconds),
                "Độ dài video phải lớn hơn 0 — không gắn nhãn lên một video chưa đo được thời lượng.");
        }

        var (width, _) = ratio.ToResolution();
        var font = Math.Max(MinReadableFontPx, (int)Math.Round(width * 0.032));

        return new AiLabelSpec(
            OverlayText: string.IsNullOrWhiteSpace(overlayText) ? DefaultOverlayText : overlayText.Trim(),
            // TopLeft: góc ít bị UI của TikTok/Reels/Shorts che nhất. Các nền tảng đó đặt tương tác
            // ở cạnh phải và góc dưới phải, nên nhãn ở đó sẽ bị đè.
            Position: LabelPosition.TopLeft,
            FontSizePx: font,
            StartSeconds: 0,
            // Hiện suốt video. Hiện vài giây đầu rồi tắt thì người xem tua tới giữa sẽ không thấy.
            DurationSeconds: 0,
            Opacity: 0.92,
            Metadata: MergeMetadata(metadata));
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(IReadOnlyDictionary<string, string>? extra)
    {
        if (extra is null || extra.Count == 0) return DefaultMetadata;

        var merged = new Dictionary<string, string>(DefaultMetadata, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in extra)
        {
            merged[key] = value;
        }
        return merged;
    }

    /// <summary>
    /// QC bước 9 dùng hàm này. Nhãn không đạt thì FAIL JOB, không phải cảnh báo.
    /// </summary>
    /// <remarks>
    /// Trả về danh sách lý do thay vì bool: một video bị fail vì nhãn mà không nói rõ thiếu gì
    /// thì người vận hành phải tự đi đoán.
    /// </remarks>
    public static IReadOnlyList<string> Validate(AiLabelSpec? spec)
    {
        if (spec is null)
        {
            return ["Thiếu đặc tả nhãn AI. Video tạo bằng AI bắt buộc phải gắn nhãn — không có ngoại lệ và không tắt được."];
        }

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(spec.OverlayText))
            problems.Add("Nhãn AI có nội dung rỗng.");

        if (spec.FontSizePx < MinReadableFontPx)
            problems.Add($"Chữ nhãn AI {spec.FontSizePx}px nhỏ hơn mức đọc được tối thiểu {MinReadableFontPx}px trên màn hình điện thoại.");

        if (spec.Opacity < MinOpacity)
            problems.Add($"Độ mờ nhãn AI {spec.Opacity:0.##} thấp hơn mức tối thiểu {MinOpacity:0.##} — sẽ bị nuốt bởi nền sáng.");

        if (spec.DurationSeconds > 0)
            problems.Add("Nhãn AI chỉ hiện một phần video. Phải hiện suốt, vì người xem có thể tua tới giữa.");

        if (spec.Metadata.Count == 0)
            problems.Add("Thiếu metadata đánh dấu AI trong container file.");
        else if (!spec.Metadata.Any(kv =>
                     kv.Key.Contains("ai", StringComparison.OrdinalIgnoreCase) ||
                     kv.Value.Contains("ai", StringComparison.OrdinalIgnoreCase)))
            problems.Add("Metadata không có trường nào đánh dấu nội dung AI.");

        return problems;
    }
}
