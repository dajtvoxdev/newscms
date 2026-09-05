using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Engagement;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class CommitmentConfiguration : IEntityTypeConfiguration<Commitment>
{
    public void Configure(EntityTypeBuilder<Commitment> b)
    {
        b.ToTable("Commitments");
        b.HasKey(x => x.Id);

        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(80);
        b.Property(x => x.Message).IsRequired().HasMaxLength(280);
        b.Property(x => x.SignatureUrl).IsRequired().HasMaxLength(512);
        b.Property(x => x.SignerKey).IsRequired().HasMaxLength(64);
        b.Property(x => x.IpHash).IsRequired().HasMaxLength(64);
        b.Property(x => x.UserAgent).IsRequired().HasMaxLength(256);
        b.Property(x => x.SignatureKind).HasConversion<int>();

        b.HasIndex(x => new { x.SiteId, x.SignerKey }).IsUnique();
        b.HasIndex(x => x.CreatedAt);
    }
}
