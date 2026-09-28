using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Media;
using Microsoft.Extensions.Options;

namespace AdVideo.Worker.Jobs;

/// <summary>Font đã sẵn trên đĩa cho FFmpeg, hoặc lý do không có.</summary>
/// <param name="Source"><c>minio:{khoá}</c> hoặc <c>config</c> — ghi vào log để biết video được vẽ bằng font nào.</param>
public sealed record LabelFontResolution(string? Path, string? Source, string? Problem, bool IsRetryable = false);

/// <summary>
/// Tìm file font để vẽ nhãn AI: font tải lên qua API quản trị (MinIO) trước, font trong cấu hình máy sau.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cache trên đĩa theo tên object.</b> Khoá object là <c>fonts/{sha256}.{ext}</c>, nên cùng một
/// tên là cùng một nội dung: tải một lần rồi dùng mãi, không cần hỏi "bản cache có cũ không". Người
/// vận hành đổi font thì khoá đổi, job kế tiếp tự tải bản mới.
/// </para>
/// <para>
/// <b>Tải vào file tạm rồi đổi tên.</b> Hai job trên cùng worker có thể cùng thấy cache trống; ghi
/// thẳng vào tên đích thì một job có thể đọc một file font viết dở — FFmpeg sẽ không báo gì mà vẽ
/// sai. Đổi tên trên cùng ổ đĩa là nguyên tử.
/// </para>
/// </remarks>
public sealed class LabelFontResolver
{
    private readonly ISettingsStore _settings;
    private readonly IStorageService _storage;
    private readonly FfmpegOptions _options;
    private readonly ILogger<LabelFontResolver> _logger;

    public LabelFontResolver(
        ISettingsStore settings,
        IStorageService storage,
        IOptions<FfmpegOptions> options,
        ILogger<LabelFontResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _settings = settings;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Thư mục cache font trên máy worker.</summary>
    public static string CacheDirectory => Path.Combine(Path.GetTempPath(), "advideo-fonts");

    public async Task<LabelFontResolution> ResolveAsync(CancellationToken cancellationToken = default)
    {
        string? objectKey = await _settings.GetStringAsync(SettingKeys.AiLabelFontObjectKey, cancellationToken);

        if (!string.IsNullOrWhiteSpace(objectKey))
        {
            return await FromStorageAsync(objectKey.Trim(), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(_options.FontFile) && File.Exists(_options.FontFile))
        {
            return new LabelFontResolution(_options.FontFile, "config", null);
        }

        // Chặn trước khi tải vài trăm MB clip về đĩa: thiếu font thì FFmpeg vẫn chạy, vẫn xuất ra
        // video, chỉ là chữ tiếng Việt hiện thành ô vuông và không có dòng lỗi nào.
        return new LabelFontResolution(
            null,
            null,
            "Chưa có font vẽ nhãn AI. Tải font lên bằng POST /v1/admin/assets/label-font, hoặc đặt " +
            $"{FfmpegOptions.SectionName}:FontFile trên máy chạy worker. Không có font có dấu thì nhãn " +
            "hiện thành ô vuông, mà FFmpeg không báo lỗi gì cả.");
    }

    private async Task<LabelFontResolution> FromStorageAsync(string objectKey, CancellationToken cancellationToken)
    {
        // Chỉ lấy tên file: khoá đến từ DB, và một khoá kiểu "../../x" không được phép chọn nơi ghi trên đĩa.
        string fileName = Path.GetFileName(objectKey);
        string localPath = Path.Combine(CacheDirectory, fileName);
        string source = $"minio:{objectKey}";

        if (File.Exists(localPath))
        {
            return new LabelFontResolution(localPath, source, null);
        }

        try
        {
            if (!await _storage.ExistsAsync(Buckets.System, objectKey, cancellationToken))
            {
                return new LabelFontResolution(
                    null,
                    source,
                    $"Font nhãn AI \"{objectKey}\" không còn trong bucket {Buckets.System}. Tải font lên lại, hoặc " +
                    "DELETE /v1/admin/assets/label-font để quay về font trong cấu hình máy.");
            }

            Directory.CreateDirectory(CacheDirectory);
            string temporary = Path.Combine(CacheDirectory, $"{fileName}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (Stream remote = await _storage.OpenReadAsync(Buckets.System, objectKey, cancellationToken))
                await using (FileStream local = File.Create(temporary))
                {
                    await remote.CopyToAsync(local, cancellationToken);
                }

                File.Move(temporary, localPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            _logger.LogInformation("Đã tải font nhãn AI {ObjectKey} về cache {Path}.", objectKey, localPath);

            return new LabelFontResolution(localPath, source, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kho file trục trặc tạm thời: thử lại được. Font thiếu hẳn đã bị bắt ở nhánh Exists phía trên.
            return new LabelFontResolution(null, source, $"Không tải được font nhãn AI {objectKey}: {ex.Message}", IsRetryable: true);
        }
    }
}
