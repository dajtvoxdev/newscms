using AdVideo.Core.Enums;

namespace AdVideo.Core.Providers;

/// <summary>
/// Quyết định xử lý track tiếng do provider sinh ra. Tách thành lớp riêng vì đây là chỗ dễ
/// sai nhất mà hậu quả lại khó lần ra nhất: video có hai lớp tiếng chồng nhau thì người xem
/// chỉ thấy "tiếng kỳ kỳ", không ai chỉ ra được vì sao.
/// </summary>
/// <param name="ShouldRequestSuppression">Gửi tham số yêu cầu provider tắt tiếng.</param>
/// <param name="MustStripInCompose">
/// FFmpeg bước 8 có phải bỏ track tiếng gốc không. <b>Không suy ra được từ
/// <see cref="ShouldRequestSuppression"/></b>: tham số đã gửi không phải bằng chứng provider
/// đã tuân thủ. Kiểm chứng bằng ffprobe rồi mới quyết định.
/// </param>
/// <param name="KeepSfxAtDb">
/// Nếu provider tách được sfx khỏi thoại thì giữ sfx ở mức này (−18 dB theo thiết kế).
/// Null nghĩa là bỏ hết.
/// </param>
/// <param name="Reason">Giải thích cho log. Không trả ra API.</param>
public sealed record NativeSoundDecision(
    bool ShouldRequestSuppression,
    bool MustStripInCompose,
    int? KeepSfxAtDb,
    string Reason);

/// <summary>
/// Suy ra cách xử lý tiếng gốc từ capability của provider và ngữ cảnh job.
/// </summary>
/// <remarks>
/// <para>
/// Lớp này tồn tại vì hai sự thật khó chịu: (1) <b>Vidu mặc định BẬT âm thanh</b>, và
/// (2) tham số tắt mà mình gửi đi không phải là bằng chứng provider đã tắt. Cả hai đều dẫn tới
/// cùng một lỗi đầu ra — hai lớp tiếng — nếu xử lý bằng cách "gửi tham số tắt rồi tin là được".
/// </para>
/// <para>
 /// Thuần hàm, không trạng thái, không I/O: mọi nhánh đều test được mà không cần mock.
/// </para>
/// </remarks>
public static class NativeSoundTranslator
{
    /// <summary>Mức âm lượng tiếng động gốc khi được giữ. Theo thiết kế bước 8: −18 dB.</summary>
    public const int SfxLevelDb = -18;

    public static NativeSoundDecision Decide(
        VideoProviderCapability capability,
        bool hasVoiceOver,
        bool clientWantsSoundEffects)
    {
        ArgumentNullException.ThrowIfNull(capability);

        // 1. Provider không sinh tiếng → không có gì phải quyết định.
        if (!capability.GeneratesNativeAudio)
        {
            return new NativeSoundDecision(
                ShouldRequestSuppression: false,
                MustStripInCompose: false,
                KeepSfxAtDb: null,
                $"{capability.Provider} không sinh track tiếng; voice-over là lớp âm thanh duy nhất.");
        }

        // 2. Không có voice-over → tiếng gốc là thứ duy nhất người xem nghe.
        if (!hasVoiceOver)
        {
            return clientWantsSoundEffects
                ? new NativeSoundDecision(
                    ShouldRequestSuppression: false,
                    MustStripInCompose: false,
                    KeepSfxAtDb: SfxLevelDb,
                    "Không có voice-over nên giữ tiếng gốc. Vẫn hạ về −18 dB cho nhất quán với video có voice-over.")
                : new NativeSoundDecision(
                    ShouldRequestSuppression: true,
                    MustStripInCompose: true,
                    KeepSfxAtDb: null,
                    "Khách tắt tiếng động: yêu cầu provider không sinh tiếng và strip khi compose.");
        }

        // 3. Có voice-over. Đây là nhánh quan trọng nhất.
        //    D3: tắt THOẠI của model, không nhất thiết tắt TIẾNG ĐỘNG.
        if (capability.CanSeparateSfxFromSpeech && clientWantsSoundEffects)
        {
            return new NativeSoundDecision(
                ShouldRequestSuppression: true,
                MustStripInCompose: false,
                KeepSfxAtDb: SfxLevelDb,
                $"{capability.Provider} tách được sfx khỏi thoại: tắt thoại của model, giữ tiếng động ở {SfxLevelDb} dB.");
        }

        // 4. Có voice-over nhưng KHÔNG tách được → buộc phải bỏ hết track gốc.
        //    Không thể giữ sfx mà bỏ thoại khi chúng nằm trong cùng một track.
        var reason = capability.NativeAudioEnabledByDefault
            ? $"{capability.Provider} MẶC ĐỊNH BẬT tiếng và trả về track trộn sẵn không tách được thoại/sfx. " +
              "Phải tắt tường minh VÀ strip khi compose — chỉ gửi tham số tắt là không đủ."
            : $"{capability.Provider} trả về track trộn sẵn không tách được thoại/sfx; có voice-over nên phải strip toàn bộ.";

        return new NativeSoundDecision(
            ShouldRequestSuppression: true,
            MustStripInCompose: true,
            KeepSfxAtDb: null,
            reason);
    }
}
