using AdVideo.Core.Enums;
using AdVideo.Core.Storage;
using FluentAssertions;

namespace AdVideo.Core.Tests.Storage;

/// <summary>
/// Chọn bucket và dựng object key.
/// </summary>
/// <remarks>
/// Hai lỗi mà bộ test này canh đều thuộc loại "không ai phát hiện trong nhiều tuần": ghi nhầm
/// bucket thì lifecycle rule không áp (file trung gian nằm lại mãi, hoặc video giao khách bị xoá
/// sau 7 ngày), và khoá object thiếu tenant thì hai khách ghi đè file của nhau.
/// </remarks>
public class BucketsTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Job = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Theory]
    [InlineData(AssetKind.ProductImage, Buckets.Uploads)]
    [InlineData(AssetKind.ReferenceImage, Buckets.Uploads)]
    [InlineData(AssetKind.VoiceAudio, Buckets.Voice)]
    [InlineData(AssetKind.FinalVideo, Buckets.Final)]
    [InlineData(AssetKind.Thumbnail, Buckets.Final)]
    [InlineData(AssetKind.Subtitle, Buckets.Final)]
    public void Moi_loai_asset_vao_dung_bucket(AssetKind kind, string expected)
    {
        Buckets.ForKind(kind).Should().Be(expected);
    }

    [Fact]
    public void Thumbnail_va_phu_de_di_theo_video_cuoi_chu_khong_nam_o_bucket_tam()
    {
        // Cả ba là thứ khách còn mở lại sau nhiều tháng. Nằm ở adv-work nghĩa là 7 ngày sau,
        // video vẫn còn mà ảnh đại diện thì mất.
        Buckets.ForKind(AssetKind.Thumbnail).Should().Be(Buckets.ForKind(AssetKind.FinalVideo));
        Buckets.ForKind(AssetKind.Subtitle).Should().Be(Buckets.ForKind(AssetKind.FinalVideo));
    }

    [Fact]
    public void Loai_asset_moi_mac_dinh_roi_vao_bucket_tam()
    {
        // Mặc định phải là bucket CÓ hạn xoá. Thêm một AssetKind mà quên map thì hậu quả xấu nhất
        // là file bị dọn sau 7 ngày — nhẹ hơn nhiều so với một bucket phình vô hạn không ai để ý.
        Buckets.ForKind((AssetKind)999).Should().Be(Buckets.Work);
    }

    [Fact]
    public void Moi_bucket_co_mot_ten_rieng()
    {
        var names = new[] { Buckets.Uploads, Buckets.Work, Buckets.Final, Buckets.Voice };

        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => n.StartsWith("adv-", StringComparison.Ordinal));
    }

    [Fact]
    public void Khoa_object_theo_quy_uoc_tenant_job_loai_ten_file()
    {
        string key = IStorageService.BuildKey(Tenant, Job, AssetKind.FinalVideo, "final.mp4");

        key.Should().Be(
            "aaaaaaaa000000000000000000000001/bbbbbbbb000000000000000000000002/finalvideo/final.mp4");
    }

    [Fact]
    public void Khoa_object_luon_bat_dau_bang_tenant()
    {
        // Tiền tố tenant là thứ khiến hai khách không ghi đè lên nhau, và cũng là thứ cho phép
        // tính dung lượng theo khách mà không cần bảng phụ.
        string key = IStorageService.BuildKey(Tenant, null, AssetKind.ProductImage, "anh.jpg");

        key.Should().StartWith(Tenant.ToString("N"));
    }

    [Fact]
    public void Asset_chua_gan_job_nam_o_mot_thu_muc_goi_ten_duoc()
    {
        // Ảnh upload trước khi tạo job là ca bình thường, không phải lỗi. Nhưng "tenant//kind/file"
        // với một đoạn rỗng là một khoá khó đọc và khó liệt kê bằng prefix.
        IStorageService.BuildKey(Tenant, null, AssetKind.ProductImage, "anh.jpg")
            .Should().Be("aaaaaaaa000000000000000000000001/unassigned/productimage/anh.jpg");
    }

    [Fact]
    public void Khoa_object_khong_chua_dau_gach_ngang_cua_Guid()
    {
        // Định dạng "N" cho khoá ngắn hơn và tránh phải nghĩ về chuyện dấu gạch ngang có cần
        // escape trong URL presigned hay không.
        IStorageService.BuildKey(Tenant, Job, AssetKind.VoiceAudio, "voice.mp3")
            .Should().NotContain("-");
    }

    [Fact]
    public void Ten_loai_asset_trong_khoa_viet_thuong()
    {
        // S3 phân biệt hoa thường. "FinalVideo" và "finalvideo" là hai thư mục khác nhau, và sự
        // khác nhau đó chỉ lộ ra khi đi tìm một file "chắc chắn đã upload rồi".
        string key = IStorageService.BuildKey(Tenant, Job, AssetKind.ReferenceImage, "ref.png");

        key.Should().Contain("/referenceimage/");
    }
}
