using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.ImageStudio;

/// <summary>
/// Một lần tạo hoặc sửa ảnh. Lưu trong DB trước khi xếp hàng — restart giữa chừng không làm mất job
/// (mất job là mất tiền đã trả cho provider).
/// </summary>
public class ImageJob : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    public ImageJobMode Mode { get; set; }

    public ImagePurpose Purpose { get; set; }

    public Guid ImageModelId { get; set; }

    /// <summary>Tên model lúc tạo — model có đổi tên hay bị xoá thì lịch sử vẫn đọc được.</summary>
    public string ModelName { get; set; } = default!;

    public Guid? TemplateId { get; set; }

    /// <summary>Prompt người dùng gõ (hoặc mẫu sau khi điền chỗ giữ).</summary>
    public string UserPrompt { get; set; } = default!;

    /// <summary>Prompt thật gửi đi (kèm phong cách site, chỉ dẫn vùng) — để truy vết và tạo lại.</summary>
    public string FinalPrompt { get; set; } = default!;

    /// <summary>Tỉ lệ người dùng chọn, ví dụ <c>16:9</c>.</summary>
    public string AspectRatio { get; set; } = "1:1";

    /// <summary>Giá trị gửi cho provider sau khi đổi theo model, ví dụ <c>1536x1024</c>.</summary>
    public string Size { get; set; } = default!;

    public string? Quality { get; set; }

    public int VariantCount { get; set; } = 1;

    // ── Sửa ảnh / ảnh tham chiếu (Đợt 3) ───────────────────────────────────────
    public Guid? SourceMediaId { get; set; }

    public Guid? SourceOutputId { get; set; }

    public string? ReferenceMediaIdsJson { get; set; }

    public string? RegionsJson { get; set; }

    public RegionStrategy Strategy { get; set; }

    public bool PreserveOutside { get; set; } = true;

    public string? MaskStorageKey { get; set; }

    public string? AnnotatedStorageKey { get; set; }

    public Guid? ParentJobId { get; set; }

    /// <summary>Người dùng xác nhận có quyền dùng ảnh tải lên để sửa.</summary>
    public bool RightsConfirmed { get; set; }

    // ── Ngữ cảnh mở modal ──────────────────────────────────────────────────────
    /// <summary><c>post</c>, <c>product</c>, <c>media</c> hoặc null (mở từ trang Xưởng ảnh).</summary>
    public string? ContextType { get; set; }

    public Guid? ContextId { get; set; }

    // ── Trạng thái ─────────────────────────────────────────────────────────────
    public ImageJobStatus Status { get; set; }

    /// <summary>Lỗi tiếng Việt cho người dùng, đã che key.</summary>
    public string? Error { get; set; }

    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public decimal EstimatedCostUsd { get; set; }

    public decimal CostUsd { get; set; }

    /// <summary>Sinh lúc mở form: bấm hai lần không tạo (và tính tiền) hai job. Duy nhất theo site.</summary>
    public string IdempotencyKey { get; set; } = default!;

    public ICollection<ImageJobOutput> Outputs { get; set; } = new List<ImageJobOutput>();
}

/// <summary>
/// Một ảnh job sinh ra. Chưa phải <c>Media</c>: chỉ khi người dùng bấm "Dùng ảnh này" mới đưa vào thư
/// viện (<see cref="PromotedMediaId"/>). Ảnh không được chọn bị dọn sau vài ngày.
/// </summary>
public class ImageJobOutput : BaseEntity
{
    public Guid JobId { get; set; }

    public ImageJob Job { get; set; } = default!;

    public int Index { get; set; }

    public string StorageKey { get; set; } = default!;

    public string Url { get; set; } = default!;

    public int Width { get; set; }

    public int Height { get; set; }

    public long Bytes { get; set; }

    public string MimeType { get; set; } = default!;

    public Guid? PromotedMediaId { get; set; }

    public DateTime? PromotedAt { get; set; }

    /// <summary>File đã bị dọn (ảnh không được chọn quá hạn giữ).</summary>
    public bool IsPurged { get; set; }
}

/// <summary>
/// Sổ gọi provider — nguồn sự thật về chi phí (như <c>ProviderCall</c> của AdVideo). Ghi cả lần lỗi.
/// </summary>
public class ImageProviderCall : BaseEntity
{
    public Guid? JobId { get; set; }

    public Guid SiteId { get; set; }

    public Guid ImageModelId { get; set; }

    public string ModelId { get; set; } = default!;

    /// <summary><c>generate</c>, <c>edit</c> hoặc <c>test</c>.</summary>
    public string Operation { get; set; } = default!;

    public int? HttpStatus { get; set; }

    public int DurationMs { get; set; }

    public int ImagesReturned { get; set; }

    public decimal CostUsd { get; set; }

    /// <summary><c>content_policy</c>, <c>rate_limited</c>, <c>timeout</c>, <c>bad_request</c>, <c>auth</c>, <c>provider_error</c>.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Đã che key và base64, cắt ngắn.</summary>
    public string? ErrorMessage { get; set; }
}
