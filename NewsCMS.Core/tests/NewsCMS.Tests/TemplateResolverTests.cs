using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder;

namespace NewsCMS.Tests;

/// <summary>
/// Chuỗi tra "URL chi tiết → trang builder nào render nó":
/// (1) template gắn dưới trang cha suy từ URL → (2) template mặc định toàn site → (3) không có.
/// Bước (3) là cam kết tương thích ngược — site chưa dựng template phải đi đúng đường cũ.
/// </summary>
public sealed class TemplateResolverTests
{
    [Fact]
    public async Task Khong_co_template_thi_tra_PageId_null()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var (post, _) = SeedTinTuc(fx);

        var result = await fx.Templates.ForPostAsync(post.Id, "/tin-tuc/bai-mot", BuilderRenderFixture.Culture);

        Assert.NotNull(result);
        Assert.Null(result!.PageId);
        // Route vẫn có: shell cần path để render breadcrumb kể cả khi rơi về HTML dựng sẵn.
        Assert.Equal("/tin-tuc/bai-mot", result.Route.Path);
    }

    [Fact]
    public async Task Template_gan_duoi_trang_cha_duoc_chon()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var (post, listing) = SeedTinTuc(fx);
        var template = fx.AddPage("Chi tiết bài viết", "tin-tuc-chi-tiet",
            kind: PageKind.PostTemplate, parentPageId: listing.Id);

        var result = await fx.Templates.ForPostAsync(post.Id, "/tin-tuc/bai-mot", BuilderRenderFixture.Culture);

        Assert.Equal(template.Id, result!.PageId);
    }

    [Fact]
    public async Task Template_cua_trang_cha_KHAC_khong_duoc_dung()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var (post, _) = SeedTinTuc(fx);

        // Template gắn dưới /san-pham, còn bài viết nằm dưới /tin-tuc.
        var other = fx.AddPage("Sản phẩm", "san-pham");
        fx.AddRoute("/san-pham", RouteType.Page, other.Id);
        fx.AddPage("Chi tiết SP", "san-pham-chi-tiet",
            kind: PageKind.PostTemplate, parentPageId: other.Id);

        var result = await fx.Templates.ForPostAsync(post.Id, "/tin-tuc/bai-mot", BuilderRenderFixture.Culture);

        Assert.Null(result!.PageId);
    }

    [Fact]
    public async Task Template_chua_publish_bi_bo_qua()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var (post, listing) = SeedTinTuc(fx);
        fx.AddPage("Chi tiết bài viết", "tin-tuc-chi-tiet",
            kind: PageKind.PostTemplate, parentPageId: listing.Id, published: false);

        var result = await fx.Templates.ForPostAsync(post.Id, "/tin-tuc/bai-mot", BuilderRenderFixture.Culture);

        Assert.Null(result!.PageId);
    }

    /// <summary>
    /// Template rỗng nguy hiểm hơn không có template: nó thắng bước tra rồi render ra trang trắng,
    /// trong khi "không có template" còn rơi về HTML dựng sẵn.
    /// </summary>
    [Fact]
    public async Task Template_rong_bi_bo_qua()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var (post, listing) = SeedTinTuc(fx);
        fx.AddPage("Chi tiết bài viết", "tin-tuc-chi-tiet", compiledHtml: "",
            kind: PageKind.PostTemplate, parentPageId: listing.Id);

        var result = await fx.Templates.ForPostAsync(post.Id, "/tin-tuc/bai-mot", BuilderRenderFixture.Culture);

        Assert.Null(result!.PageId);
    }

    [Fact]
    public async Task Khong_tim_duoc_theo_trang_cha_thi_dung_template_mac_dinh()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var (post, _) = SeedTinTuc(fx);
        var fallback = fx.AddPage("Bài viết (mặc định)", "mau-bai-viet",
            kind: PageKind.PostTemplate, isDefaultTemplate: true);

        var result = await fx.Templates.ForPostAsync(post.Id, "/tin-tuc/bai-mot", BuilderRenderFixture.Culture);

        Assert.Equal(fallback.Id, result!.PageId);
    }

    [Fact]
    public async Task Template_theo_trang_cha_thang_template_mac_dinh()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var (post, listing) = SeedTinTuc(fx);
        fx.AddPage("Bài viết (mặc định)", "mau-bai-viet",
            kind: PageKind.PostTemplate, isDefaultTemplate: true);
        var specific = fx.AddPage("Chi tiết tin tức", "tin-tuc-chi-tiet",
            kind: PageKind.PostTemplate, parentPageId: listing.Id);

        var result = await fx.Templates.ForPostAsync(post.Id, "/tin-tuc/bai-mot", BuilderRenderFixture.Culture);

        Assert.Equal(specific.Id, result!.PageId);
    }

    [Fact]
    public async Task Template_san_pham_khong_bi_nham_voi_template_bai_viet()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var cat = fx.AddProductCategory("Cà phê", "ca-phe");
        var product = fx.AddProduct("Lotus Arabica", "lotus-arabica", cat.Id);

        var listing = fx.AddPage("Sản phẩm", "san-pham");
        fx.AddRoute("/san-pham", RouteType.Page, listing.Id);
        fx.AddRoute("/san-pham/lotus-arabica", RouteType.Product, product.Id);

        // Cùng trang cha nhưng Kind là PostTemplate → không được chọn cho sản phẩm.
        fx.AddPage("Nhầm kind", "nham", kind: PageKind.PostTemplate, parentPageId: listing.Id);
        var right = fx.AddPage("Chi tiết sản phẩm", "san-pham-chi-tiet",
            kind: PageKind.ProductTemplate, parentPageId: listing.Id);

        var result = await fx.Templates.ForProductAsync(
            product.Id, "/san-pham/lotus-arabica", BuilderRenderFixture.Culture);

        Assert.Equal(right.Id, result!.PageId);
        Assert.Equal(RouteType.Product, result.Route.RouteType);
        Assert.Equal(cat.Id, result.Route.CategoryId);
    }

    [Fact]
    public async Task Chuyen_muc_uu_tien_TemplatePageId_cua_chinh_no()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var listingPage = fx.AddPage("Listing chuyên mục", "listing-chuyen-muc",
            kind: PageKind.CategoryTemplate);
        var cat = fx.AddCategory("Tin tức", "tin-tuc", templatePageId: listingPage.Id);
        fx.AddRoute("/tin-tuc", RouteType.Category, cat.Id);

        // Có cả template mặc định, nhưng TemplatePageId của chuyên mục phải thắng.
        fx.AddPage("Chuyên mục (mặc định)", "mau-chuyen-muc",
            kind: PageKind.CategoryTemplate, isDefaultTemplate: true);

        var result = await fx.Templates.ForCategoryAsync(cat.Id, "/tin-tuc", BuilderRenderFixture.Culture);

        Assert.Equal(listingPage.Id, result!.PageId);
        Assert.Equal(cat.Id, result.Route.EntityId);
    }

    /// <summary>
    /// Path null (preview / gọi service trực tiếp) phải tra ngược được từ SiteRoutes, nếu không
    /// thì bước "suy trang cha" mất đầu vào và mọi template gắn theo cây đều trượt.
    /// </summary>
    [Fact]
    public async Task Path_null_thi_tra_nguoc_tu_SiteRoutes()
    {
        using var fx = new BuilderRenderFixture("tplresolve");
        var (post, listing) = SeedTinTuc(fx);
        var template = fx.AddPage("Chi tiết bài viết", "tin-tuc-chi-tiet",
            kind: PageKind.PostTemplate, parentPageId: listing.Id);

        var result = await fx.Templates.ForPostAsync(post.Id, path: null, BuilderRenderFixture.Culture);

        Assert.Equal(template.Id, result!.PageId);
        Assert.Equal("/tin-tuc/bai-mot", result.Route.Path);
    }

    [Fact]
    public async Task Entity_khong_ton_tai_thi_tra_null()
    {
        using var fx = new BuilderRenderFixture("tplresolve");

        Assert.Null(await fx.Templates.ForPostAsync(Guid.NewGuid(), "/tin-tuc/x", BuilderRenderFixture.Culture));
        Assert.Null(await fx.Templates.ForProductAsync(Guid.NewGuid(), "/san-pham/x", BuilderRenderFixture.Culture));
        Assert.Null(await fx.Templates.ForCategoryAsync(Guid.NewGuid(), "/x", BuilderRenderFixture.Culture));
    }

    [Theory]
    [InlineData("/tin-tuc/bai-mot", "/tin-tuc")]
    [InlineData("/tin-tuc", "/")]
    [InlineData("/a/b/c", "/a/b")]
    [InlineData("/", null)]
    [InlineData("", null)]
    public void ParentPath_cat_dung_mot_cap(string path, string? expected)
    {
        Assert.Equal(expected, TemplateResolver.ParentPath(path));
    }

    // ── Route của chính trang template ────────────────────────────────────────────

    /// <summary>
    /// Trang template không được có SiteRoute: có route nghĩa là nó truy cập được trực tiếp và
    /// render ra trang trắng (không entity → mọi khối dữ liệu rỗng), đồng thời lọt vào sitemap.
    /// </summary>
    [Fact]
    public async Task Trang_template_khong_duoc_sinh_SiteRoute()
    {
        using var fx = new BuilderRenderFixture("tplroute");
        var template = fx.AddPage("Chi tiết bài viết", "tin-tuc-chi-tiet", kind: PageKind.PostTemplate);

        await fx.Routes.SyncPageRouteAsync(template.Id);

        Assert.False(await fx.Db.SiteRoutes.AnyAsync(r => r.TargetId == template.Id));
    }

    [Fact]
    public async Task Doi_trang_thuong_thanh_template_thi_go_route_cu()
    {
        using var fx = new BuilderRenderFixture("tplroute");
        var page = fx.AddPage("Tin tức", "tin-tuc");
        await fx.Routes.SyncPageRouteAsync(page.Id);
        Assert.True(await fx.Db.SiteRoutes.AnyAsync(r => r.TargetId == page.Id));

        page.Kind = PageKind.PostTemplate;
        await fx.Db.SaveChangesAsync();
        await fx.Routes.SyncPageRouteAsync(page.Id);

        Assert.False(await fx.Db.SiteRoutes.AnyAsync(r => r.TargetId == page.Id));
    }

    /// <summary>Trang thường vẫn phải có route như trước — chống hồi quy cho nhánh vừa thêm.</summary>
    [Fact]
    public async Task Trang_thuong_van_sinh_route_nhu_cu()
    {
        using var fx = new BuilderRenderFixture("tplroute");
        var page = fx.AddPage("Giới thiệu", "gioi-thieu");

        await fx.Routes.SyncPageRouteAsync(page.Id);

        var route = await fx.Db.SiteRoutes.FirstOrDefaultAsync(r => r.TargetId == page.Id);
        Assert.NotNull(route);
        Assert.Equal("/gioi-thieu", route!.Path);
    }

    /// <summary>Chuyên mục "tin-tuc" + trang listing "/tin-tuc" + một bài đã publish.</summary>
    private static (Domain.Entities.Content.Post Post, Domain.Entities.Site.Page Listing) SeedTinTuc(
        BuilderRenderFixture fx)
    {
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài một", "bai-mot", cat.Id);
        var listing = fx.AddPage("Tin tức", "tin-tuc");

        fx.AddRoute("/tin-tuc", RouteType.Page, listing.Id);
        fx.AddRoute("/tin-tuc/bai-mot", RouteType.Post, post.Id);

        return (post, listing);
    }
}
