using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;

namespace NewsCMS.Infrastructure.Ai.Tools;

public sealed class FirecrawlSearchTool : IAiTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FirecrawlSearchTool> _logger;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FirecrawlSearchTool(IHttpClientFactory httpClientFactory, ILogger<FirecrawlSearchTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Key => "firecrawl_search";
    public string DisplayName => "Web Search (Firecrawl)";
    public string Description => "Tìm nguồn live mới nhất trên web bằng Firecrawl. Có thể trả về title, URL, mô tả và markdown sạch của các kết quả để xác minh đội hình, chấn thương, phong độ, thời tiết, odds hoặc bối cảnh trận.";
    public string ParametersJsonSchema => @"{
        ""type"": ""object"",
        ""properties"": {
            ""query"": { ""type"": ""string"", ""description"": ""Từ khóa tìm kiếm"" },
            ""limit"": { ""type"": ""integer"", ""description"": ""Số kết quả (mặc định theo config, tối đa 10)"" },
            ""includeContent"": { ""type"": ""boolean"", ""description"": ""true để scrape luôn markdown của từng kết quả"" },
            ""includeDomains"": {
                ""type"": ""array"",
                ""items"": { ""type"": ""string"" },
                ""description"": ""Giới hạn các domain ưu tiên như fifa.com, theanalyst.com, reuters.com""
            },
            ""excludeDomains"": {
                ""type"": ""array"",
                ""items"": { ""type"": ""string"" },
                ""description"": ""Loại trừ các domain không mong muốn""
            }
        },
        ""required"": [""query""]
    }";

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct = default)
    {
        string query;
        if (!args.TryGetProperty("query", out var q) || string.IsNullOrWhiteSpace(q.GetString()))
            return "Lỗi: thiếu từ khóa tìm kiếm (query).";
        query = q.GetString()!;

        if (string.IsNullOrWhiteSpace(ctx.ApiKey))
            return "Lỗi cấu hình: chưa có Firecrawl API key.";

        var options = FirecrawlToolSupport.ParseOptions(ctx.ConfigJson);
        var limit = Math.Clamp(FirecrawlToolSupport.ReadInt(args, "limit") ?? options.MaxResults, 1, 10);
        var includeContent = FirecrawlToolSupport.ReadBoolean(args, "includeContent") ?? options.DefaultIncludeContent;
        var includeDomains = FirecrawlToolSupport.ReadStringArray(args, "includeDomains");
        var excludeDomains = FirecrawlToolSupport.ReadStringArray(args, "excludeDomains");

        var baseUrl = ctx.BaseUrl ?? "https://api.firecrawl.dev";
        if (!FirecrawlToolSupport.IsAllowedApiBaseUrl(baseUrl, out var error))
        {
            _logger.LogWarning("Tool '{ToolKey}' blocked request to disallowed URL: {Url} — {Reason}", Key, baseUrl, error);
            return $"Lỗi cấu hình: {error}";
        }

        var client = _httpClientFactory.CreateClient("ai-tools");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/search");
        request.Headers.Add("Authorization", $"Bearer {ctx.ApiKey}");
        request.Content = JsonContent.Create(new SearchRequest
        {
            Query = query,
            Limit = limit,
            Sources = ["web"],
            IgnoreInvalidUrls = true,
            IncludeDomains = includeDomains,
            ExcludeDomains = excludeDomains,
            ScrapeOptions = includeContent
                ? new SearchScrapeOptions
                {
                    Formats = ["markdown"],
                    OnlyMainContent = true,
                    MaxAge = 0,
                    BlockAds = true,
                    RemoveBase64Images = true
                }
                : null
        }, options: SerializerOptions);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = FirecrawlToolSupport.TrimForPrompt(await response.Content.ReadAsStringAsync(ct), 500);
            _logger.LogWarning("Firecrawl search failed with status {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
            return $"Firecrawl search lỗi {(int)response.StatusCode}: {errorBody}";
        }

        var result = await response.Content.ReadFromJsonAsync<FirecrawlResponse>(cancellationToken: ct);
        if (result?.Success != true || result.Data?.Web == null || result.Data.Web.Count == 0)
            return "Không tìm thấy kết quả.";

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.Warning))
            sb.AppendLine($"Cảnh báo: {FirecrawlToolSupport.TrimForPrompt(result.Warning, 220)}");

        sb.AppendLine($"Truy vấn: {query}");
        sb.AppendLine($"Số nguồn: {result.Data.Web.Count}");
        sb.AppendLine();

        var index = 1;
        foreach (var item in result.Data.Web)
        {
            sb.AppendLine($"Nguồn {index}: {item.Title ?? item.Metadata?.Title ?? "Không có tiêu đề"}");
            sb.AppendLine($"URL: {item.Url ?? item.Metadata?.Url ?? item.Metadata?.SourceUrl ?? "N/A"}");
            if (!string.IsNullOrEmpty(item.Description))
                sb.AppendLine($"Mô tả: {FirecrawlToolSupport.TrimForPrompt(item.Description, 320)}");

            if (!string.IsNullOrWhiteSpace(item.Markdown))
            {
                sb.AppendLine("Markdown:");
                sb.AppendLine(FirecrawlToolSupport.TrimForPrompt(item.Markdown, options.MaxMarkdownCharsPerResult));
            }

            sb.AppendLine();
            index++;
        }

        return sb.ToString();
    }
    private class FirecrawlResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("data")]
        public FirecrawlData? Data { get; set; }
        [JsonPropertyName("warning")]
        public string? Warning { get; set; }
    }

    private class FirecrawlData
    {
        [JsonPropertyName("web")]
        public List<FirecrawlWebResult>? Web { get; set; }
    }

    private class FirecrawlWebResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        [JsonPropertyName("markdown")]
        public string? Markdown { get; set; }
        [JsonPropertyName("metadata")]
        public FirecrawlMetadata? Metadata { get; set; }
    }

    private class FirecrawlMetadata
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("sourceURL")]
        public string? SourceUrl { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private class SearchRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = default!;
        [JsonPropertyName("limit")]
        public int Limit { get; set; }
        [JsonPropertyName("sources")]
        public string[]? Sources { get; set; }
        [JsonPropertyName("includeDomains")]
        public List<string>? IncludeDomains { get; set; }
        [JsonPropertyName("excludeDomains")]
        public List<string>? ExcludeDomains { get; set; }
        [JsonPropertyName("ignoreInvalidURLs")]
        public bool IgnoreInvalidUrls { get; set; }
        [JsonPropertyName("scrapeOptions")]
        public SearchScrapeOptions? ScrapeOptions { get; set; }
    }

    private class SearchScrapeOptions
    {
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
}
