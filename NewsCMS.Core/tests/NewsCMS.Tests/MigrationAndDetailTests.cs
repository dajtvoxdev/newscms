using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Infrastructure.Builder.Blocks;
using Xunit;

namespace NewsCMS.Tests;

/// <summary>
/// Bộ kiểm thử cho Phase 6: Dọn dẹp & Di trú (Cleanup & Migration).
/// Kiểm tra:
/// 1. ChuKafe_TrangSanPham_RenderThanhCong_Khong404: Trang /san-pham được tạo thành công, có route công khai, render không bị 404.
/// 2. ChuKafe_TinTucChiTiet_SuDungBuilderTemplate: Bài viết tại /tin-tuc/{slug} tự động áp dụng template con tin-tuc-chi-tiet dựng bằng builder blocks.
/// 3. ChuKafe_SanPhamChiTiet_SuDungBuilderTemplate: Sản phẩm tại /san-pham/{slug} tự động áp dụng template con san-pham-chi-tiet dựng bằng builder blocks.
/// 4. LegacyFallback_HoatDongKhiKhongCoTemplate: Khi site không cấu hình template, hệ thống fallback về RenderFallbackHtmlAsync (BuildPostHtml / BuildProductHtml cũ) đảm bảo tương thích ngược 100%.
/// 5. SoSanh_ThongTin_TemplateBuilder_Va_LegacyFallback: Đảm bảo cả hai cách render đều hiển thị đầy đủ tiêu đề, chuyên mục, nội dung thực thể.
/// </summary>
public sealed class MigrationAndDetailTests
{
    private static IDynamicBlock[] CreateChuKafeBlocks(BuilderRenderFixture fx) =>
    [
        new EntityTitleBlock(fx.ContentTypes),
        new EntityContentBlock(fx.ContentTypes),
        new EntityImageBlock(fx.ContentTypes),
        new EntityMetaBlock(fx.ContentTypes),
        new EntityExcerptBlock(fx.ContentTypes),
        new ProductPriceBlock(fx.Db),
        new ProductGridBlock(fx.Db)
    ];

    [Fact]
    public async Task ChuKafe_TrangSanPham_RenderThanhCong_Khong404()
    {
        using var fx = new BuilderRenderFixture("chukafe-san-pham");
        var blocks = CreateChuKafeBlocks(fx);
        var api = fx.NewSiteBuilderApi(blocks);

        var spec = new SiteSpec(
            SiteName: "CHU Kafe",
            SiteSlug: "chu-kafe",
            PrimaryDomain: null,
            DefaultTheme: "Universal",
            DefaultCulture: "vi",
            SupportedCultures: "vi",
            Layouts: new[]
            {
                new LayoutSpec(
                    Key: "shell-default",
                    Name: "Khung CHU Kafe",
                    Kind: "Shell",
                    CompiledHtml: "<!DOCTYPE html><html><body><header>CHU</header><main data-nc-body></main><footer>FOOTER</footer></body></html>",
                    CompiledCss: null,
                    CustomJs: null,
                    IsDefault: true
                )
            },
            Pages: new[]
            {
                new PageSpec(
                    Title: "Trang chủ",
                    Slug: "home",
                    CompiledHtml: "<section><h1>CHU KAFE Home</h1></section>",
                    CompiledCss: null,
                    CustomCss: null,
                    CustomJs: null,
                    Kind: "Landing",
                    LayoutKey: "shell-default",
                    ParentSlug: null,
                    IsDefaultTemplate: false
                ),
                new PageSpec(
                    Title: "Sản phẩm",
                    Slug: "san-pham",
                    CompiledHtml: "<section><h1>Sản phẩm CHU Kafe</h1><div data-nc-block=\"product-grid\" data-nc-props='{\"count\":12}'></div></section>",
                    CompiledCss: null,
                    CustomCss: null,
                    CustomJs: null,
                    Kind: "Static",
                    LayoutKey: "shell-default",
                    ParentSlug: null,
                    IsDefaultTemplate: false
                )
            },
            Categories: Array.Empty<CategorySpec>(),
            Menus: new[]
            {
                new MenuSpec(
                    Name: "Menu chính",
                    Location: "header",
                    Items: new[]
                    {
                        new MenuItemSpec("Trang chủ", "/", 0, "_self"),
                        new MenuItemSpec("Sản phẩm", "/san-pham", 1, "_self")
                    }
                )
            },
            DesignTokens: Array.Empty<DesignTokenSpec>(),
            Settings: Array.Empty<SiteSettingSpec>()
        );

        var report = await api.ApplyAsync(spec);
        Assert.True(report.Succeeded);

        // Route /san-pham phải tồn tại trong bảng định tuyến
        var route = await fx.Routes.ResolveAsync("/san-pham");
        Assert.NotNull(route);
        Assert.Equal(RouteType.Page, route.RouteType);

        // Render trang /san-pham không bị 404
        var page = await fx.Db.Pages.FirstOrDefaultAsync(p => p.Slug == "san-pham");
        Assert.NotNull(page);

        var renderer = fx.NewPageRenderer(blocks);
        var rendered = await renderer.RenderAsync(page.Id, "vi");
        Assert.NotNull(rendered);
        Assert.Contains("Sản phẩm CHU Kafe", rendered!.Html);
        Assert.Contains("CHU", rendered.Html);
        Assert.Contains("FOOTER", rendered.Html);
    }

