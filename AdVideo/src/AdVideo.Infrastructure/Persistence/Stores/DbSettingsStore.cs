using System.Globalization;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Infrastructure.Persistence.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Persistence.Stores;

/// <summary>
/// Đọc tham số vận hành từ bảng <c>SystemSettings</c> (D10) — sửa số không cần deploy.
/// </summary>
/// <remarks>
/// <b>Vì sao có cache 60 giây:</b> pipeline đọc setting ở hầu hết mọi bước, mỗi job vài chục lần.
/// Không cache thì mỗi lần render là một tràng round-trip DB chỉ để lấy một con số. 60 giây là
/// thoả hiệp: người vận hành sửa số rồi chờ tối đa một phút thấy tác dụng, hoặc gọi
/// <see cref="Invalidate"/> để thấy ngay.
/// </remarks>
public sealed class DbSettingsStore : ISettingsStore
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private readonly AdVideoDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly StoreCacheSignal<DbSettingsStore> _signal;
    private readonly ILogger<DbSettingsStore> _logger;

    public DbSettingsStore(
        AdVideoDbContext db,
        IMemoryCache cache,
        StoreCacheSignal<DbSettingsStore> signal,
        ILogger<DbSettingsStore> logger)
    {
        _db = db;
        _cache = cache;
        _signal = signal;
        _logger = logger;
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default)
    {
        string? raw = await ReadRawAsync(key, cancellationToken);

        if (raw is null)
        {
            return fallback;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        LogUnparsable(key, raw, "số nguyên", fallback.ToString(CultureInfo.InvariantCulture));
        return fallback;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken cancellationToken = default)
    {
        string? raw = await ReadRawAsync(key, cancellationToken);

        if (raw is null)
        {
            return fallback;
        }

        // InvariantCulture là bắt buộc, không phải thói quen: VPS chạy locale vi-VN coi dấu phẩy
        // là dấu thập phân. Parse theo culture của máy thì "0.5" (nửa đô) thành 5 hoặc ném lỗi,
        // và con số này là trần chi phí.
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
        {
            return value;
        }

        LogUnparsable(key, raw, "số thập phân", fallback.ToString(CultureInfo.InvariantCulture));
        return fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default)
    {
        string? raw = await ReadRawAsync(key, cancellationToken);

        if (raw is null)
        {
            return fallback;
        }

        // Chấp nhận cả "1"/"0" vì người vận hành sửa trực tiếp bằng SQL và thói quen đó rất phổ biến.
        if (bool.TryParse(raw, out bool value))
        {
            return value;
        }

        if (raw == "1")
        {
            return true;
        }

        if (raw == "0")
        {
            return false;
        }

        LogUnparsable(key, raw, "true/false", fallback ? "true" : "false");
        return fallback;
    }

    public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
        => ReadRawAsync(key, cancellationToken);

    public async Task SetAsync(
        string key,
        string value,
        SettingValueType valueType,
        string description,
        bool isProvisional = false,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters để thấy cả dòng đã xoá mềm: gặp dòng cũ thì hồi sinh thay vì chèn
        // dòng mới. Chèn mới cũng qua được unique index (index đã lọc IsDeleted = 0), nhưng sẽ
        // để lại hai dòng cùng key mà chỉ một dòng có tác dụng — đúng kiểu bẫy cho lần sửa sau.
        SystemSetting? existing = await _db.SystemSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

        if (existing is null)
        {
            _db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value,
                ValueType = valueType,
                Description = description,
                IsProvisional = isProvisional,
            });
        }
        else
        {
            existing.Value = value;
            existing.ValueType = valueType;
            existing.Description = description;
            existing.IsProvisional = isProvisional;
            existing.IsDeleted = false;
            existing.DeletedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        Invalidate();
    }

    public async Task<IReadOnlyList<SystemSetting>> GetProvisionalAsync(CancellationToken cancellationToken = default)
    {
        // Không cache: đây là danh sách "số đang đoán, chờ đo thật" mà trang vận hành hiển thị,
        // gọi thưa và cần đúng tại thời điểm xem.
        return await _db.SystemSettings
            .AsNoTracking()
            .Where(x => x.IsProvisional)
            .OrderBy(x => x.Key)
            .ToListAsync(cancellationToken);
    }

    public void Invalidate() => _signal.Reset();

    private async Task<string?> ReadRawAsync(string key, CancellationToken cancellationToken)
    {
        string cacheKey = $"advideo:setting:{key}";

        // Cache cả trường hợp không tìm thấy (null): key thiếu là trạng thái bình thường trước khi
        // seed chạy, và không cache thì mỗi lần đọc lại là một lần quét DB không ra gì.
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.AddExpirationToken(_signal.Token);

            return await _db.SystemSettings
                .AsNoTracking()
                .Where(x => x.Key == key)
                .Select(x => x.Value)
                .FirstOrDefaultAsync(cancellationToken);
        });
    }

    private void LogUnparsable(string key, string raw, string expected, string fallback)
    {
        // Warning chứ không ném lỗi: một lỗi gõ trong bảng cấu hình không đáng làm chết cả job.
        // Nhưng phải in ra cả giá trị thật lẫn giá trị sẽ dùng thay, vì im lặng quay về mặc định
        // là kiểu hỏng khó tìm nhất — hệ thống chạy đúng quy trình với sai con số.
        _logger.LogWarning(
            "Setting {Key} có giá trị {Raw} không đọc được thành {Expected}; dùng tạm {Fallback}. Sửa lại dòng này trong bảng SystemSettings.",
            key,
            raw,
            expected,
            fallback);
    }
}
