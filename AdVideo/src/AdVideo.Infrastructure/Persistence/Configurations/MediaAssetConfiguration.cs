using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("MediaAssets");

        builder.HasKey(x => x.Id);

        // 63 là giới hạn tên bucket của S3, không phải con số tròn tự chọn.
        builder.Property(x => x.Bucket).IsRequired().HasMaxLength(63);
        builder.Property(x => x.ObjectKey).IsRequired().HasMaxLength(1024);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(127);

        // SHA-256 hex luôn đúng 64 ký tự nên dùng char cố định: nvarchar biến thiên ở đây chỉ tốn
        // thêm byte độ dài mà không bao giờ dùng tới.
        builder.Property(x => x.ChecksumSha256).HasMaxLength(64).IsFixedLength();

        // ObjectKey đã dài 1024 nên không đưa vào unique index trực tiếp được (vượt giới hạn 1700
        // byte khoá của SQL Server). Dùng index thường trên checksum để phát hiện file trùng, và
        // để tính duy nhất cho tầng lưu trữ tự lo bằng cách sinh key có GUID.
        builder.HasIndex(x => new { x.TenantId, x.Kind })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_MediaAssets_Tenant_Kind");

        builder.HasIndex(x => x.ChecksumSha256)
            .HasDatabaseName("IX_MediaAssets_Checksum");

        // Dọn rác: tìm asset của job đã xong để chuyển sang lưu trữ lạnh hoặc xoá theo retention.
        builder.HasIndex(x => new { x.JobId, x.Kind })
            .HasDatabaseName("IX_MediaAssets_Job_Kind");

        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(x => x.DerivedFromAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
