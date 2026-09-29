namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>Tham số vận hành của Xưởng ảnh — <c>appsettings</c> mục <c>ImageStudio</c>.</summary>
/// <remarks>
/// Chỉ những thứ thuộc về máy chủ nằm ở đây. Model, giá, hạn mức là dữ liệu sửa trên màn hình.
/// </remarks>
public sealed class ImageStudioOptions
{
    public const string SectionName = "ImageStudio";

    /// <summary>Số job chạy cùng lúc trên một tiến trình. Mỗi job lại gọi song song theo số biến thể.</summary>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>
    /// Cho phép adapter Fake (vẽ ảnh giả, không gọi mạng). Chỉ bật ở Development: bật ở production thì
    /// người dùng có thể chọn nhầm model giả.
    /// </summary>
    public bool EnableFakeProvider { get; set; }

    /// <summary>Ảnh không được chọn đưa vào thư viện bị dọn sau số ngày này.</summary>
    public int OutputRetentionDays { get; set; } = 14;

    /// <summary>Job ở trạng thái Running lâu hơn chừng này lúc khởi động thì coi như chết giữa chừng.</summary>
    public int StaleRunningMinutes { get; set; } = 15;
}
