using AdVideo.Core.Providers;
using FluentAssertions;

namespace AdVideo.Core.Tests.Providers;

/// <summary>P2 — host được phép gọi nằm ở SystemSetting, không nằm trong descriptor.</summary>
public class ProviderHostAllowlistTests
{
    private static readonly ProviderHostAllowlist List = ProviderHostAllowlist.Parse(
        "queue.fal.run, *.fal.media; api.example.com:8443\nhttp://127.0.0.1:8080, ftp://files.x.com, https://x.com/path, *.1.2.3.4, http://u:p@h.com");

    [Theory]
    [InlineData("https://queue.fal.run/fal-ai/kling")]
    [InlineData("https://QUEUE.fal.run/x?y=1")]
    [InlineData("https://v3.fal.media/files/a.mp4")]
    [InlineData("https://a.b.fal.media/x")]
    [InlineData("https://api.example.com:8443/v1")]
    [InlineData("http://127.0.0.1:8080/tts")]
    public void Cho_phep(string url)
    {
        List.IsAllowed(new Uri(url)).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://queue.fal.run/x")]
    [InlineData("https://queue.fal.run:444/x")]
    [InlineData("https://fal.media/x")]
    [InlineData("https://evilfal.media/x")]
    [InlineData("https://api.example.com/v1")]
    [InlineData("https://127.0.0.1:8080/tts")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("https://queue.fal.run.evil.com/x")]
    [InlineData("https://queue.fal.run@evil.com/x")]
    public void Chan(string url)
    {
        List.Explain(new Uri(url)).Should().Contain("không nằm trong allowlist");
    }

    [Fact]
    public void Muc_hong_bi_bo_qua_va_bao_lai()
    {
        List.Count.Should().Be(4);
        List.InvalidEntries.Should().BeEquivalentTo("ftp://files.x.com", "https://x.com/path", "*.1.2.3.4", "http://u:p@h.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Danh_sach_rong_thi_chan_het(string? csv)
    {
        ProviderHostAllowlist.Parse(csv).IsAllowed(new Uri("https://api.elevenlabs.io")).Should().BeFalse();
    }

    [Theory]
    [InlineData("ftp://files.example.com")]
    [InlineData("h.com/path")]
    [InlineData("u:p@h.com")]
    public void Muc_khong_hop_le(string entry)
    {
        ProviderHostAllowlist.Parse(entry).InvalidEntries.Should().ContainSingle();
    }

    [Fact]
    public void URL_tuong_doi_bi_tu_choi()
    {
        List.Explain(new Uri("/x", UriKind.Relative)).Should().Contain("không tuyệt đối");
        FluentActions.Invoking(() => List.Explain(null!)).Should().Throw<ArgumentNullException>();
    }
}
