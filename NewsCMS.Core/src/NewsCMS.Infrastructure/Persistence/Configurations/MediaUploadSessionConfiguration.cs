using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Content;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class MediaUploadSessionConfiguration : IEntityTypeConfiguration<MediaUploadSession>
{
    public void Configure(EntityTypeBuilder<MediaUploadSession> builder)
    {
        builder.ToTable("MediaUploadSessions");

        builder.Property(s => s.TusFileId).HasMaxLength(128).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Error).HasMaxLength(500);
        builder.Property(s => s.FileName).HasMaxLength(260).IsRequired();

        // Tra cứu theo tus id (status polling + idempotency) và quét dọn theo Status.
        builder.HasIndex(s => s.TusFileId).IsUnique();
        builder.HasIndex(s => s.Status);
    }
}
