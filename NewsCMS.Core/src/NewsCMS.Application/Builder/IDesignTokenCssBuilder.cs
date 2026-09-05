namespace NewsCMS.Application.Builder;

/// <summary>
/// Sinh CSS từ SiteDesignToken của site: một khối duy nhất vừa là CSS variables vừa là @theme
/// Tailwind, để cả var(--color-brand-500) lẫn utility bg-brand-500 cùng trỏ một nguồn.
/// Kết quả được phục vụ qua /_nc/site/{slug}-{hash}.css (immutable cache) hoặc inline vào trang.
/// </summary>
public interface IDesignTokenCssBuilder
{
    /// <summary>
    /// Sinh nội dung CSS từ toàn bộ token của site hiện tại. Gồm @import tailwind, khối @theme
    /// và :root CSS variables. Không có token nào vẫn trả CSS hợp lệ (rỗng phần token).
    /// </summary>
    Task<string> BuildCssAsync(CancellationToken ct = default);

    /// <summary>
    /// Hash ngắn của nội dung CSS — dùng trong tên file /_nc/site/{slug}-{hash}.css để
    /// cache immutable; đổi token là đổi hash, client tự lấy bản mới.
    /// </summary>
    Task<string> GetCssHashAsync(CancellationToken ct = default);
}
