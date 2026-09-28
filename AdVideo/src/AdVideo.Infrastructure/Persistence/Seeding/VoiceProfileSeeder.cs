using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Infrastructure.Persistence.Seeding;

/// <summary>
/// Giọng có sẵn cho engine TTS giả — để môi trường dev và test có danh sách giọng để chọn.
/// </summary>
/// <remarks>
/// <b>Không seed giọng thật nào.</b> Voice id của ElevenLabs phụ thuộc tài khoản (và giọng "Default"
/// của họ ngừng phục vụ cuối 2026); người vận hành nhập từ thư viện của chính tài khoản mình ở trang
/// Giọng đọc. Giọng giả không lọt ra production: danh sách chỉ hiện giọng của engine đang bật, và
/// engine giả bị cấm bật trên Production.
/// </remarks>
public sealed class VoiceProfileSeeder
{
    private readonly AdVideoDbContext _db;

    public VoiceProfileSeeder(AdVideoDbContext db) => _db = db;

    public static IReadOnlyList<(string VoiceId, string Name, string Description)> FakePresets { get; } =
    [
        ("fake-nu-bac", "Nữ · miền Bắc (giả lập)", "Giọng mẫu của engine giả — dev/test."),
        ("fake-nam-nam", "Nam · miền Nam (giả lập)", "Giọng mẫu của engine giả — dev/test."),
    ];

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        HashSet<string> existing = (await _db.VoiceProfiles
            .IgnoreQueryFilters()
            .Where(v => v.Provider == ProviderNames.Fake && v.TenantId == null)
            .Select(v => v.ProviderVoiceId)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        int added = 0;

        for (int i = 0; i < FakePresets.Count; i++)
        {
            (string voiceId, string name, string description) = FakePresets[i];

            if (existing.Contains(voiceId))
            {
                continue;
            }

            _db.VoiceProfiles.Add(new VoiceProfile
            {
                Name = name,
                Description = description,
                Provider = ProviderNames.Fake,
                ProviderVoiceId = voiceId,
                Kind = VoiceProfileKind.Preset,
                SortOrder = 1000 + i,
            });

            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return added;
    }
}
