using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdVideo.Infrastructure.Media;

/// <summary>Kết quả một lần chạy ffmpeg hoặc ffprobe.</summary>
/// <remarks>
/// <c>StandardError</c> luôn được giữ lại kể cả khi thành công: ffmpeg in TOÀN BỘ nhật ký ra
/// stderr, kể cả thông tin bình thường. Coi stderr là "chỗ chứa lỗi" là hiểu sai công cụ.
/// </remarks>
public sealed record FfmpegRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Elapsed)
{
    public bool IsSuccess => ExitCode == 0;

    /// <summary>Vài dòng cuối của stderr — phần gần như luôn chứa nguyên nhân thật.</summary>
    public string Tail(int lines = 12)
    {
        string[] all = StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return string.Join('\n', all.TakeLast(lines)).Trim();
    }
}

public interface IFfmpegRunner
{
    Task<FfmpegRunResult> RunFfmpegAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);

    Task<FfmpegRunResult> RunFfprobeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// Chạy ffmpeg/ffprobe bằng tiến trình con.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dùng <c>ArgumentList</c> chứ không nối chuỗi.</b> Tham số của ta chứa đường dẫn có dấu cách
/// và chuỗi drawtext có dấu tiếng Việt cùng dấu nháy. Tự nối chuỗi rồi tự escape là con đường
/// chắc chắn dẫn tới một lỗi khó tái hiện — chỉ hỏng với đúng một tên file của đúng một khách.
/// <c>ArgumentList</c> để .NET lo phần escape theo quy ước của từng hệ điều hành.
/// </para>
/// <para>
/// <b>Đọc stdout và stderr bằng sự kiện, không phải <c>ReadToEnd</c>.</b> Bộ đệm pipe của hệ điều
/// hành chỉ vài KB: đọc tuần tự thì ffmpeg ghi đầy stderr, bị chặn, và ta thì đang chờ nó kết
/// thúc. Hai bên chờ nhau vĩnh viễn — treo mà không có lỗi nào.
/// </para>
/// </remarks>
public sealed class FfmpegRunner : IFfmpegRunner
{
    private readonly FfmpegOptions _options;
    private readonly ILogger<FfmpegRunner> _logger;

    public FfmpegRunner(IOptions<FfmpegOptions> options, ILogger<FfmpegRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
    }

    public Task<FfmpegRunResult> RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
        => RunAsync(_options.FfmpegPath, arguments, cancellationToken);

    public Task<FfmpegRunResult> RunFfprobeAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
        => RunAsync(_options.FfprobePath, arguments, cancellationToken);

    private async Task<FfmpegRunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            // Lỗi ở đây gần như luôn là "không tìm thấy file thực thi". FfmpegOptions.Validate()
            // đã bắt trường hợp đường dẫn sai lúc khởi động, nên tới được đây nghĩa là file vừa
            // biến mất hoặc thiếu quyền chạy.
            throw new InvalidOperationException(
                $"Không khởi động được {executable}. Kiểm tra đường dẫn trong {FfmpegOptions.SectionName} và quyền thực thi của file.",
                ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process, executable);

            // Phân biệt hai lý do dừng: người dùng huỷ job thì ném OperationCanceledException để
            // tầng trên không ghi nhận là lỗi; còn quá giờ là sự cố thật và phải có tên riêng.
            cancellationToken.ThrowIfCancellationRequested();

            throw new TimeoutException(
                $"{executable} chạy quá {_options.TimeoutSeconds} giây và đã bị dừng. " +
                $"Nhật ký cuối:\n{TakeTail(stderr.ToString())}");
        }

        // WaitForExitAsync trả về ngay khi tiến trình kết thúc, nhưng hai luồng đọc pipe có thể
        // còn vài dòng chưa gom. WaitForExit() không tham số sẽ chờ nốt phần đó.
        process.WaitForExit();

        stopwatch.Stop();

        var result = new FfmpegRunResult(
            process.ExitCode,
            stdout.ToString(),
            stderr.ToString(),
            stopwatch.Elapsed);

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "{Executable} kết thúc với mã {ExitCode} sau {Elapsed}. Nhật ký cuối:\n{Tail}",
                executable,
                result.ExitCode,
                stopwatch.Elapsed,
                result.Tail());
        }

        return result;
    }

    private void KillQuietly(Process process, string executable)
    {
        try
        {
            // entireProcessTree: ffmpeg có thể đã sinh tiến trình con. Giết mỗi tiến trình cha là
            // để lại tiến trình mồ côi vẫn đang đọc/ghi vào file mà ta sắp xoá.
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không dừng được tiến trình {Executable} sau khi quá giờ.", executable);
        }
    }

    private static string TakeTail(string text, int lines = 12)
    {
        string[] all = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return string.Join('\n', all.TakeLast(lines)).Trim();
    }
}
