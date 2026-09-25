using AdVideo.Core.Costing;
using AdVideo.Core.Tests.TestData;
using FluentAssertions;

namespace AdVideo.Core.Tests.Costing;

/// <summary>
/// D8 — dự toán TRƯỚC khi gọi provider.
/// </summary>
/// <remarks>
/// Đây là một trong hai hàng rào chống thủng ví (hàng rào kia là
/// <see cref="Core.Idempotency.IdempotencyGuard"/>). Test ở đây canh đúng một câu hỏi: con số
/// dùng để gác cổng có BAO GIỜ thấp hơn hoá đơn thật không. Nếu có thì cái trần không còn là trần.
/// </remarks>
public class CostEstimatorTests
{
    [Fact]
    public void Nem_khi_doi_so_vo_nghia()
    {
        var cap = Caps.Video();

        ((Action)(() => CostEstimator.Estimate(10, null!, null, 1))).Should().Throw<ArgumentNullException>();
        ((Action)(() => CostEstimator.Estimate(0, cap, null, 1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => CostEstimator.Estimate(-5, cap, null, 1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => CostEstimator.Estimate(10, cap, null, -1))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Tinh_tien_theo_so_giay_BI_TINH_TIEN_chu_khong_theo_so_giay_khach_xin()
    {
        // 15 giây trên lưới 4/6/8 = 8 + 8 = 16 giây bị tính tiền. Dự toán theo 15 giây sẽ luôn
        // thấp hơn hoá đơn, và một cái trần luôn thấp hơn hoá đơn thì không phải là trần.
        var estimate = CostEstimator.Estimate(15, Caps.Video(costPerSecond: 0.05m), null, maxRetriesPerShot: 0);

        estimate.ShotCount.Should().Be(2);
        estimate.BilledVideoSeconds.Should().Be(16);
        estimate.VideoCostUsd.Should().Be(0.80m);
        estimate.TtsCostUsd.Should().Be(0m, "không có TTS thì không có tiền giọng đọc");
        estimate.EstimatedCostUsd.Should().Be(0.80m);
        estimate.WorstCaseCostUsd.Should().Be(0.80m, "không retry thì xấu nhất bằng dự toán");
    }

    [Fact]
    public void Retry_chi_nhan_tien_video_khong_nhan_tien_giong_doc()
    {
        // Lời thoại sinh một lần là dùng cho mọi lần render lại — đó cũng là lý do thứ tự
        // 4 → 5 → 6 không đảo được.
        var estimate = CostEstimator.Estimate(
            8,
            Caps.Video(costPerSecond: 0.05m),
            Caps.Tts(costPer1000Chars: 1.0m),
            maxRetriesPerShot: 2,
            script: new string('a', 1000));

        estimate.VideoCostUsd.Should().Be(0.40m);
        estimate.TtsCostUsd.Should().Be(1.0m);
        estimate.EstimatedCostUsd.Should().Be(1.40m);
        estimate.WorstCaseCostUsd.Should().Be(2.20m, "0,40 × 3 lần + 1,0 tiền giọng đọc");
    }

    [Fact]
    public void Chua_co_loi_thoai_that_thi_uoc_so_ky_tu_tu_thoi_luong()
    {
        var estimate = CostEstimator.Estimate(
            8, Caps.Video(costPerSecond: 0m), Caps.Tts(costPer1000Chars: 1.0m), maxRetriesPerShot: 0);

        // 8 giây × 12,5 ký tự/giây = 100 ký tự → 0,1 của 1000 ký tự.
        estimate.TtsCostUsd.Should().Be(0.1m);
    }

    [Fact]
    public void Lam_tron_tien_den_4_chu_so_va_lam_tron_len_khi_o_giua()
    {
        var estimate = CostEstimator.Estimate(
            4, Caps.Video(costPerSecond: 0.00005m), null, maxRetriesPerShot: 0);

        // 4 giây × 0,00005 = 0,0002 — vẫn giữ được ở 4 chữ số thập phân.
        estimate.VideoCostUsd.Should().Be(0.0002m);
    }

    [Theory]
    [InlineData(4, new[] { 4 })]
    [InlineData(1, new[] { 4 })]
    [InlineData(5, new[] { 6 })]
    [InlineData(8, new[] { 8 })]
    [InlineData(9, new[] { 8, 4 })]
    [InlineData(20, new[] { 8, 8, 4 })]
    [InlineData(24, new[] { 8, 8, 8 })]
    public void Chia_shot_theo_luoi_va_luon_lam_tron_len(int target, int[] expected)
    {
        CostEstimator.PlanShotDurations(target, Caps.Video(grid: Caps.Grid468))
            .Should().Equal(expected);
    }

    [Fact]
    public void Luoi_nhap_lon_xon_van_chia_dung()
    {
        CostEstimator.PlanShotDurations(15, Caps.Video(grid: [8, 4, 8, 6, 0, -3]))
            .Should().Equal(8, 8);
    }

    [Fact]
    public void Khong_co_luoi_thi_nem_chu_khong_doan()
    {
        // Không tính được tiền thì không được phép gọi. Trả về 0 ở đây là mở cửa cho một job
        // không có trần.
        Action act = () => CostEstimator.PlanShotDurations(10, Caps.Video(grid: []));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*không khai báo lưới thời lượng*");
    }

    [Fact]
    public void PlanShotDurations_nem_khi_thieu_capability()
    {
        ((Action)(() => CostEstimator.PlanShotDurations(10, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Video_dai_tren_luoi_thua_la_ca_dong_tien_bi_tinh_thua()
    {
        // 180 giây trên lưới 5/10 = 18 lần gọi. Con số này là lý do dự toán phải chạy trước.
        var estimate = CostEstimator.Estimate(
            180, Caps.Video(grid: Caps.Grid510, costPerSecond: 0.10m), null, maxRetriesPerShot: 1);

        estimate.ShotCount.Should().Be(18);
        estimate.BilledVideoSeconds.Should().Be(180);
        estimate.EstimatedCostUsd.Should().Be(18.0m);
        estimate.WorstCaseCostUsd.Should().Be(36.0m);
    }
}
