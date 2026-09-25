using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class PromptTemplateConfiguration : IEntityTypeConfiguration<PromptTemplate>
{
    public void Configure(EntityTypeBuilder<PromptTemplate> builder)
    {
        builder.ToTable("PromptTemplates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.FormatCode).HasMaxLength(64);
        builder.Property(x => x.Example);
        builder.Property(x => x.ChangeNote).HasMaxLength(500);

        // Bản cũ được GIỮ chứ không sửa đè: khi một video ra kết quả lạ, câu hỏi đầu tiên là
        // "lúc đó prompt nào đang chạy". Sửa đè là mất câu trả lời đó vĩnh viễn.
        builder.HasIndex(x => new { x.Code, x.Version })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_PromptTemplates_Code_Version");

        // Mỗi code chỉ được một bản đang hoạt động. Ràng buộc ở DB chứ không ở code, vì bật bản
        // mới mà quên tắt bản cũ thì hệ thống vẫn chạy — chỉ là chạy bằng prompt nào thì tuỳ thứ
        // tự dòng trả về, và bug đó không lộ ra ngay.
        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("UX_PromptTemplates_Code_Active");
    }
}
