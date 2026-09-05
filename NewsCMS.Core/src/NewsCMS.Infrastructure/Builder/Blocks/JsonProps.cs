using System.Text.Json;

namespace NewsCMS.Infrastructure.Builder.Blocks;

/// <summary>
/// Helper đọc props JSON của khối động. Nguyên tắc: KHÔNG BAO GIỜ throw — props do người dùng
/// nhập trong builder hoặc agent MCP sinh ra, một khoá sai kiểu không được phép làm sập cả trang.
/// Thiếu/sai thì trả fallback.
/// </summary>
internal sealed class JsonProps
{
    private readonly JsonElement _root;
    private readonly bool _valid;

    public JsonProps(string? json)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                _root = JsonDocument.Parse(json).RootElement;
                _valid = _root.ValueKind == JsonValueKind.Object;
            }
        }
        catch { /* ignore */ }
    }

    public bool Has(string key) => _valid && _root.TryGetProperty(key, out _);

    /// <summary>
    /// Số nguyên. Chấp nhận cả chuỗi số ("6") vì trait builder ghi giá trị ô nhập dạng chuỗi
    /// khi người dùng gõ tay — trước đây rơi âm thầm về mặc định.
    /// </summary>
    public int GetInt(string key, int fallback)
    {
        if (!_valid || !_root.TryGetProperty(key, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => fallback
        };
    }

    public string? GetString(string key)
    {
        if (_valid && _root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    public bool GetBool(string key, bool fallback)
    {
        if (!_valid || !_root.TryGetProperty(key, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => v.GetString() switch
            {
                "true" or "1" => true,
                "false" or "0" or "" => false,
                _ => fallback
            },
            JsonValueKind.Number when v.TryGetInt32(out var n) => n != 0,
            _ => fallback
        };
    }

    /// <summary>
    /// Mảng chuỗi. Chấp nhận cả một chuỗi đơn ("tin-tuc") và chuỗi phân tách bằng dấu phẩy
    /// ("tin-tuc,su-kien") để prop số nhiều mới đọc được cả dữ liệu người dùng/agent viết tay.
    /// Trả mảng rỗng khi thiếu — rỗng luôn mang nghĩa "không lọc".
    /// </summary>
    public IReadOnlyList<string> GetStringArray(string key)
    {
        if (!_valid || !_root.TryGetProperty(key, out var v)) return Array.Empty<string>();

        if (v.ValueKind == JsonValueKind.String)
            return Split(v.GetString());

        if (v.ValueKind != JsonValueKind.Array) return Array.Empty<string>();

        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
        }
        return list;
    }

    /// <summary>Mảng Guid; bỏ qua phần tử không parse được thay vì làm hỏng cả prop.</summary>
    public IReadOnlyList<Guid> GetGuidArray(string key)
    {
        if (!_valid || !_root.TryGetProperty(key, out var v)) return Array.Empty<Guid>();

        var raw = v.ValueKind switch
        {
            JsonValueKind.String => Split(v.GetString()),
            JsonValueKind.Array => v.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .ToList(),
            _ => (IReadOnlyList<string>)Array.Empty<string>()
        };

        var ids = new List<Guid>(raw.Count);
        foreach (var s in raw)
            if (Guid.TryParse(s, out var id) && id != Guid.Empty) ids.Add(id);
        return ids;
    }

    private static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
