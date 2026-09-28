using System.Globalization;
using System.Text.Json;
using AdVideo.Core.Entities;

namespace AdVideo.Core.Configuration;

/// <summary>
/// Kiểm một giá trị mới cho <see cref="SystemSetting"/> trước khi ghi — dùng chung cho CLI và API quản trị.
/// </summary>
/// <remarks>
/// <para>
/// <b>Kiểm lúc GHI, không chỉ lúc đọc.</b> <c>DbSettingsStore</c> gặp giá trị không đọc được thì
/// log cảnh báo rồi dùng fallback — đúng cho lúc đọc (một lỗi gõ không đáng làm chết job đang chạy),
/// nhưng nghĩa là một giá trị sai lọt được vào DB sẽ bị lờ đi trong im lặng. Chặn ở cửa ghi thì
/// người sửa thấy lỗi ngay trên màn hình của mình.
/// </para>
/// <para>
/// Số đọc theo <see cref="CultureInfo.InvariantCulture"/>, giống <c>DbSettingsStore</c>: máy chủ
/// chạy culture vi-VN thì "0.5" theo culture máy là 5.
/// </para>
/// </remarks>
public static class SettingValueValidator
{
    /// <summary>Độ dài tối đa của chữ nhãn AI — dài hơn thì tràn một dòng trên khung dọc 1080 px.</summary>
    public const int MaxAiLabelLength = 60;

    /// <summary>Trả về lý do từ chối, hoặc null nếu giá trị hợp lệ.</summary>
    public static string? Validate(SystemSetting setting, string? value)
    {
        ArgumentNullException.ThrowIfNull(setting);

        if (value is null)
        {
            return $"Thiếu giá trị cho {setting.Key}.";
        }

        string? typeProblem = setting.ValueType switch
        {
            SettingValueType.Int => CheckNumber(setting, value, integer: true),
            SettingValueType.Decimal => CheckNumber(setting, value, integer: false),
            SettingValueType.Bool => value is "true" or "false" or "1" or "0"
                ? null
                : $"{setting.Key} nhận true/false, không nhận \"{value}\".",
            SettingValueType.Json => CheckJson(setting, value),
            _ => null,
        };

        return typeProblem ?? CheckKeySpecific(setting.Key, value);
    }

    private static string? CheckNumber(SystemSetting setting, string value, bool integer)
    {
        // KHÔNG cho dấu phân cách nghìn: người gõ quen kiểu Việt viết "0,5" để chỉ nửa đô, và
        // NumberStyles.Number sẽ đọc thành 5 — trần chi phí lệch 10 lần mà không ai được báo.
        NumberStyles style = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign
            | (integer ? NumberStyles.None : NumberStyles.AllowDecimalPoint);

        if (!decimal.TryParse(value, style, CultureInfo.InvariantCulture, out decimal parsed))
        {
            return integer
                ? $"\"{value}\" không phải số nguyên, trong khi {setting.Key} có kiểu Int."
                : $"\"{value}\" không phải số hợp lệ cho {setting.Key}: dấu thập phân là dấu chấm (0.5), không dùng dấu phẩy.";
        }

        if (decimal.TryParse(setting.MinValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal min) && parsed < min)
        {
            return $"{setting.Key} phải ≥ {min.ToString(CultureInfo.InvariantCulture)}. {setting.Description}";
        }

        if (decimal.TryParse(setting.MaxValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal max) && parsed > max)
        {
            return $"{setting.Key} phải ≤ {max.ToString(CultureInfo.InvariantCulture)}. {setting.Description}";
        }

        return null;
    }

    private static string? CheckJson(SystemSetting setting, string value)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(value);

            return null;
        }
        catch (JsonException ex)
        {
            return $"{setting.Key} phải là JSON hợp lệ: {ex.Message}";
        }
    }

    private static string? CheckKeySpecific(string key, string value) => key switch
    {
        // D9: đổi được chữ, không tắt được nhãn. Chuỗi rỗng ở đây chính là "tắt nhãn" đội lốt.
        SettingKeys.AiLabelOverlayText when string.IsNullOrWhiteSpace(value) =>
            "Chữ nhãn AI không được để trống. Nhãn là nghĩa vụ pháp lý của bên triển khai (Luật TTNT 2025) và không có cách tắt.",

        SettingKeys.AiLabelOverlayText when value.Trim().Length > MaxAiLabelLength =>
            $"Chữ nhãn AI dài {value.Trim().Length} ký tự, tối đa {MaxAiLabelLength} — dài hơn thì tràn dòng trên khung dọc.",

        // Font chỉ đổi qua endpoint upload, là nơi kiểm file có đúng là font. Sửa tay khoá này là
        // trỏ worker tới một object chưa ai kiểm.
        SettingKeys.AiLabelFontObjectKey =>
            "Không sửa trực tiếp. Tải font lên bằng POST /v1/admin/assets/label-font — endpoint đó kiểm file rồi mới ghi khoá này.",

        _ => null,
    };
}
