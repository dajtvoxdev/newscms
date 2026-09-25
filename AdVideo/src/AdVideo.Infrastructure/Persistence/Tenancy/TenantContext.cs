namespace AdVideo.Infrastructure.Persistence.Tenancy;

/// <summary>
/// Tenant của luồng thực thi hiện tại. Là mỏ neo cho global query filter chống rò dữ liệu
/// cross-tenant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao cần cờ <see cref="BypassFilters"/>:</b> worker Hangfire chạy nền, không có HTTP
/// request nên không có API key để suy ra tenant. Nếu để filter áp dụng bình thường thì worker
/// không nhìn thấy chính cái job nó đang chạy. Bypass rồi lọc tường minh theo <c>job.TenantId</c>
/// là cách duy nhất đúng — và phải ghi rõ ở chỗ gọi, không phải âm thầm.
/// </para>
/// <para>
/// <b>Fail-closed có chủ đích:</b> khi <see cref="TenantId"/> là null và không bypass, filter so
/// sánh <c>Guid</c> với <c>null</c> nên không dòng nào khớp. Nghĩa là quên set tenant thì thấy
/// dữ liệu RỖNG, chứ không phải thấy dữ liệu của tenant khác. Lỗi rỗng thì phát hiện ngay khi
/// test; lỗi rò thì phát hiện khi có sự cố.
/// </para>
/// </remarks>
public interface ITenantContext
{
    /// <summary>Tenant hiện tại. Null khi chưa xác thực hoặc khi worker chưa set.</summary>
    Guid? TenantId { get; }

    /// <summary>Tắt global query filter tenant. Chỉ worker được bật, và phải lọc tay sau đó.</summary>
    bool BypassFilters { get; set; }

    void SetTenant(Guid tenantId);
}

/// <summary>Cài đặt theo phạm vi scoped — mỗi request/mỗi job scope một instance.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public bool BypassFilters { get; set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;
}

/// <summary>
/// Cài đặt singleton dùng khi không có scope nào — ví dụ design-time factory lúc chạy migration.
/// </summary>
/// <remarks>
/// Trả <see cref="BypassFilters"/> = true vì migration và seeding phải chạm được mọi tenant.
/// Không bao giờ đăng ký cái này cho request pipeline.
/// </remarks>
public sealed class NullTenantContext : ITenantContext
{
    public Guid? TenantId => null;

    public bool BypassFilters { get; set; } = true;

    public void SetTenant(Guid tenantId) { }
}
