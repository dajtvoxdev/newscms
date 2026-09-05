using NewsCMS.Domain.Common;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Domain.Entities.Content;

public class Category : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Description { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();

    public ICollection<Post> Posts { get; set; } = new List<Post>();

    // ── Mở rộng cho Site Builder (theme Universal) ─────────────────────────────
    /// <summary>Chuyên mục chứa loại nội dung nào (bài viết / sản phẩm / trang).</summary>
    public CategoryType Type { get; set; } = CategoryType.Post;
    /// <summary>Path đầy đủ theo cây phân cấp (cha/con/cháu) — dùng cho route + breadcrumb.</summary>
    public string? PathSlug { get; set; }
    /// <summary>Trang builder dùng làm template listing cho chuyên mục này.</summary>
    public Guid? TemplatePageId { get; set; }
    /// <summary>Layout riêng cho chuyên mục (nếu khác layout mặc định).</summary>
    public Guid? LayoutId { get; set; }
    public Guid? CoverImageId { get; set; }
    /// <summary>Màu đại diện (hex) cho UI chuyên mục.</summary>
    public string? Color { get; set; }
    public string? Icon { get; set; }
}
