using System.Buffers.Binary;
using AdVideo.Core.Media;
using FluentAssertions;
using Xunit;

namespace AdVideo.Core.Tests.Media;

public sealed class FontInspectorTests
{
    [Fact]
    public void Font_chi_co_ASCII_bi_tu_choi_va_liet_ke_chu_thieu()
    {
        byte[] font = SyntheticFont.Format4(first: 0x20, last: 0x7E);

        FontInspection result = FontInspector.Inspect(font);

        result.Format.Should().Be("ttf");
        result.IsUsable.Should().BeFalse();
        result.MissingCharacters.Should().Contain(["ă", "đ", "ơ", "ư", "ạ", "Ỹ"]);
        result.MissingCharacters.Should().NotContain("a", "chữ không dấu có trong bảng ASCII");
        result.Problem.Should().Contain("ô vuông");
    }

    [Fact]
    public void Font_phu_du_bang_chu_Viet_thi_dung_duoc()
    {
        // Mọi chữ tiếng Việt có dấu nằm trong U+00C0–U+1EF9.
        byte[] font = SyntheticFont.Format12(first: 0x20, last: 0x1EFF);

        FontInspection result = FontInspector.Inspect(font);

        result.IsUsable.Should().BeTrue(result.Problem);
        result.MissingCharacters.Should().BeEmpty();
    }

    [Fact]
    public void Chu_nhan_dang_dung_cung_phai_ve_duoc()
    {
        byte[] font = SyntheticFont.Format12(first: 0x20, last: 0x1EFF);

        FontInspection result = FontInspector.Inspect(font, "Tạo bởi AI ©\U0001F916");

        result.IsUsable.Should().BeFalse();
        result.MissingCharacters.Should().Equal("\U0001F916");
    }

    [Fact]
    public void Khoang_trang_trong_chu_nhan_khong_tinh_la_thieu()
    {
        byte[] font = SyntheticFont.Format12(first: 0x21, last: 0x1EFF);

        FontInspector.Inspect(font, "Nội dung AI").IsUsable.Should().BeTrue();
    }

    [Theory]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E, 0, 0, 0, 0, 0, 0 })] // "<html>"
    [InlineData(new byte[] { 0x77, 0x4F, 0x46, 0x46, 0, 1, 0, 0, 0, 0, 0, 0 })] // "wOFF"
    public void File_khong_phai_TTF_OTF_bi_tu_choi(byte[] data)
    {
        FontInspection result = FontInspector.Inspect(data);

        result.Format.Should().BeNull();
        result.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void File_qua_nho_bi_tu_choi_khong_nem()
    {
        FontInspector.Inspect([0, 1, 0]).IsUsable.Should().BeFalse();
    }

    [Fact]
    public void Dau_OTTO_nhung_than_rac_thi_bao_hong_chu_khong_nem()
    {
        byte[] data = new byte[64];
        "OTTO"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 200); // khai 200 bảng trong 64 byte

        FontInspection result = FontInspector.Inspect(data);

        result.Format.Should().Be("otf");
        result.IsUsable.Should().BeFalse();
        result.Problem.Should().Contain("cmap");
    }

    [Fact]
    public void Bo_suu_tap_ttc_doc_font_dau_tien()
    {
        byte[] inner = SyntheticFont.Format12(first: 0x20, last: 0x1EFF);

        // Header ttcf 16 byte, rồi font. Offset bảng trong font phải tính từ ĐẦU FILE, nên dời theo.
        byte[] ttc = new byte[16 + inner.Length];
        "ttcf"u8.CopyTo(ttc);
        BinaryPrimitives.WriteUInt32BigEndian(ttc.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(ttc.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(ttc.AsSpan(12), 16);
        inner.CopyTo(ttc, 16);
        BinaryPrimitives.WriteUInt32BigEndian(ttc.AsSpan(16 + 12 + 8), 16 + SyntheticFont.CmapOffset);

        FontInspection result = FontInspector.Inspect(ttc);

        result.Format.Should().Be("ttc");
        result.IsUsable.Should().BeTrue(result.Problem);
    }

    /// <summary>Dựng font tối thiểu: thư mục sfnt một bảng + bảng cmap một subtable.</summary>
    private static class SyntheticFont
    {
        public const int CmapOffset = 12 + 16;

        public static byte[] Format4(int first, int last)
        {
            // Hai đoạn: [first..last] và đoạn kết thúc bắt buộc 0xFFFF.
            const int segCount = 2;
            int length = 16 + (segCount * 8);
            byte[] sub = new byte[length];
            Span<byte> s = sub;

            BinaryPrimitives.WriteUInt16BigEndian(s, 4);
            BinaryPrimitives.WriteUInt16BigEndian(s[2..], (ushort)length);
            BinaryPrimitives.WriteUInt16BigEndian(s[6..], segCount * 2);
            BinaryPrimitives.WriteUInt16BigEndian(s[14..], (ushort)last);
            BinaryPrimitives.WriteUInt16BigEndian(s[16..], 0xFFFF);
            BinaryPrimitives.WriteUInt16BigEndian(s[20..], (ushort)first);
            BinaryPrimitives.WriteUInt16BigEndian(s[22..], 0xFFFF);
            BinaryPrimitives.WriteInt16BigEndian(s[24..], (short)(1 - first)); // first → glyph 1
            BinaryPrimitives.WriteInt16BigEndian(s[26..], 1);

            return Wrap(sub, platform: 3, encoding: 1);
        }

        public static byte[] Format12(int first, int last)
        {
            byte[] sub = new byte[16 + 12];
            Span<byte> s = sub;

            BinaryPrimitives.WriteUInt16BigEndian(s, 12);
            BinaryPrimitives.WriteUInt32BigEndian(s[4..], (uint)sub.Length);
            BinaryPrimitives.WriteUInt32BigEndian(s[12..], 1);
            BinaryPrimitives.WriteUInt32BigEndian(s[16..], (uint)first);
            BinaryPrimitives.WriteUInt32BigEndian(s[20..], (uint)last);
            BinaryPrimitives.WriteUInt32BigEndian(s[24..], 1);

            return Wrap(sub, platform: 3, encoding: 10);
        }

        private static byte[] Wrap(byte[] subtable, int platform, int encoding)
        {
            int cmapLength = 4 + 8 + subtable.Length;
            byte[] font = new byte[CmapOffset + cmapLength];
            Span<byte> f = font;

            BinaryPrimitives.WriteUInt32BigEndian(f, 0x00010000);
            BinaryPrimitives.WriteUInt16BigEndian(f[4..], 1);

            "cmap"u8.CopyTo(f[12..]);
            BinaryPrimitives.WriteUInt32BigEndian(f[(12 + 8)..], CmapOffset);
            BinaryPrimitives.WriteUInt32BigEndian(f[(12 + 12)..], (uint)cmapLength);

            Span<byte> cmap = f[CmapOffset..];
            BinaryPrimitives.WriteUInt16BigEndian(cmap[2..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(cmap[4..], (ushort)platform);
            BinaryPrimitives.WriteUInt16BigEndian(cmap[6..], (ushort)encoding);
            BinaryPrimitives.WriteUInt32BigEndian(cmap[8..], 12);
            subtable.CopyTo(cmap[12..]);

            return font;
        }
    }
}
