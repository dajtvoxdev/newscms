using AdVideo.Api.Auth;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Providers.Declarative;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Api.Admin;

/// <summary>
/// API quản trị <c>/v1/admin</c> — cửa để app (NewsCMS admin) cấu hình AdVideo thay cho CLI trên máy chủ.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mọi route nằm sau policy <see cref="AuthPolicies.Operator"/></b>, gắn ở nhóm chứ không ở từng
/// route: thêm một endpoint mới mà quên gắn quyền là không thể.
/// </para>
/// <para>
/// <b>Không gì ở đây ghi ra ngoài qua đường riêng.</b> Credential, setting, tenant đi qua đúng các
/// service mà CLI dùng (<see cref="CredentialAdmin"/>, <see cref="SettingAdmin"/>,
/// <see cref="TenantAdmin"/>); descriptor và prompt đi qua store có sẵn. Cache của mọi tiến trình
/// được làm hỏng như khi sửa bằng CLI — không có thay đổi nào đòi restart.
/// </para>
/// <para>
/// Hai việc cố ý KHÔNG có endpoint: sinh operator key (gốc của quyền, chỉ CLI) và <c>migrate</c>
/// (đổi schema là bước deploy có người nhìn).
/// </para>
/// </remarks>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder admin = app.MapGroup("/v1/admin").RequireAuthorization(AuthPolicies.Operator);

        MapSettings(admin.MapGroup("/settings"));
        MapCredentials(admin.MapGroup("/credentials"));
        MapDescriptors(admin.MapGroup("/descriptors"));
        MapPrompts(admin.MapGroup("/prompts"));
        MapTenants(admin.MapGroup("/tenants"));
        admin.MapLabelFontEndpoints();

        return app;
    }

    // ---------------------------------------------------------------- settings

    private static void MapSettings(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AdVideoDbContext db, CancellationToken ct) =>
        {
            List<SystemSetting> rows = await db.SystemSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct);

            return Results.Ok(rows.Select(SettingView.From));
        })
        .WithSummary("Mọi setting kèm kiểu, khoảng hợp lệ và cờ tạm (chưa đo thật).");

        group.MapPut("/{key}", async (
            HttpContext http, string key, [FromBody] UpdateSettingRequest? body, SettingAdmin settings, CancellationToken ct) =>
        {
            SettingUpdateResult result = await settings.UpdateAsync(key, body?.Value, body?.IsProvisional, ct);

            if (result.NotFound)
            {
                return ApiProblem.NotFound(http, $"{result.Error} Khoá mới sinh ra từ code, không tạo từ màn hình quản trị.");
            }

            return result.Error is not null
                ? ApiProblem.Unprocessable(http, "Giá trị không hợp lệ", [result.Error])
                : Results.Ok(SettingView.From(result.Setting!));
        })
        .WithSummary("Đổi giá trị một setting đã có. Có hiệu lực trên API lẫn Worker trong vòng vài giây.");
    }

    // ---------------------------------------------------------------- credentials

    private static void MapCredentials(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AdVideoDbContext db, CancellationToken ct) =>
        {
            List<ProviderCredential> rows = await db.ProviderCredentials
                .AsNoTracking()
                .OrderBy(c => c.Category).ThenBy(c => c.Provider).ThenByDescending(c => c.Priority)
                .ToListAsync(ct);

            return Results.Ok(rows.Select(CredentialView.From));
        })
        .WithSummary("Mọi credential provider. Key chỉ hiện bản che — không có đường nào đọc lại key gốc.");

        group.MapPut("/{provider}", async (
            HttpContext http, string provider, [FromBody] UpsertCredentialRequest? body, CredentialAdmin credentials, CancellationToken ct) =>
        {
            CredentialAdminResult result = await credentials.UpsertAsync(
                new CredentialInput
                {
                    Provider = provider,
                    ApiKey = body?.ApiKey,
                    ModelId = body?.ModelId,
                    EndpointUrl = body?.EndpointUrl,
                    CapabilityJson = body?.Capability is { ValueKind: not System.Text.Json.JsonValueKind.Null } cap ? cap.GetRawText() : null,
                    Priority = body?.Priority,
                    IsActive = body?.IsActive,
                    Note = body?.Note,
                },
                ct);

            if (result.Error is not null)
            {
                return ApiProblem.Unprocessable(http, "Không lưu được credential", [result.Error]);
            }

            return Results.Ok(new UpsertCredentialResponse(CredentialView.From(result.Credential!), result.KeyChanged, result.Notices));
        })
        .WithSummary("Nạp hoặc sửa credential scope System. Trường bỏ trống = giữ giá trị đang có; api_key bỏ trống = giữ key cũ.");

        group.MapPost("/{provider}/deactivate", async (
            HttpContext http, string provider, [FromBody] ReasonRequest? body, ICredentialStore store, CancellationToken ct) =>
        {
            string reason = string.IsNullOrWhiteSpace(body?.Reason)
                ? $"Tắt từ API quản trị bởi {http.User.Identity?.Name}"
                : body!.Reason!.Trim();

            await store.DeactivateAsync(provider.Trim().ToLowerInvariant(), reason, ct);

            return Results.NoContent();
        })
        .WithSummary("Tắt mọi credential System của một provider. Bật lại bằng PUT với is_active = true.");
    }

    // ---------------------------------------------------------------- descriptors

    private static void MapDescriptors(RouteGroupBuilder group)
    {
        group.MapGet("/", async (string? provider, IDescriptorStore store, CancellationToken ct) =>
        {
            IReadOnlyList<ProviderDescriptorRow> rows = await store.ListAsync(provider, ct);

            return Results.Ok(rows.Select(r => DescriptorView.From(r, includeJson: false)));
        })
        .WithSummary("Mọi bản descriptor, mới nhất trước. Lọc bằng ?provider=.");

        group.MapGet("/{provider}/versions/{version:int}", async (
            HttpContext http, string provider, int version, IDescriptorStore store, CancellationToken ct) =>
        {
            ProviderDescriptorRow? row = (await store.ListAsync(provider, ct)).FirstOrDefault(r => r.Version == version);

            return row is null
                ? ApiProblem.NotFound(http, $"Không có descriptor {provider} bản {version}.")
                : Results.Ok(DescriptorView.From(row, includeJson: true));
        })
        .WithSummary("Một bản descriptor, kèm JSON đầy đủ.");

        group.MapPost("/", async (
            HttpContext http, [FromBody] AddDescriptorRequest? body, IDescriptorStore store, CancellationToken ct) =>
        {
            if (body?.RawJson is not { } json)
            {
                return ApiProblem.Unprocessable(
                    http, "Thiếu descriptor", ["Gửi \"descriptor\" (JSON object) hoặc \"descriptor_json\" (nguyên văn file, giữ được comment)."]);
            }

            try
            {
                ProviderDescriptorRow row = await store.AddVersionAsync(json, body.Note, ct);

                return Results.Created(
                    $"/v1/admin/descriptors/{row.Code}/versions/{row.Version}",
                    DescriptorView.From(row, includeJson: false));
            }
            catch (DescriptorInvalidException ex)
            {
                return ApiProblem.Unprocessable(http, "Descriptor không hợp lệ", ex.Errors);
            }
        })
        .WithSummary("Kiểm rồi lưu một bản descriptor mới ở trạng thái TẮT. Bật bằng .../activate.");

        group.MapPost("/{provider}/versions/{version:int}/activate", async (
            HttpContext http, string provider, int version, IDescriptorStore store, CancellationToken ct) =>
        {
            bool exists = (await store.ListAsync(provider, ct)).Any(r => r.Version == version);

            if (!exists)
            {
                return ApiProblem.NotFound(http, $"Không có descriptor {provider} bản {version}.");
            }

            try
            {
                await store.ActivateVersionAsync(provider, version, ct);
            }
            catch (DescriptorInvalidException ex)
            {
                return ApiProblem.Unprocessable(http, "Descriptor không còn hợp lệ với bộ kiểm hiện tại", ex.Errors);
            }

            return Results.NoContent();
        })
        .WithSummary("Bật một bản (tắt bản đang bật) và ghi capability vào credential cùng tên.");

        group.MapPost("/{provider}/deactivate", async (string provider, IDescriptorStore store, CancellationToken ct) =>
        {
            await store.DeactivateAsync(provider, ct);

            return Results.NoContent();
        })
        .WithSummary("Tắt descriptor đang bật. Provider quay về adapter viết tay nếu có.");

        group.MapPost("/preview", async (
            HttpContext http, [FromBody] PreviewDescriptorRequest? body, IDescriptorStore store, ISettingsStore settings, CancellationToken ct) =>
        {
            string? json = body?.RawJson;

            if (json is null && !string.IsNullOrWhiteSpace(body?.Provider))
            {
                IReadOnlyList<ProviderDescriptorRow> rows = await store.ListAsync(body.Provider, ct);
                json = (body.Version is { } v ? rows.FirstOrDefault(r => r.Version == v) : rows.FirstOrDefault())?.Json;

                if (json is null)
                {
                    return ApiProblem.NotFound(http, $"Không có descriptor {body.Provider}{(body.Version is { } ver ? $" bản {ver}" : string.Empty)}.");
                }
            }

            if (json is null)
            {
                return ApiProblem.Unprocessable(
                    http, "Thiếu descriptor", ["Gửi \"descriptor\" / \"descriptor_json\", hoặc \"provider\" [+ \"version\"] của bản đã lưu."]);
            }

            DescriptorParseResult parsed = ProviderDescriptorParser.Parse(json);

            if (!parsed.IsValid)
            {
                return ApiProblem.Unprocessable(http, "Descriptor không hợp lệ", parsed.Errors);
            }

            string? allowlist = await settings.GetStringAsync(SettingKeys.ProviderHostAllowlist, ct);
            DescriptorPreviewResult preview = DescriptorPreview.Render(parsed.Descriptor!, ProviderHostAllowlist.Parse(allowlist));

            return Results.Ok(new DescriptorPreviewView(preview.Method, preview.Url, preview.Headers, preview.Body, preview.Warnings));
        })
        .WithSummary("Chạy khô: dựng request mẫu và soát allowlist. KHÔNG gọi mạng, key được che.");
    }

    // ---------------------------------------------------------------- prompts

    private static void MapPrompts(RouteGroupBuilder group)
    {
        group.MapGet("/", async (string? code, AdVideoDbContext db, CancellationToken ct) =>
        {
            IQueryable<PromptTemplate> query = db.PromptTemplates.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(code))
            {
                query = query.Where(p => p.Code == code);
            }

            List<PromptTemplate> rows = await query.OrderBy(p => p.Code).ThenByDescending(p => p.Version).ToListAsync(ct);

            return Results.Ok(rows.Select(PromptView.From));
        })
        .WithSummary("Mọi phiên bản prompt, theo code rồi mới nhất trước. Lọc bằng ?code=.");

        group.MapPost("/{code}/versions", async (
            HttpContext http, string code, [FromBody] AddPromptVersionRequest? body, IPromptStore store, CancellationToken ct) =>
        {
            var errors = new List<string>();

            if (body?.Kind is null)
            {
                errors.Add("Thiếu kind (global_negative, director, format, quality_check).");
            }

            if (string.IsNullOrWhiteSpace(body?.Content))
            {
                errors.Add("Thiếu content.");
            }

            if (string.IsNullOrWhiteSpace(body?.ChangeNote))
            {
                // Bắt buộc: prompt không ghi lý do đổi là prompt không ai dám rollback.
                errors.Add("Thiếu change_note — ghi vì sao đổi, để lần sau còn biết có nên quay lại không.");
            }

            if (errors.Count > 0)
            {
                return ApiProblem.Unprocessable(http, "Không thêm được phiên bản prompt", errors);
            }

            PromptTemplate added = await store.AddVersionAsync(
                code.Trim(), body!.Kind!.Value, body.Content!, body.ChangeNote!.Trim(), body.FormatCode, ct);

            if (body.Activate == true)
            {
                await store.ActivateVersionAsync(added.Code, added.Version, ct);
                added.IsActive = true;
            }

            return Results.Created($"/v1/admin/prompts?code={Uri.EscapeDataString(added.Code)}", PromptView.From(added));
        })
        .WithSummary("Thêm phiên bản prompt mới. Bản cũ giữ lại để rollback và tái hiện video cũ.");

        group.MapPost("/{code}/versions/{version:int}/activate", async (
            HttpContext http, string code, int version, AdVideoDbContext db, IPromptStore store, CancellationToken ct) =>
        {
            bool exists = await db.PromptTemplates.AnyAsync(p => p.Code == code && p.Version == version, ct);

            if (!exists)
            {
                return ApiProblem.NotFound(http, $"Không có prompt {code} bản {version}.");
            }

            await store.ActivateVersionAsync(code, version, ct);

            return Results.NoContent();
        })
        .WithSummary("Bật một phiên bản prompt (tắt bản đang bật cùng code) — cũng là cách rollback.");
    }

    // ---------------------------------------------------------------- tenants

    private static void MapTenants(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AdVideoDbContext db, CancellationToken ct) =>
        {
            List<Tenant> rows = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);

            return Results.Ok(rows.Select(TenantView.From));
        })
        .WithSummary("Mọi tenant. Chỉ có prefix key, không có key.");

        group.MapPost("/", async (
            HttpContext http, [FromBody] CreateTenantRequest? body, TenantAdmin tenants, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Name))
            {
                return ApiProblem.Unprocessable(http, "Thiếu tên tenant", ["Gửi \"name\", ví dụ tên site NewsCMS."]);
            }

            IssuedTenantKey issued = await tenants.CreateAsync(body.Name, body.Note, ct);

            NoStore(http);

            return Results.Created(
                $"/v1/admin/tenants/{issued.Tenant.Id}",
                new IssuedTenantKeyResponse(TenantView.From(issued.Tenant), issued.ApiKey));
        })
        .WithSummary("Tạo tenant. api_key chỉ có trong phản hồi này — mất thì cấp lại bằng rotate-key.");

        group.MapPost("/{id:guid}/rotate-key", async (HttpContext http, Guid id, TenantAdmin tenants, CancellationToken ct) =>
        {
            IssuedTenantKey? issued = await tenants.RotateKeyAsync(id, ct);

            if (issued is null)
            {
                return ApiProblem.NotFound(http, $"Không có tenant {id}.");
            }

            NoStore(http);

            return Results.Ok(new IssuedTenantKeyResponse(TenantView.From(issued.Tenant), issued.ApiKey));
        })
        .WithSummary("Cấp key mới. Key cũ mất hiệu lực NGAY.");

        group.MapPost("/{id:guid}/activate", (HttpContext http, Guid id, [FromBody] ReasonRequest? body, TenantAdmin tenants, CancellationToken ct) =>
            SetActiveAsync(http, id, true, body, tenants, ct));

        group.MapPost("/{id:guid}/deactivate", (HttpContext http, Guid id, [FromBody] ReasonRequest? body, TenantAdmin tenants, CancellationToken ct) =>
            SetActiveAsync(http, id, false, body, tenants, ct))
            .WithSummary("Tạm ngừng tenant: chặn request mới ngay, job đang chạy vẫn chạy nốt.");
    }

    private static async Task<IResult> SetActiveAsync(
        HttpContext http, Guid id, bool isActive, ReasonRequest? body, TenantAdmin tenants, CancellationToken ct)
    {
        Tenant? tenant = await tenants.SetActiveAsync(id, isActive, body?.Reason, ct);

        return tenant is null
            ? ApiProblem.NotFound(http, $"Không có tenant {id}.")
            : Results.Ok(TenantView.From(tenant));
    }

    /// <summary>Phản hồi chứa secret không được nằm lại trong cache của proxy hay trình duyệt.</summary>
    internal static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
    }
}
