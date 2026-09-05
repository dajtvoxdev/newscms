using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;

namespace NewsCMS.Tests;

/// <summary>
/// Cache của DynamicBlockRenderer phải tách theo entity với khối đọc
/// <see cref="DynamicBlockContext.Route"/>.
///
/// Vì sao đây là test quan trọng nhất của tính năng trang chi tiết: MỘT template phục vụ MỌI bài
/// viết, và mọi bài đều dùng đúng props giống nhau. Nếu cache key chỉ gồm
/// (site, blockKey, culture, propsHash) thì bài thứ hai trở đi nhận lại HTML của bài đầu tiên —
/// lỗi không làm test nào khác đỏ, không ném exception, và chỉ lộ ra trên production sau khi
/// cache hết hạn 5 phút.
/// </summary>
public sealed class EntityBlockCacheTests
{
    private const string TemplateHtml = "<main><div data-nc-block=\"entity-title\"></div></main>";

    [Fact]
    public async Task Khoi_doc_entity_thi_hai_bai_khac_nhau_ra_hai_ket_qua()
    {
        using var fx = new BuilderRenderFixture("entitycache");
        var renderer = fx.NewBlockRenderer(new EntityTitleProbe());

        var first = await renderer.RenderAsync(
            TemplateHtml, Guid.Empty, "vi", RouteFor("bai-mot"));
        var second = await renderer.RenderAsync(
            TemplateHtml, Guid.Empty, "vi", RouteFor("bai-hai"));

        Assert.Contains("bai-mot", first);
        Assert.Contains("bai-hai", second);
        Assert.DoesNotContain("bai-mot", second);
    }

    [Fact]
    public async Task Cung_mot_entity_thi_van_dung_lai_cache()
    {
        using var fx = new BuilderRenderFixture("entitycache");
        var probe = new EntityTitleProbe();
        var renderer = fx.NewBlockRenderer(probe);

        var route = RouteFor("bai-mot");
        await renderer.RenderAsync(TemplateHtml, Guid.Empty, "vi", route);
        await renderer.RenderAsync(TemplateHtml, Guid.Empty, "vi", route);

        Assert.Equal(1, probe.Calls);
    }

    /// <summary>
    /// Mặt còn lại của cùng một quy tắc: khối KHÔNG đọc entity (post-list, site-menu… nằm chung
    /// trong template chi tiết) phải tiếp tục dùng chung một cache entry cho mọi URL. Nếu nhét
    /// EntityId vào key của mọi khối thì mỗi bài viết lại render lại toàn bộ danh sách bài liên
    /// quan — đúng kết quả nhưng đắt gấp N lần.
    /// </summary>
    [Fact]
    public async Task Khoi_khong_doc_entity_thi_dung_chung_cache_cho_moi_url()
    {
        using var fx = new BuilderRenderFixture("entitycache");
        var probe = new StaticProbe();
        var renderer = fx.NewBlockRenderer(probe);

        const string html = "<main><div data-nc-block=\"static-probe\"></div></main>";
        await renderer.RenderAsync(html, Guid.Empty, "vi", RouteFor("bai-mot"));
        await renderer.RenderAsync(html, Guid.Empty, "vi", RouteFor("bai-hai"));

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Khoi_doc_entity_o_trang_thuong_khong_no()
    {
        using var fx = new BuilderRenderFixture("entitycache");
        var renderer = fx.NewBlockRenderer(new EntityTitleProbe());

        // route null = trang landing thường. Khối phải tự xử lý, không được throw.
        var html = await renderer.RenderAsync(TemplateHtml, Guid.Empty, "vi", route: null);

        Assert.Contains("(khong co entity)", html);
    }

    private static RouteContext RouteFor(string slug) => new(
        RouteType.Post, DeterministicId(slug), slug, $"/tin-tuc/{slug}", null, "tin-tuc");

    /// <summary>Id ổn định theo slug để test đọc dễ hơn Guid ngẫu nhiên.</summary>
    private static Guid DeterministicId(string slug)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(slug));
        return new Guid(bytes);
    }

    /// <summary>Khối giả lập "Tiêu đề bài viết" của Phase 2: đọc entity, nên EntityScoped = true.</summary>
    private sealed class EntityTitleProbe : IDynamicBlock
    {
        public int Calls;

        public string Key => "entity-title";

        public BlockDescriptor Descriptor => new(
            Label: "Tiêu đề bài viết",
            Category: "Chi tiết",
            Description: "Probe cho test.",
            IconSvg: "<svg/>",
            Presets: [],
            Props: [],
            EntityScoped: true);

        public Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(context.Route is { } r ? $"<h1>{r.Slug}</h1>" : "<h1>(khong co entity)</h1>");
        }
    }

    /// <summary>Khối không đọc entity — giữ EntityScoped mặc định (false).</summary>
    private sealed class StaticProbe : IDynamicBlock
    {
        public int Calls;

        public string Key => "static-probe";

        public BlockDescriptor Descriptor => new(
            Label: "Khối tĩnh",
            Category: "Bố cục",
            Description: "Probe cho test.",
            IconSvg: "<svg/>",
            Presets: [],
            Props: []);

        public Task<string> RenderAsync(DynamicBlockContext context, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult("<p>tinh</p>");
        }
    }
}
