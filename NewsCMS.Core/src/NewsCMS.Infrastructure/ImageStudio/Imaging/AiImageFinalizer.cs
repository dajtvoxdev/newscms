using System.Text;
using System.Xml.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.Processing;

namespace NewsCMS.Infrastructure.ImageStudio.Imaging;

public sealed record FinalizedImage(byte[] Bytes, string MimeType, string Extension, int Width, int Height);

/// <summary>
/// Bước cuối của mọi ảnh AI trước khi lưu: kiểm là ảnh thật, xoá metadata của provider, <b>gắn nhãn
/// AI vào metadata</b> (IPTC <c>DigitalSourceType</c> trong XMP + EXIF <c>Software</c>), mã hoá lại.
/// </summary>
/// <remarks>
/// Nhãn nằm trong file nên đi theo ảnh cả khi bị tải về dùng chỗ khác. ImageSharp giữ nguyên profile
/// XMP khi nạp rồi lưu lại, nên bước tối ưu ảnh lúc đưa vào thư viện media không làm mất nhãn.
/// </remarks>
public static class AiImageFinalizer
{
    /// <summary>Ảnh tạo mới hoàn toàn bằng model.</summary>
    public const string TrainedAlgorithmicMedia = "http://cv.iptc.org/newscodes/digitalsourcetype/trainedAlgorithmicMedia";

    /// <summary>Ảnh thật được sửa một phần bằng model.</summary>
    public const string CompositeWithTrainedAlgorithmicMedia = "http://cv.iptc.org/newscodes/digitalsourcetype/compositeWithTrainedAlgorithmicMedia";

    public const string SoftwareName = "NewsCMS ImageStudio (AI)";

    /// <summary>Chặn bom giải nén: ảnh khai kích thước khổng lồ bị từ chối trước khi giải mã.</summary>
    public const long MaxPixels = 40_000_000;

    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Iptc4xmpExt = "http://iptc.org/std/Iptc4xmpExt/2008-02-29/";
    private static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace XmpMeta = "adobe:ns:meta/";

    /// <param name="maxEdge">Có giá trị thì thu nhỏ để cạnh dài không vượt quá (ảnh demo, ảnh xem trước).</param>
    /// <exception cref="InvalidDataException">Không phải ảnh, hoặc quá lớn.</exception>
    public static FinalizedImage Finalize(byte[] input, string? outputFormat, string digitalSourceType, int? maxEdge = null)
    {
        using Image image = LoadChecked(input, "Nhà cung cấp trả về dữ liệu không phải ảnh.");
        ShrinkTo(image, maxEdge);

        // Bỏ metadata provider gửi kèm (có thể chứa prompt nội bộ, id tài khoản…), chỉ giữ nhãn của ta.
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Software, SoftwareName);
        image.Metadata.ExifProfile = exif;
        image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(BuildXmp(digitalSourceType)));

        (IImageEncoder encoder, string mime, string extension) = Encoder(outputFormat);

        using var output = new MemoryStream();
        image.Save(output, encoder);

        return new FinalizedImage(output.ToArray(), mime, extension, image.Width, image.Height);
    }

    /// <summary>
    /// Ảnh người dùng/quản trị tải lên (không phải ảnh AI): giải mã lại để chắc là ảnh thật, xoay theo
    /// EXIF, bỏ toàn bộ metadata (vị trí GPS, máy chụp…), thu nhỏ, mã hoá lại.
    /// </summary>
    /// <exception cref="InvalidDataException">Không phải ảnh, hoặc quá lớn.</exception>
    public static FinalizedImage Normalize(byte[] input, string? outputFormat, int? maxEdge = null)
    {
        using Image image = LoadChecked(input, "File tải lên không phải ảnh.");
        image.Mutate(x => x.AutoOrient());
        ShrinkTo(image, maxEdge);

        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        (IImageEncoder encoder, string mime, string extension) = Encoder(outputFormat);

        using var output = new MemoryStream();
        image.Save(output, encoder);

        return new FinalizedImage(output.ToArray(), mime, extension, image.Width, image.Height);
    }

    private static Image LoadChecked(byte[] input, string notImageMessage)
    {
        ImageInfo info;

        try
        {
            info = Image.Identify(input);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidDataException(notImageMessage, ex);
        }

        if ((long)info.Width * info.Height > MaxPixels)
        {
            throw new InvalidDataException("Ảnh quá lớn (trên 40 megapixel).");
        }

        try
        {
            return Image.Load(input);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidDataException(notImageMessage, ex);
        }
    }

    private static void ShrinkTo(Image image, int? maxEdge)
    {
        if (maxEdge is { } max && max > 0 && (image.Width > max || image.Height > max))
        {
            image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(max, max), Sampler = KnownResamplers.Lanczos3 }));
        }
    }

    /// <summary>Đọc lại <c>DigitalSourceType</c> — để test và để trang chi tiết kiểm nhãn.</summary>
    public static string? ReadDigitalSourceType(byte[] bytes)
    {
        XmpProfile? xmp = Image.Identify(bytes).Metadata.XmpProfile;
        XDocument? doc = xmp?.GetDocument();

        return doc?.Descendants(Rdf + "Description")
            .Select(d => (string?)d.Attribute(Iptc4xmpExt + "DigitalSourceType"))
            .FirstOrDefault(v => v is not null);
    }

    private static (IImageEncoder Encoder, string Mime, string Extension) Encoder(string? format) => format?.ToLowerInvariant() switch
    {
        "jpeg" or "jpg" => (new JpegEncoder { Quality = 90 }, "image/jpeg", "jpg"),
        "webp" => (new WebpEncoder { Quality = 90 }, "image/webp", "webp"),
        _ => (new PngEncoder(), "image/png", "png"),
    };

    private static string BuildXmp(string digitalSourceType)
    {
        var doc = new XDocument(
            new XElement(XmpMeta + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", XmpMeta),
                new XElement(Rdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", Rdf),
                    new XElement(Rdf + "Description",
                        new XAttribute(Rdf + "about", ""),
                        new XAttribute(XNamespace.Xmlns + "Iptc4xmpExt", Iptc4xmpExt),
                        new XAttribute(XNamespace.Xmlns + "xmp", Xmp),
                        new XAttribute(Iptc4xmpExt + "DigitalSourceType", digitalSourceType),
                        new XAttribute(Xmp + "CreatorTool", SoftwareName)))));

        return "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
               + doc.ToString(SaveOptions.DisableFormatting)
               + "<?xpacket end=\"w\"?>";
    }
}