    [Fact]
    public async Task ChuKafe_TinTucChiTiet_SuDungBuilderTemplate()
    {
        using var fx = new BuilderRenderFixture("chukafe-post-template");
        var blocks = CreateChuKafeBlocks(fx);
        var api = fx.NewSiteBuilderApi(blocks);

        var spec = new SiteSpec(
            SiteName: "CHU Kafe",
            SiteSlug: "chu-kafe",
            PrimaryDomain: null,
            DefaultTheme: "Universal",
            DefaultCulture: "vi",
            SupportedCultures: "vi",
            Layouts: new[]
            {
                new LayoutSpec(
                    Key: "shell-default",
                    Name: "Khung CHU Kafe",
                    Kind: "Shell",
                    CompiledHtml: "<html><body><main data-nc-body></main></body></html>",
                    CompiledCss: null,
                    CustomJs: null,
                    IsDefault: true
                )
            },
            Pages: new[]
            {
                new PageSpec(
                    Title: "Tin tức",
                    Slug: "tin-tuc",
                    CompiledHtml: "<section><h1>Danh sách tin tức</h1></section>",
                    CompiledCss: null,
                    CustomCss: null,
                    CustomJs: null,
                    Kind: "Static",
                    LayoutKey: "shell-default",
                    ParentSlug: null,
                    IsDefaultTemplate: false
                ),
                new PageSpec(
                    Title: "Chi tiết bài viết",
                    Slug: "tin-tuc-chi-tiet",
                    CompiledHtml: "<article class=\"chu-post-template\">" +
                                  "<div data-nc-block=\"entity-meta\" data-nc-props='{\"showCategory\":true,\"showDate\":true}'></div>" +
                                  "<div data-nc-block=\"entity-title\" data-nc-props='{\"tag\":\"h1\"}'></div>" +
                                  "<div data-nc-block=\"entity-excerpt\"></div>" +
                                  "<div data-nc-block=\"entity-content\"></div>" +
                                  "</article>",
                    CompiledCss: null,
                    CustomCss: null,
                    CustomJs: null,
                    Kind: "PostTemplate",
                    LayoutKey: "shell-default",
                    ParentSlug: "tin-tuc",
                    IsDefaultTemplate: true
                )
            },
            Categories: new[]
            {
                new CategorySpec(
                    Name: "Tin tức",
                    Slug: "tin-tuc",
                    Description: "Chuyện cà phê",
                    Type: "Post",
                    Order: 1,
                    TemplatePageSlug: null
                )
            },
            Menus: Array.Empty<MenuSpec>(),
            DesignTokens: Array.Empty<DesignTokenSpec>(),
            Settings: Array.Empty<SiteSettingSpec>()
        );

        var report = await api.ApplyAsync(spec);
        Assert.True(report.Succeeded);

        // Tạo bài viết thực tế
        var cat = await fx.Db.Categories.FirstAsync(c => c.Slug == "tin-tuc");
        var post = fx.AddPost("Hành Trình Cold Brew Thủ Công", "hanh-trinh-cold-brew", cat.Id, "<p>Chi tiết về quá trình ủ lạnh 24 giờ.</p>");
        post.Excerpt = "Khám phá hương vị cà phê ủ lạnh thanh mát.";
        await fx.Db.SaveChangesAsync();

        var renderer = fx.NewPageRenderer(blocks);
        var rendered = await renderer.RenderPostAsync(post.Id, "vi", "/tin-tuc/hanh-trinh-cold-brew");

        Assert.NotNull(rendered);
        // Khẳng định template builder được áp dụng
        Assert.Contains("chu-post-template", rendered!.Html);
        Assert.Contains("Hành Trình Cold Brew Thủ Công", rendered.Html);
        Assert.Contains("Chi tiết về quá trình ủ lạnh 24 giờ.", rendered.Html);
        Assert.Contains("Khám phá hương vị cà phê ủ lạnh thanh mát.", rendered.Html);
        Assert.Equal("Hành Trình Cold Brew Thủ Công", rendered.Title);
    }

