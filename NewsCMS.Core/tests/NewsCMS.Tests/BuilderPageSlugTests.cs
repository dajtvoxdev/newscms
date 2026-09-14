using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Infrastructure.Content;
using Xunit;

namespace NewsCMS.Tests;

/// <summary>
/// Slug của trang đã xoá mềm không được giữ chỗ trong unique index IX_Pages_SiteId_Slug.
///
/// Bug thật (2026-09-12, site phuphuc): một trang chủ đã xoá mềm vẫn mang slug "", trang chủ đang
/// sống mang slug "home". Mỗi lần lưu, NormalizeSlugForStorage đổi "home" → "" và UPDATE đâm
/// thẳng vào index → SqlException 2601 → builder nhận 500, không lưu/xuất bản được trang nào.
/// Điểm mù: global query filter ẩn hàng IsDeleted nên MỌI kiểm tra trùng slug đều báo "trống",
/// chỉ SaveChanges mới lòi lỗi ra.
///
/// InMemory không ép unique index, nên test khoá hành vi quan sát được: xoá là nhả slug, và lưu
/// đè lên slug đang bị trang đã xoá chiếm thì phải thành công.
/// </summary>
public sealed class BuilderPageSlugTests
{
    private static BuilderPageSaveRequest Req(string title, string slug, string html = "<div>noi dung</div>") =>
        new(title, slug, null, html, null, null, null, "Landing");

    [Fact]
    public async Task DeleteAsync_NhaSlugDeTrangKhacDungLai()
    {
        using var fx = new BuilderRenderFixture("slug-delete");
        var service = new BuilderPageService(fx.Db, new ContentSanitizer(), fx.Routes);

        var created = await service.CreateAsync(Req("Trang chủ cũ", "home"));
        Assert.True(created.Succeeded);
        var id = created.Value!.Id;

        Assert.True((await service.DeleteAsync(id)).Succeeded);

        var row = await fx.Db.Pages.IgnoreQueryFilters().FirstAsync(p => p.Id == id);
        Assert.True(row.IsDeleted);
        Assert.Contains("-deleted-", row.Slug);      // slug "" đã được nhả
        Assert.NotEqual(string.Empty, row.Slug);

        // Trang chủ mới tạo được ngay, không còn vướng hàng đã xoá.
        var again = await service.CreateAsync(Req("Trang chủ mới", "home"));
        Assert.True(again.Succeeded);
    }

    [Fact]
    public async Task UpdateAsync_SlugDangBiTrangDaXoaChiemCho_VanLuuDuoc()
    {
        using var fx = new BuilderRenderFixture("slug-squatter");
        var service = new BuilderPageService(fx.Db, new ContentSanitizer(), fx.Routes);

        // Dữ liệu cũ trong DB: hàng đã xoá giữ slug "", hàng sống mang slug "home".
        var squatter = NewPage("Trang chủ đã xoá", string.Empty, deleted: true);
        var live = NewPage("Trang chủ", "home", deleted: false);
        fx.Db.Pages.AddRange(squatter, live);
        await fx.Db.SaveChangesAsync();

        var result = await service.UpdateAsync(live.Id, Req("Trang chủ", "home", "<div>ban moi</div>"));

        Assert.True(result.Succeeded, result.Error);

        var liveRow = await fx.Db.Pages.IgnoreQueryFilters().FirstAsync(p => p.Id == live.Id);
        var squatterRow = await fx.Db.Pages.IgnoreQueryFilters().FirstAsync(p => p.Id == squatter.Id);
        Assert.Equal(string.Empty, liveRow.Slug);          // trang sống giữ đúng slug trang chủ
        Assert.Contains("-deleted-", squatterRow.Slug);    // hàng đã xoá bị đẩy sang slug khác
    }

    [Fact]
    public async Task UpdateAsync_SlugTrungTrangDangSong_VanBaoLoiRoRang()
    {
        using var fx = new BuilderRenderFixture("slug-live-conflict");
        var service = new BuilderPageService(fx.Db, new ContentSanitizer(), fx.Routes);

        await service.CreateAsync(Req("Giới thiệu", "gioi-thieu"));
        var other = await service.CreateAsync(Req("Liên hệ", "lien-he"));

        var result = await service.UpdateAsync(other.Value!.Id, Req("Liên hệ", "gioi-thieu"));

        Assert.False(result.Succeeded);
        Assert.Contains("gioi-thieu", result.Error);
    }

    private static Page NewPage(string title, string slug, bool deleted) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Slug = slug,
        Content = string.Empty,
        CompiledHtml = "<div>noi dung</div>",
        Kind = PageKind.Landing,
        Status = BuilderPageStatus.Published,
        IsPublished = true,
        IsDeleted = deleted,
        DeletedAt = deleted ? DateTime.UtcNow : null
    };
}
