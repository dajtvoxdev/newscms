using AdVideo.Core.Enums;
using AdVideo.Core.Media;
using FluentAssertions;

namespace AdVideo.Core.Tests.Media;

/// <summary>
/// D9 — nhãn AI không có cờ tắt.
/// </summary>
/// <remarks>
/// Luật TTNT 2025 đặt nghĩa vụ gắn nhãn lên bên triển khai, phạt tới 2 tỉ đồng, và nghĩa vụ đó
/// không chuyển sang khách bằng điều khoản được. Test ở đây vì vậy không chỉ kiểm tính toán mà
/// còn <b>khoá thiết kế</b>: nếu có ai thêm một tham số cho phép bỏ nhãn thì test phải đỏ.
/// </remarks>
public class AiLabelStamperTests
{
    [Fact]
    public void Khong_co_cach_nao_goi_Build_ma_khong_gan_nhan()
    {
        // Khoá thiết kế: mọi overload của Build đều phải trả về một đặc tả nhãn hợp lệ, và
        // không overload nào được nhận cờ bật/tắt.
        var overloads = typeof(AiLabelStamper).GetMethods()
            .Where(m => m.Name == nameof(AiLabelStamper.Build))
            .ToList();

        overloads.Should().NotBeEmpty();
        overloads.SelectMany(m => m.GetParameters())
            .Should().NotContain(
                p => p.ParameterType == typeof(bool),
                "một cờ bool trong Build là một cách để ai đó vô tình bỏ nhãn");

        foreach (var overload in overloads)
        {
            overload.ReturnType.Should().Be(typeof(AiLabelSpec));
        }
    }

    [Fact]
    public void Nhan_mac_dinh_hien_suot_video_o_goc_it_bi_UI_nen_tang_che()
    {
        var spec = AiLabelStamper.Build(AspectRatio.Portrait9x16, videoDurationSeconds: 15);

        spec.OverlayText.Should().Be(AiLabelStamper.DefaultOverlayText);
        spec.Position.Should().Be(
            LabelPosition.TopLeft, "TikTok/Reels/Shorts đặt nút tương tác ở cạnh phải và góc dưới phải");
        spec.StartSeconds.Should().Be(0);
        spec.DurationSeconds.Should().Be(0);
        spec.IsPermanent.Should().BeTrue("người xem tua tới giữa video vẫn phải thấy nhãn");
        spec.Opacity.Should().BeGreaterThanOrEqualTo(AiLabelStamper.MinOpacity);
        spec.Metadata.Should().BeSameAs(AiLabelStamper.DefaultMetadata);
    }

    [Theory]
    [InlineData(AspectRatio.Portrait9x16, 1080)]
    [InlineData(AspectRatio.Square1x1, 1920)]
    [InlineData(AspectRatio.Landscape16x9, 1920)]
    public void Co_chu_tinh_theo_chieu_rong_khung_hinh(AspectRatio ratio, int expectedWidth)
    {
        var spec = AiLabelStamper.Build(ratio, videoDurationSeconds: 10);

        spec.FontSizePx.Should().Be((int)Math.Round(expectedWidth * 0.032));
        spec.FontSizePx.Should().BeGreaterThanOrEqualTo(AiLabelStamper.MinReadableFontPx);
    }

    [Fact]
    public void Khung_hinh_hep_nhat_van_cho_chu_doc_duoc_tren_dien_thoai()
    {
        // Dọc 9:16 là khung hẹp nhất được hỗ trợ (1080 px ngang) và cũng là khung hay dùng nhất.
        // 35px ở đây sát ngưỡng 34px — nghĩa là không còn chỗ để hạ cỡ chữ "cho đỡ vướng".
        var spec = AiLabelStamper.Build(AspectRatio.Portrait9x16, videoDurationSeconds: 10);

        spec.FontSizePx.Should().Be(35);
        spec.FontSizePx.Should().BeGreaterThanOrEqualTo(AiLabelStamper.MinReadableFontPx);
    }

