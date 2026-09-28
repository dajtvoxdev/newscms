using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.VideoStudio;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.VideoStudio;

/// <summary>
/// Nơi DUY NHẤT mã hoá / giải mã key AdVideo trong NewsCMS.
/// </summary>
/// <remarks>
/// <para>
/// Hai "purpose" DataProtection riêng cho operator key và tenant key: bản mã của loại này không giải
/// mã được bằng protector của loại kia, nên một dòng dữ liệu bị chép nhầm cột không biến tenant key
/// thành quyền quản trị.
/// </para>
/// <para>
/// Tenant key đọc theo <paramref name="siteId"/> TƯỜNG MINH với <c>IgnoreQueryFilters</c>: trang
/// cấu hình của SuperAdmin thao tác trên site khác site đang chọn. Trang VideoStudio thì luôn truyền
/// site hiện tại.
/// </para>
/// </remarks>
public sealed class AdVideoKeyStore
{
    private readonly AppDbContext _db;
    private readonly IDataProtector _operatorProtector;
    private readonly IDataProtector _tenantProtector;

    public AdVideoKeyStore(AppDbContext db, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _operatorProtector = dataProtection.CreateProtector("NewsCMS.AdVideo.OperatorKey");
        _tenantProtector = dataProtection.CreateProtector("NewsCMS.AdVideo.TenantKey");
    }

    public string ProtectOperatorKey(string key) => _operatorProtector.Protect(key);

    public string ProtectTenantKey(string key) => _tenantProtector.Protect(key);

    public Task<AdVideoConnection?> GetConnectionAsync(CancellationToken ct) =>
        _db.AdVideoConnections.OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(ct);

    /// <summary>Địa chỉ + operator key. Lỗi = chuỗi hướng dẫn người vận hành phải làm gì.</summary>
    public async Task<(AdVideoEndpoint? Endpoint, string? Error)> GetOperatorEndpointAsync(CancellationToken ct)
    {
        AdVideoConnection? connection = await GetConnectionAsync(ct);

        if (connection is null || string.IsNullOrWhiteSpace(connection.BaseUrl))
        {
            return (null, "Chưa cấu hình kết nối AdVideo. Vào Cấu hình AdVideo, nhập địa chỉ và operator key.");
        }

        if (string.IsNullOrEmpty(connection.OperatorKeyEncrypted))
        {
            return (null, "Chưa nhập operator key AdVideo. Sinh key bằng lệnh create-operator-key trên máy chủ AdVideo rồi dán vào Cấu hình AdVideo.");
        }

        try
        {
            string key = _operatorProtector.Unprotect(connection.OperatorKeyEncrypted);

            return (new AdVideoEndpoint(connection.BaseUrl, key, AdVideoHttp.OperatorKeyHeader, connection.TimeoutSeconds), null);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return (null, "Không giải mã được operator key (key ring DataProtection đã đổi). Nhập lại operator key rồi lưu.");
        }
    }

    /// <summary>Địa chỉ + tenant key của một site.</summary>
    public async Task<(AdVideoEndpoint? Endpoint, string? Error)> GetTenantEndpointAsync(Guid siteId, CancellationToken ct)
    {
        AdVideoConnection? connection = await GetConnectionAsync(ct);

        if (connection is null || string.IsNullOrWhiteSpace(connection.BaseUrl))
        {
            return (null, "Hệ thống chưa kết nối dịch vụ video AdVideo. Liên hệ quản trị nền tảng.");
        }

        SiteAdVideoTenant? link = await _db.SiteAdVideoTenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.SiteId == siteId, ct);

        if (link is null)
        {
            return (null, "Site này chưa được kết nối với AdVideo. Quản trị nền tảng bật ở Cấu hình AdVideo → Site.");
        }

        try
        {
            string key = _tenantProtector.Unprotect(link.ApiKeyEncrypted);

            return (new AdVideoEndpoint(connection.BaseUrl, key, AdVideoHttp.TenantKeyHeader, connection.TimeoutSeconds), null);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return (null, "Không giải mã được key AdVideo của site (key ring đã đổi). Quản trị nền tảng cấp lại key ở Cấu hình AdVideo.");
        }
    }
}
