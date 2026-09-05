using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Builder;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Seo;
using NewsCMS.Domain.Entities.Site;
using DomainSite = NewsCMS.Domain.Entities.Site.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Clone SiteTemplate → site mới. Chạy NGOÀI ngữ cảnh site hiện tại nên phải bật
/// AppDbContext.BypassSiteScope và gán SiteId tường minh cho mọi entity (giống DbSeeder).
/// Toàn bộ trong một transaction; lỗi giữa chừng → rollback sạch, không để site nửa vời.
/// </summary>
public sealed class SiteTemplateService : ISiteTemplateService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _db;
    private readonly ContentSanitizer _sanitizer;
    private readonly SiteCacheSignal _cacheSignal;

    public SiteTemplateService(AppDbContext db, ContentSanitizer sanitizer, SiteCacheSignal cacheSignal)
    {
        _db = db;
        _sanitizer = sanitizer;
        _cacheSignal = cacheSignal;
    }

    public async Task<IReadOnlyList<SiteTemplateDto>> ListAsync(CancellationToken ct = default)
    {
        // SiteTemplate là bảng toàn cục (không ISiteScoped) nên không bị query filter.
        return await _db.SiteTemplates.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new SiteTemplateDto(t.Id, t.Key, t.Name, t.Description))
            .ToListAsync(ct);
    }

    public async Task<SiteSpec?> GetSpecAsync(Guid templateId, CancellationToken ct = default)
    {
        var template = await _db.SiteTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null) return null;

        return ParseSpec(template.SpecJson);
    }

    public async Task<Result<Guid>> CloneToSiteAsync(
        Guid templateId, string siteName, string siteSlug, string? primaryDomain, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(siteName))
            return Result<Guid>.Failure("Tên site không được để trống.");
        if (string.IsNullOrWhiteSpace(siteSlug))
            return Result<Guid>.Failure("Slug site không được để trống.");

        var template = await _db.SiteTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return Result<Guid>.Failure("Template không tồn tại.");

        var spec = ParseSpec(template.SpecJson);
        if (spec is null)
            return Result<Guid>.Failure("SpecJson của template không hợp lệ.");

        // Tạo dữ liệu cho site KHÁC site hiện tại → phải tắt scope, tự gán SiteId.
        var previousBypass = _db.BypassSiteScope;
        _db.BypassSiteScope = true;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (await _db.Sites.AnyAsync(s => s.Slug == siteSlug, ct))
                return Result<Guid>.Failure($"Slug site '{siteSlug}' đã tồn tại.");

            var normalizedHost = SiteHostNormalizer.Normalize(primaryDomain);
            if (!string.IsNullOrWhiteSpace(normalizedHost)
                && await _db.SiteDomains.AnyAsync(d => d.Host == normalizedHost, ct))
                return Result<Guid>.Failure($"Host '{normalizedHost}' đã được dùng.");

            // 1. Site + domain
            var site = new DomainSite
            {
                Name = siteName.Trim(),
                Slug = siteSlug.Trim(),
                PrimaryDomain = string.IsNullOrWhiteSpace(normalizedHost) ? null : normalizedHost,
                DefaultTheme = string.IsNullOrWhiteSpace(spec.DefaultTheme) ? "Universal" : spec.DefaultTheme,
                DefaultCulture = string.IsNullOrWhiteSpace(spec.DefaultCulture) ? "vi" : spec.DefaultCulture,
                SupportedCultures = string.IsNullOrWhiteSpace(spec.SupportedCultures) ? "vi" : spec.SupportedCultures,
                IsActive = true
            };
            _db.Sites.Add(site);
            await _db.SaveChangesAsync(ct);

            if (!string.IsNullOrWhiteSpace(normalizedHost))
            {
                _db.SiteDomains.Add(new SiteDomain
                {
                    SiteId = site.Id,
                    Host = normalizedHost,
                    IsPrimary = true
                });
            }

            // 2. Design tokens
            foreach (var t in spec.DesignTokens ?? Array.Empty<DesignTokenSpec>())
            {
                _db.SiteDesignTokens.Add(new SiteDesignToken
                {
                    SiteId = site.Id,
                    Group = Enum.TryParse<DesignTokenGroup>(t.Group, true, out var g) ? g : DesignTokenGroup.Color,
                    Key = t.Key,
                    Value = t.Value,
                    SortOrder = t.SortOrder
                });
            }

            // 3. Layouts — giữ map Key → Id để page tham chiếu LayoutKey.
            var layoutIdByKey = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in spec.Layouts ?? Array.Empty<LayoutSpec>())
            {
                var layout = new SiteLayout
                {
                    SiteId = site.Id,
                    Key = l.Key,
                    Name = l.Name,
                    Kind = Enum.TryParse<LayoutKind>(l.Kind, true, out var k) ? k : LayoutKind.Shell,
                    CompiledHtml = _sanitizer.SanitizeBuilder(l.CompiledHtml),
                    CompiledCss = l.CompiledCss,
                    CustomCss = l.CustomCss,
                    CustomJs = l.CustomJs,
                    IsDefault = l.IsDefault
                };
                _db.SiteLayouts.Add(layout);
                layoutIdByKey[l.Key] = layout.Id;
            }

            // 4. Pages (+ route). Page publish sẵn để site mới chạy được ngay.
            var pageIdBySlug = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var orderedPages = (spec.Pages ?? Array.Empty<PageSpec>())
                .OrderBy(p => string.IsNullOrWhiteSpace(p.ParentSlug) ? 0 : 1)
                .ToList();

            foreach (var p in orderedPages)
            {
                Guid? layoutId = null;
                if (!string.IsNullOrWhiteSpace(p.LayoutKey) && layoutIdByKey.TryGetValue(p.LayoutKey, out var lid))
                    layoutId = lid;

                Guid? parentPageId = null;
                if (!string.IsNullOrWhiteSpace(p.ParentSlug) && pageIdBySlug.TryGetValue(p.ParentSlug.Trim(), out var pid))
                    parentPageId = pid;

                var kind = Enum.TryParse<PageKind>(p.Kind, true, out var pk) ? pk : PageKind.Landing;

                var page = new Page
                {
                    SiteId = site.Id,
                    Title = p.Title,
                    Slug = p.Slug,
                    Content = string.Empty,
                    LayoutId = layoutId,
                    CompiledHtml = _sanitizer.SanitizeBuilder(p.CompiledHtml),
                    CompiledCss = p.CompiledCss,
                    CustomCss = p.CustomCss,
                    CustomJs = p.CustomJs,
                    Kind = kind,
                    ParentPageId = parentPageId,
                    IsDefaultTemplate = p.IsDefaultTemplate,
                    Status = BuilderPageStatus.Published,
                    IsPublished = true,
                    PublishedAt = DateTime.UtcNow,
                    Version = 1
                };
                _db.Pages.Add(page);
                pageIdBySlug[p.Slug] = page.Id;

                // Trang template không có URL riêng
                if (!kind.IsTemplate())
                {
                    _db.SiteRoutes.Add(new SiteRoute
                    {
                        SiteId = site.Id,
                        // Trang chủ dùng slug "home"/"index"/rỗng → path "/".
                        Path = IsHomeSlug(p.Slug) ? "/" : "/" + p.Slug,
                        Culture = site.DefaultCulture,
                        RouteType = RouteType.Page,
                        TargetId = page.Id,
                        IsPrimary = true
                    });
                }
            }

            // 5. Categories (+ route)
            foreach (var c in spec.Categories ?? Array.Empty<CategorySpec>())
            {
                Guid? templatePageId = null;
                if (!string.IsNullOrWhiteSpace(c.TemplatePageSlug) && pageIdBySlug.TryGetValue(c.TemplatePageSlug.Trim(), out var tid))
                    templatePageId = tid;

                var cat = new Category
                {
                    SiteId = site.Id,
                    Name = c.Name,
                    Slug = c.Slug,
                    Description = c.Description,
                    Type = Enum.TryParse<CategoryType>(c.Type, true, out var ct2) ? ct2 : CategoryType.Post,
                    PathSlug = c.Slug,
                    Order = c.Order,
                    TemplatePageId = templatePageId,
                    IsActive = true
                };
                _db.Categories.Add(cat);

                _db.SiteRoutes.Add(new SiteRoute
                {
                    SiteId = site.Id,
                    Path = "/" + c.Slug,
                    Culture = site.DefaultCulture,
                    RouteType = RouteType.Category,
                    TargetId = cat.Id,
                    IsPrimary = true
                });
            }

            // 6. Menus
            foreach (var m in spec.Menus ?? Array.Empty<MenuSpec>())
            {
                var menu = new Menu
                {
                    SiteId = site.Id,
                    Name = m.Name,
                    Location = m.Location
                };
                _db.Menus.Add(menu);

                foreach (var item in m.Items ?? Array.Empty<MenuItemSpec>())
                {
                    _db.MenuItems.Add(new MenuItem
                    {
                        MenuId = menu.Id,
                        Title = item.Title,
                        Url = item.Url,
                        Order = item.Order,
                        Target = item.Target
                    });
                }
            }

            // 7. Site settings
            foreach (var s in spec.Settings ?? Array.Empty<SiteSettingSpec>())
            {
                _db.SiteSettings.Add(new SiteSetting
                {
                    SiteId = site.Id,
                    Key = s.Key,
                    Value = s.Value,
                    Group = string.IsNullOrWhiteSpace(s.Group) ? "general" : s.Group,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Domain mapping mới → drop cache resolver để host có hiệu lực ngay.
            _cacheSignal.Invalidate();

            return Result<Guid>.Success(site.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return Result<Guid>.Failure($"Lỗi khi tạo site từ template: {ex.Message}");
        }
        finally
        {
            _db.BypassSiteScope = previousBypass;
        }
    }

    private static bool IsHomeSlug(string? slug) =>
        string.IsNullOrWhiteSpace(slug)
        || slug.Equals("home", StringComparison.OrdinalIgnoreCase)
        || slug.Equals("index", StringComparison.OrdinalIgnoreCase);

    private static SiteSpec? ParseSpec(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SiteSpec>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
