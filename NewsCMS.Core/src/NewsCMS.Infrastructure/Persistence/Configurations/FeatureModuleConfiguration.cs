using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Site;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class FeatureModuleConfiguration : IEntityTypeConfiguration<FeatureModule>, IEntityTypeConfiguration<SiteFeatureModule>
{
    public void Configure(EntityTypeBuilder<FeatureModule> builder)
    {
        builder.ToTable("FeatureModules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.HasIndex(x => x.Code)
            .IsUnique();
    }

    public void Configure(EntityTypeBuilder<SiteFeatureModule> builder)
    {
        builder.ToTable("SiteFeatureModules");
        builder.HasKey(x => new { x.SiteId, x.FeatureModuleId });

        builder.HasOne(x => x.Site)
            .WithMany(x => x.FeatureModules)
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FeatureModule)
            .WithMany(x => x.Sites)
            .HasForeignKey(x => x.FeatureModuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
