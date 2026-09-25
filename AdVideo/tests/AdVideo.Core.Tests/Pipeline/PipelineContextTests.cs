using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;
using FluentAssertions;

namespace AdVideo.Core.Tests.Pipeline;

/// <summary>
/// Ngữ cảnh pipeline, kết quả bước, và credential đã giải mã.
/// </summary>
/// <remarks>
/// Ba thứ nhỏ nhưng mỗi thứ gác một cửa: trần chi tiêu (không dừng thì job tự tiêu tiếp),
/// cờ retry (phân biệt "thử lại được" với "thử lại bao nhiêu lần cũng thế"), và ToString của
/// credential (một dòng log sai chỗ là một lần rò API key).
/// </remarks>
public class PipelineContextTests
{
    private static PipelineContext Context() => new()
    {
        JobId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
    };

    [Fact]
    public void Khong_dat_tran_thi_khong_bao_gio_bao_vuot_tran()
    {
        // Sprint 1 có job chạy không trần (nội bộ, nhỏ). Nếu null bị coi là 0 thì mọi job như vậy
        // dừng ngay ở bước đầu với lý do "vượt trần" — một lỗi rất khó đoán khi đọc log.
        var context = Context();
        context.AccumulatedCostUsd = 100m;

        context.IsCostCeilingHit.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.5, 1.0, false)]
    [InlineData(0.999, 1.0, false)]
    [InlineData(1.0, 1.0, true)]
    [InlineData(1.5, 1.0, true)]
    public void Cham_tran_la_da_phai_dung_chu_khong_doi_vuot_tran(
        double accumulated, double ceiling, bool expected)
    {
        // So bằng >= chứ không phải >: chi phí shot tiếp theo luôn > 0, nên "đúng bằng trần" mà
        // chạy tiếp thì chắc chắn vượt.
        var context = Context();
        context.AccumulatedCostUsd = (decimal)accumulated;
        context.MaxCostUsd = (decimal)ceiling;

        context.IsCostCeilingHit.Should().Be(expected);
    }

    [Fact]
    public void Mac_dinh_cua_ngu_canh_la_tier_Standard_va_khung_doc()
    {
        var context = Context();

        context.Tier.Should().Be(VideoTier.Standard);
        context.AspectRatio.Should().Be(AspectRatio.Portrait9x16);
        context.ProviderName.Should().BeNull("provider do hệ thống chọn ở bước chọn provider, không có mặc định");
        context.Timeline.Should().BeNull();
    }

    [Fact]
    public void Cac_bo_suu_tap_trong_ngu_canh_luon_san_sang_ghi()
    {
        // Mỗi bước ghi vào đây mà không kiểm null. Một collection null nghĩa là bước đó ném
        // NullReferenceException giữa lúc đang có file tạm nằm trên MinIO.
        var context = Context();

        context.ProductImageKeys.Should().BeEmpty();
        context.WordTimings.Should().BeEmpty();
        context.ShotClipKeys.Should().BeEmpty();
        context.ShotNativeAudioKeys.Should().BeEmpty();
        context.Bag.Should().BeEmpty();
    }

    [Fact]
    public void Bag_phan_biet_hoa_thuong_de_khong_co_hai_khoa_gan_giong_nhau()
    {
        var context = Context();
        context.Bag["retryCount"] = 1;

        context.Bag.ContainsKey("RetryCount").Should().BeFalse(
            "hai khoá chỉ khác chữ hoa là hai chỗ chứa khác nhau, và bước đọc sẽ đọc nhầm chỗ trống");
    }

    [Fact]
    public void Ket_qua_thanh_cong_khong_mang_theo_ly_do_loi()
    {
        var result = StepResult.Ok();

        result.IsSuccess.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.RawError.Should().BeNull();
        result.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void Loi_khong_retry_duoc_thi_khong_duoc_danh_dau_retry()
    {
        // Nội dung bị provider từ chối, thiếu ảnh, vượt trần: thử lại chỉ tiêu thêm tiền cho
        // cùng một kết quả.
        var result = StepResult.Fail("Provider từ chối nội dung.", "moderation_blocked");

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.FailureReason.Should().Be("Provider từ chối nội dung.");
        result.RawError.Should().Be("moderation_blocked");
    }

    [Fact]
    public void Loi_ha_tang_thi_danh_dau_retry_duoc()
    {
        var result = StepResult.Retry("Provider hết thời gian chờ.", "504");

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public void Loi_ky_thuat_tach_khoi_ly_do_cho_nguoi_dung_doc()
    {
        // FailureReason hiện trên UI của khách; RawError chỉ nằm trong DB cho người vận hành.
        // Gộp làm một nghĩa là hoặc khách đọc stack trace, hoặc người vận hành không có gì để lần.
        var result = StepResult.Fail("Không tạo được video.", "{\"code\":500,\"detail\":\"internal\"}");

        result.FailureReason.Should().NotContain("{");
        result.RawError.Should().Contain("500");
    }

    [Theory]
    [InlineData("k", "****")]
    [InlineData("1234", "****")]
    [InlineData("sk-live-abcd1234", "****1234")]
    public void Credential_chi_lo_4_ky_tu_cuoi(string apiKey, string expected)
    {
        Credential(apiKey).MaskedKey.Should().Be(expected);
    }

    [Fact]
    public void ToString_cua_credential_khong_bao_gio_chua_key()
    {
        // Record C# có ToString mặc định in MỌI thuộc tính. Một dòng
        // logger.LogInformation("{Credential}", credential) là đủ để API key nằm vĩnh viễn
        // trong log — nơi backup, nơi chuyển sang hệ thống giám sát, nơi ai cũng đọc được.
        var credential = Credential("sk-live-supersecret9999");

        string text = credential.ToString();

        text.Should().NotContain("supersecret");
        text.Should().Contain("****9999").And.Contain(ProviderNames.Kling);
    }

    [Fact]
    public void Credential_hien_du_de_biet_dang_dung_key_nao()
    {
        // Che quá tay cũng là một lỗi: khi có hai key của cùng một provider và một cái hết credit,
        // người vận hành phải phân biệt được chúng trong log.
        Credential("sk-live-aaaa1111").ToString()
            .Should().NotBe(Credential("sk-live-bbbb2222").ToString());
    }

    private static ResolvedCredential Credential(string apiKey) => new(
        ProviderNames.Kling,
        "kling-v2",
        ProviderCategory.Video,
        "https://api.example.com",
        apiKey,
        CredentialScope.System,
        TenantId: null,
        Priority: 0);
}
