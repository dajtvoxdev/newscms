using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Content;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> b)
    {
        b.ToTable("Posts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).IsRequired().HasMaxLength(300);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(320);
        b.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
        b.Property(x => x.Excerpt).HasMaxLength(1000);
        b.Property(x => x.Content).HasColumnType("nvarchar(max)").IsRequired();

        b.HasOne(x => x.Category).WithMany(x => x.Posts)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.FeaturedImage).WithMany()
            .HasForeignKey(x => x.FeaturedImageId).OnDelete(DeleteBehavior.SetNull);

        b.HasMany(x => x.PostTags).WithOne(x => x.Post)
            .HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);

        // Site + soft-delete filter áp tập trung trong AppDbContext.OnModelCreating.
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("Categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(220);
        b.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
        b.HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

        // Mở rộng Site Builder
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PathSlug).HasMaxLength(600);
        b.Property(x => x.Color).HasMaxLength(20);
        b.Property(x => x.Icon).HasMaxLength(80);
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("Tags");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(150);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(170);
        b.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
    }
}

public class PostTagConfiguration : IEntityTypeConfiguration<PostTag>
{
    public void Configure(EntityTypeBuilder<PostTag> b)
    {
        b.ToTable("PostTags");
        b.HasKey(x => new { x.PostId, x.TagId });
        b.HasOne(x => x.Tag).WithMany(x => x.PostTags).HasForeignKey(x => x.TagId);
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<NewsCMS.Domain.Entities.Identity.Permission>
{
    public void Configure(EntityTypeBuilder<NewsCMS.Domain.Entities.Identity.Permission> b)
    {
        b.ToTable("Permissions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(100);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(x => x.Module).IsRequired().HasMaxLength(50);
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<NewsCMS.Domain.Entities.Identity.RolePermission>
{
    public void Configure(EntityTypeBuilder<NewsCMS.Domain.Entities.Identity.RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(x => new { x.RoleId, x.PermissionId });
        b.HasOne(x => x.Role).WithMany(x => x.RolePermissions).HasForeignKey(x => x.RoleId);
        b.HasOne(x => x.Permission).WithMany(x => x.RolePermissions).HasForeignKey(x => x.PermissionId);
    }
}

public class SiteSettingConfiguration : IEntityTypeConfiguration<NewsCMS.Domain.Entities.Site.SiteSetting>
{
    public void Configure(EntityTypeBuilder<NewsCMS.Domain.Entities.Site.SiteSetting> b)
    {
        b.ToTable("SiteSettings");
        b.HasKey(x => new { x.SiteId, x.Key });
        b.Property(x => x.Key).HasMaxLength(150);
        b.Property(x => x.Group).IsRequired().HasMaxLength(50);
    }
}
