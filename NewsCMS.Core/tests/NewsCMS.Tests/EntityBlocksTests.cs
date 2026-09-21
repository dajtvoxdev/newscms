using System.Net;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.Blocks;

namespace NewsCMS.Tests;

/// <summary>
/// Bộ kiểm thử toàn diện cho 10 khối trường dữ liệu (Entity Blocks) của Phase 2:
/// - Khi không có RouteContext: render placeholder an toàn, không ném ngoại lệ (không throw).
/// - Khi có RouteContext: lấy đúng dữ liệu từ DbContext và phát đúng định dạng.
/// </summary>
public sealed class EntityBlocksTests
{
    // ── 1. entity-title ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Title_KhongCoRoute_TraVePlaceholder()
    {
        using var fx = new BuilderRenderFixture("ent-title");
        var block = new EntityTitleBlock(fx.ContentTypes);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"tag\":\"h2\"}", Route: null);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("<h2", html);
        Assert.Contains("Tiêu đề bài viết / sản phẩm mẫu", html);
    }

    [Fact]
    public async Task Title_VoiPost_TraVeTieuDeVaDungTag()
    {
        using var fx = new BuilderRenderFixture("ent-title");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Hạt cà phê Cầu Đất", "hat-ca-phe", cat.Id);

        var block = new EntityTitleBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/hat-ca-phe", cat.Id, cat.Slug);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"tag\":\"h1\",\"align\":\"center\"}", route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("<h1", html);
        Assert.Contains("Hạt cà phê Cầu Đất", WebUtility.HtmlDecode(html));
        Assert.Contains("text-align:center", html);
    }

    [Fact]
    public async Task Title_VoiProduct_TraVeTenSanPham()
    {
        using var fx = new BuilderRenderFixture("ent-title");
        var cat = fx.AddProductCategory("Cà phê hạt", "ca-phe-hat");
        var product = fx.AddProduct("Arabica Đặc Biệt", "arabica-dac-biet", cat.Id);

        var block = new EntityTitleBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Product, product.Id, product.Slug, "/san-pham/arabica-dac-biet");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Arabica Đặc Biệt", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Title_VoiCategory_TraVeTenChuyenMuc()
    {
        using var fx = new BuilderRenderFixture("ent-title");
        var cat = fx.AddCategory("Đời sống", "doi-song");

        var block = new EntityTitleBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Category, cat.Id, cat.Slug, "/doi-song");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Đời sống", WebUtility.HtmlDecode(html));
    }

    // ── 2. entity-image ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Image_KhongCoRoute_TraVePlaceholder()
    {
        using var fx = new BuilderRenderFixture("ent-img");
        var block = new EntityImageBlock(fx.ContentTypes);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, Route: null);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Ảnh đại diện bài viết / sản phẩm mẫu", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Image_VoiPostCoAnh_TraVeImgTag()
    {
        using var fx = new BuilderRenderFixture("ent-img");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var media = new Media
        {
            SiteId = Guid.Empty,
            FilePath = "/media/coffee.jpg",
            FileName = "coffee.jpg",
            MimeType = "image/jpeg",
            StorageKey = "coffee.jpg"
        };
        fx.Db.Medias.Add(media);
        fx.Db.SaveChanges();

        var post = fx.AddPost("Cold brew", "cold-brew", cat.Id);
        post.FeaturedImageId = media.Id;
        fx.Db.SaveChanges();

        var block = new EntityImageBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/cold-brew");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"ratio\":\"16:9\"}", route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("/media/coffee.jpg", html);
        Assert.Contains("aspect-ratio:16/9", html);
    }

    [Fact]
    public async Task Image_VoiPostKhongAnh_TraVeChuoiRong()
    {
        using var fx = new BuilderRenderFixture("ent-img");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết chữ", "bai-viet-chu", cat.Id);

        var block = new EntityImageBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/bai-viet-chu");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        Assert.Equal(string.Empty, html);
    }

    // ── 3. entity-content ────────────────────────────────────────────────────────

    [Fact]
    public async Task Content_KhongCoRoute_TraVePlaceholder()
    {
        using var fx = new BuilderRenderFixture("ent-content");
        var block = new EntityContentBlock(fx.ContentTypes);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, Route: null);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("nc-post-body", html);
        Assert.Contains("Nội dung chi tiết của bài viết hoặc mô tả sản phẩm sẽ hiển thị tại đây", html);
    }

    [Fact]
    public async Task Content_VoiPost_TraVeNoiDungNcPostBody()
    {
        using var fx = new BuilderRenderFixture("ent-content");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài test", "bai-test", cat.Id, "<p>Đây là nội dung thân bài viết rất dài.</p>");

        var block = new EntityContentBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/bai-test");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"maxWidth\":\"820px\"}", route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("nc-post-body", html);
        Assert.Contains("<p>Đây là nội dung thân bài viết rất dài.</p>", html);
        Assert.Contains("max-width:820px", html);

        // Bài không có video/audio — KHÔNG được tải Plyr thừa.
        Assert.DoesNotContain("plyr", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Content_VoiVideo_NhungPlyrKhongNonceRieng()
    {
        // Theme Universal (site builder) render post body DUY NHẤT qua entity-content —
        // khác theme HaiLuuNguoc có Post/Detail.cshtml riêng. Video/audio chèn từ TinyMCE
        // chỉ có thể full-width + skin Plyr nếu khối này tự nhúng CSS/JS.
        using var fx = new BuilderRenderFixture("ent-content-video");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var body = "<p>Xem video:</p><video controls preload=\"metadata\" playsinline "
                 + "style=\"width:100%;height:auto;max-width:100%;border-radius:12px;\">"
                 + "<source src=\"/uploads/videos/clip.mp4\" type=\"video/mp4\"></video>";
        var post = fx.AddPost("Bài có video", "bai-co-video", cat.Id, body);

        var block = new EntityContentBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/bai-co-video");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("<video", html);
        Assert.Contains(".nc-post-body video", html); // CSS full-width scope đúng class
        Assert.Contains("/lib/plyr/plyr.css", html);
        Assert.Contains("/lib/plyr/plyr.polyfilled.min.js", html);
        Assert.Contains("new Plyr(", html);

        // Script src="..." KHÔNG cần nonce (script-src 'self' đã cho phép same-origin);
        // chỉ script inline (không src) mới cần — và đây phải là thứ PageRenderer.
        // StampInlineScriptNonce sẽ tự gắn nonce khi ráp trang, nên ở mức khối KHÔNG được
        // tự chứa sẵn nonce="..." (nonce sinh per-request, khối không biết được).
        Assert.DoesNotContain("nonce=", html);
    }

    [Fact]
    public async Task Content_VoiAudio_NhungPlyr()
    {
        using var fx = new BuilderRenderFixture("ent-content-audio");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var body = "<audio controls preload=\"metadata\" style=\"width:100%\">"
                 + "<source src=\"/uploads/audio/track.mp3\" type=\"audio/mpeg\"></audio>";
        var post = fx.AddPost("Bài có audio", "bai-co-audio", cat.Id, body);

        var block = new EntityContentBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/bai-co-audio");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("<audio", html);
        Assert.Contains("/lib/plyr/plyr.polyfilled.min.js", html);
    }

    [Fact]
    public async Task Content_VoiVideo_CoNutDoiTiLeVaSuaPosterSai()
    {
        using var fx = new BuilderRenderFixture("ent-content-ratio");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        // poster trỏ vào chính file .mp4 — đúng dữ liệu rác TinyMCE từng lưu (xem PlyrAssets).
        var body = "<video controls preload=\"metadata\" poster=\"/uploads/videos/clip.mp4\">"
                 + "<source src=\"/uploads/videos/clip.mp4\" type=\"video/mp4\"></video>";
        var post = fx.AddPost("Bài có video", "bai-co-video-ratio", cat.Id, body);

        var block = new EntityContentBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/bai-co-video-ratio");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        // Nút đổi tỉ lệ + nhãn tiếng Việt cho screen reader.
        Assert.Contains("nc-ratio-toggle", html);
        Assert.Contains("Vừa khung", html);
        Assert.Contains("Lấp đầy", html);

        // Khung 16:9 cho chế độ lấp đầy, và khung chờ trước khi có metadata.
        Assert.Contains("plyr--nc-fill", html);
        Assert.Contains("plyr--nc-pending", html);
        Assert.Contains("aspect-ratio:16/9", html);

        // Video phải căn giữa khi lấp đầy — neo top:0 của Plyr làm cắt mất đáy (nơi có chữ).
        Assert.Contains("translate(-50%,-50%)", html);

        // KHÔNG chừa dải riêng cho thanh điều khiển: người dùng đã từ chối vì player cao hơn
        // video trông như "cục đen lòi ở dưới". Giữ hành vi gốc của Plyr (thanh phủ đáy video).
        Assert.DoesNotContain("--nc-controls-h", html);

        // TOÀN MÀN HÌNH: khung bị ép đúng bằng màn hình còn thẻ video giữ height:auto (inline style
        // của nội dung bài viết) nên cao hơn khung, Plyr overflow:hidden -> CẮT mất đáy. Đo thật
        // trên màn hình ngang 844x390: chỉ thấy 82%, mất đúng dải chữ ở đáy. Phải ép video vừa khít
        // khung ở cả chế độ native fullscreen lẫn fallback (mobile không cho fullscreen trên <div>).
        Assert.Contains(":fullscreen", html);
        Assert.Contains("plyr--fullscreen-fallback", html);
        Assert.Contains("max-height:100%!important", html);

        // Sửa poster sai (.mp4) và gắn #t=0.1 để trình duyệt vẽ frame đầu.
        Assert.Contains("#t=0.1", html);
        Assert.Contains("poster", html);

        // Vẫn KHÔNG được truyền option ratio cho Plyr: ratio:0 làm Plyr throw khi chưa có metadata
        // và mất sạch icon điều khiển (xem plyr-ratio-zero-throw). Khung 16:9 đặt bằng CSS
        // (aspect-ratio) nên chuỗi "ratio:" vẫn xuất hiện — phải khẳng định trên ĐỐI SỐ của Plyr:
        // iconUrl đứng ngay trước captions nghĩa là không có gì chen vào giữa.
        Assert.Contains("iconUrl:SPRITE,captions:", html);
        Assert.DoesNotContain("ratio:0", html);
    }

    // ── 4. entity-meta ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Meta_KhongCoRoute_TraVePlaceholder()
    {
        using var fx = new BuilderRenderFixture("ent-meta");
        var block = new EntityMetaBlock(fx.ContentTypes);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, Route: null);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("CHUYÊN MỤC", html);
    }

    [Fact]
    public async Task Meta_VoiPost_TraVeNgayVaLinkChuyenMuc()
    {
        using var fx = new BuilderRenderFixture("ent-meta");
        var cat = fx.AddCategory("Văn hóa cà phê", "van-hoa-ca-phe");
        var post = fx.AddPost("Hành trình hạt cà phê", "hanh-trinh", cat.Id);
        post.PublishedAt = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
        fx.Db.SaveChanges();

        var block = new EntityMetaBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/van-hoa-ca-phe/hanh-trinh", cat.Id, cat.Slug);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"dateFormat\":\"dd.MM.yyyy\"}", route);

        var html = await block.RenderAsync(ctx);

        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Văn hóa cà phê", decoded);
        Assert.Contains("/van-hoa-ca-phe", decoded);
        Assert.Contains("03.09.2026", decoded);
    }

    // ── 5. breadcrumb ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Breadcrumb_PathRong_TuDongLayTuRouteContext()
    {
        using var fx = new BuilderRenderFixture("ent-bread");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Cà phê ngon", "ca-phe-ngon", cat.Id);

        fx.AddRoute("/tin-tuc", RouteType.Category, cat.Id);
        fx.AddRoute("/tin-tuc/ca-phe-ngon", RouteType.Post, post.Id);

        var block = new BreadcrumbBlock(fx.Db, fx.ContentTypes);
        // Không truyền path trong props
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/ca-phe-ngon", cat.Id, cat.Slug);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{}", route);

        var html = await block.RenderAsync(ctx);

        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Trang chủ", decoded);
        Assert.Contains("Tin tức", decoded);
        Assert.Contains("Cà phê ngon", decoded);
    }

    [Fact]
    public async Task Breadcrumb_VoiProductRoute_TraVeTenSanPham()
    {
        using var fx = new BuilderRenderFixture("ent-bread");
        var cat = fx.AddProductCategory("Sản phẩm", "san-pham");
        var product = fx.AddProduct("Máy pha cà phê", "may-pha-ca-phe", cat.Id);

        fx.AddRoute("/san-pham", RouteType.Category, cat.Id);
        fx.AddRoute("/san-pham/may-pha-ca-phe", RouteType.Product, product.Id);

        var block = new BreadcrumbBlock(fx.Db, fx.ContentTypes);
        var route = new RouteContext(RouteType.Product, product.Id, product.Slug, "/san-pham/may-pha-ca-phe");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Máy pha cà phê", WebUtility.HtmlDecode(html));
    }

    // ── 6. product-price ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Price_KhongCoRoute_TraVePlaceholder()
    {
        using var fx = new BuilderRenderFixture("ent-price");
        var block = new ProductPriceBlock(fx.Db);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, Route: null);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("290.000 ₫", html);
        Assert.Contains("350.000 ₫", html);
    }

    [Fact]
    public async Task Price_VoiGiaThuong_TraVeGiaVND()
    {
        using var fx = new BuilderRenderFixture("ent-price");
        var cat = fx.AddProductCategory("Cà phê", "ca-phe");
        var product = fx.AddProduct("Robusta Honey", "robusta-honey", cat.Id);
        product.Price = 180_000m;
        product.SalePrice = null;
        fx.Db.SaveChanges();

        var block = new ProductPriceBlock(fx.Db);
        var route = new RouteContext(RouteType.Product, product.Id, product.Slug, "/san-pham/robusta-honey");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("180.000 ₫", html);
        Assert.DoesNotContain("line-through", html);
    }

    [Fact]
    public async Task Price_VoiGiaKhuyenMai_TraVeGiaSaleVaGachNgangGiaGoc()
    {
        using var fx = new BuilderRenderFixture("ent-price");
        var cat = fx.AddProductCategory("Cà phê", "ca-phe");
        var product = fx.AddProduct("Arabica Bourbon", "arabica-bourbon", cat.Id);
        product.Price = 250_000m;
        product.SalePrice = 199_000m;
        fx.Db.SaveChanges();

        var block = new ProductPriceBlock(fx.Db);
        var route = new RouteContext(RouteType.Product, product.Id, product.Slug, "/san-pham/arabica-bourbon");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"showCompareAt\":true}", route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("199.000 ₫", html);
        Assert.Contains("250.000 ₫", html);
        Assert.Contains("line-through", html);
    }

    // ── 7. product-gallery ───────────────────────────────────────────────────────

    [Fact]
    public async Task Gallery_KhongCoRoute_TraVePlaceholder()
    {
        using var fx = new BuilderRenderFixture("ent-gallery");
        var block = new ProductGalleryBlock(fx.Db);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, Route: null);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Ảnh sản phẩm mẫu", html);
    }

    [Fact]
    public async Task Gallery_Thumbnails_TraVeMainVaThumbnails()
    {
        using var fx = new BuilderRenderFixture("ent-gallery");
        var cat = fx.AddProductCategory("Cà phê", "ca-phe");

        var media = new Media
        {
            SiteId = Guid.Empty,
            FilePath = "/media/blend-1.jpg",
            FileName = "blend-1.jpg",
            MimeType = "image/jpeg",
            StorageKey = "blend-1.jpg"
        };
        fx.Db.Medias.Add(media);

        var product = new Product
        {
            SiteId = Guid.Empty,
            Name = "Espresso Blend",
            Slug = "espresso-blend",
            Sku = "ESPRESSO-BLEND",
            Description = "<p>mo ta</p>",
            Price = 100_000m,
            ProductCategoryId = cat.Id,
            Status = ProductStatus.Published,
            PublishedAt = DateTime.UtcNow,
            ThumbnailMediaId = media.Id
        };
        fx.Db.Products.Add(product);
        fx.Db.ProductImages.Add(new ProductImage { ProductId = product.Id, Url = "/media/blend-2.jpg", SortOrder = 1 });
        fx.Db.SaveChanges();

        var block = new ProductGalleryBlock(fx.Db);
        var route = new RouteContext(RouteType.Product, product.Id, product.Slug, "/san-pham/espresso-blend");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"preset\":\"thumbnails\"}", route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("/media/blend-1.jpg", html);
        Assert.Contains("/media/blend-2.jpg", html);
        // Thumb đổi ảnh chính qua data-gal-thumb (CSP chặn inline onclick) — nhìn vào đúng móc.
        Assert.Contains("data-gal-thumb=", html);
        Assert.DoesNotContain("onclick=", html);
    }

    // ── 8. related-posts ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RelatedPosts_KhongCoRoute_TraVePlaceholder()
    {
        using var fx = new BuilderRenderFixture("ent-rel");
        var block = new RelatedPostsBlock(fx.Db);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, Route: null);

        var html = await block.RenderAsync(ctx);

        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Bài viết liên quan", decoded);
        Assert.Contains("(mẫu)", decoded);
    }

    [Fact]
    public async Task RelatedPosts_VoiPost_LoaiTruPostHienTaiVaLayCungChuyenMuc()
    {
        using var fx = new BuilderRenderFixture("ent-rel");
        var cat1 = fx.AddCategory("Tin tức", "tin-tuc");
        var cat2 = fx.AddCategory("Sự kiện", "su-kien");

        var currentPost = fx.AddPost("Bài đang xem", "bai-dang-xem", cat1.Id);
        var sameCatPost = fx.AddPost("Bài cùng chuyên mục", "bai-cung-muc", cat1.Id);
        var otherCatPost = fx.AddPost("Bài mục khác", "bai-muc-khac", cat2.Id);

        var block = new RelatedPostsBlock(fx.Db);
        var route = new RouteContext(RouteType.Post, currentPost.Id, currentPost.Slug, "/tin-tuc/bai-dang-xem", cat1.Id, cat1.Slug);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"sameCategory\":true}", route);

        var html = await block.RenderAsync(ctx);

        var decoded = WebUtility.HtmlDecode(html);
        // Phải có bài cùng chuyên mục
        Assert.Contains("Bài cùng chuyên mục", decoded);
        // Không được chứa bài đang xem
        Assert.DoesNotContain("Bài đang xem", decoded);
        // Không được chứa bài chuyên mục khác khi sameCategory = true
        Assert.DoesNotContain("Bài mục khác", decoded);
    }

    // ── 9. product-stock ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Stock_KhongCoRoute_TraVePlaceholder()
    {
        using var fx = new BuilderRenderFixture("ent-stock");
        var block = new ProductStockBlock(fx.Db);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, Route: null);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Còn 12 sản phẩm", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Stock_ConHang_TraVeSoLuong()
    {
        using var fx = new BuilderRenderFixture("ent-stock");
        var cat = fx.AddProductCategory("Cà phê", "ca-phe");
        var product = fx.AddProduct("Cà phê Rang", "ca-phe-rang", cat.Id);
        product.IsTrackingStock = true;
        product.StockQuantity = 45;
        fx.Db.SaveChanges();

        var block = new ProductStockBlock(fx.Db);
        var route = new RouteContext(RouteType.Product, product.Id, product.Slug, "/san-pham/ca-phe-rang");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"showQuantity\":true}", route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Còn 45 sản phẩm", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Stock_HetHang_TraVeHetHang()
    {
        using var fx = new BuilderRenderFixture("ent-stock");
        var cat = fx.AddProductCategory("Cà phê", "ca-phe");
        var product = fx.AddProduct("Cà phê Hết", "ca-phe-het", cat.Id);
        product.IsTrackingStock = true;
        product.StockQuantity = 0;
        fx.Db.SaveChanges();

        var block = new ProductStockBlock(fx.Db);
        var route = new RouteContext(RouteType.Product, product.Id, product.Slug, "/san-pham/ca-phe-het");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", "{\"outOfStockText\":\"Tạm hết hàng\"}", route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Tạm hết hàng", WebUtility.HtmlDecode(html));
    }

    // ── 10. entity-excerpt ───────────────────────────────────────────────────────

    [Fact]
    public async Task Excerpt_KhongCoRoute_TraVePlaceholder()
    {
        using var fx = new BuilderRenderFixture("ent-excerpt");
        var block = new EntityExcerptBlock(fx.ContentTypes);
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, Route: null);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Đoạn mô tả ngắn hoặc sapo mở đầu", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Excerpt_VoiPost_TraVeExcerpt()
    {
        using var fx = new BuilderRenderFixture("ent-excerpt");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết", "bai-viet", cat.Id);
        post.Excerpt = "Đây là đoạn sapo mở đầu câu chuyện cà phê.";
        fx.Db.SaveChanges();

        var block = new EntityExcerptBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/bai-viet");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Đây là đoạn sapo mở đầu câu chuyện cà phê.", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Excerpt_VoiProduct_TraVeShortDescription()
    {
        using var fx = new BuilderRenderFixture("ent-excerpt");
        var cat = fx.AddProductCategory("Sản phẩm", "san-pham");
        var product = fx.AddProduct("Phin cà phê", "phin-ca-phe", cat.Id);
        product.ShortDescription = "Phin nhôm truyền thống chất lượng cao.";
        fx.Db.SaveChanges();

        var block = new EntityExcerptBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Product, product.Id, product.Slug, "/san-pham/phin-ca-phe");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi", null, route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains("Phin nhôm truyền thống chất lượng cao.", WebUtility.HtmlDecode(html));
    }

    // ── 11. prop CSS tự do không được tiêm vào style ─────────────────────────────

    /// <summary>
    /// Prop <c>color</c> là ô text tự do và đổ thẳng vào <c>style="..."</c>. CSP của site dùng
    /// <c>style-src 'unsafe-inline'</c> nên trình duyệt không chặn gì — khối phải tự lọc.
    /// Payload ở đây kết thúc declaration bằng <c>;</c> rồi thêm declaration thứ hai.
    ///
    /// Nhóm test này từng bị xoá trong lần refactor Phase 2.5 (khối đổi constructor sang
    /// IContentTypeRegistry), và bản vá lọc CSS bị revert theo mà không ai biết. Giữ nó ở đây
    /// chính là để lần refactor sau không lặp lại.
    /// </summary>
    [Theory]
    [InlineData("red;color:expression(alert(1))")]
    [InlineData("red;position:absolute")]
    [InlineData("\"><script>alert(1)</script>")]
    [InlineData("url(javascript:alert(1))")]
    public async Task Title_MauDocHai_BiBoVaDungGiaTriMacDinh(string payload)
    {
        using var fx = new BuilderRenderFixture("ent-css");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết", "bai-viet", cat.Id);

        var block = new EntityTitleBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/bai-viet");
        var ctx = new DynamicBlockContext(
            Guid.Empty, "vi", $"{{\"color\":{System.Text.Json.JsonSerializer.Serialize(payload)}}}", route);

        var html = await block.RenderAsync(ctx);

        Assert.DoesNotContain("expression", html);
        Assert.DoesNotContain("position:absolute", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("javascript:", html);
        // Bị chặn thì phải về mặc định của theme, không phải rỗng.
        Assert.Contains("color:var(--color-brand-500)", html);
    }

    /// <summary>
    /// Chiều ngược lại, và là lý do không được chặn hết: chính giá trị mà Hint của prop gợi ý
    /// cho người dùng gõ vào phải được chấp nhận.
    /// </summary>
    [Theory]
    [InlineData("var(--color-brand-500)")]
    [InlineData("#0f172a")]
    [InlineData("rgba(15, 23, 42, 1)")]
    [InlineData("currentColor")]
    public async Task Title_MauHopLe_DuocNhan(string value)
    {
        using var fx = new BuilderRenderFixture("ent-css");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết", "bai-viet", cat.Id);

        var block = new EntityTitleBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/bai-viet");
        var ctx = new DynamicBlockContext(
            Guid.Empty, "vi", $"{{\"color\":{System.Text.Json.JsonSerializer.Serialize(value)}}}", route);

        var html = await block.RenderAsync(ctx);

        Assert.Contains($"color:{value}", html);
    }

    [Fact]
    public async Task Price_MauDocHai_BiBoVaDungGiaTriMacDinh()
    {
        using var fx = new BuilderRenderFixture("ent-css");
        var cat = fx.AddProductCategory("Cà phê", "ca-phe");
        var product = fx.AddProduct("Arabica", "arabica", cat.Id);

        var block = new ProductPriceBlock(fx.Db);
        var route = new RouteContext(RouteType.Product, product.Id, product.Slug, "/san-pham/arabica");
        var ctx = new DynamicBlockContext(
            Guid.Empty, "vi", "{\"color\":\"red;position:fixed\"}", route);

        var html = await block.RenderAsync(ctx);

        Assert.DoesNotContain("position:fixed", html);
        Assert.Contains("color:var(--color-ink)", html);
    }

    [Fact]
    public async Task Image_MaxHeightDocHai_KhongVaoDuocStyle()
    {
        using var fx = new BuilderRenderFixture("ent-css");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");

        // EntityImageBlock trả rỗng khi post không có ảnh — phải seed ảnh để tới được nhánh
        // xử lý style, không thì test đang chứng minh một hành vi khác.
        var media = new Media
        {
            SiteId = Guid.Empty, FilePath = "/media/coffee.jpg",
            FileName = "coffee.jpg", MimeType = "image/jpeg", StorageKey = "coffee.jpg"
        };
        fx.Db.Medias.Add(media);
        fx.Db.SaveChanges();

        var post = fx.AddPost("Bài viết", "bai-viet", cat.Id);
        post.FeaturedImageId = media.Id;
        fx.Db.SaveChanges();

        var block = new EntityImageBlock(fx.ContentTypes);
        var route = new RouteContext(RouteType.Post, post.Id, post.Slug, "/tin-tuc/bai-viet");
        var ctx = new DynamicBlockContext(Guid.Empty, "vi",
            "{\"maxHeight\":\"400px;background:url(javascript:1)\",\"fit\":\"whatever\"}", route);

        var html = await block.RenderAsync(ctx);

        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("background:url", html);
        // fit không hợp lệ phải về cover, không phải xuyên thủng vào object-fit.
        Assert.Contains("object-fit:cover", html);
    }
}
