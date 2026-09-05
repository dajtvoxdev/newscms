namespace NewsCMS.Infrastructure.Ar;

/// <summary>
/// Merges several MindAR <c>.mind</c> files into one multi-target file.
///
/// A .mind file is msgpack: <c>{ "v": &lt;version&gt;, "dataList": [ &lt;target&gt;, ... ] }</c>.
/// Each individually-compiled file has one entry in <c>dataList</c>. Merging is therefore
/// a pure structural concat of the <c>dataList</c> arrays — no image re-compilation (no
/// TensorFlow) is needed. We extract each target's raw msgpack bytes verbatim and re-emit
/// them under a single <c>dataList</c>, so the binary feature/tracking data is preserved
/// byte-for-byte.
/// </summary>
public static class MindFileMerger
{
    private const byte Version = 2;

    /// <summary>Number of targets (dataList entries) contained in a single .mind file.</summary>
    public static int CountTargets(byte[] mind)
    {
        var r = new Reader(mind);
        return ReadDataList(r, _ => { });
    }

    /// <summary>
    /// Merge the given .mind files (in order) into one. The resulting target indices follow
    /// input order: file[0]'s targets come first, then file[1]'s, and so on.
    /// </summary>
    public static byte[] Merge(IReadOnlyList<byte[]> minds)
    {
        var elements = new List<ArraySegment<byte>>();
        foreach (var data in minds)
        {
            var r = new Reader(data);
            ReadDataList(r, seg => elements.Add(seg));
        }

        using var ms = new MemoryStream();
        ms.WriteByte(0x82);                              // fixmap, 2 entries
        WriteStr(ms, "v");
        ms.WriteByte(Version);                           // positive fixint version
        WriteStr(ms, "dataList");
        WriteArrayHeader(ms, elements.Count);
        foreach (var e in elements)
            ms.Write(e.Array!, e.Offset, e.Count);
        return ms.ToArray();
    }

    /// <summary>
    /// Walk the top-level map, find "dataList", and emit each element's raw byte segment.
    /// Returns the number of elements found.
    /// </summary>
    private static int ReadDataList(Reader r, Action<ArraySegment<byte>> onElement)
    {
        int mapCount = r.ReadMapHeader();
        int found = 0;
        for (int i = 0; i < mapCount; i++)
        {
            string key = r.ReadString();
            if (key == "dataList")
            {
                int arr = r.ReadArrayHeader();
                for (int j = 0; j < arr; j++)
                {
                    int start = r.Pos;
                    r.SkipValue();
                    onElement(new ArraySegment<byte>(r.Data, start, r.Pos - start));
                }
                found = arr;
            }
            else
            {
                r.SkipValue(); // skip the value for any other key (e.g. "v")
            }
        }
        return found;
    }

    private static void WriteStr(Stream s, string str)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(str);
        if (bytes.Length < 32) s.WriteByte((byte)(0xa0 | bytes.Length));
        else { s.WriteByte(0xd9); s.WriteByte((byte)bytes.Length); }
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WriteArrayHeader(Stream s, int count)
    {
        if (count < 16) s.WriteByte((byte)(0x90 | count));
        else if (count <= 0xffff) { s.WriteByte(0xdc); s.WriteByte((byte)(count >> 8)); s.WriteByte((byte)count); }
        else { s.WriteByte(0xdd); s.WriteByte((byte)(count >> 24)); s.WriteByte((byte)(count >> 16)); s.WriteByte((byte)(count >> 8)); s.WriteByte((byte)count); }
    }

    /// <summary>Minimal forward-only msgpack reader: enough to navigate maps/arrays and skip any value.</summary>
    private sealed class Reader
    {
        public byte[] Data { get; }
        public int Pos { get; private set; }

        public Reader(byte[] data) => Data = data;

        private byte ReadByte() => Data[Pos++];

        public int ReadMapHeader()
        {
            byte b = ReadByte();
            if (b >= 0x80 && b <= 0x8f) return b & 0x0f;
            if (b == 0xde) return ReadU16();
            if (b == 0xdf) return (int)ReadU32();
            throw new FormatException($"Expected map header, got 0x{b:x2}.");
        }

