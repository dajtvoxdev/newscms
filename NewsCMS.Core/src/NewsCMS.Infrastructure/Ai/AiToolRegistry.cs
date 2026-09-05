using NewsCMS.Application.Ai;

namespace NewsCMS.Infrastructure.Ai;

public sealed class AiToolRegistry : IAiToolRegistry
{
    private readonly IReadOnlyList<IAiTool> _tools;

    public AiToolRegistry(IEnumerable<IAiTool> tools)
    {
        _tools = tools.ToList().AsReadOnly();
    }

    public IReadOnlyList<IAiTool> All() => _tools;

    public IAiTool? Find(string key) =>
        _tools.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
}
