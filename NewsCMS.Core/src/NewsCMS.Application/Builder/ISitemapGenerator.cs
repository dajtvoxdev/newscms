namespace NewsCMS.Application.Builder;

/// <summary>
/// Sinh sitemap XML từ SiteRoute. Hỗ trợ sitemap index + sitemap con cho site lớn.
/// Giữ nhánh legacy cho 3 theme RCL cũ (không ảnh hưởng behavior hiện tại).
/// </summary>
public interface ISitemapGenerator
{
    /// <summary>Sinh sitemap XML đầy đủ cho site hiện tại.</summary>
    Task<string> GenerateAsync(CancellationToken ct = default);

    /// <summary>Sinh robots.txt cho site hiện tại.</summary>
    Task<string> GenerateRobotsTxtAsync(CancellationToken ct = default);
}
