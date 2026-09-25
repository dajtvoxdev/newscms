using AdVideo.Core.Providers;
using AdVideo.Core.Tests.TestData;
using AdVideo.Core.Timeline;
using FluentAssertions;

namespace AdVideo.Core.Tests.Timeline;

/// <summary>
/// Bước 5 — khoá timeline. Đây là class có nhiều nhánh nhất trong Core và cũng là chỗ sai thì
/// hỏng im lặng nhất: timeline sai không làm job fail, nó chỉ làm video ra có tiếng lệch hình.
/// </summary>
/// <remarks>
/// Mọi test ở đây đều thuần tính toán — không provider, không FFmpeg, không tốn một đồng nào.
/// </remarks>
public class TimelineLockerTests
{
    private static TimelineLockRequest Request(
        double narrationSeconds,
        IReadOnlyList<int>? grid = null,
        IReadOnlyList<WordTiming>? words = null,
        AudioLayoutStrategy layout = AudioLayoutStrategy.PadPerShot,
        int maxShots = 8,
        double maxLipSync = 0.2,
        double silenceThreshold = 0.25,
        IReadOnlyList<NarrationSegment>? preset = null) =>
        new()
        {
            NarrationDurationSeconds = narrationSeconds,
            DurationGrid = grid ?? Caps.Grid468,
            WordTimings = words ?? [],
            Layout = layout,
            MaxShots = maxShots,
            MaxLipSyncOffsetSeconds = maxLipSync,
            SilenceThresholdSeconds = silenceThreshold,
            PresetSegments = preset,
        };

