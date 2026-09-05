using System.Text.Json;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Builder;

namespace NewsCMS.Application.Builder;

// ── DTOs cho template (Phase 6) ─────────────────────────────────────────────────

/// <summary>Spec khai báo toàn bộ site — đầu vào của ApplyAsync/clone (cùng định dạng Phase 7 MCP).</summary>
public sealed record SiteSpec(
    string SiteName,
    string SiteSlug,
    string? PrimaryDomain,
    string DefaultTheme,
    string DefaultCulture,
    string SupportedCultures,
    IReadOnlyList<LayoutSpec> Layouts,
    IReadOnlyList<PageSpec> Pages,
    IReadOnlyList<CategorySpec> Categories,
    IReadOnlyList<MenuSpec> Menus,
    IReadOnlyList<DesignTokenSpec> DesignTokens,
    IReadOnlyList<SiteSettingSpec> Settings);

public sealed record LayoutSpec(string Key, string Name, string Kind, string? CompiledHtml, string? CompiledCss, string? CustomJs, bool IsDefault, string? CustomCss = null);
public sealed record PageSpec(string Title, string Slug, string? CompiledHtml, string? CompiledCss, string? CustomCss, string? CustomJs, string Kind, string? LayoutKey, string? ParentSlug = null, bool IsDefaultTemplate = false);
public sealed record CategorySpec(string Name, string Slug, string? Description, string Type, int Order, string? TemplatePageSlug = null);
public sealed record MenuSpec(string Name, string Location, IReadOnlyList<MenuItemSpec> Items);
public sealed record MenuItemSpec(string Title, string Url, int Order, string? Target);
public sealed record DesignTokenSpec(string Group, string Key, string Value, int SortOrder);
public sealed record SiteSettingSpec(string Key, string? Value, string Group);

/// <summary>
/// Clone một SiteTemplate (SpecJson) thành site mới: layout/page/menu/category/token/media/setting.
/// Mọi thao tác trong một transaction — rollback toàn bộ nếu lỗi. Idempotent: site slug trùng → fail.
/// Dùng lại các service sẵn có (IBuilderPageService, IBuilderCategoryService...) khi có thể.
/// </summary>
public interface ISiteTemplateService
{
    /// <summary>Danh sách template toàn cục.</summary>
    Task<IReadOnlyList<SiteTemplateDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Lấy spec của một template.</summary>
    Task<SiteSpec?> GetSpecAsync(Guid templateId, CancellationToken ct = default);

    /// <summary>Clone template thành site mới. Trả về siteId vừa tạo.</summary>
    Task<Result<Guid>> CloneToSiteAsync(Guid templateId, string siteName, string siteSlug, string? primaryDomain, CancellationToken ct = default);
}

public sealed record SiteTemplateDto(Guid Id, string Key, string Name, string? Description);
