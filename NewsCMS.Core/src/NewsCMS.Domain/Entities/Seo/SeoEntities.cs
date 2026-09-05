using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Seo;

public class SeoMeta : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string EntityType { get; set; } = default!;  // Post | Category | Page ...
    public Guid EntityId { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? OgImage { get; set; }
    public string? Canonical { get; set; }
    public string? Robots { get; set; }   // index, follow / noindex, nofollow ...

    // ── Mở rộng cho Site Builder + đa ngôn ngữ ─────────────────────────────────
    /// <summary>Ngôn ngữ của bản SEO này (khớp Culture của entity/route).</summary>
    public string Culture { get; set; } = "vi";
    /// <summary>JSON-LD structured data (Article, Product, FAQPage...).</summary>
    public string? SchemaJsonLd { get; set; }
    public string? OgType { get; set; }          // website | article | product ...
    public string? TwitterCard { get; set; }      // summary | summary_large_image ...
    /// <summary>Ưu tiên trong sitemap (0.0 - 1.0).</summary>
    public double? Priority { get; set; }
    /// <summary>Tần suất thay đổi trong sitemap (daily, weekly...).</summary>
    public string? ChangeFreq { get; set; }
}

public class Redirect : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string FromPath { get; set; } = default!;
    public string ToPath { get; set; } = default!;
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
}
