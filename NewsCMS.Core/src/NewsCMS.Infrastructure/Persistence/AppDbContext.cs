using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Common;
using NewsCMS.Domain.Entities.Analytics;
using NewsCMS.Domain.Entities.Audit;
using NewsCMS.Domain.Entities.Ar;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Engagement;
using NewsCMS.Domain.Entities.Form;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Domain.Entities.KeoBia;
using NewsCMS.Domain.Entities.Seo;
using NewsCMS.Domain.Entities.Site;

namespace NewsCMS.Infrastructure.Persistence;

public class AppDbContext
    : IdentityDbContext<AppUser, AppRole, Guid,
        Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>,
        Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>,
        Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>,
        Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>,
        Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>
{
    private readonly ICurrentSite? _currentSite;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentSite currentSite)
        : base(options)
    {
        _currentSite = currentSite;
    }

    /// <summary>Site hiện tại cho query filter/stamp. Empty = chưa resolve (seeder, design-time).</summary>
    // internal (không private): service trong cùng assembly cần site hiện tại khi phải truy vấn
    // với IgnoreQueryFilters — bỏ filter là bỏ luôn scope site, phải tự chặn lại theo SiteId.
    internal Guid CurrentSiteId => _currentSite?.SiteId ?? Guid.Empty;

    /// <summary>Khi true (seeder/admin cross-site), bỏ qua việc tự stamp SiteId rỗng.</summary>
    public bool BypassSiteScope { get; set; }

    // RBAC
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    // Content
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PostTag> PostTags => Set<PostTag>();
    public DbSet<Media> Medias => Set<Media>();
    public DbSet<MediaFolder> MediaFolders => Set<MediaFolder>();
    public DbSet<MediaUploadSession> MediaUploadSessions => Set<MediaUploadSession>();

    // Site
    public DbSet<Domain.Entities.Site.Site> Sites => Set<Domain.Entities.Site.Site>();
    public DbSet<SiteDomain> SiteDomains => Set<SiteDomain>();
    public DbSet<FeatureModule> FeatureModules => Set<FeatureModule>();
    public DbSet<SiteFeatureModule> SiteFeatureModules => Set<SiteFeatureModule>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();

    // SEO
    public DbSet<SeoMeta> SeoMetas => Set<SeoMeta>();
    public DbSet<Redirect> Redirects => Set<Redirect>();
    public DbSet<SiteRoute> SiteRoutes => Set<SiteRoute>();

    // Builder (theme Universal / Site Builder)
    public DbSet<NewsCMS.Domain.Entities.Builder.SiteLayout> SiteLayouts => Set<NewsCMS.Domain.Entities.Builder.SiteLayout>();
    public DbSet<NewsCMS.Domain.Entities.Builder.PageRevision> PageRevisions => Set<NewsCMS.Domain.Entities.Builder.PageRevision>();
    public DbSet<NewsCMS.Domain.Entities.Builder.BlockDefinition> BlockDefinitions => Set<NewsCMS.Domain.Entities.Builder.BlockDefinition>();
    public DbSet<NewsCMS.Domain.Entities.Builder.SiteDesignToken> SiteDesignTokens => Set<NewsCMS.Domain.Entities.Builder.SiteDesignToken>();
    public DbSet<NewsCMS.Domain.Entities.Builder.SiteTemplate> SiteTemplates => Set<NewsCMS.Domain.Entities.Builder.SiteTemplate>();
    public DbSet<NewsCMS.Domain.Entities.Builder.SiteApiKey> SiteApiKeys => Set<NewsCMS.Domain.Entities.Builder.SiteApiKey>();

    // I18n (bản dịch theo ngôn ngữ)
    public DbSet<NewsCMS.Domain.Entities.I18n.PageTranslation> PageTranslations => Set<NewsCMS.Domain.Entities.I18n.PageTranslation>();
    public DbSet<NewsCMS.Domain.Entities.I18n.CategoryTranslation> CategoryTranslations => Set<NewsCMS.Domain.Entities.I18n.CategoryTranslation>();
    public DbSet<NewsCMS.Domain.Entities.I18n.PostTranslation> PostTranslations => Set<NewsCMS.Domain.Entities.I18n.PostTranslation>();
    public DbSet<NewsCMS.Domain.Entities.I18n.MenuItemTranslation> MenuItemTranslations => Set<NewsCMS.Domain.Entities.I18n.MenuItemTranslation>();

    // Form
    public DbSet<ContactForm> ContactForms => Set<ContactForm>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();

    // Analytics
    public DbSet<VisitorSession> VisitorSessions => Set<VisitorSession>();

    // Engagement
    public DbSet<Commitment> Commitments => Set<Commitment>();

    // AR Experiences
    public DbSet<ArExperience> ArExperiences => Set<ArExperience>();

    // Catalog
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    // KeoBia 2026
    public DbSet<KeoBiaPlayer> KeoBiaPlayers => Set<KeoBiaPlayer>();
    public DbSet<KeoBiaMatch> KeoBiaMatches => Set<KeoBiaMatch>();
    public DbSet<KeoBiaBet> KeoBiaBets => Set<KeoBiaBet>();
    public DbSet<KeoBiaActivity> KeoBiaActivities => Set<KeoBiaActivity>();
    public DbSet<KeoBiaChatMessage> KeoBiaChatMessages => Set<KeoBiaChatMessage>();
    public DbSet<KeoBiaImportJob> KeoBiaImportJobs => Set<KeoBiaImportJob>();
    public DbSet<KeoBiaChangelog> KeoBiaChangelogs => Set<KeoBiaChangelog>();
    public DbSet<KeoBiaCupLog> KeoBiaCupLogs => Set<KeoBiaCupLog>();
    public DbSet<KeoBiaQuestion> KeoBiaQuestions => Set<KeoBiaQuestion>();
    public DbSet<KeoBiaQuestionVote> KeoBiaQuestionVotes => Set<KeoBiaQuestionVote>();
    public DbSet<KeoBiaBeerPayment> KeoBiaBeerPayments => Set<KeoBiaBeerPayment>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // AI
    public DbSet<NewsCMS.Domain.Entities.Ai.AiConnection> AiConnections => Set<NewsCMS.Domain.Entities.Ai.AiConnection>();
    public DbSet<NewsCMS.Domain.Entities.Ai.AiSkill> AiSkills => Set<NewsCMS.Domain.Entities.Ai.AiSkill>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Áp tất cả IEntityTypeConfiguration trong assembly
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Đổi tên prefix Identity tables: AspNet* → User/Role*
        builder.Entity<AppUser>().ToTable("Users");
        builder.Entity<AppRole>().ToTable("Roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        ApplySiteScopedFilters(builder);
    }

    /// <summary>
    /// Áp global query filter cho mọi entity ISiteScoped: chỉ trả về data của site hiện tại
    /// (SiteId == CurrentSiteId), kết hợp soft-delete nếu có. Đọc CurrentSiteId LAZY trong
    /// expression nên resolve được sau khi middleware Set() (DbContext tạo trước middleware).
    /// </summary>
    private void ApplySiteScopedFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (!typeof(ISiteScoped).IsAssignableFrom(clr)) continue;

            var isSoftDelete = typeof(ISoftDelete).IsAssignableFrom(clr);
            var param = System.Linq.Expressions.Expression.Parameter(clr, "e");

            // e.SiteId == this.CurrentSiteId
            var siteEq = System.Linq.Expressions.Expression.Equal(
                System.Linq.Expressions.Expression.Property(param, nameof(ISiteScoped.SiteId)),
                System.Linq.Expressions.Expression.Property(
                    System.Linq.Expressions.Expression.Constant(this), nameof(CurrentSiteId)));

            System.Linq.Expressions.Expression body = siteEq;
            if (isSoftDelete)
            {
                // && !e.IsDeleted
                var notDeleted = System.Linq.Expressions.Expression.Not(
                    System.Linq.Expressions.Expression.Property(param, nameof(ISoftDelete.IsDeleted)));
                body = System.Linq.Expressions.Expression.AndAlso(siteEq, notDeleted);
            }

            var lambda = System.Linq.Expressions.Expression.Lambda(body, param);
            builder.Entity(clr).HasQueryFilter(lambda);
            builder.Entity(clr).HasIndex(nameof(ISiteScoped.SiteId));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampSiteId();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampSiteId();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Tự gán SiteId cho entity ISiteScoped mới thêm (nếu chưa set) theo site hiện tại.</summary>
    private void StampSiteId()
    {
        if (BypassSiteScope) return;
        var siteId = CurrentSiteId;
        if (siteId == Guid.Empty) return;

        foreach (var entry in ChangeTracker.Entries<ISiteScoped>())
        {
            if (entry.State == EntityState.Added && entry.Entity.SiteId == Guid.Empty)
                entry.Entity.SiteId = siteId;
        }
    }
}
