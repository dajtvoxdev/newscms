using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Site;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class SiteDomainConfiguration : IEntityTypeConfiguration<SiteDomain>
{
    public void Configure(EntityTypeBuilder<SiteDomain> builder)
    {
        builder.ToTable("SiteDomains");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Host)
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(x => x.Host)
            .IsUnique();

        builder.HasIndex(x => x.SiteId);

        builder.HasOne(x => x.Site)
            .WithMany(x => x.Domains)
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
