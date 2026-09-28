using AdVideo.Core.Media;
using FluentAssertions;
using Xunit;

namespace AdVideo.Core.Tests.Media;

public sealed class ProductImageFormatTests
{
    [Fact]
    public void Nhan_ra_JPEG_PNG_WebP_tu_noi_dung()
    {
        ProductImageFormat.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0, 0])!.ContentType.Should().Be("image/jpeg");
        ProductImageFormat.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0])!.Extension.Should().Be(".png");
        ProductImageFormat.Detect("RIFF\0\0\0\0WEBPVP8 "u8)!.ContentType.Should().Be("image/webp");
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script/></svg>")]
    [InlineData("GIF89a......")]
    [InlineData("RIFF....WAVEfmt ")] // RIFF nhưng là audio
    [InlineData("")]
    public void Tu_choi_moi_thu_khac(string content)
    {
        ProductImageFormat.Detect(System.Text.Encoding.ASCII.GetBytes(content)).Should().BeNull();
    }
}
