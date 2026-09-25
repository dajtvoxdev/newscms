using AdVideo.Core.Common;

namespace AdVideo.Core.Entities;

/// <summary>Loại prompt. Quyết định chỗ nào trong pipeline dùng nó.</summary>
public enum PromptKind
{
    /// <summary>Negative prompt áp cho MỌI lời gọi video. Chống model vẽ chữ tiếng Việt sai dấu (D4).</summary>
    GlobalNegative = 0,

    /// <summary>Prompt hệ thống cho lớp đạo diễn LLM (bước 2, Sprint 2).</summary>
    Director = 1,

    /// <summary>Prompt theo định dạng, gắn với một <c>AdFormat</c>.</summary>
    Format = 2,

    /// <summary>Prompt cho bước QC đọc hình và phát hiện chữ lọt lưới (Sprint 5).</summary>
    QualityCheck = 3,
}

/// <summary>
/// Kho prompt là DỮ LIỆU, không phải code (D7).
/// </summary>
/// <remarks>
/// <para>
/// Thêm định dạng, sửa mô tả, nhân bản cho một khách — không việc nào được cần một lần deploy.
/// Người viết prompt nên là người biên tập nội dung, không phải dev; nếu prompt nằm trong code
/// thì mọi lần tinh chỉnh đều đi qua một chu trình release.
/// </para>
/// <para>
/// <b>Có version.</b> Video render sáu tháng trước phải tái hiện được bằng prompt của thời điểm
/// đó. Không version thì mỗi lần sửa prompt là mất khả năng giải thích vì sao một video cũ
/// trông như vậy — và mất khả năng rollback khi prompt mới tệ hơn.
/// </para>
/// </remarks>
public class PromptTemplate : AuditableEntity, ISoftDelete
{
    /// <summary>Mã ổn định, ví dụ <c>global.negative</c>. Không đổi theo thời gian.</summary>
    public required string Code { get; set; }

    /// <summary>Version tăng dần. Unique theo <c>(Code, Version)</c>.</summary>
    public required int Version { get; set; }

    public required PromptKind Kind { get; set; }

    /// <summary>Nội dung prompt. Có thể chứa placeholder <c>{{...}}</c> để lớp đạo diễn điền.</summary>
    public required string Content { get; set; }

    /// <summary>
    /// Chỉ một version active cho mỗi code.
    /// </summary>
    /// <remarks>
    /// Đảo cờ này là cách rollback prompt: đặt version cũ về active, không cần khôi phục dữ liệu.
    /// EF configuration phải có filtered unique index <c>(Code) WHERE IsActive = 1</c>.
    /// </remarks>
    public bool IsActive { get; set; }

    /// <summary>Mã định dạng mà prompt này phục vụ. Null cho prompt toàn cục.</summary>
    public string? FormatCode { get; set; }

    /// <summary>Ví dụ đầu vào/đầu ra để người biên tập biết prompt này định làm gì.</summary>
    public string? Example { get; set; }

    /// <summary>Vì sao version này thay version trước. Bắt buộc — prompt đổi mà không ghi lý do thì không ai dám đổi tiếp.</summary>
    public string? ChangeNote { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
