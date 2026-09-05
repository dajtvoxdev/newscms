using NewsCMS.Domain.Common;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Domain.Entities.Builder;

/// <summary>
/// Định nghĩa một khối kéo-thả. SiteId = null → khối toàn cục (seed sẵn, dùng chung mọi site);
/// SiteId khác null → khối riêng của site đó. Khối động (Kind=Dynamic) được renderer thay nội dung
/// bằng IDynamicBlock tương ứng DynamicHandler ở Phase 4.
/// </summary>
public class BlockDefinition : AuditableEntity, ISoftDelete
{
    /// <summary>null = khối toàn cục; khác null = khối riêng của site.</summary>
    public Guid? SiteId { get; set; }

    /// <summary>Key ổn định (ví dụ "hero-basic", "post-grid").</summary>
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    /// <summary>Nhóm hiển thị trong bảng Blocks (Hero, Feature, Footer...).</summary>
    public string Category { get; set; } = "General";
    public string? Icon { get; set; }
    public Guid? PreviewImageId { get; set; }

    /// <summary>Nội dung GrapesJS của khối (JSON/HTML).</summary>
    public string BuilderJson { get; set; } = default!;
    public BlockKind Kind { get; set; } = BlockKind.Static;

    /// <summary>Với khối động: key của IDynamicBlock (post-list, product-grid...).</summary>
    public string? DynamicHandler { get; set; }
    /// <summary>JSON Schema mô tả props khối động cho builder/AI.</summary>
    public string? PropsSchemaJson { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
