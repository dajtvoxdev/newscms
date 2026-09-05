using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Builder;

/// <summary>
/// Bản mẫu site toàn cục (không thuộc site nào) — khi root tạo site mới thì clone SpecJson
/// thành layout/page/menu/category/token. SpecJson cùng định dạng với ISiteBuilderApi.ApplyAsync
/// (Phase 7) để round-trip.
/// </summary>
public class SiteTemplate : BaseEntity
{
    /// <summary>Key ổn định, ví dụ "news", "product-landing", "business".</summary>
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public Guid? PreviewImageId { get; set; }

    /// <summary>Spec khai báo toàn bộ site (JSON) — đầu vào của ApplyAsync/clone.</summary>
    public string SpecJson { get; set; } = default!;
}
