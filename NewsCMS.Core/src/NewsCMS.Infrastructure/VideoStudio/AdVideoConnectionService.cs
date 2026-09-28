using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.Site;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Domain.Entities.VideoStudio;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.VideoStudio;

/// <summary>
/// Kết nối NewsCMS ↔ AdVideo và liên kết site ↔ tenant.
/// </summary>
/// <remarks>
/// <b>Thứ tự khi liên kết site: gọi AdVideo trước, ghi DB sau.</b> Ghi trước rồi gọi hỏng là một
/// dòng liên kết không có key dùng được. Gọi xong mà ghi DB hỏng thì còn một tenant mồ côi bên
/// AdVideo — thứ thấy được trong danh sách tenant và tắt được, không làm hỏng site nào.
/// </remarks>
public sealed class AdVideoConnectionService : IAdVideoConnectionService
{
    /// <summary>Độ dài prefix key hiển thị — khớp <c>ApiKeyHasher.LookupPrefixLength</c> bên AdVideo.</summary>
    private const int PrefixLength = 12;

    private readonly AppDbContext _db;
    private readonly AdVideoKeyStore _keys;
    private readonly IAdVideoAdminClient _admin;
    private readonly IHttpClientFactory _http;
    private readonly ICurrentSite _site;

    public AdVideoConnectionService(
        AppDbContext db, AdVideoKeyStore keys, IAdVideoAdminClient admin, IHttpClientFactory http, ICurrentSite site)
    {
        _db = db;
        _keys = keys;
        _admin = admin;
        _http = http;
        _site = site;
    }

    public async Task<AdVideoConnectionDto> GetAsync(CancellationToken ct = default)
    {
        AdVideoConnection? c = await _keys.GetConnectionAsync(ct);

        return c is null
            ? new AdVideoConnectionDto(false, null, false, null, 60, null)
            : new AdVideoConnectionDto(
                !string.IsNullOrWhiteSpace(c.BaseUrl) && !string.IsNullOrEmpty(c.OperatorKeyEncrypted),
                c.BaseUrl,
                !string.IsNullOrEmpty(c.OperatorKeyEncrypted),
                c.OperatorKeyPrefix,
                c.TimeoutSeconds,
                c.UpdatedAt ?? c.CreatedAt);
    }

    public async Task<Result> SaveAsync(AdVideoConnectionInput input, CancellationToken ct = default)
    {
        string baseUrl = (input.BaseUrl ?? string.Empty).Trim().TrimEnd('/');

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Result.Failure("Địa chỉ AdVideo phải là URL http/https, ví dụ http://127.0.0.1:5080.");
        }

        if (input.TimeoutSeconds is < 5 or > 600)
        {
            return Result.Failure("Timeout phải trong khoảng 5–600 giây.");
        }

        string? key = input.OperatorKey?.Trim();

        if (!string.IsNullOrEmpty(key) && !key.StartsWith("advop_", StringComparison.Ordinal))
        {
            // Dán nhầm tenant key (adv_…) vào đây là lỗi hay gặp nhất — AdVideo sẽ trả 401 mà không
            // nói vì sao. Chặn sớm với lời giải thích.
            return Result.Failure("Operator key bắt đầu bằng \"advop_\". Key \"adv_…\" là key của một site, không mở được API quản trị.");
        }

        AdVideoConnection? connection = await _keys.GetConnectionAsync(ct);

        if (connection is null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return Result.Failure("Lần đầu cấu hình phải nhập operator key.");
            }

