using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using AdVideo.Infrastructure.Persistence.Tenancy;

namespace AdVideo.Infrastructure.Persistence;

/// <summary>
/// Cho phép <c>dotnet ef</c> dựng DbContext mà không cần chạy cả ứng dụng.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao cần:</b> <see cref="AdVideoDbContext"/> có constructor nhận
/// <see cref="ITenantContext"/> và <see cref="IApiKeyProtector"/>. Không có factory này thì
/// <c>dotnet ef migrations add</c> báo "Unable to create an object of type 'AdVideoDbContext'"
/// mà không nói thiếu gì.
/// </para>
/// <para>
/// <b>Dùng <see cref="NullTenantContext"/>:</b> migration phải nhìn thấy toàn bộ model, không
/// thuộc tenant nào. Còn protector ở đây chỉ để dựng được model — scaffold migration không
/// đọc/ghi dữ liệu nên không chạm tới ciphertext thật.
/// </para>
/// </remarks>
public sealed class AdVideoDbContextFactory : IDesignTimeDbContextFactory<AdVideoDbContext>
{
    /// <summary>Tên biến môi trường chứa connection string lúc chạy lệnh ef.</summary>
    public const string ConnectionStringVariable = "ADVIDEO_DB";

    private const string LocalFallback =
        "Server=localhost;Database=AdVideoDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public AdVideoDbContext CreateDbContext(string[] args)
    {
        // Đọc từ biến môi trường chứ không từ appsettings: design-time chạy ở thư mục project
        // Infrastructure, nơi không có file cấu hình nào của Api hay Worker. Fallback localhost
        // để lệnh scaffold chạy được trên máy dev mới mà chưa đặt biến nào.
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = LocalFallback;
        }

        var options = new DbContextOptionsBuilder<AdVideoDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(
                typeof(AdVideoDbContextFactory).Assembly.GetName().Name))
            .Options;

        // Thư mục khoá tạm: chỉ tồn tại trong tiến trình ef, không ghi đè key ring thật.
        IDataProtectionProvider protectionProvider = DataProtectionProvider.Create("AdVideo.DesignTime");

        return new AdVideoDbContext(
            options,
            new NullTenantContext(),
            new DataProtectionApiKeyProtector(protectionProvider));
    }
}
