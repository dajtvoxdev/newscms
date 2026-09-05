using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.ContentTypes;

/// <summary>
/// Triển khai IContentType cho loại nội dung "Chuyên mục" (Category).
/// Đọc dữ liệu từ bảng Categories, lấy thông tin phân cấp PathSlug và TemplatePageId riêng nếu có.
/// </summary>
public sealed class CategoryContentType : IContentType
{
    public string Key => "category";
    public string DisplayName => "Chuyên mục";
    public RouteType RouteType => RouteType.Category;
    public PageKind TemplateKind => PageKind.CategoryTemplate;

    private readonly AppDbContext _db;

    public CategoryContentType(AppDbContext db) => _db = db;

    public async Task<ContentDetail?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        var cat = await _db.Categories.AsNoTracking()
            .Where(c => c.Id == id && c.IsActive)
            .Select(c => new
            {
                c.Id,
                c.Slug,
                c.Name,
                c.Description,
                c.PathSlug,
                c.TemplatePageId,
                c.Type
            })
            .FirstOrDefaultAsync(ct);

        if (cat is null) return null;

        var extra = new Dictionary<string, object?>
        {
            ["TemplatePageId"] = cat.TemplatePageId,
            ["PathSlug"] = cat.PathSlug,
            ["Type"] = cat.Type
        };

        return new ContentDetail(
            Id: cat.Id,
            Slug: cat.Slug,
            Title: cat.Name,
            Body: null,
            Excerpt: cat.Description,
            ImageUrl: null,
            ImageWidth: null,
            ImageHeight: null,
            ImageAlt: null,
            PublishedAt: null,
            CategoryId: cat.Id,
            CategorySlug: cat.Slug,
            CategoryName: cat.Name,
            Extra: extra);
    }

    public async Task<string> BuildPathAsync(Guid id, CancellationToken ct = default)
    {
        var cat = await _db.Categories.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Slug, c.PathSlug })
            .FirstOrDefaultAsync(ct);

        if (cat is null) return "/";

        var segment = string.IsNullOrWhiteSpace(cat.PathSlug) ? cat.Slug : cat.PathSlug;
        return IRouteRegistry.NormalizePath("/" + segment);
    }

    public Task<string?> RenderFallbackHtmlAsync(ContentDetail detail, CancellationToken ct = default)
    {
        // Chuyên mục không có HTML dựng sẵn dự phòng — trả về null để caller trả về 404
        // nếu site chưa dựng template listing, đúng hành vi chuẩn từ trước đến nay.
        return Task.FromResult<string?>(null);
    }
}

