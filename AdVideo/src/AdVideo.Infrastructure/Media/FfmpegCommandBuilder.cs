using System.Globalization;
using System.Text;
using AdVideo.Core.Enums;
using AdVideo.Core.Media;
using AdVideo.Core.Timeline;

namespace AdVideo.Infrastructure.Media;

/// <summary>Một shot đã render, sẵn sàng ghép.</summary>
/// <param name="VideoPath">File clip trên đĩa cục bộ.</param>
/// <param name="NativeAudioPath">
/// Track tiếng gốc GIỮ LẠI của shot này, hoặc null nếu bỏ. Quyết định do
/// <see cref="Core.Providers.NativeSoundTranslator"/> đưa ra, không phải ở đây.
/// </param>
public sealed record ComposeShot(ShotPlan Plan, string VideoPath, string? NativeAudioPath = null);

/// <summary>Một mảng chữ FFmpeg vẽ lên hình.</summary>
/// <param name="TextFilePath">
/// Đường dẫn file UTF-8 chứa nội dung chữ.
/// </param>
/// <remarks>
/// <b>Chữ luôn nằm trong FILE, không nằm trong tham số.</b> Bộ lọc drawtext coi <c>:</c>, <c>'</c>,
/// <c>%</c> và <c>\</c> là ký tự điều khiển; tiếng Việt lại hay có dấu nháy và dấu hai chấm. Thoát
/// tay chuỗi là nguồn lỗi bất tận — còn <c>textfile=</c> thì không phải thoát gì cả.
/// </remarks>
public sealed record DrawTextSpec(
    string TextFilePath,
    LabelPosition Position,
    int FontSizePx,
    double Opacity,
    double StartSeconds = 0,
    double EndSeconds = 0,
    bool WithBox = true);

/// <summary>Đầu vào của bước 8 (ghép).</summary>
public sealed record ComposeRequest
{
    public required Guid JobId { get; init; }

    public required IReadOnlyList<ComposeShot> Shots { get; init; }

    /// <summary>File giọng đọc liên tục do bước 4 trả về. Các đoạn được cắt ra theo timeline.</summary>
    public required string VoiceAudioPath { get; init; }

    public required AspectRatio AspectRatio { get; init; }

    /// <summary>Nhãn AI. Không nullable: D9 không có đường tắt.</summary>
    public required AiLabelSpec Label { get; init; }

    /// <summary>File UTF-8 chứa chữ của nhãn AI. <see cref="FfmpegComposer"/> ghi ra trước khi gọi.</summary>
    public required string LabelTextFilePath { get; init; }

    /// <summary>Đường dẫn file font có dấu tiếng Việt.</summary>
    public required string FontFile { get; init; }

    public required string OutputPath { get; init; }

    /// <summary>Chữ khác trên hình (phụ đề, khẩu hiệu). Theo D4, tất cả đều do FFmpeg vẽ.</summary>
    public IReadOnlyList<DrawTextSpec> Overlays { get; init; } = [];

    /// <summary>Mức tiếng động gốc khi được giữ, dB. Null thì bỏ hết track gốc.</summary>
    public int? SfxLevelDb { get; init; }

    public double TargetLoudnessLufs { get; init; } = -14;

    public int FrameRate { get; init; } = 30;
}

/// <summary>
/// Dựng danh sách tham số FFmpeg cho bước ghép. Thuần hàm: không chạy tiến trình, không chạm đĩa.
/// </summary>
/// <remarks>
/// <para>
/// Tách khỏi <see cref="FfmpegComposer"/> để filtergraph — phần dễ sai nhất — test được bằng
/// assert chuỗi, không cần cài FFmpeg trên máy chạy test.
/// </para>
/// <para>
/// <b>Mọi chữ trên hình do FFmpeg vẽ (D4).</b> Model sinh video luôn viết sai dấu tiếng Việt;
/// không có ngoại lệ nào đáng để thử lại. Prompt phủ định toàn cục cấm model vẽ chữ, và chữ thật
/// được vẽ ở đây, nơi ta kiểm soát được font.
/// </para>
/// </remarks>
public static class FfmpegCommandBuilder
{
    /// <summary>Đệm từ mép khung, tính theo phần trăm chiều rộng. Đủ để không bị crop an toàn của nền tảng cắt.</summary>
    private const double MarginRatio = 0.04;