            connection = new AdVideoConnection { BaseUrl = baseUrl };
            _db.AdVideoConnections.Add(connection);
        }

        connection.BaseUrl = baseUrl;
        connection.TimeoutSeconds = input.TimeoutSeconds;
        connection.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(key))
        {
            connection.OperatorKeyEncrypted = _keys.ProtectOperatorKey(key);
            connection.OperatorKeyPrefix = key.Length <= PrefixLength ? key : key[..PrefixLength];
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<AdVideoHealthDto>> TestAsync(CancellationToken ct = default)
    {
        (AdVideoEndpoint? endpoint, string? error) = await _keys.GetOperatorEndpointAsync(ct);

        if (endpoint is null)
        {
            return Result<AdVideoHealthDto>.Failure(error!);
        }

        using HttpClient client = AdVideoHttp.CreateClient(_http, endpoint);

        // /healthz trả 503 khi không khoẻ nhưng thân vẫn là JSON có chi tiết — đọc thân dù mã lỗi.
        AdVideoHealthDto? health = null;

        try
        {
            using HttpResponseMessage response = await client.GetAsync("healthz", ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            health = System.Text.Json.JsonSerializer.Deserialize<AdVideoHealthDto>(body, AdVideoHttp.Json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return Result<AdVideoHealthDto>.Failure($"Không gọi được {endpoint.BaseUrl}/healthz: {ex.Message}");
        }

        if (health is null)
        {
            return Result<AdVideoHealthDto>.Failure($"{endpoint.BaseUrl} không trả lời như AdVideo.Api.");
        }

        // Healthz không cần key — phải gọi thêm một endpoint quản trị mới biết key có đúng không.
        Result<IReadOnlyList<AdVideoSettingDto>> auth = await _admin.GetSettingsAsync(ct);

        return auth.Succeeded
            ? Result<AdVideoHealthDto>.Success(health)
            : Result<AdVideoHealthDto>.Failure(auth.Error!);
    }

    public async Task<IReadOnlyList<AdVideoSiteLinkDto>> GetSiteLinksAsync(CancellationToken ct = default)
    {
        var sites = await _db.Sites.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Slug })
            .ToListAsync(ct);

        Dictionary<Guid, SiteAdVideoTenant> links = await _db.SiteAdVideoTenants.IgnoreQueryFilters().AsNoTracking()
            .ToDictionaryAsync(t => t.SiteId, ct);

        return sites
            .Select(s => links.TryGetValue(s.Id, out SiteAdVideoTenant? link)
                ? new AdVideoSiteLinkDto(s.Id, s.Name, s.Slug, true, link.AdVideoTenantId, link.ApiKeyPrefix, link.LinkedAt)
                : new AdVideoSiteLinkDto(s.Id, s.Name, s.Slug, false, null, null, null))
            .ToList();
    }

    public async Task<Result> LinkSiteAsync(Guid siteId, CancellationToken ct = default)
    {
        var site = await _db.Sites.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == siteId)
            .Select(s => new { s.Name, s.Slug })
            .FirstOrDefaultAsync(ct);

        if (site is null)
        {
            return Result.Failure("Không tìm thấy site.");
        }

        if (await _db.SiteAdVideoTenants.IgnoreQueryFilters().AnyAsync(t => t.SiteId == siteId, ct))
        {
            return Result.Failure("Site đã kết nối AdVideo. Dùng \"Cấp lại key\" nếu cần key mới.");
        }

        Result<AdVideoIssuedTenantKey> issued = await _admin.CreateTenantAsync(
            site.Name, $"NewsCMS site {site.Slug} ({siteId})", ct);

        if (!issued.Succeeded)
        {
            return Result.Failure(issued.Error!);
        }

        _db.SiteAdVideoTenants.Add(new SiteAdVideoTenant
        {
            SiteId = siteId,
            AdVideoTenantId = issued.Value!.Tenant.Id,
            ApiKeyEncrypted = _keys.ProtectTenantKey(issued.Value.ApiKey),
            ApiKeyPrefix = issued.Value.Tenant.ApiKeyPrefix,
            LinkedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> RotateSiteKeyAsync(Guid siteId, CancellationToken ct = default)
    {
        SiteAdVideoTenant? link = await _db.SiteAdVideoTenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.SiteId == siteId, ct);

        if (link is null)
        {
            return Result.Failure("Site chưa kết nối AdVideo.");
        }

        Result<AdVideoIssuedTenantKey> issued = await _admin.RotateTenantKeyAsync(link.AdVideoTenantId, ct);

        if (!issued.Succeeded)
        {
            return Result.Failure(issued.Error!);
        }

        // Key cũ đã chết bên AdVideo ngay lúc này — phải ghi key mới bằng mọi giá, nên không có
        // bước nào có thể thất bại chen giữa lời gọi và lệnh ghi.
        link.ApiKeyEncrypted = _keys.ProtectTenantKey(issued.Value!.ApiKey);
        link.ApiKeyPrefix = issued.Value.Tenant.ApiKeyPrefix;
        link.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> UnlinkSiteAsync(Guid siteId, CancellationToken ct = default)
    {
        SiteAdVideoTenant? link = await _db.SiteAdVideoTenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.SiteId == siteId, ct);

        if (link is null)
        {
            return Result.Failure("Site chưa kết nối AdVideo.");
        }

        Result deactivated = await _admin.SetTenantActiveAsync(link.AdVideoTenantId, false, "Bỏ liên kết từ NewsCMS", ct);

        if (!deactivated.Succeeded)
        {
            // Không xoá liên kết khi chưa tắt được tenant: xoá xong là mất dấu một key vẫn còn dùng được.
            return Result.Failure($"Chưa bỏ liên kết: không tắt được tenant bên AdVideo. {deactivated.Error}");
        }

        _db.SiteAdVideoTenants.Remove(link);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<bool> IsCurrentSiteLinkedAsync(CancellationToken ct = default) =>
        _site.IsResolved
        && _site.SiteId != Guid.Empty
        && await _db.SiteAdVideoTenants.IgnoreQueryFilters().AnyAsync(t => t.SiteId == _site.SiteId, ct);
}
