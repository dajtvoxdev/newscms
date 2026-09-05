using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.I18n;

/// <summary>
/// Bản dịch theo ngôn ngữ (Culture) của các entity nội dung. Entity gốc giữ ngôn ngữ mặc định
/// của site; mỗi Culture khác có một bản dịch. Renderer chọn bản dịch khớp Culture của request,
/// fallback về entity gốc nếu chưa có. hreflang sinh từ tập SiteRoute cùng TargetId khác Culture.
/// </summary>
public class PageTranslation : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid PageId { get; set; }
    public string Culture { get; set; } = default!;

    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? BuilderJson { get; set; }
    public string? CompiledHtml { get; set; }
    public string? CompiledCss { get; set; }
    public string? CustomJs { get; set; }
}

public class CategoryTranslation : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid CategoryId { get; set; }
    public string Culture { get; set; } = default!;

    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Description { get; set; }
}

public class PostTranslation : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid PostId { get; set; }
    public string Culture { get; set; } = default!;

    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Excerpt { get; set; }
    public string? Content { get; set; }
}

public class MenuItemTranslation : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid MenuItemId { get; set; }
    public string Culture { get; set; } = default!;

    public string Title { get; set; } = default!;
}
