using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Builder;
using NewsCMS.Domain.Entities.I18n;
using NewsCMS.Domain.Entities.Seo;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

// =============================================================================
// Cấu hình EF cho các entity của Site Builder (theme Universal) + routing/SEO + i18n.
// Query filter theo SiteId (+ soft-delete) được áp tập trung trong AppDbContext
// cho mọi entity ISiteScoped; ở đây chỉ khai báo bảng, khoá, index, độ dài cột.
// =============================================================================

public class SiteLayoutConfiguration : IEntityTypeConfiguration<SiteLayout>
{
    public void Configure(EntityTypeBuilder<SiteLayout> b)
    {
        b.ToTable("SiteLayouts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(100);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.BuilderJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.CompiledHtml).HasColumnType("nvarchar(max)");
        b.Property(x => x.CompiledCss).HasColumnType("nvarchar(max)");
        b.Property(x => x.CustomCss).HasColumnType("nvarchar(max)");
        b.Property(x => x.CustomJs).HasColumnType("nvarchar(max)");
        b.HasIndex(x => new { x.SiteId, x.Key }).IsUnique();
    }
}

public class PageRevisionConfiguration : IEntityTypeConfiguration<PageRevision>
{
    public void Configure(EntityTypeBuilder<PageRevision> b)
    {
        b.ToTable("PageRevisions");
        b.HasKey(x => x.Id);
        b.Property(x => x.BuilderJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.CompiledHtml).HasColumnType("nvarchar(max)");
        b.Property(x => x.CompiledCss).HasColumnType("nvarchar(max)");
        b.Property(x => x.CustomCss).HasColumnType("nvarchar(max)");
        b.Property(x => x.CustomJs).HasColumnType("nvarchar(max)");
        b.Property(x => x.Note).HasMaxLength(500);
        // Một page chỉ có một revision cho mỗi version.
        b.HasIndex(x => new { x.PageId, x.Version }).IsUnique();
    }
}

public class BlockDefinitionConfiguration : IEntityTypeConfiguration<BlockDefinition>
{
    public void Configure(EntityTypeBuilder<BlockDefinition> b)
    {
        b.ToTable("BlockDefinitions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(100);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Category).HasMaxLength(80);
        b.Property(x => x.Icon).HasMaxLength(80);
        b.Property(x => x.BuilderJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.DynamicHandler).HasMaxLength(100);
        b.Property(x => x.PropsSchemaJson).HasColumnType("nvarchar(max)");
        // BlockDefinition KHÔNG ISiteScoped (SiteId nullable: null = khối toàn cục) nên
        // filter soft-delete phải khai báo thủ công ở đây.
        b.HasQueryFilter(x => !x.IsDeleted);
        b.HasIndex(x => new { x.SiteId, x.Key });
    }
}

public class SiteDesignTokenConfiguration : IEntityTypeConfiguration<SiteDesignToken>
{
    public void Configure(EntityTypeBuilder<SiteDesignToken> b)
    {
        b.ToTable("SiteDesignTokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Group).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Key).IsRequired().HasMaxLength(100);
        b.Property(x => x.Value).IsRequired().HasMaxLength(600);
        // Cùng một site: key chỉ xuất hiện một lần trong mỗi nhóm (color/font/space...).
        b.HasIndex(x => new { x.SiteId, x.Group, x.Key }).IsUnique();
    }
}

public class SiteTemplateConfiguration : IEntityTypeConfiguration<SiteTemplate>
{
    public void Configure(EntityTypeBuilder<SiteTemplate> b)
    {
        b.ToTable("SiteTemplates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(100);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.SpecJson).HasColumnType("nvarchar(max)").IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
    }
}

public class SiteApiKeyConfiguration : IEntityTypeConfiguration<SiteApiKey>
{
    public void Configure(EntityTypeBuilder<SiteApiKey> b)
    {
        b.ToTable("SiteApiKeys");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.KeyHash).IsRequired().HasMaxLength(128);
        // Key gốc mã hoá DataProtection — dài hơn key thô (base64url 43 ký tự) nhiều, chừa rộng.
        b.Property(x => x.KeyCipher).HasMaxLength(1000);
        b.Property(x => x.Scopes).HasMaxLength(500);
        b.HasIndex(x => x.KeyHash).IsUnique();
        b.HasIndex(x => x.SiteId);
    }
}

public class SiteRouteConfiguration : IEntityTypeConfiguration<SiteRoute>
{
    public void Configure(EntityTypeBuilder<SiteRoute> b)
    {
        b.ToTable("SiteRoutes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Path).IsRequired().HasMaxLength(800);
        b.Property(x => x.Culture).IsRequired().HasMaxLength(10);
        b.Property(x => x.RouteType).HasConversion<string>().HasMaxLength(20);
        // Một site: mỗi path chỉ trỏ một entity trong một ngôn ngữ. Index này là sống lưng
        // của IRouteRegistry.ResolveAsync.
        b.HasIndex(x => new { x.SiteId, x.Culture, x.Path }).IsUnique();
        // Tra theo entity để sinh hreflang / đổi slug.
        b.HasIndex(x => new { x.RouteType, x.TargetId });
    }
}

public class PageTranslationConfiguration : IEntityTypeConfiguration<PageTranslation>
{
    public void Configure(EntityTypeBuilder<PageTranslation> b)
    {
        b.ToTable("PageTranslations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Culture).IsRequired().HasMaxLength(10);
        b.Property(x => x.Title).IsRequired().HasMaxLength(300);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(320);
        b.Property(x => x.BuilderJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.CompiledHtml).HasColumnType("nvarchar(max)");
        b.Property(x => x.CompiledCss).HasColumnType("nvarchar(max)");
        b.Property(x => x.CustomJs).HasColumnType("nvarchar(max)");
        b.HasIndex(x => new { x.PageId, x.Culture }).IsUnique();
    }
}

public class CategoryTranslationConfiguration : IEntityTypeConfiguration<CategoryTranslation>
{
    public void Configure(EntityTypeBuilder<CategoryTranslation> b)
    {
        b.ToTable("CategoryTranslations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Culture).IsRequired().HasMaxLength(10);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(220);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.HasIndex(x => new { x.CategoryId, x.Culture }).IsUnique();
    }
}

public class PostTranslationConfiguration : IEntityTypeConfiguration<PostTranslation>
{
    public void Configure(EntityTypeBuilder<PostTranslation> b)
    {
        b.ToTable("PostTranslations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Culture).IsRequired().HasMaxLength(10);
        b.Property(x => x.Title).IsRequired().HasMaxLength(300);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(320);
        b.Property(x => x.Excerpt).HasMaxLength(1000);
        b.Property(x => x.Content).HasColumnType("nvarchar(max)");
        b.HasIndex(x => new { x.PostId, x.Culture }).IsUnique();
    }
}

public class MenuItemTranslationConfiguration : IEntityTypeConfiguration<MenuItemTranslation>
{
    public void Configure(EntityTypeBuilder<MenuItemTranslation> b)
    {
        b.ToTable("MenuItemTranslations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Culture).IsRequired().HasMaxLength(10);
        b.Property(x => x.Title).IsRequired().HasMaxLength(300);
        b.HasIndex(x => new { x.MenuItemId, x.Culture }).IsUnique();
    }
}

/// <summary>Cấu hình SeoMeta (entity sẵn có được mở rộng cột ở Phase 1).</summary>
public class SeoMetaConfiguration : IEntityTypeConfiguration<SeoMeta>
{
    public void Configure(EntityTypeBuilder<SeoMeta> b)
    {
        b.ToTable("SeoMetas");
        b.HasKey(x => x.Id);
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(50);
        b.Property(x => x.MetaTitle).HasMaxLength(300);
        b.Property(x => x.MetaDescription).HasMaxLength(600);
        b.Property(x => x.MetaKeywords).HasMaxLength(600);
        b.Property(x => x.OgImage).HasMaxLength(600);
        b.Property(x => x.Canonical).HasMaxLength(600);
        b.Property(x => x.Robots).HasMaxLength(100);
        b.Property(x => x.Culture).HasMaxLength(10).IsRequired();
        b.Property(x => x.SchemaJsonLd).HasColumnType("nvarchar(max)");
        b.Property(x => x.OgType).HasMaxLength(40);
        b.Property(x => x.TwitterCard).HasMaxLength(40);
        b.Property(x => x.ChangeFreq).HasMaxLength(20);
        // Một entity chỉ có một bản SEO cho mỗi ngôn ngữ.
        b.HasIndex(x => new { x.SiteId, x.EntityType, x.EntityId, x.Culture }).IsUnique();
    }
}
