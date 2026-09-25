using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using FluentAssertions;

namespace AdVideo.Core.Tests.Providers;

/// <summary>
/// Các thuộc tính suy ra trên kết quả provider: <c>CanLockTimeline</c>, <c>CanRetry</c>,
/// và kết quả chọn provider.
/// </summary>
/// <remarks>
/// Ba thuộc tính này đều là một dòng code, và cả ba đều quyết định worker làm gì tiếp theo. Tin
/// nhầm một trong ba thì hỏng theo kiểu im lặng: timeline dựng trên dữ liệu rỗng, hoặc một lỗi
/// vĩnh viễn được thử lại đủ số lần rồi mới chịu fail — mỗi lần một hoá đơn.
/// </remarks>
public class ProviderResultTests
{
    private static readonly WordTiming Word = new("xin", 0, 0.4);
    private static readonly CharacterTiming Character = new('x', 0, 0.1);

    [Fact]
    public void TTS_thanh_cong_nhung_khong_co_moc_thoi_gian_thi_chua_khoa_duoc_timeline()
    {
        // Ca nguy hiểm nhất của bước 4: provider trả 200, có file audio, nhưng không kèm mốc
        // thời gian. Tin mỗi IsSuccess thì bước 5 chia shot trên một danh sách rỗng và cho ra một
        // timeline sai mà không bước nào fail.
        var result = new TtsResult { IsSuccess = true, AudioBytes = [1, 2, 3], AudioDurationSeconds = 4 };

        result.IsSuccess.Should().BeTrue();
        result.CanLockTimeline.Should().BeFalse();
    }

    [Fact]
    public void Co_moc_theo_tu_thi_khoa_duoc_timeline()
    {
        new TtsResult { IsSuccess = true, WordTimings = [Word] }.CanLockTimeline.Should().BeTrue();
    }

    [Fact]
    public void Chi_co_moc_theo_ky_tu_cung_du_de_khoa_timeline()
    {
        // Một số engine chỉ trả mốc theo ký tự. Gộp lại thành từ được, nên từ chối ở đây là tự
        // loại một provider vì lý do định dạng.
        new TtsResult { IsSuccess = true, CharacterTimings = [Character] }.CanLockTimeline.Should().BeTrue();
    }

    [Fact]
    public void That_bai_thi_khong_khoa_timeline_du_co_moc_thoi_gian()
    {
        new TtsResult { IsSuccess = false, WordTimings = [Word] }.CanLockTimeline.Should().BeFalse();
    }

    [Theory]
    [InlineData(VideoFailureKind.Transient, true)]
    [InlineData(VideoFailureKind.RateLimited, true)]
    [InlineData(VideoFailureKind.Unknown, true)]
    [InlineData(VideoFailureKind.ContentRejected, false)]
    [InlineData(VideoFailureKind.ProviderUnavailable, false)]
    [InlineData(VideoFailureKind.None, false)]
    public void Chi_loi_tam_thoi_moi_duoc_thu_lai(VideoFailureKind kind, bool canRetry)
    {
        // Retry một nội dung đã bị kiểm duyệt từ chối, hoặc một request sai định dạng, là trả
        // tiền nhiều lần cho cùng một câu trả lời "không".
        new VideoResult { IsSuccess = false, FailureKind = kind }.CanRetry.Should().Be(canRetry);
    }

    [Fact]
    public void Provider_chet_thi_fail_ca_job_chu_khong_thu_lai_cung_provider()
    {
        // Luật 1: một job, một provider. Provider chết thì không tự đổi sang provider khác — hai
        // model cho hai phong cách hình, ghép lại thì lộ rõ ở chỗ chuyển cảnh.
        new VideoResult { IsSuccess = false, FailureKind = VideoFailureKind.ProviderUnavailable }
            .CanRetry.Should().BeFalse();
    }

    [Fact]
    public void Quen_dat_loai_loi_thi_khong_thu_lai()
    {
        // Adapter mới quên điền FailureKind là chuyện sẽ xảy ra. Mặc định None đẩy về phía KHÔNG
        // tiêu thêm tiền: hậu quả là một job fail sớm và một dòng log khó hiểu — nhìn thấy được.
        // Mặc định ngược lại là ba lần gọi provider cho một lỗi chưa ai biết là lỗi gì.
        new VideoResult { IsSuccess = false }.CanRetry.Should().BeFalse();
    }

    [Fact]
    public void Chon_duoc_provider_thi_khong_kem_ly_do_that_bai()
    {
        var result = ProviderSelectionResult.Success(ProviderNames.Kling);

        result.IsSuccess.Should().BeTrue();
        result.ProviderName.Should().Be(ProviderNames.Kling);
        result.Reasons.Should().BeEmpty();
        result.Suggestions.Should().BeEmpty();
    }

    [Fact]
    public void Khong_chon_duoc_thi_luon_co_danh_sach_goi_y_du_rong()
    {
        // 422 không kèm gợi ý là 422 vô dụng. Kể cả khi không nghĩ ra được phương án nào, danh
        // sách phải là rỗng chứ không phải null — phía API serialise thẳng ra JSON.
        var result = ProviderSelectionResult.Failure(["Không có provider nào nhận mặt người ở tier này."]);

        result.IsSuccess.Should().BeFalse();
        result.ProviderName.Should().BeNull();
        result.Reasons.Should().ContainSingle();
        result.Suggestions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Goi_y_kem_theo_duoc_giu_nguyen()
    {
        var suggestion = new ProviderSuggestion(ProviderNames.Kling, VideoTier.Standard, "nhận mặt người", 1.2m);

        ProviderSelectionResult.Failure(["x"], [suggestion])
            .Suggestions.Should().Equal(suggestion);
    }

    [Fact]
    public void Ket_qua_kiem_capability_khong_co_canh_bao_van_tra_danh_sach_rong()
    {
        CapabilityCheckResult.Ok(recommendedDuration: 8).Warnings.Should().NotBeNull().And.BeEmpty();
        CapabilityCheckResult.Reject(["x"], []).Warnings.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Tu_choi_thi_khong_con_thoi_luong_de_nghi()
    {
        // Để lại một con số ở đây là mời bước sau dùng nó mà quên rằng provider đã bị loại.
        CapabilityCheckResult.Reject(["x"], []).RecommendedShotDurationSeconds.Should().BeNull();
    }
}
