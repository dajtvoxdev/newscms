using System.Text.Json;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Persistence.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Persistence.Stores;

/// <summary>
/// Đọc credential và manifest capability từ DB (D10). Key được value converter của EF giải mã khi
/// đọc lên, nên ở đây chỉ còn plaintext trong bộ nhớ, không bao giờ trên đĩa.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cache rất ngắn (30 giây):</b> tắt một provider đang hỏng là thao tác khẩn cấp. Cache dài thì
/// người vận hành tắt xong vẫn thấy job tiếp tục gọi vào provider chết, sẽ tắt lần nữa, rồi nghi
/// ngờ là lệnh không ăn.
/// </para>
/// <para>
/// <b>Cache chứa key dạng plaintext.</b> Đây là đánh đổi có chủ đích: giải mã Data Protection mỗi
/// lần gọi provider là tốn CPU vô ích, còn key thì dù sao cũng phải nằm trong RAM lúc gọi HTTP.
/// Nhưng cũng vì vậy <see cref="ResolvedCredential"/> mới ghi đè <c>ToString()</c> — để không có
/// đường nào key lọt vào log.
/// </para>
/// </remarks>
public sealed class DbCredentialStore : ICredentialStore
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly AdVideoDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly StoreCacheSignal<DbCredentialStore> _signal;
    private readonly ILogger<DbCredentialStore> _logger;

    public DbCredentialStore(
        AdVideoDbContext db,
        IMemoryCache cache,
        StoreCacheSignal<DbCredentialStore> signal,
        ILogger<DbCredentialStore> logger)
    {
        _db = db;
        _cache = cache;
        _signal = signal;
        _logger = logger;
    }

    public async Task<ResolvedCredential?> GetAsync(
        string provider,
        ProviderCategory category,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ResolvedCredential> candidates = await ListActiveAsync(category, tenantId, cancellationToken);

        return candidates.FirstOrDefault(x =>
            string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<ResolvedCredential>> ListActiveAsync(
        ProviderCategory category,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = $"advideo:cred:{category}:{tenantId?.ToString() ?? "system"}";

        IReadOnlyList<ResolvedCredential>? cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.AddExpirationToken(_signal.Token);

            return await LoadActiveAsync(category, tenantId, cancellationToken);
        });

        return cached ?? [];
    }

    public async Task<VideoProviderCapability?> GetVideoCapabilityAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VideoProviderCapability> all = await GetAllVideoCapabilitiesAsync(cancellationToken);

        return all.FirstOrDefault(x =>
            string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<TtsProviderCapability?> GetTtsCapabilityAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        List<CapabilityRow> rows = await LoadCapabilityRowsAsync(ProviderCategory.TextToSpeech, cancellationToken);

        CapabilityRow? row = rows.FirstOrDefault(x =>
            string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));

        return row is null ? null : Deserialize<TtsProviderCapability>(row);
    }

    public async Task<IReadOnlyList<VideoProviderCapability>> GetAllVideoCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        List<CapabilityRow> rows = await LoadCapabilityRowsAsync(ProviderCategory.Video, cancellationToken);

        // Dòng hỏng bị bỏ qua chứ không ném lỗi: một manifest sai cú pháp chỉ được phép loại chính
        // provider đó ra khỏi danh sách chọn, không được làm mọi job dừng. Lỗi đã có log trong
        // Deserialize để người vận hành biết dòng nào phải sửa.
        List<VideoProviderCapability> result = [];

        foreach (CapabilityRow row in rows)
        {
            VideoProviderCapability? capability = Deserialize<VideoProviderCapability>(row);

            if (capability is not null)
            {
                result.Add(capability);
            }
        }

        return result;
    }

    public async Task UpsertAsync(
        ProviderCredential credential,
        string plainApiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        ProviderCredential? existing = await _db.ProviderCredentials
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.Provider == credential.Provider
                    && x.ModelId == credential.ModelId
                    && x.Category == credential.Category
                    && x.TenantId == credential.TenantId,
                cancellationToken);

        // Gán plaintext thẳng vào thuộc tính: value converter của EF mã hoá lúc ghi. Ở đây không
        // có lệnh mã hoá nào, và đó là chủ đích — xem EncryptedStringConverter.
        if (existing is null)
        {
            credential.EncryptedApiKey = plainApiKey;
            _db.ProviderCredentials.Add(credential);
        }
        else
        {
            existing.EndpointUrl = credential.EndpointUrl;
            existing.CapabilityJson = credential.CapabilityJson;
            existing.IsActive = credential.IsActive;
            existing.Priority = credential.Priority;
            existing.CreditExpiresAt = credential.CreditExpiresAt;
            existing.DailyCostLimitUsd = credential.DailyCostLimitUsd;
            existing.Notes = credential.Notes;
            existing.Scope = credential.Scope;
            existing.IsDeleted = false;
            existing.DeletedAt = null;

            // Chuỗi rỗng nghĩa là "giữ key cũ": lệnh set-credential hay được dùng để sửa capability
            // hoặc bật/tắt provider, và bắt gõ lại key mỗi lần sửa là cách chắc chắn nhất để key
            // nằm lại trong lịch sử shell.
            if (!string.IsNullOrEmpty(plainApiKey))
            {
                existing.EncryptedApiKey = plainApiKey;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        Invalidate();
    }

    public void Invalidate() => _signal.Reset();

    private async Task<IReadOnlyList<ResolvedCredential>> LoadActiveAsync(
        ProviderCategory category,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;

        List<ProviderCredential> rows = await _db.ProviderCredentials
            .AsNoTracking()
            .Where(x => x.Category == category
                && x.IsActive
                && (x.CreditExpiresAt == null || x.CreditExpiresAt > now)
                && (x.Scope == CredentialScope.System || x.TenantId == tenantId))
            .ToListAsync(cancellationToken);

        // Sắp xếp trong bộ nhớ vì quy tắc "key của khách đè key nền tảng" không diễn đạt gọn bằng
        // SQL, và danh sách này luôn chỉ vài dòng.
        return rows
            .OrderByDescending(x => x.Scope == CredentialScope.Tenant)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(Resolve)
            .ToList();
    }

    private static ResolvedCredential Resolve(ProviderCredential row)
    {
        // row.EncryptedApiKey lúc này đã là plaintext — tên thuộc tính nói về dạng lưu trong DB,
        // không phải dạng đang cầm trên tay.
        return new ResolvedCredential(
            row.Provider,
            row.ModelId,
            row.Category,
            row.EndpointUrl ?? string.Empty,
            row.EncryptedApiKey,
            row.Scope,
            row.TenantId,
            row.Priority);
    }

    private async Task<List<CapabilityRow>> LoadCapabilityRowsAsync(
        ProviderCategory category,
        CancellationToken cancellationToken)
    {
        string cacheKey = $"advideo:cap:{category}";

        List<CapabilityRow>? cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.AddExpirationToken(_signal.Token);

            // Chỉ lấy hai cột: manifest không cần key, và không đọc cột key lên thì không có gì để
            // giải mã — tránh cả chi phí lẫn nguy cơ ném AdVideoSecretException ở một đường hoàn
            // toàn không liên quan đến xác thực.
            return await _db.ProviderCredentials
                .AsNoTracking()
                .Where(x => x.Category == category && x.IsActive)
                .OrderBy(x => x.Priority)
                .Select(x => new CapabilityRow(x.Provider, x.CapabilityJson))
                .ToListAsync(cancellationToken);
        });

        return cached ?? [];
    }

    private T? Deserialize<T>(CapabilityRow row) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(row.Json, AdVideoJson.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "CapabilityJson của provider {Provider} không đọc được thành {Type}; provider này bị loại khỏi danh sách chọn cho tới khi sửa dòng tương ứng trong bảng ProviderCredentials.",
                row.Provider,
                typeof(T).Name);

            return null;
        }
    }

    private sealed record CapabilityRow(string Provider, string Json);
}
