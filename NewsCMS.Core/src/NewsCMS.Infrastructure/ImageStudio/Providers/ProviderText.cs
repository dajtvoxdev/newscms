using System.Text.Json;
using System.Text.RegularExpressions;

namespace NewsCMS.Infrastructure.ImageStudio.Providers;

/// <summary>Làm sạch chữ trả về từ provider trước khi lưu DB hay hiện ra màn hình.</summary>
public static partial class ProviderText
{
    /// <summary>
    /// Che key và cắt ngắn. Provider đôi khi dội lại header hay tham số trong thông báo lỗi; và một
    /// chuỗi base64 dài (ảnh) trong cột lỗi thì vừa vô ích vừa phình DB.
    /// </summary>
    public static string Redact(string? text, string? apiKey, int maxChars = 500)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string result = text;

        if (!string.IsNullOrEmpty(apiKey) && apiKey.Length >= 4)
        {
            result = result.Replace(apiKey, "***", StringComparison.Ordinal);
        }

        result = BearerPattern().Replace(result, "Bearer ***");
        result = KeyParamPattern().Replace(result, "$1***");
        result = SecretLikePattern().Replace(result, "***");
        result = Base64Pattern().Replace(result, "[base64]");
        result = result.Trim();

        return result.Length <= maxChars ? result : result[..maxChars].TrimEnd() + " …";
    }

    /// <summary>Lấy <c>error.message</c> (chuẩn OpenAI) hoặc <c>message</c>/<c>detail</c> từ thân lỗi JSON; không được thì trả nguyên văn.</summary>
    public static string ExtractErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out JsonElement error))
                {
                    if (error.ValueKind == JsonValueKind.String)
                    {
                        return error.GetString() ?? string.Empty;
                    }

                    if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String)
                    {
                        return message.GetString() ?? string.Empty;
                    }
                }

                foreach (string name in new[] { "message", "detail" })
                {
                    if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Không phải JSON — dùng nguyên văn.
        }

        return body;
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-\._~\+/=]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"((?:api[_-]?key|key|token|secret)\s*[=:]\s*)[^\s&""',;]{6,}", RegexOptions.IgnoreCase)]
    private static partial Regex KeyParamPattern();

    /// <summary>Dạng key phổ biến: sk-…, AIza…, fal key uuid:hex.</summary>
    [GeneratedRegex(@"\b(?:sk-[A-Za-z0-9_\-]{16,}|AIza[0-9A-Za-z_\-]{20,}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[0-9a-f]{16,})\b")]
    private static partial Regex SecretLikePattern();

    [GeneratedRegex(@"[A-Za-z0-9\+/]{200,}={0,2}")]
    private static partial Regex Base64Pattern();
}
