using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.VideoStudio;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class AdVideoConnectionConfiguration : IEntityTypeConfiguration<AdVideoConnection>
{
    public void Configure(EntityTypeBuilder<AdVideoConnection> builder)
    {
        builder.ToTable("AdVideoConnections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.OperatorKeyEncrypted).HasMaxLength(2000);
        builder.Property(x => x.OperatorKeyPrefix).HasMaxLength(20);
    }
}

public class SiteAdVideoTenantConfiguration : IEntityTypeConfiguration<SiteAdVideoTenant>
{
    public void Configure(EntityTypeBuilder<SiteAdVideoTenant> builder)
    {
        builder.ToTable("SiteAdVideoTenants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ApiKeyEncrypted).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ApiKeyPrefix).HasMaxLength(20).IsRequired();

        // Một site đúng một tenant: hai dòng cho cùng site thì không biết job nào đi bằng key nào.
        builder.HasIndex(x => x.SiteId).IsUnique();
    }
}
