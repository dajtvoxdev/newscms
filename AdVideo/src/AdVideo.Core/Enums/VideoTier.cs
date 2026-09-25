namespace AdVideo.Core.Enums;

/// <summary>
/// Chất lượng khách chọn. Khách KHÔNG chọn provider — xem Luật 3 trong tài liệu thiết kế:
/// "đừng bắt người làm marketing phải biết Veo là gì". API nhận tier + "có người xuất hiện không",
/// hệ thống tự suy ra provider.
/// </summary>
public enum VideoTier
{
    /// <summary>Nhanh, rẻ, model nhẹ. Mục tiêu trả kết quả trong ~2 phút.</summary>
    Draft = 0,

    Standard = 1,

    /// <summary>Model tốt nhất, giá cao nhất.</summary>
    Premium = 2,
}
