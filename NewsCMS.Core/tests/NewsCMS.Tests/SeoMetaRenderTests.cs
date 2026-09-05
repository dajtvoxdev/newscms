using System.Net;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;
using Xunit;

namespace NewsCMS.Tests;

/// <summary>
/// Bộ kiểm thử cho Phase 4: SEO Meta, OpenGraph, Twitter Card và Schema.org JSON-LD.
/// Kiểm tra:
/// 1. Fallback tự động cho trang tĩnh / landing khi chưa có SeoMeta (Title, Robots, Canonical, OpenGraph, WebPage JSON-LD).
/// 2. Ghi đè chính xác khi có bản ghi SeoMeta tuỳ biến cho trang.
/// 3. Fallback cho bài viết từ Excerpt, FeaturedImage, Article JSON-LD.
/// 4. Fallback cho sản phẩm từ ShortDescription, Thumbnail, Product JSON-LD kèm Offer giá và kho hàng.
/// 5. Ưu tiên SeoMeta tùy biến của thực thể so với dữ liệu fallback gốc.
/// 6. Trang template builder khi render entity phát sinh SEO meta của entity chứ không phải tên trang template.
/// </summary>
public sealed class SeoMetaRenderTests
{
    [Fact]
    public async Task Page_KhongCoSeoMeta_RenderFallbackMacDinh()
    {
        using var fx = new BuilderRenderFixture("seo-page-def");
        var page = fx.AddPage("Giới thiệu công ty", "gioi-thieu");

        var rendered = await fx.NewPageRenderer().RenderAsync(page.Id, "vi");
        Assert.NotNull(rendered);

        var html = WebUtility.HtmlDecode(rendered.Html);

        // Title chuẩn
        Assert.Contains("<title>Giới thiệu công ty</title>", html);
        // Robots mặc định index, follow
        Assert.Contains("<meta name=\"robots\" content=\"index, follow\">", html);
        // Canonical chuẩn theo slug
        Assert.Contains("<link rel=\"canonical\" href=\"/gioi-thieu\">", html);
        // OpenGraph
        Assert.Contains("<meta property=\"og:title\" content=\"Giới thiệu công ty\">", html);
        Assert.Contains("<meta property=\"og:type\" content=\"website\">", html);
        Assert.Contains("<meta property=\"og:url\" content=\"/gioi-thieu\">", html);
        // Twitter Card
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary\">", html);
        // Schema.org WebPage JSON-LD
        Assert.Contains("\"@type\":\"WebPage\"", html);
        Assert.Contains("\"name\":\"Giới thiệu công ty\"", html);
    }

    [Fact]
    public async Task Page_CoSeoMetaCustom_RenderTheoCustom()
    {
        using var fx = new BuilderRenderFixture("seo-page-custom");
        var page = fx.AddPage("Liên hệ", "lien-he");

        // Lưu cấu hình SEO tùy biến
        await fx.SeoMetas.SaveAsync(new SeoMetaSaveRequest(
            EntityType: "page",
            EntityId: page.Id,
            Culture: "vi",
            MetaTitle: "Liên hệ tư vấn kiến trúc & nội thất | Hai Lưu Ngược",
            MetaDescription: "Liên hệ văn phòng Hai Lưu Ngược để được tư vấn thiết kế chuyên nghiệp.",
            OgImage: "/uploads/seo-contact.jpg",
            Canonical: "/lien-he-chinh-thuc",
            Robots: "noindex, nofollow",
            SchemaJsonLd: "{\"@context\":\"https://schema.org\",\"@type\":\"ContactPage\",\"name\":\"Custom Contact\"}",
            OgType: "website",
            TwitterCard: "summary_large_image",
            Priority: 0.8,
            ChangeFreq: "monthly"
        ));

        var rendered = await fx.NewPageRenderer().RenderAsync(page.Id, "vi");
        Assert.NotNull(rendered);

        var html = WebUtility.HtmlDecode(rendered.Html);

        // Thẻ title và meta description tùy biến
        Assert.Contains("<title>Liên hệ tư vấn kiến trúc & nội thất | Hai Lưu Ngược</title>", html);
        Assert.Contains("<meta name=\"description\" content=\"Liên hệ văn phòng Hai Lưu Ngược để được tư vấn thiết kế chuyên nghiệp.\">", html);
        // Robots noindex, nofollow tùy biến
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\">", html);
        // Canonical tùy biến
        Assert.Contains("<link rel=\"canonical\" href=\"/lien-he-chinh-thuc\">", html);
        // OpenGraph và Twitter Card với ảnh tùy biến
        Assert.Contains("<meta property=\"og:image\" content=\"/uploads/seo-contact.jpg\">", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\">", html);
        // Schema.org override
        Assert.Contains("\"@type\":\"ContactPage\"", html);
        Assert.Contains("\"name\":\"Custom Contact\"", html);
    }

    [Fact]
    public async Task Post_KhongCoSeoMeta_RenderFallbackTuExcerptVaImage()
    {
        using var fx = new BuilderRenderFixture("seo-post-fallback");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Khai trương chi nhánh Cà phê mới", "khai-truong-chi-nhanh-moi", cat.Id);
        post.Excerpt = "Chuỗi cà phê mở thêm chi nhánh thứ 5 tại trung tâm.";

        var media = new Media
        {
            SiteId = Guid.Empty,
            FilePath = "/media/branch5.jpg",
            FileName = "branch5.jpg",
            MimeType = "image/jpeg",
            StorageKey = "branch5.jpg"
        };
        fx.Db.Medias.Add(media);
        post.FeaturedImageId = media.Id;
        post.FeaturedImage = media;
        await fx.Db.SaveChangesAsync();

        var rendered = await fx.NewPageRenderer().RenderPostAsync(post.Id, "vi");
        Assert.NotNull(rendered);

        var html = WebUtility.HtmlDecode(rendered.Html);

        // Title bài viết
        Assert.Contains("<title>Khai trương chi nhánh Cà phê mới</title>", html);
        // Description fallback từ Excerpt
        Assert.Contains("<meta name=\"description\" content=\"Chuỗi cà phê mở thêm chi nhánh thứ 5 tại trung tâm.\">", html);
        // OpenGraph fallback
        Assert.Contains("<meta property=\"og:type\" content=\"article\">", html);
        Assert.Contains("<meta property=\"og:image\" content=\"/media/branch5.jpg\">", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\">", html);
        // Schema.org Article JSON-LD
        Assert.Contains("\"@type\":\"Article\"", html);
        Assert.Contains("\"headline\":\"Khai trương chi nhánh Cà phê mới\"", html);
        Assert.Contains("\"image\":\"/media/branch5.jpg\"", html);
        Assert.Contains("\"articleSection\":\"Tin tức\"", html);
    }

    [Fact]
    public async Task Product_KhongCoSeoMeta_RenderFallbackGiaVaOffer()
    {
        using var fx = new BuilderRenderFixture("seo-prod-fallback");
        var cat = new ProductCategory
        {
            SiteId = Guid.Empty,
            Name = "Dụng Cụ Pha Chế",
            Slug = "dung-cu-pha-che",
            IsActive = true
        };
        fx.Db.ProductCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var thumb = new Media
        {
            SiteId = Guid.Empty,
            FilePath = "/media/timemore-c3.jpg",
            FileName = "timemore-c3.jpg",
            MimeType = "image/jpeg",
            StorageKey = "timemore-c3.jpg"
        };
        fx.Db.Medias.Add(thumb);
        await fx.Db.SaveChangesAsync();

        var product = new Product
        {
            SiteId = Guid.Empty,
            Name = "Máy xay cà phê Timemore C3",
            Slug = "may-xay-timemore-c3",
            Sku = "TM-C3-PRO",
            ShortDescription = "Máy xay cà phê cầm tay đĩa thép không gỉ",
            Description = "<p>Mô tả chi tiết máy xay</p>",
            ProductCategoryId = cat.Id,
            Price = 1200000m,
            SalePrice = 990000m,
            StockQuantity = 15,
            ThumbnailMediaId = thumb.Id,
            Thumbnail = thumb,
            Status = ProductStatus.Published
        };
        fx.Db.Products.Add(product);
        await fx.Db.SaveChangesAsync();

        var rendered = await fx.NewPageRenderer().RenderProductAsync(product.Id, "vi");
        Assert.NotNull(rendered);

        var html = WebUtility.HtmlDecode(rendered.Html);

        // Title sản phẩm
        Assert.Contains("<title>Máy xay cà phê Timemore C3</title>", html);
        // Description từ ShortDescription
        Assert.Contains("<meta name=\"description\" content=\"Máy xay cà phê cầm tay đĩa thép không gỉ\">", html);
        // OpenGraph Product
        Assert.Contains("<meta property=\"og:type\" content=\"product\">", html);
        Assert.Contains("<meta property=\"og:image\" content=\"/media/timemore-c3.jpg\">", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\">", html);
        // Schema.org Product JSON-LD có Offer giá 990.000 và tình trạng InStock
        Assert.Contains("\"@type\":\"Product\"", html);
        Assert.Contains("\"name\":\"Máy xay cà phê Timemore C3\"", html);
        Assert.Contains("\"sku\":\"TM-C3-PRO\"", html);
        Assert.Contains("\"@type\":\"Offer\"", html);
        Assert.Contains("\"price\":990000", html);
        Assert.Contains("\"availability\":\"https://schema.org/InStock\"", html);
    }

    [Fact]
    public async Task Entity_CoSeoMetaOverride_GhiDeFallback()
    {
        using var fx = new BuilderRenderFixture("seo-override");
        var cat = fx.AddCategory("Đời Sống", "doi-song");
        var post = fx.AddPost("Tiêu Đề Bài Gốc", "tieu-de-bai-goc", cat.Id);
        post.Excerpt = "Mô tả trích dẫn gốc trong bài viết";
        await fx.Db.SaveChangesAsync();

        // Ghi đè SEO tùy biến
        await fx.SeoMetas.SaveAsync(new SeoMetaSaveRequest(
            EntityType: "post",
            EntityId: post.Id,
            Culture: "vi",
            MetaTitle: "SEO Title Được Tối Ưu Cho Google",
            MetaDescription: "SEO Description Đã Tối Ưu CTR Cao",
            OgImage: "/media/seo-custom.png",
            Canonical: "/doi-song/tieu-de-chuan-seo",
            Robots: "index, follow",
            SchemaJsonLd: null,
            OgType: "article",
            TwitterCard: "summary_large_image",
            Priority: null,
            ChangeFreq: null
        ));

        var rendered = await fx.NewPageRenderer().RenderPostAsync(post.Id, "vi");
        Assert.NotNull(rendered);

        var html = WebUtility.HtmlDecode(rendered.Html);

        // Title & Description lấy theo SeoMeta tùy biến chứ không lấy theo entity gốc
        Assert.Contains("<title>SEO Title Được Tối Ưu Cho Google</title>", html);
        Assert.Contains("<meta name=\"description\" content=\"SEO Description Đã Tối Ưu CTR Cao\">", html);
        Assert.DoesNotContain($"<meta name=\"description\" content=\"{post.Excerpt}\">", html);
        Assert.Contains("<link rel=\"canonical\" href=\"/doi-song/tieu-de-chuan-seo\">", html);
        Assert.Contains("<meta property=\"og:image\" content=\"/media/seo-custom.png\">", html);
    }

    [Fact]
    public async Task TemplatePage_RenderEntity_PhatSeoMetaCuaEntityChuKhongPhaiTemplate()
    {
        using var fx = new BuilderRenderFixture("seo-template-entity");

        // Tạo trang template bài viết (Kind = PostTemplate)
        var templatePage = fx.AddPage(
            title: "Template Chi Tiết Tin Tức Toàn Site",
            slug: "template-tin-tuc",
            compiledHtml: "<article data-nc-block=\"entity-title\"></article>",
            kind: PageKind.PostTemplate,
            isDefaultTemplate: true
        );

        var cat = fx.AddCategory("Cà phê", "ca-phe");
        var post = fx.AddPost("Nghệ thuật rang cà phê thủ công", "nghe-thuat-rang-ca-phe", cat.Id);
        post.Excerpt = "Tìm hiểu kỹ thuật rang thủ công tạo hương vị đặc biệt.";
        await fx.Db.SaveChangesAsync();

        var rendered = await fx.NewPageRenderer().RenderPostAsync(post.Id, "vi");
        Assert.NotNull(rendered);

        var html = WebUtility.HtmlDecode(rendered.Html);

        // Tiêu đề trang phải là tên bài viết, KHÔNG phải tên template trang builder
        Assert.Contains("<title>Nghệ thuật rang cà phê thủ công</title>", html);
        Assert.DoesNotContain("<title>Template Chi Tiết Tin Tức Toàn Site</title>", html);
        // Thẻ meta description là của bài viết
        Assert.Contains("<meta name=\"description\" content=\"Tìm hiểu kỹ thuật rang thủ công tạo hương vị đặc biệt.\">", html);
        // Schema.org là Article của bài viết
        Assert.Contains("\"@type\":\"Article\"", html);
        Assert.Contains("\"headline\":\"Nghệ thuật rang cà phê thủ công\"", html);
    }

    // ── URL tuyệt đối cho canonical / og:url / og:image ──────────────────────────

    /// <summary>
    /// Facebook, Zalo và Google BỎ QUA canonical/og:url/og:image dạng tương đối, nên
    /// <c>href="/tin-tuc/abc"</c> tương đương không khai gì: link chia sẻ mất ảnh, mất tiêu đề,
    /// và canonical không hợp nhất được URL trùng nội dung.
    ///
    /// Site có PrimaryDomain thì phải ghép thành URL đầy đủ — đúng cách
    /// <c>SitemapGenerator</c> vẫn làm (SitemapGenerator.cs:42).
    /// </summary>
    [Fact]
    public async Task Canonical_va_og_url_phai_la_url_tuyet_doi()
    {
        using var fx = new BuilderRenderFixture("seo-abs");
        fx.SetPrimaryDomain("chukafe.vn");

        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var media = new Media
        {
            SiteId = Guid.Empty, FilePath = "/uploads/coffee.jpg",
            FileName = "coffee.jpg", MimeType = "image/jpeg", StorageKey = "coffee.jpg"
        };
        fx.Db.Medias.Add(media);
        fx.Db.SaveChanges();

        var post = fx.AddPost("Cold brew hạt Cầu Đất", "cold-brew", cat.Id);
        post.FeaturedImageId = media.Id;
        fx.Db.SaveChanges();
        fx.AddRoute("/tin-tuc/cold-brew", RouteType.Post, post.Id);

        var rendered = await fx.NewPageRenderer().RenderPostAsync(post.Id, "vi", "/tin-tuc/cold-brew");
        var html = WebUtility.HtmlDecode(rendered!.Html);

        Assert.Contains("<link rel=\"canonical\" href=\"https://chukafe.vn/tin-tuc/cold-brew\">", html);
        Assert.Contains("content=\"https://chukafe.vn/tin-tuc/cold-brew\"", html);
        Assert.Contains("content=\"https://chukafe.vn/uploads/coffee.jpg\"", html);
        // Không còn thẻ nào mang URL tương đối.
        Assert.DoesNotContain("href=\"/tin-tuc/cold-brew\"", html);
    }

    /// <summary>
    /// Ảnh OG trỏ CDN (đã tuyệt đối, hoặc protocol-relative) không được ghép thêm domain của site
    /// — ghép vào là URL rác kiểu <c>https://site.vn/https://cdn…</c>.
    /// </summary>
    [Theory]
    [InlineData("https://cdn.example.com/a.jpg", "https://cdn.example.com/a.jpg")]
    [InlineData("//cdn.example.com/a.jpg", "//cdn.example.com/a.jpg")]
    public async Task OgImage_da_tuyet_doi_thi_giu_nguyen(string input, string expected)
    {
        using var fx = new BuilderRenderFixture("seo-abs");
        fx.SetPrimaryDomain("chukafe.vn");

        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết", "bai-viet", cat.Id);

        await fx.SeoMetas.SaveAsync(new SeoMetaSaveRequest(
            "post", post.Id, "vi", null, null, input, null, null, null, null, null, null, null));

        var rendered = await fx.NewPageRenderer().RenderPostAsync(post.Id, "vi", "/tin-tuc/bai-viet");
        var html = WebUtility.HtmlDecode(rendered!.Html);

        Assert.Contains($"<meta property=\"og:image\" content=\"{expected}\">", html);
        Assert.DoesNotContain($"https://chukafe.vn/{input}", html);
    }

    /// <summary>
    /// Site chưa khai PrimaryDomain (và render ngoài pipeline HTTP, vd job/preview MCP) thì giữ
    /// path tương đối: thẻ tương đối vẫn hơn thẻ rỗng, và không được ghép ra
    /// <c>https:///tin-tuc/…</c>.
    /// </summary>
    /// <summary>
    /// Đúng trạng thái của chu-kafe trên DB thật: <c>Sites.PrimaryDomain</c> để trống nhưng
    /// <c>SiteDomains</c> có <c>tourimate.site</c> với <c>IsPrimary = 1</c>. Trước khi có
    /// <c>ISiteUrlResolver</c>, sitemap phát <c>https://localhost/...</c> và canonical rơi về host
    /// request — cả hai đều bỏ qua domain thật đang nằm sẵn trong DB.
    /// </summary>
    [Fact]
    public async Task PrimaryDomain_trong_thi_lay_host_tu_SiteDomains()
    {
        using var fx = new BuilderRenderFixture("seo-abs");
        fx.AddSiteDomain("tourimate.site");

        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết", "bai-viet", cat.Id);

        var rendered = await fx.NewPageRenderer().RenderPostAsync(post.Id, "vi", "/tin-tuc/bai-viet");
        var html = WebUtility.HtmlDecode(rendered!.Html);

        Assert.Contains("<link rel=\"canonical\" href=\"https://tourimate.site/tin-tuc/bai-viet\">", html);
    }

    /// <summary>
    /// Site nhiều domain: chỉ host <c>IsPrimary</c> được dùng làm canonical. Lấy sai host thì mỗi
    /// domain tự khai mình là bản chính và thẻ canonical mất tác dụng hợp nhất URL trùng nội dung.
    /// </summary>
    [Fact]
    public async Task Nhieu_domain_thi_chi_lay_cai_IsPrimary()
    {
        using var fx = new BuilderRenderFixture("seo-abs");
        fx.AddSiteDomain("localhost", isPrimary: false);
        fx.AddSiteDomain("chukafe.vn", isPrimary: true);

        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết", "bai-viet", cat.Id);

        var rendered = await fx.NewPageRenderer().RenderPostAsync(post.Id, "vi", "/tin-tuc/bai-viet");
        var html = WebUtility.HtmlDecode(rendered!.Html);

        Assert.Contains("https://chukafe.vn/tin-tuc/bai-viet", html);
        Assert.DoesNotContain("localhost", html);
    }

    /// <summary>
    /// <c>Sites.PrimaryDomain</c> là trường admin khai tay nên phải THẮNG bảng SiteDomains —
    /// ngược lại thì sửa domain chính trong admin không có tác dụng.
    /// </summary>
    [Fact]
    public async Task PrimaryDomain_thang_SiteDomains()
    {
        using var fx = new BuilderRenderFixture("seo-abs");
        fx.AddSiteDomain("cu.example.com");
        fx.SetPrimaryDomain("moi.example.com");

        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết", "bai-viet", cat.Id);

        var rendered = await fx.NewPageRenderer().RenderPostAsync(post.Id, "vi", "/tin-tuc/bai-viet");
        var html = WebUtility.HtmlDecode(rendered!.Html);

        Assert.Contains("https://moi.example.com/tin-tuc/bai-viet", html);
        Assert.DoesNotContain("cu.example.com", html);
    }

    [Fact]
    public async Task Khong_co_PrimaryDomain_thi_giu_path_tuong_doi()
    {
        using var fx = new BuilderRenderFixture("seo-abs");

        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết", "bai-viet", cat.Id);

        var rendered = await fx.NewPageRenderer().RenderPostAsync(post.Id, "vi", "/tin-tuc/bai-viet");
        var html = WebUtility.HtmlDecode(rendered!.Html);

        Assert.Contains("<link rel=\"canonical\" href=\"/tin-tuc/bai-viet\">", html);
        Assert.DoesNotContain("https:///", html);
    }
}
