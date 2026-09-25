using AdVideo.Core.Enums;

namespace AdVideo.Core.Storage;

/// <summary>Tên bucket. Hằng số để khỏi gõ nhầm — sai một ký tự là ghi nhầm bucket và lifecycle rule không áp.</summary>
public static class Buckets
{
    /// <summary>Ảnh khách upload. Giữ lâu dài.</summary>
    public const string Uploads = "adv-uploads";

    /// <summary>
    /// File trung gian: clip thô từng shot, audio, frame nối.
    /// </summary>
    /// <remarks>
    /// <b>Xoá sau 7 ngày.</b> Đây là bucket phình nhanh nhất — mỗi job đẻ 5–10 file trung gian
    /// mà khách không bao giờ xem. Lifecycle rule phải bật NGAY từ Sprint 1, không phải "để sau":
    /// bật sau nghĩa là phải dọn tay đống file đã tích tụ.
    /// </remarks>
    public const string Work = "adv-work";

    /// <summary>Video giao khách. Sang lớp lạnh sau 90 ngày.</summary>
    public const string Final = "adv-final";

    /// <summary>File giọng đọc và mốc thời gian.</summary>
    public const string Voice = "adv-voice";

    /// <summary>Bucket nên dùng cho một loại asset. Video cuối và thumbnail đi theo video cuối.</summary>
    public static string ForKind(AssetKind kind) => kind switch
    {
        AssetKind.ProductImage or AssetKind.ReferenceImage => Uploads,
        AssetKind.VoiceAudio => Voice,
        AssetKind.FinalVideo or AssetKind.Thumbnail or AssetKind.Subtitle => Final,
        _ => Work,
    };
}

/// <summary>Kết quả upload.</summary>
public sealed record StorageUploadResult(string Bucket, string ObjectKey, long SizeBytes, string ETag);

/// <summary>
/// Trừu tượng hoá object storage (MinIO qua S3 protocol).
/// </summary>
/// <remarks>
/// Trả về <c>(bucket, objectKey)</c> chứ không phải URL: URL presigned có thời hạn, còn object key
/// thì không. Lưu URL vào DB là lưu một thứ sẽ chết.
/// </remarks>
public interface IStorageService
{
    Task<StorageUploadResult> UploadAsync(
        string bucket, string objectKey, Stream content, string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>Tải toàn bộ nội dung vào bộ nhớ. Chỉ dùng cho file nhỏ (audio, JSON) — KHÔNG dùng cho video.</summary>
    Task<byte[]> DownloadBytesAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sinh URL tải về có thời hạn.
    /// </summary>
    /// <remarks>
    /// Hai bẫy MinIO đã biết: <b>lệch giờ máy chủ</b> làm chữ ký hết hạn sớm (bật NTP), và
    /// <b>thiếu CORS</b> làm trình duyệt thất bại IM LẶNG (F12 mới thấy). Cả hai phải cấu hình
    /// từ Sprint 1, không đợi tới Sprint 4 khi có UI.
    /// </remarks>
    Task<Uri> GetPresignedUrlAsync(string bucket, string objectKey, TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>Tạo bucket nếu chưa có, kèm lifecycle rule. Idempotent — chạy mỗi lần khởi động.</summary>
    Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>Dựng khoá object theo quy ước <c>{tenant}/{jobId}/{kind}/{filename}</c>.</summary>
    static string BuildKey(Guid tenantId, Guid? jobId, AssetKind kind, string filename) =>
        jobId is { } job
            ? $"{tenantId:N}/{job:N}/{kind.ToString().ToLowerInvariant()}/{filename}"
            : $"{tenantId:N}/unassigned/{kind.ToString().ToLowerInvariant()}/{filename}";
}
