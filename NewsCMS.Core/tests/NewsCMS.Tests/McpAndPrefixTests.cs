using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;
using Xunit;

namespace NewsCMS.Tests;

/// <summary>
/// Bộ kiểm thử cho Phase 5: MCP + Prefix dữ liệu sản phẩm.
/// Kiểm tra:
/// 1. ApplyAsync: PageSpec phân giải đúng ParentSlug và cờ IsDefaultTemplate; không tạo route cho trang template.
/// 2. ApplyAsync: CategorySpec phân giải đúng TemplatePageSlug thành TemplatePageId.
/// 3. ExportAsync: Xuất đầy đủ ParentSlug, IsDefaultTemplate và TemplatePageSlug.
/// 4. PreviewAsync: Hỗ trợ sampleSlug để render trang template với dữ liệu entity mẫu.
/// 5. Prefix sản phẩm: Mặc định là /san-pham/{slug}.
/// 6. Prefix sản phẩm: Theo ProductCategory.PathSlug khi có cấu hình chuyên mục.
/// 7. Prefix sản phẩm: Theo SiteSetting "catalog:detailPrefix".
/// 8. ResyncProductRoutesAsync: Tự động cập nhật route và sinh Redirect 301 khi prefix thay đổi.
/// </summary>
public sealed class McpAndPrefixTests
{
    [Fact]
    public async Task ApplyAsync_PageSpec_PhanGiaiParentSlug_Va_IsDefaultTemplate()
    {
        using var fx = new BuilderRenderFixture("mcp-page-spec");
        var api = fx.NewSiteBuilderApi();

        var spec = new SiteSpec(
            SiteName: "Test Site",
            SiteSlug: "test",
            PrimaryDomain: null,
            DefaultTheme: "Universal",
            DefaultCulture: "vi",
            SupportedCultures: "vi",
            Layouts: Array.Empty<LayoutSpec>(),
            Pages: new[]
            {
                // Đưa template con lên trước trong mảng để kiểm chứng ApplyPagesAsync tự sắp xếp cha trước con
                new PageSpec(
                    Title: "Chi Tiết Tin Tức",
                    Slug: "tin-tuc-chi-tiet",
                    CompiledHtml: "<article>Template nội dung</article>",
                    CompiledCss: null,
                    CustomCss: null,
                    CustomJs: null,
                    Kind: "PostTemplate",
                    LayoutKey: null,
                    ParentSlug: "tin-tuc",
                    IsDefaultTemplate: true
                ),
                new PageSpec(
                    Title: "Tin Tức",
                    Slug: "tin-tuc",
                    CompiledHtml: "<main>Danh sách tin tức</main>",
                    CompiledCss: null,
                    CustomCss: null,
                    CustomJs: null,
                    Kind: "Landing",
                    LayoutKey: null,
                    ParentSlug: null,
                    IsDefaultTemplate: false
                )
            },
            Categories: Array.Empty<CategorySpec>(),
            Menus: Array.Empty<MenuSpec>(),
            DesignTokens: Array.Empty<DesignTokenSpec>(),
            Settings: Array.Empty<SiteSettingSpec>()
        );

        var report = await api.ApplyAsync(spec);
        Assert.True(report.Succeeded);

        var parent = await fx.Db.Pages.FirstOrDefaultAsync(p => p.Slug == "tin-tuc");
        Assert.NotNull(parent);
        Assert.Null(parent.ParentPageId);

        var child = await fx.Db.Pages.FirstOrDefaultAsync(p => p.Slug == "tin-tuc-chi-tiet");
        Assert.NotNull(child);
        Assert.Equal(parent.Id, child.ParentPageId);
        Assert.True(child.IsDefaultTemplate);

        // Trang cha có route công khai
        var parentRoute = await fx.Db.SiteRoutes.FirstOrDefaultAsync(r => r.Path == "/tin-tuc");
        Assert.NotNull(parentRoute);

        // Trang template KHÔNG có route công khai
        var childRoute = await fx.Db.SiteRoutes.FirstOrDefaultAsync(r => r.Path == "/tin-tuc-chi-tiet");
        Assert.Null(childRoute);
    }

