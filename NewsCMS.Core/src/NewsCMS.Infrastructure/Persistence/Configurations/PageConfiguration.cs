using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Site;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Cấu hình các cột builder thêm vào Page (Phase 1). LayoutId/CompiledHtml... là dữ liệu của
/// theme Universal; các cột legacy (Title/Slug/Content/IsPublished) giữ nguyên cho theme RCL cũ.
/// </summary>
public class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> b)
    {
        b.ToTable("Pages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).IsRequired().HasMaxLength(300);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(320);
        b.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();

        // Cột builder (nội dung lớn → nvarchar(max)).
        b.Property(x => x.BuilderJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.CompiledHtml).HasColumnType("nvarchar(max)");
        b.Property(x => x.CompiledCss).HasColumnType("nvarchar(max)");
        b.Property(x => x.CustomCss).HasColumnType("nvarchar(max)");
        b.Property(x => x.CustomJs).HasColumnType("nvarchar(max)");

        // Enum lưu dạng string cho dễ tra cứu DB.
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        b.Property(x => x.Version).HasDefaultValue(0);
        b.Property(x => x.CssDirty).HasDefaultValue(false);
        b.Property(x => x.IsDefaultTemplate).HasDefaultValue(false);

        // ParentPageId tự trỏ vào Pages nhưng KHÔNG khai FK (cùng lý do LayoutId bên dưới): Page
        // có soft-delete, FK cứng sẽ chặn xoá trang cha dù bản ghi chỉ bị đánh dấu IsDeleted.
        // TemplateResolver luôn kiểm tra trang template còn sống trước khi dùng.
        // Index này là đường tra chính: "template nào gắn dưới trang cha X".
        b.HasIndex(x => new { x.ParentPageId, x.Kind });

        // LayoutId trỏ SiteLayout nhưng KHÔNG tạo FK constraint: SiteLayout soft-delete,
        // ràng buộc cứng sẽ cản trở; renderer tự fallback layout mặc định khi LayoutId rỗng.
        b.HasIndex(x => x.LayoutId);
    }
}
