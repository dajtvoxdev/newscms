using AdVideo.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AdVideo.Tests.Persistence;

/// <summary>
/// L1 — schema phải dựng được từ migration, không phải từ <c>EnsureCreated</c>.
/// </summary>
/// <remarks>
/// Test integration dùng SQLite + <c>EnsureCreatedAsync</c>, tức là vòng qua migration hoàn toàn.
/// Không có test này thì đổi một entity mà quên <c>migrations add</c> vẫn xanh hết, và lỗi chỉ lộ
/// ra lúc <c>migrate</c> trên SQL Server thật. Dùng model của provider SQL Server; không cần kết nối.
/// </remarks>
public class MigrationTests
{
    [Fact]
    public void Model_hien_tai_khop_snapshot_migration()
    {
        using AdVideoDbContext db = new AdVideoDbContextFactory().CreateDbContext([]);

        IMigrationsAssembly migrations = db.GetService<IMigrationsAssembly>();
        IModel? snapshot = migrations.ModelSnapshot?.Model;

        snapshot.Should().NotBeNull("phải có ít nhất một migration");

        if (snapshot is IMutableModel mutable)
        {
            snapshot = mutable.FinalizeModel();
        }

        snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot!, designTime: true, validationLogger: null);

        IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> differences = db
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshot.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        differences.Should().BeEmpty(
            "model đã đổi mà chưa có migration — chạy: dotnet ef migrations add <Tên> " +
            "--project src/AdVideo.Infrastructure --startup-project src/AdVideo.Api --output-dir Persistence/Migrations");
    }

    [Fact]
    public void InitialCreate_co_bang_ProviderDescriptors()
    {
        using AdVideoDbContext db = new AdVideoDbContextFactory().CreateDbContext([]);

        IMigrationsAssembly migrations = db.GetService<IMigrationsAssembly>();

        migrations.Migrations.Keys.Should().ContainSingle(k => k.EndsWith("_InitialCreate", StringComparison.Ordinal));
        migrations.ModelSnapshot!.Model.FindEntityType(typeof(AdVideo.Core.Entities.ProviderDescriptorRow))!
            .GetTableName().Should().Be("ProviderDescriptors");
    }
}
