namespace AdVideo.Infrastructure.Media;

/// <summary>
/// Đường dẫn và giới hạn cho ffmpeg/ffprobe.
/// </summary>
/// <remarks>
/// Nằm ở config chứ không ở DB vì đây là đặc tính của MÁY đang chạy, không phải chính sách vận
/// hành: cùng một bản build chạy trên Windows dev và Ubuntu VPS thì hai đường dẫn khác nhau, còn
/// trần chi phí thì giống nhau.
/// </remarks>
public sealed class FfmpegOptions
{
    public const string SectionName = "AdVideo:Ffmpeg";

    /// <summary>Đường dẫn ffmpeg. Để tên trần thì tìm trong PATH.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Đường dẫn ffprobe. Để tên trần thì tìm trong PATH.</summary>
    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>Timeout một lệnh ffmpeg, giây. Ghép video 30 giây không nên quá vài phút.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>Số luồng cho ffmpeg. 0 = để ffmpeg tự quyết.</summary>
    public int Threads { get; set; }

    /// <summary>
    /// Font dùng cho chữ vẽ bằng <c>drawtext</c>.
    /// </summary>
    /// <remarks>
    /// <b>Phải là font có đủ dấu tiếng Việt.</b> Mọi chữ trên màn hình đều do ffmpeg vẽ (D4), nên
    /// font thiếu glyph là hỏng đúng cái việc mà cả quyết định D4 sinh ra để làm. DejaVu Sans có
    /// sẵn trên hầu hết bản Ubuntu và đủ dấu.
    /// </remarks>
    public string? FontFile { get; set; }

    /// <summary>
    /// Kiểm tra lúc khởi động rằng hai file thực thi tồn tại thật.
    /// </summary>
    /// <remarks>
    /// <b>Vì sao kiểm tra sớm và ném lỗi to:</b> đã từng có sự cố đúng kiểu này ở NewsCMS — cấu
    /// hình để đường dẫn Windows (<c>C:\ffmpeg\bin\ffmpeg.exe</c>) rồi deploy lên VPS Ubuntu.
    /// Không ai thấy lỗi gì cả, chỉ là video không bao giờ được nén và poster không bao giờ sinh
    /// ra. Hỏng im lặng ở tầng hạ tầng mất hàng giờ để tìm; hỏng lúc khởi động mất một phút.
    /// </remarks>
    public void Validate()
    {
        EnsureExecutable(FfmpegPath, nameof(FfmpegPath));
        EnsureExecutable(FfprobePath, nameof(FfprobePath));

        if (TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException($"{SectionName}:TimeoutSeconds phải lớn hơn 0.");
        }

        if (!string.IsNullOrWhiteSpace(FontFile) && !File.Exists(FontFile))
        {
            throw new InvalidOperationException(
                $"{SectionName}:FontFile trỏ tới {FontFile} nhưng file không tồn tại. " +
                "Chữ tiếng Việt vẽ bằng drawtext sẽ ra ô vuông nếu thiếu font.");
        }
    }

    private static void EnsureExecutable(string path, string settingName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{SectionName}:{settingName} không được rỗng.");
        }

        // Tên trần (không có dấu phân cách thư mục) nghĩa là "tìm trong PATH" — hợp lệ, và việc
        // tìm để hệ điều hành lo.
        bool looksLikePath = path.Contains('/', StringComparison.Ordinal)
            || path.Contains('\\', StringComparison.Ordinal);

        if (!looksLikePath)
        {
            return;
        }

        if (File.Exists(path))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{SectionName}:{settingName} trỏ tới {path} nhưng không có file nào ở đó. " +
            "Nếu đây là đường dẫn Windows mà ứng dụng đang chạy trên Linux, hãy ghi đè bằng " +
            $"appsettings.Production.json hoặc biến môi trường {SectionName.Replace(':', '_')}__{settingName}.");
    }
}
