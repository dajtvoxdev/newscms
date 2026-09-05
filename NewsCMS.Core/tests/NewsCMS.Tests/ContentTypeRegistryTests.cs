using System.Net;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Builder.Blocks;
using NewsCMS.Infrastructure.Builder.ContentTypes;
using Xunit;

namespace NewsCMS.Tests;

/// <summary>
/// Bộ kiểm thử cho Phase 2.5: Registry loại nội dung (Content Type Registry),
/// các ContentType triển khai cụ thể, scoped cache per-request và SupportedTypes trên BlockDescriptor.
/// </summary>
public sealed class ContentTypeRegistryTests
{
    // ── 1. Registry Lookup & Mapping ──────────────────────────────────────────────

    [Fact]
    public void Registry_TraCuuTheoKey_RouteType_TemplateKind_ChinhXac()
    {
        using var fx = new BuilderRenderFixture("ctr-lookup");
        var registry = fx.ContentTypes;

        Assert.Equal(3, registry.All.Count);

        // Key tra cứu không phân biệt hoa thường
        var postByKey = registry.FindByKey("POST");
        Assert.NotNull(postByKey);
        Assert.Equal(RouteType.Post, postByKey.RouteType);
        Assert.Equal(PageKind.PostTemplate, postByKey.TemplateKind);

        var prodByRoute = registry.FindByRouteType(RouteType.Product);
        Assert.NotNull(prodByRoute);
        Assert.Equal("product", prodByRoute.Key);

        var catByKind = registry.FindByTemplateKind(PageKind.CategoryTemplate);
        Assert.NotNull(catByKind);
        Assert.Equal(RouteType.Category, catByKind.RouteType);

        Assert.Null(registry.FindByKey("unknown"));
        Assert.Null(registry.FindByRouteType(RouteType.Page));
    }

    // ── 2. PostContentType ───────────────────────────────────────────────────────

    [Fact]
    public async Task PostContentType_LoadAsync_TraVeDuLieuChuanHoa()
    {
        using var fx = new BuilderRenderFixture("ctr-post");
        var cat = fx.AddCategory("Cà phê Specialty", "ca-phe-specialty");
        var post = fx.AddPost("Hạt Arabica Cầu Đất", "hat-arabica-cau-dat", cat.Id);
        post.Excerpt = "Sapo bài viết cà phê";
        post.ViewCount = 350;

        var media = new Media
        {
            SiteId = Guid.Empty,
            FilePath = "/media/arabica.jpg",
            FileName = "arabica.jpg",
            MimeType = "image/jpeg",
            StorageKey = "arabica.jpg",
            AltText = "Ảnh hạt Arabica"
        };
        fx.Db.Medias.Add(media);
        post.FeaturedImageId = media.Id;
        post.FeaturedImage = media;
        fx.Db.SaveChanges();

        var postType = new PostContentType(fx.Db);
        var detail = await postType.LoadAsync(post.Id);

        Assert.NotNull(detail);
        Assert.Equal(post.Id, detail.Id);
        Assert.Equal("hat-arabica-cau-dat", detail.Slug);
        Assert.Equal("Hạt Arabica Cầu Đất", detail.Title);
        Assert.Equal("Sapo bài viết cà phê", detail.Excerpt);
        Assert.Equal("/media/arabica.jpg", detail.ImageUrl);
        Assert.Equal("Ảnh hạt Arabica", detail.ImageAlt);
        Assert.Equal(cat.Id, detail.CategoryId);
        Assert.Equal("ca-phe-specialty", detail.CategorySlug);
        Assert.Equal("Cà phê Specialty", detail.CategoryName);
        Assert.Equal((long)350, detail.Extra["ViewCount"]);

        // BuildPathAsync
        var path = await postType.BuildPathAsync(post.Id);
        Assert.Equal("/ca-phe-specialty/hat-arabica-cau-dat", path);

        // RenderFallbackHtmlAsync
        var fallbackHtml = await postType.RenderFallbackHtmlAsync(detail);
        Assert.NotNull(fallbackHtml);
        var decoded = WebUtility.HtmlDecode(fallbackHtml);
        Assert.Contains("Hạt Arabica Cầu Đất", decoded);
        Assert.Contains("Sapo bài viết cà phê", decoded);
    }