    public static IReadOnlyList<string> BuildCompose(ComposeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Shots.Count == 0)
        {
            throw new ArgumentException("Không có shot nào để ghép.", nameof(request));
        }

        (int width, int height) = request.AspectRatio.ToResolution();

        var args = new List<string> { "-hide_banner", "-nostats", "-y" };

        foreach (ComposeShot shot in request.Shots)
        {
            args.Add("-i");
            args.Add(shot.VideoPath);
        }

        int voiceInput = request.Shots.Count;
        args.Add("-i");
        args.Add(request.VoiceAudioPath);

        var sfxInputs = new List<(int Input, ShotPlan Plan)>();

        if (request.SfxLevelDb is not null)
        {
            foreach (ComposeShot shot in request.Shots)
            {
                if (string.IsNullOrWhiteSpace(shot.NativeAudioPath))
                {
                    continue;
                }

                sfxInputs.Add((voiceInput + 1 + sfxInputs.Count, shot.Plan));
                args.Add("-i");
                args.Add(shot.NativeAudioPath);
            }
        }

        args.Add("-filter_complex");
        args.Add(BuildFilterGraph(request, width, height, voiceInput, sfxInputs));

        args.Add("-map");
        args.Add("[vout]");
        args.Add("-map");
        args.Add("[aout]");

        // Metadata lấy thẳng từ nhãn AI: chữ trên hình cho người xem, metadata container cho công
        // cụ kiểm tra và nền tảng phân phối. Thiếu một trong hai là chưa đủ theo D9.
        foreach ((string key, string value) in request.Label.Metadata)
        {
            args.Add("-metadata");
            args.Add($"{key}={value}");
        }

        args.AddRange(
        [
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "20",

            // yuv420p chứ không yuv444p/10-bit: điện thoại Android tầm trung không phát được
            // profile cao, và video quảng cáo chủ yếu xem trên điện thoại.
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "192k",
            "-ar", "48000",

            // faststart đẩy chỉ mục lên đầu file để video bắt đầu phát ngay khi tải dở — thiếu nó
            // thì người xem phải chờ tải xong toàn bộ.
            "-movflags", "+faststart",
            request.OutputPath,
        ]);

