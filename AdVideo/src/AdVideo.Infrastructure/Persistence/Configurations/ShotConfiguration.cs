using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class ShotConfiguration : IEntityTypeConfiguration<Shot>
{
    public void Configure(EntityTypeBuilder<Shot> builder)
    {
        builder.ToTable("Shots");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.VisualPrompt).IsRequired();
        builder.Property(x => x.SpokenText);
        builder.Property(x => x.ProviderModelId).HasMaxLength(100);
        builder.Property(x => x.ProviderRequestId).HasMaxLength(200);
        builder.Property(x => x.TtsRequestId).HasMaxLength(200);
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.Property(x => x.CostUsd).HasPrecision(18, 6);

        // Thứ tự shot là dữ liệu, không phải gợi ý: hai shot cùng Index trong một job làm bước 8
        // (ghép FFmpeg) nối sai thứ tự mà không báo lỗi gì.
        builder.HasIndex(x => new { x.JobId, x.Index })
            .IsUnique()
            .HasDatabaseName("UX_Shots_Job_Index");

        // Dò shot đang render quá lâu trên toàn hệ thống.
        builder.HasIndex(x => new { x.Status, x.RenderStartedAt })
            .HasDatabaseName("IX_Shots_Status_RenderStartedAt");

        // ClipAsset và NativeAudioAsset là navigation tuỳ chọn tới cùng bảng MediaAsset. Khai báo
        // tường minh vì EF không tự đoán được FK nào thuộc navigation nào khi có hai đường tới
        // cùng một kiểu — để mặc định thì nó dựng thêm cột shadow.
        builder.HasOne(x => x.ClipAsset)
            .WithMany()
            .HasForeignKey(x => x.ClipAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.NativeAudioAsset)
            .WithMany()
            .HasForeignKey(x => x.NativeAudioAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
