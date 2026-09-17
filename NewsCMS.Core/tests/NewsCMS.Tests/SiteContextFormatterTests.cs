using NewsCMS.Application.Ai;

namespace NewsCMS.Tests;

/// <summary>
/// Định dạng ngữ cảnh site gửi cho AI. Hỏng ở đây thì AI vẫn trả lời bình thường
/// nhưng viết sai về site — kiểu lỗi im lặng, nên phải khoá bằng test.
/// </summary>
public class SiteContextFormatterTests
{
    private static SiteContextSnapshot Snapshot(
        string? name = "Thế Giới Xe Điện Phú Phúc",
        string? description = "Thế giới xe điện Phú Phúc",
        IReadOnlyList<string>? categories = null,
        IReadOnlyList<SiteContextPost>? posts = null,
        IReadOnlyList<string>? products = null)
        => new(name, description,
            categories ?? new[] { "Xe điện", "Phụ kiện" },
            posts ?? Array.Empty<SiteContextPost>(),
            products ?? Array.Empty<string>());

    [Fact]
    public void Format_DayDuThongTin_CoTenSiteVaChuyenMuc()
    {
        var text = SiteContextFormatter.Format(Snapshot(products: new[] { "Xe đạp điện A (cho học sinh)" }));

        Assert.Contains("Thế Giới Xe Điện Phú Phúc", text);
        Assert.Contains("Thế giới xe điện Phú Phúc", text);
        Assert.Contains("Xe điện, Phụ kiện", text);
        Assert.Contains("Xe đạp điện A (cho học sinh)", text);
        Assert.Contains("--- HẾT NGỮ CẢNH ---", text);
    }

    [Fact]
    public void Format_RongHoanToan_TraChuoiRong()
    {
        // Chuỗi rỗng để service khỏi nối một khối ngữ cảnh trống vào prompt.
        var text = SiteContextFormatter.Format(new SiteContextSnapshot(
            null, null, Array.Empty<string>(), Array.Empty<SiteContextPost>(), Array.Empty<string>()));

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void Format_KhongCoBaiViet_KhongInTieuDeRong()
    {
        var text = SiteContextFormatter.Format(Snapshot());

        Assert.DoesNotContain("bài đã đăng gần đây", text);
    }

    [Fact]
    public void Format_CoBaiViet_InTieuDeVaTomTat()
    {
        var posts = new[] { new SiteContextPost("Xe đạp điện cho học sinh", "Tóm tắt bài viết.", "Trích đoạn thân bài.") };

        var text = SiteContextFormatter.Format(Snapshot(posts: posts));

        Assert.Contains("bài đã đăng gần đây", text);
        Assert.Contains("1. Xe đạp điện cho học sinh", text);
        Assert.Contains("Tóm tắt: Tóm tắt bài viết.", text);
        Assert.Contains("Trích đoạn: Trích đoạn thân bài.", text);
    }

    [Fact]
    public void Format_NhieuBai_DanhSoTangDan()
    {
        var posts = new[]
        {
            new SiteContextPost("Bài một", null, null),
            new SiteContextPost("Bài hai", null, null),
            new SiteContextPost("Bài ba", null, null)
        };

        var text = SiteContextFormatter.Format(Snapshot(posts: posts));

        Assert.Contains("1. Bài một", text);
        Assert.Contains("2. Bài hai", text);
        Assert.Contains("3. Bài ba", text);
    }

    [Fact]
    public void Format_CoCauChanKhongBiaSanPham()
    {
        // Chốt chống bịa: AI hay tự thêm sản phẩm/giá không có thật nếu không dặn.
        var text = SiteContextFormatter.Format(Snapshot());

        Assert.Contains("không bịa thêm sản phẩm", text);
    }

    [Fact]
    public void Truncate_ChuoiNgan_GiuNguyen()
    {
        Assert.Equal("ngắn gọn", SiteContextFormatter.Truncate("ngắn gọn", 50));
    }

    [Fact]
    public void Truncate_ChuoiDai_CatOWordBoundaryVaThemDauBaCham()
    {
        var text = SiteContextFormatter.Truncate("một hai ba bốn năm sáu bảy tám", 15);

        Assert.EndsWith("…", text);
        // Không được cắt giữa một từ.
        Assert.DoesNotContain("nă…", text);
        Assert.True(text.Length <= 16, $"dài quá: {text.Length}");
    }

    [Fact]
    public void Truncate_GopXuongDongVaKhoangTrangThua()
    {
        // Nội dung là HTML đã bỏ thẻ, còn sót xuống dòng — gộp lại để không phí
        // chỗ trong hạn mức ký tự.
        var text = SiteContextFormatter.Truncate("dòng một\n\n   dòng hai", 100);

        Assert.Equal("dòng một dòng hai", text);
    }

    [Fact]
    public void Truncate_ChuoiMotTuDaiKhongCoKhoangTrang_VanCatDuoc()
    {
        var text = SiteContextFormatter.Truncate(new string('a', 100), 10);

        Assert.Equal(new string('a', 10) + "…", text);
    }
}
