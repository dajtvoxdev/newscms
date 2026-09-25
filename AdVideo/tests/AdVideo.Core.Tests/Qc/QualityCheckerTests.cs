using AdVideo.Core.Enums;
using AdVideo.Core.Media;
using AdVideo.Core.Qc;
using FluentAssertions;

namespace AdVideo.Core.Tests.Qc;

/// <summary>
/// Bước 9 — QC. Phân biệt rõ phép kiểm CHẶN với phép kiểm chỉ báo.
/// </summary>
/// <remarks>
/// Chặn sai thì khách không nhận được video đã trả tiền; không chặn thứ đáng chặn thì khách nhận
/// một video vi phạm luật gắn nhãn. Vì vậy mỗi test ở đây kiểm cả <c>Passed</c> lẫn
/// <c>IsBlocking</c>, không kiểm mỗi cái tổng.
/// </remarks>
public class QualityCheckerTests
{
    private static readonly AiLabelSpec GoodLabel = AiLabelStamper.Build(AspectRatio.Portrait9x16, 15);

    private static MediaProbeResult Probe(
        double duration = 15,
        int width = 1080,
        int height = 1920,
        string? videoCodec = "h264",
        bool hasAudio = true,
        long sizeBytes = 2_000_000,
        double? lufs = -14,
        bool hasBlackFrames = false,
        double? lipSyncDrift = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<string>? detectedText = null) =>
        new(
            duration, width, height, videoCodec, hasAudio, hasAudio ? "aac" : null, sizeBytes,
            lufs, metadata, hasBlackFrames, lipSyncDrift, detectedText);

    private static QcCheck Check(QcReport report, string name) =>
        report.Checks.Single(c => c.Check == name);

