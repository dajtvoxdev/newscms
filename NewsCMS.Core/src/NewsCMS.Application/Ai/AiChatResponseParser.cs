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
                    using var doc = System.Text.Json.JsonDocument.Parse(json[open..(close + 1)]);
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
    /// Bỏ vỏ ```json ... ``` nếu model tự bọc. Tìm dấu ``` đầu tiên rồi lấy từ
    /// xuống dòng sau nó tới dấu ``` cuối cùng.
    /// </summary>
    private static string StripCodeFence(string json)
    {
        var fenceStart = json.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart < 0) return json;

        var afterFence = json.IndexOf('\n', fenceStart);
        var fenceEnd = json.LastIndexOf("```", StringComparison.Ordinal);
        if (afterFence > 0 && fenceEnd > afterFence)
            return json[(afterFence + 1)..fenceEnd].Trim();

        return json;
    }
}
