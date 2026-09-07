using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.Builder;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Seo;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Bề mặt thao tác site cho agent (MCP) và trợ lý AI trong admin. Mọi thao tác chạy trong phạm vi
/// site hiện tại — không nhận siteId từ ngoài, nên global query filter của AppDbContext là ranh giới
/// tenant duy nhất và không thể bị lách.
///
/// ApplyAsync khai báo + idempotent: diff theo khoá tự nhiên rồi tạo/cập nhật, KHÔNG xoá thứ nằm
/// ngoài spec (agent gửi spec thiếu không được phá site). Trang tạo qua đây đặt CssDirty = true:
/// bundle site-utilities.css đã phủ tập utility tài liệu hoá nên trang chạy được ngay, cờ này chỉ
/// đánh dấu "chưa qua builder" cho utility nằm ngoài bundle.
/// </summary>
public sealed partial class SiteBuilderApi : ISiteBuilderApi
{
    private readonly AppDbContext _db;
    private readonly ICurrentSite _currentSite;
    private readonly ContentSanitizer _sanitizer;
    private readonly IRouteRegistry _routeRegistry;
    private readonly IBuilderPageService _pages;
    private readonly ISiteLayoutService _layouts;
    private readonly IBuilderCategoryService _categories;
    private readonly ISiteCustomCodeService _customCode;
    private readonly ISeoMetaService _seoMeta;
    private readonly IPageRenderer _renderer;
    private readonly IDynamicBlockRegistry _blockRegistry;
    private readonly SiteCacheSignal _signal;

    public SiteBuilderApi(
        AppDbContext db,
        ICurrentSite currentSite,
        ContentSanitizer sanitizer,
        IRouteRegistry routeRegistry,
        IBuilderPageService pages,
        ISiteLayoutService layouts,
        IBuilderCategoryService categories,
        ISiteCustomCodeService customCode,
        ISeoMetaService seoMeta,
        IPageRenderer renderer,
        IDynamicBlockRegistry blockRegistry,
        SiteCacheSignal signal)
    {
        _db = db;
        _currentSite = currentSite;
        _sanitizer = sanitizer;
        _routeRegistry = routeRegistry;
        _pages = pages;
        _layouts = layouts;
        _categories = categories;
        _customCode = customCode;
        _seoMeta = seoMeta;
        _renderer = renderer;
        _blockRegistry = blockRegistry;
        _signal = signal;
    }

    // ── Schema + summary ────────────────────────────────────────────────────────

    public async Task<BuilderSchema> GetSchemaAsync(CancellationToken ct = default)
    {
        var blocks = await ListBlocksAsync(ct);
        var site = await _db.Sites.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == _currentSite.SiteId, ct);

        var cultures = ParseCsv(site?.SupportedCultures).ToList();
        if (cultures.Count == 0) cultures.Add(site?.DefaultCulture ?? "vi");

