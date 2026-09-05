using NewsCMS.Application.Common;

namespace NewsCMS.Application.Builder;

// ── DTOs cho Custom CSS/JS 3 tầng (Phase 3) ─────────────────────────────────────

public sealed record SiteCustomCodeDto(
    string? CustomCss,
    string? CustomJs,
    string? HeadHtml,
    string? BodyEndHtml);

public sealed record SiteCustomCodeSaveRequest(
    string? CustomCss,
    string? CustomJs,
    string? HeadHtml,
    string? BodyEndHtml);

/// <summary>
/// Quản lý custom CSS/JS/HTML ở tầng site (lưu qua SiteSetting). Hai tầng còn lại (Layout, Page)
/// đã có cột CompiledCss/CustomJs trên entity tương ứng. Thứ tự cộng dồn: Site → Layout → Page.
/// </summary>
public interface ISiteCustomCodeService
{
    Task<SiteCustomCodeDto> GetAsync(CancellationToken ct = default);
    Task<Result<SiteCustomCodeDto>> SaveAsync(SiteCustomCodeSaveRequest request, CancellationToken ct = default);
}
