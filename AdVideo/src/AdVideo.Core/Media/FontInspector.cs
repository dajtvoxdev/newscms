using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AdVideo.Core.Media;

/// <summary>Kết quả kiểm một file font.</summary>
/// <param name="Format"><c>ttf</c>, <c>otf</c> hoặc <c>ttc</c>. Null khi không phải font.</param>
/// <param name="Problem">Lý do từ chối. Null = dùng được.</param>
/// <param name="MissingCharacters">Ký tự cần vẽ mà font không có glyph.</param>
public sealed record FontInspection(string? Format, string? Problem, IReadOnlyList<string> MissingCharacters)
{
    public bool IsUsable => Problem is null;
}

/// <summary>
/// Kiểm file font trước khi cho worker dùng vẽ nhãn AI: đúng định dạng, và có glyph cho chữ tiếng Việt.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao phải đọc bảng cmap.</b> FFmpeg <c>drawtext</c> gặp ký tự font không có thì vẽ ô vuông
/// và KHÔNG báo lỗi — lỗi chỉ lộ ra khi có người xem video thành phẩm (D4). Kiểm lúc tải font lên
/// là chỗ rẻ nhất để chặn: một font Latin thuần sẽ bị từ chối ngay trên màn hình quản trị, với danh
/// sách ký tự thiếu.
/// </para>
/// <para>
/// Chỉ đọc đủ để trả lời hai câu đó: bảng thư mục sfnt và subtable cmap định dạng 4 / 12 (hai định
/// dạng mọi font Unicode hiện đại đều có). Không phải trình đọc font tổng quát. Thuần hàm, không
/// ném exception với dữ liệu hỏng — file rác chỉ nhận về <see cref="FontInspection.Problem"/>.
/// </para>
/// </remarks>
public static class FontInspector
{
    /// <summary>Mọi chữ cái tiếng Việt có dấu, hoa và thường. Font thiếu bất kỳ ký tự nào là font không dùng được.</summary>
    public const string VietnameseLetters =
        "aăâbcdđeêghiklmnoôơpqrstuưvxy" +
        "áàảãạắằẳẵặấầẩẫậéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ" +
        "AĂÂBCDĐEÊGHIKLMNOÔƠPQRSTUƯVXY" +
        "ÁÀẢÃẠẮẰẲẴẶẤẦẨẪẬÉÈẺẼẸẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌỐỒỔỖỘỚỜỞỠỢÚÙỦŨỤỨỪỬỮỰÝỲỶỸỴ";

    private const uint TagTrueType = 0x00010000;
    private const uint TagTrue = 0x74727565; // 'true' — font TrueType kiểu Mac cũ
    private const uint TagOtto = 0x4F54544F; // 'OTTO' — OpenType nhân CFF
    private const uint TagTtcf = 0x74746366; // 'ttcf' — bộ sưu tập nhiều font
    private const uint TagCmap = 0x636D6170; // 'cmap'

    /// <param name="data">Nội dung file.</param>
    /// <param name="requiredText">Chữ phải vẽ được, ngoài bảng chữ cái tiếng Việt — ví dụ chữ nhãn AI đang dùng.</param>
    public static FontInspection Inspect(ReadOnlySpan<byte> data, string? requiredText = null)
    {
        if (data.Length < 12)
        {
            return new FontInspection(null, "File quá nhỏ, không phải font.", []);
        }

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(data);
        string? format = tag switch
        {
            TagTrueType or TagTrue => "ttf",
            TagOtto => "otf",
            TagTtcf => "ttc",
            _ => null,
        };

        if (format is null)
        {
            return new FontInspection(
                null, "Không phải font TrueType/OpenType (.ttf, .otf, .ttc). WOFF/WOFF2 là định dạng cho web, FFmpeg không vẽ được.", []);
        }

        Func<int, bool>? hasGlyph;

        try
        {
            int fontOffset = 0;

            if (format == "ttc")
            {
                // Bộ sưu tập: lấy font ĐẦU TIÊN, đúng như FreeType (và do đó FFmpeg) làm khi không chỉ số font.
                uint count = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
                fontOffset = count == 0 ? -1 : checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[12..]));
            }

