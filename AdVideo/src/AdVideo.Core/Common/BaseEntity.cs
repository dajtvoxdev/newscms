namespace AdVideo.Core.Common;

/// <summary>
/// Nền cho mọi entity. Sao chép quy ước từ <c>NewsCMS.Domain.Common.BaseEntity</c>
/// để hai codebase trong cùng một repo đọc giống nhau.
/// </summary>
/// <remarks>
/// <c>UpdatedAt</c> là nullable và KHÔNG được đặt tự động trong constructor: giá trị
/// null nghĩa là "chưa từng sửa", khác với "sửa lúc đúng thời điểm tạo". EF configuration
/// của mỗi entity tự gán khi update.
/// </remarks>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Entity có vết người tạo/người sửa — cần cho việc truy trách nhiệm nội dung quảng cáo (R3).</summary>
public abstract class AuditableEntity : BaseEntity
{
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Soft delete. Dùng cho dữ liệu khách hàng nhìn thấy; KHÔNG dùng cho
/// <see cref="AdVideo.Core.Entities.ProviderCall"/> hay <see cref="AdVideo.Core.Entities.ConsentRecord"/>
/// — hai loại đó là bằng chứng kiểm toán và phải bất biến.
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

/// <summary>Đánh dấu entity thuộc về một khách hàng. Là mỏ neo cho global query filter chống rò dữ liệu cross-tenant.</summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
