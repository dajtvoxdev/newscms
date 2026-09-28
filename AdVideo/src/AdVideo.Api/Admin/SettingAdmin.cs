using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Media;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Api.Admin;

/// <summary>Kết quả sửa setting.</summary>
public sealed record SettingUpdateResult(SystemSetting? Setting, string? PreviousValue, string? Error, bool NotFound);

/// <summary>
/// Sửa <see cref="SystemSetting"/> — dùng chung cho <c>set-setting</c> và <c>PUT /v1/admin/settings/{key}</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chỉ sửa khoá đã có.</b> Khoá mới sinh ra từ code (<c>SettingKeys</c> + seeder), không từ màn
/// hình: một khoá gõ nhầm tên được tạo ra sẽ không có code nào đọc, và người sửa tưởng mình đã đổi
/// cấu hình.
/// </para>
/// <para>
/// Ghi qua <see cref="ISettingsStore"/> để cache của mọi tiến trình (API lẫn Worker) được làm hỏng
/// — đổi setting không bao giờ đòi restart.
/// </para>
/// </remarks>
public sealed class SettingAdmin
{
    private readonly AdVideoDbContext _db;
    private readonly ISettingsStore _store;
    private readonly IStorageService _storage;

    public SettingAdmin(AdVideoDbContext db, ISettingsStore store, IStorageService storage)
    {
        _db = db;
        _store = store;
        _storage = storage;
    }

    /// <param name="isProvisional">
    /// Null = coi là đã chốt (false): người vận hành gõ một giá trị là đang quyết định nó. Chỉ truyền
    /// true khi cố ý ghi một con số vẫn đang đoán.
    /// </param>
    public async Task<SettingUpdateResult> UpdateAsync(
        string key, string? value, bool? isProvisional, CancellationToken cancellationToken = default)
    {
        SystemSetting? setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting is null)
        {
            return new SettingUpdateResult(null, null, $"Không có setting \"{key}\".", NotFound: true);
        }

        string? problem = SettingValueValidator.Validate(setting, value);

        if (problem is not null)
        {
            return new SettingUpdateResult(setting, setting.Value, problem, NotFound: false);
        }

        string normalized = setting.Key == SettingKeys.AiLabelOverlayText ? value!.Trim() : value!;

        if (setting.Key == SettingKeys.AiLabelOverlayText
            && await CheckLabelFontCoversAsync(normalized, cancellationToken) is { } glyphProblem)
        {
            return new SettingUpdateResult(setting, setting.Value, glyphProblem, NotFound: false);
        }

        await _store.SetAsync(
            key, normalized, setting.ValueType, setting.Description, isProvisional ?? false, cancellationToken);

        SystemSetting updated = await _db.SystemSettings
            .AsNoTracking()
            .SingleAsync(s => s.Key == key, cancellationToken);

        return new SettingUpdateResult(updated, setting.Value, null, NotFound: false);
    }

    /// <summary>
    /// Chữ nhãn mới có vẽ được bằng font đã tải lên không. Null = được (hoặc đang dùng font cấu hình máy).
    /// </summary>
    /// <remarks>
    /// Font cấu hình máy (<c>AdVideo:Ffmpeg:FontFile</c>) nằm trên máy chạy worker, API không đọc được
    /// — lúc đó chỉ còn QC bước 9 và mắt người. Đây là thêm một lý do để tải font lên qua API.
    /// </remarks>
    private async Task<string?> CheckLabelFontCoversAsync(string labelText, CancellationToken cancellationToken)
    {
        string? fontKey = await _store.GetStringAsync(SettingKeys.AiLabelFontObjectKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(fontKey))
        {
            return null;
        }

        byte[] font = await _storage.DownloadBytesAsync(Buckets.System, fontKey, cancellationToken);
        FontInspection inspection = FontInspector.Inspect(font, labelText);

        return inspection.IsUsable
            ? null
            : $"Font nhãn đang dùng ({fontKey}) không vẽ được chữ này: {inspection.Problem}";
    }
}
