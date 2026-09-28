namespace AdVideo.Core.Media;

/// <summary>
/// Nhận dạng file mẫu giọng bằng magic byte — cùng lý do với <see cref="ProductImageFormat"/>: không tin
/// đuôi file hay Content-Type client gửi.
/// </summary>
public static class VoiceSampleFormat
{
    /// <summary>Trần một file mẫu. ElevenLabs nhận tối đa ~10 MB mỗi file.</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    /// <summary>Số file mẫu tối đa một lần clone.</summary>
    public const int MaxFiles = 5;

    /// <summary>Trả (content type, đuôi file), hoặc null nếu không phải định dạng audio được nhận.</summary>
    public static (string ContentType, string Extension)? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return null;
        }

        if (data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WAVE"u8))
        {
            return ("audio/wav", ".wav");
        }

        if (data[..3].SequenceEqual("ID3"u8) || (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0))
        {
            return ("audio/mpeg", ".mp3");
        }

        if (data[..4].SequenceEqual("OggS"u8))
        {
            return ("audio/ogg", ".ogg");
        }

        if (data[..4].SequenceEqual("fLaC"u8))
        {
            return ("audio/flac", ".flac");
        }

        if (data[4..8].SequenceEqual("ftyp"u8))
        {
            // Ghi âm trên iPhone / Android thường là .m4a (MP4 chỉ có audio).
            return ("audio/mp4", ".m4a");
        }

        if (data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3)
        {
            // Ghi âm ngay trong trình duyệt (MediaRecorder) ra WebM.
            return ("audio/webm", ".webm");
        }

        return null;
    }
}
