using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Analytics;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class VisitorSessionConfiguration : IEntityTypeConfiguration<VisitorSession>
{
    public void Configure(EntityTypeBuilder<VisitorSession> b)
    {
        b.ToTable("VisitorSessions");
        b.HasKey(x => x.Id);

        b.Property(x => x.VisitorKey).IsRequired().HasMaxLength(64);
        b.Property(x => x.IpHash).IsRequired().HasMaxLength(64);
        b.Property(x => x.UserAgent).IsRequired().HasMaxLength(256);
        b.Property(x => x.DayBucket).IsRequired();
        b.Property(x => x.FirstSeenUtc).IsRequired();
        b.Property(x => x.LastSeenUtc).IsRequired();

        b.HasIndex(x => new { x.SiteId, x.DayBucket, x.VisitorKey }).IsUnique();
        b.HasIndex(x => x.LastSeenUtc);
    }
}
