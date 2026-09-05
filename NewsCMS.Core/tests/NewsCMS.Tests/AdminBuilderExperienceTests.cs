using Microsoft.AspNetCore.Mvc;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Infrastructure.Builder.Blocks;
using NewsCMS.Infrastructure.Catalog;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Web.Areas.Admin.Pages.Builder;
using Xunit;

namespace NewsCMS.Tests;

/// <summary>
/// Bộ kiểm thử cho Phase 3: Nâng cấp trải nghiệm Admin Site Builder.
/// Kiểm tra:
/// 1. Lưu & đọc ParentPageId, IsDefaultTemplate qua BuilderPageService.
/// 2. Chặn vòng lặp phân cấp cha-con (A → A hoặc A → B → A).
/// 3. Lưu & đọc TemplatePageId trong chuyên mục sản phẩm qua ProductCategoryService.
/// 4. BlockPreviewApiController hiển thị dữ liệu thật (RouteContext) khi có SampleEntityId.
/// </summary>
public sealed class AdminBuilderExperienceTests
{
    // ── 1. Cây phân cấp trang & Template trong BuilderPageService ─────────────────

    [Fact]
    public async Task BuilderPageService_TaoTrangVoiParentPageIdVaIsDefaultTemplate_LuuChinhXac()
    {
        using var fx = new BuilderRenderFixture("admin-p3-create");
        var service = new BuilderPageService(fx.Db, new ContentSanitizer(), fx.Routes);

        // 1. Tạo trang listing cha
        var parentRes = await service.CreateAsync(new BuilderPageSaveRequest(
            Title: "Trang Tin Tức",
            Slug: "tin-tuc",
            BuilderJson: null,
            CompiledHtml: "<main>listing tin tức</main>",
            CompiledCss: null,
            CustomCss: null,
            CustomJs: null,
            Kind: "Landing"
        ));
        Assert.True(parentRes.Succeeded);
        var parentId = parentRes.Value!.Id;

        // 2. Tạo template trang con gắn ParentPageId và IsDefaultTemplate = true
        var templateRes = await service.CreateAsync(new BuilderPageSaveRequest(
            Title: "Template Chi Tiết Bài Viết",
            Slug: "tin-tuc-chi-tiet",
            BuilderJson: null,
            CompiledHtml: "<article>chi tiet bai viet</article>",
            CompiledCss: null,
            CustomCss: null,
            CustomJs: null,
            Kind: "PostTemplate",
            LayoutId: null,
            ParentPageId: parentId,
            IsDefaultTemplate: true
        ));
        Assert.True(templateRes.Succeeded);
        var templateId = templateRes.Value!.Id;

        // 3. Kiểm tra GetByIdAsync
        var getRes = await service.GetByIdAsync(templateId);
        Assert.True(getRes.Succeeded);
        Assert.Equal(parentId, getRes.Value!.ParentPageId);
        Assert.True(getRes.Value!.IsDefaultTemplate);
        Assert.Equal("PostTemplate", getRes.Value!.Kind);

        // 4. Kiểm tra ListAsync trả về đúng thông tin phân cấp
        var listRes = await service.ListAsync(1, 10, null);
        var foundTemplate = listRes.Items.FirstOrDefault(p => p.Id == templateId);
        Assert.NotNull(foundTemplate);
        Assert.Equal(parentId, foundTemplate.ParentPageId);
        Assert.True(foundTemplate.IsDefaultTemplate);
    }

    [Fact]
    public async Task BuilderPageService_CapNhatParentPageIdVaIsDefaultTemplate_ThanhCong()
    {
        using var fx = new BuilderRenderFixture("admin-p3-update");
        var service = new BuilderPageService(fx.Db, new ContentSanitizer(), fx.Routes);

        var pageA = await service.CreateAsync(new BuilderPageSaveRequest(
            Title: "Sản phẩm", Slug: "san-pham", BuilderJson: null,
            CompiledHtml: "<div>san pham</div>", CompiledCss: null, CustomCss: null, CustomJs: null, Kind: "Landing"));
        var pageB = await service.CreateAsync(new BuilderPageSaveRequest(
            Title: "Template Sản phẩm", Slug: "san-pham-detail", BuilderJson: null,
            CompiledHtml: "<div>detail</div>", CompiledCss: null, CustomCss: null, CustomJs: null, Kind: "ProductTemplate"));

        // Cập nhật PageB trỏ về PageA làm cha và gắn IsDefaultTemplate
        var updateRes = await service.UpdateAsync(pageB.Value!.Id, new BuilderPageSaveRequest(
            Title: "Template Sản phẩm (Đã gán cha)",
            Slug: "san-pham-detail",
            BuilderJson: null,
            CompiledHtml: "<div>detail moi</div>",
            CompiledCss: null,
            CustomCss: null,
            CustomJs: null,
            Kind: "ProductTemplate",
            ParentPageId: pageA.Value!.Id,
            IsDefaultTemplate: true
        ));

        Assert.True(updateRes.Succeeded);
        Assert.Equal(pageA.Value!.Id, updateRes.Value!.ParentPageId);
        Assert.True(updateRes.Value!.IsDefaultTemplate);
    }

