using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.I18n;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// CRUD bản dịch trang. Mỗi (PageId, Culture) là unique. Khi lưu translation cũng tạo/cập nhật
/// SiteRoute tương ứng để renderer resolve đúng path theo culture.
/// </summary>
public sealed class PageTranslationService : IPageTranslationService
{
    private readonly AppDbContext _db;
    private readonly ContentSanitizer _sanitizer;

    public PageTranslationService(AppDbContext db, ContentSanitizer sanitizer)
    {
        _db = db;
        _sanitizer = sanitizer;
    }

    public async Task<IReadOnlyList<PageTranslationDto>> ListByPageAsync(Guid pageId, CancellationToken ct = default)
    {
        return await _db.PageTranslations.AsNoTracking()
            .Where(t => t.PageId == pageId)
            .OrderBy(t => t.Culture)
            .Select(t => new PageTranslationDto(t.Id, t.PageId, t.Culture, t.Title, t.Slug, t.BuilderJson, t.CompiledHtml))
            .ToListAsync(ct);
    }

    public async Task<PageTranslationDto?> GetAsync(Guid pageId, string culture, CancellationToken ct = default)
    {
        var t = await _db.PageTranslations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PageId == pageId && x.Culture == culture, ct);
        return t is null ? null : new PageTranslationDto(t.Id, t.PageId, t.Culture, t.Title, t.Slug, t.BuilderJson, t.CompiledHtml);
    }

    public async Task<Result<PageTranslationDto>> SaveAsync(PageTranslationSaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<PageTranslationDto>.Failure("Tiêu đề không được để trống.");
        if (string.IsNullOrWhiteSpace(request.Slug))
            return Result<PageTranslationDto>.Failure("Slug không được để trống.");

        var existing = await _db.PageTranslations
            .FirstOrDefaultAsync(t => t.PageId == request.PageId && t.Culture == request.Culture, ct);

        if (existing is null)
        {
            existing = new PageTranslation
            {
                PageId = request.PageId,
                Culture = request.Culture.Trim().ToLowerInvariant()
            };
            _db.PageTranslations.Add(existing);
        }

        existing.Title = request.Title.Trim();
        existing.Slug = request.Slug.Trim();
        existing.BuilderJson = request.BuilderJson;
        existing.CompiledHtml = _sanitizer.SanitizeBuilder(request.CompiledHtml);
        existing.CompiledCss = request.CompiledCss;
        existing.CustomJs = request.CustomJs;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // Sync SiteRoute cho translation (path = /{culture}/{slug} hoặc /{slug} nếu culture = default).
        await SyncTranslationRouteAsync(existing, ct);

        return Result<PageTranslationDto>.Success(
            new PageTranslationDto(existing.Id, existing.PageId, existing.Culture, existing.Title, existing.Slug, existing.BuilderJson, existing.CompiledHtml));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _db.PageTranslations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return Result.Failure("Bản dịch không tồn tại.");

        // Xóa route tương ứng.
        var route = await _db.SiteRoutes
            .FirstOrDefaultAsync(r => r.RouteType == Domain.Enums.RouteType.Page && r.TargetId == t.PageId && r.Culture == t.Culture, ct);
        if (route is not null)
            _db.SiteRoutes.Remove(route);

        _db.PageTranslations.Remove(t);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task SyncTranslationRouteAsync(PageTranslation translation, CancellationToken ct)
    {
        var page = await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == translation.PageId, ct);
        if (page is null) return;

        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == page.SiteId, ct);
        var defaultCulture = site?.DefaultCulture ?? "vi";

        // Nếu culture = default → path = /{slug}; khác default → path = /{culture}/{slug}.
        var path = string.Equals(translation.Culture, defaultCulture, StringComparison.OrdinalIgnoreCase)
            ? "/" + translation.Slug
            : $"/{translation.Culture}/{translation.Slug}";

        var existing = await _db.SiteRoutes
            .FirstOrDefaultAsync(r => r.RouteType == Domain.Enums.RouteType.Page && r.TargetId == translation.PageId && r.Culture == translation.Culture, ct);

        if (existing is null)
        {
            _db.SiteRoutes.Add(new Domain.Entities.Seo.SiteRoute
            {
                SiteId = page.SiteId,
                Path = path,
                Culture = translation.Culture,
                RouteType = Domain.Enums.RouteType.Page,
                TargetId = translation.PageId,
                IsPrimary = false // Bản gốc mới là primary
            });
        }
        else if (!string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            _db.Redirects.Add(new Domain.Entities.Seo.Redirect
            {
                SiteId = page.SiteId,
                FromPath = existing.Path,
                ToPath = path,
                StatusCode = 301,
                IsActive = true
            });
            existing.Path = path;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}
