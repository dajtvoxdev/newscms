using System.Security.Cryptography;
using System.Text;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers.Descriptors;
using AdVideo.Infrastructure.Persistence.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Persistence.Stores;

/// <summary>
/// Descriptor provider khai báo trong DB, có phiên bản — khuôn <see cref="DbPromptStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Kiểm lại lúc ĐỌC, không chỉ lúc ghi.</b> Validator có thể chặt hơn theo thời gian; một dòng
/// cũ không còn hợp lệ thì provider biến khỏi danh sách kèm cảnh báo, thay vì chạy bằng một mô tả
/// mà phiên bản code hiện tại coi là nguy hiểm.
/// </para>
/// <para>
/// Cache ngắn (30 giây) như credential: tắt một descriptor hỏng là thao tác khẩn cấp.
/// </para>
/// </remarks>
public sealed class DbDescriptorStore : IDescriptorStore
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly AdVideoDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly StoreCacheSignal<DbDescriptorStore> _signal;
    private readonly StoreCacheSignal<DbCredentialStore> _credentialSignal;
    private readonly ILogger<DbDescriptorStore> _logger;

    public DbDescriptorStore(
        AdVideoDbContext db,
        IMemoryCache cache,
        StoreCacheSignal<DbDescriptorStore> signal,
        StoreCacheSignal<DbCredentialStore> credentialSignal,
        ILogger<DbDescriptorStore> logger)
    {
        _db = db;
        _cache = cache;
        _signal = signal;
        _credentialSignal = credentialSignal;
        _logger = logger;
    }

    /// <summary>SHA-256 hex thường của chuỗi UTF-8.</summary>
    public static string Sha256Of(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    public async Task<ActiveDescriptor?> GetActiveAsync(string provider, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return await _cache.GetOrCreateAsync($"advideo:descriptor:{provider}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.AddExpirationToken(_signal.Token);

            ProviderDescriptorRow? row = await _db.ProviderDescriptors
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Code == provider && x.IsActive, cancellationToken);

            if (row is null)
            {
                return null;
            }

            DescriptorParseResult parsed = ProviderDescriptorParser.Parse(row.Json);

            if (!parsed.IsValid)
            {
                _logger.LogError(
                    "Descriptor {Provider} v{Version} đang bật nhưng không còn hợp lệ — provider bị bỏ qua: {Errors}",
                    provider,
                    row.Version,
                    string.Join(" | ", parsed.Errors));

                return null;
            }

            return new ActiveDescriptor(parsed.Descriptor!, row.Version, row.Sha256);
        });
    }

    public async Task<IReadOnlyList<ProviderDescriptorRow>> ListAsync(
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ProviderDescriptorRow> query = _db.ProviderDescriptors.AsNoTracking();

        if (provider is not null)
        {
            query = query.Where(x => x.Code == provider);
        }

        return await query
            .OrderBy(x => x.Code)
            .ThenByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProviderDescriptorRow> AddVersionAsync(
        string json,
        string? changeNote,
        CancellationToken cancellationToken = default)
    {
        DescriptorParseResult parsed = ProviderDescriptorParser.Parse(json);

        if (!parsed.IsValid)
        {
            throw new DescriptorInvalidException(parsed.Errors);
        }

        ProviderDescriptor descriptor = parsed.Descriptor!;

        // IgnoreQueryFilters: bản đã xoá mềm vẫn giữ số thứ tự, để hai nội dung khác nhau không
        // bao giờ mang cùng một số hiệu trong nhật ký.
        int lastVersion = await _db.ProviderDescriptors
            .IgnoreQueryFilters()
            .Where(x => x.Code == descriptor.Name)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken) ?? 0;

        var row = new ProviderDescriptorRow
        {
            Code = descriptor.Name,
            Version = lastVersion + 1,
            Kind = descriptor.Kind,
            Json = json,
            Sha256 = Sha256Of(json),
            ChangeNote = changeNote,
            IsActive = false,
        };

        _db.ProviderDescriptors.Add(row);
        await _db.SaveChangesAsync(cancellationToken);

        return row;
    }

    public async Task ActivateVersionAsync(string provider, int version, CancellationToken cancellationToken = default)
    {
        ProviderDescriptorRow target = await _db.ProviderDescriptors
            .FirstOrDefaultAsync(x => x.Code == provider && x.Version == version, cancellationToken)
            ?? throw new InvalidOperationException($"Không tìm thấy descriptor {provider} bản {version}.");

        DescriptorParseResult parsed = ProviderDescriptorParser.Parse(target.Json);

        if (!parsed.IsValid)
        {
            throw new DescriptorInvalidException(parsed.Errors);
        }

        int credentialCount = 0;

        // Transaction PHẢI chạy trong execution strategy: DbContext bật EnableRetryOnFailure, và với
        // chiến lược đó SQL Server ném lỗi ngay khi tự mở transaction ở ngoài. SQLite trong test
        // không có chiến lược retry nên không bao giờ thấy lỗi này.
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction =
                _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;

            List<ProviderDescriptorRow> currentlyActive = await _db.ProviderDescriptors
                .Where(x => x.Code == provider && x.IsActive && x.Id != target.Id)
                .ToListAsync(cancellationToken);

            List<ProviderCredential> credentials = await _db.ProviderCredentials
                .Where(x => x.Provider == provider)
                .ToListAsync(cancellationToken);

            // Tắt trước, lưu, rồi mới bật — cùng lý do với DbPromptStore: EF tự chọn thứ tự UPDATE,
            // và bật trước khi tắt thì unique index lọc (Code) WHERE IsActive = 1 chặn ngay.
            foreach (ProviderDescriptorRow row in currentlyActive)
            {
                row.IsActive = false;
            }

            await _db.SaveChangesAsync(cancellationToken);

            target.IsActive = true;

            // Capability trong descriptor là nguồn sự thật cho provider này: ghi thẳng vào credential
            // để registry, ProviderCapabilityValidator và Luật 3 không cần biết descriptor tồn tại.
            foreach (ProviderCredential credential in credentials)
            {
                credential.CapabilityJson = parsed.CapabilityJson!;
            }

            await _db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            credentialCount = credentials.Count;
        });

        if (credentialCount == 0)
        {
            _logger.LogWarning(
                "Đã bật descriptor {Provider} v{Version} nhưng chưa có credential nào tên này — nạp bằng set-credential.",
                provider,
                version);
        }

        Invalidate();
        _credentialSignal.Reset();
    }

    public async Task DeactivateAsync(string provider, CancellationToken cancellationToken = default)
    {
        List<ProviderDescriptorRow> active = await _db.ProviderDescriptors
            .Where(x => x.Code == provider && x.IsActive)
            .ToListAsync(cancellationToken);

        foreach (ProviderDescriptorRow row in active)
        {
            row.IsActive = false;
        }

        await _db.SaveChangesAsync(cancellationToken);

        Invalidate();
    }

    public void Invalidate() => _signal.Reset();
}