    [Fact]
    public async Task BuilderPageService_ChanTuLamCha_TraVeLoi()
    {
        using var fx = new BuilderRenderFixture("admin-p3-self");
        var service = new BuilderPageService(fx.Db, new ContentSanitizer(), fx.Routes);

        var page = await service.CreateAsync(new BuilderPageSaveRequest(
            Title: "Trang Độc Lập", Slug: "doc-lap", BuilderJson: null,
            CompiledHtml: "<div>noi dung</div>", CompiledCss: null, CustomCss: null, CustomJs: null, Kind: "Static"));

        var selfUpdate = await service.UpdateAsync(page.Value!.Id, new BuilderPageSaveRequest(
            Title: "Trang Độc Lập", Slug: "doc-lap", BuilderJson: null,
            CompiledHtml: "<div>noi dung</div>", CompiledCss: null, CustomCss: null, CustomJs: null, Kind: "Static",
            ParentPageId: page.Value!.Id // Tự trỏ vào chính nó
        ));

        Assert.False(selfUpdate.Succeeded);
        Assert.Contains("không thể làm cha của chính nó", selfUpdate.Error!);
    }

    [Fact]
    public async Task BuilderPageService_ChanVongLapPhanCap_TraVeLoi()
    {
        using var fx = new BuilderRenderFixture("admin-p3-circular");
        var service = new BuilderPageService(fx.Db, new ContentSanitizer(), fx.Routes);

        // Page A làm cha Page B
        var pageA = await service.CreateAsync(new BuilderPageSaveRequest(
            Title: "Trang A", Slug: "trang-a", BuilderJson: null,
            CompiledHtml: "<div>A</div>", CompiledCss: null, CustomCss: null, CustomJs: null, Kind: "Landing"));
        var pageB = await service.CreateAsync(new BuilderPageSaveRequest(
            Title: "Trang B", Slug: "trang-b", BuilderJson: null,
            CompiledHtml: "<div>B</div>", CompiledCss: null, CustomCss: null, CustomJs: null, Kind: "Landing",
            ParentPageId: pageA.Value!.Id));

        // Cố tình sửa Page A để trỏ về Page B (tạo vòng lặp A → B → A)
        var loopUpdate = await service.UpdateAsync(pageA.Value!.Id, new BuilderPageSaveRequest(
            Title: "Trang A", Slug: "trang-a", BuilderJson: null,
            CompiledHtml: "<div>A</div>", CompiledCss: null, CustomCss: null, CustomJs: null, Kind: "Landing",
            ParentPageId: pageB.Value!.Id));

        Assert.False(loopUpdate.Succeeded);
        Assert.Contains("vòng lặp phân cấp", loopUpdate.Error!);
    }

    // ── 2. Chuyên mục sản phẩm: ProductCategoryService ───────────────────────────

    [Fact]
    public async Task ProductCategoryService_LuuVaNapTemplatePageId_ChinhXac()
    {
        using var fx = new BuilderRenderFixture("admin-p3-prodcat");
        var prodCatService = new ProductCategoryService(fx.Db, new SlugHelper());

        var templatePage = fx.AddPage("Template Danh Mục Rượu Vang", "template-ruou-vang",
            "<section>ruou vang</section>", PageKind.CategoryTemplate);

        // Tạo chuyên mục có gán TemplatePageId
        var createDto = new ProductCategoryUpsertDto(
            Id: null,
            Name: "Rượu Vang Đỏ",
            Slug: "ruou-vang-do",
            Description: "Các dòng vang đỏ nhập khẩu",
            Order: 1,
            IsActive: true,
            ParentId: null,
            TemplatePageId: templatePage.Id
        );
        var createRes = await prodCatService.CreateAsync(createDto);
        Assert.True(createRes.Succeeded);
        var catId = createRes.Value;

        // Tra cứu qua GetByIdAsync
        var fetched = await prodCatService.GetByIdAsync(catId);
        Assert.NotNull(fetched);
        Assert.Equal(templatePage.Id, fetched.TemplatePageId);

        // Tra cứu qua GetAllAsync
        var all = await prodCatService.GetAllAsync();
        var itemInAll = all.FirstOrDefault(x => x.Id == catId);
        Assert.NotNull(itemInAll);
        Assert.Equal(templatePage.Id, itemInAll.TemplatePageId);

        // Cập nhật gỡ bỏ TemplatePageId
        var updateDto = new ProductCategoryUpsertDto(
            Id: catId,
            Name: "Rượu Vang Đỏ",
            Slug: "ruou-vang-do",
            Description: "Các dòng vang đỏ nhập khẩu",
            Order: 1,
            IsActive: true,
            ParentId: null,
            TemplatePageId: null // Gỡ bỏ template
        );
        var updateRes = await prodCatService.UpdateAsync(updateDto);
        Assert.True(updateRes.Succeeded);

        var afterUpdate = await prodCatService.GetByIdAsync(catId);
        Assert.NotNull(afterUpdate);
        Assert.Null(afterUpdate.TemplatePageId);
    }

