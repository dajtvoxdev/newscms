using AdVideo.Core.Enums;
using FluentAssertions;

namespace AdVideo.Core.Tests.Enums;

/// <summary>
/// Tỉ lệ khung hình → chuỗi gửi provider và độ phân giải để QC đối chiếu.
/// </summary>
/// <remarks>
/// Hai giá trị này phải khớp nhau: bước 6 gửi <c>ToProviderString</c> lên provider, bước 9 so
/// video nhận về với <c>ToResolution</c>. Lệch nhau thì mọi video đều báo "sai độ phân giải"
/// dù chẳng có gì sai.
/// </remarks>
public class AspectRatioExtensionsTests
{
    [Theory]
    [InlineData(AspectRatio.Portrait9x16, "9:16")]
    [InlineData(AspectRatio.Square1x1, "1:1")]
    [InlineData(AspectRatio.Landscape16x9, "16:9")]
    public void Chuoi_gui_provider_dung_dang_provider_hieu(AspectRatio ratio, string expected)
    {
        ratio.ToProviderString().Should().Be(expected);
    }

    [Theory]
    [InlineData(AspectRatio.Portrait9x16, 1080, 1920)]
    [InlineData(AspectRatio.Square1x1, 1920, 1920)]
    [InlineData(AspectRatio.Landscape16x9, 1920, 1080)]
    public void Do_phan_giai_mac_dinh_lay_canh_dai_1920(AspectRatio ratio, int width, int height)
    {
        ratio.ToResolution().Should().Be((width, height));
    }

    [Theory]
    [InlineData(AspectRatio.Portrait9x16, 720, 405, 720)]
    [InlineData(AspectRatio.Landscape16x9, 1280, 1280, 720)]
    public void Do_phan_giai_doi_theo_canh_dai_truyen_vao(AspectRatio ratio, int longSide, int width, int height)
    {
        ratio.ToResolution(longSide).Should().Be((width, height));
    }

    [Fact]
    public void Doc_la_mac_dinh_vi_khach_hang_muc_tieu_dang_mang_xa_hoi()
    {
        // Giá trị 0 của enum là thứ mọi record chưa gán rơi vào. Để mặc định là 16:9 nghĩa là
        // một brief thiếu trường tỉ lệ sẽ lặng lẽ ra video ngang cho một khách đăng TikTok.
        default(AspectRatio).Should().Be(AspectRatio.Portrait9x16);
    }

    [Fact]
    public void Ti_le_chua_ho_tro_thi_nem_chu_khong_doan_bua()
    {
        // Không có giá trị mặc định nào đúng ở đây: đoán sai tỉ lệ nghĩa là tiêu tiền render ra
        // một video có khung hình khách không dùng được.
        ((Action)(() => ((AspectRatio)42).ToProviderString()))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => ((AspectRatio)42).ToResolution()))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(AspectRatio.Portrait9x16)]
    [InlineData(AspectRatio.Square1x1)]
    [InlineData(AspectRatio.Landscape16x9)]
    public void Chuoi_gui_provider_va_do_phan_giai_noi_cung_mot_ti_le(AspectRatio ratio)
    {
        var (width, height) = ratio.ToResolution();
        string[] parts = ratio.ToProviderString().Split(':');

        // So bằng tích chéo để không phải so số thực: w/h == a/b ⟺ w*b == h*a.
        (width * int.Parse(parts[1])).Should().Be(height * int.Parse(parts[0]),
            "bước 6 gửi chuỗi này còn bước 9 đo theo độ phân giải kia — hai bên phải nói cùng một tỉ lệ");
    }

    [Theory]
    [InlineData(AspectRatio.Portrait9x16)]
    [InlineData(AspectRatio.Square1x1)]
    [InlineData(AspectRatio.Landscape16x9)]
    public void Moi_ti_le_deu_cho_kich_thuoc_chan(AspectRatio ratio)
    {
        // H.264 với yuv420p không mã hoá được chiều lẻ: ffmpeg fail với "width not divisible by 2"
        // ở tận bước compose, sau khi đã trả tiền cho toàn bộ shot.
        var (width, height) = ratio.ToResolution();

        (width % 2).Should().Be(0);
        (height % 2).Should().Be(0);
    }
}
