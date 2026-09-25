using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using AdVideo.Core.Tests.TestData;
using FluentAssertions;

namespace AdVideo.Core.Tests.Providers;

/// <summary>
/// Luật 2 — API chuẩn hoá lỗi, không chuyển tiếp JSON của nhà cung cấp.
/// </summary>
/// <remarks>
/// Giá trị thật của lớp này là TIỀN: một yêu cầu bất khả thi bị chặn ở đây thì không có lời gọi
/// provider nào phát sinh. Vì vậy test tập trung vào "có chặn không" và "thông báo có đọc được
/// không", chứ không chỉ vào kiểu dữ liệu trả về.
/// </remarks>
public class ProviderCapabilityValidatorTests
{
    [Fact]
    public void Nem_khi_thieu_doi_so()
    {
        var cap = Caps.Video();

        ((Action)(() => ProviderCapabilityValidator.Check(null!, cap))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ProviderCapabilityValidator.Check(Caps.Requirements(), null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Yeu_cau_hop_le_thi_dat_va_kem_bac_thoi_luong_nen_dung()
    {
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(durationSeconds: 5),
            Caps.Video(grid: Caps.Grid468));

        result.IsSatisfied.Should().BeTrue();
        result.BlockingReasons.Should().BeEmpty();
        result.Suggestions.Should().BeEmpty();
        result.RecommendedShotDurationSeconds.Should().Be(6, "5 giây phải làm tròn LÊN bậc 6");
    }

    [Fact]
    public void Luoi_thoi_luong_rong_bi_goi_dung_ten_la_loi_van_hanh()
    {
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(),
            Caps.Video(grid: []));

        result.IsSatisfied.Should().BeFalse();
        result.BlockingReasons.Should().ContainSingle().Which.Should()
            .Contain("lỗi vận hành", "người vận hành phải biết đây là DB thiếu cấu hình, không phải brief sai");
        result.Suggestions.Should().BeEmpty();
    }

    [Fact]
    public void Khung_hinh_khong_ho_tro_thi_chan_va_liet_ke_cai_ho_tro_duoc()
    {
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(ratio: AspectRatio.Square1x1),
            Caps.Video(ratios: [AspectRatio.Portrait9x16]));

        result.IsSatisfied.Should().BeFalse();
        result.BlockingReasons.Should().ContainSingle().Which.Should().Contain("1:1").And.Contain("9:16");
    }

    [Fact]
    public void Tier_khong_phuc_vu_duoc_thi_chan_bang_tieng_Viet()
    {
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(tier: VideoTier.Premium),
            Caps.Video(tiers: [VideoTier.Draft]));

