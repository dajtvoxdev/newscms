using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;

namespace NewsCMS.Infrastructure.Ai.Tools;

public sealed class NineRouterSearchTool : IAiTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NineRouterSearchTool> _logger;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public NineRouterSearchTool(IHttpClientFactory httpClientFactory, ILogger<NineRouterSearchTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Key => "ninerouter_search";
    public string DisplayName => "Web Search (9Router)";
    public string Description => "Tìm nguồn live mới nhất trên web qua 9Router (hỗ trợ Tavily, Exa, Brave, Serper, Perplexity, SearXNG,...). Dùng để xác minh đội hình, chấn thương, phong độ, thời tiết, odds hoặc bối cảnh trận.";
    public string ParametersJsonSchema => @"{
        ""type"": ""object"",
        ""properties"": {
            ""query"": { ""type"": ""string"", ""description"": ""Từ khóa tìm kiếm"" },
            ""max_results"": { ""type"": ""integer"", ""description"": ""Số kết quả (mặc định theo config, tối đa 10)"" },
            ""search_type"": { ""type"": ""string"", ""enum"": [""web"", ""news""], ""description"": ""Loại tìm kiếm: web hoặc news"" },
            ""domain_filter"": {
                ""type"": ""array"",
                ""items"": { ""type"": ""string"" },
                ""description"": ""Giới hạn các domain ưu tiên""
            }
        },
        ""required"": [""query""]
    }";

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct = default)
    {
        if (!args.TryGetProperty("query", out var q) || string.IsNullOrWhiteSpace(q.GetString()))
            return "Lỗi: thiếu từ khóa tìm kiếm (query).";
        var query = q.GetString()!;

        if (string.IsNullOrWhiteSpace(ctx.BaseUrl))
            return "Lỗi cấu hình: chưa có 9Router URL.";

        var baseUrl = NineRouterToolSupport.NormalizeBaseUrl(ctx.BaseUrl);
        if (!NineRouterToolSupport.IsAllowedApiBaseUrl(baseUrl, out var urlError))
        {
            _logger.LogWarning("Tool '{ToolKey}' blocked request to disallowed URL: {Url} — {Reason}", Key, baseUrl, urlError);
            return $"Lỗi cấu hình: {urlError}";
        }

        var options = NineRouterToolSupport.ParseOptions(ctx.ConfigJson);
        var maxResults = Math.Clamp(NineRouterToolSupport.ReadInt(args, "max_results") ?? options.MaxResults, 1, 10);
        var searchType = NineRouterToolSupport.ReadString(args, "search_type") ?? "web";
        var domainFilter = NineRouterToolSupport.ReadStringArray(args, "domain_filter");

        var client = _httpClientFactory.CreateClient("ai-tools");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/search");

        if (!string.IsNullOrWhiteSpace(ctx.ApiKey))
            request.Headers.Add("Authorization", $"Bearer {ctx.ApiKey}");

        var body = new SearchRequest
        {
            Model = options.Provider,
            Query = query,
            MaxResults = maxResults,
            SearchType = searchType,
            DomainFilter = domainFilter
        };
        request.Content = JsonContent.Create(body, options: SerializerOptions);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = NineRouterToolSupport.TrimForPrompt(await response.Content.ReadAsStringAsync(ct), 500);
            _logger.LogWarning("9Router search failed with status {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
            return $"9Router search lỗi {(int)response.StatusCode}: {errorBody}";
        }

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken: ct);
        if (result?.Results == null || result.Results.Count == 0)
            return "Không tìm thấy kết quả.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Truy vấn: {query}");
        sb.AppendLine($"Provider: {result.Provider ?? options.Provider}");
        sb.AppendLine($"Số nguồn: {result.Results.Count}");
        sb.AppendLine();

        var index = 1;
        foreach (var item in result.Results)
        {
            sb.AppendLine($"Nguồn {index}: {item.Title ?? "Không có tiêu đề"}");
            sb.AppendLine($"URL: {item.Url ?? "N/A"}");
            if (!string.IsNullOrEmpty(item.Snippet))
                sb.AppendLine($"Mô tả: {NineRouterToolSupport.TrimForPrompt(item.Snippet, 320)}");
            if (!string.IsNullOrWhiteSpace(item.Content))
            {
                sb.AppendLine("Nội dung:");
                sb.AppendLine(NineRouterToolSupport.TrimForPrompt(item.Content, options.MaxMarkdownCharsPerResult));
            }
            sb.AppendLine();
            index++;
        }

        if (!string.IsNullOrWhiteSpace(result.Answer))
            sb.AppendLine($"Tóm tắt: {NineRouterToolSupport.TrimForPrompt(result.Answer, 500)}");

        return sb.ToString();
    }

    private class SearchRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = default!;
        [JsonPropertyName("query")]
        public string Query { get; set; } = default!;
        [JsonPropertyName("max_results")]
        public int MaxResults { get; set; }
        [JsonPropertyName("search_type")]
        public string? SearchType { get; set; }
        [JsonPropertyName("domain_filter")]
        public List<string>? DomainFilter { get; set; }
    }

    private class SearchResponse
    {
        [JsonPropertyName("provider")]
        public string? Provider { get; set; }
        [JsonPropertyName("query")]
        public string? Query { get; set; }
        [JsonPropertyName("results")]
        public List<SearchResult>? Results { get; set; }
        [JsonPropertyName("answer")]
        public string? Answer { get; set; }
    }

    private class SearchResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }
        [JsonPropertyName("snippet")]
        public string? Snippet { get; set; }
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
