using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.VideoStudio;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class AdVideoConnectionConfiguration : IEntityTypeConfiguration<AdVideoConnection>
{
    public void Configure(EntityTypeBuilder<AdVideoConnection> builder)
    {
        builder.ToTable("AdVideoConnections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.OperatorKeyEncrypted).HasMaxLength(2000);
        builder.Property(x => x.OperatorKeyPrefix).HasMaxLength(20);
    }
}

public class SiteAdVideoTenantConfiguration : IEntityTypeConfiguration<SiteAdVideoTenant>
{
    public void Configure(EntityTypeBuilder<SiteAdVideoTenant> builder)
    {
        builder.ToTable("SiteAdVideoTenants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ApiKeyEncrypted).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ApiKeyPrefix).HasMaxLength(20).IsRequired();

        // Một site đúng một tenant: hai dòng cho cùng site thì không biết job nào đi bằng key nào.
        builder.HasIndex(x => x.SiteId).IsUnique();
    }
}

public class VideoPromptTemplateConfiguration : IEntityTypeConfiguration<VideoPromptTemplate>
{
    public void Configure(EntityTypeBuilder<VideoPromptTemplate> builder)
    {
        builder.ToTable("VideoPromptTemplates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ScenePrompt).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ScriptTemplate).HasMaxLength(5000).IsRequired();
        builder.Property(x => x.AspectRatio).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TrendName).HasMaxLength(200);
        builder.Property(x => x.SourceUrls).HasMaxLength(4000);

        // Không site-scoped nên filter chung của AppDbContext không áp — tự lọc xoá mềm ở đây.
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.HasIndex(x => new { x.Status, x.Category });
    }
}

public class VideoPromptTrendRunConfiguration : IEntityTypeConfiguration<VideoPromptTrendRun>
{
    public void Configure(EntityTypeBuilder<VideoPromptTrendRun> builder)
    {
        builder.ToTable("VideoPromptTrendRuns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Trigger).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(8000);
        builder.HasIndex(x => x.StartedAt);
    }
}

public class VideoPromptLibrarySettingsConfiguration : IEntityTypeConfiguration<VideoPromptLibrarySettings>
{
    public void Configure(EntityTypeBuilder<VideoPromptLibrarySettings> builder)
    {
        builder.ToTable("VideoPromptLibrarySettings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Focus).HasMaxLength(1000);
    }
}
