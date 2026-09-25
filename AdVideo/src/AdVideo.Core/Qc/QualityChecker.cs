namespace AdVideo.Core.Qc;

/// <summary>Một phép kiểm trong QC bước 9.</summary>
/// <param name="Check">Tên phép kiểm, dạng ổn định để máy đọc.</param>
/// <param name="IsBlocking">
/// Fail phép kiểm này có làm fail cả job không.
/// </param>
/// <remarks>
/// Nhãn AI là <b>blocking</b>: Luật TTNT 2025 đặt nghĩa vụ lên bên triển khai, phạt tới 2 tỉ đồng,
/// và không chuyển sang khách bằng điều khoản được. Giao một video thiếu nhãn là giao một rủi ro
/// pháp lý, không phải giao một sản phẩm chưa hoàn thiện.
///
/// Chữ lọt lưới do model tự vẽ cũng blocking ở mức thấp hơn: model viết chữ tiếng Việt LUÔN sai dấu,
/// và một dòng sai dấu trong video quảng cáo thì khách sẽ thấy ngay.
/// </remarks>
/// <param name="Passed">Đạt hay không.</param>
/// <param name="Expected">Giá trị kỳ vọng, dạng chuỗi để log được.</param>
/// <param name="Actual">Giá trị đo được.</param>
/// <param name="Message">Giải thích cho người vận hành.</param>
public sealed record QcCheck(
    string Check,
    bool Passed,
    bool IsBlocking,
    string? Expected = null,
    string? Actual = null,
    string? Message = null);

/// <summary>Kết quả QC của một video. Lưu cùng job và xem được trong UI.</summary>
public sealed record QcReport(
    bool IsPassed,
    IReadOnlyList<QcCheck> Checks)
{
    public IReadOnlyList<QcCheck> Failed => Checks.Where(c => !c.Passed).ToList();

    public IReadOnlyList<QcCheck> FailedBlocking => Checks.Where(c => !c.Passed && c.IsBlocking).ToList();

    /// <summary>Tóm tắt một dòng cho log và cho trường <c>FailureReason</c> trên job.</summary>
    /// <remarks>
    /// Phép kiểm không chặn mà trượt vẫn phải hiện ra ở đây. Nếu dòng này báo "đạt 8/8" trong khi
    /// một phép kiểm đã trượt thì cảnh báo đó không tồn tại với người vận hành: họ chỉ đọc một dòng.
    /// </remarks>
    public string Summary => !IsPassed
        ? $"QC fail {Failed.Count}/{Checks.Count}: {string.Join("; ", Failed.Select(c => c.Check))}"
        : Failed.Count == 0
            ? $"QC đạt {Checks.Count}/{Checks.Count} phép kiểm."
            : $"QC đạt, có {Failed.Count}/{Checks.Count} cảnh báo: {string.Join("; ", Failed.Select(c => c.Check))}";
}

/// <summary>
/// Thông số đo được từ video, do ffprobe cung cấp. Core nhận số liệu, không tự chạy ffprobe.
/// </summary>
public sealed record MediaProbeResult(
    double DurationSeconds,
    int Width,
    int Height,
    string? VideoCodec,
    bool HasAudio,
    string? AudioCodec,
    long SizeBytes,
    double? LoudnessLufs = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    bool HasBlackFrames = false,
    double? LipSyncDriftSeconds = null,
    IReadOnlyList<string>? DetectedTextOverlays = null);

/// <summary>
/// Ngưỡng QC. Nạp từ <see cref="Entities.SystemSetting"/> — không hard-code, vì ngưỡng là thứ
/// sẽ phải chỉnh sau khi xem kết quả thật.
/// </summary>
public sealed record QcThresholds
{
    /// <summary>Chuẩn âm lượng đầu ra theo thiết kế bước 8.</summary>
    public double TargetLoudnessLufs { get; init; } = -14;

