using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Seo;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// CRUD SEO meta + JSON-LD generation. SeoMeta đã có global query filter theo SiteId.
/// </summary>
public sealed class SeoMetaService : ISeoMetaService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AppDbContext _db;

    public SeoMetaService(AppDbContext db) => _db = db;

    public async Task<SeoMetaDto?> GetAsync(string entityType, Guid entityId, string culture, CancellationToken ct = default)
    {
        var meta = await _db.SeoMetas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.EntityType == entityType && m.EntityId == entityId && m.Culture == culture, ct);
        return meta is null ? null : Map(meta);
    }

    public async Task<Result<SeoMetaDto>> SaveAsync(SeoMetaSaveRequest request, CancellationToken ct = default)
    {
        var existing = await _db.SeoMetas
            .FirstOrDefaultAsync(m => m.EntityType == request.EntityType && m.EntityId == request.EntityId && m.Culture == request.Culture, ct);

        if (existing is null)
        {
            existing = new SeoMeta
            {
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                Culture = request.Culture
            };
            _db.SeoMetas.Add(existing);
        }

        existing.MetaTitle = request.MetaTitle;
        existing.MetaDescription = request.MetaDescription;
        existing.OgImage = request.OgImage;
        existing.Canonical = request.Canonical;
        existing.Robots = request.Robots;
        existing.SchemaJsonLd = request.SchemaJsonLd;
        existing.OgType = request.OgType;
        existing.TwitterCard = request.TwitterCard;
        existing.Priority = request.Priority;
        existing.ChangeFreq = request.ChangeFreq;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result<SeoMetaDto>.Success(Map(existing));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var meta = await _db.SeoMetas.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (meta is null) return Result.Failure("SEO meta không tồn tại.");
        _db.SeoMetas.Remove(meta);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<string?> GenerateJsonLdAsync(string entityType, Guid entityId, string culture, CancellationToken ct = default)
    {
        // Lấy meta nếu có schema override.
        var meta = await _db.SeoMetas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.EntityType == entityType && m.EntityId == entityId && m.Culture == culture, ct);

        if (!string.IsNullOrEmpty(meta?.SchemaJsonLd))
            return meta.SchemaJsonLd;

        // Auto-generate JSON-LD theo entity type.
        switch (entityType.ToLowerInvariant())
        {
            case "page":
                var page = await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == entityId, ct);
                if (page is null) return null;
                return JsonSerializer.Serialize(new
                {
                    at_context = "https://schema.org",
                    at_type = "WebPage",
                    name = page.Title,
                    description = meta?.MetaDescription
                }, JsonOpts).Replace("at_", "@");

            case "post":
                // Nạp thông tin bài viết kèm Category và Ảnh đại diện (FeaturedImage)
                var post = await _db.Posts.AsNoTracking()
                    .Include(p => p.Category)
                    .Include(p => p.FeaturedImage)
                    .FirstOrDefaultAsync(p => p.Id == entityId, ct);
                if (post is null) return null;

                var postImage = meta?.OgImage ?? post.FeaturedImage?.FilePath;
                return JsonSerializer.Serialize(new
                {
                    at_context = "https://schema.org",
                    at_type = "Article",
                    headline = meta?.MetaTitle ?? post.Title,
                    description = meta?.MetaDescription ?? post.Excerpt,
                    image = postImage,
                    datePublished = post.PublishedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    articleSection = post.Category?.Name
                }, JsonOpts).Replace("at_", "@");

            case "product":
                // Tự động sinh Schema.org Product với ưu đãi giá (Offer) và tình trạng kho
                var prod = await _db.Products.AsNoTracking()
                    .Include(p => p.ProductCategory)
                    .Include(p => p.Thumbnail)
                    .FirstOrDefaultAsync(p => p.Id == entityId, ct);
                if (prod is null) return null;

                var prodImage = meta?.OgImage ?? prod.Thumbnail?.FilePath;
                var price = prod.SalePrice ?? prod.Price;
                return JsonSerializer.Serialize(new
                {
                    at_context = "https://schema.org",
                    at_type = "Product",
                    name = meta?.MetaTitle ?? prod.Name,
                    description = meta?.MetaDescription ?? prod.ShortDescription ?? prod.Description,
                    image = prodImage,
                    sku = string.IsNullOrEmpty(prod.Sku) ? null : prod.Sku,
                    offers = new
                    {
                        at_type = "Offer",
                        price = price,
                        priceCurrency = "VND",
                        availability = prod.StockQuantity > 0 ? "https://schema.org/InStock" : "https://schema.org/OutOfStock"
                    }
                }, JsonOpts).Replace("at_", "@");

            case "category":
                var cat = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == entityId, ct);
                if (cat is null) return null;
                return JsonSerializer.Serialize(new
                {
                    at_context = "https://schema.org",
                    at_type = "CollectionPage",
                    name = meta?.MetaTitle ?? cat.Name,
                    description = meta?.MetaDescription ?? cat.Description
                }, JsonOpts).Replace("at_", "@");

            default:
                return null;
        }
    }

    private static SeoMetaDto Map(SeoMeta m) => new(
        m.Id, m.EntityType, m.EntityId, m.Culture,
        m.MetaTitle, m.MetaDescription, m.OgImage, m.Canonical, m.Robots,
        m.SchemaJsonLd, m.OgType, m.TwitterCard, m.Priority, m.ChangeFreq);
}
