using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.ImageStudio;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NewsCMS.Infrastructure.ImageStudio.Providers;

/// <summary>
/// Provider giả cho phát triển và test: vẽ một ảnh gradient có màu theo prompt, đúng tỉ lệ yêu cầu.
/// Không gọi mạng, không tốn tiền. Chỉ đăng ký được khi <c>ImageStudio:EnableFakeProvider = true</c>.
/// </summary>
/// <remarks>Prompt chứa <c>[fake-error]</c> thì trả lỗi vi phạm nội dung — để thử đường báo lỗi trên giao diện.</remarks>
public sealed class FakeImageProvider : IImageProvider
{
    public const string ErrorTrigger = "[fake-error]";

    private const int LongEdge = 768;

    public ImageProviderAdapter Adapter => ImageProviderAdapter.Fake;

    public async Task<ImageProviderResult> GenerateAsync(ImageGenerateRequest request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromMilliseconds(250), ct);

        if (request.Prompt.Contains(ErrorTrigger, StringComparison.OrdinalIgnoreCase))
        {
            return ImageProviderResult.Failure(ImageProviderErrorCodes.ContentPolicy,
                "Nhà cung cấp từ chối nội dung này (vi phạm chính sách). Hãy đổi mô tả.", 400, (int)stopwatch.ElapsedMilliseconds);
        }

        (int width, int height) = Dimensions(request.Size);

        // Màu theo prompt + một chút ngẫu nhiên: các biến thể của cùng một lần tạo trông khác nhau như thật.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Prompt + Guid.NewGuid().ToString("N")[..4]));
        var from = new Rgba32(hash[0], hash[1], hash[2]);
        var to = new Rgba32(hash[3], hash[4], hash[5]);

        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);

                for (int x = 0; x < row.Length; x++)
                {
                    float t = (x + y) / (float)(width + height);
                    row[x] = new Rgba32(
                        (byte)(from.R + (to.R - from.R) * t),
                        (byte)(from.G + (to.G - from.G) * t),
                        (byte)(from.B + (to.B - from.B) * t));
                }
            }
        });

        using var output = new MemoryStream();
        await image.SaveAsPngAsync(output, ct);

        return ImageProviderResult.Success(new ImageData(output.ToArray(), "image/png"), 200, (int)stopwatch.ElapsedMilliseconds);
    }

    /// <summary><c>1536x1024</c> hoặc <c>16:9</c> → kích thước cùng tỉ lệ, cạnh dài 768 cho nhanh.</summary>
    private static (int Width, int Height) Dimensions(string size)
    {
        if (!ImageStudioRules.TryRatio(size, out double ratio))
        {
            ratio = 1;
        }

        return ratio >= 1
            ? (LongEdge, Math.Max(1, (int)Math.Round(LongEdge / ratio)))
            : (Math.Max(1, (int)Math.Round(LongEdge * ratio)), LongEdge);
    }
}
