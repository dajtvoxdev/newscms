using AdVideo.Core.Common;
using AdVideo.Core.Enums;

namespace AdVideo.Core.Entities;

/// <summary>
/// Một file trong object storage. Ảnh vào, clip ra, audio, video cuối, thumbnail, phụ đề.
/// </summary>
/// <remarks>
/// <b>Không bao giờ lưu đường dẫn tuyệt đối trên ổ đĩa.</b> Chỉ lưu <c>(bucket, objectKey)</c>:
/// worker và API có thể chạy trên hai máy khác nhau, và MinIO có thể dời endpoint. Lưu đường
/// dẫn local là cách chắc chắn nhất để video "mất tích" sau một lần deploy.
///
/// <b>Không lưu URL của provider.</b> Veo giữ file trên server Google khoảng 2 ngày rồi xoá.
/// Mọi file phải được tải về MinIO ngay khi provider trả kết quả.
/// </remarks>
public class MediaAsset : AuditableEntity, ITenantScoped, ISoftDelete
{
    public required Guid TenantId { get; set; }

    /// <summary>Null cho asset chưa gắn job (ảnh khách upload trước khi tạo job).</summary>
    public Guid? JobId { get; set; }

    public required AssetKind Kind { get; set; }

    /// <summary>Tên bucket: <c>adv-uploads</c>, <c>adv-work</c>, <c>adv-final</c>, <c>adv-voice</c>.</summary>
    /// <remarks>
    /// Bucket quyết định vòng đời: <c>adv-work</c> bị xoá sau 7 ngày (mỗi job đẻ 5–10 file trung
    /// gian, đây là bucket phình nhanh nhất), <c>adv-final</c> sang lớp lạnh sau 90 ngày.
    /// </remarks>
    public required string Bucket { get; set; }

    /// <summary>Khoá object, dạng <c>{tenant}/{jobId}/{kind}/{filename}</c>.</summary>
    public required string ObjectKey { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 của nội dung. Để phát hiện upload trùng và để chứng minh file không bị đổi.</summary>
    public string? ChecksumSha256 { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }

    /// <summary>File có track audio không — đo bằng ffprobe, không phải đọc từ metadata provider.</summary>
    public bool HasAudio { get; set; }

    /// <summary>Đã quét QC chưa (bước 9).</summary>
    public bool IsQcPassed { get; set; }

    /// <summary>Asset gốc mà asset này sinh ra — ví dụ thumbnail sinh từ video cuối.</summary>
    public Guid? DerivedFromAssetId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public AdVideoJob? Job { get; set; }
}