    /// <summary>Dung sai LUFS. Chặt quá thì rớt oan vì khác biệt encoder, lỏng quá thì mất tác dụng.</summary>
    public double LoudnessToleranceLu { get; init; } = 1.5;

    /// <summary>Dung sai thời lượng, giây. Video phải đúng độ dài đã hứa với khách.</summary>
    public double DurationToleranceSeconds { get; init; } = 0.5;

    /// <summary>Lệch tiếng-hình tối đa, giây. Tiêu chí thành công S4: 200 ms.</summary>
    public double MaxLipSyncDriftSeconds { get; init; } = 0.2;

    /// <summary>Kích thước file tối thiểu để coi là video thật, không phải file hỏng 0 byte.</summary>
    public long MinSizeBytes { get; init; } = 10_000;
}

/// <summary>
/// Đánh giá kết quả ffprobe theo ngưỡng. Thuần hàm — test được mà không cần video thật.
/// </summary>
/// <remarks>
/// Sprint 1 chạy bản tối thiểu này. Sprint 5 thêm quét chữ lọt lưới bằng model thị giác và đo
/// lệch tiếng-hình thật; các phép kiểm đó đã có chỗ trong <see cref="QcCheck"/> nên thêm vào
/// không phải đổi contract.
/// </remarks>
public static class QualityChecker
{
    public static QcReport Evaluate(MediaProbeResult probe, QcThresholds thresholds,
        Media.AiLabelSpec? labelSpec, int expectedWidth, int expectedHeight, double expectedDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(thresholds);

        var checks = new List<QcCheck>();

        checks.Add(new QcCheck(
            "has_video_stream", probe.VideoCodec is not null, IsBlocking: true,
            Expected: "có video stream", Actual: probe.VideoCodec ?? "không có",
            Message: probe.VideoCodec is null ? "File không có video stream — compose hỏng hoặc tải về chưa xong." : null));

        checks.Add(new QcCheck(
            "has_audio_stream", probe.HasAudio, IsBlocking: true,
            Expected: "có audio stream", Actual: probe.HasAudio ? probe.AudioCodec ?? "có" : "không có",
            Message: probe.HasAudio ? null : "Video không có tiếng. Kiểm tra bước 4 (TTS) và bước 8 (trộn âm)."));

        var durationOk = Math.Abs(probe.DurationSeconds - expectedDurationSeconds) <= thresholds.DurationToleranceSeconds;
        checks.Add(new QcCheck(
            "duration", durationOk, IsBlocking: true,
            Expected: $"{expectedDurationSeconds:0.##}s ±{thresholds.DurationToleranceSeconds}s",
            Actual: $"{probe.DurationSeconds:0.###}s",
            Message: durationOk ? null : "Thời lượng lệch quá dung sai. Thường do một shot bị cắt ngắn hoặc timeline khoá sai."));

        var sizeOk = probe.SizeBytes >= thresholds.MinSizeBytes;
        checks.Add(new QcCheck(
            "file_size", sizeOk, IsBlocking: true,
            Expected: $"≥ {thresholds.MinSizeBytes} bytes", Actual: $"{probe.SizeBytes} bytes",
            Message: sizeOk ? null : "File quá nhỏ — có thể là file hỏng hoặc video toàn khung đen."));

        var resolutionOk = probe.Width == expectedWidth && probe.Height == expectedHeight;
        checks.Add(new QcCheck(
            "resolution", resolutionOk, IsBlocking: false,
            Expected: $"{expectedWidth}x{expectedHeight}", Actual: $"{probe.Width}x{probe.Height}",
            Message: resolutionOk ? null : "Độ phân giải lệch khung hình yêu cầu. Không chặn vì video vẫn dùng được, nhưng phải báo."));

        if (probe.LoudnessLufs is { } lufs)
        {
            var loudnessOk = Math.Abs(lufs - thresholds.TargetLoudnessLufs) <= thresholds.LoudnessToleranceLu;
            checks.Add(new QcCheck(
                "loudness", loudnessOk, IsBlocking: false,
                Expected: $"{thresholds.TargetLoudnessLufs} LUFS ±{thresholds.LoudnessToleranceLu}",
                Actual: $"{lufs:0.##} LUFS",
                Message: loudnessOk ? null : "Âm lượng lệch chuẩn. Không chặn nhưng video sẽ to/nhỏ bất thường khi đăng cạnh video khác."));
        }
        else
        {
            checks.Add(new QcCheck(
                "loudness", Passed: false, IsBlocking: false,
                Expected: $"{thresholds.TargetLoudnessLufs} LUFS", Actual: "không đo được",
                Message: "Không đo được LUFS. Chạy loudnorm ở bước 8 rồi đo lại bằng ffmpeg -af ebur128."));
        }

        checks.Add(new QcCheck(
            "no_black_frames", !probe.HasBlackFrames, IsBlocking: false,
            Expected: "không có khung đen", Actual: probe.HasBlackFrames ? "có khung đen" : "không",
            Message: probe.HasBlackFrames
                ? "Có khung đen — thường do crossfade hỏng hoặc shot bị thiếu."
                : null));

        if (probe.LipSyncDriftSeconds is { } drift)
        {
            var driftOk = Math.Abs(drift) <= thresholds.MaxLipSyncDriftSeconds;
            checks.Add(new QcCheck(
                "lip_sync_drift", driftOk, IsBlocking: true,
                Expected: $"≤ {thresholds.MaxLipSyncDriftSeconds * 1000:0} ms",
                Actual: $"{Math.Abs(drift) * 1000:0} ms",
                Message: driftOk ? null : "Lệch tiếng-hình vượt ngưỡng. Kiểm tra lại bước 5 — timeline khoá sai thì mọi thứ sau đó đều sai."));
        }

        // Nhãn AI — phép kiểm quan trọng nhất và là phép kiểm KHÔNG có ngoại lệ.
        var labelProblems = Media.AiLabelStamper.Validate(labelSpec);
        checks.Add(new QcCheck(
            "ai_label", labelProblems.Count == 0, IsBlocking: true,
            Expected: "có nhãn AI trên hình và trong metadata",
            Actual: labelProblems.Count == 0 ? "đạt" : string.Join(" ", labelProblems),
            Message: labelProblems.Count == 0 ? null : string.Join(" ", labelProblems)));

        // Kiểm chứng metadata thật trong file, không chỉ kiểm đặc tả: đặc tả đúng mà FFmpeg
        // không ghi được (sai cú pháp -metadata) thì vẫn là thiếu nhãn.
        if (labelProblems.Count == 0 && probe.Metadata is { } md)
        {
            var hasMarker = md.Any(kv =>
                kv.Key.Contains("ai", StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Contains("ai", StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Contains("nhân tạo", StringComparison.OrdinalIgnoreCase));
            checks.Add(new QcCheck(
                "ai_label_in_file_metadata", hasMarker, IsBlocking: true,
                Expected: "metadata file có trường đánh dấu AI",
                Actual: hasMarker ? "có" : $"không ({string.Join(", ", md.Keys.Take(6))})",
                Message: hasMarker ? null : "Đặc tả nhãn đúng nhưng metadata không nằm trong file — kiểm tra cú pháp -metadata ở bước 8."));
        }

        if (probe.DetectedTextOverlays is { Count: > 0 } texts)
        {
            checks.Add(new QcCheck(
                "no_model_drawn_text", Passed: false, IsBlocking: true,
                Expected: "không có chữ do model tự vẽ",
                Actual: string.Join(" | ", texts.Take(3)),
                Message: "Model đã tự vẽ chữ. Chữ tiếng Việt do model vẽ LUÔN sai dấu — mọi chữ trên hình phải do FFmpeg vẽ (D4)."));
        }

        return new QcReport(!checks.Any(c => !c.Passed && c.IsBlocking), checks);
    }
}