    // ── 3. BlockPreviewApiController với Dữ liệu Thật (SampleEntityId) ─────────────

    [Fact]
    public async Task BlockPreviewApiController_KhongCoSampleId_RenderPlaceholder()
    {
        using var fx = new BuilderRenderFixture("admin-p3-preview-null");
        var registry = new DynamicBlockRegistry(new IDynamicBlock[] { new EntityTitleBlock(fx.ContentTypes) });
        var controller = new BlockPreviewApiController(registry, fx.Site, fx.ContentTypes);

        var actionResult = await controller.Preview("entity-title", new BlockPreviewApiController.BlockPreviewRequest(
            PropsJson: "{}",
            SampleEntityId: null,
            SampleType: null
        ), default);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        Assert.NotNull(okResult.Value);

        // Lấy property html qua reflection
        var htmlProp = okResult.Value.GetType().GetProperty("html");
        Assert.NotNull(htmlProp);
        var html = htmlProp.GetValue(okResult.Value) as string;
        Assert.NotNull(html);

        // Khi không có sample, khối render placeholder mẫu
        Assert.Contains("Tiêu đề bài viết", html);
    }

    [Fact]
    public async Task BlockPreviewApiController_CoSampleIdPost_RenderDuLieuThat()
    {
        using var fx = new BuilderRenderFixture("admin-p3-preview-post");
        var cat = fx.AddCategory("Đời Sống", "doi-song");
        var post = fx.AddPost("Hành trình khám phá Tây Bắc mùa lúa chín", "hanh-trinh-tay-bac", cat.Id);

        var registry = new DynamicBlockRegistry(new IDynamicBlock[] { new EntityTitleBlock(fx.ContentTypes) });
        var controller = new BlockPreviewApiController(registry, fx.Site, fx.ContentTypes);

        var actionResult = await controller.Preview("entity-title", new BlockPreviewApiController.BlockPreviewRequest(
            PropsJson: "{}",
            SampleEntityId: post.Id,
            SampleType: "post"
        ), default);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var htmlProp = okResult.Value!.GetType().GetProperty("html");
        var html = htmlProp!.GetValue(okResult.Value) as string;

        // Render ra tiêu đề bài viết THẬT của bài đã chọn (được WebUtility.HtmlEncode trong block)
        var decodedHtml = System.Net.WebUtility.HtmlDecode(html ?? string.Empty);
        Assert.Contains("Hành trình khám phá Tây Bắc mùa lúa chín", decodedHtml);
        Assert.DoesNotContain("mẫu", decodedHtml);
    }

    [Fact]
    public async Task BlockPreviewApiController_CoSampleIdProduct_RenderGiaThat()
    {
        using var fx = new BuilderRenderFixture("admin-p3-preview-prod");
        var cat = new ProductCategory
        {
            SiteId = Guid.Empty,
            Name = "Cà Phê Rang Xay",
            Slug = "ca-phe-rang-xay",
            IsActive = true
        };
        fx.Db.ProductCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var product = new Product
        {
            SiteId = Guid.Empty,
            Name = "Cà phê Robusta Honey 500g",
            Slug = "ca-phe-robusta-honey-500g",
            Sku = "SKU-ROBUSTA-HONEY",
            Description = "Cà phê Robusta mật ong chất lượng cao",
            ProductCategoryId = cat.Id,
            Price = 240000m,
            SalePrice = 195000m,
            Status = ProductStatus.Published
        };
        fx.Db.Products.Add(product);
        await fx.Db.SaveChangesAsync();

        var registry = new DynamicBlockRegistry(new IDynamicBlock[] { new ProductPriceBlock(fx.Db) });
        var controller = new BlockPreviewApiController(registry, fx.Site, fx.ContentTypes);

        var actionResult = await controller.Preview("product-price", new BlockPreviewApiController.BlockPreviewRequest(
            PropsJson: "{}",
            SampleEntityId: product.Id,
            SampleType: "product"
        ), default);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var htmlProp = okResult.Value!.GetType().GetProperty("html");
        var html = htmlProp!.GetValue(okResult.Value) as string;

        // Render ra giá THẬT của sản phẩm đã chọn: giá khuyến mãi 195.000₫ và giá gốc 240.000₫
        Assert.Contains("195.000", html);
        Assert.Contains("240.000", html);
        Assert.DoesNotContain("390.000", html); // Không dùng giá placeholder giả định
    }
}
