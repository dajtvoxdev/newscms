using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.ImageStudio;

/// <summary>
/// Một model tạo ảnh do quản trị nền tảng cấu hình sẵn. Người dùng chọn một trong các model đang bật
/// ở form tạo ảnh — giống chọn giọng đọc ở VideoStudio.
/// </summary>
/// <remarks>
/// <para>
/// <b>Model id, giá, kích thước là dữ liệu, không phải code.</b> Nhà cung cấp đổi tên model hay giá
/// thì sửa một dòng ở màn hình, không deploy.
/// </para>
/// <para>
/// Key không nằm ở đây: dùng lại <c>AiConnection</c> (đã mã hoá DataProtection). 9Router dùng một key
/// cho cả chat lẫn ảnh. Chat chỉ dùng kết nối <i>mặc định</i>, nên kết nối riêng cho ảnh không ảnh hưởng chat.
/// </para>
/// <para>Dùng chung mọi site — không site-scoped.</para>
/// </remarks>
public class ImageModel : AuditableEntity, ISoftDelete
{
    /// <summary>Tên hiện cho người dùng, ví dụ "GPT Image — chất lượng cao".</summary>
    public string Name { get; set; } = default!;

    /// <summary>Một câu giúp người dùng chọn: "Nhanh, rẻ — hợp ảnh minh hoạ trong bài".</summary>
    public string? Description { get; set; }

    public Guid ConnectionId { get; set; }

    public ImageProviderAdapter Adapter { get; set; }

    /// <summary>Model id gửi cho nhà cung cấp, ví dụ <c>gpt-image-1</c>.</summary>
    public string ModelId { get; set; } = default!;

    public ImageCapabilities Capabilities { get; set; } = ImageCapabilities.TextToImage;

    public int MaxReferenceImages { get; set; }

    public MaskConvention MaskConvention { get; set; }

    /// <summary>
    /// Kích thước model nhận, mỗi dòng một giá trị: <c>1024x1024</c> (pixel) hoặc <c>16:9</c> (tỉ lệ).
    /// Form chọn tỉ lệ khung; server đổi sang giá trị gần nhất trong danh sách này.
    /// </summary>
    public string SupportedSizes { get; set; } = "1024x1024\n1536x1024\n1024x1536";

    public int MaxVariants { get; set; } = 4;

    /// <summary>Mức chất lượng gửi đi (<c>low</c>/<c>medium</c>/<c>high</c>/<c>auto</c>). Null = không gửi.</summary>
    public string? Quality { get; set; }

    /// <summary><c>png</c>, <c>jpeg</c> hoặc <c>webp</c>.</summary>
    public string OutputFormat { get; set; } = "png";

    /// <summary>Giá ước tính mỗi ảnh — hiện trước khi bấm tạo. Chi phí thật ghi ở <see cref="ImageProviderCall"/>.</summary>
    public decimal PricePerImageUsd { get; set; }

    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>JSON object trộn thêm vào body request (tham số riêng của nhà cung cấp).</summary>
    public string? ExtraParamsJson { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Chọn sẵn ở form tạo ảnh. Tối đa một model.</summary>
    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public DateTime? LastTestedAt { get; set; }

    public bool? LastTestOk { get; set; }

    public string? LastTestError { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }
}
