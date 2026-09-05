using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Site;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class SiteConfiguration : IEntityTypeConfiguration<NewsCMS.Domain.Entities.Site.Site>
{
    public void Configure(EntityTypeBuilder<NewsCMS.Domain.Entities.Site.Site> builder)
    {
        builder.ToTable("Sites");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Slug)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.PrimaryDomain)
            .HasMaxLength(255);

        builder.Property(x => x.DefaultTheme)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.DefaultCulture)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.SupportedCultures)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(x => x.Slug)
            .IsUnique();

        builder.HasIndex(x => x.PrimaryDomain)
            .IsUnique()
            .HasFilter("[PrimaryDomain] IS NOT NULL");

        builder.HasMany(x => x.Users)
            .WithOne(x => x.Site)
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
