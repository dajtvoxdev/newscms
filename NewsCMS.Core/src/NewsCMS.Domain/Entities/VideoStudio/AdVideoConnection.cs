using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.VideoStudio;

/// <summary>
/// Kết nối tới dịch vụ AdVideo — MỘT dòng cho cả hệ thống, chỉ SuperAdmin sửa.
/// </summary>
/// <remarks>
/// Giữ <b>operator key</b> (<c>advop_…</c>): key mở toàn bộ API quản trị của AdVideo — nạp key
/// provider, đổi trần chi tiêu, tạo tenant. Mã hoá bằng DataProtection như <c>AiConnection</c>,
/// không bao giờ hiện lại ra màn hình.
/// </remarks>
public class AdVideoConnection : AuditableEntity
{
    /// <summary>Địa chỉ AdVideo.Api, ví dụ <c>http://127.0.0.1:5080</c>. Không có dấu / cuối.</summary>
    public string BaseUrl { get; set; } = default!;

    /// <summary>Operator key đã mã hoá. Null = chưa nhập.</summary>
    public string? OperatorKeyEncrypted { get; set; }

    /// <summary>12 ký tự đầu của key (phần không bí mật) — để nhận ra key nào đang dùng.</summary>
    public string? OperatorKeyPrefix { get; set; }

    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Site NewsCMS này là tenant nào bên AdVideo, kèm tenant key đã mã hoá.
/// </summary>
/// <remarks>
/// Tenant key được NewsCMS tự xin bằng operator key (<c>POST /v1/admin/tenants</c>) và cất thẳng
/// vào đây — không ai phải nhìn thấy hay chép dán nó. Site-scoped: người dùng của site A không
/// bao giờ đọc được dòng của site B qua global filter.
/// </remarks>
public class SiteAdVideoTenant : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    /// <summary>Id tenant bên AdVideo.</summary>
    public Guid AdVideoTenantId { get; set; }

    public string ApiKeyEncrypted { get; set; } = default!;

    /// <summary>Prefix key (không bí mật), khớp cột <c>api_key_prefix</c> bên AdVideo.</summary>
    public string ApiKeyPrefix { get; set; } = default!;

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
