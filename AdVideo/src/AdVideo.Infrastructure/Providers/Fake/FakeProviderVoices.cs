using AdVideo.Core.Providers;

namespace AdVideo.Infrastructure.Providers.Fake;

/// <summary>
/// Thư viện giọng + clone giả, đi cùng <see cref="FakeTtsProvider"/> — để chạy trọn luồng "clone giọng
/// rồi tạo video" mà không tốn đồng nào. Engine TTS giả bỏ qua voice id nên giọng nào cũng như nhau.
/// </summary>
public sealed class FakeProviderVoices : IProviderVoices
{
    private static readonly IReadOnlyList<ProviderVoice> Library =
    [
        new("fake-nu-bac", "Nữ miền Bắc (giả lập)", "premade", "Giọng mẫu cho môi trường dev", null, new Dictionary<string, string> { ["gender"] = "female", ["accent"] = "north" }),
        new("fake-nam-nam", "Nam miền Nam (giả lập)", "premade", "Giọng mẫu cho môi trường dev", null, new Dictionary<string, string> { ["gender"] = "male", ["accent"] = "south" }),
    ];

    public string Name => ProviderNames.Fake;

    public bool SupportsCloning => true;

    /// <summary>Id các giọng đã "xoá" — để test kiểm được lệnh xoá có đi tới engine.</summary>
    public List<string> Deleted { get; } = [];

    public Task<IReadOnlyList<ProviderVoice>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(Library);

    public Task<VoiceCloneResult> CloneAsync(
        string name, string? description, IReadOnlyList<VoiceSample> samples, CancellationToken cancellationToken = default) =>
        Task.FromResult(samples.Count == 0
            ? VoiceCloneResult.Fail("Không có mẫu ghi âm.")
            : new VoiceCloneResult(true, $"fake-clone-{Guid.NewGuid():N}", null));

    public Task<bool> DeleteAsync(string voiceId, CancellationToken cancellationToken = default)
    {
        lock (Deleted)
        {
            Deleted.Add(voiceId);
        }

        return Task.FromResult(true);
    }
}
