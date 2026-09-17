using NewsCMS.Application.Ai.Dtos;

namespace NewsCMS.Application.Ai;

/// <summary>
/// Tách JSON mà AI trả về thành 3 phần. Tách riêng khỏi service để test được
/// bằng unit test — đây là phần dễ vỡ nhất khi đổi model hoặc đổi prompt, vì
/// model hay bọc JSON trong ```json ... ``` hoặc thêm lời dẫn trước/sau.
/// </summary>
public static class AiChatResponseParser
{
    /// <param name="raw">Nguyên văn phản hồi của model.</param>
    /// <param name="isProduct">Đổi câu thông báo khi không tách được (sản phẩm / bài viết).</param>
    /// <returns>
    /// <c>ParsedStructured</c> = false khi không tìm được JSON hợp lệ có ít nhất
    /// một phần; khi đó <c>Result.Body</c> giữ nguyên văn để người dùng vẫn dùng được.
    /// </returns>
    public static (AiChatResult Result, bool ParsedStructured) Parse(string raw, bool isProduct)
    {
        var json = (raw ?? string.Empty).Trim();

        json = StripCodeFence(json);

        // Thử lần lượt từng dấu { cho tới } cuối cùng, không chỉ dấu { đầu tiên:
        // model hay viết "Kết quả {như sau}: {json}" và lời dẫn có ngoặc nhọn sẽ
        // làm đoạn cắt từ { đầu tiên không phải JSON hợp lệ.
        var close = json.LastIndexOf('}');
        if (close > 0)
        {
            var searchFrom = 0;
            while (searchFrom < close)
            {
                var open = json.IndexOf('{', searchFrom);
                if (open < 0 || open >= close) break;

                try
                {
                    // Model hay xuống dòng THẬT bên trong giá trị chuỗi thay vì
                    // escape thành \n — JSON cấm ký tự điều khiển thô trong chuỗi
                    // nên phải vá trước khi parse, nếu không mất cả 3 phần.
                    var candidate = EscapeRawControlChars(json[open..(close + 1)]);
                    using var doc = System.Text.Json.JsonDocument.Parse(candidate);
                    var root = doc.RootElement;

                    string Read(string name)
                    {
                        if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return string.Empty;
                        foreach (var prop in root.EnumerateObject())
                        {
                            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                                && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                return prop.Value.GetString() ?? string.Empty;
                            }
                        }
                        return string.Empty;
                    }

                    var title = Read("title");
                    var excerpt = Read("excerpt");
                    var body = Read("body");
                    var reply = Read("reply");

                    // JSON hợp lệ nhưng thiếu cả 3 phần: nhiều khả năng model trả về
                    // cấu trúc khác — thử dấu { kế tiếp trước khi bỏ cuộc.
                    if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(excerpt) || !string.IsNullOrWhiteSpace(body))
                    {
                        return (new AiChatResult(
                            string.IsNullOrWhiteSpace(reply) ? "Đã cập nhật bản nháp." : reply,
                            title, excerpt, body), true);
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Đoạn này không phải JSON — thử dấu { kế tiếp.
                }

                searchFrom = open + 1;
            }
        }

        return (new AiChatResult(
            isProduct
                ? "AI trả về văn bản tự do (không tách được mô tả ngắn). Nội dung nằm ở phần mô tả chi tiết."
                : "AI trả về văn bản tự do (không tách được tiêu đề/tóm tắt). Nội dung nằm ở phần nội dung.",
            string.Empty, string.Empty, raw ?? string.Empty), false);
    }

    /// <summary>
    /// Bỏ vỏ ```json ... ``` nếu model tự bọc. Chỉ nhận khi dấu ``` nằm ở ĐẦU
    /// phản hồi: thân bài có thể chứa ``` (ví dụ khối code mẫu), cắt theo dấu ```
    /// bất kỳ sẽ làm mất phần lớn nội dung.
    /// </summary>
    private static string StripCodeFence(string json)
    {
        if (!json.StartsWith("```", StringComparison.Ordinal)) return json;

        var afterFence = json.IndexOf('\n');
        if (afterFence < 0) return json;

        var fenceEnd = json.LastIndexOf("```", StringComparison.Ordinal);
        if (fenceEnd <= afterFence) return json;

        return json[(afterFence + 1)..fenceEnd].Trim();
    }

    /// <summary>
    /// Escape các ký tự điều khiển thô nằm BÊN TRONG chuỗi JSON. Model thường trả
    /// về thân bài với newline thật, mà JSON (RFC 8259) không cho phép ký tự
    /// &lt; 0x20 chưa escape trong chuỗi — không vá thì parse ném lỗi và mất trắng.
    /// </summary>
    private static string EscapeRawControlChars(string json)
    {
        // Đếm trước: phần lớn phản hồi không dính lỗi này, khỏi cấp phát chuỗi mới.
        var needsFix = false;
        foreach (var ch in json)
        {
            if (ch is '\n' or '\r' or '\t') { needsFix = true; break; }
        }
        if (!needsFix) return json;

        var sb = new System.Text.StringBuilder(json.Length + 16);
        var inString = false;
        var escaped = false;

        foreach (var ch in json)
        {
            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                sb.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                sb.Append(ch);
                continue;
            }

            if (inString)
            {
                switch (ch)
                {
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                    case '\b': sb.Append("\\b"); continue;
                    case '\f': sb.Append("\\f"); continue;
                }

                if (ch < 0x20)
                {
                    sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    continue;
                }
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
