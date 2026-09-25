namespace AdVideo.Tests.Infrastructure;

/// <summary>
/// Tìm ffmpeg, ffprobe và một font có dấu trên máy đang chạy test.
/// </summary>
/// <remarks>
/// <para>
/// Test integration của AdVideo dùng <b>FFmpeg thật</b>, không giả lập. Phần lớn lỗi của bước ghép
/// là lỗi cú pháp filter graph — một dấu hai chấm chưa escape trong đường dẫn font, một nhãn
/// <c>[v0]</c> khai báo mà không dùng. Giả lập FFmpeg thì test xanh còn production đỏ, và đỏ ở
/// đúng chỗ đã gọi provider xong, tức là đã tiêu tiền.
/// </para>
/// <para>
/// Không tìm thấy công cụ thì <b>ném</b> chứ không bỏ qua test. Một test integration tự tắt trên
/// máy thiếu FFmpeg là một test không bao giờ chạy trên CI mà không ai nhận ra: bảng kết quả vẫn
/// xanh, chỉ là nó xanh vì không kiểm gì cả.
/// </para>
/// </remarks>
public static class ExternalTools
{
    /// <summary>Thư mục chứa ffmpeg/ffprobe, dùng khi chúng không nằm trong PATH.</summary>
    public const string FfmpegDirVariable = "ADVIDEO_TEST_FFMPEG_DIR";

    /// <summary>Đường dẫn đầy đủ tới file font dùng để vẽ nhãn AI.</summary>
    public const string FontFileVariable = "ADVIDEO_TEST_FONTFILE";

    private static readonly Lazy<string> LazyFfmpeg = new(() => LocateTool("ffmpeg"));
    private static readonly Lazy<string> LazyFfprobe = new(() => LocateTool("ffprobe"));
    private static readonly Lazy<string> LazyFont = new(LocateFont);

    /// <summary>Đường dẫn ffmpeg.</summary>
    public static string FfmpegPath => LazyFfmpeg.Value;

    /// <summary>Đường dẫn ffprobe.</summary>
    public static string FfprobePath => LazyFfprobe.Value;

    /// <summary>
    /// Font có dấu tiếng Việt để <c>drawtext</c> vẽ nhãn AI.
    /// </summary>
    /// <remarks>
    /// Thiếu font thì FFmpeg <b>không báo lỗi</b>: nó vẽ ô vuông. Nhãn "Video tạo bằng AI" thành
    /// một hàng ô vuông là vi phạm Luật TTNT 2025 mà không có dòng log nào.
    /// </remarks>
    public static string FontFile => LazyFont.Value;

    private static string LocateTool(string name)
    {
        string fileName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;

        string? fromVariable = Environment.GetEnvironmentVariable(FfmpegDirVariable);

        IEnumerable<string> directories = new[] { fromVariable }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Concat(CommonToolDirectories)
            .Where(d => !string.IsNullOrWhiteSpace(d))!;

        foreach (string directory in directories)
        {
            string candidate;

            try
            {
                candidate = Path.Combine(directory, fileName);
            }
            catch (ArgumentException)
            {
                // Một mục rác trong PATH (ký tự không hợp lệ) không được làm hỏng cả bộ test.
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Không tìm thấy {fileName}. Test integration của AdVideo dùng FFmpeg thật. " +
            $"Cài FFmpeg rồi cho nó vào PATH, hoặc đặt biến môi trường {FfmpegDirVariable} " +
            "trỏ tới thư mục chứa ffmpeg và ffprobe.");
    }

    private static string LocateFont()
    {
        string? fromVariable = Environment.GetEnvironmentVariable(FontFileVariable);

        if (!string.IsNullOrWhiteSpace(fromVariable) && File.Exists(fromVariable))
        {
            return fromVariable;
        }

        foreach (string candidate in CommonFontFiles)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Không tìm thấy font nào để vẽ nhãn AI. Đặt biến môi trường " +
            $"{FontFileVariable} trỏ tới một file .ttf có dấu tiếng Việt (ví dụ DejaVuSans.ttf).");
    }

    private static IEnumerable<string> CommonToolDirectories
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                yield return @"C:\ffmpeg\bin";
                yield return @"C:\ffmpeg\ffmpeg-master-latest-win64-gpl\bin";
                yield return @"C:\Program Files\ffmpeg\bin";
                yield break;
            }

            yield return "/usr/bin";
            yield return "/usr/local/bin";
            yield return "/opt/homebrew/bin";
            yield return "/snap/bin";
        }
    }

    private static IEnumerable<string> CommonFontFiles
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                string fonts = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

                yield return Path.Combine(fonts, "arial.ttf");
                yield return Path.Combine(fonts, "segoeui.ttf");
                yield return Path.Combine(fonts, "tahoma.ttf");
                yield break;
            }

            yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
            yield return "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf";
            yield return "/usr/share/fonts/TTF/DejaVuSans.ttf";
            yield return "/Library/Fonts/Arial.ttf";
            yield return "/System/Library/Fonts/Supplemental/Arial.ttf";
        }
    }
}