    [Fact]
    public async Task ChuKafe_SanPhamChiTiet_SuDungBuilderTemplate()
    {
        using var fx = new BuilderRenderFixture("chukafe-prod-template");
        var blocks = CreateChuKafeBlocks(fx);
        var api = fx.NewSiteBuilderApi(blocks);

        var spec = new SiteSpec(
            SiteName: "CHU Kafe",
            SiteSlug: "chu-kafe",
            PrimaryDomain: null,
            DefaultTheme: "Universal",
            DefaultCulture: "vi",
            SupportedCultures: "vi",
            Layouts: new[]
            {
                new LayoutSpec(
                    Key: "shell-default",
                    Name: "Khung CHU Kafe",
                    Kind: "Shell",
                    CompiledHtml: "<html><body><main data-nc-body></main></body></html>",
                    CompiledCss: null,
                    CustomJs: null,
                    IsDefault: true
                )
            },
            Pages: new[]
            {
                new PageSpec(
                    Title: "Sản phẩm",
                    Slug: "san-pham",
                    CompiledHtml: "<section><h1>Danh mục sản phẩm</h1></section>",
                    CompiledCss: null,
                    CustomCss: null,
                    CustomJs: null,
                    Kind: "Static",
                    LayoutKey: "shell-default",
                    ParentSlug: null,
                    IsDefaultTemplate: false
                ),
                new PageSpec(
                    Title: "Chi tiết sản phẩm",
                    Slug: "san-pham-chi-tiet",
                    CompiledHtml: "<article class=\"chu-product-template\">" +
                                  "<div class=\"grid-cols-2\">" +
                                  "<div data-nc-block=\"entity-image\"></div>" +
                                  "<div>" +
                                  "<div data-nc-block=\"entity-title\" data-nc-props='{\"tag\":\"h1\"}'></div>" +
                                  "<div data-nc-block=\"product-price\" data-nc-props='{\"size\":\"lg\"}'></div>" +
                                  "<div data-nc-block=\"entity-content\"></div>" +
                                  "</div></div></article>",
                    CompiledCss: null,
                    CustomCss: null,
                    CustomJs: null,
                    Kind: "ProductTemplate",
                    LayoutKey: "shell-default",
                    ParentSlug: "san-pham",
                    IsDefaultTemplate: true
                )
            },
            Categories: Array.Empty<CategorySpec>(),
            Menus: Array.Empty<MenuSpec>(),
            DesignTokens: Array.Empty<DesignTokenSpec>(),
            Settings: Array.Empty<SiteSettingSpec>()
        );

        var report = await api.ApplyAsync(spec);
        Assert.True(report.Succeeded);

        // Tạo sản phẩm thực tế
        var cat = fx.AddProductCategory("Hạt Rang", "hat-rang");
        var product = fx.AddProduct("Lotus Arabica Specialty", "lotus-arabica-specialty", cat.Id);
        product.Price = 280000;
        product.SalePrice = 250000;
        product.Description = "<p>Hạt Arabica Cầu Đất sơ chế ướt, rang mộc độ vừa.</p>";
        await fx.Db.SaveChangesAsync();

        var renderer = fx.NewPageRenderer(blocks);
        var rendered = await renderer.RenderProductAsync(product.Id, "vi", "/san-pham/lotus-arabica-specialty");

        Assert.NotNull(rendered);
        // Khẳng định template builder sản phẩm được kích hoạt
        Assert.Contains("chu-product-template", rendered!.Html);
        Assert.Contains("Lotus Arabica Specialty", rendered.Html);
        Assert.Contains("250.000", rendered.Html);
        Assert.Contains("Hạt Arabica Cầu Đất sơ chế ướt", rendered.Html);
        Assert.Equal("Lotus Arabica Specialty", rendered.Title);
    }