        public int ReadArrayHeader()
        {
            byte b = ReadByte();
            if (b >= 0x90 && b <= 0x9f) return b & 0x0f;
            if (b == 0xdc) return ReadU16();
            if (b == 0xdd) return (int)ReadU32();
            throw new FormatException($"Expected array header, got 0x{b:x2}.");
        }

        public string ReadString()
        {
            byte b = ReadByte();
            int len;
            if (b >= 0xa0 && b <= 0xbf) len = b & 0x1f;
            else if (b == 0xd9) len = ReadByte();
            else if (b == 0xda) len = ReadU16();
            else if (b == 0xdb) len = (int)ReadU32();
            else throw new FormatException($"Expected string, got 0x{b:x2}.");
            var s = System.Text.Encoding.UTF8.GetString(Data, Pos, len);
            Pos += len;
            return s;
        }

        private int ReadU16() { int v = (Data[Pos] << 8) | Data[Pos + 1]; Pos += 2; return v; }
        private uint ReadU32() { uint v = ((uint)Data[Pos] << 24) | ((uint)Data[Pos + 1] << 16) | ((uint)Data[Pos + 2] << 8) | Data[Pos + 3]; Pos += 4; return v; }

        /// <summary>Advance past exactly one msgpack value (recursing into containers).</summary>
        public void SkipValue()
        {
            byte b = ReadByte();
            // positive / negative fixint, nil, bools
            if (b <= 0x7f || b >= 0xe0) return;          // fixint
            if (b == 0xc0 || b == 0xc2 || b == 0xc3) return; // nil/false/true

            // fixstr
            if (b >= 0xa0 && b <= 0xbf) { Pos += b & 0x1f; return; }
            // fixmap
            if (b >= 0x80 && b <= 0x8f) { SkipValues((b & 0x0f) * 2); return; }
            // fixarray
            if (b >= 0x90 && b <= 0x9f) { SkipValues(b & 0x0f); return; }

            switch (b)
            {
                case 0xcc: Pos += 1; return;             // uint8
                case 0xcd: Pos += 2; return;             // uint16
                case 0xce: Pos += 4; return;             // uint32
                case 0xcf: Pos += 8; return;             // uint64
                case 0xd0: Pos += 1; return;             // int8
                case 0xd1: Pos += 2; return;             // int16
                case 0xd2: Pos += 4; return;             // int32
                case 0xd3: Pos += 8; return;             // int64
                case 0xca: Pos += 4; return;             // float32
                case 0xcb: Pos += 8; return;             // float64
                // NB: read the length into a local FIRST. `Pos += ReadXxx()` would capture
                // the old Pos before ReadXxx() advances it, dropping the length-field bytes.
                case 0xd9: { int n = ReadByte(); Pos += n; return; }      // str8
                case 0xda: { int n = ReadU16(); Pos += n; return; }       // str16
                case 0xdb: { int n = (int)ReadU32(); Pos += n; return; }  // str32
                case 0xc4: { int n = ReadByte(); Pos += n; return; }      // bin8
                case 0xc5: { int n = ReadU16(); Pos += n; return; }       // bin16
                case 0xc6: { int n = (int)ReadU32(); Pos += n; return; }  // bin32
                case 0xdc: SkipValues(ReadU16()); return;        // array16
                case 0xdd: SkipValues((int)ReadU32()); return;   // array32
                case 0xde: SkipValues(ReadU16() * 2); return;    // map16
                case 0xdf: SkipValues((int)ReadU32() * 2); return; // map32
                case 0xd4: Pos += 1 + 1; return;         // fixext1
                case 0xd5: Pos += 1 + 2; return;         // fixext2
                case 0xd6: Pos += 1 + 4; return;         // fixext4
                case 0xd7: Pos += 1 + 8; return;         // fixext8
                case 0xd8: Pos += 1 + 16; return;        // fixext16
                case 0xc7: { int len = ReadByte(); Pos += 1 + len; return; }      // ext8
                case 0xc8: { int len = ReadU16(); Pos += 1 + len; return; }       // ext16
                case 0xc9: { int len = (int)ReadU32(); Pos += 1 + len; return; }  // ext32
                default: throw new FormatException($"Unknown msgpack byte 0x{b:x2}.");
            }
        }

        private void SkipValues(int n)
        {
            for (int i = 0; i < n; i++) SkipValue();
        }
    }
}
