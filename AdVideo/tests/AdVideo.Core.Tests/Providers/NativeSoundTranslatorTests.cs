using AdVideo.Core.Providers;
using AdVideo.Core.Tests.TestData;
using FluentAssertions;

namespace AdVideo.Core.Tests.Providers;

/// <summary>
/// D3 — tắt THOẠI của model, không nhất thiết tắt TIẾNG ĐỘNG.
/// </summary>
/// <remarks>
/// Lỗi mà bộ test này canh: video ra có hai lớp tiếng chồng nhau. Nó không làm job fail, không
/// hiện trong log, và người xem chỉ nói được là "nghe kỳ kỳ" — nên nếu không chốt bằng test thì
/// sẽ chốt bằng một khách hàng khó chịu.
/// </remarks>
public class NativeSoundTranslatorTests
{
    [Fact]
    public void Nem_khi_thieu_capability()
    {
        Action act = () => NativeSoundTranslator.Decide(null!, hasVoiceOver: true, clientWantsSoundEffects: true);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Provider_khong_sinh_tieng_thi_khong_phai_quyet_dinh_gi()
    {
        var decision = NativeSoundTranslator.Decide(
            Caps.Video(generatesNativeAudio: false), hasVoiceOver: true, clientWantsSoundEffects: true);

        decision.ShouldRequestSuppression.Should().BeFalse();
        decision.MustStripInCompose.Should().BeFalse();
        decision.KeepSfxAtDb.Should().BeNull();
        decision.Reason.Should().Contain("không sinh track tiếng");
    }

    [Fact]
    public void Khong_co_voice_over_va_khach_muon_tieng_dong_thi_giu_o_muc_chuan()
    {
        var decision = NativeSoundTranslator.Decide(
            Caps.Video(generatesNativeAudio: true), hasVoiceOver: false, clientWantsSoundEffects: true);

        decision.ShouldRequestSuppression.Should().BeFalse();
        decision.MustStripInCompose.Should().BeFalse();
        decision.KeepSfxAtDb.Should().Be(NativeSoundTranslator.SfxLevelDb).And.Be(-18);
    }

    [Fact]
    public void Khong_co_voice_over_va_khach_tat_tieng_dong_thi_vua_xin_tat_vua_strip()
    {
        var decision = NativeSoundTranslator.Decide(
            Caps.Video(generatesNativeAudio: true), hasVoiceOver: false, clientWantsSoundEffects: false);

        decision.ShouldRequestSuppression.Should().BeTrue();
        decision.MustStripInCompose.Should().BeTrue();
        decision.KeepSfxAtDb.Should().BeNull();
    }

    [Fact]
    public void Tach_duoc_sfx_khoi_thoai_thi_tat_thoai_nhung_giu_tieng_dong()
    {
        var decision = NativeSoundTranslator.Decide(
            Caps.Video(generatesNativeAudio: true, canSeparateSfx: true),
            hasVoiceOver: true,
            clientWantsSoundEffects: true);

        decision.ShouldRequestSuppression.Should().BeTrue();
        decision.MustStripInCompose.Should().BeFalse("giữ được sfx thì không được bỏ cả track");
        decision.KeepSfxAtDb.Should().Be(-18);
    }

    [Fact]
    public void Tach_duoc_nhung_khach_khong_muon_tieng_dong_thi_van_strip_het()
    {
        var decision = NativeSoundTranslator.Decide(
            Caps.Video(generatesNativeAudio: true, canSeparateSfx: true),
            hasVoiceOver: true,
            clientWantsSoundEffects: false);

        decision.ShouldRequestSuppression.Should().BeTrue();
        decision.MustStripInCompose.Should().BeTrue();
        decision.KeepSfxAtDb.Should().BeNull();
    }

    [Fact]
    public void Track_tron_san_thi_gui_tham_so_tat_van_chua_du()
    {
        var decision = NativeSoundTranslator.Decide(
            Caps.Video(generatesNativeAudio: true, canSeparateSfx: false),
            hasVoiceOver: true,
            clientWantsSoundEffects: true);

        decision.ShouldRequestSuppression.Should().BeTrue();
        decision.MustStripInCompose.Should().BeTrue(
            "tham số đã gửi không phải bằng chứng provider đã tuân thủ");
        decision.KeepSfxAtDb.Should().BeNull();
        decision.Reason.Should().Contain("trộn sẵn");
    }

    [Fact]
    public void Provider_mac_dinh_bat_tieng_thi_ly_do_phai_noi_thang_ra_dieu_do()
    {
        // Vidu mặc định BẬT âm thanh. Dòng lý do này là thứ người vận hành đọc trong log khi đi
        // tìm nguồn gốc của một video hai lớp tiếng.
        var decision = NativeSoundTranslator.Decide(
            Caps.Video(provider: ProviderNames.Vidu, generatesNativeAudio: true, nativeAudioOn: true),
            hasVoiceOver: true,
            clientWantsSoundEffects: true);

        decision.MustStripInCompose.Should().BeTrue();
        decision.Reason.Should().Contain("MẶC ĐỊNH BẬT").And.Contain("chỉ gửi tham số tắt là không đủ");
    }
}
