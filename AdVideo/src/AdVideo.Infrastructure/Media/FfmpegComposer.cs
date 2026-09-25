using System.Text;
using AdVideo.Core.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdVideo.Infrastructure.Media;

/// <summary>Chữ cần vẽ lên hình, ở dạng Core hiểu được — chưa biết gì về file tạm hay FFmpeg.</summary>
/// <param name="Text">Nội dung tiếng Việt có dấu.</param>
public sealed record TextOverlayRequest(
    string Text,
    LabelPosition Position,
    int FontSizePx,
    double Opacity = 0.95,
    double StartSeconds = 0,
    double EndSeconds = 0);

/// <summary>Kết quả ghép.</summary>
public sealed record ComposeResult(
    bool IsSuccess,
    string? OutputPath = null,
    string? FailureReason = null,
    string? RawError = null);

/// <summary>Ghép các shot đã render thành video cuối (bước 8).</summary>
public interface IVideoComposer
{
    Task<ComposeResult> ComposeAsync(
        ComposeRequest request,
        IReadOnlyList<TextOverlayRequest> overlays,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Cài đặt bước 8 bằng một lệnh FFmpeg duy nhất.
/// </summary>
/// <remarks>
/// <para>
/// <b>Một lệnh, không phải nhiều bước nối nhau.</b> Ghép từng cặp rồi encode lại nhiều lần làm
/// chất lượng tụt sau mỗi vòng và tốn gấp mấy lần thời gian. Filtergraph dài hơn nhưng chỉ encode
/// đúng một lần.
/// </para>
/// <para>
/// <b>Chữ được ghi ra file tạm trước khi gọi.</b> Lý do ở <see cref="DrawTextSpec"/>: tiếng Việt
/// có dấu nháy và dấu hai chấm, mà drawtext coi chúng là ký tự điều khiển.
/// </para>
/// <para>
/// <b>Font phải có dấu tiếng Việt.</b> Thiếu font thì FFmpeg vẫn chạy và vẫn xuất ra video — chỉ
/// là chữ hiện thành ô vuông. Nên thiếu font bị chặn ngay ở đây, trước khi tốn một lần encode.
/// </para>
/// </remarks>
public sealed class FfmpegComposer : IVideoComposer
{
    private readonly IFfmpegRunner _runner;
    private readonly FfmpegOptions _options;
    private readonly ILogger<FfmpegComposer> _logger;

    public FfmpegComposer(
        IFfmpegRunner runner,
        IOptions<FfmpegOptions> options,
        ILogger<FfmpegComposer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ComposeResult> ComposeAsync(
        ComposeRequest request,
        IReadOnlyList<TextOverlayRequest> overlays,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(overlays);

        IReadOnlyList<string> labelProblems = AiLabelStamper.Validate(request.Label);

        if (labelProblems.Count > 0)
        {
            // Chặn ở đây chứ không đợi QC bước 9: encode xong rồi mới biết nhãn sai nghĩa là vứt
            // đi vài phút CPU cho một kết quả chắc chắn bị đánh trượt.
            return new ComposeResult(
                false,
                FailureReason: $"Nhãn AI không hợp lệ: {string.Join(" ", labelProblems)}");
        }

        if (string.IsNullOrWhiteSpace(request.FontFile) || !File.Exists(request.FontFile))
        {
            return new ComposeResult(
                false,
                FailureReason:
                $"Không tìm thấy file font '{request.FontFile}'. Không có font có dấu thì chữ tiếng Việt " +
                "hiện thành ô vuông, và FFmpeg không báo lỗi gì cả.");
        }

        string workDir = Path.Combine(Path.GetTempPath(), $"advideo-compose-{request.JobId:N}");
        Directory.CreateDirectory(workDir);

        try
        {
            string labelPath = await WriteTextAsync(workDir, "label", request.Label.OverlayText, cancellationToken);

            var overlaySpecs = new List<DrawTextSpec>(overlays.Count);

            for (int i = 0; i < overlays.Count; i++)
            {
                TextOverlayRequest overlay = overlays[i];
                string path = await WriteTextAsync(workDir, $"overlay-{i}", overlay.Text, cancellationToken);

                overlaySpecs.Add(new DrawTextSpec(
                    path,
                    overlay.Position,
                    overlay.FontSizePx,
                    overlay.Opacity,
                    overlay.StartSeconds,
                    overlay.EndSeconds));
            }

            ComposeRequest prepared = request with
            {
                LabelTextFilePath = labelPath,
                Overlays = overlaySpecs,
            };

            IReadOnlyList<string> args = FfmpegCommandBuilder.BuildCompose(prepared);

            _logger.LogInformation(
                "Ghép {Shots} shot cho job {JobId} thành {Output}.",
                request.Shots.Count,
                request.JobId,
                request.OutputPath);

            FfmpegRunResult run = await _runner.RunFfmpegAsync(args, cancellationToken);

            if (!run.IsSuccess)
            {
                return new ComposeResult(
                    false,
                    FailureReason: "FFmpeg không ghép được video.",
                    RawError: run.Tail());
            }

            if (!File.Exists(request.OutputPath) || new FileInfo(request.OutputPath).Length == 0)
            {
                // FFmpeg thoát 0 mà file rỗng là chuyện có thật khi hết dung lượng đĩa giữa chừng.
                return new ComposeResult(
                    false,
                    FailureReason: "FFmpeg báo thành công nhưng file kết quả rỗng — kiểm tra dung lượng đĩa.",
                    RawError: run.Tail());
            }

            return new ComposeResult(true, request.OutputPath);
        }
        finally
        {
            DeleteQuietly(workDir);
        }
    }

    private static async Task<string> WriteTextAsync(
        string workDir,
        string name,
        string text,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(workDir, $"{name}.txt");

        // UTF-8 KHÔNG BOM: drawtext đọc BOM như ký tự thật và vẽ ra một ô vuông ở đầu dòng chữ.
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        return path;
    }

    private void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Không xoá được thư mục tạm {Directory}.", directory);
        }
    }

    /// <summary>
    /// Font mặc định lấy từ cấu hình, để bước gọi không phải biết đường dẫn font.
    /// </summary>
    /// <remarks>
    /// Null khi chưa cấu hình. <see cref="ComposeAsync"/> từ chối luôn trong trường hợp đó, thay
    /// vì để FFmpeg dùng font hệ thống và xuất ra một video đầy ô vuông.
    /// </remarks>
    public string? DefaultFontFile => _options.FontFile;
}
