using NewsCMS.Domain.Common;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Domain.Entities.Builder;

/// <summary>
/// Layout dùng chung (shell/header/footer/sidebar) của theme Universal. Nội dung dựng bằng
/// GrapesJS lưu ở BuilderJson; bản render cuối cùng lưu ở CompiledHtml/CompiledCss để renderer
/// không phải chạy lại builder. CustomJs đi đường riêng (không lẫn vào HTML để không lách sanitizer).
/// </summary>
public class SiteLayout : AuditableEntity, ISoftDelete, ISiteScoped
{
    public Guid SiteId { get; set; }

    /// <summary>Key ổn định để tham chiếu (ví dụ "shell-default", "header-main").</summary>
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    public LayoutKind Kind { get; set; } = LayoutKind.Shell;

    /// <summary>Dữ liệu project GrapesJS (JSON) — nguồn chỉnh sửa trong builder.</summary>
    public string? BuilderJson { get; set; }
    /// <summary>HTML đã biên dịch từ BuilderJson — renderer dùng cái này.</summary>
    public string? CompiledHtml { get; set; }
    /// <summary>CSS đã biên dịch (Tailwind + token) của riêng layout.</summary>
    public string? CompiledCss { get; set; }
    /// <summary>
    /// CSS tay của layout viết qua panel Code — đường riêng khỏi CompiledCss để lần compile
    /// Tailwind sau (shell builder) không ghi đè. Renderer nối vào SAU CompiledCss nên luôn thắng.
    /// Đối xứng với <see cref="NewsCMS.Domain.Entities.Site.Page.CustomCss"/>.
    /// </summary>
    public string? CustomCss { get; set; }
    /// <summary>JavaScript tuỳ chỉnh của layout (chỉ người có Builder.Code.Manage mới sửa).</summary>
    public string? CustomJs { get; set; }

    /// <summary>Layout mặc định được chọn khi tạo page mới không chỉ định layout.</summary>
    public bool IsDefault { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
