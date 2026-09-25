using AdVideo.Core.Entities;
using AdVideo.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

        builder.Property(x => x.ApiKeyPrefix)
            .IsRequired()
            .HasMaxLength(ApiKeyHasher.LookupPrefixLength);

        // SHA-256 hex là đúng 64 ký tự, và luôn là 64. Khai báo cố định thay vì nvarchar(max) để
        // index dưới đây là index thật sự chứ không phải index trên cột tràn trang.
        builder.Property(x => x.ApiKeyHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsUnicode(false);

        builder.Property(x => x.Note).HasMaxLength(1000);

        // Unique trên prefix: prefix là cái được tra cứu, nên hai tenant trùng prefix sẽ biến một
        // phép tra thành một phép duyệt. Xác suất trùng rất thấp, nhưng "rất thấp" trong một
        // ràng buộc xác thực thì vẫn phải là "không thể".
        builder.HasIndex(x => x.ApiKeyPrefix)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Tenants_ApiKeyPrefix");

        builder.HasMany(x => x.Projects)
            .WithOne()
            .HasForeignKey(x => x.TenantId)

            // Restrict chứ không Cascade: xoá một tenant mà kéo theo toàn bộ project, job và
            // ProviderCall của họ là xoá luôn sổ cái chi phí — thứ phải giữ lại kể cả sau khi
            // khách đi. Xoá tenant là soft-delete; dọn dữ liệu là một việc riêng, có chủ đích.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
