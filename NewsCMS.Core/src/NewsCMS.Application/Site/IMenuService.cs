namespace NewsCMS.Application.Site;

public interface IMenuService
{
    Task<MenuTreeDto?> GetByLocationAsync(string location, CancellationToken ct = default);
}

public record MenuItemDto(Guid Id, string Title, string Url, string? Target, string? Icon, IReadOnlyList<MenuItemDto> Children);
public record MenuTreeDto(Guid Id, string Name, string Location, IReadOnlyList<MenuItemDto> Items);
