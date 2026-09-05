namespace NewsCMS.Application.Site;

public interface ISiteSettingService
{
    Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, string group = "general", CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string?>> GetGroupAsync(string group, CancellationToken ct = default);
}

public interface IBannerService
{
    Task<IReadOnlyList<BannerDto>> GetByPositionAsync(string position, CancellationToken ct = default);
}

public record BannerDto(Guid Id, string Title, string ImageUrl, string? Url, int Order);
