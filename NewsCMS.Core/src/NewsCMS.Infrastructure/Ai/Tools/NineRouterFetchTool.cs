using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;

namespace NewsCMS.Infrastructure.Ai.Tools;

public sealed class NineRouterFetchTool : IAiTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NineRouterFetchTool> _logger;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public NineRouterFetchTool(IHttpClientFactory httpClientFactory, ILogger<NineRouterFetchTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Key => "ninerouter_fetch";
    public string DisplayName => "Web Fetch (9Router)";
    public string Description => "Đọc chi tiết một URL cụ thể qua 9Router (hỗ trợ Jina Reader, Firecrawl, Tavily, Exa) và trả về markdown sạch. Dùng sau bước search để xác minh nguồn quan trọng.";
    public string ParametersJsonSchema => @"{
        ""type"": ""object"",
        ""properties"": {
            ""url"": { ""type"": ""string"", ""description"": ""URL HTTPS cần fetch chi tiết"" },
            ""format"": { ""type"": ""string"", ""enum"": [""markdown"", ""text"", ""html""], ""description"": ""Định dạng trả về (mặc định markdown)"" },
            ""max_characters"": { ""type"": ""integer"", ""description"": ""Giới hạn ký tự trả về"" }
        },
        ""required"": [""url""]
    }";

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct = default)
    {
        var targetUrl = NineRouterToolSupport.ReadString(args, "url");
        if (string.IsNullOrWhiteSpace(targetUrl))
            return "Lỗi: thiếu URL cần fetch.";

        if (string.IsNullOrWhiteSpace(ctx.BaseUrl))
            return "Lỗi cấu hình: chưa có 9Router URL.";

        var baseUrl = NineRouterToolSupport.NormalizeBaseUrl(ctx.BaseUrl);
        if (!NineRouterToolSupport.IsAllowedApiBaseUrl(baseUrl, out var baseUrlError))
        {
            _logger.LogWarning("Tool '{ToolKey}' blocked request to disallowed URL: {Url} — {Reason}", Key, baseUrl, baseUrlError);
            return $"Lỗi cấu hình: {baseUrlError}";
        }

        if (!FirecrawlToolSupport.IsAllowedTargetUrl(targetUrl, out var targetUrlError))
            return $"Lỗi URL mục tiêu: {targetUrlError}";

        var options = NineRouterToolSupport.ParseOptions(ctx.ConfigJson);
        var format = NineRouterToolSupport.ReadString(args, "format") ?? "markdown";
        var maxChars = NineRouterToolSupport.ReadInt(args, "max_characters") ?? options.MaxMarkdownChars;

        var client = _httpClientFactory.CreateClient("ai-tools");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/web/fetch");

        if (!string.IsNullOrWhiteSpace(ctx.ApiKey))
            request.Headers.Add("Authorization", $"Bearer {ctx.ApiKey}");

        var body = new FetchRequest
        {
            Model = options.Provider,
            Url = targetUrl,
            Format = format,
            MaxCharacters = maxChars > 0 ? maxChars : null
        };
        request.Content = JsonContent.Create(body, options: SerializerOptions);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = NineRouterToolSupport.TrimForPrompt(await response.Content.ReadAsStringAsync(ct), 500);
            _logger.LogWarning("9Router fetch failed with status {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
            return $"9Router fetch lỗi {(int)response.StatusCode}: {errorBody}";
        }

        var result = await response.Content.ReadFromJsonAsync<FetchResponse>(cancellationToken: ct);
        if (result?.Content?.Text == null)
            return "Không fetch được nội dung từ URL này.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Tiêu đề: {result.Title ?? "Không có tiêu đề"}");
        sb.AppendLine($"URL: {targetUrl}");
        sb.AppendLine($"Provider: {result.Provider ?? options.Provider}");

        if (!string.IsNullOrWhiteSpace(result.Metadata?.Author))
            sb.AppendLine($"Tác giả: {result.Metadata.Author}");

        sb.AppendLine($"Nội dung ({result.Content.Format}, {result.Content.Length} chars):");
        sb.AppendLine(NineRouterToolSupport.TrimForPrompt(result.Content.Text, maxChars > 0 ? maxChars : options.MaxMarkdownChars));
        return sb.ToString();
    }

    private class FetchRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = default!;
        [JsonPropertyName("url")]
        public string Url { get; set; } = default!;
        [JsonPropertyName("format")]
        public string? Format { get; set; }
        [JsonPropertyName("max_characters")]
        public int? MaxCharacters { get; set; }
    }

    private class FetchResponse
    {
        [JsonPropertyName("provider")]
        public string? Provider { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("content")]
        public FetchContent? Content { get; set; }
        [JsonPropertyName("metadata")]
        public FetchMetadata? Metadata { get; set; }
    }

    private class FetchContent
    {
        [JsonPropertyName("format")]
        public string? Format { get; set; }
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        [JsonPropertyName("length")]
        public int Length { get; set; }
    }

    private class FetchMetadata
    {
        [JsonPropertyName("author")]
        public string? Author { get; set; }
        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; }
        [JsonPropertyName("language")]
        public string? Language { get; set; }
    }
}