        return args;
    }

    private static string BuildFilterGraph(
        ComposeRequest request,
        int width,
        int height,
        int voiceInput,
        IReadOnlyList<(int Input, ShotPlan Plan)> sfxInputs)
    {
        var graph = new StringBuilder();

        // ---- Hình: chuẩn hoá từng shot rồi nối ----
        // Chuẩn hoá là bắt buộc dù mọi shot cùng một provider: provider có thể đổi độ phân giải
        // giữa hai lần gọi, và concat từ chối nối những luồng khác kích thước/SAR.
        for (int i = 0; i < request.Shots.Count; i++)
        {
            ShotPlan plan = request.Shots[i].Plan;

            graph.Append(CultureInfo.InvariantCulture, $"[{i}:v]");
            graph.Append(CultureInfo.InvariantCulture, $"scale={width}:{height}:force_original_aspect_ratio=decrease,");
            graph.Append(CultureInfo.InvariantCulture, $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,");
            graph.Append(CultureInfo.InvariantCulture, $"setsar=1,fps={request.FrameRate},");

            // trim theo đúng bậc thời lượng đã khoá ở bước 5: provider hay trả dư vài frame, và
            // vài frame dư nhân với sáu shot là hơn nửa giây lệch so với timeline.
            graph.Append(CultureInfo.InvariantCulture, $"trim=duration={Seconds(plan.VideoDurationSeconds)},");
            graph.Append(CultureInfo.InvariantCulture, $"setpts=PTS-STARTPTS[v{i}];");
        }

        for (int i = 0; i < request.Shots.Count; i++)
        {
            graph.Append(CultureInfo.InvariantCulture, $"[v{i}]");
        }

        graph.Append(CultureInfo.InvariantCulture, $"concat=n={request.Shots.Count}:v=1:a=0[vcat];");

        // ---- Chữ: nhãn AI trước, rồi tới phụ đề ----
        string videoLabel = "vcat";
        int drawIndex = 0;

        foreach (DrawTextSpec spec in EnumerateTextSpecs(request))
        {
            string next = $"vt{drawIndex++}";

            graph.Append(CultureInfo.InvariantCulture, $"[{videoLabel}]{BuildDrawText(spec, request.FontFile, width)}[{next}];");
            videoLabel = next;
        }

        graph.Append(CultureInfo.InvariantCulture, $"[{videoLabel}]null[vout];");

        // ---- Tiếng ----
        AppendAudioGraph(graph, request, voiceInput, sfxInputs);

        return graph.ToString();
    }

    private static void AppendAudioGraph(
        StringBuilder graph,
        ComposeRequest request,
        int voiceInput,
        IReadOnlyList<(int Input, ShotPlan Plan)> sfxInputs)
    {
        int shotCount = request.Shots.Count;

        // Một luồng audio không thể dùng làm đầu vào cho nhiều bộ lọc, nên phải asplit trước.
        // Thiếu bước này FFmpeg báo "Filter ... has an unconnected output" — thông báo không hề
        // gợi ý tới nguyên nhân thật.
        graph.Append(CultureInfo.InvariantCulture, $"[{voiceInput}:a]asplit={shotCount}");

        for (int i = 0; i < shotCount; i++)
        {
            graph.Append(CultureInfo.InvariantCulture, $"[vsrc{i}]");
        }

        graph.Append(';');

        var mixLabels = new List<string>();

        for (int i = 0; i < shotCount; i++)
        {
            ShotPlan plan = request.Shots[i].Plan;
            int delayMs = (int)Math.Round(plan.AudioStartInTimelineSeconds * 1000);

            graph.Append(CultureInfo.InvariantCulture, $"[vsrc{i}]");
            graph.Append(
                CultureInfo.InvariantCulture,
                $"atrim=start={Seconds(plan.NarrationStartSeconds)}:end={Seconds(plan.NarrationEndSeconds)},");
            graph.Append("asetpts=PTS-STARTPTS,");

            // all=1 để trễ áp cho MỌI kênh. Không có nó thì chỉ kênh trái bị trễ và giọng đọc
            // nghe như lệch sang một bên tai.
            graph.Append(CultureInfo.InvariantCulture, $"adelay={delayMs}:all=1[va{i}];");

            mixLabels.Add($"va{i}");
        }

        foreach ((int input, ShotPlan plan) in sfxInputs)
        {
            int delayMs = (int)Math.Round(plan.VideoStartSeconds * 1000);
            string label = $"sfx{input}";

            graph.Append(CultureInfo.InvariantCulture, $"[{input}:a]");
            graph.Append(CultureInfo.InvariantCulture, $"volume={request.SfxLevelDb}dB,");
            graph.Append(CultureInfo.InvariantCulture, $"atrim=duration={Seconds(plan.VideoDurationSeconds)},");
            graph.Append("asetpts=PTS-STARTPTS,");
            graph.Append(CultureInfo.InvariantCulture, $"adelay={delayMs}:all=1[{label}];");

            mixLabels.Add(label);
        }

        string mixed;

        if (mixLabels.Count == 1)
        {
            mixed = mixLabels[0];
        }
        else
        {
            foreach (string label in mixLabels)
            {
                graph.Append(CultureInfo.InvariantCulture, $"[{label}]");
            }

            // normalize=0 là tham số quan trọng nhất của cả graph audio: mặc định amix CHIA âm
            // lượng cho số đầu vào, nên sáu shot làm giọng đọc nhỏ đi sáu lần. Video ra vẫn có
            // tiếng, chỉ là nhỏ một cách khó hiểu — và loudnorm phía sau sẽ kéo cả tiếng ồn nền lên.
            graph.Append(
                CultureInfo.InvariantCulture,
                $"amix=inputs={mixLabels.Count}:normalize=0:dropout_transition=0[amixed];");

            mixed = "amixed";
        }

        // loudnorm một lượt (không phải hai lượt): đủ cho bản thành phẩm quảng cáo, và hai lượt
        // đòi chạy FFmpeg hai lần chỉ để giảm sai số vài phần mười LU.
        graph.Append(CultureInfo.InvariantCulture, $"[{mixed}]");
        graph.Append(
            CultureInfo.InvariantCulture,
            $"loudnorm=I={Seconds(request.TargetLoudnessLufs)}:TP=-1.5:LRA=11,");
        graph.Append("aresample=48000[aout]");
    }

    private static IEnumerable<DrawTextSpec> EnumerateTextSpecs(ComposeRequest request)
    {
        // Nhãn AI vẽ TRƯỚC phụ đề: vẽ sau nghĩa là phụ đề có thể bị nhãn đè, mà nhãn thì bắt buộc
        // phải đọc được (D9).
        yield return new DrawTextSpec(
            request.LabelTextFilePath,
            request.Label.Position,
            request.Label.FontSizePx,
            request.Label.Opacity,
            request.Label.StartSeconds,
            request.Label.IsPermanent ? 0 : request.Label.StartSeconds + request.Label.DurationSeconds);

        foreach (DrawTextSpec overlay in request.Overlays)
        {
            yield return overlay;
        }
    }

    private static string BuildDrawText(DrawTextSpec spec, string fontFile, int width)
    {
        int margin = (int)Math.Round(width * MarginRatio);

        var filter = new StringBuilder("drawtext=");

        filter.Append(CultureInfo.InvariantCulture, $"fontfile='{EscapePath(fontFile)}':");
        filter.Append(CultureInfo.InvariantCulture, $"textfile='{EscapePath(spec.TextFilePath)}':");
        filter.Append(CultureInfo.InvariantCulture, $"fontsize={spec.FontSizePx}:");
        filter.Append(CultureInfo.InvariantCulture, $"fontcolor=white@{Seconds(spec.Opacity)}:");

        if (spec.WithBox)
        {
            // Hộp nền là thứ giữ cho chữ đọc được trên cảnh sáng. Không có nó thì nhãn AI trên
            // nền trời trắng coi như vô hình, và QC bước 9 sẽ đánh trượt.
            filter.Append("box=1:boxcolor=black@0.45:boxborderw=12:");
        }

        filter.Append(CultureInfo.InvariantCulture, $"x={HorizontalExpression(spec.Position, margin)}:");
        filter.Append(CultureInfo.InvariantCulture, $"y={VerticalExpression(spec.Position, margin)}");

        if (spec.EndSeconds > spec.StartSeconds)
        {
            filter.Append(
                CultureInfo.InvariantCulture,
                $":enable='between(t,{Seconds(spec.StartSeconds)},{Seconds(spec.EndSeconds)})'");
        }

        return filter.ToString();
    }

    private static string HorizontalExpression(LabelPosition position, int margin)
        => position is LabelPosition.TopRight or LabelPosition.BottomRight
            ? $"w-tw-{margin}"
            : margin.ToString(CultureInfo.InvariantCulture);

    private static string VerticalExpression(LabelPosition position, int margin)
        => position is LabelPosition.BottomLeft or LabelPosition.BottomRight
            ? $"h-th-{margin}"
            : margin.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Thoát đường dẫn để nhét được vào filtergraph.
    /// </summary>
    /// <remarks>
    /// Trong filtergraph, <c>\</c> là ký tự thoát và <c>:</c> là dấu phân cách tham số — nghĩa là
    /// một đường dẫn Windows như <c>D:\fonts\arial.ttf</c> bị hiểu thành ba tham số. Đổi sang gạch
    /// chéo xuôi (FFmpeg chấp nhận trên Windows) rồi thoát dấu hai chấm là cách duy nhất chạy được
    /// trên cả hai hệ điều hành.
    /// </remarks>
    internal static string EscapePath(string path)
        => path.Replace('\\', '/').Replace(":", "\\:", StringComparison.Ordinal);

    private static string Seconds(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
