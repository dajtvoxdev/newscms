using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Tests;

/// <summary>
/// Hợp đồng lưu shell — nơi hai màn hình cùng ghi một bản ghi: trình thiết kế trực quan
/// (EditShell, gửi HTML + CSS compile) và form code (EditLayout, chỉ gửi CSS tay). Quy ước
/// "null = giữ nguyên" là thứ giữ cho hai bên không xoá thành quả của nhau; marker
/// <c>data-nc-body</c> là thứ giữ cho shell không âm thầm mất vùng nội dung.
/// </summary>
public sealed class SiteLayoutServiceTests
{
    [Fact]
    public async Task CreateAsync_Shell_WithoutHtml_SeedsDefaultWithMarkerAndMenu()
    {
        await using var db = NewDb();

        var result = await NewService(db).CreateAsync(new SiteLayoutSaveRequest(
            Key: "shell-moi", Name: "Shell mới", Kind: "Shell",
            BuilderJson: null, CompiledHtml: null, CompiledCss: null,
            CustomCss: null, CustomJs: null, IsDefault: false));

        Assert.True(result.Succeeded, result.Error);
        var html = result.Value!.CompiledHtml!;
        Assert.Contains("data-nc-body", html);
        Assert.Contains("data-nc-block=\"site-menu\"", html);
        Assert.Contains("<footer", html);
    }

    [Fact]
    public async Task CreateAsync_Shell_WithHtmlMissingMarker_Fails()
    {
        await using var db = NewDb();

        var result = await NewService(db).CreateAsync(new SiteLayoutSaveRequest(
            Key: "shell-loi", Name: "Shell lỗi", Kind: "Shell",
            BuilderJson: null, CompiledHtml: "<header>chỉ có header</header>", CompiledCss: null,
            CustomCss: null, CustomJs: null, IsDefault: false));

        Assert.False(result.Succeeded);
        Assert.Contains("data-nc-body", result.Error!);
    }

    /// <summary>
    /// Kịch bản thật của form code: người dùng chỉ sửa CSS tay và gửi null cho HTML/CSS compile/
    /// BuilderJson/JS. Không có quy ước "null = giữ nguyên" thì một cú lưu ở đây xoá sạch shell
    /// vừa dựng bằng kéo-thả.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_NullFields_KeepExistingHtmlCssJsAndBuilderJson()
    {
        await using var db = NewDb();
        var layout = new SiteLayout
        {
            SiteId = Guid.Empty, Key = "shell-default", Name = "Shell", Kind = LayoutKind.Shell,
            IsDefault = true,
            BuilderJson = "{\"pages\":[1]}",
            CompiledHtml = "<header>h</header><main data-nc-body></main>",
            CompiledCss = ".a{color:red}",
            CustomJs = "console.log(1)"
        };
        db.SiteLayouts.Add(layout);
        await db.SaveChangesAsync();

        var result = await NewService(db).UpdateAsync(layout.Id, new SiteLayoutSaveRequest(
            Key: "shell-default", Name: "Shell", Kind: "Shell",
            BuilderJson: null, CompiledHtml: null, CompiledCss: null,
            CustomCss: ".b{color:green}", CustomJs: null, IsDefault: true));

        Assert.True(result.Succeeded, result.Error);
        var dto = result.Value!;
        Assert.Equal("{\"pages\":[1]}", dto.BuilderJson);
        Assert.Contains("data-nc-body", dto.CompiledHtml!);
        Assert.Equal(".a{color:red}", dto.CompiledCss);
        Assert.Equal("console.log(1)", dto.CustomJs);
        Assert.Equal(".b{color:green}", dto.CustomCss);
    }

    /// <summary>
    /// Sửa HTML bằng tay làm cây component cũ lạc hậu; form gửi chuỗi rỗng để CHỦ ĐỘNG xoá
    /// BuilderJson, để lần mở trình thiết kế sau parse lại từ HTML mới thay vì dựng lại cây cũ.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_EmptyBuilderJson_ClearsIt()
    {
        await using var db = NewDb();
        var layout = new SiteLayout
        {
            SiteId = Guid.Empty, Key = "shell-default", Name = "Shell", Kind = LayoutKind.Shell,
            BuilderJson = "{\"pages\":[1]}",
            CompiledHtml = "<main data-nc-body></main>"
        };
        db.SiteLayouts.Add(layout);
        await db.SaveChangesAsync();

        var result = await NewService(db).UpdateAsync(layout.Id, new SiteLayoutSaveRequest(
            Key: "shell-default", Name: "Shell", Kind: "Shell",
            BuilderJson: "", CompiledHtml: "<header>mới</header><main data-nc-body></main>",
            CompiledCss: null, CustomCss: null, CustomJs: null, IsDefault: false));

        Assert.True(result.Succeeded, result.Error);
        Assert.Null(result.Value!.BuilderJson);
    }

    [Fact]
    public async Task ListAsync_ExcludesSoftDeleted()
    {
        await using var db = NewDb();
        db.SiteLayouts.Add(new SiteLayout
        {
            SiteId = Guid.Empty, Key = "con-dung", Name = "Còn dùng", Kind = LayoutKind.Shell,
            CompiledHtml = "<main data-nc-body></main>"
        });
        db.SiteLayouts.Add(new SiteLayout
        {
            SiteId = Guid.Empty, Key = "da-xoa", Name = "Đã xoá", Kind = LayoutKind.Shell,
            CompiledHtml = "<main data-nc-body></main>", IsDeleted = true, DeletedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var list = await NewService(db).ListAsync();

        Assert.Single(list);
        Assert.Equal("con-dung", list[0].Key);
    }

    private static SiteLayoutService NewService(AppDbContext db) => new(db, new ContentSanitizer());

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"sitelayout-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }
}
