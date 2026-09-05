using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Ai;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class AiConnectionConfiguration : IEntityTypeConfiguration<AiConnection>
{
    public void Configure(EntityTypeBuilder<AiConnection> builder)
    {
        builder.ToTable("AiConnections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ApiKeyEncrypted).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.DefaultModel).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.IsDefault).HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