        return new BuilderSchema(
            SpecJsonSchema: SpecJsonSchema,
            Blocks: blocks,
            PageKinds: Enum.GetNames<PageKind>(),
            LayoutKinds: Enum.GetNames<LayoutKind>(),
            DesignTokenGroups: Enum.GetNames<DesignTokenGroup>(),
            CategoryTypes: Enum.GetNames<CategoryType>(),
            Cultures: cultures);
    }

    public async Task<Result<SiteSummary>> GetSummaryAsync(CancellationToken ct = default)
    {
        var site = await _db.Sites.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == _currentSite.SiteId, ct);
        if (site is null) return Result<SiteSummary>.Failure("Không xác định được site hiện tại.");

        return Result<SiteSummary>.Success(new SiteSummary(
            SiteId: site.Id,
            Name: site.Name,
            Slug: site.Slug,
            PrimaryDomain: site.PrimaryDomain,
            DefaultTheme: site.DefaultTheme,
            DefaultCulture: site.DefaultCulture,
            SupportedCultures: site.SupportedCultures,
            PageCount: await _db.Pages.CountAsync(p => !p.IsDeleted, ct),
            PublishedPageCount: await _db.Pages.CountAsync(p => !p.IsDeleted && p.Status == BuilderPageStatus.Published, ct),
            LayoutCount: await _db.SiteLayouts.CountAsync(l => !l.IsDeleted, ct),
            CategoryCount: await _db.Categories.CountAsync(ct),
            MenuCount: await _db.Menus.CountAsync(ct),
            DesignTokenCount: await _db.SiteDesignTokens.CountAsync(ct)));
    }

    // ── Apply (khai báo, idempotent) ────────────────────────────────────────────

    public async Task<Result<ApplyReport>> ApplyAsync(SiteSpec spec, CancellationToken ct = default)
    {
        if (spec is null) return Result<ApplyReport>.Failure("Spec rỗng.");

        var siteId = _currentSite.SiteId;
        if (siteId == Guid.Empty)
            return Result<ApplyReport>.Failure("Chưa xác định được site hiện tại.");

        var site = await _db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site is null) return Result<ApplyReport>.Failure("Site không tồn tại.");

        var entries = new List<ApplyEntry>();
        var warnings = new List<string>();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // 1. Thuộc tính site — chỉ ghi khi spec có giá trị, tránh xoá cấu hình sẵn có.
            if (!string.IsNullOrWhiteSpace(spec.SiteName) && spec.SiteName != site.Name)
                site.Name = spec.SiteName.Trim();
            if (!string.IsNullOrWhiteSpace(spec.DefaultCulture) && spec.DefaultCulture != site.DefaultCulture)
                site.DefaultCulture = spec.DefaultCulture.Trim();
            if (!string.IsNullOrWhiteSpace(spec.SupportedCultures) && spec.SupportedCultures != site.SupportedCultures)
                site.SupportedCultures = spec.SupportedCultures.Trim();

            // Slug site và domain KHÔNG đổi qua đây: đó là thao tác hạ tầng của root, agent chỉ
            // được dựng nội dung bên trong site đã cấp.
            if (!string.IsNullOrWhiteSpace(spec.SiteSlug)
                && !spec.SiteSlug.Equals(site.Slug, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Bỏ qua SiteSlug '{spec.SiteSlug}': không đổi được slug site qua ApplyAsync.");
            if (!string.IsNullOrWhiteSpace(spec.PrimaryDomain)
                && !string.Equals(spec.PrimaryDomain, site.PrimaryDomain, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Bỏ qua PrimaryDomain '{spec.PrimaryDomain}': gán domain là thao tác của root.");

            entries.Add(await ApplyDesignTokensAsync(siteId, spec.DesignTokens, ct));

            var layoutResult = await ApplyLayoutsAsync(siteId, spec.Layouts, ct);
            entries.Add(layoutResult.Entry);

            var pageResult = await ApplyPagesAsync(siteId, site.DefaultCulture, spec.Pages, layoutResult.IdByKey, warnings, ct);
            entries.Add(pageResult.Entry);

            entries.Add(await ApplyCategoriesAsync(siteId, site.DefaultCulture, spec.Categories, pageResult.PageIdBySlug, warnings, ct));
            entries.Add(await ApplyMenusAsync(siteId, spec.Menus, ct));
            entries.Add(await ApplySettingsAsync(siteId, spec.Settings, ct));

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _routeRegistry.Invalidate();

            // Spec không có nhóm nội dung nào thường là dấu hiệu payload sai định dạng (sai kiểu
            // đặt tên thuộc tính, bọc nhầm một lớp object...) chứ không phải chủ ý. Không báo thì
            // caller thấy "thành công" với toàn số 0 và không hiểu vì sao site chẳng đổi gì.
            var touched = (spec.Layouts?.Count ?? 0) + (spec.Pages?.Count ?? 0)
                        + (spec.Categories?.Count ?? 0) + (spec.Menus?.Count ?? 0)
                        + (spec.DesignTokens?.Count ?? 0) + (spec.Settings?.Count ?? 0);
            if (touched == 0)
                warnings.Add("Spec không chứa layout/page/category/menu/token/setting nào — "
                           + "kiểm tra lại định dạng payload (tên thuộc tính, cấu trúc lồng).");

            return Result<ApplyReport>.Success(new ApplyReport(entries, warnings));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return Result<ApplyReport>.Failure($"Lỗi khi áp dụng spec: {ex.Message}");
        }
    }

    private async Task<ApplyEntry> ApplyDesignTokensAsync(
        Guid siteId, IReadOnlyList<DesignTokenSpec>? specs, CancellationToken ct)
    {
        int created = 0, updated = 0, unchanged = 0;
        if (specs is null || specs.Count == 0) return new ApplyEntry("DesignTokens", 0, 0, 0);

        var existing = await _db.SiteDesignTokens.ToListAsync(ct);

        foreach (var s in specs)
        {
            if (string.IsNullOrWhiteSpace(s.Key)) continue;
            var group = ParseEnum(s.Group, DesignTokenGroup.Color);

            var match = existing.FirstOrDefault(t =>
                t.Group == group && t.Key.Equals(s.Key, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                _db.SiteDesignTokens.Add(new SiteDesignToken
                {
                    SiteId = siteId,
                    Group = group,
                    Key = s.Key.Trim(),
                    Value = s.Value ?? string.Empty,
                    SortOrder = s.SortOrder
                });
                created++;
            }
            else if (match.Value != s.Value || match.SortOrder != s.SortOrder)
            {
                match.Value = s.Value ?? string.Empty;
                match.SortOrder = s.SortOrder;
                updated++;
            }
            else unchanged++;
        }

        return new ApplyEntry("DesignTokens", created, updated, unchanged);
    }

    private async Task<(ApplyEntry Entry, Dictionary<string, Guid> IdByKey)> ApplyLayoutsAsync(
        Guid siteId, IReadOnlyList<LayoutSpec>? specs, CancellationToken ct)
    {
        int created = 0, updated = 0, unchanged = 0;
        var idByKey = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var existing = await _db.SiteLayouts.Where(l => !l.IsDeleted).ToListAsync(ct);

        // Nạp cả layout sẵn có để page tham chiếu được LayoutKey không nằm trong spec lần này.
        foreach (var l in existing) idByKey[l.Key] = l.Id;

        if (specs is null || specs.Count == 0)
            return (new ApplyEntry("Layouts", 0, 0, 0), idByKey);

        foreach (var s in specs)
        {
            if (string.IsNullOrWhiteSpace(s.Key)) continue;

            var kind = ParseEnum(s.Kind, LayoutKind.Shell);
            var html = _sanitizer.SanitizeBuilder(s.CompiledHtml);
            var match = existing.FirstOrDefault(l => l.Key.Equals(s.Key, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                var layout = new SiteLayout
                {
                    SiteId = siteId,
                    Key = s.Key.Trim(),
                    Name = string.IsNullOrWhiteSpace(s.Name) ? s.Key.Trim() : s.Name.Trim(),
                    Kind = kind,
                    CompiledHtml = html,
                    CompiledCss = s.CompiledCss,
                    CustomCss = s.CustomCss,
                    CustomJs = s.CustomJs,
                    IsDefault = s.IsDefault
                };
                _db.SiteLayouts.Add(layout);
                idByKey[layout.Key] = layout.Id;
                created++;
            }
            else if (match.Name != s.Name || match.Kind != kind || match.CompiledHtml != html
                     || match.CompiledCss != s.CompiledCss || match.CustomCss != s.CustomCss
                     || match.CustomJs != s.CustomJs
                     || match.IsDefault != s.IsDefault)
            {
                match.Name = string.IsNullOrWhiteSpace(s.Name) ? match.Name : s.Name.Trim();
                match.Kind = kind;
                match.CompiledHtml = html;
                match.CompiledCss = s.CompiledCss;
                match.CustomCss = s.CustomCss;
                match.CustomJs = s.CustomJs;
                match.IsDefault = s.IsDefault;
                match.UpdatedAt = DateTime.UtcNow;
                updated++;
            }
            else unchanged++;
        }

        return (new ApplyEntry("Layouts", created, updated, unchanged), idByKey);
    }

    private async Task<ApplyEntry> ApplyCategoriesAsync(
        Guid siteId, string defaultCulture, IReadOnlyList<CategorySpec>? specs,
        IReadOnlyDictionary<string, Guid> pageIdBySlug, List<string> warnings, CancellationToken ct)
    {
        int created = 0, updated = 0, unchanged = 0;
        if (specs is null || specs.Count == 0) return new ApplyEntry("Categories", 0, 0, 0);

        var existing = await _db.Categories.ToListAsync(ct);

        foreach (var s in specs)
        {
            if (string.IsNullOrWhiteSpace(s.Slug)) continue;

            var type = ParseEnum(s.Type, CategoryType.Post);
            var match = existing.FirstOrDefault(c => c.Slug.Equals(s.Slug, StringComparison.OrdinalIgnoreCase));

            Guid? templatePageId = null;
            if (!string.IsNullOrWhiteSpace(s.TemplatePageSlug))
            {
                if (pageIdBySlug.TryGetValue(s.TemplatePageSlug.Trim(), out var tId))
                    templatePageId = tId;
                else
                    warnings.Add($"Category '{s.Slug}': không tìm thấy TemplatePageSlug '{s.TemplatePageSlug}'.");
            }

            if (match is null)
            {
                var cat = new Category
                {
                    SiteId = siteId,
                    Name = string.IsNullOrWhiteSpace(s.Name) ? s.Slug.Trim() : s.Name.Trim(),
                    Slug = s.Slug.Trim(),
                    Description = s.Description,
                    Type = type,
                    PathSlug = s.Slug.Trim(),
                    Order = s.Order,
                    TemplatePageId = templatePageId,
                    IsActive = true
                };
                _db.Categories.Add(cat);
                await UpsertRouteAsync(siteId, "/" + cat.Slug, defaultCulture, RouteType.Category, cat.Id, ct);
                created++;
            }
            else if (match.Name != s.Name || match.Description != s.Description
                     || match.Type != type || match.Order != s.Order || match.TemplatePageId != templatePageId)
            {
                match.Name = string.IsNullOrWhiteSpace(s.Name) ? match.Name : s.Name.Trim();
                match.Description = s.Description;
                match.Type = type;
                match.Order = s.Order;
                match.TemplatePageId = templatePageId;
                match.UpdatedAt = DateTime.UtcNow;
                await UpsertRouteAsync(siteId, "/" + match.Slug, defaultCulture, RouteType.Category, match.Id, ct);
                updated++;
            }
            else
            {
                await UpsertRouteAsync(siteId, "/" + match.Slug, defaultCulture, RouteType.Category, match.Id, ct);
                unchanged++;
            }
        }

        return new ApplyEntry("Categories", created, updated, unchanged);
    }

    private async Task<(ApplyEntry Entry, Dictionary<string, Guid> PageIdBySlug)> ApplyPagesAsync(
        Guid siteId, string defaultCulture, IReadOnlyList<PageSpec>? specs,
        Dictionary<string, Guid> layoutIdByKey, List<string> warnings, CancellationToken ct)
    {
        int created = 0, updated = 0, unchanged = 0;
        var existing = await _db.Pages.Where(p => !p.IsDeleted).ToListAsync(ct);
        var pageIdBySlug = existing.ToDictionary(p => p.Slug, p => p.Id, StringComparer.OrdinalIgnoreCase);

        if (specs is null || specs.Count == 0) return (new ApplyEntry("Pages", 0, 0, 0), pageIdBySlug);

        // Thứ tự apply: trang không có ParentSlug (trang listing/cha) được xử lý trước, sau đó đến trang con/template
        var orderedSpecs = specs.OrderBy(s => string.IsNullOrWhiteSpace(s.ParentSlug) ? 0 : 1).ToList();

        foreach (var s in orderedSpecs)
        {
            if (string.IsNullOrWhiteSpace(s.Slug) && string.IsNullOrWhiteSpace(s.Title))
            {
                warnings.Add("Bỏ qua một page không có cả Slug lẫn Title.");
                continue;
            }

            var slug = string.IsNullOrWhiteSpace(s.Slug) ? Slugify(s.Title) : s.Slug.Trim();
            var kind = ParseEnum(s.Kind, PageKind.Landing);
            var html = _sanitizer.SanitizeBuilder(s.CompiledHtml);

            Guid? layoutId = null;
            if (!string.IsNullOrWhiteSpace(s.LayoutKey))
            {
                if (layoutIdByKey.TryGetValue(s.LayoutKey, out var lid)) layoutId = lid;
                else warnings.Add($"Page '{slug}': không tìm thấy layout '{s.LayoutKey}', dùng layout mặc định.");
            }

            Guid? parentPageId = null;
            if (!string.IsNullOrWhiteSpace(s.ParentSlug))
            {
                if (pageIdBySlug.TryGetValue(s.ParentSlug.Trim(), out var pId))
                    parentPageId = pId;
                else
                    warnings.Add($"Page '{slug}': không tìm thấy ParentSlug '{s.ParentSlug}'.");
            }

            var match = existing.FirstOrDefault(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
            var routePath = IsHomeSlug(slug) ? "/" : "/" + slug;

            if (match is null)
            {
                var page = new Page
                {
                    SiteId = siteId,
                    Title = string.IsNullOrWhiteSpace(s.Title) ? slug : s.Title.Trim(),
                    Slug = slug,
                    Content = string.Empty,   // legacy field của theme RCL cũ
                    LayoutId = layoutId,
                    CompiledHtml = html,
                    CompiledCss = s.CompiledCss,
                    CustomCss = s.CustomCss,
                    CustomJs = s.CustomJs,
                    Kind = kind,
                    ParentPageId = parentPageId,
                    IsDefaultTemplate = s.IsDefaultTemplate,
                    Status = BuilderPageStatus.Published,
                    IsPublished = true,
                    PublishedAt = DateTime.UtcNow,
                    Version = 1,
                    // Chưa qua builder: utility ngoài bundle site-utilities.css chưa được compile.
                    CssDirty = true
                };
                _db.Pages.Add(page);
                pageIdBySlug[slug] = page.Id;

                // Trang template không có route độc lập công khai
                if (!kind.IsTemplate())
                {
                    await UpsertRouteAsync(siteId, routePath, defaultCulture, RouteType.Page, page.Id, ct);
                }
                created++;
            }
            else
            {
                pageIdBySlug[slug] = match.Id;

                var isDiff = match.Title != s.Title || match.CompiledHtml != html || match.CompiledCss != s.CompiledCss
                             || match.CustomCss != s.CustomCss || match.CustomJs != s.CustomJs || match.Kind != kind
                             || (layoutId is not null && match.LayoutId != layoutId)
                             || match.ParentPageId != parentPageId
                             || match.IsDefaultTemplate != s.IsDefaultTemplate;

                if (isDiff)
                {
                    // Snapshot bản CŨ trước khi ghi đè để restore được nếu agent ghi nhầm.
                    _db.PageRevisions.Add(new PageRevision
                    {
                        SiteId = siteId,
                        PageId = match.Id,
                        Version = await NextRevisionVersionAsync(match.Id, match.Version, ct),
                        BuilderJson = match.BuilderJson,
                        CompiledHtml = match.CompiledHtml,
                        CompiledCss = match.CompiledCss,
                        CustomCss = match.CustomCss,
                        CustomJs = match.CustomJs,
                        Note = "Bản trước khi ISiteBuilderApi.ApplyAsync ghi đè"
                    });

                    match.Title = string.IsNullOrWhiteSpace(s.Title) ? match.Title : s.Title.Trim();
                    match.CompiledHtml = html;
                    match.BuilderJson = null;
                    match.CompiledCss = s.CompiledCss;
                    match.CustomCss = s.CustomCss;
                    match.CustomJs = s.CustomJs;
                    match.Kind = kind;
                    if (layoutId is not null) match.LayoutId = layoutId;
                    match.ParentPageId = parentPageId;
                    match.IsDefaultTemplate = s.IsDefaultTemplate;
                    match.Status = BuilderPageStatus.Published;
                    match.IsPublished = true;
                    match.PublishedAt ??= DateTime.UtcNow;
                    match.Version++;
                    match.CssDirty = true;
                    match.UpdatedAt = DateTime.UtcNow;

                    if (!kind.IsTemplate())
                    {
                        await UpsertRouteAsync(siteId, routePath, defaultCulture, RouteType.Page, match.Id, ct);
                    }
                    else
                    {
                        await RemoveRouteIfExistsAsync(routePath, defaultCulture, ct);
                    }
                    updated++;
                }
                else
                {
                    if (!kind.IsTemplate())
                    {
                        await UpsertRouteAsync(siteId, routePath, defaultCulture, RouteType.Page, match.Id, ct);
                    }
                    else
                    {
                        await RemoveRouteIfExistsAsync(routePath, defaultCulture, ct);
                    }
                    unchanged++;
                }
            }
        }

        return (new ApplyEntry("Pages", created, updated, unchanged), pageIdBySlug);
    }

    private async Task RemoveRouteIfExistsAsync(string path, string culture, CancellationToken ct)
    {
        var normalized = IRouteRegistry.NormalizePath(path);
        culture = culture.Trim().ToLowerInvariant();

        var pending = _db.ChangeTracker.Entries<SiteRoute>()
            .Where(e => e.State == EntityState.Added)
            .FirstOrDefault(e => e.Entity.Path == normalized && e.Entity.Culture == culture);
        if (pending is not null)
        {
            _db.SiteRoutes.Remove(pending.Entity);
            return;
        }

        var existing = await _db.SiteRoutes
            .FirstOrDefaultAsync(r => r.Path == normalized && r.Culture == culture, ct);
        if (existing is not null)
        {
            _db.SiteRoutes.Remove(existing);
        }
    }

    private async Task<ApplyEntry> ApplyMenusAsync(
        Guid siteId, IReadOnlyList<MenuSpec>? specs, CancellationToken ct)
    {
        int created = 0, updated = 0, unchanged = 0;
        if (specs is null || specs.Count == 0) return new ApplyEntry("Menus", 0, 0, 0);

        var existing = await _db.Menus.Include(m => m.Items).ToListAsync(ct);

        foreach (var s in specs)
        {
            if (string.IsNullOrWhiteSpace(s.Location)) continue;

            var match = existing.FirstOrDefault(m => m.Location.Equals(s.Location, StringComparison.OrdinalIgnoreCase));
            var specItems = s.Items ?? Array.Empty<MenuItemSpec>();

            if (match is null)
            {
                var menu = new Menu
                {
                    SiteId = siteId,
                    Name = string.IsNullOrWhiteSpace(s.Name) ? s.Location.Trim() : s.Name.Trim(),
                    Location = s.Location.Trim()
                };
                _db.Menus.Add(menu);

                foreach (var item in specItems)
                    _db.MenuItems.Add(NewMenuItem(menu.Id, item));

                created++;
            }
            else if (MenuDiffers(match, s, specItems))
            {
                match.Name = string.IsNullOrWhiteSpace(s.Name) ? match.Name : s.Name.Trim();
                match.UpdatedAt = DateTime.UtcNow;

                // Menu là danh sách có thứ tự — thay trọn bộ item đơn giản và đúng hơn diff từng cái.
                _db.MenuItems.RemoveRange(match.Items);
                foreach (var item in specItems)
                    _db.MenuItems.Add(NewMenuItem(match.Id, item));

                updated++;
            }
            else unchanged++;
        }

        return new ApplyEntry("Menus", created, updated, unchanged);
    }

    private async Task<ApplyEntry> ApplySettingsAsync(
        Guid siteId, IReadOnlyList<SiteSettingSpec>? specs, CancellationToken ct)
    {
        int created = 0, updated = 0, unchanged = 0;
        if (specs is null || specs.Count == 0) return new ApplyEntry("Settings", 0, 0, 0);

        var existing = await _db.SiteSettings.ToListAsync(ct);

        foreach (var s in specs)
        {
            if (string.IsNullOrWhiteSpace(s.Key)) continue;

            var group = string.IsNullOrWhiteSpace(s.Group) ? "general" : s.Group.Trim();
            var match = existing.FirstOrDefault(x => x.Key.Equals(s.Key, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                _db.SiteSettings.Add(new SiteSetting
                {
                    SiteId = siteId,
                    Key = s.Key.Trim(),
                    Value = s.Value,
                    Group = group,
                    UpdatedAt = DateTime.UtcNow
                });
                created++;
            }
            else if (match.Value != s.Value || match.Group != group)
            {
                match.Value = s.Value;
                match.Group = group;
                match.UpdatedAt = DateTime.UtcNow;
                updated++;
            }
            else unchanged++;
        }

        return new ApplyEntry("Settings", created, updated, unchanged);
    }

    /// <summary>Số version trống kế tiếp cho PageRevisions của một trang.</summary>
    private async Task<int> NextRevisionVersionAsync(Guid pageId, int preferred, CancellationToken ct)
    {
        var max = await _db.PageRevisions.AsNoTracking()
            .Where(r => r.PageId == pageId)
            .Select(r => (int?)r.Version)
            .MaxAsync(ct) ?? 0;
        return preferred > max ? preferred : max + 1;
    }

    /// <summary>
    /// Tạo SiteRoute nếu chưa có; đã có thì trỏ lại đúng target — không nhân bản route khi
    /// ApplyAsync được gọi lại nhiều lần với cùng spec.
    /// </summary>
    private async Task UpsertRouteAsync(
        Guid siteId, string path, string culture, RouteType type, Guid targetId, CancellationToken ct)
    {
        var normalized = IRouteRegistry.NormalizePath(path);

        // RouteRegistry.ResolveAsync so khớp culture đã lowercase; ghi nguyên trạng (ví dụ "vi-VN")
        // sẽ tạo route không bao giờ resolve được.
        culture = culture.Trim().ToLowerInvariant();

        // Route vừa Add trong cùng transaction chưa nằm trong DB → phải soi cả ChangeTracker,
        // nếu không sẽ tạo hai route trùng path trong một lần Apply.
        var pending = _db.ChangeTracker.Entries<SiteRoute>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .FirstOrDefault(r => r.Path == normalized && r.Culture == culture);
        if (pending is not null)
        {
            pending.RouteType = type;
            pending.TargetId = targetId;
            return;
        }

        var existing = await _db.SiteRoutes
            .FirstOrDefaultAsync(r => r.Path == normalized && r.Culture == culture, ct);

        if (existing is null)
        {
            _db.SiteRoutes.Add(new SiteRoute
            {
                SiteId = siteId,
                Path = normalized,
                Culture = culture,
                RouteType = type,
                TargetId = targetId,
                IsPrimary = true
            });
            return;
        }

        existing.RouteType = type;
        existing.TargetId = targetId;
        existing.IsPrimary = true;
    }

    // ── Export ──────────────────────────────────────────────────────────────────

    public async Task<Result<SiteSpec>> ExportAsync(CancellationToken ct = default)
    {
        var site = await _db.Sites.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == _currentSite.SiteId, ct);
        if (site is null) return Result<SiteSpec>.Failure("Site không tồn tại.");

        var layouts = await _db.SiteLayouts.AsNoTracking().Where(l => !l.IsDeleted)
            .OrderBy(l => l.Key)
            .Select(l => new LayoutSpec(l.Key, l.Name, l.Kind.ToString(), l.CompiledHtml, l.CompiledCss, l.CustomJs, l.IsDefault, l.CustomCss))
            .ToListAsync(ct);

        // Page tham chiếu layout bằng Key trong spec → cần map ngược LayoutId → Key.
        var layoutKeyById = await _db.SiteLayouts.AsNoTracking().Where(l => !l.IsDeleted)
            .ToDictionaryAsync(l => l.Id, l => l.Key, ct);

        var pageRows = await _db.Pages.AsNoTracking().Where(p => !p.IsDeleted)
            .OrderBy(p => p.Slug)
            .Select(p => new { p.Id, p.Title, p.Slug, p.CompiledHtml, p.CompiledCss, p.CustomCss, p.CustomJs, p.Kind, p.LayoutId, p.ParentPageId, p.IsDefaultTemplate })
            .ToListAsync(ct);

        var pageSlugById = pageRows.ToDictionary(p => p.Id, p => p.Slug);

        var pages = pageRows
            .Select(p => new PageSpec(
                p.Title, p.Slug, p.CompiledHtml, p.CompiledCss, p.CustomCss, p.CustomJs, p.Kind.ToString(),
                p.LayoutId is { } id && layoutKeyById.TryGetValue(id, out var key) ? key : null,
                p.ParentPageId is { } parentId && pageSlugById.TryGetValue(parentId, out var parentSlug) ? parentSlug : null,
                p.IsDefaultTemplate))
            .ToList();

        var categoryRows = await _db.Categories.AsNoTracking()
            .OrderBy(c => c.Order).ThenBy(c => c.Slug)
            .Select(c => new { c.Name, c.Slug, c.Description, c.Type, c.Order, c.TemplatePageId })
            .ToListAsync(ct);

        var categories = categoryRows
            .Select(c => new CategorySpec(
                c.Name, c.Slug, c.Description, c.Type.ToString(), c.Order,
                c.TemplatePageId is { } tId && pageSlugById.TryGetValue(tId, out var tSlug) ? tSlug : null))
            .ToList();

        var menuRows = await _db.Menus.AsNoTracking().Include(m => m.Items)
            .OrderBy(m => m.Location)
            .ToListAsync(ct);

        var menus = menuRows
            .Select(m => new MenuSpec(
                m.Name, m.Location,
                m.Items.OrderBy(i => i.Order)
                    .Select(i => new MenuItemSpec(i.Title, i.Url, i.Order, i.Target))
                    .ToList()))
            .ToList();

        var tokens = await _db.SiteDesignTokens.AsNoTracking()
            .OrderBy(t => t.Group).ThenBy(t => t.SortOrder).ThenBy(t => t.Key)
            .Select(t => new DesignTokenSpec(t.Group.ToString(), t.Key, t.Value, t.SortOrder))
            .ToListAsync(ct);

        var settings = await _db.SiteSettings.AsNoTracking()
            .OrderBy(s => s.Group).ThenBy(s => s.Key)
            .Select(s => new SiteSettingSpec(s.Key, s.Value, s.Group))
            .ToListAsync(ct);

        return Result<SiteSpec>.Success(new SiteSpec(
            site.Name, site.Slug, site.PrimaryDomain, site.DefaultTheme,
            site.DefaultCulture, site.SupportedCultures,
            layouts, pages, categories, menus, tokens, settings));
    }

    // ── Preview ─────────────────────────────────────────────────────────────────

    public async Task<Result<string>> PreviewAsync(
        Guid pageId, string? culture = null, string? sampleSlug = null, CancellationToken ct = default)
    {
        var page = await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null || page.IsDeleted) return Result<string>.Failure("Trang không tồn tại.");

        var resolvedCulture = culture;
        if (string.IsNullOrWhiteSpace(resolvedCulture))
        {
            resolvedCulture = await _db.Sites.AsNoTracking()
                .Where(s => s.Id == _currentSite.SiteId)
                .Select(s => s.DefaultCulture)
                .FirstOrDefaultAsync(ct) ?? "vi";
        }

        // Nếu là trang template, ưu tiên nạp thực thể mẫu để render với đầy đủ ngữ cảnh
        if (page.Kind.IsTemplate())
        {
            RenderedPage? preview = null;
            if (page.Kind == PageKind.PostTemplate)
            {
                var query = _db.Posts.AsNoTracking().Where(p => !p.IsDeleted && p.Status == PostStatus.Published);
                var post = !string.IsNullOrWhiteSpace(sampleSlug)
                    ? await query.FirstOrDefaultAsync(p => p.Slug == sampleSlug.Trim(), ct)
                    : await query.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt).FirstOrDefaultAsync(ct);

                if (post is not null)
                {
                    preview = await _renderer.RenderTemplatePreviewAsync(pageId, RouteType.Post, post.Id, resolvedCulture, ct);
                }
            }
            else if (page.Kind == PageKind.ProductTemplate)
            {
                var query = _db.Products.AsNoTracking().Where(p => !p.IsDeleted && p.Status == ProductStatus.Published);
                var product = !string.IsNullOrWhiteSpace(sampleSlug)
                    ? await query.FirstOrDefaultAsync(p => p.Slug == sampleSlug.Trim(), ct)
                    : await query.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt).FirstOrDefaultAsync(ct);

                if (product is not null)
                {
                    preview = await _renderer.RenderTemplatePreviewAsync(pageId, RouteType.Product, product.Id, resolvedCulture, ct);
                }
            }
            else if (page.Kind == PageKind.CategoryTemplate)
            {
                var query = _db.Categories.AsNoTracking().Where(c => c.IsActive);
                var cat = !string.IsNullOrWhiteSpace(sampleSlug)
                    ? await query.FirstOrDefaultAsync(c => c.Slug == sampleSlug.Trim(), ct)
                    : await query.OrderBy(c => c.Order).FirstOrDefaultAsync(ct);

                if (cat is not null)
                {
                    preview = await _renderer.RenderTemplatePreviewAsync(pageId, RouteType.Category, cat.Id, resolvedCulture, ct);
                }
            }

            if (preview is not null)
            {
                return Result<string>.Success(preview.Html);
            }
        }

        var rendered = await _renderer.RenderAsync(pageId, resolvedCulture, ct);
        if (rendered is null)
            return Result<string>.Failure("Trang chưa xuất bản nên không render được. Publish trước rồi thử lại.");

        return Result<string>.Success(rendered.Html);
    }

    // ── CRUD mỏng: uỷ quyền cho service sẵn có, gom về một bề mặt cho agent ─────

    public Task<PagedList<BuilderPageDto>> ListPagesAsync(int page, int pageSize, string? search, CancellationToken ct = default)
        => _pages.ListAsync(Math.Max(1, page), Clamp(pageSize, 1, 100), search, ct);

    public Task<Result<BuilderPageDto>> GetPageAsync(Guid id, CancellationToken ct = default)
        => _pages.GetByIdAsync(id, ct);

    public Task<Result<BuilderPageDto>> SavePageAsync(Guid? id, BuilderPageSaveRequest request, CancellationToken ct = default)
        => id is { } pageId ? _pages.UpdateAsync(pageId, request, ct) : _pages.CreateAsync(request, ct);

    public Task<Result<BuilderPageDto>> PublishPageAsync(Guid id, string? note, CancellationToken ct = default)
        => _pages.PublishAsync(id, new BuilderPagePublishRequest(note), ct);

    public Task<Result> DeletePageAsync(Guid id, CancellationToken ct = default)
        => _pages.DeleteAsync(id, ct);

    public Task<IReadOnlyList<SiteLayoutDto>> ListLayoutsAsync(CancellationToken ct = default)
        => _layouts.ListAsync(ct);

    public Task<Result<SiteLayoutDto>> SaveLayoutAsync(Guid? id, SiteLayoutSaveRequest request, CancellationToken ct = default)
        => id is { } layoutId ? _layouts.UpdateAsync(layoutId, request, ct) : _layouts.CreateAsync(request, ct);

    public Task<PagedList<BuilderCategoryDto>> ListCategoriesAsync(int page, int pageSize, string? search, CancellationToken ct = default)
        => _categories.ListAsync(Math.Max(1, page), Clamp(pageSize, 1, 100), search, ct);

    public Task<Result<BuilderCategoryDto>> SaveCategoryAsync(Guid? id, BuilderCategorySaveRequest request, CancellationToken ct = default)
        => id is { } catId ? _categories.UpdateAsync(catId, request, ct) : _categories.CreateAsync(request, ct);

    public async Task<IReadOnlyList<BlockInfo>> ListBlocksAsync(CancellationToken ct = default)
    {
        // BlockDefinition không ISiteScoped (SiteId nullable = khối toàn cục) nên phải lọc tay.
        var siteId = _currentSite.SiteId;
        var defs = await _db.BlockDefinitions.AsNoTracking()
            .Where(b => !b.IsDeleted && (b.SiteId == null || b.SiteId == siteId))
            .OrderBy(b => b.Category).ThenBy(b => b.Key)
            .Select(b => new BlockInfo(b.Key, b.Name, b.Category, b.Kind.ToString(), b.PropsSchemaJson))
            .ToListAsync(ct);

        // BlockDefinition lưu tay có thể để trống schema; khối cùng key trong registry đã tự mô tả
        // mình nên lấy schema từ đó thay vì trả null.
        for (var i = 0; i < defs.Count; i++)
        {
            if (defs[i].PropsSchemaJson is not null) continue;
            if (_blockRegistry.Get(defs[i].Key) is { } dyn)
                defs[i] = defs[i] with { PropsSchemaJson = BlockPropsSchema.Build(dyn.Descriptor) };
        }

        // Dynamic block code-first chưa có BlockDefinition tương ứng vẫn phải hiện ra cho agent,
        // kèm schema props sinh từ Descriptor — không có schema thì agent phải đoán tên prop, đoán
        // sai là khối render rỗng mà không báo lỗi.
        var known = defs.Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extra = _blockRegistry.All
            .Where(b => !known.Contains(b.Key))
            .Select(b => new BlockInfo(
                b.Key,
                b.Descriptor.Label,
                b.Descriptor.Category,
                nameof(BlockKind.Dynamic),
                BlockPropsSchema.Build(b.Descriptor)));

        return defs.Concat(extra).ToList();
    }

    public async Task<IReadOnlyList<DesignTokenSpec>> ListDesignTokensAsync(CancellationToken ct = default)
        => await _db.SiteDesignTokens.AsNoTracking()
            .OrderBy(t => t.Group).ThenBy(t => t.SortOrder).ThenBy(t => t.Key)
            .Select(t => new DesignTokenSpec(t.Group.ToString(), t.Key, t.Value, t.SortOrder))
            .ToListAsync(ct);

    public async Task<Result> SaveDesignTokensAsync(IReadOnlyList<DesignTokenSpec> tokens, CancellationToken ct = default)
    {
        if (tokens is null || tokens.Count == 0) return Result.Failure("Danh sách token rỗng.");

        await ApplyDesignTokensAsync(_currentSite.SiteId, tokens, ct);
        await _db.SaveChangesAsync(ct);
        // CSS token được DesignTokenCssBuilder cache theo SiteCacheSignal — không invalidate thì
        // màu vừa lưu vẫn không đổi trên trang public tới khi cache bị drop vì lý do khác.
        _signal.Invalidate();
        return Result.Success();
    }

    public async Task<Result> DeleteDesignTokenAsync(string group, string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return Result.Failure("Thiếu key token cần xoá.");

        var g = ParseEnum(group, DesignTokenGroup.Color);
        // SiteDesignTokens ít bản ghi nên nạp cả rồi khớp OrdinalIgnoreCase giống
        // ApplyDesignTokensAsync, không phụ thuộc collation của DB.
        var token = (await _db.SiteDesignTokens.ToListAsync(ct))
            .FirstOrDefault(t => t.Group == g && t.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));
        if (token is null) return Result.Failure($"Không tìm thấy token {group}/{key}.");

        _db.SiteDesignTokens.Remove(token);
        await _db.SaveChangesAsync(ct);
        _signal.Invalidate();
        return Result.Success();
    }

    public Task<SiteCustomCodeDto> GetCustomCodeAsync(CancellationToken ct = default)
        => _customCode.GetAsync(ct);

    public Task<Result<SiteCustomCodeDto>> SaveCustomCodeAsync(SiteCustomCodeSaveRequest request, CancellationToken ct = default)
        => _customCode.SaveAsync(request, ct);

    public Task<Result<SeoMetaDto>> SaveSeoMetaAsync(SeoMetaSaveRequest request, CancellationToken ct = default)
        => _seoMeta.SaveAsync(request, ct);

    public async Task<IReadOnlyList<MediaBriefDto>> ListMediaAsync(int limit, CancellationToken ct = default)
        => await _db.Medias.AsNoTracking()
            .Where(m => !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Clamp(limit, 1, 200))
            .Select(m => new MediaBriefDto(m.Id, m.FileName, m.FilePath, m.AltText, m.Width, m.Height))
            .ToListAsync(ct);

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static MenuItem NewMenuItem(Guid menuId, MenuItemSpec spec) => new()
    {
        MenuId = menuId,
        Title = spec.Title,
        Url = spec.Url,
        Order = spec.Order,
        Target = spec.Target
    };

    private static bool MenuDiffers(Menu menu, MenuSpec spec, IReadOnlyList<MenuItemSpec> specItems)
    {
        if (!string.IsNullOrWhiteSpace(spec.Name) && menu.Name != spec.Name) return true;
        if (menu.Items.Count != specItems.Count) return true;

        var current = menu.Items.OrderBy(i => i.Order).ToList();
        var wanted = specItems.OrderBy(i => i.Order).ToList();

        for (var i = 0; i < current.Count; i++)
        {
            if (current[i].Title != wanted[i].Title
                || current[i].Url != wanted[i].Url
                || current[i].Order != wanted[i].Order
                || current[i].Target != wanted[i].Target) return true;
        }

        return false;
    }

    private static bool IsHomeSlug(string? slug) =>
        string.IsNullOrWhiteSpace(slug)
        || slug.Equals("home", StringComparison.OrdinalIgnoreCase)
        || slug.Equals("index", StringComparison.OrdinalIgnoreCase);

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : fallback;

    private static IEnumerable<string> ParseCsv(string? csv)
        => (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    /// <summary>Slug tối giản từ tiêu đề, dùng khi agent quên truyền Slug.</summary>
    private static string Slugify(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Guid.NewGuid().ToString("n")[..8];

        var normalized = title.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-');

        var slug = new string(chars.ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');

        return string.IsNullOrEmpty(slug) ? Guid.NewGuid().ToString("n")[..8] : slug;
    }
}
