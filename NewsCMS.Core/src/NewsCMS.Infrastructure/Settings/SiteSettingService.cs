using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Settings;

public sealed class SiteSettingService : ISiteSettingService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _db;

    public SiteSettingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken ct = default)
    {
        var raw = await _db.SiteSettings
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(ct);

        if (raw is null)
        {
            return defaultValue;
        }

        try
        {
            return Convert<T>(raw);
        }
        catch
        {
            return defaultValue;
        }
    }

    public async Task SetAsync<T>(string key, T value, string group = "general", CancellationToken ct = default)
    {
        var encoded = Encode(value);
        var existing = await _db.SiteSettings.FirstOrDefaultAsync(x => x.Key == key, ct);

        if (existing is null)
        {
            _db.SiteSettings.Add(new SiteSetting
            {
                Key = key,
                Value = encoded,
                Group = group,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Value = encoded;
            existing.Group = group;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetGroupAsync(string group, CancellationToken ct = default)
    {
        var rows = await _db.SiteSettings
            .Where(x => x.Group == group)
            .Select(x => new { x.Key, x.Value })
            .ToListAsync(ct);

        var dict = new Dictionary<string, string?>(rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            dict[row.Key] = row.Value;
        }
        return dict;
    }

    private static T? Convert<T>(string raw)
    {
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (type == typeof(string))
        {
            return (T)(object)raw;
        }
        if (type == typeof(bool))
        {
            return (T)(object)bool.Parse(raw);
        }
        if (type == typeof(int))
        {
            return (T)(object)int.Parse(raw, CultureInfo.InvariantCulture);
        }
        if (type == typeof(long))
        {
            return (T)(object)long.Parse(raw, CultureInfo.InvariantCulture);
        }
        if (type == typeof(decimal))
        {
            return (T)(object)decimal.Parse(raw, CultureInfo.InvariantCulture);
        }
        if (type == typeof(Guid))
        {
            return (T)(object)Guid.Parse(raw);
        }
        if (type == typeof(DateTime))
        {
            return (T)(object)DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        return JsonSerializer.Deserialize<T>(raw, JsonOpts);
    }

    private static string Encode<T>(T value)
    {
        if (value is null)
        {
            return string.Empty;
        }
        if (value is string s)
        {
            return s;
        }
        if (value is bool b)
        {
            return b ? "true" : "false";
        }
        if (value is IFormattable f)
        {
            return f.ToString(null, CultureInfo.InvariantCulture);
        }
        return JsonSerializer.Serialize(value, JsonOpts);
    }
}
