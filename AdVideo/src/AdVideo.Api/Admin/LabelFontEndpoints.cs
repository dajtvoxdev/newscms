using System.Security.Cryptography;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Media;
using AdVideo.Core.Storage;
using Microsoft.AspNetCore.Mvc;

namespace AdVideo.Api.Admin;

/// <summary>
/// Font vẽ nhãn AI: tải lên MinIO qua API thay vì đặt đường dẫn file trong cấu hình máy chủ.
/// </summary>
/// <remarks>
/// <para>
/// <b>Khoá object theo SHA-256 nội dung</b> (<c>fonts/{sha256}.{ext}</c>). Hai hệ quả có chủ đích:
/// tải lại cùng một font là no-op, và worker cache font trên đĩa theo đúng tên đó nên không bao giờ
/// phải hỏi "bản trong cache có còn mới không" — nội dung đổi thì tên đổi.
/// </para>
/// <para>
/// <b>Kiểm trước khi ghi setting.</b> Setting <see cref="SettingKeys.AiLabelFontObjectKey"/> chỉ
/// được ghi ở đây, sau khi <see cref="FontInspector"/> xác nhận file là font và có đủ glyph tiếng
/// Việt lẫn chữ nhãn đang dùng. Worker tin khoá này mà không kiểm lại.
/// </para>
/// </remarks>
public static class LabelFontEndpoints
{
    /// <summary>Trần kích thước font. Font CJK đầy đủ cũng chỉ ~20 MB; lớn hơn là nhầm file.</summary>
    public const long MaxFontBytes = 25L * 1024 * 1024;

    public static RouteGroupBuilder MapLabelFontEndpoints(this RouteGroupBuilder admin)
    {
        RouteGroupBuilder group = admin.MapGroup("/assets/label-font");

        group.MapGet("/", async (ISettingsStore settings, CancellationToken ct) =>
        {
            string? key = await settings.GetStringAsync(SettingKeys.AiLabelFontObjectKey, ct);

            return Results.Ok(string.IsNullOrWhiteSpace(key)
                ? new LabelFontView("config", null, null, null, null, [])
                : new LabelFontView("minio", key, ShaFromKey(key), null, Path.GetExtension(key).TrimStart('.'), []));
        })
        .WithSummary("Font nhãn AI đang dùng: đã tải lên (minio) hay font trong cấu hình máy chạy worker (config).");

        group.MapPost("/", async (
            HttpContext http,
            IFormFile? file,
            IStorageService storage,
            ISettingsStore settings,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return ApiProblem.Unprocessable(http, "Thiếu file font", ["Gửi multipart/form-data với trường \"file\" là file .ttf/.otf/.ttc."]);
            }

            if (file.Length > MaxFontBytes)
            {
                return ApiProblem.Unprocessable(http, "Font quá lớn", [$"Font nặng {file.Length / 1024 / 1024} MB, tối đa {MaxFontBytes / 1024 / 1024} MB."]);
            }

            byte[] bytes;

            using (var buffer = new MemoryStream((int)file.Length))
            {
                await file.CopyToAsync(buffer, ct);
                bytes = buffer.ToArray();
            }

            // Kiểm với cả chữ nhãn ĐANG dùng: một chữ nhãn có ký tự ngoài bảng chữ cái (dấu ©, ký
            // hiệu) mà font không có thì nhãn vẫn ra ô vuông dù font đọc tiếng Việt tốt.
            string labelText = await settings.GetStringAsync(SettingKeys.AiLabelOverlayText, ct) ?? AiLabelStamper.DefaultOverlayText;
            FontInspection inspection = FontInspector.Inspect(bytes, labelText);

            if (!inspection.IsUsable)
            {
                return ApiProblem.Create(
                    http,
                    StatusCodes.Status422UnprocessableEntity,
                    "Font không dùng được để vẽ nhãn AI",
                    inspection.Problem,
                    new Dictionary<string, object?> { ["missing_characters"] = inspection.MissingCharacters });
            }

            string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string objectKey = $"fonts/{sha256}.{inspection.Format}";

            if (!await storage.ExistsAsync(Buckets.System, objectKey, ct))
            {
                using var content = new MemoryStream(bytes, writable: false);
                await storage.UploadAsync(Buckets.System, objectKey, content, "font/" + inspection.Format, ct);
            }

            string? previous = await settings.GetStringAsync(SettingKeys.AiLabelFontObjectKey, ct);

            await settings.SetAsync(
                SettingKeys.AiLabelFontObjectKey,
                objectKey,
                SettingValueType.String,
                $"Font vẽ nhãn AI trong bucket {Buckets.System}. Tải lên qua API quản trị từ file \"{Path.GetFileName(file.FileName)}\". Không sửa tay.",
                isProvisional: false,
                ct);

            loggerFactory.CreateLogger(typeof(LabelFontEndpoints)).LogInformation(
                "Đổi font nhãn AI: {Previous} → {ObjectKey} ({Bytes} byte, bởi {Operator}).",
                string.IsNullOrWhiteSpace(previous) ? "(font cấu hình máy)" : previous,
                objectKey,
                bytes.LongLength,
                http.User.Identity?.Name);

            return Results.Ok(new LabelFontView("minio", objectKey, sha256, bytes.LongLength, inspection.Format, []));
        })
        // API xác thực bằng header, không bằng cookie, nên không có gì để CSRF lợi dụng — mà thiếu
        // dòng này thì .NET 8 đòi middleware antiforgery cho mọi endpoint nhận form.
        .DisableAntiforgery()
        .WithSummary("Tải font lên (multipart, trường \"file\"). Kiểm định dạng + glyph tiếng Việt rồi mới dùng.");

        group.MapDelete("/", async (ISettingsStore settings, CancellationToken ct) =>
        {
            await settings.SetAsync(
                SettingKeys.AiLabelFontObjectKey,
                string.Empty,
                SettingValueType.String,
                $"Font vẽ nhãn AI trong bucket {Buckets.System}. Rỗng = dùng AdVideo:Ffmpeg:FontFile. Không sửa tay.",
                isProvisional: false,
                ct);

            return Results.NoContent();
        })
        .WithSummary("Bỏ font đã tải lên, quay về AdVideo:Ffmpeg:FontFile. File trong MinIO giữ nguyên để quay lại được.");

        return group;
    }

    private static string? ShaFromKey(string key)
    {
        string name = Path.GetFileNameWithoutExtension(key);

        return name.Length == 64 ? name : null;
    }
}
