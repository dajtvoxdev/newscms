using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class AdVideoProjectConfiguration : IEntityTypeConfiguration<AdVideoProject>
{
    public void Configure(EntityTypeBuilder<AdVideoProject> builder)
    {
        builder.ToTable("AdVideoProjects");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ProductName).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Industry).HasMaxLength(100);

        // Lọc IsDeleted ngay trong index: global query filter luôn kèm !IsDeleted, nên index
        // không có điều kiện đó sẽ chứa cả dòng đã xoá mà query không bao giờ đọc tới.
        builder.HasIndex(x => new { x.TenantId, x.Name })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_AdVideoProjects_Tenant_Name");

        builder.HasMany(x => x.Jobs)
            .WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
