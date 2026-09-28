namespace AdVideo.Core.Providers;

/// <summary>Một giọng trong thư viện của engine TTS (để người vận hành chọn làm giọng có sẵn).</summary>
public sealed record ProviderVoice(
    string VoiceId,
    string Name,
    string? Category,
    string? Description,
    string? PreviewUrl,
    IReadOnlyDictionary<string, string> Labels);

/// <summary>Một file mẫu ghi âm gửi đi clone.</summary>
public sealed record VoiceSample(string FileName, string ContentType, byte[] Content);

/// <summary>Kết quả clone. <see cref="VoiceId"/> là id bên engine.</summary>
public sealed record VoiceCloneResult(bool IsSuccess, string? VoiceId, string? FailureReason, string? RawError = null)
{
    public static VoiceCloneResult Fail(string reason, string? raw = null) => new(false, null, reason, raw);
}

/// <summary>
/// Quản lý giọng phía engine TTS: liệt kê thư viện, clone từ mẫu, xoá.
/// </summary>
/// <remarks>
/// Tách khỏi <see cref="ITtsProvider"/>: đọc văn bản là việc của mọi engine, còn thư viện giọng và
/// clone thì chỉ vài engine có. Engine không có thì registry trả null, và API nói rõ "engine này
/// không clone được" thay vì một lỗi 404 từ phía nhà cung cấp.
/// </remarks>
public interface IProviderVoices
{
    string Name { get; }

    bool SupportsCloning { get; }

    Task<IReadOnlyList<ProviderVoice>> ListAsync(CancellationToken cancellationToken = default);

    Task<VoiceCloneResult> CloneAsync(
        string name, string? description, IReadOnlyList<VoiceSample> samples, CancellationToken cancellationToken = default);

    /// <summary>Xoá giọng bên engine. False = không xoá được (đã mất, hoặc lỗi) — bên gọi vẫn xoá hồ sơ.</summary>
    Task<bool> DeleteAsync(string voiceId, CancellationToken cancellationToken = default);
}
