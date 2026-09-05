using System.Text.Json;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Builder;

namespace NewsCMS.Infrastructure.Ai.Tools;

/// <summary>
/// Tuỳ chọn tuần tự hoá dùng chung cho các tool builder — camelCase để khớp JSON mà mô hình quen,
/// không escape ký tự Unicode để tiếng Việt đọc được trong kết quả trả về.
/// </summary>
internal static class SiteBuilderToolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

/// <summary>
/// Trợ lý AI trong admin đọc cấu trúc site hiện tại. Song song với MCP tool site_get_schema /
/// site_export nhưng đi qua phiên đăng nhập của admin thay vì API key.
/// </summary>
public sealed class SiteBuilderSchemaTool : IAiTool
{
    private readonly ISiteBuilderApi _api;

    public SiteBuilderSchemaTool(ISiteBuilderApi api) => _api = api;

    public string Key => "site_builder_schema";
    public string DisplayName => "Site Builder — schema và hiện trạng";
    public string Description =>
        "Lấy JSON Schema của SiteSpec, danh sách khối dựng sẵn và tóm tắt site hiện tại. " +
        "Gọi tool này TRƯỚC khi dựng hoặc sửa trang để biết đúng định dạng và những gì đã có.";

    public string ParametersJsonSchema => """
    {
      "type": "object",
      "properties": {
        "includeExport": {
          "type": "boolean",
          "description": "true để kèm luôn toàn bộ SiteSpec hiện tại (dài). Mặc định false, chỉ trả schema và tóm tắt."
        }
      }
    }
    """;

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct = default)
    {
        var includeExport = args.ValueKind == JsonValueKind.Object
                            && args.TryGetProperty("includeExport", out var flag)
                            && flag.ValueKind == JsonValueKind.True;

        var schema = await _api.GetSchemaAsync(ct);
        var summary = await _api.GetSummaryAsync(ct);

        if (!includeExport)
            return SiteBuilderToolJson.Serialize(new { schema, summary = summary.Value, error = summary.Error });

        var export = await _api.ExportAsync(ct);
        return SiteBuilderToolJson.Serialize(new
        {
            schema,
            summary = summary.Value,
            spec = export.Value,
            error = summary.Error ?? export.Error
        });
    }
}

/// <summary>
/// Trợ lý AI trong admin dựng site bằng một SiteSpec khai báo. Uỷ quyền hoàn toàn cho
/// ISiteBuilderApi.ApplyAsync nên hành vi giống hệt MCP tool site_apply, kể cả tính idempotent.
/// </summary>
public sealed class SiteBuilderApplyTool : IAiTool
{
    private readonly ISiteBuilderApi _api;

    public SiteBuilderApplyTool(ISiteBuilderApi api) => _api = api;

    public string Key => "site_builder_apply";
    public string DisplayName => "Site Builder — áp dụng spec";
    public string Description =>
        "Tạo hoặc cập nhật layout, trang, chuyên mục, menu và design token của site hiện tại từ một " +
        "SiteSpec khai báo, trong một lần gọi. Idempotent: gọi lại cùng spec không tạo bản ghi trùng, " +
        "và không xoá thứ nằm ngoài spec. Gọi site_builder_schema trước để biết định dạng.";

    public string ParametersJsonSchema => """
    {
      "type": "object",
      "properties": {
        "spec": {
          "type": "object",
          "description": "SiteSpec đầy đủ. Xem site_builder_schema để biết cấu trúc từng nhóm (layouts, pages, categories, menus, designTokens, settings)."
        }
      },
      "required": ["spec"]
    }
    """;

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct = default)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("spec", out var specElement))
            return "Lỗi: thiếu tham số 'spec'.";

        SiteSpec? spec;
        try
        {
            spec = specElement.Deserialize<SiteSpec>(SiteBuilderToolJson.Options);
        }
        catch (JsonException ex)
        {
            return $"Lỗi: spec không parse được — {ex.Message}";
        }

        if (spec is null) return "Lỗi: spec rỗng.";

        var result = await _api.ApplyAsync(spec, ct);
        return result.Succeeded
            ? SiteBuilderToolJson.Serialize(result.Value)
            : $"Lỗi: {result.Error}";
    }
}

/// <summary>
/// Trợ lý AI trong admin xem trước HTML một trang đã xuất bản để tự kiểm chứng kết quả vừa dựng.
/// </summary>
public sealed class SiteBuilderPreviewTool : IAiTool
{
    /// <summary>Cắt bớt HTML trả về để không nuốt trọn cửa sổ ngữ cảnh của mô hình.</summary>
    private const int MaxHtmlLength = 20_000;

    private readonly ISiteBuilderApi _api;

    public SiteBuilderPreviewTool(ISiteBuilderApi api) => _api = api;

    public string Key => "site_builder_preview";
    public string DisplayName => "Site Builder — xem trước trang";
    public string Description =>
        "Render một trang đã xuất bản thành HTML hoàn chỉnh để kiểm tra kết quả sau khi dựng.";

    public string ParametersJsonSchema => """
    {
      "type": "object",
      "properties": {
        "pageId": { "type": "string", "description": "Id của trang (GUID)." },
        "culture": { "type": "string", "description": "Mã ngôn ngữ, ví dụ \"vi\". Bỏ trống để dùng ngôn ngữ mặc định của site." }
      },
      "required": ["pageId"]
    }
    """;

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct = default)
    {
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("pageId", out var pageIdElement)
            || !Guid.TryParse(pageIdElement.GetString(), out var pageId))
            return "Lỗi: thiếu hoặc sai 'pageId' (phải là GUID).";

        string? culture = null;
        if (args.TryGetProperty("culture", out var cultureElement))
            culture = cultureElement.GetString();

        string? sampleSlug = null;
        if (args.TryGetProperty("sampleSlug", out var sampleSlugElement))
            sampleSlug = sampleSlugElement.GetString();

        var result = await _api.PreviewAsync(pageId, culture, sampleSlug, ct);
        if (!result.Succeeded) return $"Lỗi: {result.Error}";

        var html = result.Value ?? string.Empty;
        return html.Length <= MaxHtmlLength
            ? html
            : html[..MaxHtmlLength] + $"\n<!-- … đã cắt bớt, tổng {html.Length} ký tự -->";
    }
}