    [Fact]
    public async Task LegacyFallback_HoatDongKhiKhongCoTemplate()
    {
        using var fx = new BuilderRenderFixture("legacy-fallback");
        var renderer = fx.NewPageRenderer();

        // 1. Kiểm tra fallback của bài viết (RenderFallbackHtmlAsync / BuildPostHtml)
        var postCat = fx.AddCategory("Đời sống", "doi-song");
        var post = fx.AddPost("Hương vị mùa thu", "huong-vi-mua-thu", postCat.Id, "<p>Nội dung mùa thu.</p>");
        post.Excerpt = "Mô tả ngắn mùa thu.";
        await fx.Db.SaveChangesAsync();

        var renderedPost = await renderer.RenderPostAsync(post.Id, "vi", "/doi-song/huong-vi-mua-thu");
        Assert.NotNull(renderedPost);
        // Khẳng định rơi vào mã HTML inline fallback của PostContentType.RenderFallbackHtmlAsync
        Assert.Contains("max-width:820px;margin:0 auto", renderedPost!.Html);
        Assert.Contains("nc-post-body", renderedPost.Html);
        Assert.Contains("Hương vị mùa thu", renderedPost.Html);
        Assert.Contains("Nội dung mùa thu.", renderedPost.Html);
        Assert.Contains("Mô tả ngắn mùa thu.", renderedPost.Html);

        // 2. Kiểm tra fallback của sản phẩm (RenderFallbackHtmlAsync / BuildProductHtml)
        var prodCat = fx.AddProductCategory("Cà phê túi lọc", "ca-phe-tui-loc");
        var product = fx.AddProduct("Drip Bag Classic", "drip-bag-classic", prodCat.Id);
        product.Price = 120000;
        product.Description = "<p>Hộp 10 gói tiện lợi.</p>";
        await fx.Db.SaveChangesAsync();

        var renderedProduct = await renderer.RenderProductAsync(product.Id, "vi", "/san-pham/drip-bag-classic");
        Assert.NotNull(renderedProduct);
        // Khẳng định rơi vào mã HTML inline fallback của ProductContentType.RenderFallbackHtmlAsync
        Assert.Contains("max-width:1100px;margin:0 auto", renderedProduct!.Html);
        Assert.Contains("Drip Bag Classic", renderedProduct.Html);
        Assert.Contains("120.000", renderedProduct.Html);
        Assert.Contains("Hộp 10 gói tiện lợi.", renderedProduct.Html);
    }

    [Fact]
    public async Task SoSanh_ThongTin_TemplateBuilder_Va_LegacyFallback()
    {
        using var fx = new BuilderRenderFixture("compare-template-legacy");
        var blocks = CreateChuKafeBlocks(fx);

        var cat = fx.AddCategory("Cà Phê", "ca-phe");
        var post = fx.AddPost("Nghệ Thuật Cupping", "nghe-thuat-cupping", cat.Id, "<p>Thử nếm và đánh giá mùi hương.</p>");
        post.Excerpt = "Kỹ thuật cupping chuẩn SCA.";
        await fx.Db.SaveChangesAsync();

        // 1. Render qua Legacy Fallback
        var legacyRenderer = fx.NewPageRenderer();
        var legacyResult = await legacyRenderer.RenderPostAsync(post.Id, "vi", "/ca-phe/nghe-thuat-cupping");
        Assert.NotNull(legacyResult);

        // 2. Tạo template builder cho chuyên mục / listing
        var listing = fx.AddPage("Cà Phê", "ca-phe");
        fx.AddRoute("/ca-phe", RouteType.Page, listing.Id);
        fx.AddPage("Template Cupping", "tpl-cupping",
            compiledHtml: "<article class=\"custom-template\">" +
                          "<div data-nc-block=\"entity-title\"></div>" +
                          "<div data-nc-block=\"entity-excerpt\"></div>" +
                          "<div data-nc-block=\"entity-content\"></div>" +
                          "</article>",
            kind: PageKind.PostTemplate, parentPageId: listing.Id, isDefaultTemplate: true);
        await fx.Db.SaveChangesAsync();

        // 3. Render qua Builder Template
        var templateRenderer = fx.NewPageRenderer(blocks);
        var templateResult = await templateRenderer.RenderPostAsync(post.Id, "vi", "/ca-phe/nghe-thuat-cupping");
        Assert.NotNull(templateResult);

        // Cả 2 đều có đầy đủ thông tin cốt lõi
        Assert.Equal(legacyResult!.Title, templateResult!.Title);
        Assert.Contains("Nghệ Thuật Cupping", legacyResult.Html);
        Assert.Contains("Nghệ Thuật Cupping", templateResult.Html);
        Assert.Contains("Thử nếm và đánh giá mùi hương.", legacyResult.Html);
        Assert.Contains("Thử nếm và đánh giá mùi hương.", templateResult.Html);
        Assert.Contains("Kỹ thuật cupping chuẩn SCA.", legacyResult.Html);
        Assert.Contains("Kỹ thuật cupping chuẩn SCA.", templateResult.Html);
    }
}

