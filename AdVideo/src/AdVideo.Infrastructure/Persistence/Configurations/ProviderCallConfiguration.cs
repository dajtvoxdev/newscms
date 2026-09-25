using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Sổ cái chi phí. Append-only: không soft-delete, không query filter (xem
/// <see cref="AdVideoDbContext"/>), và không có đường nào sửa dòng đã ghi.
/// </summary>
public sealed class ProviderCallConfiguration : IEntityTypeConfiguration<ProviderCall>
{
    public void Configure(EntityTypeBuilder<ProviderCall> builder)
    {
        builder.ToTable("ProviderCalls");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).IsRequired().HasMaxLength(50);
        builder.Property(x => x.ModelId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ProviderRequestId).HasMaxLength(200);
        builder.Property(x => x.RequestJson);
        builder.Property(x => x.RawError);
        builder.Property(x => x.CostUsd).HasPrecision(18, 6);

        // Cột chốt chặn ngân sách ngày (SettingKeys.DailySystemCostLimitUsd): mỗi lần nhận job mới
        // đều phải cộng chi phí trong 24h qua, nên đây là truy vấn nóng nhất của bảng này.
        // Include CostUsd để cộng ngay trên index, không phải mò về bảng chính.
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .IncludeProperties(x => x.CostUsd)
            .HasDatabaseName("IX_ProviderCalls_Tenant_CreatedAt");

        builder.HasIndex(x => new { x.JobId, x.CreatedAt })
            .HasDatabaseName("IX_ProviderCalls_Job_CreatedAt");

        // Đối soát hoá đơn: tổng chi theo provider theo kỳ.
        builder.HasIndex(x => new { x.Provider, x.CreatedAt })
            .IncludeProperties(x => x.CostUsd)
            .HasDatabaseName("IX_ProviderCalls_Provider_CreatedAt");
    }
}
