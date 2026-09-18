using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder.ContentTypes;

/// <summary>
/// Triển khai IContentType cho loại nội dung "Sản phẩm" (Product).
/// Đọc dữ liệu từ bảng Products, nạp Thumbnail, danh sách ảnh bổ sung, giá và tồn kho.
/// </summary>
public sealed class ProductContentType : IContentType
{
    public string Key => "product";
    public string DisplayName => "Sản phẩm";
    public RouteType RouteType => RouteType.Product;
    public PageKind TemplateKind => PageKind.ProductTemplate;

    private readonly AppDbContext _db;

    public ProductContentType(AppDbContext db) => _db = db;

    public async Task<ContentDetail?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == id && !p.IsDeleted && p.Status == ProductStatus.Published)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Name,
                p.Description,
                p.ShortDescription,
                p.Price,
                p.SalePrice,
                p.StockQuantity,
                p.IsTrackingStock,
                p.Sku,
                PublishedAt = p.PublishedAt ?? p.CreatedAt,
                CategoryId = p.ProductCategoryId,
                CategorySlug = p.ProductCategory != null ? p.ProductCategory.Slug : null,
                CategoryName = p.ProductCategory != null ? p.ProductCategory.Name : null,
                ThumbnailUrl = p.Thumbnail != null ? p.Thumbnail.FilePath : null,
                ThumbnailAlt = p.Thumbnail != null ? p.Thumbnail.AltText : null,
                Images = p.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Url, i.AltText }).ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (product is null) return null;

        var imageUrl = product.ThumbnailUrl ?? product.Images.FirstOrDefault()?.Url;
        var imageAlt = product.ThumbnailAlt ?? product.Images.FirstOrDefault()?.AltText;

        var extra = new Dictionary<string, object?>
        {
            ["Price"] = product.Price,
            ["SalePrice"] = product.SalePrice,
            ["StockQuantity"] = product.StockQuantity,
            ["IsTrackingStock"] = product.IsTrackingStock,
            ["Sku"] = product.Sku,
            ["Images"] = product.Images
        };

        return new ContentDetail(
            Id: product.Id,
            Slug: product.Slug,
            Title: product.Name,
            Body: product.Description,
            Excerpt: product.ShortDescription,
            ImageUrl: imageUrl,
            ImageWidth: null,
            ImageHeight: null,
            ImageAlt: imageAlt,
            PublishedAt: product.PublishedAt,
            CategoryId: product.CategoryId,
            CategorySlug: product.CategorySlug,
            CategoryName: product.CategoryName,
            Extra: extra);
    }

    public async Task<string> BuildPathAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Slug,
                CategoryPathSlug = p.ProductCategory != null ? p.ProductCategory.PathSlug : null
            })
            .FirstOrDefaultAsync(ct);

        if (product is null) return "/san-pham";

        string prefix;
        if (!string.IsNullOrWhiteSpace(product.CategoryPathSlug))
        {
            prefix = product.CategoryPathSlug.Trim().Trim('/');
        }
        else
        {
            var customPrefix = await _db.SiteSettings.AsNoTracking()
                .Where(s => s.Key == "catalog:detailPrefix")
                .Select(s => s.Value)
                .FirstOrDefaultAsync(ct);

            prefix = !string.IsNullOrWhiteSpace(customPrefix) ? customPrefix.Trim().Trim('/') : "san-pham";
        }

        var slug = product.Slug ?? id.ToString();
        return IRouteRegistry.NormalizePath($"/{prefix}/{slug}");
    }

    /// <summary>
    /// Fallback legacy (trước đây là BuildProductHtml trong PageRenderer): sinh HTML mặc định cho sản phẩm
    /// khi site chưa thiết lập trang Builder Template tương ứng.
    /// Giữ nguyên mã nguồn để tương thích ngược 100% với các site legacy hoặc môi trường test tối giản.
    /// </summary>
    public Task<string?> RenderFallbackHtmlAsync(ContentDetail detail, CancellationToken ct = default)
    {
        // Fallback legacy C# StringBuilder nối chuỗi HTML
        static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        static string Price(decimal v) => v.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";

        var sb = new StringBuilder();
        sb.Append("<article style=\"padding:var(--space-section,72px) 24px;max-width:1100px;margin:0 auto\">");
        sb.Append("<div style=\"display:grid;grid-template-columns:1fr;gap:40px\">");

        if (!string.IsNullOrWhiteSpace(detail.ImageUrl))
        {
            var alt = Enc(string.IsNullOrWhiteSpace(detail.ImageAlt) ? detail.Title : detail.ImageAlt);
            sb.Append("<div style=\"border-radius:var(--radius-card,18px);overflow:hidden;background:rgba(228,226,221,1)\">");
            sb.Append($"<img src=\"{Enc(detail.ImageUrl)}\" alt=\"{alt}\" style=\"width:100%;height:auto;display:block\">");
            sb.Append("</div>");
        }

        sb.Append("<div>");
        if (!string.IsNullOrWhiteSpace(detail.CategorySlug))
        {
            sb.Append($"<a href=\"/{Enc(detail.CategorySlug)}\" style=\"font-size:0.72rem;font-weight:600;letter-spacing:0.08em;text-transform:uppercase;color:var(--color-accent-500);text-decoration:none\">");
            sb.Append(Enc(detail.CategoryName));
            sb.Append("</a>");
        }

        sb.Append("<h1 style=\"margin:12px 0 10px;font-family:var(--font-display);font-weight:400;font-size:clamp(1.8rem,4vw,2.4rem);line-height:1.25;color:var(--color-brand-500)\">");
        sb.Append(Enc(detail.Title));
        sb.Append("</h1>");

        decimal price = 0;
        decimal? salePrice = null;
        if (detail.Extra.TryGetValue("Price", out var pObj) && pObj is decimal pVal) price = pVal;
        if (detail.Extra.TryGetValue("SalePrice", out var spObj) && spObj is decimal spVal) salePrice = spVal;

        sb.Append("<div style=\"margin:16px 0 24px;font-size:1.4rem;font-weight:600;color:var(--color-brand-500)\">");
        if (salePrice.HasValue && salePrice.Value < price)
        {
            sb.Append($"<span>{Price(salePrice.Value)}</span>");
            sb.Append($"<span style=\"margin-left:12px;font-size:1rem;color:var(--color-muted);text-decoration:line-through;font-weight:400\">{Price(price)}</span>");
        }
        else
        {
            sb.Append($"<span>{Price(price)}</span>");
        }
        sb.Append("</div>");

        if (!string.IsNullOrWhiteSpace(detail.Excerpt))
        {
            sb.Append("<p style=\"font-size:1.05rem;line-height:1.6;color:var(--color-ink);margin:0 0 24px;opacity:.9\">");
            sb.Append(Enc(detail.Excerpt));
            sb.Append("</p>");
        }

        sb.Append("<div class=\"nc-post-body\" style=\"font-size:1rem;line-height:1.75;color:var(--color-ink)\">");
        sb.Append(detail.Body ?? string.Empty);
        sb.Append("</div>");

        // Fallback mô tả sản phẩm cho site Universal chưa có ProductTemplate — cùng lý do
        // như PostContentType: dùng chung PlyrAssets.ForBody để video/audio luôn có skin Plyr
        // bất kể đi đường render nào. Script inline được PageRenderer.StampInlineScriptNonce
        // gắn nonce CSP per-request khi ráp trang.
        sb.Append(PlyrAssets.ForBody(detail.Body));

        sb.Append("</div></div></article>");
        return Task.FromResult<string?>(sb.ToString());
    }
}

