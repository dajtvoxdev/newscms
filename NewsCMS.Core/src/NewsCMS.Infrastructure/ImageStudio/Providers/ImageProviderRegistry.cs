using Microsoft.Extensions.Options;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Infrastructure.ImageStudio.Providers;

public sealed class ImageProviderRegistry : IImageProviderRegistry
{
    private readonly Dictionary<ImageProviderAdapter, IImageProvider> _providers;

    public ImageProviderRegistry(IEnumerable<IImageProvider> providers, IOptions<ImageStudioOptions> options)
    {
        bool fakeEnabled = options.Value.EnableFakeProvider;

        _providers = providers
            .Where(p => p.Adapter != ImageProviderAdapter.Fake || fakeEnabled)
            .GroupBy(p => p.Adapter)
            .ToDictionary(g => g.Key, g => g.First());

        Available = _providers.Keys.OrderBy(a => (int)a).ToList();
    }

    public IReadOnlyList<ImageProviderAdapter> Available { get; }

    public IImageProvider? Get(ImageProviderAdapter adapter) => _providers.GetValueOrDefault(adapter);
}
