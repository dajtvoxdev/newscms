using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Builder.Blocks;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Tests;

/// <summary>
/// Khối "Menu điều hướng" — đường nối duy nhất giữa Admin → Menu và nav trong shell. Ba điều bắt
/// buộc: lấy đúng menu theo <c>location</c>, giữ thứ tự <c>Order</c>, và không có menu thì im lặng
/// (comment) thay vì đổ chữ lạ vào header.
/// </summary>
public sealed class SiteMenuBlockTests
{
    /// <summary>
    /// <c>WebUtility.HtmlEncode</c> đổi ký tự Latin-1 (à, ã, ê…) thành entity số nhưng để nguyên
    /// codepoint cao hơn (ủ, ị…). Trang render đúng, nhưng so chuỗi thô trong test sẽ trượt — nên
    /// test so trên bản đã encode giống hệt khối phát ra.
    /// </summary>
    private static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);

    [Fact]
    public async Task RenderAsync_PicksMenuByLocation_AndKeepsOrder()
    {
        await using var db = NewDb();
        SeedMenu(db, "header", ("Trang chủ", "/", 0), ("Liên hệ", "/lien-he", 2), ("Giới thiệu", "/gioi-thieu", 1));
        SeedMenu(db, "footer", ("Chính sách", "/chinh-sach", 0));
        await db.SaveChangesAsync();

        var html = await new SiteMenuBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"location\":\"header\"}"));

        Assert.DoesNotContain(Enc("Chính sách"), html);
        var home = html.IndexOf(Enc("Trang chủ"), StringComparison.Ordinal);
        var about = html.IndexOf(Enc("Giới thiệu"), StringComparison.Ordinal);
        var contact = html.IndexOf(Enc("Liên hệ"), StringComparison.Ordinal);
        Assert.True(home >= 0 && about >= 0 && contact >= 0, "Cả 3 mục menu phải xuất hiện.");
        Assert.True(home < about && about < contact, "Thứ tự phải theo Order, không theo thứ tự insert.");
    }

    [Fact]
    public async Task RenderAsync_NoMenuAtLocation_ReturnsCommentOnly()
    {
        await using var db = NewDb();
        SeedMenu(db, "header", ("Trang chủ", "/", 0));
        await db.SaveChangesAsync();

        var html = await new SiteMenuBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"location\":\"footer\"}"));

        Assert.DoesNotContain("<nav", html);
        Assert.DoesNotContain("<a ", html);
        Assert.Contains("<!--", html);
    }

    [Fact]
    public async Task RenderAsync_Vertical_UsesColumnFlow_AndRespectsGap()
    {
        await using var db = NewDb();
        SeedMenu(db, "footer", ("Chính sách", "/chinh-sach", 0));
        await db.SaveChangesAsync();

        var html = await new SiteMenuBlock(db).RenderAsync(new DynamicBlockContext(
            Guid.Empty, "vi", "{\"location\":\"footer\",\"direction\":\"vertical\",\"gap\":8}"));

        Assert.Contains("flex-direction:column", html);
        Assert.Contains("gap:8px", html);
    }

    /// <summary>
    /// depth=1 phải bỏ mục con; depth=2 mới hiện. Nếu ngược lại, header ngang sẽ trào một rừng link
    /// cấp hai — đúng lỗi khó thấy vì shell nào cũng render được, chỉ là xấu.
    /// </summary>
    [Fact]
    public async Task RenderAsync_DepthControlsChildren()
    {
        await using var db = NewDb();
        var menu = SeedMenu(db, "header", ("Sản phẩm", "/san-pham", 0));
        var parent = menu.Items.First();
        menu.Items.Add(new MenuItem
        {
            MenuId = menu.Id, ParentId = parent.Id, Title = "Cà phê hạt", Url = "/ca-phe-hat", Order = 0
        });
        await db.SaveChangesAsync();

        var flat = await new SiteMenuBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"location\":\"header\",\"depth\":1}"));
        Assert.DoesNotContain(Enc("Cà phê hạt"), flat);

        var nested = await new SiteMenuBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"location\":\"header\",\"depth\":2}"));
        Assert.Contains(Enc("Cà phê hạt"), nested);
        Assert.Contains("nc-menu-sub", nested);
    }

    /// <summary>
    /// Tiêu đề/URL do người dùng nhập ở Admin → Menu, đi thẳng vào HTML shell của mọi trang, nên
    /// phải escape. Bỏ sót chỗ này là một lỗ XSS lưu trữ ở vị trí đắc địa nhất của site.
    /// </summary>
    [Fact]
    public async Task RenderAsync_EncodesTitleAndUrl()
    {
        await using var db = NewDb();
        SeedMenu(db, "header", ("<script>alert(1)</script>", "/x\"><script>", 0));
        await db.SaveChangesAsync();

        var html = await new SiteMenuBlock(db).RenderAsync(
            new DynamicBlockContext(Guid.Empty, "vi", "{\"location\":\"header\"}"));

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    private static Menu SeedMenu(AppDbContext db, string location, params (string Title, string Url, int Order)[] items)
    {
        var menu = new Menu
        {
            SiteId = Guid.Empty,
            Name = "Menu " + location,
            Location = location
        };
        foreach (var (title, url, order) in items)
        {
            menu.Items.Add(new MenuItem { MenuId = menu.Id, Title = title, Url = url, Order = order });
        }
        db.Menus.Add(menu);
        return menu;
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"sitemenu-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }
}
