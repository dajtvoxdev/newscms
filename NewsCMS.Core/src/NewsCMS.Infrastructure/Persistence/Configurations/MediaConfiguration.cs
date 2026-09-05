using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Content;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class MediaConfiguration : IEntityTypeConfiguration<Media>
{
    public void Configure(EntityTypeBuilder<Media> builder)
    {
        builder.ToTable("Medias");

        builder.Property(m => m.FileName).HasMaxLength(260).IsRequired();
        builder.Property(m => m.StorageKey).HasMaxLength(1024).IsRequired();
        builder.Property(m => m.FilePath).HasMaxLength(1024).IsRequired();
        builder.Property(m => m.MimeType).HasMaxLength(150).IsRequired();
        builder.Property(m => m.Kind).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Title).HasMaxLength(300);
        builder.Property(m => m.AltText).HasMaxLength(500);
        builder.Property(m => m.PosterUrl).HasMaxLength(1024);
        builder.Property(m => m.PosterStorageKey).HasMaxLength(1024);

        builder.HasIndex(m => m.FileName);
        builder.HasIndex(m => m.Kind);
        builder.HasIndex(m => m.FolderId);
        builder.HasIndex(m => m.CreatedAt);
        builder.HasIndex(m => m.IsDeleted);

        builder.HasOne(m => m.Folder)
            .WithMany(f => f.Items)
            .HasForeignKey(m => m.FolderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
