using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class ProviderDescriptorRowConfiguration : IEntityTypeConfiguration<ProviderDescriptorRow>
{
    public void Configure(EntityTypeBuilder<ProviderDescriptorRow> builder)
    {
        builder.ToTable("ProviderDescriptors");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Json).IsRequired();
        builder.Property(x => x.Sha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ChangeNote).HasMaxLength(500);

        // Cùng hai ràng buộc với PromptTemplates, cùng lý do: bản cũ giữ nguyên để tra ngược, và
        // mỗi provider chỉ một bản đang chạy — ép ở DB chứ không tin vào thứ tự dòng trả về.
        builder.HasIndex(x => new { x.Code, x.Version })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_ProviderDescriptors_Code_Version");

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("UX_ProviderDescriptors_Code_Active");

        // Tra ngược từ ProviderCall.DescriptorSha256.
        builder.HasIndex(x => x.Sha256).HasDatabaseName("IX_ProviderDescriptors_Sha256");
    }
}
