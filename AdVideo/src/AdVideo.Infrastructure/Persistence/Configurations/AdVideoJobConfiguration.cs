using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class AdVideoJobConfiguration : IEntityTypeConfiguration<AdVideoJob>
{
    public void Configure(EntityTypeBuilder<AdVideoJob> builder)
    {
        builder.ToTable("AdVideoJobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(x => x.RequestHash).HasMaxLength(64);
        builder.Property(x => x.BriefJson).IsRequired();
        builder.Property(x => x.FormatCode).HasMaxLength(64);
        builder.Property(x => x.Provider).HasMaxLength(50);
        builder.Property(x => x.ProviderModelId).HasMaxLength(100);
        builder.Property(x => x.FailureReason).HasMaxLength(1000);

        // RawProviderError để nvarchar(max) có chủ đích: lỗi provider là JSON dài và cắt cụt nó
        // thì mất đúng phần cần để mở ticket với nhà cung cấp.
        builder.Property(x => x.RawProviderError);

        // 45 ký tự = độ dài tối đa của IPv6 dạng ánh xạ IPv4. Đây là bằng chứng pháp lý (R3),
        // lưu thiếu ký tự cuối là bằng chứng hỏng.
        builder.Property(x => x.TermsAcceptedByIp).HasMaxLength(45);

        builder.Property(x => x.EstimatedCostUsd).HasPrecision(18, 6);
        builder.Property(x => x.ActualCostUsd).HasPrecision(18, 6);
        builder.Property(x => x.MaxCostUsd).HasPrecision(18, 6);

        // Chống trùng job khi client gửi lại cùng một request (bước 1 của pipeline). Unique ở DB
        // chứ không chỉ kiểm tra trong code: hai request song song cùng key sẽ cùng đọc thấy
        // "chưa có" rồi cùng ghi, chỉ ràng buộc ở DB mới chặn được.
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_AdVideoJobs_Tenant_IdempotencyKey");

        // Truy vấn danh sách job của một tenant luôn sắp theo thời gian tạo giảm dần.
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_AdVideoJobs_Tenant_CreatedAt");

        // Dò job kẹt: worker và trang vận hành đều lọc theo Status.
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("IX_AdVideoJobs_Status_CreatedAt");

        builder.HasMany(x => x.Shots)
            .WithOne(x => x.Job)
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        // Asset và ProviderCall KHÔNG cascade: xoá job mà kéo theo sổ cái chi phí là mất khả năng
        // đối soát hoá đơn provider, còn xoá MediaAsset thì DB sạch nhưng file trên MinIO vẫn còn
        // và không còn dòng nào trỏ tới để dọn.
        builder.HasMany(x => x.Assets)
            .WithOne(x => x.Job)
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ProviderCalls)
            .WithOne(x => x.Job)
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
