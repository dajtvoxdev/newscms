namespace AdVideo.Core.Enums;

/// <summary>
/// Tỉ lệ khung hình. Là enum vì đây là tập đóng và UI cần liệt kê — khác với danh tính provider
/// (xem <see cref="AdVideo.Core.Providers.ProviderNames"/>) vốn cố ý để kiểu chuỗi.
/// </summary>
public enum AspectRatio
{
    /// <summary>Dọc — Reels, TikTok, Shorts. Mặc định vì khách hàng mục tiêu đăng mạng xã hội.</summary>
    Portrait9x16 = 0,

    Square1x1 = 1,

    Landscape16x9 = 2,
}

public static class AspectRatioExtensions
{
    /// <summary>Chuỗi gửi cho provider. Phần lớn provider nhận đúng dạng "9:16".</summary>
    public static string ToProviderString(this AspectRatio ratio) => ratio switch
    {
        AspectRatio.Portrait9x16 => "9:16",
        AspectRatio.Square1x1 => "1:1",
        AspectRatio.Landscape16x9 => "16:9",
        _ => throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "Tỉ lệ khung hình chưa được hỗ trợ."),
    };

    /// <summary>Độ phân giải thật để QC bước 9 đối chiếu. `shortSide` là cạnh 1080 theo mặc định.</summary>
    public static (int Width, int Height) ToResolution(this AspectRatio ratio, int longSide = 1920) => ratio switch
    {
        AspectRatio.Portrait9x16 => (longSide * 9 / 16, longSide),
        AspectRatio.Square1x1 => (longSide, longSide),
        AspectRatio.Landscape16x9 => (longSide, longSide * 9 / 16),
        _ => throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "Tỉ lệ khung hình chưa được hỗ trợ."),
    };
}
