using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// CRUD chuyên mục mở rộng cho builder. Tự tính PathSlug từ cây phân cấp khi tạo/sửa.
/// Đổi slug → sync route (tạo redirect 301).
/// </summary>
public sealed class BuilderCategoryService : IBuilderCategoryService
{
    private readonly AppDbContext _db;
    private readonly IRouteRegistry _routeRegistry;

    public BuilderCategoryService(AppDbContext db, IRouteRegistry routeRegistry)
    {
        _db = db;
        _routeRegistry = routeRegistry;
    }

    public async Task<Result<BuilderCategoryDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cat = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat is null) return Result<BuilderCategoryDto>.Failure("Chuyên mục không tồn tại.");
        return Result<BuilderCategoryDto>.Success(Map(cat));
    }

    public async Task<PagedList<BuilderCategoryDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = _db.Categories.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c => c.Name.Contains(s) || c.Slug.Contains(s));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedList<BuilderCategoryDto>
        {
            Items = items.Select(Map).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task<Result<BuilderCategoryDto>> CreateAsync(BuilderCategorySaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<BuilderCategoryDto>.Failure("Tên chuyên mục không được để trống.");
        if (string.IsNullOrWhiteSpace(request.Slug))
            return Result<BuilderCategoryDto>.Failure("Slug không được để trống.");

        var slugExists = await _db.Categories.AnyAsync(c => c.Slug == request.Slug, ct);
        if (slugExists)
            return Result<BuilderCategoryDto>.Failure($"Slug '{request.Slug}' đã tồn tại.");

        var cat = new Category
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim(),
            Description = request.Description,
            Type = ParseType(request.Type),
            TemplatePageId = request.TemplatePageId,
            LayoutId = request.LayoutId,
            CoverImageId = request.CoverImageId,
            Color = request.Color,
            Icon = request.Icon,
            Order = request.Order,
            IsActive = request.IsActive
        };

        // Tính PathSlug (chưa có parent nên chỉ là slug).
        cat.PathSlug = cat.Slug;

        _db.Categories.Add(cat);
        await _db.SaveChangesAsync(ct);

        // Sync route cho category mới.
        await SyncCategoryRouteAsync(cat, ct);

        return Result<BuilderCategoryDto>.Success(Map(cat));
    }

    public async Task<Result<BuilderCategoryDto>> UpdateAsync(Guid id, BuilderCategorySaveRequest request, CancellationToken ct = default)
    {
        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat is null) return Result<BuilderCategoryDto>.Failure("Chuyên mục không tồn tại.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<BuilderCategoryDto>.Failure("Tên chuyên mục không được để trống.");

        if (!string.Equals(cat.Slug, request.Slug, StringComparison.OrdinalIgnoreCase))
        {
            var slugExists = await _db.Categories.AnyAsync(c => c.Slug == request.Slug && c.Id != id, ct);
            if (slugExists)
                return Result<BuilderCategoryDto>.Failure($"Slug '{request.Slug}' đã tồn tại.");
        }

        cat.Name = request.Name.Trim();
        cat.Slug = request.Slug.Trim();
        cat.Description = request.Description;
        cat.Type = ParseType(request.Type);
        cat.TemplatePageId = request.TemplatePageId;
        cat.LayoutId = request.LayoutId;
        cat.CoverImageId = request.CoverImageId;
        cat.Color = request.Color;
        cat.Icon = request.Icon;
        cat.Order = request.Order;
        cat.IsActive = request.IsActive;
        cat.UpdatedAt = DateTime.UtcNow;

        // Recalculate PathSlug.
        cat.PathSlug = cat.ParentId is { } parentId
            ? await BuildPathSlugAsync(parentId, cat.Slug, ct)
            : cat.Slug;

        await _db.SaveChangesAsync(ct);
        await SyncCategoryRouteAsync(cat, ct);

        return Result<BuilderCategoryDto>.Success(Map(cat));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat is null) return Result.Failure("Chuyên mục không tồn tại.");

        cat.IsActive = false;
        cat.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _routeRegistry.Invalidate();
        return Result.Success();
    }

    /// <summary>Xây PathSlug từ parent chain: grandparent-slug/parent-slug/my-slug.</summary>
    private async Task<string> BuildPathSlugAsync(Guid parentId, string mySlug, CancellationToken ct)
    {
        var segments = new List<string> { mySlug };
        var current = parentId;

        while (true)
        {
            var parent = await _db.Categories.AsNoTracking()
                .Where(c => c.Id == current)
                .Select(c => new { c.Slug, c.ParentId })
                .FirstOrDefaultAsync(ct);

            if (parent is null) break;
            segments.Insert(0, parent.Slug);
            if (parent.ParentId is null) break;
            current = parent.ParentId.Value;
        }

        return string.Join("/", segments);
    }

    /// <summary>Sync SiteRoute cho category (tạo mới hoặc cập nhật path, redirect nếu slug đổi).</summary>
    private async Task SyncCategoryRouteAsync(Category cat, CancellationToken ct)
    {
        var culture = "vi"; // Phase 5 sẽ hỗ trợ đa ngôn ngữ đầy đủ
        var newPath = "/" + (cat.PathSlug ?? cat.Slug);

        var existing = await _db.SiteRoutes
            .FirstOrDefaultAsync(r => r.RouteType == RouteType.Category && r.TargetId == cat.Id && r.Culture == culture, ct);

        if (existing is null)
        {
            _db.SiteRoutes.Add(new Domain.Entities.Seo.SiteRoute
            {
                SiteId = cat.SiteId,
                Path = newPath,
                Culture = culture,
                RouteType = RouteType.Category,
                TargetId = cat.Id,
                IsPrimary = true
            });
        }
        else if (!string.Equals(existing.Path, newPath, StringComparison.OrdinalIgnoreCase))
        {
            _db.Redirects.Add(new Domain.Entities.Seo.Redirect
            {
                SiteId = cat.SiteId,
                FromPath = existing.Path,
                ToPath = newPath,
                StatusCode = 301,
                IsActive = true
            });
            existing.Path = newPath;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            return;
        }

        await _db.SaveChangesAsync(ct);
        _routeRegistry.Invalidate();
    }

    private static BuilderCategoryDto Map(Category c) => new(
        c.Id, c.Name, c.Slug, c.Description, c.Type.ToString(),
        c.PathSlug, c.TemplatePageId, c.LayoutId, c.CoverImageId,
        c.Color, c.Icon, c.Order, c.IsActive);

    private static CategoryType ParseType(string? type) =>
        Enum.TryParse<CategoryType>(type, ignoreCase: true, out var t) ? t : CategoryType.Post;
}
