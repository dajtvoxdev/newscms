namespace NewsCMS.Application.Builder;

/// <summary>
/// Registry tra IDynamicBlock theo key. Resolve từ DI (tất cả IDynamicBlock đã đăng ký).
/// </summary>
public interface IDynamicBlockRegistry
{
    /// <summary>Tìm block theo key. Trả null nếu không có (renderer dùng fallback).</summary>
    IDynamicBlock? Get(string key);

    /// <summary>Danh sách tất cả block đã đăng ký (cho builder/AI liệt kê).</summary>
    IReadOnlyList<IDynamicBlock> All { get; }
}
