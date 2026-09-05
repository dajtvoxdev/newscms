using System.Net;
using System.Text.Json;

namespace NewsCMS.Infrastructure.Ai.Tools;

internal static class FirecrawlToolSupport
{
    private static readonly HashSet<string> AllowedApiHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.firecrawl.dev"
    };

    public static bool IsAllowedApiBaseUrl(string url, out string? error)
    {
        error = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "URL không hợp lệ.";
            return false;
        }

        if (uri.Scheme != "https")
        {
            error = "Chỉ hỗ trợ HTTPS.";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                error = "Không được phép kết nối đến địa chỉ loopback/link-local.";
                return false;
            }

            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                if (bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 169 && bytes[1] == 254))
                {
                    error = "Không được phép kết nối đến địa chỉ private.";
                    return false;
                }
            }
        }
        else
        {
            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            {
                error = "Không được phép kết nối đến hostname local.";
                return false;
            }

            if (!AllowedApiHosts.Contains(uri.Host))
            {
                error = $"Host '{uri.Host}' không nằm trong danh sách cho phép.";
                return false;
            }
        }

        return true;
    }

    public static bool IsAllowedTargetUrl(string url, out string? error)
    {
        error = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "URL mục tiêu không hợp lệ.";
            return false;
        }

        if (uri.Scheme != "https")
        {
            error = "Chỉ cho phép scrape URL HTTPS.";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                error = "Không được scrape địa chỉ loopback/link-local.";
                return false;
            }

            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                if (bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 169 && bytes[1] == 254))
                {
                    error = "Không được scrape địa chỉ private.";
                    return false;
                }
            }
        }
        else if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            error = "Không được scrape hostname local.";
            return false;
        }

        return true;
    }

    public static string TrimForPrompt(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Trim();
        if (text.Length <= maxChars)
            return text;

        return text[..maxChars].TrimEnd() + " ...";
    }

    public static List<string>? ReadStringArray(JsonElement args, string propertyName, int maxItems = 8)
    {
        if (!args.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            var text = item.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            items.Add(text);
            if (items.Count >= maxItems)
                break;
        }

        return items.Count == 0 ? null : items;
    }

    public static bool? ReadBoolean(JsonElement args, string propertyName)
    {
        if (!args.TryGetProperty(propertyName, out var value) || value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            return null;

        return value.GetBoolean();
    }

    public static int? ReadInt(JsonElement args, string propertyName)
    {
        if (!args.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;

        return value.TryGetInt32(out var result) ? result : null;
    }

    public static string? ReadString(JsonElement args, string propertyName)
    {
        if (!args.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString()?.Trim();
    }

    public static FirecrawlToolOptions ParseOptions(string? configJson)
    {
        var options = new FirecrawlToolOptions();
        if (string.IsNullOrWhiteSpace(configJson))
            return options;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("defaultIncludeContent", out var includeContent) &&
                includeContent.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                options.DefaultIncludeContent = includeContent.GetBoolean();
            }

            if (root.TryGetProperty("maxResults", out var maxResults) &&
                maxResults.ValueKind == JsonValueKind.Number &&
                maxResults.TryGetInt32(out var maxResultsValue))
            {
                options.MaxResults = Math.Clamp(maxResultsValue, 1, 10);
            }

            if (root.TryGetProperty("maxMarkdownCharsPerResult", out var perResult) &&
                perResult.ValueKind == JsonValueKind.Number &&
                perResult.TryGetInt32(out var perResultValue))
            {
                options.MaxMarkdownCharsPerResult = Math.Clamp(perResultValue, 600, 4000);
            }

            if (root.TryGetProperty("maxMarkdownChars", out var markdownChars) &&
                markdownChars.ValueKind == JsonValueKind.Number &&
                markdownChars.TryGetInt32(out var markdownCharsValue))
            {
                options.MaxMarkdownChars = Math.Clamp(markdownCharsValue, 1000, 12000);
            }
        }
        catch
        {
            // Tool vẫn chạy với default nếu config lỗi.
        }

        return options;
    }
}

internal sealed class FirecrawlToolOptions
{
    public bool DefaultIncludeContent { get; set; } = true;
    public int MaxResults { get; set; } = 4;
    public int MaxMarkdownCharsPerResult { get; set; } = 1800;
    public int MaxMarkdownChars { get; set; } = 5000;
}
