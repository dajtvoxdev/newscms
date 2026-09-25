using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdVideo.Core.Qc;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Media;

/// <summary>Đo đặc tính kỹ thuật của một file media để bước 9 chấm QC.</summary>
public interface IMediaInspector
{
    /// <param name="measureLoudness">
    /// Đo LUFS hay không. Phép đo này phải giải mã toàn bộ audio nên tốn thời gian — chỉ bật cho
    /// file thành phẩm, không bật cho từng shot.
    /// </param>
    Task<MediaProbeResult> ProbeAsync(
        string filePath,
        bool measureLoudness = false,
        bool detectBlackFrames = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Cài đặt bằng ffprobe (đặc tính luồng) và ffmpeg (âm lượng, khung đen).
/// </summary>
/// <remarks>
/// Chia làm ba lần gọi tiến trình riêng thay vì một lệnh gộp: ffprobe đọc metadata gần như tức
/// thì, còn đo LUFS phải giải mã hết audio. Gộp lại là bắt mọi phép kiểm tra nhẹ phải trả giá của
/// phép kiểm tra nặng nhất.
/// </remarks>
public sealed partial class FfprobeInspector : IMediaInspector
{
    private readonly IFfmpegRunner _runner;
    private readonly ILogger<FfprobeInspector> _logger;

    public FfprobeInspector(IFfmpegRunner runner, ILogger<FfprobeInspector> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task<MediaProbeResult> ProbeAsync(
        string filePath,
        bool measureLoudness = false,
        bool detectBlackFrames = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Không có file để đo: {filePath}", filePath);
        }

        FfmpegRunResult probe = await _runner.RunFfprobeAsync(
            [
                "-v", "error",
                "-print_format", "json",
                "-show_format",
                "-show_streams",
                filePath,
            ],
            cancellationToken);

        if (!probe.IsSuccess)
        {
            throw new InvalidOperationException(
                $"ffprobe không đọc được {Path.GetFileName(filePath)}: {probe.Tail()}");
        }

        using JsonDocument document = JsonDocument.Parse(probe.StandardOutput);
        JsonElement root = document.RootElement;

        JsonElement? videoStream = FindStream(root, "video");
        JsonElement? audioStream = FindStream(root, "audio");

        long sizeBytes = new FileInfo(filePath).Length;

        double? loudness = measureLoudness && audioStream is not null
            ? await MeasureLoudnessAsync(filePath, cancellationToken)
            : null;

        bool hasBlackFrames = detectBlackFrames
            && await DetectBlackFramesAsync(filePath, cancellationToken);

        return new MediaProbeResult(
            DurationSeconds: ReadDuration(root, videoStream),
            Width: ReadInt(videoStream, "width"),
            Height: ReadInt(videoStream, "height"),
            VideoCodec: ReadString(videoStream, "codec_name"),
            HasAudio: audioStream is not null,
            AudioCodec: ReadString(audioStream, "codec_name"),
            SizeBytes: sizeBytes,
            LoudnessLufs: loudness,
            Metadata: ReadMetadata(root),
            HasBlackFrames: hasBlackFrames);
    }

    /// <summary>
    /// Đo âm lượng tích hợp bằng bộ lọc ebur128.
    /// </summary>
    /// <remarks>
    /// Dùng <c>-f null -</c>: ta chỉ cần số đo, không cần file ra. Bỏ quên phần này là ffmpeg đòi
    /// đường dẫn đầu ra và thoát với lỗi cú pháp.
    /// </remarks>
    private async Task<double?> MeasureLoudnessAsync(string filePath, CancellationToken cancellationToken)
    {
        FfmpegRunResult result = await _runner.RunFfmpegAsync(
            [
                "-hide_banner",
                "-nostats",
                "-i", filePath,
                "-af", "ebur128=peak=true",
                "-f", "null",
                "-",
            ],
            cancellationToken);

        // ebur128 in kết quả ra stderr, trong khối "Summary:" ở cuối, dạng "    I:         -14.2 LUFS".
        Match match = IntegratedLoudnessPattern().Match(result.StandardError);

        if (!match.Success)
        {
            _logger.LogWarning(
                "Không đọc được chỉ số LUFS từ đầu ra ebur128 của {File}; bỏ qua phép kiểm âm lượng.",
                Path.GetFileName(filePath));

            return null;
        }

        // InvariantCulture bắt buộc: ffmpeg luôn in dấu chấm thập phân, còn máy chạy locale vi-VN
        // thì double.Parse mặc định hiểu dấu phẩy. Đọc sai ở đây là chấm trượt QC vì lý do bịa.
        return double.Parse(match.Groups["lufs"].Value, CultureInfo.InvariantCulture);
    }

    /// <remarks>
    /// Ngưỡng 0,5 giây: một hai khung đen là hiệu ứng chuyển cảnh bình thường, nửa giây trở lên
    /// mới là dấu hiệu shot rỗng hoặc ghép hụt.
    /// </remarks>
    private async Task<bool> DetectBlackFramesAsync(string filePath, CancellationToken cancellationToken)
    {
        FfmpegRunResult result = await _runner.RunFfmpegAsync(
            [
                "-hide_banner",
                "-nostats",
                "-i", filePath,
                "-vf", "blackdetect=d=0.5:pic_th=0.98",
                "-f", "null",
                "-",
            ],
            cancellationToken);

        return result.StandardError.Contains("black_start", StringComparison.Ordinal);
    }

    private static JsonElement? FindStream(JsonElement root, string codecType)
    {
        if (!root.TryGetProperty("streams", out JsonElement streams))
        {
            return null;
        }

        foreach (JsonElement stream in streams.EnumerateArray())
        {
            if (stream.TryGetProperty("codec_type", out JsonElement type)
                && string.Equals(type.GetString(), codecType, StringComparison.Ordinal))
            {
                return stream;
            }
        }

        return null;
    }

    private static double ReadDuration(JsonElement root, JsonElement? videoStream)
    {
        // Ưu tiên format.duration vì đó là độ dài của cả container — cũng là độ dài người xem thấy.
        // Luồng video có thể ngắn hơn audio hoặc ngược lại, và lấy nhầm sẽ lệch đúng phần đuôi.
        if (root.TryGetProperty("format", out JsonElement format)
            && TryReadDouble(format, "duration", out double formatDuration))
        {
            return formatDuration;
        }

        if (videoStream is { } stream && TryReadDouble(stream, "duration", out double streamDuration))
        {
            return streamDuration;
        }

        return 0;
    }

    private static bool TryReadDouble(JsonElement element, string property, out double value)
    {
        value = 0;

        if (!element.TryGetProperty(property, out JsonElement raw))
        {
            return false;
        }

        // ffprobe trả số dưới dạng CHUỖI trong JSON ("duration": "8.033333"), không phải số.
        string? text = raw.ValueKind == JsonValueKind.String ? raw.GetString() : raw.ToString();

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static int ReadInt(JsonElement? element, string property)
    {
        if (element is not { } value || !value.TryGetProperty(property, out JsonElement raw))
        {
            return 0;
        }

        return raw.ValueKind == JsonValueKind.Number ? raw.GetInt32() : 0;
    }

    private static string? ReadString(JsonElement? element, string property)
    {
        if (element is not { } value || !value.TryGetProperty(property, out JsonElement raw))
        {
            return null;
        }

        return raw.GetString();
    }

    private static IReadOnlyDictionary<string, string> ReadMetadata(JsonElement root)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("format", out JsonElement format)
            && format.TryGetProperty("tags", out JsonElement tags)
            && tags.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty tag in tags.EnumerateObject())
            {
                metadata[tag.Name] = tag.Value.GetString() ?? string.Empty;
            }
        }

        return metadata;
    }

    [GeneratedRegex(@"^\s*I:\s*(?<lufs>-?\d+(\.\d+)?)\s*LUFS", RegexOptions.Multiline)]
    private static partial Regex IntegratedLoudnessPattern();
}
