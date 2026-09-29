using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class ImageModelConfiguration : IEntityTypeConfiguration<ImageModel>
{
    public void Configure(EntityTypeBuilder<ImageModel> builder)
    {
        builder.ToTable("ImageModels");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ModelId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SupportedSizes).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Quality).HasMaxLength(20);
        builder.Property(x => x.OutputFormat).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PricePerImageUsd).HasPrecision(18, 6);
        builder.Property(x => x.ExtraParamsJson).HasMaxLength(4000);
        builder.Property(x => x.LastTestError).HasMaxLength(1000);

        // Không site-scoped nên filter chung của AppDbContext không áp — tự lọc xoá mềm ở đây.
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}

public class ImageJobConfiguration : IEntityTypeConfiguration<ImageJob>
{
    public void Configure(EntityTypeBuilder<ImageJob> builder)
    {
        builder.ToTable("ImageJobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModelName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.UserPrompt).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.FinalPrompt).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.AspectRatio).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Size).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Quality).HasMaxLength(20);
        builder.Property(x => x.ReferenceMediaIdsJson).HasMaxLength(2000);
        builder.Property(x => x.RegionsJson).HasMaxLength(16000);
        builder.Property(x => x.MaskStorageKey).HasMaxLength(1024);
        builder.Property(x => x.AnnotatedStorageKey).HasMaxLength(1024);
        builder.Property(x => x.ContextType).HasMaxLength(20);
        builder.Property(x => x.Error).HasMaxLength(1000);
        builder.Property(x => x.EstimatedCostUsd).HasPrecision(18, 6);
        builder.Property(x => x.CostUsd).HasPrecision(18, 6);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();

        // Nhận job = UPDATE ... WHERE Status = Queued. Hai worker (hai instance, hoặc worker và nút
        // "Huỷ") cùng đổi một job thì một bên nhận DbUpdateConcurrencyException thay vì cùng chạy
        // (và cùng tính tiền). Dùng concurrency token chứ không ExecuteUpdate để test InMemory chạy được.
        builder.Property(x => x.Status).IsConcurrencyToken();

        // Bấm "Tạo" hai lần với cùng khoá phải trả lại job cũ, không tạo job (và hoá đơn) thứ hai.
        builder.HasIndex(x => new { x.SiteId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.SiteId, x.Status, x.QueuedAt });
        builder.HasIndex(x => new { x.Status, x.QueuedAt });

        builder.HasMany(x => x.Outputs)
            .WithOne(o => o.Job)
            .HasForeignKey(o => o.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImageJobOutputConfiguration : IEntityTypeConfiguration<ImageJobOutput>
{
    public void Configure(EntityTypeBuilder<ImageJobOutput> builder)
    {
        builder.ToTable("ImageJobOutputs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StorageKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(50).IsRequired();

        // Worker dọn tìm "chưa promote, cũ hơn N ngày".
        builder.HasIndex(x => new { x.PromotedMediaId, x.CreatedAt });
    }
}

public class ImageProviderCallConfiguration : IEntityTypeConfiguration<ImageProviderCall>
{
    public void Configure(EntityTypeBuilder<ImageProviderCall> builder)
    {
        builder.ToTable("ImageProviderCalls");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModelId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Operation).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CostUsd).HasPrecision(18, 6);
        builder.Property(x => x.ErrorCode).HasMaxLength(40);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);

        // Báo cáo chi phí theo site và tháng.
        builder.HasIndex(x => new { x.SiteId, x.CreatedAt });
        builder.HasIndex(x => x.JobId);
    }
}

public class ImagePromptTemplateConfiguration : IEntityTypeConfiguration<ImagePromptTemplate>
{
    public void Configure(EntityTypeBuilder<ImagePromptTemplate> builder)
    {
        builder.ToTable("ImagePromptTemplates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Prompt).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.AspectRatio).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TrendName).HasMaxLength(200);
        builder.Property(x => x.SourceUrls).HasMaxLength(4000);
        builder.Property(x => x.DemoStorageKey).HasMaxLength(1024);
        builder.Property(x => x.DemoImageUrl).HasMaxLength(1024);
        builder.Property(x => x.DemoSource).HasMaxLength(200);

        // Không site-scoped nên filter chung của AppDbContext không áp — tự lọc xoá mềm ở đây.
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.HasIndex(x => new { x.Status, x.Purpose });
    }
}

public class ImagePromptTrendRunConfiguration : IEntityTypeConfiguration<ImagePromptTrendRun>
{
    public void Configure(EntityTypeBuilder<ImagePromptTrendRun> builder)
    {
        builder.ToTable("ImagePromptTrendRuns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Trigger).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(8000);
        builder.HasIndex(x => x.StartedAt);
    }
}

public class ImagePromptLibrarySettingsConfiguration : IEntityTypeConfiguration<ImagePromptLibrarySettings>
{
    public void Configure(EntityTypeBuilder<ImagePromptLibrarySettings> builder)
    {
        builder.ToTable("ImagePromptLibrarySettings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Focus).HasMaxLength(1000);
        builder.Property(x => x.BlockedTerms).HasMaxLength(4000);
    }
}

public class ImageStudioSiteSettingsConfiguration : IEntityTypeConfiguration<ImageStudioSiteSettings>
{
    public void Configure(EntityTypeBuilder<ImageStudioSiteSettings> builder)
    {
        builder.ToTable("ImageStudioSiteSettings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BrandStyle).HasMaxLength(500);
        builder.Property(x => x.CoverAspect).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ProductAspect).HasMaxLength(10).IsRequired();
        builder.Property(x => x.AiCaptionText).HasMaxLength(200).IsRequired();

        // Mỗi site đúng một dòng cấu hình.
        builder.HasIndex(x => x.SiteId).IsUnique();
    }
}
