using System.Net;
using System.Text.Json;

namespace NewsCMS.Infrastructure.Ai.Tools;

internal static class NineRouterToolSupport
{
    public static string NormalizeBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var trimmed = url.Trim().TrimEnd('/');

        if (trimmed.EndsWith("/v1/search", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^"/v1/search".Length];
        else if (trimmed.EndsWith("/v1/web/fetch", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^"/v1/web/fetch".Length];
        else if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^"/v1".Length];

        return trimmed.TrimEnd('/');
    }

    public static bool IsAllowedApiBaseUrl(string url, out string? error)
    {
        error = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "URL không hợp lệ.";
            return false;
        }

        if (uri.Scheme is not ("https" or "http"))
        {
            error = "Chỉ hỗ trợ HTTP/HTTPS.";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;

            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                if (bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 169 && bytes[1] == 254))
                {
                    return true;
                }
            }
        }
        else
        {
            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (uri.Scheme == "https")
                return true;

            error = "Host công cộng phải dùng HTTPS.";
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

    public static NineRouterToolOptions ParseOptions(string? configJson)
    {
        var options = new NineRouterToolOptions();
        if (string.IsNullOrWhiteSpace(configJson))
            return options;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("provider", out var provider) &&
                provider.ValueKind == JsonValueKind.String)
            {
                options.Provider = provider.GetString()!;
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
        }

        return options;
    }
}

internal sealed class NineRouterToolOptions
{
    public string Provider { get; set; } = "serper";
    public int MaxResults { get; set; } = 5;
    public int MaxMarkdownCharsPerResult { get; set; } = 2000;
    public int MaxMarkdownChars { get; set; } = 6000;
}
