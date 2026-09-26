using AdVideo.Core.Common;
using AdVideo.Core.Entities;
using AdVideo.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AdVideo.Infrastructure.Persistence;

/// <summary>
/// DbContext của AdVideo — database riêng <c>AdVideoDb</c>, không dùng chung với NewsCMS.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao DB riêng:</b> vòng đời dữ liệu khác nhau hoàn toàn. <see cref="ProviderCall"/> là
/// bảng append-only tăng rất nhanh và không bao giờ sửa; bảng nội dung của NewsCMS thì ngược lại.
/// Ghép chung thì mọi quyết định về index, retention và backup đều phải thoả hiệp.
/// </para>
/// <para>
/// <b>Hai loại filter toàn cục</b> được áp ở <see cref="OnModelCreating"/> chứ không rải trong
/// từng configuration: để một chỗ thì đọc một lần là biết entity nào được bảo vệ, thay vì phải
/// mở tám file configuration để chắc chắn không sót cái nào.
/// </para>
/// </remarks>
public class AdVideoDbContext : DbContext
{
    private readonly ITenantContext _tenant;
    private readonly IApiKeyProtector _protector;

    public AdVideoDbContext(
        DbContextOptions<AdVideoDbContext> options,
        ITenantContext tenant,
        IApiKeyProtector protector)
        : base(options)
    {
        // Cả hai đều là singleton hoặc scoped-có-chủ-đích; EF tham số hoá được cả hai trong query
        // filter nên mỗi lần query dùng đúng giá trị của scope hiện tại.
        _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AdVideoProject> Projects => Set<AdVideoProject>();
    public DbSet<AdVideoJob> Jobs => Set<AdVideoJob>();
    public DbSet<Shot> Shots => Set<Shot>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<ProviderCall> ProviderCalls => Set<ProviderCall>();
    public DbSet<ProviderCredential> ProviderCredentials => Set<ProviderCredential>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<ProviderDescriptorRow> ProviderDescriptors => Set<ProviderDescriptorRow>();

    /// <summary>
    /// Khoá cache model: model gắn converter mã hoá giữ <see cref="IApiKeyProtector"/>, nên mỗi
    /// protector phải có model riêng. Xem <see cref="ProtectorAwareModelCacheKeyFactory"/>.
    /// </summary>
    internal IApiKeyProtector Protector => _protector;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ProtectorAwareModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdVideoDbContext).Assembly);

        ApplyEncryptedColumns(modelBuilder);
        ApplyGlobalQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Gắn converter mã hoá vào cột secret.
    /// </summary>
    /// <remarks>
    /// Đặt ở đây chứ không trong <c>ProviderCredentialConfiguration</c> vì configuration không có
    /// đường nào lấy được <see cref="IApiKeyProtector"/> — EF dựng configuration bằng
    /// constructor không tham số. Nếu sau này có thêm cột secret thì thêm vào đúng hàm này.
    /// </remarks>
    private void ApplyEncryptedColumns(ModelBuilder modelBuilder)
    {
        var converter = new EncryptedStringConverter(_protector);

        modelBuilder.Entity<ProviderCredential>()
            .Property(x => x.EncryptedApiKey)
            .HasConversion(converter);
    }

    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        // Ba entity vừa tenant-scoped vừa soft-delete. So sánh Guid với Guid? cho kết quả false khi
        // tenant null, nên quên set tenant thì thấy dữ liệu rỗng chứ không phải dữ liệu tenant khác.
        modelBuilder.Entity<AdVideoProject>().HasQueryFilter(
            e => _tenant.BypassFilters || (!e.IsDeleted && e.TenantId == _tenant.TenantId));

        modelBuilder.Entity<AdVideoJob>().HasQueryFilter(
            e => _tenant.BypassFilters || (!e.IsDeleted && e.TenantId == _tenant.TenantId));

        modelBuilder.Entity<MediaAsset>().HasQueryFilter(
            e => _tenant.BypassFilters || (!e.IsDeleted && e.TenantId == _tenant.TenantId));

        // ProviderCredential có TenantId nhưng KHÔNG tenant-scoped theo nghĩa thông thường: credential
        // scope System có TenantId = null và phải dùng được cho mọi tenant. Nên chỉ lọc soft-delete,
        // còn việc chọn đúng scope là trách nhiệm của DbCredentialStore (có test riêng).
        // Tenant KHÔNG tenant-scoped (nó chính là tenant). Chỉ lọc soft-delete — nhưng lọc là
        // bắt buộc: handler xác thực tra bảng này, và một tenant đã xoá mà vẫn đăng nhập được là
        // lỗi bảo mật, không phải lỗi hiển thị.
        modelBuilder.Entity<Tenant>().HasQueryFilter(e => !e.IsDeleted);

        modelBuilder.Entity<ProviderCredential>().HasQueryFilter(e => !e.IsDeleted);

        // SystemSetting có cột IsDeleted nhưng không khai báo ISoftDelete — vẫn lọc, vì một setting
        // đã "xoá" mà còn hiện ra thì người vận hành sẽ sửa một dòng không có tác dụng.
        modelBuilder.Entity<SystemSetting>().HasQueryFilter(e => !e.IsDeleted);

        modelBuilder.Entity<PromptTemplate>().HasQueryFilter(e => !e.IsDeleted);

        modelBuilder.Entity<ProviderDescriptorRow>().HasQueryFilter(e => !e.IsDeleted);

        // Shot: không soft-delete. Shot là bằng chứng của một lần render đã tiêu tiền; xoá nó là
        // làm mất khả năng đối soát chi phí. Muốn "xoá" thì đổi Status.

        // ProviderCall: CỐ Ý KHÔNG có filter nào. Đây là sổ cái chi phí, append-only.
        // Một bảng sổ cái mà có thể lọc bớt dòng là một bảng sổ cái không dùng để đối chiếu được.
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// Đặt <c>CreatedAt</c>/<c>UpdatedAt</c>.
    /// </summary>
    /// <remarks>
    /// <c>CreatedAt</c> chỉ set khi còn default: entity đã có giá trị (ví dụ dựng lại để import dữ
    /// liệu cũ) thì tôn trọng, không ghi đè. <c>UpdatedAt</c> thì LUÔN set khi sửa, kể cả khi đã
    /// có giá trị — đó là ý nghĩa của nó.
    /// </remarks>
    private void ApplyAuditTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity.CreatedAt == default:
                    entry.Entity.CreatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }

        // DeletedAt đặt ở đây chứ không dựa vào SaveChanges của caller: soft-delete qua
        // ChangeTracker (Remove bị chặn bằng interceptor ở sprint sau) vẫn phải có mốc thời gian,
        // vì "xoá lúc nào" là một phần của bằng chứng kiểm toán (R3).
        foreach (var entry in ChangeTracker.Entries<ISoftDelete>())
        {
            if (entry.Entity.IsDeleted && entry.Entity.DeletedAt is null)
            {
                entry.Entity.DeletedAt = now;
            }
        }
    }
}

/// <summary>
/// Cache model EF theo (kiểu context, protector, design-time) thay vì chỉ theo kiểu context.
/// </summary>
/// <remarks>
/// EF cache model TOÀN CỤC theo kiểu DbContext. <see cref="EncryptedStringConverter"/> nằm trong model
/// và giữ <see cref="IApiKeyProtector"/> của context ĐẦU TIÊN dựng model — mọi host sau dùng lại
/// protector đó. Production chỉ có một host nên không thấy; test dựng nhiều host thì host đầu bị
/// dispose là mọi lần mã hoá sau ném <c>ObjectDisposedException</c>, tuỳ thứ tự test.
/// </remarks>
internal sealed class ProtectorAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is AdVideoDbContext advideo
            ? (context.GetType(), advideo.Protector, designTime)
            : (object)(context.GetType(), designTime);
}