            hasGlyph = fontOffset < 0 ? null : ReadCmap(data, fontOffset);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            hasGlyph = null;
        }

        if (hasGlyph is null)
        {
            return new FontInspection(format, "Không đọc được bảng ký tự (cmap) của font — file hỏng hoặc font không có bảng Unicode.", []);
        }

        var missing = new List<string>();
        var seen = new HashSet<int>();

        try
        {
            foreach (Rune rune in (VietnameseLetters + (requiredText ?? string.Empty)).EnumerateRunes())
            {
                if (Rune.IsWhiteSpace(rune) || !seen.Add(rune.Value))
                {
                    continue;
                }

                if (!hasGlyph(rune.Value))
                {
                    missing.Add(rune.ToString());
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            // Bảng cmap khai số đoạn lớn hơn dữ liệu thật: file hỏng, không phải font thiếu chữ.
            return new FontInspection(format, "Bảng ký tự (cmap) của font bị hỏng.", []);
        }

        if (missing.Count > 0)
        {
            string sample = string.Concat(missing.Take(20));
            string more = missing.Count > 20 ? $" … (+{missing.Count - 20})" : string.Empty;

            return new FontInspection(
                format,
                $"Font thiếu {missing.Count.ToString(CultureInfo.InvariantCulture)} ký tự tiếng Việt: {sample}{more}. FFmpeg sẽ vẽ ô vuông thay cho các ký tự này mà không báo lỗi.",
                missing);
        }

        return new FontInspection(format, null, []);
    }

    /// <summary>Tìm bảng cmap và trả về hàm "có glyph cho code point này không". Null = không có subtable dùng được.</summary>
    private static Func<int, bool>? ReadCmap(ReadOnlySpan<byte> data, int fontOffset)
    {
        ReadOnlySpan<byte> font = data[fontOffset..];
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font[4..]);

        for (int i = 0; i < numTables; i++)
        {
            ReadOnlySpan<byte> record = font.Slice(12 + (i * 16), 16);

            if (BinaryPrimitives.ReadUInt32BigEndian(record) != TagCmap)
            {
                continue;
            }

            // Offset trong bảng thư mục tính từ ĐẦU FILE, kể cả với .ttc.
            int cmapOffset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]));

            return PickSubtable(data, cmapOffset);
        }

        return null;
    }

    private static Func<int, bool>? PickSubtable(ReadOnlySpan<byte> data, int cmapOffset)
    {
        ReadOnlySpan<byte> cmap = data[cmapOffset..];
        int count = BinaryPrimitives.ReadUInt16BigEndian(cmap[2..]);

        int best = -1;
        int bestRank = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = cmap.Slice(4 + (i * 8), 8);
            int platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            int encoding = BinaryPrimitives.ReadUInt16BigEndian(record[2..]);
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[4..]));

            // Ưu tiên bảng đầy đủ (Unicode full repertoire) rồi mới tới BMP. Bảng Symbol / Mac Roman
            // không dùng: chúng không mô tả Unicode.
            int rank = (platform, encoding) switch
            {
                (3, 10) => 0,
                (0, 4) or (0, 6) => 1,
                (3, 1) => 2,
                (0, _) => 3,
                _ => int.MaxValue,
            };

            if (rank < bestRank)
            {
                int format = BinaryPrimitives.ReadUInt16BigEndian(cmap[offset..]);

                if (format is 4 or 12)
                {
                    best = cmapOffset + offset;
                    bestRank = rank;
                }
            }
        }

        if (best < 0)
        {
            return null;
        }

        return BinaryPrimitives.ReadUInt16BigEndian(data[best..]) == 12
            ? ReadFormat12(data[best..])
            : ReadFormat4(data[best..]);
    }

    private static Func<int, bool> ReadFormat12(ReadOnlySpan<byte> table)
    {
        uint groups = BinaryPrimitives.ReadUInt32BigEndian(table[12..]);
        var ranges = new List<(int Start, int End, int FirstGlyph)>();

        for (int i = 0; i < groups; i++)
        {
            ReadOnlySpan<byte> group = table.Slice(16 + (i * 12), 12);
            ranges.Add((
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(group)),
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(group[4..])),
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(group[8..]))));
        }

        return codePoint => ranges.Any(r => codePoint >= r.Start && codePoint <= r.End && r.FirstGlyph + (codePoint - r.Start) != 0);
    }

    private static Func<int, bool> ReadFormat4(ReadOnlySpan<byte> table)
    {
        int segCount = BinaryPrimitives.ReadUInt16BigEndian(table[6..]) / 2;
        int endsAt = 14;
        int startsAt = endsAt + (segCount * 2) + 2;
        int deltasAt = startsAt + (segCount * 2);
        int rangeOffsetsAt = deltasAt + (segCount * 2);

        // Chép ra mảng: lambda không giữ được ReadOnlySpan. Chép tới hết file chứ không theo trường
        // length của subtable — trường đó là uint16 và bị cắt cụt ở font có bảng glyphId lớn.
        byte[] copy = table.ToArray();

        return codePoint =>
        {
            if (codePoint > 0xFFFF)
            {
                return false;
            }

            ReadOnlySpan<byte> t = copy;

            for (int i = 0; i < segCount; i++)
            {
                int end = BinaryPrimitives.ReadUInt16BigEndian(t[(endsAt + (i * 2))..]);

                if (end < codePoint)
                {
                    continue;
                }

                int start = BinaryPrimitives.ReadUInt16BigEndian(t[(startsAt + (i * 2))..]);

                if (start > codePoint)
                {
                    return false;
                }

                int delta = BinaryPrimitives.ReadInt16BigEndian(t[(deltasAt + (i * 2))..]);
                int rangeOffsetPosition = rangeOffsetsAt + (i * 2);
                int rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(t[rangeOffsetPosition..]);

                if (rangeOffset == 0)
                {
                    return ((codePoint + delta) & 0xFFFF) != 0;
                }

                int glyphAt = rangeOffsetPosition + rangeOffset + ((codePoint - start) * 2);

                if (glyphAt + 2 > t.Length)
                {
                    return false;
                }

                int glyph = BinaryPrimitives.ReadUInt16BigEndian(t[glyphAt..]);

                return glyph != 0 && ((glyph + delta) & 0xFFFF) != 0;
            }

            return false;
        };
    }
}
