using NewsCMS.Application.Builder;
using NewsCMS.Infrastructure.Builder.Blocks;

namespace NewsCMS.Tests;

/// <summary>
/// Khối "Nhúng Video": tự nhận diện YouTube/Vimeo/link trực tiếp từ một URL, KHÔNG nhận HTML từ
/// người dùng (khác "Nhúng HTML" — xem <see cref="VideoEmbedBlock"/>).
/// </summary>
public sealed class VideoEmbedBlockTests
{
    private static VideoEmbedBlock NewBlock() => new();

    [Fact]
    public async Task RenderAsync_NoUrl_ShowsPlaceholder()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi", "{}"));
        Assert.Contains("Dán link", html);
        Assert.DoesNotContain("<iframe", html);
    }

    [Fact]
    public async Task RenderAsync_YoutubeWatchUrl_BuildsNoCookieEmbed()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=abc\"}"));
        Assert.Contains("youtube-nocookie.com/embed/dQw4w9WgXcQ", html);
    }

    [Fact]
    public async Task RenderAsync_YoutuBeShortUrl_ParsesId()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://youtu.be/dQw4w9WgXcQ\"}"));
        Assert.Contains("youtube-nocookie.com/embed/dQw4w9WgXcQ", html);
    }

    [Fact]
    public async Task RenderAsync_VimeoUrl_BuildsPlayerEmbed()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://vimeo.com/76979871\"}"));
        Assert.Contains("player.vimeo.com/video/76979871", html);
    }

    [Fact]
    public async Task RenderAsync_DirectMp4Url_RendersNativeVideoTag()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://cdn.example.com/clip.mp4\"}"));
        Assert.Contains("<video", html);
        Assert.Contains("src=\"https://cdn.example.com/clip.mp4\"", html);
    }

    [Fact]
    public async Task RenderAsync_UnsupportedUrl_ShowsWarning_NotIframeOrVideo()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://example.com/some-page\"}"));
        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("<video", html);
        Assert.Contains("Không nhận diện", html);
    }

    [Fact]
    public async Task RenderAsync_LookalikeDomain_IsNotTreatedAsYoutube()
    {
        // "evilyoutube.com" không có ranh giới từ trước "youtube" nên KHÔNG được khớp là YouTube.
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://evilyoutube.com/watch?v=dQw4w9WgXcQ\"}"));
        Assert.DoesNotContain("youtube-nocookie.com", html);
    }

    [Fact]
    public async Task RenderAsync_AutoplayChecked_AddsMuteParams()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://www.youtube.com/watch?v=dQw4w9WgXcQ\",\"autoplay\":true}"));
        Assert.Contains("autoplay=1", html);
        Assert.Contains("mute=1", html);
    }

    [Fact]
    public async Task RenderAsync_LoopChecked_AddsPlaylistParamForYoutube()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://www.youtube.com/watch?v=dQw4w9WgXcQ\",\"loop\":true}"));
        Assert.Contains("loop=1", html);
        Assert.Contains("playlist=dQw4w9WgXcQ", html);
    }

    [Fact]
    public async Task RenderAsync_VerticalRatio_SetsPaddingTop()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://vimeo.com/76979871\",\"ratio\":\"9-16\"}"));
        Assert.Contains("padding-top:177.78%", html);
    }

    [Fact]
    public async Task RenderAsync_Caption_IsHtmlEncoded()
    {
        var html = await NewBlock().RenderAsync(new DynamicBlockContext(Guid.Empty, "vi",
            "{\"url\":\"https://vimeo.com/76979871\",\"caption\":\"<script>alert(1)</script>\"}"));
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
