using NewsCMS.Domain.Common;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Domain.Entities.Site;

public class Menu : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string Name { get; set; } = default!;
    public string Location { get; set; } = default!;  // header | footer | sidebar | ...
    public ICollection<MenuItem> Items { get; set; } = new List<MenuItem>();
}

public class MenuItem : BaseEntity
{
    public Guid MenuId { get; set; }
    public Menu Menu { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public MenuItem? Parent { get; set; }
    public ICollection<MenuItem> Children { get; set; } = new List<MenuItem>();

    public string Title { get; set; } = default!;
    public string Url { get; set; } = default!;
    public string? Target { get; set; }   // _self / _blank
    public int Order { get; set; }
    public string? Icon { get; set; }
}

public class Banner : AuditableEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string Title { get; set; } = default!;
    public Guid ImageId { get; set; }
    public string? Url { get; set; }
    public string Position { get; set; } = "home-hero";
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Page : AuditableEntity, ISoftDelete, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string? Layout { get; set; }    // tên layout/view trong theme (legacy, theme RCL cũ)
    public bool IsPublished { get; set; }

    // ── Mở rộng cho Site Builder (theme Universal) ─────────────────────────────
    /// <summary>Layout builder (SiteLayout) bọc trang này. Null = dùng layout mặc định của site.</summary>
    public Guid? LayoutId { get; set; }
    /// <summary>Dữ liệu project GrapesJS (JSON) — nguồn chỉnh sửa trong builder.</summary>
    public string? BuilderJson { get; set; }
    /// <summary>HTML đã biên dịch từ BuilderJson — UniversalController render cái này.</summary>
    public string? CompiledHtml { get; set; }
    /// <summary>CSS đã biên dịch (Tailwind + token) của riêng trang.</summary>
    public string? CompiledCss { get; set; }
    /// <summary>
    /// CSS tay của trang viết qua panel Code (CodeMirror) — đường riêng khỏi CompiledCss để
    /// lần compile Tailwind sau không ghi đè. Renderer nối vào SAU CompiledCss nên luôn thắng.
    /// </summary>
    public string? CustomCss { get; set; }
    /// <summary>JavaScript tuỳ chỉnh của trang (riêng, không lẫn HTML; cần quyền Builder.Code.Manage).</summary>
    public string? CustomJs { get; set; }
    public PageKind Kind { get; set; } = PageKind.Static;
    /// <summary>
    /// Trang cha trong cây site. Với trang template chi tiết, đây là trang listing mà nó phục vụ:
    /// template có <c>ParentPageId</c> = trang <c>/tin-tuc</c> sẽ render mọi URL <c>/tin-tuc/{slug}</c>.
    /// Nhờ suy prefix từ slug của cha, đổi slug trang listing thì URL chi tiết đi theo — không có
    /// cột prefix thứ hai để lệch nhau.
    /// </summary>
    public Guid? ParentPageId { get; set; }
    /// <summary>
    /// Template dự phòng toàn site cho <see cref="Kind"/> này, dùng khi không tìm được template
    /// theo trang cha. Chỉ có nghĩa với các <c>Kind</c> template (xem <c>PageKindExtensions.IsTemplate</c>).
    /// </summary>
    public bool IsDefaultTemplate { get; set; }
    public BuilderPageStatus Status { get; set; } = BuilderPageStatus.Draft;
    public DateTime? PublishedAt { get; set; }
    /// <summary>Số phiên bản — tăng mỗi lần lưu/publish, khớp PageRevision.Version.</summary>
    public int Version { get; set; }
    /// <summary>True khi CSS cần compile lại (trang tạo qua MCP chưa mở builder lưu lại).</summary>
    public bool CssDirty { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class SiteSetting : ISiteScoped
{
    public Guid SiteId { get; set; }                // PK part 1
    public string Key { get; set; } = default!;   // PK part 2
    public string? Value { get; set; }
    public string Group { get; set; } = "general"; // general | seo | smtp | analytics | theme ...
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
