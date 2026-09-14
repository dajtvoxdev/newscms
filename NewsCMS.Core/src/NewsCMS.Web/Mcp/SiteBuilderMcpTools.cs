using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Builder;
using NewsCMS.Web.Middleware;

namespace NewsCMS.Web.Mcp;

/// <summary>
/// MCP tool cho agent dựng site. Đây là VỎ MỎNG: mọi nghiệp vụ nằm ở ISiteBuilderApi, tool chỉ
/// kiểm scope của API key, gọi xuống, rồi tuần tự hoá kết quả. Thêm bề mặt REST sau (nếu cần)
/// chỉ là viết controller gọi cùng interface đó.
///
/// Ranh giới tenant: McpApiKeyMiddleware đã set ICurrentSite theo key trước khi tool chạy, và
/// không tool nào nhận tham số siteId — nên không có đường nào trỏ sang site khác.
/// </summary>
[McpServerToolType]
public sealed class SiteBuilderMcpTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Nhận cả PascalCase lẫn camelCase khi đọc: SpecJson của SiteTemplate dùng PascalCase,
        // còn agent thường gửi camelCase. Thiếu cờ này, spec sai kiểu bị bỏ qua IM LẶNG —
        // ApplyAsync báo thành công với toàn số 0 thay vì báo lỗi.
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ISiteBuilderApi _api;
    private readonly IHttpContextAccessor _http;
    private readonly ICurrentSite _currentSite;

    public SiteBuilderMcpTools(ISiteBuilderApi api, IHttpContextAccessor http, ICurrentSite currentSite)
    {
        _api = api;
        _http = http;
        _currentSite = currentSite;
    }

    // ── Read ────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "site_get_schema", ReadOnly = true, Title = "Lấy schema dựng site")]
    [Description("Trả về JSON Schema của SiteSpec, danh sách block khả dụng và các giá trị enum hợp lệ. Gọi tool này TRƯỚC khi dựng site để biết đúng định dạng payload.")]
    public async Task<string> GetSchemaAsync(CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderRead) is { } denied) return denied;
        return Serialize(await _api.GetSchemaAsync(ct));
    }

    [McpServerTool(Name = "site_get_summary", ReadOnly = true, Title = "Tóm tắt site hiện tại")]
    [Description("Thông tin site đang thao tác: tên, slug, domain, ngôn ngữ và số lượng page/layout/chuyên mục/menu/token.")]
    public async Task<string> GetSummaryAsync(CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderRead) is { } denied) return denied;
        return Serialize(await _api.GetSummaryAsync(ct));
    }

    [McpServerTool(Name = "site_export", ReadOnly = true, Title = "Xuất site thành spec")]
    [Description("Xuất toàn bộ site hiện tại thành SiteSpec — cùng định dạng site_apply nhận vào, dùng để đọc hiện trạng trước khi sửa hoặc để sao chép sang site khác.")]
    public async Task<string> ExportAsync(CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderRead) is { } denied) return denied;
        return Serialize(await _api.ExportAsync(ct));
    }

    [McpServerTool(Name = "site_list_pages", ReadOnly = true, Title = "Liệt kê trang")]
    [Description("Danh sách trang của site, có phân trang và tìm kiếm theo tiêu đề/slug.")]
    public async Task<string> ListPagesAsync(
        [Description("Trang thứ mấy, bắt đầu từ 1.")] int page = 1,
        [Description("Số bản ghi mỗi trang, tối đa 100.")] int pageSize = 20,
        [Description("Từ khoá tìm trong tiêu đề hoặc slug. Bỏ trống để lấy tất cả.")] string? search = null,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderRead) is { } denied) return denied;
        return Serialize(await _api.ListPagesAsync(page, pageSize, search, ct));
    }

    [McpServerTool(Name = "site_list_blocks", ReadOnly = true, Title = "Liệt kê khối dựng sẵn")]
    [Description("Danh sách khối kéo-thả khả dụng (tĩnh và động). Khối động được render server-side qua thuộc tính data-nc-block trong HTML.")]
    public async Task<string> ListBlocksAsync(CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderRead) is { } denied) return denied;
        return Serialize(await _api.ListBlocksAsync(ct));
    }

    [McpServerTool(Name = "site_list_media", ReadOnly = true, Title = "Liệt kê media")]
    [Description("Ảnh/tệp đã có trong thư viện media của site. Dùng URL trả về thay vì bịa đường dẫn ảnh.")]
    public async Task<string> ListMediaAsync(
        [Description("Số bản ghi tối đa, tối đa 200.")] int limit = 50,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderRead) is { } denied) return denied;
        return Serialize(await _api.ListMediaAsync(limit, ct));
    }

    [McpServerTool(Name = "site_list_design_tokens", ReadOnly = true, Title = "Liệt kê design token")]
    [Description("Design token của site (màu, font, khoảng cách, bo góc, đổ bóng). Mỗi token vừa là CSS variable vừa là utility Tailwind.")]
    public async Task<string> ListDesignTokensAsync(CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderRead) is { } denied) return denied;
        return Serialize(await _api.ListDesignTokensAsync(ct));
    }

    [McpServerTool(Name = "site_preview_page", ReadOnly = true, Title = "Xem trước HTML trang")]
    [Description("Render một trang đã xuất bản thành HTML hoàn chỉnh để tự kiểm chứng kết quả sau khi dựng. Với trang template chi tiết, truyền sampleSlug để xem trước cùng dữ liệu thực tế.")]
    public async Task<string> PreviewPageAsync(
        [Description("Id của trang (GUID).")] string pageId,
        [Description("Mã ngôn ngữ, ví dụ \"vi\". Bỏ trống để dùng ngôn ngữ mặc định của site.")] string? culture = null,
        [Description("Slug của bài viết/sản phẩm mẫu để xem trước trang template. Bỏ trống để lấy bản ghi mới nhất.")] string? sampleSlug = null,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderRead) is { } denied) return denied;
        if (!Guid.TryParse(pageId, out var id)) return Error($"pageId '{pageId}' không phải GUID hợp lệ.");

        var result = await _api.PreviewAsync(id, culture, sampleSlug, ct);
        return result.Succeeded ? result.Value ?? string.Empty : Error(result.Error);
    }

    // ── Write ───────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "site_apply", Idempotent = true, Title = "Áp dụng spec lên site")]
    [Description("Áp dụng một SiteSpec khai báo lên site: tạo/cập nhật layout, trang, chuyên mục, menu, design token và cấu hình trong MỘT lần gọi. Idempotent — gọi lại cùng spec không tạo bản ghi trùng. Không xoá thứ nằm ngoài spec. Gọi site_get_schema trước để biết định dạng.")]
    public async Task<string> ApplyAsync(
        [Description("SiteSpec dạng JSON. Xem site_get_schema để biết cấu trúc đầy đủ.")] string specJson,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderWrite) is { } denied) return denied;

        SiteSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<SiteSpec>(specJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            return Error($"specJson không parse được: {ex.Message}");
        }

        if (spec is null) return Error("specJson rỗng hoặc không hợp lệ.");

        // Custom JS là thực thi mã trên trình duyệt khách → đòi scope riêng, không đi kèm builder.write.
        if (SpecCarriesCustomJs(spec) && !HasScope(ApiKeyScopes.BuilderCode))
            return Error($"Spec có customJs nhưng key thiếu scope '{ApiKeyScopes.BuilderCode}'.");

        // data-nc-html = khối "Nhúng HTML": render nguyên văn, KHÔNG qua sanitizer → cùng mức
        // rủi ro với customJs, nên cùng một cổng scope.
        if (SpecCarriesRawHtml(spec) && !HasScope(ApiKeyScopes.BuilderCode))
            return Error($"Spec có khối nhúng HTML (data-nc-html) nhưng key thiếu scope '{ApiKeyScopes.BuilderCode}'.");

        return Serialize(await _api.ApplyAsync(spec, ct));
    }

    [McpServerTool(Name = "site_save_page", Title = "Tạo hoặc cập nhật một trang")]
    [Description("Tạo trang mới (bỏ trống pageId) hoặc cập nhật trang có sẵn. Trang tạo qua đây ở trạng thái nháp — gọi site_publish_page để xuất bản.")]
    public async Task<string> SavePageAsync(
        [Description("Tiêu đề trang.")] string title,
        [Description("Slug (đường dẫn). Slug \"home\" ánh xạ tới trang chủ \"/\".")] string slug,
        [Description("HTML nội dung trang. Thẻ script và thuộc tính on* sẽ bị loại bỏ khi lưu.")] string? compiledHtml = null,
        [Description("CSS riêng của trang.")] string? compiledCss = null,
        [Description("JavaScript riêng của trang. Cần scope builder.code.")] string? customJs = null,
        [Description("Loại trang: Landing, Static, CategoryTemplate, PostTemplate, ArchiveTemplate, SystemError.")] string kind = "Landing",
        [Description("Id trang cần cập nhật (GUID). Bỏ trống để tạo mới.")] string? pageId = null,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderWrite) is { } denied) return denied;
        if (!string.IsNullOrWhiteSpace(customJs) && !HasScope(ApiKeyScopes.BuilderCode))
            return Error($"customJs cần scope '{ApiKeyScopes.BuilderCode}'.");
        if (BlockCodeExtractor.CarriesRawHtml(compiledHtml) && !HasScope(ApiKeyScopes.BuilderCode))
            return Error($"Khối nhúng HTML (data-nc-html) cần scope '{ApiKeyScopes.BuilderCode}'.");
        if (BlockCodeExtractor.CarriesRawHtml(compiledHtml) && !HasScope(ApiKeyScopes.BuilderCode))
            return Error($"Khối nhúng HTML (data-nc-html) cần scope '{ApiKeyScopes.BuilderCode}'.");

        Guid? id = null;
        if (!string.IsNullOrWhiteSpace(pageId))
        {
            if (!Guid.TryParse(pageId, out var parsed)) return Error($"pageId '{pageId}' không phải GUID hợp lệ.");
            id = parsed;
        }

        var request = new BuilderPageSaveRequest(title, slug, null, compiledHtml, compiledCss, null, customJs, kind);
        return Serialize(await _api.SavePageAsync(id, request, ct));
    }

    [McpServerTool(Name = "site_publish_page", Title = "Xuất bản trang")]
    [Description("Xuất bản một trang: tăng số phiên bản, tạo bản lưu (revision) và cho phép truy cập công khai theo route của trang.")]
    public async Task<string> PublishPageAsync(
        [Description("Id trang (GUID).")] string pageId,
        [Description("Ghi chú cho bản lưu này.")] string? note = null,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderWrite) is { } denied) return denied;
        if (!Guid.TryParse(pageId, out var id)) return Error($"pageId '{pageId}' không phải GUID hợp lệ.");

        return Serialize(await _api.PublishPageAsync(id, note, ct));
    }

    [McpServerTool(Name = "site_delete_page", Destructive = true, Title = "Xoá trang")]
    [Description("Xoá mềm một trang (đánh dấu đã xoá, dữ liệu vẫn còn trong DB). Trang sẽ không còn truy cập được từ bên ngoài.")]
    public async Task<string> DeletePageAsync(
        [Description("Id trang (GUID).")] string pageId,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderWrite) is { } denied) return denied;
        if (!Guid.TryParse(pageId, out var id)) return Error($"pageId '{pageId}' không phải GUID hợp lệ.");

        return Serialize(await _api.DeletePageAsync(id, ct));
    }

    [McpServerTool(Name = "site_save_layout", Title = "Tạo hoặc cập nhật layout")]
    [Description("Tạo/cập nhật layout dùng chung (Shell, Header, Footer, Sidebar). Layout Shell phải chứa một phần tử có thuộc tính data-nc-body — nội dung trang được thế vào đó.")]
    public async Task<string> SaveLayoutAsync(
        [Description("Key ổn định của layout, ví dụ \"shell-default\".")] string key,
        [Description("Tên hiển thị.")] string name,
        [Description("Loại: Shell, Header, Footer, Sidebar.")] string kind = "Shell",
        [Description("HTML của layout.")] string? compiledHtml = null,
        [Description("CSS riêng của layout.")] string? compiledCss = null,
        [Description("CSS viết tay của layout — nối SAU compiledCss nên luôn thắng. Shell builder trực quan trong admin chỉ ghi compiledCss, nên CSS đặt ở đây không bị lần lưu builder sau xoá mất.")] string? customCss = null,
        [Description("JavaScript riêng của layout. Cần scope builder.code.")] string? customJs = null,
        [Description("Đặt làm layout mặc định cho trang mới.")] bool isDefault = false,
        [Description("Id layout cần cập nhật (GUID). Bỏ trống để tạo mới.")] string? layoutId = null,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderWrite) is { } denied) return denied;
        if (!string.IsNullOrWhiteSpace(customJs) && !HasScope(ApiKeyScopes.BuilderCode))
            return Error($"customJs cần scope '{ApiKeyScopes.BuilderCode}'.");

        Guid? id = null;
        if (!string.IsNullOrWhiteSpace(layoutId))
        {
            if (!Guid.TryParse(layoutId, out var parsed)) return Error($"layoutId '{layoutId}' không phải GUID hợp lệ.");
            id = parsed;
        }

        var request = new SiteLayoutSaveRequest(key, name, kind, null, compiledHtml, compiledCss, customCss, customJs, isDefault);
        return Serialize(await _api.SaveLayoutAsync(id, request, ct));
    }

    [McpServerTool(Name = "site_save_design_tokens", Idempotent = true, Title = "Ghi design token")]
    [Description("Tạo/cập nhật design token theo lô. Mỗi token sinh ra cả CSS variable (--color-brand-500) lẫn utility Tailwind (bg-brand-500) từ cùng một nguồn, nên đổi token là đổi toàn site.")]
    public async Task<string> SaveDesignTokensAsync(
        [Description("Mảng JSON các token: [{\"group\":\"Color\",\"key\":\"brand-500\",\"value\":\"#0d7c66\",\"sortOrder\":0}]. group hợp lệ: Color, Font, Space, Radius, Shadow.")] string tokensJson,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderWrite) is { } denied) return denied;

        List<DesignTokenSpec>? tokens;
        try
        {
            tokens = JsonSerializer.Deserialize<List<DesignTokenSpec>>(tokensJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            return Error($"tokensJson không parse được: {ex.Message}");
        }

        if (tokens is null || tokens.Count == 0) return Error("tokensJson rỗng.");
        return Serialize(await _api.SaveDesignTokensAsync(tokens, ct));
    }

    [McpServerTool(Name = "site_save_seo_meta", Idempotent = true, Title = "Ghi SEO meta")]
    [Description("Ghi thẻ SEO cho một entity (Page, Category, Post, Product) theo ngôn ngữ: title, description, OG image, canonical, robots, JSON-LD.")]
    public async Task<string> SaveSeoMetaAsync(
        [Description("Loại entity: Page, Category, Post, Product.")] string entityType,
        [Description("Id của entity (GUID).")] string entityId,
        [Description("Mã ngôn ngữ, ví dụ \"vi\".")] string culture = "vi",
        [Description("Thẻ title.")] string? metaTitle = null,
        [Description("Thẻ description.")] string? metaDescription = null,
        [Description("URL ảnh Open Graph.")] string? ogImage = null,
        [Description("URL canonical.")] string? canonical = null,
        [Description("Chỉ thị robots, ví dụ \"index, follow\".")] string? robots = null,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderWrite) is { } denied) return denied;
        if (!Guid.TryParse(entityId, out var id)) return Error($"entityId '{entityId}' không phải GUID hợp lệ.");

        var request = new SeoMetaSaveRequest(
            entityType, id, culture, metaTitle, metaDescription, ogImage, canonical, robots,
            SchemaJsonLd: null, OgType: null, TwitterCard: null, Priority: null, ChangeFreq: null);

        return Serialize(await _api.SaveSeoMetaAsync(request, ct));
    }

    [McpServerTool(Name = "site_save_custom_code", Title = "Ghi custom CSS/JS của site")]
    [Description("Ghi CSS/JS tuỳ chỉnh ở cấp site, cùng snippet chèn vào cuối <head> và cuối <body>. Cần scope builder.code vì tương đương thực thi mã trên trình duyệt của khách truy cập.")]
    public async Task<string> SaveCustomCodeAsync(
        [Description("CSS áp dụng toàn site.")] string? customCss = null,
        [Description("JavaScript áp dụng toàn site.")] string? customJs = null,
        [Description("HTML chèn vào cuối thẻ head (analytics, pixel...).")] string? headHtml = null,
        [Description("HTML chèn trước thẻ đóng body.")] string? bodyEndHtml = null,
        CancellationToken ct = default)
    {
        if (Deny(ApiKeyScopes.BuilderCode) is { } denied) return denied;

        var request = new SiteCustomCodeSaveRequest(customCss, customJs, headHtml, bodyEndHtml);
        return Serialize(await _api.SaveCustomCodeAsync(request, ct));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Kiểm scope và gắn site của key vào ICurrentSite. Trả thông báo lỗi khi bị chặn;
    /// null nghĩa là được phép đi tiếp.
    ///
    /// Việc gắn site phải làm ở đây chứ không chỉ trong middleware: tool chạy trong một DI
    /// scope riêng của MCP server, nên ICurrentSite mà ISiteBuilderApi nhận được KHÁC instance
    /// mà McpApiKeyMiddleware đã set trên request scope.
    /// </summary>
    private string? Deny(string scope)
    {
        var key = ApiKey;
        if (key is null) return Error("Yêu cầu chưa được xác thực bằng API key.");
        if (!key.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
            return Error($"API key thiếu scope '{scope}'.");

        _currentSite.Set(key.SiteId, key.SiteSlug, key.SiteTheme);
        return null;
    }

    private bool HasScope(string scope) =>
        ApiKey is { } key && key.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);

    /// <summary>Key đã xác thực, do McpApiKeyMiddleware đặt vào HttpContext.Items.</summary>
    private AuthenticatedApiKey? ApiKey =>
        _http.HttpContext?.Items[McpContextItems.ApiKey] as AuthenticatedApiKey;

    /// <summary>Spec có mang customJs ở bất kỳ tầng nào không (page, layout).</summary>
    private static bool SpecCarriesCustomJs(SiteSpec spec) =>
        (spec.Pages ?? Array.Empty<PageSpec>()).Any(p => !string.IsNullOrWhiteSpace(p.CustomJs))
        || (spec.Layouts ?? Array.Empty<LayoutSpec>()).Any(l => !string.IsNullOrWhiteSpace(l.CustomJs));

    private static bool SpecCarriesRawHtml(SiteSpec spec) =>
        (spec.Pages ?? Array.Empty<PageSpec>()).Any(p => BlockCodeExtractor.CarriesRawHtml(p.CompiledHtml))
        || (spec.Layouts ?? Array.Empty<LayoutSpec>()).Any(l => BlockCodeExtractor.CarriesRawHtml(l.CompiledHtml));

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOpts);

    private static string Error(string? message) =>
        JsonSerializer.Serialize(new { succeeded = false, error = message ?? "Lỗi không xác định." }, JsonOpts);
}
