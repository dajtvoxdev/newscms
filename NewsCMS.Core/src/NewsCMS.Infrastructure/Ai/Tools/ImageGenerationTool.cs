using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Ai;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Infrastructure.Ai.Tools;

public sealed class ImageGenerationTool : IAiTool
{
    private const string DefaultBaseUrl = "http://localhost:20128";
    private const string SubFolder = "ai-images";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileStorage _storage;
    private readonly ILogger<ImageGenerationTool> _logger;

    public ImageGenerationTool(IHttpClientFactory httpClientFactory, IFileStorage storage, ILogger<ImageGenerationTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _logger = logger;
    }

    public string Key => "image_generate";
    public string DisplayName => "Image Generation";
    public string Description => "Tạo ảnh từ prompt và trả về IMAGE_URL để Telegram gửi ảnh trực tiếp. Dùng khi người dùng yêu cầu tạo/vẽ/sinh ảnh.";
    public string ParametersJsonSchema => @"{
        ""type"": ""object"",
        ""properties"": {
            ""prompt"": { ""type"": ""string"", ""description"": ""Mô tả ảnh cần tạo, càng cụ thể càng tốt"" },
            ""size"": { ""type"": ""string"", ""description"": ""Kích thước, mặc định auto"" },
            ""quality"": { ""type"": ""string"", ""description"": ""Chất lượng, mặc định auto"" },
            ""background"": { ""type"": ""string"", ""description"": ""Nền, mặc định auto"" },
            ""output_format"": { ""type"": ""string"", ""enum"": [""png"", ""jpeg"", ""webp""], ""description"": ""Định dạng ảnh, mặc định png"" }
        },
        ""required"": [""prompt""]
    }";

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct = default)
    {
        var prompt = ReadString(args, "prompt");
        if (string.IsNullOrWhiteSpace(prompt))
            return "Lỗi: thiếu prompt tạo ảnh.";
        if (string.IsNullOrWhiteSpace(ctx.ApiKey))
            return "Lỗi cấu hình: chưa có API key tạo ảnh.";

        var baseUrl = string.IsNullOrWhiteSpace(ctx.BaseUrl) ? DefaultBaseUrl : ctx.BaseUrl.Trim().TrimEnd('/');
        if (!IsAllowedBaseUrl(baseUrl, out var urlError))
            return $"Lỗi cấu hình: {urlError}";

        var config = ParseConfig(ctx.ConfigJson);
        var format = ReadString(args, "output_format") ?? config.OutputFormat ?? "png";
        var client = _httpClientFactory.CreateClient("ai-tools");
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds ?? 240, 30, 600));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/images/generations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.ApiKey);
        request.Content = JsonContent.Create(new ImageRequest
        {
            Model = config.Model ?? "cx/gpt-5.5-image",
            Prompt = prompt.Trim(),
            N = 1,
            Size = ReadString(args, "size") ?? config.Size ?? "auto",
            Quality = ReadString(args, "quality") ?? config.Quality ?? "auto",
            Background = ReadString(args, "background") ?? config.Background ?? "auto",
            ImageDetail = config.ImageDetail ?? "high",
            OutputFormat = format
        }, options: JsonOptions);

        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Image generation failed with status {StatusCode}: {Body}", (int)response.StatusCode, Trim(raw, 500));
            return $"Tạo ảnh lỗi {(int)response.StatusCode}: {Trim(raw, 500)}";
        }

        using var doc = JsonDocument.Parse(raw);
        var item = doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0
            ? data[0]
            : default;
        if (item.ValueKind != JsonValueKind.Object)
            return "Tạo ảnh lỗi: response không có data[0].";

        var b64 = item.TryGetProperty("b64_json", out var b64Prop) ? b64Prop.GetString() : null;
        if (!string.IsNullOrWhiteSpace(b64))
        {
            var bytes = Convert.FromBase64String(b64);
            await using var stream = new MemoryStream(bytes);
            var key = await _storage.SaveAsync(stream, $"ai-image.{format}", SubFolder, ct);
            var publicUrl = _storage.GetPublicUrl(key);
            return $"IMAGE_URL: {publicUrl}\nCAPTION: {Trim(prompt, 900)}";
        }

        var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
        return string.IsNullOrWhiteSpace(url)
            ? "Tạo ảnh lỗi: response không có url hoặc b64_json."
            : $"IMAGE_URL: {url}\nCAPTION: {Trim(prompt, 900)}";
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static ImageConfig ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ImageConfig();
        try { return JsonSerializer.Deserialize<ImageConfig>(json, JsonOptions) ?? new ImageConfig(); }
        catch { return new ImageConfig(); }
    }

    private static bool IsAllowedBaseUrl(string url, out string? error)
    {
        error = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "URL tạo ảnh không hợp lệ.";
            return false;
        }

        if (uri.Scheme == "https") return true;
        if (uri.Scheme == "http" && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1" || uri.Host == "::1"))
            return true;

        error = "HTTP chỉ được dùng cho localhost; host công cộng phải dùng HTTPS.";
        return false;
    }

    private static string Trim(string? value, int maxChars)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maxChars ? text : text[..maxChars].TrimEnd() + " ...";
    }

    private sealed class ImageRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = default!;
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = default!;
        [JsonPropertyName("n")]
        public int N { get; set; }
        [JsonPropertyName("size")]
        public string? Size { get; set; }
        [JsonPropertyName("quality")]
        public string? Quality { get; set; }
        [JsonPropertyName("background")]
        public string? Background { get; set; }
        [JsonPropertyName("image_detail")]
        public string? ImageDetail { get; set; }
        [JsonPropertyName("output_format")]
        public string? OutputFormat { get; set; }
    }

    private sealed class ImageConfig
    {
        public string? Model { get; set; }
        public string? Size { get; set; }
        public string? Quality { get; set; }
        public string? Background { get; set; }
        public string? ImageDetail { get; set; }
        public string? OutputFormat { get; set; }
        public int? TimeoutSeconds { get; set; }
    }
}