    [Fact]
    public async Task PostContentType_BaiChuaPublish_HoacDaXoa_TraVeNull()
    {
        using var fx = new BuilderRenderFixture("ctr-post-draft");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var draftPost = fx.AddPost("Bài nháp", "bai-nhap", cat.Id);
        draftPost.Status = PostStatus.Draft;
        var deletedPost = fx.AddPost("Bài đã xóa", "bai-da-xoa", cat.Id);
        deletedPost.IsDeleted = true;
        fx.Db.SaveChanges();

        var postType = new PostContentType(fx.Db);

        Assert.Null(await postType.LoadAsync(draftPost.Id));
        Assert.Null(await postType.LoadAsync(deletedPost.Id));
    }

    // ── 3. ProductContentType ────────────────────────────────────────────────────

    [Fact]
    public async Task ProductContentType_LoadAsync_TraVeGiaVaTonKho()
    {
        using var fx = new BuilderRenderFixture("ctr-prod");
        var cat = fx.AddProductCategory("Thiết bị pha chế", "thiet-bi-pha-che");

        var media = new Media
        {
            SiteId = Guid.Empty,
            FilePath = "/media/v60.jpg",
            FileName = "v60.jpg",
            MimeType = "image/jpeg",
            StorageKey = "v60.jpg",
            AltText = "Phễu pha V60"
        };
        fx.Db.Medias.Add(media);

        var product = new Product
        {
            SiteId = Guid.Empty,
            Name = "Phễu V60 Ceramic",
            Slug = "pheu-v60-ceramic",
            Sku = "V60-CER-01",
            ShortDescription = "Phễu sứ pha thủ công",
            Description = "<p>Chi tiết phễu sứ chịu nhiệt cao cấp</p>",
            Price = 450_000m,
            SalePrice = 390_000m,
            StockQuantity = 25,
            IsTrackingStock = true,
            ProductCategoryId = cat.Id,
            Status = ProductStatus.Published,
            PublishedAt = DateTime.UtcNow,
            ThumbnailMediaId = media.Id
        };
        fx.Db.Products.Add(product);
        fx.Db.SaveChanges();

        var prodType = new ProductContentType(fx.Db);
        var detail = await prodType.LoadAsync(product.Id);

        Assert.NotNull(detail);
        Assert.Equal("Phễu V60 Ceramic", detail.Title);
        Assert.Equal("pheu-v60-ceramic", detail.Slug);
        Assert.Equal(450_000m, detail.Extra["Price"]);
        Assert.Equal(390_000m, detail.Extra["SalePrice"]);
        Assert.Equal(25, detail.Extra["StockQuantity"]);
        Assert.Equal(true, detail.Extra["IsTrackingStock"]);
        Assert.Equal("V60-CER-01", detail.Extra["Sku"]);

        // BuildPathAsync
        var path = await prodType.BuildPathAsync(product.Id);
        Assert.Equal("/san-pham/pheu-v60-ceramic", path);

        // RenderFallbackHtmlAsync
        var fallbackHtml = await prodType.RenderFallbackHtmlAsync(detail);
        Assert.NotNull(fallbackHtml);
        var decoded = WebUtility.HtmlDecode(fallbackHtml);
        Assert.Contains("Phễu V60 Ceramic", decoded);
        Assert.Contains("390.000 ₫", decoded);
        Assert.Contains("450.000 ₫", decoded);
    }

    // ── 4. CategoryContentType ───────────────────────────────────────────────────

    [Fact]
    public async Task CategoryContentType_LoadAsync_TraVePathSlugVaTemplatePageId()
    {
        using var fx = new BuilderRenderFixture("ctr-cat");
        var tplId = Guid.NewGuid();
        var cat = fx.AddCategory("Cà phê hạt", "ca-phe-hat");
        cat.PathSlug = "menu/ca-phe-hat";
        cat.TemplatePageId = tplId;
        fx.Db.SaveChanges();

        var catType = new CategoryContentType(fx.Db);
        var detail = await catType.LoadAsync(cat.Id);

        Assert.NotNull(detail);
        Assert.Equal("Cà phê hạt", detail.Title);
        Assert.Equal(tplId, detail.Extra["TemplatePageId"]);
        Assert.Equal("menu/ca-phe-hat", detail.Extra["PathSlug"]);

        // BuildPathAsync
        var path = await catType.BuildPathAsync(cat.Id);
        Assert.Equal("/menu/ca-phe-hat", path);

        // Category không có fallback HTML (trả 404 nếu thiếu template)
        Assert.Null(await catType.RenderFallbackHtmlAsync(detail));
    }

