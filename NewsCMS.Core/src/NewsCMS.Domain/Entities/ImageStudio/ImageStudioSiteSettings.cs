using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.ImageStudio;

/// <summary>
/// Xưởng ảnh của một site: bật/tắt và hạn mức (quản trị nền tảng đặt), phong cách thương hiệu
/// (quản trị site đặt). Site chưa có dòng nào = chưa bật.
/// </summary>
/// <remarks>
/// Chưa bật là mặc định vì tạo ảnh tốn tiền thật: quản trị nền tảng chủ động bật cho từng site,
/// như cách kết nối site với AdVideo.
/// </remarks>
public class ImageStudioSiteSettings : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Số ảnh tối đa mỗi tháng (tính theo giờ UTC). 0 = không giới hạn.</summary>
    public int MonthlyImageQuota { get; set; } = 300;

    /// <summary>Số ảnh tối đa mỗi người mỗi ngày. 0 = không giới hạn.</summary>
    public int PerUserDailyQuota { get; set; } = 50;

    /// <summary>Nối vào cuối mọi prompt, ví dụ "tông xanh lá, ánh sáng tự nhiên, tối giản".</summary>
    public string? BrandStyle { get; set; }

    public string CoverAspect { get; set; } = "16:9";

    public string ProductAspect { get; set; } = "1:1";

    public bool ShowAiCaption { get; set; } = true;

    public string AiCaptionText { get; set; } = "Ảnh minh hoạ tạo bởi AI";
}
