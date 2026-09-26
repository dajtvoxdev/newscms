using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;

namespace AdVideo.Tests.Infrastructure;

/// <summary>Kho setting trong bộ nhớ, cho test không cần DB.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.TryGetValue(key, out string? v) && int.TryParse(v, out int i) ? i : fallback);

    public Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.TryGetValue(key, out string? v) && decimal.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out decimal d) ? d : fallback);

    public Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.TryGetValue(key, out string? v) && bool.TryParse(v, out bool b) ? b : fallback);

    public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.TryGetValue(key, out string? v) ? v : null);

    public Task SetAsync(string key, string value, SettingValueType valueType, string description,
        bool isProvisional = false, CancellationToken cancellationToken = default)
    {
        Values[key] = value;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SystemSetting>> GetProvisionalAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SystemSetting>>([]);

    public void Invalidate()
    {
    }
}
