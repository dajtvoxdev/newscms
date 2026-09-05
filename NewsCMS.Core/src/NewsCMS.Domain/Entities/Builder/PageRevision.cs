using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Builder;

/// <summary>
/// Bản snapshot của một trang tại thời điểm lưu/publish — dùng để khôi phục (restore revision).
/// Lưu toàn bộ trạng thái render được để rollback không phụ thuộc BuilderJson hiện tại.
/// </summary>
public class PageRevision : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid PageId { get; set; }

    /// <summary>Số phiên bản tăng dần theo page.</summary>
    public int Version { get; set; }

    public string? BuilderJson { get; set; }
    public string? CompiledHtml { get; set; }
    public string? CompiledCss { get; set; }
    public string? CustomCss { get; set; }
    public string? CustomJs { get; set; }

    /// <summary>Ghi chú của người lưu (ví dụ "publish lần đầu", "sửa hero").</summary>
    public string? Note { get; set; }
    public Guid? CreatedBy { get; set; }
}
