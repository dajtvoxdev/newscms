using NewsCMS.Application.Common;

namespace NewsCMS.Application.Builder;

// ── DTOs cho SiteLayout API (Phase 3) ───────────────────────────────────────────

/// <param name="CompiledHtml">
/// HTML shell (header + <c>data-nc-body</c> + footer). Chỉ có giá trị ở <c>GetByIdAsync</c>;
/// <c>ListAsync</c> để null vì danh sách không cần cả cây HTML.
/// </param>
public sealed record SiteLayoutDto(
    Guid Id,
    string Key,
    string Name,
    string Kind,
    string? BuilderJson,
    string? CompiledHtml,
    string? CompiledCss,
    string? CustomCss,
    string? CustomJs,
    bool IsDefault);

/// <param name="CompiledHtml">
/// null = GIỮ NGUYÊN HTML hiện có. Trình sửa code (EditLayout) không dựng lại HTML nên gửi null;
/// chỉ shell builder trực quan mới gửi HTML thật.
/// </param>
/// <param name="CompiledCss">
/// null = giữ nguyên. Cùng lý do: CSS này do shell builder compile ra, trình sửa code không chạm.
/// </param>
public sealed record SiteLayoutSaveRequest(
    string Key,
    string Name,
    string Kind,
    string? BuilderJson,
    string? CompiledHtml,
    string? CompiledCss,
    string? CustomCss,
    string? CustomJs,
    bool IsDefault);

/// <summary>
/// CRUD layout builder (shell/header/footer/sidebar). Layout shell bọc nội dung page khi render.
/// Header/Footer là global parts — Phase 3 lưu trữ + CRUD; ghép vào render là Phase 4.
/// </summary>
public interface ISiteLayoutService
{
    Task<Result<SiteLayoutDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SiteLayoutDto>> ListAsync(CancellationToken ct = default);
    Task<Result<SiteLayoutDto>> CreateAsync(SiteLayoutSaveRequest request, CancellationToken ct = default);
    Task<Result<SiteLayoutDto>> UpdateAsync(Guid id, SiteLayoutSaveRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}
