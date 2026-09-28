using AdVideo.Core.Media;
using FluentAssertions;
using Xunit;

namespace AdVideo.Core.Tests.Media;

public sealed class VoiceSampleFormatTests
{
    [Theory]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 }, ".wav")]
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 4, 0, 0, 0, 0, 0, 0, 0, 0 }, ".mp3")]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, ".mp3")]
    [InlineData(new byte[] { 0x4F, 0x67, 0x67, 0x53, 0, 0, 0, 0, 0, 0, 0, 0 }, ".ogg")]
    [InlineData(new byte[] { 0, 0, 0, 0x20, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x34, 0x41, 0x20 }, ".m4a")]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0, 0, 0 }, ".webm")]
    [InlineData(new byte[] { 0x66, 0x4C, 0x61, 0x43, 0, 0, 0, 0, 0, 0, 0, 0 }, ".flac")]
    public void Nhan_ra_dinh_dang_ghi_am_pho_bien(byte[] header, string extension)
    {
        VoiceSampleFormat.Detect(header)!.Value.Extension.Should().Be(extension);
    }

    [Theory]
    [InlineData("<html>khong phai audio</html>")]
    [InlineData("RIFF....WEBPVP8 ")] // RIFF nhưng là ảnh
    [InlineData("ngan")]
    public void Tu_choi_thu_khong_phai_ghi_am(string content)
    {
        VoiceSampleFormat.Detect(System.Text.Encoding.ASCII.GetBytes(content)).Should().BeNull();
    }
}
