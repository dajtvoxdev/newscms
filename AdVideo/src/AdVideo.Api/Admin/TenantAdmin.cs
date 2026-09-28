using AdVideo.Core.Entities;
using AdVideo.Core.Security;
using AdVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Api.Admin;

/// <summary>Tenant vừa tạo hoặc vừa cấp key mới, kèm key gốc — lần DUY NHẤT key tồn tại ở dạng đọc được.</summary>
public sealed record IssuedTenantKey(Tenant Tenant, string ApiKey);

/// <summary>
/// Tạo tenant, cấp lại key, bật/tắt tenant — dùng chung cho CLI và <c>/v1/admin/tenants</c>.
/// </summary>
public sealed class TenantAdmin
{
    private readonly AdVideoDbContext _db;

    public TenantAdmin(AdVideoDbContext db) => _db = db;

    public async Task<IssuedTenantKey> CreateAsync(string name, string? note, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string apiKey = ApiKeyHasher.Generate();

        var tenant = new Tenant
        {
            Name = name.Trim(),
            ApiKeyPrefix = ApiKeyHasher.LookupPrefix(apiKey),
            ApiKeyHash = ApiKeyHasher.Hash(apiKey),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(cancellationToken);

        return new IssuedTenantKey(tenant, apiKey);
    }

    /// <summary>Cấp key mới. Key cũ mất hiệu lực NGAY — không có thời gian chuyển tiếp. Null = không có tenant.</summary>
    public async Task<IssuedTenantKey?> RotateKeyAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        Tenant? tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            return null;
        }

        string apiKey = ApiKeyHasher.Generate();

        tenant.ApiKeyPrefix = ApiKeyHasher.LookupPrefix(apiKey);
        tenant.ApiKeyHash = ApiKeyHasher.Hash(apiKey);
        tenant.ApiKeyRotatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new IssuedTenantKey(tenant, apiKey);
    }

    /// <summary>
    /// Bật/tắt tenant, ghi lý do vào ghi chú. Null = không có tenant.
    /// </summary>
    /// <remarks>
    /// Có hiệu lực ở request kế tiếp: <c>ApiKeyAuthHandler</c> không cache kết quả tra key. Job đang
    /// chạy của tenant bị tắt vẫn chạy nốt — tắt tenant là chặn việc MỚI, không phải huỷ việc đã
    /// nhận tiền.
    /// </remarks>
    public async Task<Tenant?> SetActiveAsync(Guid tenantId, bool isActive, string? reason, CancellationToken cancellationToken = default)
    {
        Tenant? tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            return null;
        }

        tenant.IsActive = isActive;

        if (!string.IsNullOrWhiteSpace(reason))
        {
            string entry = $"{DateTime.UtcNow:yyyy-MM-dd} {(isActive ? "bật" : "tắt")}: {reason.Trim()}";
            tenant.Note = string.IsNullOrWhiteSpace(tenant.Note) ? entry : $"{tenant.Note}\n{entry}";

            if (tenant.Note.Length > 1000)
            {
                // Giữ phần MỚI nhất: cột có trần 1000 ký tự, và lý do gần đây là thứ người vận hành cần đọc.
                tenant.Note = tenant.Note[^1000..];
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return tenant;
    }
}
