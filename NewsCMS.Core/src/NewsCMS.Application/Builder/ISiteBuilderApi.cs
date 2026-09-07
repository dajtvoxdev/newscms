using NewsCMS.Application.Common;

namespace NewsCMS.Application.Builder;

// ── DTOs cho Site Builder API (Phase 7 — bề mặt dùng chung cho MCP và admin) ────

/// <summary>
/// Kết quả của ApplyAsync: đếm số bản ghi đã tạo/cập nhật/bỏ qua theo từng nhóm.
/// Gọi lại cùng một spec phải cho Created = 0 ở mọi nhóm (idempotent).
/// </summary>
public sealed record ApplyReport(
    IReadOnlyList<ApplyEntry> Entries,
    IReadOnlyList<string> Warnings)
{
    public int TotalCreated => Entries.Sum(e => e.Created);
    public int TotalUpdated => Entries.Sum(e => e.Updated);
    public int TotalUnchanged => Entries.Sum(e => e.Unchanged);
}

/// <summary>Thống kê một nhóm entity trong ApplyAsync (ví dụ "Layouts", "Pages").</summary>
public sealed record ApplyEntry(string Group, int Created, int Updated, int Unchanged);

/// <summary>
/// Mô tả một dynamic block khả dụng — agent đọc để biết dựng block nào và truyền props gì.
/// </summary>
public sealed record BlockInfo(string Key, string Name, string Category, string Kind, string? PropsSchemaJson);

/// <summary>Schema tự mô tả của builder: agent gọi trước để biết dựng site thế nào.</summary>
public sealed record BuilderSchema(
    string SpecJsonSchema,
    IReadOnlyList<BlockInfo> Blocks,
    IReadOnlyList<string> PageKinds,
    IReadOnlyList<string> LayoutKinds,
    IReadOnlyList<string> DesignTokenGroups,
    IReadOnlyList<string> CategoryTypes,
    IReadOnlyList<string> Cultures);

/// <summary>Tóm tắt site hiện tại cho agent định hướng trước khi sửa.</summary>
public sealed record SiteSummary(
    Guid SiteId,
    string Name,
    string Slug,
    string? PrimaryDomain,
    string DefaultTheme,
    string DefaultCulture,
    string SupportedCultures,
    int PageCount,
    int PublishedPageCount,
    int LayoutCount,
    int CategoryCount,
    int MenuCount,
    int DesignTokenCount);

/// <summary>
/// Bề mặt thao tác site cho agent (MCP) và cho trợ lý AI trong admin. Toàn bộ nghiệp vụ nằm ở
/// đây; MCP tool chỉ là vỏ mỏng gọi xuống. Mọi thao tác chạy trong phạm vi site hiện tại
/// (ICurrentSite do middleware API key set) nên global query filter của AppDbContext tự bảo vệ
/// ranh giới tenant — không có tham số siteId nào để lách sang site khác.
/// </summary>
public interface ISiteBuilderApi
{
    /// <summary>Schema tự mô tả: định dạng SiteSpec, danh sách block, các enum hợp lệ.</summary>
    Task<BuilderSchema> GetSchemaAsync(CancellationToken ct = default);

    /// <summary>Tóm tắt site hiện tại (đếm page/layout/category/menu/token).</summary>
    Task<Result<SiteSummary>> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Áp dụng spec khai báo lên site hiện tại: diff theo khoá tự nhiên (layout theo Key,
    /// page theo Slug, category theo Slug, menu theo Location, token theo Group+Key,
    /// setting theo Key) rồi tạo mới hoặc cập nhật. Không xoá thứ không có trong spec.
    /// Toàn bộ trong một transaction — lỗi giữa chừng rollback sạch.
    /// </summary>
    Task<Result<ApplyReport>> ApplyAsync(SiteSpec spec, CancellationToken ct = default);

    /// <summary>
    /// Xuất site hiện tại thành SiteSpec — round-trip đúng định dạng ApplyAsync nhận vào.
    /// </summary>
    Task<Result<SiteSpec>> ExportAsync(CancellationToken ct = default);

    /// <summary>Render một trang đã publish thành HTML để agent tự kiểm chứng kết quả. Hỗ trợ sampleSlug khi xem trước template.</summary>
    Task<Result<string>> PreviewAsync(Guid pageId, string? culture = null, string? sampleSlug = null, CancellationToken ct = default);

    // ── CRUD mỏng: uỷ quyền cho service sẵn có, gom về một bề mặt cho agent ──────

    Task<PagedList<BuilderPageDto>> ListPagesAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task<Result<BuilderPageDto>> GetPageAsync(Guid id, CancellationToken ct = default);
    Task<Result<BuilderPageDto>> SavePageAsync(Guid? id, BuilderPageSaveRequest request, CancellationToken ct = default);
    Task<Result<BuilderPageDto>> PublishPageAsync(Guid id, string? note, CancellationToken ct = default);
    Task<Result> DeletePageAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<SiteLayoutDto>> ListLayoutsAsync(CancellationToken ct = default);
    Task<Result<SiteLayoutDto>> SaveLayoutAsync(Guid? id, SiteLayoutSaveRequest request, CancellationToken ct = default);

    Task<PagedList<BuilderCategoryDto>> ListCategoriesAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task<Result<BuilderCategoryDto>> SaveCategoryAsync(Guid? id, BuilderCategorySaveRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<BlockInfo>> ListBlocksAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DesignTokenSpec>> ListDesignTokensAsync(CancellationToken ct = default);
    Task<Result> SaveDesignTokensAsync(IReadOnlyList<DesignTokenSpec> tokens, CancellationToken ct = default);

    /// <summary>
    /// Xoá một token theo Group+Key. Cần riêng vì SaveDesignTokensAsync chỉ create/update theo
    /// danh sách (tính chất idempotent của ApplyAsync) — thiếu token trong danh sách không bị xoá.
    /// </summary>
    Task<Result> DeleteDesignTokenAsync(string group, string key, CancellationToken ct = default);

    Task<SiteCustomCodeDto> GetCustomCodeAsync(CancellationToken ct = default);
    Task<Result<SiteCustomCodeDto>> SaveCustomCodeAsync(SiteCustomCodeSaveRequest request, CancellationToken ct = default);

    Task<Result<SeoMetaDto>> SaveSeoMetaAsync(SeoMetaSaveRequest request, CancellationToken ct = default);

    /// <summary>Danh sách media của site (agent chọn ảnh có sẵn thay vì bịa URL).</summary>
    Task<IReadOnlyList<MediaBriefDto>> ListMediaAsync(int limit, CancellationToken ct = default);
}

/// <summary>Thông tin media tối giản cho agent: đủ để chèn vào HTML.</summary>
public sealed record MediaBriefDto(Guid Id, string FileName, string Url, string? Alt, int? Width, int? Height);
