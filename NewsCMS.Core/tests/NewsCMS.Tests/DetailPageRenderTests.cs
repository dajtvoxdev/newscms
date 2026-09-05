using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.Blocks;

namespace NewsCMS.Tests;

/// <summary>
/// Render trang chi tiết đầu-cuối: template builder khi có, HTML dựng sẵn khi không.
/// Nhóm test "không có template" là chống hồi quy cho toàn bộ site đang chạy — chúng chưa dựng
/// template nào nên phải đi nguyên đường cũ.
/// </summary>
public sealed class DetailPageRenderTests
{
    [Fact]
    public async Task Khong_co_template_thi_van_render_HTML_dung_san()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Cold brew hạt Cầu Đất", "cold-brew", cat.Id, "<p>Thân bài gốc</p>");

        var rendered = await fx.NewPageRenderer().RenderPostAsync(
            post.Id, BuilderRenderFixture.Culture, "/tin-tuc/cold-brew");

        Assert.NotNull(rendered);
        Assert.Contains("Thân bài gốc", rendered!.Html);
        Assert.Contains("Cold brew hạt Cầu Đất", rendered.Html);
        Assert.Equal("Cold brew hạt Cầu Đất", rendered.Title);
    }

    [Fact]
    public async Task Co_template_thi_render_bang_template()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Cold brew hạt Cầu Đất", "cold-brew", cat.Id, "<p>Thân bài gốc</p>");
        var listing = fx.AddPage("Tin tức", "tin-tuc");
        fx.AddRoute("/tin-tuc", RouteType.Page, listing.Id);

        fx.AddPage("Chi tiết bài viết", "tin-tuc-chi-tiet",
            compiledHtml: "<main class=\"dung-bang-builder\"><div data-nc-block=\"entity-slug\"></div></main>",
            kind: PageKind.PostTemplate, parentPageId: listing.Id);

        var rendered = await fx.NewPageRenderer(new EntitySlugBlock()).RenderPostAsync(
            post.Id, BuilderRenderFixture.Culture, "/tin-tuc/cold-brew");

        Assert.NotNull(rendered);
        Assert.Contains("dung-bang-builder", rendered!.Html);
        // Khối động trong template nhận đúng entity của URL.
        Assert.Contains("slug=cold-brew", rendered.Html);
        // HTML dựng sẵn không được lẫn vào.
        Assert.DoesNotContain("Thân bài gốc", rendered.Html);
    }

    /// <summary>
    /// &lt;title&gt; phải là tên bài, không phải tên template. Lấy nhầm thì mọi bài viết trong site
    /// cùng mang một tiêu đề "Chi tiết bài viết" trên tab trình duyệt và trên kết quả tìm kiếm.
    /// </summary>
    [Fact]
    public async Task Title_lay_tu_bai_viet_khong_phai_tu_ten_template()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Cold brew hạt Cầu Đất", "cold-brew", cat.Id);
        var listing = fx.AddPage("Tin tức", "tin-tuc");
        fx.AddRoute("/tin-tuc", RouteType.Page, listing.Id);
        fx.AddPage("Chi tiết bài viết", "tin-tuc-chi-tiet",
            kind: PageKind.PostTemplate, parentPageId: listing.Id);

        var rendered = await fx.NewPageRenderer().RenderPostAsync(
            post.Id, BuilderRenderFixture.Culture, "/tin-tuc/cold-brew");

        Assert.Equal("Cold brew hạt Cầu Đất", rendered!.Title);
        Assert.DoesNotContain("<title>Chi tiết bài viết</title>", rendered.Html);
    }

    [Fact]
    public async Task Template_bi_unpublish_thi_roi_ve_HTML_dung_san()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Cold brew", "cold-brew", cat.Id, "<p>Thân bài gốc</p>");
        var listing = fx.AddPage("Tin tức", "tin-tuc");
        fx.AddRoute("/tin-tuc", RouteType.Page, listing.Id);
        fx.AddPage("Chi tiết bài viết", "tin-tuc-chi-tiet",
            kind: PageKind.PostTemplate, parentPageId: listing.Id, published: false);

        var rendered = await fx.NewPageRenderer().RenderPostAsync(
            post.Id, BuilderRenderFixture.Culture, "/tin-tuc/cold-brew");

        Assert.Contains("Thân bài gốc", rendered!.Html);
    }

    [Fact]
    public async Task San_pham_render_bang_template_khi_co()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var cat = fx.AddProductCategory("Cà phê", "ca-phe");
        var product = fx.AddProduct("Lotus Arabica", "lotus-arabica", cat.Id);
        var listing = fx.AddPage("Sản phẩm", "san-pham");
        fx.AddRoute("/san-pham", RouteType.Page, listing.Id);
        fx.AddPage("Chi tiết sản phẩm", "san-pham-chi-tiet",
            compiledHtml: "<main class=\"sp-builder\"><div data-nc-block=\"entity-slug\"></div></main>",
            kind: PageKind.ProductTemplate, parentPageId: listing.Id);

        var rendered = await fx.NewPageRenderer(new EntitySlugBlock()).RenderProductAsync(
            product.Id, BuilderRenderFixture.Culture, "/san-pham/lotus-arabica");

        Assert.Contains("sp-builder", rendered!.Html);
        Assert.Contains("slug=lotus-arabica", rendered.Html);
        Assert.Equal("Lotus Arabica", rendered.Title);
    }

    [Fact]
    public async Task San_pham_khong_co_template_van_render_HTML_dung_san()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var cat = fx.AddProductCategory("Cà phê", "ca-phe");
        var product = fx.AddProduct("Lotus Arabica", "lotus-arabica", cat.Id);

        var rendered = await fx.NewPageRenderer().RenderProductAsync(
            product.Id, BuilderRenderFixture.Culture, "/san-pham/lotus-arabica");

        Assert.NotNull(rendered);
        Assert.Contains("Lotus Arabica", rendered!.Html);
    }

    // ── Route chuyên mục ──────────────────────────────────────────────────────────

    /// <summary>
    /// Không có template listing → null để caller trả 404, đúng hành vi trước đây (route Category
    /// rơi vào `default: NotFound()`). Không có HTML dựng sẵn nào để rơi về ở đây.
    /// </summary>
    [Fact]
    public async Task Chuyen_muc_khong_co_template_thi_tra_null()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var cat = fx.AddCategory("Tin tức mới", "tin-tuc-moi");
        fx.AddRoute("/tin-tuc-moi", RouteType.Category, cat.Id);

        var rendered = await fx.NewPageRenderer().RenderCategoryAsync(
            cat.Id, BuilderRenderFixture.Culture, "/tin-tuc-moi");

        Assert.Null(rendered);
    }

    [Fact]
    public async Task Chuyen_muc_co_template_thi_render_duoc()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var template = fx.AddPage("Listing chuyên mục", "listing-chuyen-muc",
            compiledHtml: "<main class=\"cm-builder\"><div data-nc-block=\"entity-slug\"></div></main>",
            kind: PageKind.CategoryTemplate);
        var cat = fx.AddCategory("Tin tức mới", "tin-tuc-moi", templatePageId: template.Id);
        fx.AddRoute("/tin-tuc-moi", RouteType.Category, cat.Id);

        var rendered = await fx.NewPageRenderer(new EntitySlugBlock()).RenderCategoryAsync(
            cat.Id, BuilderRenderFixture.Culture, "/tin-tuc-moi");

        Assert.NotNull(rendered);
        Assert.Contains("cm-builder", rendered!.Html);
        Assert.Contains("slug=tin-tuc-moi", rendered.Html);
        Assert.Equal("Tin tức mới", rendered.Title);
    }

    [Fact]
    public async Task Bai_viet_render_bang_template_chua_day_du_khoi_entity()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var cat = fx.AddCategory("Kiến thức", "kien-thuc");
        var post = fx.AddPost("Hương vị Espresso chuẩn Ý", "huong-vi-espresso", cat.Id,
            "<p>Espresso hoàn hảo cần áp suất 9 bar và nhiệt độ 92 độ C.</p>");
        post.Excerpt = "Khám phá nghệ thuật chiết xuất cà phê espresso đỉnh cao.";
        fx.Db.SaveChanges();

        var listing = fx.AddPage("Kiến thức", "kien-thuc");
        fx.AddRoute("/kien-thuc", RouteType.Page, listing.Id);

        const string templateHtml =
            "<article class=\"bai-viet-builder\">" +
            "<div data-nc-block=\"breadcrumb\"></div>" +
            "<div data-nc-block=\"entity-title\" data-nc-props=\"{&quot;tag&quot;:&quot;h1&quot;}\"></div>" +
            "<div data-nc-block=\"entity-excerpt\"></div>" +
            "<div data-nc-block=\"entity-meta\"></div>" +
            "<div data-nc-block=\"entity-content\"></div>" +
            "</article>";

        fx.AddPage("Template bài viết chi tiết", "template-bai-viet",
            compiledHtml: templateHtml, kind: PageKind.PostTemplate, parentPageId: listing.Id);

        var renderer = fx.NewPageRenderer(
            new EntityTitleBlock(fx.ContentTypes),
            new EntityExcerptBlock(fx.ContentTypes),
            new EntityMetaBlock(fx.ContentTypes),
            new EntityContentBlock(fx.ContentTypes),
            new BreadcrumbBlock(fx.Db, fx.ContentTypes));

        var rendered = await renderer.RenderPostAsync(
            post.Id, BuilderRenderFixture.Culture, "/kien-thuc/huong-vi-espresso");

        Assert.NotNull(rendered);
        Assert.Contains("bai-viet-builder", rendered!.Html);
        var decodedHtml = System.Net.WebUtility.HtmlDecode(rendered.Html);
        Assert.Contains("Hương vị Espresso chuẩn Ý", decodedHtml);
        Assert.Contains("Khám phá nghệ thuật chiết xuất", decodedHtml);
        Assert.Contains("Kiến thức", decodedHtml);
        Assert.Contains("Espresso hoàn hảo cần áp suất 9 bar", decodedHtml);
    }

    [Fact]
    public async Task San_pham_render_bang_template_chua_khoi_price_va_stock()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var cat = fx.AddProductCategory("Thiết bị", "thiet-bi");
        var product = fx.AddProduct("Cối xay cà phê tay", "coi-xay-tay", cat.Id);
        product.Price = 1_500_000m;
        product.SalePrice = 1_250_000m;
        product.IsTrackingStock = true;
        product.StockQuantity = 18;
        product.Description = "<p>Lưỡi xay thép hình nón 48mm chuẩn xác.</p>";
        fx.Db.SaveChanges();

        var listing = fx.AddPage("Cửa hàng", "san-pham");
        fx.AddRoute("/san-pham", RouteType.Page, listing.Id);

        const string templateHtml =
            "<div class=\"san-pham-builder\">" +
            "<div data-nc-block=\"entity-title\"></div>" +
            "<div data-nc-block=\"product-price\"></div>" +
            "<div data-nc-block=\"product-stock\"></div>" +
            "<div data-nc-block=\"entity-content\"></div>" +
            "</div>";

        fx.AddPage("Template sản phẩm chi tiết", "template-san-pham",
            compiledHtml: templateHtml, kind: PageKind.ProductTemplate, parentPageId: listing.Id);

        var renderer = fx.NewPageRenderer(
            new EntityTitleBlock(fx.ContentTypes),
            new ProductPriceBlock(fx.Db),
            new ProductStockBlock(fx.Db),
            new EntityContentBlock(fx.ContentTypes));

        var rendered = await renderer.RenderProductAsync(
            product.Id, BuilderRenderFixture.Culture, "/san-pham/coi-xay-tay");

        Assert.NotNull(rendered);
        Assert.Contains("san-pham-builder", rendered!.Html);
        var decodedHtml = System.Net.WebUtility.HtmlDecode(rendered.Html);
        Assert.Contains("Cối xay cà phê tay", decodedHtml);
        Assert.Contains("1.250.000 ₫", decodedHtml);
        Assert.Contains("1.500.000 ₫", decodedHtml);
        Assert.Contains("Còn 18 sản phẩm", decodedHtml);
        Assert.Contains("Lưỡi xay thép hình nón", decodedHtml);
    }

    // ── Trang thường không đổi ────────────────────────────────────────────────────

    /// <summary>
    /// Trang landing bình thường vẫn render như trước và KHÔNG có ngữ cảnh entity — nếu route rò
    /// vào đây thì khối dữ liệu trên trang chủ sẽ bị lọc theo một bài viết ngẫu nhiên.
    /// </summary>
    [Fact]
    public async Task Trang_thuong_khong_co_ngu_canh_entity()
    {
        using var fx = new BuilderRenderFixture("detailrender");
        var page = fx.AddPage("Trang chủ", "",
            compiledHtml: "<main><div data-nc-block=\"entity-slug\"></div></main>");

        var rendered = await fx.NewPageRenderer(new EntitySlugBlock()).RenderAsync(
            page.Id, BuilderRenderFixture.Culture);

        Assert.NotNull(rendered);
        Assert.Contains("slug=(none)", rendered!.Html);
    }

    /// <summary>Khối tối giản in slug của entity — đủ để chứng minh ngữ cảnh đi tới nơi.</summary>
    private sealed class EntitySlugBlock : IDynamicBlock
    {
        public string Key => "entity-slug";

        public BlockDescriptor Descriptor => new(
            Label: "Slug entity",
            Category: "Chi tiết",
            Description: "Probe cho test.",
            IconSvg: "<svg/>",
            Presets: [],
            Props: [],
            EntityScoped: true);

        public Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default) =>
            Task.FromResult($"<span>slug={context.Route?.Slug ?? "(none)"}</span>");
    }
}
