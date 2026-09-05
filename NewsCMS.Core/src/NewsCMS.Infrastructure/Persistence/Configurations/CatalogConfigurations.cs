using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Catalog;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> b)
    {
        b.ToTable("ProductCategories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(220);
        b.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
        b.Property(x => x.Description).HasMaxLength(2000);

        // Đối xứng với Content.Category: PathSlug là prefix URL suy từ cây chuyên mục,
        // TemplatePageId trỏ trang builder làm listing. Không khai FK sang Pages vì Page
        // soft-delete (cùng lý do với Page.ParentPageId).
        b.Property(x => x.PathSlug).HasMaxLength(500);

        b.HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

        // Site + soft-delete filter áp tập trung trong AppDbContext.OnModelCreating.
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).IsRequired().HasMaxLength(300);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(320);
        b.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
        b.Property(x => x.Sku).IsRequired().HasMaxLength(80);
        b.HasIndex(x => new { x.SiteId, x.Sku }).IsUnique();
        b.Property(x => x.ShortDescription).HasMaxLength(1000);
        b.Property(x => x.Description).HasColumnType("nvarchar(max)").IsRequired();

        b.Property(x => x.Price).HasColumnType("decimal(18,2)");
        b.Property(x => x.SalePrice).HasColumnType("decimal(18,2)");

        b.HasOne(x => x.ProductCategory).WithMany(x => x.Products)
            .HasForeignKey(x => x.ProductCategoryId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Thumbnail).WithMany()
            .HasForeignKey(x => x.ThumbnailMediaId).OnDelete(DeleteBehavior.SetNull);

        b.HasMany(x => x.Images).WithOne(x => x.Product)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Variants).WithOne(x => x.Product)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);

        // Site + soft-delete filter áp tập trung trong AppDbContext.OnModelCreating.
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.ToTable("ProductImages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Url).IsRequired().HasMaxLength(500);
        b.Property(x => x.AltText).HasMaxLength(300);
    }
}

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> b)
    {
        b.ToTable("ProductVariants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).IsRequired().HasMaxLength(80);
        b.HasIndex(x => x.Sku);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);

        b.Property(x => x.Price).HasColumnType("decimal(18,2)");
        b.Property(x => x.SalePrice).HasColumnType("decimal(18,2)");
    }
}
