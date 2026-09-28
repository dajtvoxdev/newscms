using AdVideo.Core.Entities;
using AdVideo.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class OperatorKeyConfiguration : IEntityTypeConfiguration<OperatorKey>
{
    public void Configure(EntityTypeBuilder<OperatorKey> builder)
    {
        builder.ToTable("OperatorKeys");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

        builder.Property(x => x.ApiKeyPrefix)
            .IsRequired()
            .HasMaxLength(ApiKeyHasher.LookupPrefixLength);

        builder.Property(x => x.ApiKeyHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsUnicode(false);

        builder.Property(x => x.Note).HasMaxLength(1000);

        // Unique không lọc IsActive: key đã thu hồi vẫn giữ prefix của nó, để một key mới không
        // bao giờ trùng prefix với một key cũ còn nằm trong log sự cố.
        builder.HasIndex(x => x.ApiKeyPrefix)
            .IsUnique()
            .HasDatabaseName("UX_OperatorKeys_ApiKeyPrefix");
    }
}