    [Fact]
    public void Lock_nem_khi_request_null()
    {
        Action act = () => TimelineLocker.Lock(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Luoi_thoi_luong_rong_la_loi_cau_hinh_chu_khong_phai_loi_cua_brief()
    {
        var plan = TimelineLocker.Lock(Request(6, grid: []));

        plan.IsAcceptable.Should().BeFalse();
        plan.Shots.Should().BeEmpty();
        plan.Reasons.Should().ContainSingle().Which.Should().Contain("Lưới thời lượng rỗng");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3.2)]
    public void Chua_co_audio_that_thi_khong_khoa_duoc_timeline(double narrationSeconds)
    {
        var plan = TimelineLocker.Lock(Request(narrationSeconds));

        plan.IsAcceptable.Should().BeFalse();
        plan.Reasons.Should().ContainSingle().Which.Should().Contain("4 → 5 → 6");
    }

    [Fact]
    public void MaxShots_khong_duong_bi_tu_choi()
    {
        var plan = TimelineLocker.Lock(Request(6, maxShots: 0));

        plan.IsAcceptable.Should().BeFalse();
        plan.Reasons.Should().ContainSingle().Which.Should().Contain("MaxShots");
    }

    [Fact]
    public void Preset_rong_thi_tu_choi_chu_khong_tra_ve_timeline_khong_co_shot_nao()
    {
        var plan = TimelineLocker.Lock(Request(6, preset: []));

        plan.IsAcceptable.Should().BeFalse();
        plan.Reasons.Should().ContainSingle().Which.Should().Contain("Không chia được");
    }

    [Fact]
    public void Thoai_ngan_hon_bac_dai_nhat_thi_chi_mot_shot()
    {
        var plan = TimelineLocker.Lock(Request(4.8));

        plan.IsAcceptable.Should().BeTrue();
        plan.Shots.Should().ContainSingle();
        plan.Shots[0].VideoDurationSeconds.Should().Be(
            6, "thoại 4,8s phải được làm tròn LÊN bậc 6, cắt xuống bậc 4 là cắt mất lời");
        plan.Shots[0].NarrationStartSeconds.Should().Be(0);
        plan.Shots[0].NarrationEndSeconds.Should().Be(4.8);
        plan.TotalVideoSeconds.Should().Be(6);
        plan.TotalNarrationSeconds.Should().Be(4.8);
        plan.Warnings.Should().BeEmpty();
        plan.HasMidSentenceCut.Should().BeFalse();
    }

    [Fact]
    public void Lam_tron_len_khong_bi_nhieu_so_thap_phan_cua_TTS_day_len_bac_cao_hon()
    {
        // 6,04 giây là 6 giây cộng nhiễu làm tròn của TTS. Không có dung sai thì nó nhảy lên
        // bậc 8 và job phải trả tiền thêm 2 giây video cho mỗi shot.
        var plan = TimelineLocker.Lock(Request(6.04));

        plan.Shots.Should().ContainSingle().Which.VideoDurationSeconds.Should().Be(6);
    }

    [Theory]
    [InlineData(6.0, PaddingStrategy.None)]          // khớp đúng bậc → không phải bù gì
    [InlineData(5.3, PaddingStrategy.HoldLastFrame)] // dư 0,7s → giữ khung cuối vẫn chưa trông như đơ
    [InlineData(4.8, PaddingStrategy.KenBurns)]      // dư 1,2s → đứng yên chừng đó là người xem tưởng lỗi
    public void Phan_du_quyet_dinh_cach_bu(double narrationSeconds, PaddingStrategy expected)
    {
        var plan = TimelineLocker.Lock(Request(narrationSeconds));

        plan.Shots.Should().ContainSingle().Which.Padding.Should().Be(expected);
    }

    [Fact]
    public void PadPerShot_cho_lip_sync_offset_bang_0_o_moi_shot()
    {
        var words = Caps.ContiguousWords(12);

        var plan = TimelineLocker.Lock(Request(12, words: words));

        plan.IsAcceptable.Should().BeTrue();
        plan.Shots.Should().HaveCountGreaterThan(1, "12 giây thoại không lọt vào một bậc 8 giây");
        plan.Shots.Should().OnlyContain(s => s.LipSyncOffsetSeconds == 0);
        plan.MaxLipSyncOffsetSeconds.Should().Be(0);

        // Thoại của shot sau bắt đầu đúng lúc shot đó bắt đầu, không phải lúc nó vang lên trong
        // track gốc — đó chính là ý nghĩa của PadPerShot.
        plan.Shots[1].AudioStartInTimelineSeconds.Should().Be(plan.Shots[1].VideoStartSeconds);
    }

    [Fact]
    public void Cat_o_cho_im_lang_chu_khong_cat_o_cho_day_nhat()
    {
        // Năm từ, khoảng lặng 0,5 giây, rồi năm từ nữa. Đoạn đầu ĐÁNG LẼ nhét được 7 từ
        // (7,5 giây < 8), nhưng phải lùi về chỗ ngừng tự nhiên ở giây thứ 5.
        List<WordTiming> words =
        [
            .. Caps.ContiguousWords(5),
            .. Caps.ContiguousWords(5, start: 5.5),
        ];

        var plan = TimelineLocker.Lock(Request(10.5, words: words));

        plan.IsAcceptable.Should().BeTrue();
        plan.Shots.Should().HaveCount(2);
        plan.Shots[0].NarrationEndSeconds.Should().Be(
            5, "chỗ cắt phải là chỗ im lặng, không phải chỗ nhồi được nhiều từ nhất");
        plan.Shots[1].NarrationStartSeconds.Should().Be(5.5);
        plan.HasMidSentenceCut.Should().BeFalse();
        plan.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Loi_thoai_cua_tung_shot_duoc_giu_lai_de_lam_prompt_va_phu_de()
    {
        List<WordTiming> words =
        [
            .. Caps.ContiguousWords(5),
            .. Caps.ContiguousWords(5, start: 5.5),
        ];

        var plan = TimelineLocker.Lock(Request(10.5, words: words));

        plan.Shots[0].SpokenText.Should().Be("tu1 tu2 tu3 tu4 tu5");
        plan.Shots[1].SpokenText.Should().Be("tu1 tu2 tu3 tu4 tu5");
    }

    [Fact]
    public void Khong_co_cho_im_lang_nao_thi_cat_giua_cau_va_phai_noi_ro()
    {
        var plan = TimelineLocker.Lock(Request(12, words: Caps.ContiguousWords(12)));

        plan.IsAcceptable.Should().BeTrue("cắt giữa câu là khuyết điểm chất lượng, không phải lỗi chặn job");
        plan.HasMidSentenceCut.Should().BeTrue();
        plan.Warnings.Should().Contain(w => w.Contains("Không tìm thấy khoảng lặng"));
        plan.Warnings.Should().Contain(w => w.Contains("cắt giữa câu"));
        plan.Shots.Should().HaveCount(2);
        plan.Shots[0].VideoDurationSeconds.Should().Be(8);
        plan.Shots[1].VideoDurationSeconds.Should().Be(4);
    }

    [Fact]
    public void Khong_co_moc_thoi_gian_thi_chia_deu_va_canh_bao_ro_la_do_TTS()
    {
        var plan = TimelineLocker.Lock(Request(20));

        plan.IsAcceptable.Should().BeTrue();
        plan.Shots.Should().HaveCount(3, "20 giây chia đều theo bậc 8 là 3 đoạn");
        plan.HasMidSentenceCut.Should().BeTrue();
        plan.Warnings.Should().Contain(w => w.Contains("TTS provider không trả timestamps"));
        plan.Shots[^1].NarrationEndSeconds.Should().Be(
            20, "đoạn cuối phải chạm đúng cuối track, không được làm tròn hụt");
    }

    [Fact]
    public void Qua_nhieu_shot_thi_tu_choi_kem_goi_y_sua_duoc()
    {
        var plan = TimelineLocker.Lock(Request(20, maxShots: 2));

        plan.IsAcceptable.Should().BeFalse();
        plan.Reasons.Should().ContainSingle().Which.Should()
            .Contain("vượt trần 2").And
            .Contain("Rút ngắn lời thoại");

        // Cảnh báo thu được trước lúc từ chối vẫn phải giữ: nó nói vì sao phải chia nhiều shot.
        plan.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void Doan_dai_hon_bac_lon_nhat_bi_tu_choi_chu_khong_bi_cat_bot()
    {
        var plan = TimelineLocker.Lock(Request(
            9,
            preset: [new NarrationSegment("dài quá", 0, 9)]));

        plan.IsAcceptable.Should().BeFalse();
        plan.Reasons.Should().ContainSingle().Which.Should().Contain("vượt bậc dài nhất");
    }

    [Fact]
    public void Moc_thoi_gian_chong_lan_bi_tu_choi()
    {
        var plan = TimelineLocker.Lock(Request(
            6,
            preset: [new NarrationSegment("a", 0, 3), new NarrationSegment("b", 5, 5)]));

        plan.IsAcceptable.Should().BeFalse();
        plan.Reasons.Should().ContainSingle().Which.Should().Contain("chồng lấn hoặc sai thứ tự");
    }

    [Fact]
    public void Preset_hop_le_duoc_dung_nguyen_khong_chia_lai()
    {
        var plan = TimelineLocker.Lock(Request(
            9,
            preset:
            [
                new NarrationSegment("mở đầu", 0, 3.5),
                new NarrationSegment("kết", 3.5, 9),
            ]));

        plan.IsAcceptable.Should().BeTrue();
        plan.Shots.Should().HaveCount(2);
        plan.Shots[0].SpokenText.Should().Be("mở đầu");
        plan.Shots[0].VideoDurationSeconds.Should().Be(4);
        plan.Shots[1].VideoDurationSeconds.Should().Be(6);
        plan.Shots[1].VideoStartSeconds.Should().Be(4);
        plan.TotalVideoSeconds.Should().Be(10);
    }

    [Fact]
    public void ContinuousAudio_giu_thoai_o_vi_tri_goc_va_offset_tich_luy()
    {
        var plan = TimelineLocker.Lock(Request(
            7.6,
            layout: AudioLayoutStrategy.ContinuousAudio,
            maxLipSync: 0.5,
            preset:
            [
                new NarrationSegment("một", 0, 3.8),
                new NarrationSegment("hai", 3.8, 7.6),
            ]));

        plan.IsAcceptable.Should().BeTrue();
        plan.Shots[0].LipSyncOffsetSeconds.Should().Be(0);
        plan.Shots[0].AudioStartInTimelineSeconds.Should().Be(0);

        // Shot 1 bắt đầu ở giây thứ 4 (bậc lưới) nhưng thoại của nó vang từ giây 3,8 → lệch 0,2s.
        plan.Shots[1].AudioStartInTimelineSeconds.Should().Be(3.8);
        plan.Shots[1].LipSyncOffsetSeconds.Should().Be(0.2);
        plan.MaxLipSyncOffsetSeconds.Should().Be(0.2);
    }

    [Fact]
    public void ContinuousAudio_vuot_nguong_200ms_thi_tu_choi_va_chi_ro_cach_sua()
    {
        var plan = TimelineLocker.Lock(Request(
            9,
            layout: AudioLayoutStrategy.ContinuousAudio,
            preset:
            [
                new NarrationSegment("một", 0, 3),
                new NarrationSegment("hai", 3, 9),
            ]));

        plan.IsAcceptable.Should().BeFalse();
        plan.Reasons.Should().ContainSingle().Which.Should()
            .Contain("1000 ms").And
            .Contain("200 ms").And
            .Contain(nameof(AudioLayoutStrategy.PadPerShot));
    }

    [Fact]
    public void Luoi_cau_hinh_sai_thu_tu_hoac_trung_lap_van_dung_duoc()
    {
        // Lưới nhập tay trong DB: lộn xộn, có số trùng và một số vô nghĩa.
        var plan = TimelineLocker.Lock(Request(4.8, grid: [8, 4, 6, 4, 0, -2]));

        plan.IsAcceptable.Should().BeTrue();
        plan.Shots.Should().ContainSingle().Which.VideoDurationSeconds.Should().Be(6);
    }

    [Fact]
    public void Luoi_toan_so_vo_nghia_khong_lam_sap_nhung_cho_ra_ket_qua_vo_nghia()
    {
        // Ghi lại hành vi hiện tại, không phải hành vi mong muốn: lưới không còn giá trị dương
        // nào thì NormalizeGrid trả lại lưới gốc. Chặn ca này là việc của lớp nạp capability từ
        // DB (nơi biết provider nào đang cấu hình sai), không phải của hàm thuần tính toán.
        var plan = TimelineLocker.Lock(Request(0.04, grid: [-4, 0]));

        plan.IsAcceptable.Should().BeTrue();
        plan.Shots.Should().ContainSingle().Which.VideoDurationSeconds.Should().Be(0);
    }

    [Fact]
    public void ShotPlan_tu_tinh_duoc_do_dai_thoai_va_phan_du()
    {
        var plan = TimelineLocker.Lock(Request(4.8));
        var shot = plan.Shots[0];

        shot.NarrationDurationSeconds.Should().BeApproximately(4.8, 1e-9);
        shot.PaddingSeconds.Should().BeApproximately(1.2, 1e-9);
    }

    [Fact]
    public void Reject_tra_ve_plan_rong_va_giu_canh_bao()
    {
        var plan = TimelinePlan.Reject(["lý do"], ["cảnh báo"]);

        plan.IsAcceptable.Should().BeFalse();
        plan.Shots.Should().BeEmpty();
        plan.TotalVideoSeconds.Should().Be(0);
        plan.TotalNarrationSeconds.Should().Be(0);
        plan.MaxLipSyncOffsetSeconds.Should().Be(0);
        plan.Warnings.Should().ContainSingle();
    }

    [Fact]
    public void Reject_khong_kem_canh_bao_thi_danh_sach_rong_chu_khong_null()
    {
        TimelinePlan.Reject(["lý do"]).Warnings.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Tu_co_do_dai_am_hoac_bang_0_bi_bo_qua()
    {
        // TTS trả về mốc hỏng cho một từ (end ≤ start). Bỏ qua từ đó còn hơn để nó làm lệch
        // toàn bộ phép chia — nhưng phần thoại vẫn phải được chia theo các từ còn lại.
        List<WordTiming> words =
        [
            new WordTiming("hong", 2, 2),
            .. Caps.ContiguousWords(12),
        ];

        var plan = TimelineLocker.Lock(Request(12, words: words));

        plan.IsAcceptable.Should().BeTrue();
        plan.Shots.Should().HaveCount(2);
        plan.Shots[0].SpokenText.Should().NotContain("hong");
    }

    [Fact]
    public void WordTiming_tu_tinh_do_dai()
    {
        new WordTiming("xin", 1.25, 1.75).DurationSeconds.Should().BeApproximately(0.5, 1e-9);
    }
}
