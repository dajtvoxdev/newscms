using System.Security.Claims;
using System.Text.Encodings.Web;
using AdVideo.Core.Entities;
using AdVideo.Core.Security;
using AdVideo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AdVideo.Api.Auth;

/// <summary>
/// Xác thực người vận hành bằng header <c>X-AdVideo-Operator-Key</c> cho nhóm <c>/v1/admin</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Không đặt tenant.</b> Operator không phải một khách; handler này cố ý không chạm vào
/// <c>ITenantContext</c>. Endpoint quản trị nào cần đọc dữ liệu của khách phải tự gọi
/// <c>IgnoreQueryFilters()</c> ngay tại truy vấn — để mỗi chỗ vượt rào đều đọc thấy được trong code,
/// thay vì một cờ bật ở đây làm mọi truy vấn trong request âm thầm thấy hết.
/// </para>
/// <para>
/// Cùng các quy tắc với <see cref="ApiKeyAuthHandler"/>: không cache kết quả tra key (thu hồi phải có
/// hiệu lực ngay), và vẫn chạy phép so hash khi không tìm thấy prefix (chống dò prefix bằng thời gian).
/// </para>
/// </remarks>
public sealed class OperatorKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdVideoOperatorKey";
    public const string HeaderName = "X-AdVideo-Operator-Key";

    /// <summary>Claim chứa <c>OperatorKey.Id</c>.</summary>
    public const string OperatorIdClaim = "advideo:operator_id";

    /// <summary>Không ghi <c>LastUsedAt</c> dày hơn mức này — một lần UPDATE cho mỗi request quản trị là thừa.</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(5);

    private readonly AdVideoDbContext _db;

    public OperatorKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AdVideoDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues values))
        {
            return AuthenticateResult.NoResult();
        }

        string key = values.ToString().Trim();

        if (string.IsNullOrEmpty(key))
        {
            return AuthenticateResult.Fail($"Header {HeaderName} rỗng.");
        }

        string prefix = ApiKeyHasher.LookupPrefix(key);

        OperatorKey? row = await _db.OperatorKeys.FirstOrDefaultAsync(k => k.ApiKeyPrefix == prefix);

        string storedHash = row?.ApiKeyHash ?? new string('0', 64);
        bool matches = ApiKeyHasher.Verify(key, storedHash);

        if (row is null || !matches)
        {
            Logger.LogWarning("Từ chối operator key {Prefix} — không khớp key nào.", prefix);

            return AuthenticateResult.Fail("Operator key không hợp lệ.");
        }

        if (!row.IsActive)
        {
            Logger.LogWarning("Operator key {Prefix} ({Name}) đã thu hồi nhưng vẫn được dùng.", prefix, row.Name);

            return AuthenticateResult.Fail("Operator key đã bị thu hồi.");
        }

        DateTime now = DateTime.UtcNow;

        if (row.LastUsedAt is not { } last || now - last > LastUsedResolution)
        {
            row.LastUsedAt = now;
            await _db.SaveChangesAsync();
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(OperatorIdClaim, row.Id.ToString()),
                new Claim(ClaimTypes.Name, row.Name),
            ],
            SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        AuthProblem.WriteAsync(
            Response,
            StatusCodes.Status401Unauthorized,
            "Thiếu hoặc sai operator key",
            $"Endpoint quản trị cần header {HeaderName}. Key sinh bằng lệnh CLI create-operator-key trên máy chủ; " +
            "API key của tenant không mở được cửa này.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        AuthProblem.WriteAsync(Response, StatusCodes.Status403Forbidden, "Không có quyền", null);
}

/// <summary>Tên policy phân quyền. Mỗi nhóm route gắn đúng một cái.</summary>
public static class AuthPolicies
{
    /// <summary>Khách gọi bằng <c>X-AdVideo-Key</c>. Bắt buộc có claim tenant.</summary>
    public const string Tenant = "AdVideoTenant";

    /// <summary>Người vận hành gọi bằng <c>X-AdVideo-Operator-Key</c>.</summary>
    public const string Operator = "AdVideoOperator";
}

/// <summary>Ghi phản hồi lỗi xác thực dạng <c>application/problem+json</c>.</summary>
internal static class AuthProblem
{
    public static Task WriteAsync(HttpResponse response, int status, string title, string? detail)
    {
        response.StatusCode = status;

        // Truyền contentType vào WriteAsJsonAsync: đặt response.ContentType trước rồi gọi bản không
        // tham số thì nó ghi đè lại thành application/json.
        return response.WriteAsJsonAsync(
            new
            {
                type = $"https://advideo/errors/{status}",
                title,
                status,
                detail,
            },
            options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json");
    }
}
