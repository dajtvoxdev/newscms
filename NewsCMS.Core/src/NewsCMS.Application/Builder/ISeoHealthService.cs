namespace NewsCMS.Application.Builder;

/// <summary>
/// Kiểm tra sức khỏe SEO của site: thiếu meta, trùng slug, trang mồ côi, ảnh thiếu alt, H1 trùng.
/// </summary>
public interface ISeoHealthService
{
    Task<SeoHealthReport> CheckAsync(CancellationToken ct = default);
}

public sealed record SeoHealthReport(
    int TotalPages,
    int PagesMissingMeta,
    int DuplicateSlugs,
    int OrphanPages,
    IReadOnlyList<SeoIssue> Issues);

public sealed record SeoIssue(string Severity, string Type, string Message, Guid? EntityId);
