using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;

namespace NewsCMS.Infrastructure.Ai.Tools;

public sealed class FirecrawlScrapeTool : IAiTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FirecrawlScrapeTool> _logger;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FirecrawlScrapeTool(IHttpClientFactory httpClientFactory, ILogger<FirecrawlScrapeTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Key => "firecrawl_scrape";
    public string DisplayName => "Web Scrape (Firecrawl)";
    public string Description => "Đọc chi tiết một URL cụ thể bằng Firecrawl và trả về markdown sạch. Dùng sau bước search để xác minh nguồn quan trọng như bài báo, trang FIFA, dự báo thời tiết hoặc trang thống kê.";
    public string ParametersJsonSchema => @"{
        ""type"": ""object"",
        ""properties"": {
            ""url"": { ""type"": ""string"", ""description"": ""URL HTTPS cần scrape chi tiết"" },
            ""onlyMainContent"": { ""type"": ""boolean"", ""description"": ""true để chỉ lấy nội dung chính"" },
            ""maxChars"": { ""type"": ""integer"", ""description"": ""Giới hạn ký tự markdown trả về"" }
        },
        ""required"": [""url""]
    }";

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct = default)
    {
        var targetUrl = FirecrawlToolSupport.ReadString(args, "url");
        if (string.IsNullOrWhiteSpace(targetUrl))
            return "Lỗi: thiếu URL cần scrape.";

        if (string.IsNullOrWhiteSpace(ctx.ApiKey))
            return "Lỗi cấu hình: chưa có Firecrawl API key.";

        if (!FirecrawlToolSupport.IsAllowedTargetUrl(targetUrl, out var targetUrlError))
            return $"Lỗi URL mục tiêu: {targetUrlError}";

        var baseUrl = ctx.BaseUrl ?? "https://api.firecrawl.dev";
        if (!FirecrawlToolSupport.IsAllowedApiBaseUrl(baseUrl, out var baseUrlError))
        {
            _logger.LogWarning("Tool '{ToolKey}' blocked request to disallowed URL: {Url} — {Reason}", Key, baseUrl, baseUrlError);
            return $"Lỗi cấu hình: {baseUrlError}";
        }

        var options = FirecrawlToolSupport.ParseOptions(ctx.ConfigJson);
        var maxChars = Math.Clamp(FirecrawlToolSupport.ReadInt(args, "maxChars") ?? options.MaxMarkdownChars, 1000, 12000);
        var onlyMainContent = FirecrawlToolSupport.ReadBoolean(args, "onlyMainContent") ?? true;

        var client = _httpClientFactory.CreateClient("ai-tools");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/scrape");
        request.Headers.Add("Authorization", $"Bearer {ctx.ApiKey}");
        request.Content = JsonContent.Create(new ScrapeRequest
        {
            Url = targetUrl,
            Formats = ["markdown"],
            OnlyMainContent = onlyMainContent,
            MaxAge = 0,
            BlockAds = true,
            RemoveBase64Images = true
        }, options: SerializerOptions);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = FirecrawlToolSupport.TrimForPrompt(await response.Content.ReadAsStringAsync(ct), 500);
            _logger.LogWarning("Firecrawl scrape failed with status {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
            return $"Firecrawl scrape lỗi {(int)response.StatusCode}: {errorBody}";
        }

        var result = await response.Content.ReadFromJsonAsync<ScrapeResponse>(cancellationToken: ct);
        if (result?.Success != true || result.Data is null)
            return "Không scrape được nội dung từ URL này.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Tiêu đề: {result.Data.Metadata?.Title ?? "Không có tiêu đề"}");
        sb.AppendLine($"URL: {result.Data.Metadata?.SourceUrl ?? result.Data.Metadata?.Url ?? targetUrl}");

        if (!string.IsNullOrWhiteSpace(result.Data.Metadata?.Description))
            sb.AppendLine($"Mô tả: {FirecrawlToolSupport.TrimForPrompt(result.Data.Metadata.Description, 320)}");

        if (!string.IsNullOrWhiteSpace(result.Warning))
            sb.AppendLine($"Cảnh báo: {FirecrawlToolSupport.TrimForPrompt(result.Warning, 220)}");

        if (string.IsNullOrWhiteSpace(result.Data.Markdown))
            return sb.AppendLine("Markdown: Không có nội dung văn bản phù hợp.").ToString();

        sb.AppendLine("Markdown:");
        sb.AppendLine(FirecrawlToolSupport.TrimForPrompt(result.Data.Markdown, maxChars));
        return sb.ToString();
    }

    private class ScrapeRequest
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = default!;
        [JsonPropertyName("formats")]
        public string[]? Formats { get; set; }
        [JsonPropertyName("onlyMainContent")]
        public bool OnlyMainContent { get; set; }
        [JsonPropertyName("maxAge")]
        public int MaxAge { get; set; }
        [JsonPropertyName("blockAds")]
        public bool BlockAds { get; set; }
        [JsonPropertyName("removeBase64Images")]
        public bool RemoveBase64Images { get; set; }
    }

    private class ScrapeResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("data")]
        public ScrapeData? Data { get; set; }
        [JsonPropertyName("warning")]
        public string? Warning { get; set; }
    }

    private class ScrapeData
    {
        [JsonPropertyName("markdown")]
        public string? Markdown { get; set; }
        [JsonPropertyName("metadata")]
        public ScrapeMetadata? Metadata { get; set; }
    }

    private class ScrapeMetadata
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("sourceURL")]
        public string? SourceUrl { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