    [Fact]
    public async Task ApplyAsync_CategorySpec_PhanGiaiTemplatePageSlug()
    {
        using var fx = new BuilderRenderFixture("mcp-cat-spec");
        var api = fx.NewSiteBuilderApi();

        var spec = new SiteSpec(
            SiteName: "Test Site",
            SiteSlug: "test",
            PrimaryDomain: null,
            DefaultTheme: "Universal",
            DefaultCulture: "vi",
            SupportedCultures: "vi",
            Layouts: Array.Empty<LayoutSpec>(),
            Pages: new[]
            {
                new PageSpec(
                    Title: "Template Chuyên Mục Đồ Uống",
                    Slug: "tpl-chuyen-muc",
                    CompiledHtml: "<main>Template chuyên mục</main>",
                    CompiledCss: null,
                    CustomCss: null,
                    CustomJs: null,
                    Kind: "CategoryTemplate",
                    LayoutKey: null,
                    ParentSlug: null,
                    IsDefaultTemplate: false
                )
            },
            Categories: new[]
            {
                new CategorySpec(
                    Name: "Cà Phê",
                    Slug: "ca-phe",
                    Description: "Mô tả cà phê",
                    Type: "Post",
                    Order: 1,
                    TemplatePageSlug: "tpl-chuyen-muc"
                )
            },
            Menus: Array.Empty<MenuSpec>(),
            DesignTokens: Array.Empty<DesignTokenSpec>(),
            Settings: Array.Empty<SiteSettingSpec>()
        );

        var report = await api.ApplyAsync(spec);
        Assert.True(report.Succeeded);

        var templatePage = await fx.Db.Pages.FirstOrDefaultAsync(p => p.Slug == "tpl-chuyen-muc");
        Assert.NotNull(templatePage);

        var category = await fx.Db.Categories.FirstOrDefaultAsync(c => c.Slug == "ca-phe");
        Assert.NotNull(category);
        Assert.Equal(templatePage.Id, category.TemplatePageId);
    }

    [Fact]
    public async Task ExportAsync_XuatDung_ParentSlug_IsDefaultTemplate_TemplatePageSlug()
    {
        using var fx = new BuilderRenderFixture("mcp-export");
        var parentPage = fx.AddPage("Sản Phẩm", "san-pham", kind: PageKind.Landing);
        var templatePage = fx.AddPage(
            "Template Chi Tiết Sản Phẩm",
            "san-pham-chi-tiet",
            kind: PageKind.ProductTemplate,
            parentPageId: parentPage.Id,
            isDefaultTemplate: true
        );

        var cat = fx.AddCategory("Trà & Cà Phê", "tra-ca-phe");
        cat.TemplatePageId = templatePage.Id;
        await fx.Db.SaveChangesAsync();

        var api = fx.NewSiteBuilderApi();
        var exportResult = await api.ExportAsync();
        Assert.True(exportResult.Succeeded);

        var spec = exportResult.Value;
        Assert.NotNull(spec);

        var exportedChild = spec.Pages.FirstOrDefault(p => p.Slug == "san-pham-chi-tiet");
        Assert.NotNull(exportedChild);
        Assert.Equal("san-pham", exportedChild.ParentSlug);
        Assert.True(exportedChild.IsDefaultTemplate);

        var exportedCat = spec.Categories.FirstOrDefault(c => c.Slug == "tra-ca-phe");
        Assert.NotNull(exportedCat);
        Assert.Equal("san-pham-chi-tiet", exportedCat.TemplatePageSlug);
    }

    [Fact]
    public async Task PreviewAsync_VoiSampleSlug_RenderDungDuLieuMau()
    {
        using var fx = new BuilderRenderFixture("mcp-preview");

        var templatePage = fx.AddPage(
            title: "Template Bài Viết Mẫu",
            slug: "tpl-post",
            compiledHtml: "<article><h1 data-nc-block=\"entity-title\"></h1></article>",
            kind: PageKind.PostTemplate,
            isDefaultTemplate: true
        );

        var cat = fx.AddCategory("Đời Sống", "doi-song");
        var post1 = fx.AddPost("Bài viết cũ hơn", "bai-viet-cu", cat.Id);
        var post2 = fx.AddPost("Nghệ Thuật Pha Cold Brew Chuẩn Vị", "nghe-thuat-cold-brew", cat.Id);
        await fx.Db.SaveChangesAsync();

        // Sử dụng EntityTitleBlock để render khối entity-title
        var api = fx.NewSiteBuilderApi(new Infrastructure.Builder.Blocks.EntityTitleBlock(fx.ContentTypes));

        // Preview với sampleSlug chỉ định post2
        var previewResult = await api.PreviewAsync(templatePage.Id, "vi", sampleSlug: "nghe-thuat-cold-brew");
        Assert.True(previewResult.Succeeded);

        var html = previewResult.Value;
        Assert.NotNull(html);
        Assert.Contains("Nghệ Thuật Pha Cold Brew Chuẩn Vị", html);
    }

    [Fact]
    public async Task ProductPrefix_MacDinh_LaSanPham()
    {
        using var fx = new BuilderRenderFixture("prod-prefix-default");

        var cat = new ProductCategory
        {
            SiteId = fx.Site.SiteId,
            Name = "Chung",
            Slug = "chung",
            IsActive = true
        };
        fx.Db.ProductCategories.Add(cat);

        var product = new Product
        {
            SiteId = fx.Site.SiteId,
            Name = "Máy Xay Timemore C3",
            Slug = "timemore-c3",
            Description = "Mô tả máy xay",
            Sku = "SKU-C3",
            ProductCategoryId = cat.Id,
            ProductCategory = cat,
            Status = ProductStatus.Published
        };
        fx.Db.Products.Add(product);
        await fx.Db.SaveChangesAsync();

        var contentType = fx.ContentTypes.FindByRouteType(RouteType.Product);
        Assert.NotNull(contentType);

        var path = await contentType.BuildPathAsync(product.Id);
        Assert.Equal("/san-pham/timemore-c3", path);
    }

