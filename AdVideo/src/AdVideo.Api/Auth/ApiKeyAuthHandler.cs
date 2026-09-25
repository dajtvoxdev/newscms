using System.Security.Claims;
using System.Text.Encodings.Web;
using AdVideo.Core.Entities;
using AdVideo.Core.Security;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Persistence.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AdVideo.Api.Auth;

/// <summary>
/// Xác thực bằng header <c>X-AdVideo-Key</c> và gắn tenant cho toàn bộ request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Handler này là nơi duy nhất tenant được xác lập.</b> Global query filter của EF đọc
/// <see cref="ITenantContext"/>; nếu có bất kỳ đường nào khác đặt được tenant — một header phụ,
/// một tham số query, một giá trị mặc định lúc dev — thì đó là đường rò dữ liệu sang khách khác.
/// </para>
/// <para>
/// <b>Vì sao không cache kết quả tra key.</b> Thu hồi một key phải có hiệu lực ngay. Cache 60
/// giây nghĩa là một key bị lộ vẫn dùng được thêm 60 giây sau khi người vận hành đã tắt nó, và
/// cái giá tiết kiệm được là một lần tra index trên một bảng rất nhỏ. Nếu có ngày bảng này lớn
/// tới mức phải cache, thì phải cache kèm đường vô hiệu hoá tức thì, không phải cache theo TTL.
/// </para>
/// </remarks>
public sealed class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdVideoApiKey";
    public const string HeaderName = "X-AdVideo-Key";

    /// <summary>Claim chứa <c>TenantId</c>. Endpoint đọc qua <see cref="ClaimsPrincipalExtensions"/>.</summary>
    public const string TenantIdClaim = "advideo:tenant_id";

    private readonly AdVideoDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ApiKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AdVideoDbContext db,
        ITenantContext tenantContext)
        : base(options, logger, encoder)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues values))
        {
            // NoResult chứ không Fail: "không gửi key" là chuyện bình thường ở endpoint công khai
            // như /healthz. Fail ở đây sẽ ghi log lỗi cho mọi lần gọi healthz.
            return AuthenticateResult.NoResult();
        }

        string apiKey = values.ToString().Trim();

        if (string.IsNullOrEmpty(apiKey))
        {
            return AuthenticateResult.Fail($"Header {HeaderName} rỗng.");
        }

        string prefix = ApiKeyHasher.LookupPrefix(apiKey);

        Tenant? tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ApiKeyPrefix == prefix);

        // Vẫn chạy Verify khi không tìm thấy tenant, với một hash giả có cùng độ dài: nếu thoát
        // sớm thì thời gian phản hồi của "prefix không tồn tại" khác hẳn "prefix đúng, phần bí
        // mật sai", và chênh lệch đó cho phép dò ra prefix hợp lệ.
        string storedHash = tenant?.ApiKeyHash ?? new string('0', 64);
        bool matches = ApiKeyHasher.Verify(apiKey, storedHash);

        if (tenant is null || !matches)
        {
            Logger.LogWarning("Từ chối API key {Prefix} — không khớp tenant nào.", prefix);

            return AuthenticateResult.Fail("API key không hợp lệ.");
        }

        if (!tenant.IsActive)
        {
            Logger.LogWarning("Tenant {TenantId} đang bị tắt nhưng vẫn gọi API.", tenant.Id);

            return AuthenticateResult.Fail("Tài khoản đang bị tạm ngừng. Liên hệ bên vận hành để mở lại.");
        }

        // Đặt tenant NGAY tại đây. Mọi truy vấn sau điểm này, kể cả trong cùng request, đều đi qua
        // global query filter với giá trị này.
        _tenantContext.SetTenant(tenant.Id);

        var identity = new ClaimsIdentity(
            [
                new Claim(TenantIdClaim, tenant.Id.ToString()),
                new Claim(ClaimTypes.Name, tenant.Name),
            ],
            SchemeName);

        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    /// <summary>Trả 401 kèm <c>ProblemDetails</c> thay vì thân rỗng.</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";

        await Response.WriteAsJsonAsync(new
        {
            type = "https://advideo/errors/unauthorized",
            title = "Thiếu hoặc sai API key",
            status = StatusCodes.Status401Unauthorized,
            detail = $"Gửi kèm header {HeaderName}. Key do bên vận hành cấp, mỗi tenant một key.",
        });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/problem+json";

        await Response.WriteAsJsonAsync(new
        {
            type = "https://advideo/errors/forbidden",
            title = "Không có quyền",
            status = StatusCodes.Status403Forbidden,
        });
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Lấy tenant từ principal.
    /// </summary>
    /// <remarks>
    /// Ném exception khi thiếu claim, cố ý: endpoint gọi hàm này đều nằm sau
    /// <c>RequireAuthorization()</c>, nên thiếu claim nghĩa là pipeline đã bị cấu hình sai. Trả về
    /// <see cref="Guid.Empty"/> trong tình huống đó sẽ biến một lỗi cấu hình thành một truy vấn
    /// chạy được nhưng sai tenant.
    /// </remarks>
    public static Guid GetTenantId(this ClaimsPrincipal principal)
    {
        string? raw = principal.FindFirstValue(ApiKeyAuthHandler.TenantIdClaim);

        return Guid.TryParse(raw, out Guid tenantId)
            ? tenantId
            : throw new InvalidOperationException(
                "Principal không có claim tenant. Endpoint này phải nằm sau RequireAuthorization().");
    }
}
