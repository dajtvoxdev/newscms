using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Infrastructure.Builder.ContentTypes;

/// <summary>
/// Triển khai IContentTypeRegistry quản lý toàn bộ Content Type trong Service Container.
/// Đảm bảo tính năng scoped per-request cache cho ContentDetail để tối ưu hóa hiệu năng render.
/// </summary>
public sealed class ContentTypeRegistry : IContentTypeRegistry
{
    private readonly Dictionary<string, IContentType> _byKey;
    private readonly Dictionary<RouteType, IContentType> _byRouteType;
    private readonly Dictionary<PageKind, IContentType> _byTemplateKind;
    private readonly List<IContentType> _all;

    // Bộ đệm scoped trong vòng đời request: các khối entity-* trên cùng 1 trang cùng đọc
    // từ cache này, giúp giảm số lượng query xuống còn đúng 1 query duy nhất.
    private readonly Dictionary<(RouteType, Guid), ContentDetail?> _detailCache = new();

    public ContentTypeRegistry(IEnumerable<IContentType> contentTypes)
    {
        _all = contentTypes.ToList();
        _byKey = new Dictionary<string, IContentType>(StringComparer.OrdinalIgnoreCase);
        _byRouteType = new Dictionary<RouteType, IContentType>();
        _byTemplateKind = new Dictionary<PageKind, IContentType>();

        foreach (var ct in _all)
        {
            _byKey[ct.Key] = ct;
            _byRouteType[ct.RouteType] = ct;
            _byTemplateKind[ct.TemplateKind] = ct;
        }
    }

    public IReadOnlyList<IContentType> All => _all;

    public IContentType? FindByKey(string key) =>
        _byKey.TryGetValue(key, out var ct) ? ct : null;

    public IContentType? FindByRouteType(RouteType routeType) =>
        _byRouteType.TryGetValue(routeType, out var ct) ? ct : null;

    public IContentType? FindByTemplateKind(PageKind kind) =>
        _byTemplateKind.TryGetValue(kind, out var ct) ? ct : null;

    public async Task<ContentDetail?> LoadDetailAsync(
        RouteType routeType, Guid entityId, CancellationToken ct = default)
    {
        var cacheKey = (routeType, entityId);
        if (_detailCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var contentType = FindByRouteType(routeType);
        if (contentType is null)
        {
            _detailCache[cacheKey] = null;
            return null;
        }

        var detail = await contentType.LoadAsync(entityId, ct);
        _detailCache[cacheKey] = detail;
        return detail;
    }
}

