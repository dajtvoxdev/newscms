using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Ar;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class ArExperienceConfiguration : IEntityTypeConfiguration<ArExperience>
{
    public void Configure(EntityTypeBuilder<ArExperience> b)
    {
        b.ToTable("ArExperiences");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).IsRequired().HasMaxLength(200);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(200);
        b.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.TargetImageUrl).IsRequired().HasMaxLength(512);
        b.Property(x => x.MindFileUrl).IsRequired().HasMaxLength(512);
        b.Property(x => x.VideoUrl).IsRequired().HasMaxLength(512);

        // Site + soft-delete filter áp tập trung trong AppDbContext.OnModelCreating.
    }
}
