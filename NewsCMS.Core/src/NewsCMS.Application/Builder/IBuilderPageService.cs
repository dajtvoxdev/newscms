using NewsCMS.Application.Common;

namespace NewsCMS.Application.Builder;

// ── DTOs cho Builder Page API (Phase 2) ────────────────────────────────────────

public sealed record BuilderPageDto(
    Guid Id,
    string Title,
    string Slug,
    string? BuilderJson,
    string? CompiledCss,
    string? CustomCss,
    string? CustomJs,
    string Kind,
    string Status,
    int Version,
    bool CssDirty,
    DateTime? PublishedAt,
    /// <summary>
    /// HTML đã biên dịch — NGUỒN SỰ THẬT của trang. Builder nạp canvas từ đây khi BuilderJson
    /// thiếu (trang tạo qua MCP/API không có project data của GrapesJS). ListAsync trả null
    /// để không kéo hàng chục KB HTML cho mỗi dòng danh sách.
    /// </summary>
    string? CompiledHtml = null,
    Guid? LayoutId = null,
    Guid? ParentPageId = null,
    bool IsDefaultTemplate = false);

public sealed record BuilderPageSaveRequest(
    string Title,
    string Slug,
    string? BuilderJson,
    string? CompiledHtml,
    string? CompiledCss,
    string? CustomCss,
    string? CustomJs,
    string Kind,
    /// <summary>Shell bọc nội dung khi render. null = giữ nguyên giá trị hiện có của trang.</summary>
    Guid? LayoutId = null,
    Guid? ParentPageId = null,
    bool? IsDefaultTemplate = null);

public sealed record BuilderPagePublishRequest(string? Note);

public sealed record PageRevisionDto(
    Guid Id,
    int Version,
    string? Note,
    DateTime CreatedAt,
    Guid? CreatedBy);

/// <summary>
/// Service CRUD + publish/revision cho trang builder. Mọi thao tác đều scope theo site hiện tại
/// (qua global query filter). Sanitize HTML bằng ContentSanitizer.SanitizeBuilder trước khi lưu.
/// </summary>
public interface IBuilderPageService
{
    Task<Result<BuilderPageDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedList<BuilderPageDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task<Result<BuilderPageDto>> CreateAsync(BuilderPageSaveRequest request, CancellationToken ct = default);
    Task<Result<BuilderPageDto>> UpdateAsync(Guid id, BuilderPageSaveRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Xuất bản: set Status=Published, tăng Version, tạo PageRevision snapshot.</summary>
    Task<Result<BuilderPageDto>> PublishAsync(Guid id, BuilderPagePublishRequest request, CancellationToken ct = default);

    /// <summary>Danh sách revision của một page (mới nhất trước).</summary>
    Task<IReadOnlyList<PageRevisionDto>> GetRevisionsAsync(Guid pageId, CancellationToken ct = default);

    /// <summary>Khôi phục một revision: overwrite BuilderJson/CompiledHtml/Css/Js của page từ revision.</summary>
    Task<Result<BuilderPageDto>> RestoreRevisionAsync(Guid pageId, int version, CancellationToken ct = default);
}