    [Fact]
    public async Task ProductPrefix_TheoCategoryPathSlug()
    {
        using var fx = new BuilderRenderFixture("prod-prefix-cat");

        var cat = new ProductCategory
        {
            SiteId = fx.Site.SiteId,
            Name = "Dụng Cụ Pha Chế",
            Slug = "dung-cu",
            PathSlug = "dung-cu-ca-phe",
            IsActive = true
        };
        fx.Db.ProductCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var product = new Product
        {
            SiteId = fx.Site.SiteId,
            Name = "Bình Pha Chemex 6 Cup",
            Slug = "chemex-6-cup",
            Description = "Mô tả bình chemex",
            Sku = "SKU-CHEMEX",
            ProductCategoryId = cat.Id,
            ProductCategory = cat,
            Status = ProductStatus.Published
        };
        fx.Db.Products.Add(product);
        await fx.Db.SaveChangesAsync();

        var contentType = fx.ContentTypes.FindByRouteType(RouteType.Product);
        Assert.NotNull(contentType);

        var path = await contentType.BuildPathAsync(product.Id);
        Assert.Equal("/dung-cu-ca-phe/chemex-6-cup", path);
    }

    [Fact]
    public async Task ProductPrefix_TheoSiteSetting_CatalogDetailPrefix()
    {
        using var fx = new BuilderRenderFixture("prod-prefix-setting");

        fx.Db.SiteSettings.Add(new SiteSetting
        {
            SiteId = fx.Site.SiteId,
            Group = "Catalog",
            Key = "catalog:detailPrefix",
            Value = "cua-hang"
        });

        var cat = new ProductCategory
        {
            SiteId = fx.Site.SiteId,
            Name = "Cà Phê Hạt",
            Slug = "ca-phe-hat",
            IsActive = true
        };
        fx.Db.ProductCategories.Add(cat);

        var product = new Product
        {
            SiteId = fx.Site.SiteId,
            Name = "Hạt Cà Phê Ethiopia Yirgacheffe",
            Slug = "ethiopia-yirgacheffe",
            Description = "Mô tả cà phê ethiopia",
            Sku = "SKU-ETHIOPIA",
            ProductCategoryId = cat.Id,
            ProductCategory = cat,
            Status = ProductStatus.Published
        };
        fx.Db.Products.Add(product);
        await fx.Db.SaveChangesAsync();

        var contentType = fx.ContentTypes.FindByRouteType(RouteType.Product);
        Assert.NotNull(contentType);

        var path = await contentType.BuildPathAsync(product.Id);
        Assert.Equal("/cua-hang/ethiopia-yirgacheffe", path);
    }

    [Fact]
    public async Task ResyncProductRoutes_SinhRedirect301_KhiDoiPrefix()
    {
        using var fx = new BuilderRenderFixture("prod-resync-redirect");

        var cat = new ProductCategory
        {
            SiteId = fx.Site.SiteId,
            Name = "Cà Phê Rang",
            Slug = "ca-phe-rang",
            IsActive = true
        };
        fx.Db.ProductCategories.Add(cat);

        var product = new Product
        {
            SiteId = fx.Site.SiteId,
            Name = "Cà phê Robusta Mộc",
            Slug = "robusta-moc",
            Description = "Mô tả cà phê robusta",
            Sku = "SKU-ROBUSTA",
            ProductCategoryId = cat.Id,
            ProductCategory = cat,
            Status = ProductStatus.Published
        };
        fx.Db.Products.Add(product);
        await fx.Db.SaveChangesAsync();

        // Ban đầu đồng bộ route mặc định: /san-pham/robusta-moc
        await fx.Routes.SyncProductRouteAsync(product.Id);

        var initialRoute = await fx.Routes.ResolveAsync("/san-pham/robusta-moc");
        Assert.NotNull(initialRoute);
        Assert.Equal(product.Id, initialRoute.TargetId);

        // Đổi prefix site sang "thuc-don"
        var setting = new SiteSetting
        {
            SiteId = fx.Site.SiteId,
            Group = "Catalog",
            Key = "catalog:detailPrefix",
            Value = "thuc-don"
        };
        fx.Db.SiteSettings.Add(setting);
        await fx.Db.SaveChangesAsync();

        // Thực hiện Resync toàn bộ route sản phẩm
        await fx.Routes.ResyncProductRoutesAsync();

        // Route mới phải giải quyết được
        var newRoute = await fx.Routes.ResolveAsync("/thuc-don/robusta-moc");
        Assert.NotNull(newRoute);
        Assert.Equal(product.Id, newRoute.TargetId);

        // Hệ thống phải tự động sinh bản ghi Redirect 301 từ đường dẫn cũ sang đường dẫn mới
        var redirect = await fx.Db.Redirects.FirstOrDefaultAsync(r => r.FromPath == "/san-pham/robusta-moc");
        Assert.NotNull(redirect);
        Assert.Equal("/thuc-don/robusta-moc", redirect.ToPath);
        Assert.Equal(301, redirect.StatusCode);
    }
}
