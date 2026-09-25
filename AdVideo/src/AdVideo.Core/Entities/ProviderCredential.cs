using AdVideo.Core.Common;
using AdVideo.Core.Providers;

namespace AdVideo.Core.Entities;

/// <summary>Phạm vi của một bộ credential.</summary>
public enum CredentialScope
{
    /// <summary>Key của nền tảng, dùng cho mọi tenant.</summary>
    System = 0,

    /// <summary>
    /// Key khách tự mang (BYO). Sprint 5.
    /// </summary>
    /// <remarks>
    /// Có scope này trong mô hình ngay từ Sprint 1 để khỏi phải đổi schema về sau: khách mang
    /// key riêng thì chi phí provider đi thẳng vào hoá đơn của họ, và nền tảng không phải
    /// gánh trần rate limit chung.
    /// </remarks>
    Tenant = 1,
}

/// <summary>
/// Credential + capability của một provider/model. Đây là nơi D10 hiện thực: key, model id,
/// endpoint và capability đều nằm trong DB, sửa không cần deploy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Key lưu MÃ HOÁ tại chỗ</b> bằng ASP.NET Core Data Protection, qua <c>ValueConverter</c>
/// ở tầng Infrastructure — entity này không biết gì về mã hoá. Một lần rò DB dump không được
/// phép đồng nghĩa với rò key.
/// </para>
/// <para>
/// Pattern này mirror <c>SiteApiKeyService</c> sẵn có trong NewsCMS (cùng dùng
/// <c>IDataProtector</c> với purpose string riêng).
/// </para>
/// </remarks>
public class ProviderCredential : AuditableEntity, ISoftDelete
{
    /// <summary>Tên provider, khớp <see cref="ProviderNames"/>.</summary>
    public required string Provider { get; set; }

    /// <summary>Model id gửi lên API.</summary>
    public required string ModelId { get; set; }

    public required ProviderCategory Category { get; set; }

    public CredentialScope Scope { get; set; } = CredentialScope.System;

    /// <summary>Bắt buộc khi <see cref="Scope"/> = Tenant. Null cho credential hệ thống.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Endpoint. Để trống thì adapter dùng endpoint mặc định của provider.</summary>
    public string? EndpointUrl { get; set; }

    /// <summary>
    /// API key ĐÃ MÃ HOÁ. Không bao giờ log, không bao giờ trả nguyên văn ra API.
    /// </summary>
    /// <remarks>Endpoint đọc credential chỉ được trả 4 ký tự cuối để người vận hành nhận ra key nào.</remarks>
    public required string EncryptedApiKey { get; set; }

    /// <summary>
    /// Capability serialised JSON — <see cref="VideoProviderCapability"/> hoặc <see cref="TtsProviderCapability"/>.
    /// </summary>
    /// <remarks>
    /// Lưu JSON thay vì một cột mỗi trường: provider thêm một khả năng mới thì không cần migration.
    /// EF Core 8 map bằng <c>ToJson()</c>.
    /// </remarks>
    public required string CapabilityJson { get; set; }

    /// <summary>Đang bật không. Tắt một provider chết nhanh hơn nhiều so với deploy một bản vá.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Thứ tự ưu tiên khi nhiều credential cùng phục vụ một yêu cầu. Nhỏ hơn = ưu tiên hơn.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>Ngày hết hạn credit/quyền lợi. Hết hạn thì tự tắt để khỏi fail giữa job.</summary>
    public DateTime? CreditExpiresAt { get; set; }

    /// <summary>Trần chi tiêu đô la mỗi ngày cho riêng credential này. Null = theo trần toàn hệ thống.</summary>
    public decimal? DailyCostLimitUsd { get; set; }

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