        result.IsSatisfied.Should().BeFalse();
        result.BlockingReasons.Should().ContainSingle().Which.Should()
            .Contain("Cao cấp", "khách đọc nhãn tiếng Việt, không đọc tên enum").And
            .Contain("Nháp");
    }

    [Fact]
    public void Provider_khong_nhan_mat_nguoi_thi_chan_ngay_truoc_khi_ton_tien()
    {
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(hasPerson: true),
            Caps.Video(provider: ProviderNames.Veo, acceptsHumanFaces: false));

        result.IsSatisfied.Should().BeFalse();
        result.BlockingReasons.Should().ContainSingle().Which.Should().Contain("mặt người");
    }

    [Fact]
    public void Can_noi_khung_hinh_ma_provider_khong_lam_duoc_thi_chan()
    {
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(needsFrameChaining: true),
            Caps.Video(supportsFrameChaining: false));

        result.IsSatisfied.Should().BeFalse();
        result.BlockingReasons.Should().ContainSingle().Which.Should().Contain("nối khung hình");
    }

    [Fact]
    public void Video_ngan_hon_bac_ngan_nhat_thi_chan_kem_con_so_cu_the()
    {
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(durationSeconds: 3),
            Caps.Video(grid: Caps.Grid510));

        result.IsSatisfied.Should().BeFalse();
        result.BlockingReasons.Should().ContainSingle().Which.Should().Contain("từ 5 giây trở lên");
    }

    [Fact]
    public void Nhieu_chu_the_hon_muc_da_do_thi_canh_bao_chu_khong_chan()
    {
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(subjectCount: 3),
            Caps.Video(maxSubjects: 2));

        result.IsSatisfied.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("3 chủ thể");
    }

    [Fact]
    public void Chua_do_so_chu_the_thi_khong_canh_bao_bua()
    {
        // MaxSubjectsReliably = 0 nghĩa là CHƯA ĐO (Sprint 0 bị hoãn), không phải "không giữ
        // được chủ thể nào". Cảnh báo dựa trên số chưa đo là cảnh báo dạy người dùng bỏ qua cảnh báo.
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(subjectCount: 5),
            Caps.Video(maxSubjects: 0));

        result.IsSatisfied.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Canh_bao_van_duoc_giu_khi_yeu_cau_bi_chan()
    {
        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(subjectCount: 3, hasPerson: true),
            Caps.Video(acceptsHumanFaces: false, maxSubjects: 2));

        result.IsSatisfied.Should().BeFalse();
        result.Warnings.Should().ContainSingle();
    }

    [Fact]
    public void Bi_chan_thi_goi_y_provider_lam_duoc_viec_nay_xep_theo_gia()
    {
        var rejected = Caps.Video(provider: ProviderNames.Veo, acceptsHumanFaces: false);

        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(durationSeconds: 8, hasPerson: true),
            rejected,
            alternatives:
            [
                rejected, // chính nó — phải bị loại khỏi danh sách gợi ý
                Caps.Video(provider: ProviderNames.Kling, grid: Caps.Grid510, costPerSecond: 0.10m),
                Caps.Video(provider: ProviderNames.Seedance, costPerSecond: 0.02m),
                Caps.Video(provider: ProviderNames.Vidu, grid: []), // chưa cấu hình lưới → không gợi ý
                Caps.Video(provider: ProviderNames.Runway, acceptsHumanFaces: false), // cũng chặn mặt người
            ]);

        result.IsSatisfied.Should().BeFalse();
        result.Suggestions.Select(s => s.ProviderName).Should()
            .Equal([ProviderNames.Seedance, ProviderNames.Kling], "rẻ nhất phải đứng trước");
        result.Suggestions[0].EstimatedCostUsd.Should().Be(0.16m, "8 giây trên lưới 4/6/8 là một shot 8 giây × 0,02");
        result.Suggestions[0].Reason.Should().Contain("mặt người");
        result.Suggestions[0].Tier.Should().Be(VideoTier.Standard);
    }

    [Fact]
    public void Goi_y_giai_thich_dung_ly_do_lam_duoc_viec()
    {
        var rejected = Caps.Video(provider: ProviderNames.Veo, supportsFrameChaining: false);

        var chaining = ProviderCapabilityValidator.Check(
            Caps.Requirements(needsFrameChaining: true),
            rejected,
            alternatives: [Caps.Video(provider: ProviderNames.Kling, supportsFrameChaining: true)]);

        chaining.Suggestions.Should().ContainSingle().Which.Reason.Should().Contain("nối khung hình");

        var subjects = ProviderCapabilityValidator.Check(
            Caps.Requirements(subjectCount: 3, ratio: AspectRatio.Square1x1),
            Caps.Video(ratios: [AspectRatio.Portrait9x16]),
            alternatives:
            [
                Caps.Video(
                    provider: ProviderNames.Vidu,
                    ratios: [AspectRatio.Square1x1],
                    maxSubjects: 4),
            ]);

        subjects.Suggestions.Should().ContainSingle().Which.Reason.Should().Contain("4 chủ thể");

        var generic = ProviderCapabilityValidator.Check(
            Caps.Requirements(ratio: AspectRatio.Square1x1),
            Caps.Video(provider: ProviderNames.Veo, ratios: [AspectRatio.Portrait9x16]),
            alternatives: [Caps.Video(provider: ProviderNames.Kling, ratios: [AspectRatio.Square1x1])]);

        generic.Suggestions.Should().ContainSingle().Which.Reason.Should().Contain("Chuẩn");
    }

    [Fact]
    public void Video_dai_hon_mot_shot_thi_gia_goi_y_tinh_theo_so_shot_phai_goi()
    {
        var rejected = Caps.Video(provider: ProviderNames.Veo, acceptsHumanFaces: false);

        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(durationSeconds: 30, hasPerson: true),
            rejected,
            alternatives:
            [
                Caps.Video(provider: ProviderNames.Kling, grid: Caps.Grid510, costPerSecond: 0.10m),
                Caps.Video(provider: ProviderNames.Seedance, costPerSecond: 0.02m),
            ]);

        result.Suggestions.Should().HaveCount(2);
        result.Suggestions[0].ProviderName.Should().Be(ProviderNames.Seedance);
        result.Suggestions[0].EstimatedCostUsd.Should().Be(
            0.64m, "30 giây trên lưới 4/6/8 là 4 shot 8 giây = 32 giây bị tính tiền × 0,02");
        result.Suggestions[1].EstimatedCostUsd.Should().Be(
            3.0m, "30 giây trên lưới 5/10 là 3 shot 10 giây × 0,10");
    }

    [Fact]
    public void Hai_goi_y_cung_gia_thi_xep_theo_ten_cho_on_dinh()
    {
        // Thứ tự không ổn định là thứ tự khiến cùng một request cho ra hai câu trả lời khác nhau
        // và test thì lúc xanh lúc đỏ.
        var rejected = Caps.Video(provider: ProviderNames.Veo, acceptsHumanFaces: false);

        var result = ProviderCapabilityValidator.Check(
            Caps.Requirements(durationSeconds: 8, hasPerson: true),
            rejected,
            alternatives:
            [
                Caps.Video(provider: ProviderNames.Seedance, costPerSecond: 0.02m),
                Caps.Video(provider: ProviderNames.Kling, costPerSecond: 0.02m),
            ]);

        result.Suggestions.Select(s => s.ProviderName).Should()
            .Equal(ProviderNames.Kling, ProviderNames.Seedance);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 6)]
    [InlineData(8, 8)]
    public void SmallestDurationAtLeast_luon_lam_tron_len(int seconds, int expected)
    {
        ProviderCapabilityValidator.SmallestDurationAtLeast(seconds, [4, 6, 8]).Should().Be(expected);
    }

    [Fact]
    public void SmallestDurationAtLeast_tra_null_khi_luoi_khong_voi_toi()
    {
        ProviderCapabilityValidator.SmallestDurationAtLeast(9, [4, 6, 8]).Should().BeNull();
        ProviderCapabilityValidator.SmallestDurationAtLeast(4, []).Should().BeNull();
    }

    [Fact]
    public void SmallestDurationAtLeast_tu_sap_lai_luoi_nhap_lon_xon()
    {
        ProviderCapabilityValidator.SmallestDurationAtLeast(5, [8, 4, 6]).Should().Be(6);
    }

    [Theory]
    [InlineData(VideoTier.Draft, "Nháp")]
    [InlineData(VideoTier.Standard, "Chuẩn")]
    [InlineData(VideoTier.Premium, "Cao cấp")]
    public void Nhan_tier_bang_tieng_Viet(VideoTier tier, string expected)
    {
        ProviderCapabilityValidator.TierLabel(tier).Should().Be(expected);
    }

    [Fact]
    public void Tier_la_nhan_chua_dich_thi_tra_nguyen_ten()
    {
        ProviderCapabilityValidator.TierLabel((VideoTier)99).Should().Be("99");
    }

    [Fact]
    public void MaxDurationSeconds_bang_0_khi_chua_co_luoi()
    {
        Caps.Video(grid: []).MaxDurationSeconds.Should().Be(0);
        Caps.Video(grid: Caps.Grid510).MaxDurationSeconds.Should().Be(10);
    }
}