    [Fact]
    public void Video_chua_do_duoc_thoi_luong_thi_khong_gan_nhan()
    {
        Action act = () => AiLabelStamper.Build(AspectRatio.Portrait9x16, videoDurationSeconds: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Noi_dung_rong_thi_quay_ve_mac_dinh_chu_khong_de_nhan_trong(string? text)
    {
        AiLabelStamper.Build(AspectRatio.Portrait9x16, 10, text).OverlayText
            .Should().Be(AiLabelStamper.DefaultOverlayText);
    }

    [Fact]
    public void Noi_dung_tu_cau_hinh_duoc_cat_khoang_trang_thua()
    {
        AiLabelStamper.Build(AspectRatio.Portrait9x16, 10, "  Video do AI tạo  ").OverlayText
            .Should().Be("Video do AI tạo");
    }

    [Fact]
    public void Metadata_rieng_duoc_gop_vao_metadata_mac_dinh()
    {
        var spec = AiLabelStamper.Build(
            AspectRatio.Portrait9x16,
            10,
            metadata: new Dictionary<string, string> { ["AI-Generator"] = "AdVideo v2", ["artist"] = "CHU Kafe" });

        spec.Metadata["AI-Generator"].Should().Be("AdVideo v2", "trường trùng tên thì bản riêng thắng");
        spec.Metadata["artist"].Should().Be("CHU Kafe");
        spec.Metadata["comment"].Should().Be("AI-generated content", "trường mặc định không được mất khi gộp");
        AiLabelStamper.DefaultMetadata["AI-Generator"].Should().Be("AdVideo", "bản mặc định dùng chung không được sửa");
    }

    [Fact]
    public void Metadata_rong_thi_dung_thang_ban_mac_dinh()
    {
        AiLabelStamper.Build(AspectRatio.Portrait9x16, 10, metadata: new Dictionary<string, string>())
            .Metadata.Should().BeSameAs(AiLabelStamper.DefaultMetadata);
    }

    [Fact]
    public void Ten_truong_metadata_khong_phan_biet_hoa_thuong()
    {
        AiLabelStamper.DefaultMetadata.ContainsKey("ai-generated").Should().BeTrue();
    }

    [Fact]
    public void Thieu_han_dac_ta_nhan_la_loi_nang_nhat_va_noi_ro_khong_co_ngoai_le()
    {
        AiLabelStamper.Validate(null).Should().ContainSingle()
            .Which.Should().Contain("không có ngoại lệ và không tắt được");
    }

    [Fact]
    public void Nhan_dung_chuan_thi_khong_co_van_de_nao()
    {
        AiLabelStamper.Validate(AiLabelStamper.Build(AspectRatio.Portrait9x16, 12))
            .Should().BeEmpty();
    }

    [Fact]
    public void Moi_kieu_nhan_hong_deu_duoc_goi_ten_rieng()
    {
        var broken = new AiLabelSpec(
            OverlayText: "   ",
            Position: LabelPosition.BottomRight,
            FontSizePx: 12,
            StartSeconds: 0,
            DurationSeconds: 3,
            Opacity: 0.4,
            Metadata: new Dictionary<string, string>());

        var problems = AiLabelStamper.Validate(broken);

        problems.Should().HaveCount(5);
        problems.Should().Contain(p => p.Contains("nội dung rỗng"));
        problems.Should().Contain(p => p.Contains("nhỏ hơn mức đọc được"));
        problems.Should().Contain(p => p.Contains("Độ mờ"));
        problems.Should().Contain(p => p.Contains("chỉ hiện một phần video"));
        problems.Should().Contain(p => p.Contains("Thiếu metadata"));
    }

    [Fact]
    public void Co_metadata_nhung_khong_truong_nao_danh_dau_AI_van_la_thieu_nhan()
    {
        var spec = AiLabelStamper.Build(AspectRatio.Portrait9x16, 10) with
        {
            Metadata = new Dictionary<string, string> { ["artist"] = "CHU Kafe" },
        };

        AiLabelStamper.Validate(spec).Should().ContainSingle()
            .Which.Should().Contain("không có trường nào đánh dấu nội dung AI");
    }

    [Fact]
    public void Truong_metadata_danh_dau_AI_co_the_nam_o_gia_tri_chu_khong_chi_o_ten()
    {
        var spec = AiLabelStamper.Build(AspectRatio.Portrait9x16, 10) with
        {
            Metadata = new Dictionary<string, string> { ["comment"] = "AI-generated content" },
        };

        AiLabelStamper.Validate(spec).Should().BeEmpty();
    }
}
