using Microsoft.AspNetCore.Hosting;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Gắn ?v=timestamp vào đường dẫn asset tĩnh của khối. Đổi file là khách nhận bản mới ngay,
/// không phải bảo nhau xoá cache — nhưng vẫn cache dài hạn giữa hai lần đổi.
/// </summary>
internal static class BlockAssets
{
    public static string Versioned(IWebHostEnvironment env, string path)
    {
        try
        {
            var root = env.WebRootPath;
            if (string.IsNullOrEmpty(root)) return path;
            var full = Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
                return $"{path}?v={File.GetLastWriteTimeUtc(full).Ticks:x}";
        }
        catch { /* bỏ qua, dùng path trần */ }
        return path;
    }
}