    // ── 5. Scoped Request Caching ────────────────────────────────────────────────

    [Fact]
    public async Task ContentTypeRegistry_LoadDetailAsync_CacheKetQuaTrongVongDoiRequest()
    {
        using var fx = new BuilderRenderFixture("ctr-cache");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết tối ưu cache", "bai-viet-toi-uu-cache", cat.Id);

        var registry = fx.ContentTypes;

        // Gọi lần 1: truy vấn DB và ghi cache
        var detail1 = await registry.LoadDetailAsync(RouteType.Post, post.Id);
        Assert.NotNull(detail1);

        // Gọi lần 2: phải trả về đúng reference đã cache
        var detail2 = await registry.LoadDetailAsync(RouteType.Post, post.Id);
        Assert.Same(detail1, detail2);
    }

    // ── 6. SupportedTypes on Block Descriptors ────────────────────────────────────

    [Fact]
    public void BlockDescriptors_SupportedTypes_KhaiBaoChinhXac()
    {
        using var fx = new BuilderRenderFixture("ctr-supported");

        // Các khối chung cho mọi loại thực thể: SupportedTypes == null
        var titleBlock = new EntityTitleBlock(fx.ContentTypes);
        Assert.Null(titleBlock.Descriptor.SupportedTypes);

        var contentBlock = new EntityContentBlock(fx.ContentTypes);
        Assert.Null(contentBlock.Descriptor.SupportedTypes);

        var imageBlock = new EntityImageBlock(fx.ContentTypes);
        Assert.Null(imageBlock.Descriptor.SupportedTypes);

        // Các khối đặc thù cho sản phẩm: SupportedTypes = ["product"]
        var priceBlock = new ProductPriceBlock(fx.Db);
        Assert.NotNull(priceBlock.Descriptor.SupportedTypes);
        Assert.Contains("product", priceBlock.Descriptor.SupportedTypes);

        var galleryBlock = new ProductGalleryBlock(fx.Db);
        Assert.NotNull(galleryBlock.Descriptor.SupportedTypes);
        Assert.Contains("product", galleryBlock.Descriptor.SupportedTypes);

        var stockBlock = new ProductStockBlock(fx.Db);
        Assert.NotNull(stockBlock.Descriptor.SupportedTypes);
        Assert.Contains("product", stockBlock.Descriptor.SupportedTypes);

        // Khối bài viết liên quan: SupportedTypes = ["post"]
        var relatedBlock = new RelatedPostsBlock(fx.Db);
        Assert.NotNull(relatedBlock.Descriptor.SupportedTypes);
        Assert.Contains("post", relatedBlock.Descriptor.SupportedTypes);
    }

