using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AdVideo.Core.Providers.Descriptors;

/// <summary>
/// Đọc giá trị trong cây <see cref="JsonNode"/> theo đường dẫn khai trong descriptor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cú pháp:</b> <c>a.b[0].c</c>; chỉ số âm đếm từ cuối (<c>items[-1]</c>); <c>$</c> hoặc chuỗi
/// rỗng là gốc. Nhiều phương án nối bằng <c>||</c>: <c>url || video_url || share_url</c> — lấy
/// phương án đầu tiên cho ra giá trị khác null và khác chuỗi rỗng.
/// </para>
/// <para>
/// <b>Vì sao viết mới thay vì dùng <c>ProviderJson</c>:</b> <c>ProviderJson.FindString</c> chỉ trả
/// chuỗi và dò đệ quy theo tên khoá. Mọi nguồn mốc thời gian đều là <b>mảng số</b>, và dò đệ quy
/// với dữ liệu do người vận hành khai là đoán mò — descriptor phải nói chính xác chỗ nào.
/// </para>
/// <para>
/// So khớp tên khoá <b>phân biệt hoa thường</b>, đúng như JSON.
/// </para>
/// </remarks>
public static class JsonPathReader
{
    /// <summary>Kiểm cú pháp một đường dẫn. Dùng lúc lưu descriptor, để lỗi gõ nhầm lộ ra trước khi tốn tiền.</summary>
    public static bool TryParse(string? path, out string? error)
    {
        error = null;

        if (path is null)
        {
            error = "Đường dẫn trống.";

            return false;
        }

        string[] alternatives = SplitAlternatives(path).ToArray();

        foreach (string alternative in alternatives)
        {
            // "url ||" gần như chắc chắn là gõ dở; phương án rỗng sẽ âm thầm trả cả cây gốc.
            if (alternatives.Length > 1 && alternative.Length == 0)
            {
                error = $"Có phương án rỗng giữa các \"||\" trong \"{path}\".";

                return false;
            }

            if (!TryParseSegments(alternative, out _, out error))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Giá trị tại đường dẫn, hoặc null nếu không có phương án nào khớp.</summary>
    public static JsonNode? Read(JsonNode? root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!TryParse(path, out string? error))
        {
            throw new FormatException(error);
        }

        foreach (string alternative in SplitAlternatives(path))
        {
            TryParseSegments(alternative, out IReadOnlyList<PathSegment> segments, out _);

            JsonNode? value = Walk(root, segments);

            if (value is null || IsEmptyString(value))
            {
                continue;
            }

            return value;
        }

        return null;
    }

    /// <summary>Chuỗi tại đường dẫn. Số và bool được đổi sang chuỗi theo văn hoá bất biến.</summary>
    public static string? ReadString(JsonNode? root, string path) => AsString(Read(root, path));

    /// <summary>Số tại đường dẫn. Chấp nhận cả chuỗi chứa số (nhiều cổng trả giá dạng chuỗi).</summary>
    public static decimal? ReadDecimal(JsonNode? root, string path) => AsDecimal(Read(root, path));

    /// <summary>Mảng số tại đường dẫn. Null nếu không phải mảng hoặc có phần tử không phải số.</summary>
    public static IReadOnlyList<double>? ReadNumberArray(JsonNode? root, string path)
    {
        if (Read(root, path) is not JsonArray array)
        {
            return null;
        }

        var result = new List<double>(array.Count);

        foreach (JsonNode? item in array)
        {
            if (AsDecimal(item) is not { } number)
            {
                return null;
            }

            result.Add((double)number);
        }

        return result;
    }

    /// <summary>Mảng chuỗi tại đường dẫn. Null nếu không phải mảng hoặc có phần tử không đọc được thành chuỗi.</summary>
    public static IReadOnlyList<string>? ReadStringArray(JsonNode? root, string path)
    {
        if (Read(root, path) is not JsonArray array)
        {
            return null;
        }

        var result = new List<string>(array.Count);

        foreach (JsonNode? item in array)
        {
            if (AsString(item) is not { } text)
            {
                return null;
            }

            result.Add(text);
        }

        return result;
    }

    /// <summary>Dạng chuỗi của một giá trị vô hướng. Null cho object, array và null.</summary>
    public static string? AsString(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    /// <summary>Dạng số của một giá trị: số JSON, hoặc chuỗi chứa số theo văn hoá bất biến.</summary>
    public static decimal? AsDecimal(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.GetValueKind() switch
        {
            JsonValueKind.Number => decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d) ? d : null,
            JsonValueKind.String => decimal.TryParse(value.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal s) ? s : null,
            _ => null,
        };
    }

    private static IEnumerable<string> SplitAlternatives(string path) =>
        path.Split("||", StringSplitOptions.TrimEntries);

    private static bool IsEmptyString(JsonNode node) =>
        node is JsonValue value
        && value.GetValueKind() == JsonValueKind.String
        && value.GetValue<string>().Length == 0;

    private static JsonNode? Walk(JsonNode? current, IReadOnlyList<PathSegment> segments)
    {
        foreach (PathSegment segment in segments)
        {
            current = segment.Index is { } index
                ? current is JsonArray array ? ElementAt(array, index) : null
                : current is JsonObject obj && obj.TryGetPropertyValue(segment.Name!, out JsonNode? child) ? child : null;

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static JsonNode? ElementAt(JsonArray array, int index)
    {
        int actual = index < 0 ? array.Count + index : index;

        return actual >= 0 && actual < array.Count ? array[actual] : null;
    }

    private static bool TryParseSegments(string path, out IReadOnlyList<PathSegment> segments, out string? error)
    {
        var result = new List<PathSegment>();
        segments = result;
        error = null;

        string text = path.Trim();

        if (text is "" or "$")
        {
            return true;
        }

        if (text.StartsWith("$.", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        int i = 0;

        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                int close = text.IndexOf(']', i);

                if (close < 0
                    || !int.TryParse(text.AsSpan(i + 1, close - i - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int index))
                {
                    error = $"Chỉ số mảng không hợp lệ trong \"{path}\".";

                    return false;
                }

                result.Add(new PathSegment(null, index));
                i = close + 1;

                if (i < text.Length && text[i] is not ('.' or '['))
                {
                    error = $"Sau \"]\" phải là \".\", \"[\" hoặc hết đường dẫn trong \"{path}\".";

                    return false;
                }
            }
            else
            {
                int start = i;

                while (i < text.Length && text[i] is not ('.' or '['))
                {
                    if (char.IsWhiteSpace(text[i]) || text[i] is ']' or '|' or '{' or '}')
                    {
                        error = $"Ký tự \"{text[i]}\" không được phép trong tên khoá của \"{path}\".";

                        return false;
                    }

                    i++;
                }

                if (i == start)
                {
                    error = $"Tên khoá rỗng trong \"{path}\".";

                    return false;
                }

                result.Add(new PathSegment(text[start..i], null));
            }

            if (i < text.Length && text[i] == '.')
            {
                i++;

                if (i == text.Length)
                {
                    error = $"Đường dẫn \"{path}\" kết thúc bằng dấu chấm.";

                    return false;
                }
            }
        }

        return true;
    }

    private sealed record PathSegment(string? Name, int? Index);
}
