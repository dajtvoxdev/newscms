using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Content;

/// <summary>
/// Theo dõi phiên upload lớn qua tus (endpoint /admin/media/tus).
/// TusFileId là id file trong TusDiskStore; MediaId được gán khi hoàn tất
/// (IMediaService.CreateFromUploadAsync + poster ffmpeg). Status:
/// "processing" (đang xử lý sau PATCH cuối) / "completed" / "failed".
/// Không ISiteScoped/không ISoftDelete: worker dọn dẹp phải quét xuyên site,
/// và row không mang payload nào của site.
/// </summary>
public class MediaUploadSession : BaseEntity
{
    public string TusFileId { get; set; } = default!;
    public Guid? MediaId { get; set; }
    public string Status { get; set; } = "processing";
    public string? Error { get; set; }
    public Guid UserId { get; set; }
    public long Size { get; set; }
    public string FileName { get; set; } = default!;
}