    /// <summary>
    /// Mọi giá trị trong <c>SupportedTypes</c> phải khớp <see cref="IContentType.Key"/> của một
    /// loại nội dung ĐANG tồn tại.
    ///
    /// Vì sao đáng một test riêng: palette lọc khối bằng cách so chuỗi này với key của content
    /// type. Gõ <c>"products"</c> thay <c>"product"</c> thì phép so không bao giờ đúng — khối biến
    /// mất khỏi palette ở MỌI template, im lặng, không lỗi. Cùng loại bẫy với
    /// <c>Category.TemplatePageId</c> (khai mà không ai đọc) đã gặp ở Phase 1.
    /// </summary>
    [Fact]
    public void SupportedTypes_phai_khop_key_cua_ContentType_dang_ton_tai()
    {
        using var fx = new BuilderRenderFixture("ctr-supported-keys");

        var knownKeys = fx.ContentTypes.All
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Quét mọi IDynamicBlock trong assembly thay vì liệt kê tay — khối mới tự động được kiểm.
        var blockTypes = typeof(EntityTitleBlock).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IDynamicBlock).IsAssignableFrom(t));

        var checkedAny = false;

        foreach (var type in blockTypes)
        {
            var block = TryCreate(type, fx);
            if (block is null) continue;

            var supported = block.Descriptor.SupportedTypes;
            if (supported is null || supported.Count == 0) continue;

            checkedAny = true;
            foreach (var key in supported)
            {
                Assert.True(
                    knownKeys.Contains(key),
                    $"Khối '{block.Key}' khai SupportedTypes '{key}' không khớp IContentType nào " +
                    $"(hiện có: {string.Join(", ", knownKeys)}). Khối này sẽ biến mất khỏi palette.");
            }
        }

        Assert.True(checkedAny, "Không kiểm được khối nào — test đã mất tác dụng, hãy xem lại TryCreate.");
    }

    /// <summary>
    /// Dựng khối bằng constructor một tham số mà fixture cấp được (AppDbContext hoặc
    /// IContentTypeRegistry). Khối cần dependency khác (vd IWebHostEnvironment) thì bỏ qua —
    /// chúng không khai SupportedTypes.
    /// </summary>
    private static IDynamicBlock? TryCreate(Type type, BuilderRenderFixture fx)
    {
        foreach (var ctor in type.GetConstructors())
        {
            var ps = ctor.GetParameters();
            if (ps.Length != 1) continue;

            object? arg = null;
            if (ps[0].ParameterType.IsAssignableFrom(typeof(NewsCMS.Infrastructure.Persistence.AppDbContext)))
                arg = fx.Db;
            else if (ps[0].ParameterType.IsAssignableFrom(fx.ContentTypes.GetType()))
                arg = fx.ContentTypes;

            if (arg is null) continue;

            try
            {
                return (IDynamicBlock)ctor.Invoke([arg]);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    // ── 7. RenderEntityAsync & ForEntityAsync ─────────────────────────────────────

    [Fact]
    public async Task PageRenderer_RenderEntityAsync_RenderGenericChoPostVaProduct()
    {
        using var fx = new BuilderRenderFixture("ctr-render");
        var cat = fx.AddCategory("Tin tức", "tin-tuc");
        var post = fx.AddPost("Bài viết render generic", "bai-viet-render-generic", cat.Id);

        var renderer = fx.NewPageRenderer();

        // Render generic cho Post khi chưa có template (dùng fallback HTML của PostContentType)
        var postResult = await renderer.RenderEntityAsync(RouteType.Post, post.Id, "vi", "/tin-tuc/bai-viet-render-generic");
        Assert.NotNull(postResult);
        var postDecoded = WebUtility.HtmlDecode(postResult.Html);
        Assert.Contains("Bài viết render generic", postDecoded);

        // Tra cứu template generic qua ForEntityAsync
        var resolved = await fx.Templates.ForEntityAsync(RouteType.Post, post.Id, "/tin-tuc/bai-viet-render-generic", "vi");
        Assert.NotNull(resolved);
        Assert.Equal("/tin-tuc/bai-viet-render-generic", resolved.Route.Path);
        Assert.Equal(RouteType.Post, resolved.Route.RouteType);
    }

    // ── 8. SyncEntityRouteAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RouteRegistry_SyncEntityRouteAsync_DongBoRouteHopNhat()
    {
        using var fx = new BuilderRenderFixture("ctr-sync");
        var cat = fx.AddCategory("Sự kiện", "su-kien");
        var post = fx.AddPost("Triển lãm cà phê 2026", "trien-lam-ca-phe-2026", cat.Id);

        // Đồng bộ route hợp nhất qua SyncEntityRouteAsync
        await fx.Routes.SyncEntityRouteAsync(RouteType.Post, post.Id);

        var resolved = await fx.Routes.ResolveAsync("/su-kien/trien-lam-ca-phe-2026", "vi");
        Assert.NotNull(resolved);
        Assert.Equal(RouteType.Post, resolved.RouteType);
        Assert.Equal(post.Id, resolved.TargetId);
    }
}
