using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Registry tra IDynamicBlock theo key. Build từ tất cả IDynamicBlock đã đăng ký trong DI.
/// Immutable sau khi khởi tạo — không cần lock.
/// </summary>
public sealed class DynamicBlockRegistry : IDynamicBlockRegistry
{
    private readonly Dictionary<string, IDynamicBlock> _blocks;

    public DynamicBlockRegistry(IEnumerable<IDynamicBlock> blocks)
    {
        _blocks = new Dictionary<string, IDynamicBlock>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            // Nếu trùng key, giữ cái đăng ký trước (first-wins). Log warning ở production nếu cần.
            if (!_blocks.ContainsKey(block.Key))
                _blocks[block.Key] = block;
        }
        All = _blocks.Values.ToList().AsReadOnly();
    }

    public IDynamicBlock? Get(string key) =>
        _blocks.TryGetValue(key, out var block) ? block : null;

    public IReadOnlyList<IDynamicBlock> All { get; }
}
