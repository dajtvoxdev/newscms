using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Persistence.Seeding;

/// <summary>
/// Nạp giá trị khởi đầu cho bảng <c>SystemSettings</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chỉ thêm khoá còn thiếu, KHÔNG bao giờ ghi đè.</b> Người vận hành chỉnh một con số lúc
/// nửa đêm để cứu sự cố, rồi lần deploy sau seeder trả nó về mặc định là kiểu hỏng tồi tệ nhất:
/// hệ thống tự quay lại trạng thái đã biết là sai, và không ai nhớ vì sao.
/// </para>
/// <para>
/// <b>Vì sao gần hết đều <c>IsProvisional = true</c>:</b> Sprint 0 (đo thật bằng key thật) bị bỏ
/// qua vì chưa có API key và ngân sách. Những số này là phỏng đoán bảo toàn để hệ thống chạy
/// được, không phải số đã đo. Cờ provisional là danh sách việc phải làm lại khi có key —
/// <c>ISettingsStore.GetProvisionalAsync</c> đọc đúng danh sách đó.
/// </para>
/// </remarks>
public sealed class SystemSettingSeeder
{
    private readonly AdVideoDbContext _db;
    private readonly ILogger<SystemSettingSeeder> _logger;

    public SystemSettingSeeder(AdVideoDbContext db, ILogger<SystemSettingSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Nạp các khoá còn thiếu. Trả về số khoá vừa thêm.</summary>
    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: một khoá đã xoá mềm vẫn chiếm chỗ trong unique index lọc theo
        // IsDeleted, nhưng quan trọng hơn là nó thể hiện một quyết định — ai đó đã cố ý bỏ khoá
        // này đi. Seeder không có quyền lật lại quyết định đó.
        HashSet<string> existing = (await _db.SystemSettings
            .IgnoreQueryFilters()
            .Select(x => x.Key)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        int added = 0;

        foreach (SettingSeed seed in Definitions)
        {
            if (existing.Contains(seed.Key))
            {
                continue;
            }

            _db.SystemSettings.Add(new SystemSetting
            {
                Key = seed.Key,
                Value = seed.Value,
                ValueType = seed.ValueType,
                Description = seed.Description,
                IsProvisional = seed.IsProvisional,
                MinValue = seed.MinValue,
                MaxValue = seed.MaxValue,
            });

            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Đã nạp {Count} setting còn thiếu vào bảng SystemSettings.", added);
        }

        return added;
    }

    /// <summary>Danh sách khoá và giá trị khởi đầu. Công khai để test đối chiếu với SettingKeys.</summary>
    public static IReadOnlyList<SettingSeed> Definitions { get; } =
    [
        new(
            SettingKeys.MaxConcurrentShots,
            "1",
            SettingValueType.Int,
            "Số shot render đồng thời trong một job. Để 1 cho tới khi đo được rate limit thật của key.",
            IsProvisional: true,
            MinValue: "1",
            MaxValue: "8"),

        // Mặc định là provider giả, KHÔNG phải Veo hay Kling: Sprint 1 chạy toàn tuyến mà không
        // tiêu một đồng nào. Đổi sang provider thật là một hành động có ý thức của người vận hành
        // sau khi đã nạp key bằng lệnh set-credential.
        new(
            SettingKeys.DefaultVideoProvider,
            ProviderNames.Fake,
            SettingValueType.String,
            "Provider video dùng khi yêu cầu không chỉ định. Đang là provider giả — đổi khi đã nạp key thật.",
            IsProvisional: true),

        new(
            SettingKeys.DraftTtsProvider,
            ProviderNames.Fake,
            SettingValueType.String,
            "Engine TTS cho tier Nháp. Đổi sang vieneu khi đã dựng được máy chủ tự host.",
            IsProvisional: true),

        new(
            SettingKeys.StandardTtsProvider,
            ProviderNames.Fake,
            SettingValueType.String,
            "Engine TTS cho tier Thành phẩm. Bắt buộc engine có mốc thời gian theo từ — đổi sang elevenlabs khi có key.",
            IsProvisional: true),

        // 600 giây vì Veo là long-running operation: gửi xong còn phải poll. Timeout ngắn hơn
        // thời gian render thật thì client bỏ cuộc trong khi provider vẫn render — và vẫn tính tiền.
        new(
            SettingKeys.VideoProviderTimeoutSeconds,
            "600",
            SettingValueType.Int,
            "Timeout một lần gọi video provider, tính cả thời gian chờ poll, đơn vị giây.",
            IsProvisional: true,
            MinValue: "60",
            MaxValue: "1800"),

        new(
            SettingKeys.ShotMaxRetries,
            "2",
            SettingValueType.Int,
            "Số lần render lại một shot khi lỗi tạm thời. Mỗi lần retry là một lần tính tiền.",
            IsProvisional: true,
            MinValue: "0",
            MaxValue: "5"),

        // Hai trần chi tiêu để thấp có chủ đích: chưa có ngân sách nên cái giá của một vòng lặp
        // retry chạy loạn phải bị chặn ở mức vài chục đô, không phải vài nghìn.
        new(
            SettingKeys.DailySystemCostLimitUsd,
            "20",
            SettingValueType.Decimal,
            "Trần chi tiêu đô la toàn hệ thống mỗi ngày. Chạm trần thì ngừng nhận job mới.",
            IsProvisional: true,
            MinValue: "0",
            MaxValue: "10000"),

        new(
            SettingKeys.MaxCostPerJobUsd,
            "2",
            SettingValueType.Decimal,
            "Trần chi tiêu đô la cho một job. Kiểm tra trước khi gọi provider, không phải sau.",
            IsProvisional: true,
            MinValue: "0",
            MaxValue: "500"),

        new(
            SettingKeys.DownloadUrlLifetimeMinutes,
            "60",
            SettingValueType.Int,
            "Hạn của presigned URL tải video về, đơn vị phút.",
            IsProvisional: false,
            MinValue: "5",
            MaxValue: "1440"),

        // −14 LUFS là chuẩn các nền tảng mạng xã hội tự chuẩn hoá về. Đây là số đã chốt ở thiết kế,
        // không phải phỏng đoán, nên không provisional.
        new(
            SettingKeys.TargetLoudnessLufs,
            "-14",
            SettingValueType.Decimal,
            "Chuẩn âm lượng đầu ra, đơn vị LUFS. Khớp với mức các nền tảng mạng xã hội chuẩn hoá về.",
            IsProvisional: false,
            MinValue: "-31",
            MaxValue: "-5"),

        new(
            SettingKeys.MaxLipSyncDriftMs,
            "200",
            SettingValueType.Int,
            "Dung sai lệch tiếng-hình tối đa, mili giây. Vượt ngưỡng này thì QC đánh trượt.",
            IsProvisional: false,
            MinValue: "40",
            MaxValue: "1000"),

        new(
            SettingKeys.GlobalNegativePromptCode,
            PromptTemplateSeeder.GlobalNegativeCode,
            SettingValueType.String,
            "Code của prompt chứa negative prompt toàn cục, tra trong bảng PromptTemplates.",
            IsProvisional: false),

        new(
            SettingKeys.ProviderSmokeTestEnabled,
            "false",
            SettingValueType.Bool,
            "Bật smoke test provider hằng ngày. Tắt ở Sprint 1 vì mỗi lần test là một lần tiêu tiền thật.",
            IsProvisional: false),

        // Rỗng có chủ đích: Luật 3 — khách không chọn provider. Mở từng tên khi cần chẩn đoán.
        new(
            SettingKeys.ForceableVideoProviders,
            "",
            SettingValueType.String,
            "Provider video khách được chỉ định qua options.provider, cách nhau bằng dấu phẩy. Rỗng = không cho ép. Provider giả không bao giờ ép được ở production.",
            IsProvisional: false),
    ];

    /// <summary>Một dòng seed. Tách record để test đọc được danh sách mà không phải chạm DB.</summary>
    public sealed record SettingSeed(
        string Key,
        string Value,
        SettingValueType ValueType,
        string Description,
        bool IsProvisional,
        string? MinValue = null,
        string? MaxValue = null);
}
