using AdVideo.Core.Common;

namespace AdVideo.Core.Entities;

/// <summary>
/// Chiến dịch của khách: một sản phẩm, nhiều video biến thể.
/// </summary>
/// <remarks>
/// Khách thường làm 5–10 biến thể cho cùng một sản phẩm (dọc cho Reels, vuông cho feed,
/// ngang cho YouTube). Gom vào project để tái dùng ảnh sản phẩm và brand kit thay vì
/// upload lại mỗi lần.
/// </remarks>
public class AdVideoProject : AuditableEntity, ITenantScoped, ISoftDelete
{
    public required Guid TenantId { get; set; }

    public required string Name { get; set; }

    /// <summary>Tên sản phẩm. Đưa vào prompt đạo diễn và vào từ điển phát âm.</summary>
    public string? ProductName { get; set; }

    /// <summary>Mô tả sản phẩm bằng tiếng Việt thường — đầu vào cho lớp đạo diễn ở Sprint 2.</summary>
    public string? Description { get; set; }

    /// <summary>Ngành hàng. Dùng để chọn <c>SceneEntry</c> và định dạng phù hợp (Sprint 3/6).</summary>
    public string? Industry { get; set; }

    /// <summary>Brand kit mặc định. Nullable tới Sprint 5.</summary>
    public Guid? DefaultBrandKitId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<AdVideoJob> Jobs { get; set; } = [];
}