    [Fact]
    public void Nem_khi_thieu_so_lieu_do_duoc()
    {
        var thresholds = new QcThresholds();

        ((Action)(() => QualityChecker.Evaluate(null!, thresholds, GoodLabel, 1080, 1920, 15)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => QualityChecker.Evaluate(Probe(), null!, GoodLabel, 1080, 1920, 15)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Video_dat_chuan_thi_qua_het_va_tom_tat_noi_ro_so_phep_kiem()
    {
        var report = QualityChecker.Evaluate(
            Probe(metadata: new Dictionary<string, string> { ["AI-Generated"] = "true" }),
            new QcThresholds(),
            GoodLabel,
            1080, 1920, 15);

        report.IsPassed.Should().BeTrue();
        report.Failed.Should().BeEmpty();
        report.FailedBlocking.Should().BeEmpty();
        report.Summary.Should().StartWith("QC đạt");
    }

    [Fact]
    public void Thieu_video_stream_la_loi_chan()
    {
        var report = QualityChecker.Evaluate(Probe(videoCodec: null), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "has_video_stream");
        check.Passed.Should().BeFalse();
        check.IsBlocking.Should().BeTrue();
        check.Message.Should().Contain("compose hỏng");
        report.IsPassed.Should().BeFalse();
    }

    [Fact]
    public void Video_khong_co_tieng_bi_chan_va_chi_dung_buoc_phai_kiem_tra()
    {
        var report = QualityChecker.Evaluate(Probe(hasAudio: false), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "has_audio_stream");
        check.Passed.Should().BeFalse();
        check.IsBlocking.Should().BeTrue();
        check.Message.Should().Contain("bước 4").And.Contain("bước 8");
        report.Summary.Should().StartWith("QC fail").And.Contain("has_audio_stream",
            "dòng này đi thẳng vào FailureReason của job — không nêu tên phép kiểm thì người vận hành phải mở QcReport mới biết hỏng ở đâu");
    }

    [Fact]
    public void Co_tieng_nhung_ffprobe_khong_doc_duoc_codec_thi_van_tinh_la_co()
    {
        // ffprobe đôi khi thấy audio stream mà không nhận ra codec. Đó là chuyện của ffprobe,
        // không phải bằng chứng video mất tiếng — chặn ở đây là chặn nhầm một video đúng.
        var report = QualityChecker.Evaluate(
            Probe() with { AudioCodec = null }, new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "has_audio_stream");
        check.Passed.Should().BeTrue();
        check.Actual.Should().Be("có");
    }

    [Theory]
    [InlineData(15.4, true)]
    [InlineData(15.6, false)]
    [InlineData(14.5, true)]
    public void Thoi_luong_phai_dung_trong_dung_sai(double actualDuration, bool shouldPass)
    {
        var report = QualityChecker.Evaluate(
            Probe(duration: actualDuration), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        Check(report, "duration").Passed.Should().Be(shouldPass);
    }

    [Fact]
    public void File_qua_nho_bi_chan_vi_thuong_la_file_hong()
    {
        var report = QualityChecker.Evaluate(Probe(sizeBytes: 900), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "file_size");
        check.Passed.Should().BeFalse();
        check.IsBlocking.Should().BeTrue();
    }

    [Fact]
    public void Do_phan_giai_lech_thi_bao_nhung_khong_chan()
    {
        // Video vẫn xem được nên chặn là quá tay; nhưng im lặng thì lần sau không ai biết
        // provider đã trả về khung khác khung mình xin.
        var report = QualityChecker.Evaluate(
            Probe(width: 720, height: 1280), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "resolution");
        check.Passed.Should().BeFalse();
        check.IsBlocking.Should().BeFalse();
        report.IsPassed.Should().BeTrue("phép kiểm không chặn thì không được làm fail cả job");
        report.Failed.Should().ContainSingle();
        report.Summary.Should().StartWith("QC đạt, có 1/").And.Contain("resolution",
            "một dòng tóm tắt nói \"đạt 8/8\" sẽ xoá sạch cảnh báo khỏi mắt người vận hành");
    }

    [Theory]
    [InlineData(-14.0, true)]
    [InlineData(-15.4, true)]
    [InlineData(-18.0, false)]
    public void Am_luong_lech_chuan_thi_bao_nhung_khong_chan(double lufs, bool shouldPass)
    {
        var report = QualityChecker.Evaluate(Probe(lufs: lufs), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "loudness");
        check.Passed.Should().Be(shouldPass);
        check.IsBlocking.Should().BeFalse();
    }

    [Fact]
    public void Khong_do_duoc_LUFS_cung_la_mot_phep_kiem_truot_kem_cach_do_lai()
    {
        var report = QualityChecker.Evaluate(Probe(lufs: null), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "loudness");
        check.Passed.Should().BeFalse();
        check.IsBlocking.Should().BeFalse();
        check.Message.Should().Contain("ebur128");
    }

    [Fact]
    public void Khung_den_duoc_bao_nhung_khong_chan()
    {
        var report = QualityChecker.Evaluate(Probe(hasBlackFrames: true), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "no_black_frames");
        check.Passed.Should().BeFalse();
        check.IsBlocking.Should().BeFalse();
        check.Message.Should().Contain("crossfade");
    }

    [Theory]
    [InlineData(0.15, true)]
    [InlineData(-0.15, true)]
    [InlineData(0.35, false)]
    public void Lech_tieng_hinh_do_duoc_thi_chan_theo_nguong_200ms(double drift, bool shouldPass)
    {
        var report = QualityChecker.Evaluate(
            Probe(lipSyncDrift: drift), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "lip_sync_drift");
        check.Passed.Should().Be(shouldPass);
        check.IsBlocking.Should().BeTrue();
        check.Actual.Should().EndWith("ms");
    }

    [Fact]
    public void Khong_do_duoc_lech_tieng_hinh_thi_khong_co_phep_kiem_do()
    {
        // Sprint 1 chưa đo được. Bịa ra một phép kiểm "đạt" khi chưa đo là tự nói dối.
        var report = QualityChecker.Evaluate(Probe(lipSyncDrift: null), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        report.Checks.Should().NotContain(c => c.Check == "lip_sync_drift");
    }

    [Fact]
    public void Thieu_nhan_AI_lam_fail_ca_job()
    {
        var report = QualityChecker.Evaluate(Probe(), new QcThresholds(), labelSpec: null, 1080, 1920, 15);

        var check = Check(report, "ai_label");
        check.Passed.Should().BeFalse();
        check.IsBlocking.Should().BeTrue();
        report.IsPassed.Should().BeFalse();
        report.FailedBlocking.Should().ContainSingle();
    }

    [Fact]
    public void Dac_ta_nhan_dung_nhung_file_khong_co_metadata_van_la_thieu_nhan()
    {
        // Ca nguy hiểm nhất: mọi thứ trong code đều đúng, chỉ có cú pháp -metadata của FFmpeg
        // là sai, nên file giao khách không mang dấu AI nào.
        var report = QualityChecker.Evaluate(
            Probe(metadata: new Dictionary<string, string> { ["title"] = "quảng cáo cà phê" }),
            new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "ai_label_in_file_metadata");
        check.Passed.Should().BeFalse();
        check.IsBlocking.Should().BeTrue();
        check.Actual.Should().Contain("title");
    }

    [Fact]
    public void Metadata_tieng_Viet_cung_duoc_chap_nhan_la_dau_AI()
    {
        var report = QualityChecker.Evaluate(
            Probe(metadata: new Dictionary<string, string> { ["comment"] = "Nội dung do trí tuệ nhân tạo tạo ra" }),
            new QcThresholds(), GoodLabel, 1080, 1920, 15);

        Check(report, "ai_label_in_file_metadata").Passed.Should().BeTrue();
    }

    [Fact]
    public void Dac_ta_nhan_da_hong_thi_khong_kiem_metadata_file_nua()
    {
        // Không cộng dồn hai lỗi cho cùng một nguyên nhân: đặc tả hỏng thì metadata chắc chắn
        // cũng hỏng, và hai dòng fail cho một nguyên nhân chỉ làm báo cáo khó đọc.
        var report = QualityChecker.Evaluate(
            Probe(metadata: new Dictionary<string, string> { ["title"] = "x" }),
            new QcThresholds(), labelSpec: null, 1080, 1920, 15);

        report.Checks.Should().NotContain(c => c.Check == "ai_label_in_file_metadata");
    }

    [Fact]
    public void Khong_co_metadata_nao_trong_file_thi_bo_qua_phep_kiem_metadata()
    {
        var report = QualityChecker.Evaluate(Probe(metadata: null), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        report.Checks.Should().NotContain(c => c.Check == "ai_label_in_file_metadata");
        report.IsPassed.Should().BeTrue();
    }

    [Fact]
    public void Chu_do_model_tu_ve_lam_fail_job_vi_chu_tieng_Viet_luon_sai_dau()
    {
        var report = QualityChecker.Evaluate(
            Probe(detectedText: ["GIÃM GIA 50%", "MUÁ NGAY", "HÔM NAY", "THỨ TƯ"]),
            new QcThresholds(), GoodLabel, 1080, 1920, 15);

        var check = Check(report, "no_model_drawn_text");
        check.Passed.Should().BeFalse();
        check.IsBlocking.Should().BeTrue();
        check.Actual.Should().Contain("GIÃM GIA 50%");
        check.Actual.Should().NotContain("THỨ TƯ", "chỉ trích 3 mẫu là đủ cho log, không cần đổ hết vào");
        check.Message.Should().Contain("D4");
    }

    [Fact]
    public void Khong_phat_hien_chu_nao_thi_khong_them_phep_kiem()
    {
        var report = QualityChecker.Evaluate(Probe(detectedText: []), new QcThresholds(), GoodLabel, 1080, 1920, 15);

        report.Checks.Should().NotContain(c => c.Check == "no_model_drawn_text");
    }

    [Fact]
    public void Nguong_QC_nap_tu_cau_hinh_chu_khong_hard_code()
    {
        // Ngưỡng là thứ sẽ phải chỉnh sau khi xem kết quả thật, nên phải đổi được mà không sửa code.
        var thresholds = new QcThresholds
        {
            TargetLoudnessLufs = -23,
            LoudnessToleranceLu = 0.5,
            DurationToleranceSeconds = 2,
            MaxLipSyncDriftSeconds = 0.5,
            MinSizeBytes = 1,
        };

        var report = QualityChecker.Evaluate(
            Probe(duration: 16.5, lufs: -23.2, sizeBytes: 500, lipSyncDrift: 0.4),
            thresholds, GoodLabel, 1080, 1920, 15);

        report.IsPassed.Should().BeTrue();
        report.Failed.Should().BeEmpty();
    }

    [Fact]
    public void Nguong_mac_dinh_bam_theo_thiet_ke()
    {
        var defaults = new QcThresholds();

        defaults.TargetLoudnessLufs.Should().Be(-14);
        defaults.MaxLipSyncDriftSeconds.Should().Be(0.2, "tiêu chí thành công S4");
        defaults.DurationToleranceSeconds.Should().Be(0.5);
        defaults.LoudnessToleranceLu.Should().Be(1.5);
        defaults.MinSizeBytes.Should().Be(10_000);
    }
}
