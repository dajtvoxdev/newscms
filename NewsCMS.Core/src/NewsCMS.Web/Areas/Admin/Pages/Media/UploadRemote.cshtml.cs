using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using NewsCMS.Application.Content;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Media;

/// <summary>
/// POST /admin/Media/UploadRemote — body { url } — downloads a remote image,
/// validates it, and delegates persistence to IMediaService.
/// Used by TinyMCE paste handler to re-host externally pasted images.
/// </summary>
[IgnoreAntiforgeryToken(Order = 1001)]
[Authorize(Permissions.Media.Upload)]
public sealed class UploadRemoteModel : PageModel
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IMediaService _media;
    private readonly UserManager<AppUser> _users;
    private readonly IConfiguration _cfg;
    private readonly ILogger<UploadRemoteModel> _logger;

    public UploadRemoteModel(
        IHttpClientFactory httpFactory,
        IMediaService media,
        UserManager<AppUser> users,
        IConfiguration cfg,
        ILogger<UploadRemoteModel> logger)
    {
        _httpFactory = httpFactory;
        _media = media;
        _users = users;
        _cfg = cfg;
        _logger = logger;
    }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync([FromBody] RemoteRequest? body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Url))
            return BadRequest(new { error = "URL trống." });

        if (!Uri.TryCreate(body.Url, UriKind.Absolute, out var uri))
            return BadRequest(new { error = "URL không hợp lệ." });

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return BadRequest(new { error = "Chỉ chấp nhận http/https." });

        if (await IsBlockedHostAsync(uri.Host, ct))
            return BadRequest(new { error = "Host không được phép." });

        var maxBytes = (_cfg.GetValue<int?>("Storage:MaxFileSizeMb") ?? 20) * 1024L * 1024L;

        var http = _httpFactory.CreateClient("remote-media");
        using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            return BadRequest(new { error = $"Tải thất bại (HTTP {(int)resp.StatusCode})." });

        var mime = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Tài nguyên không phải ảnh." });

        if (resp.Content.Headers.ContentLength is long len && len > maxBytes)
            return BadRequest(new { error = $"Ảnh vượt quá {maxBytes / (1024 * 1024)}MB." });

        var ext = MimeToExtension(mime);
        var allowed = _cfg.GetSection("Storage:AllowedExtensions").Get<string[]>()
                      ?? new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg" };
        if (!allowed.Contains(ext))
            return BadRequest(new { error = $"Phần mở rộng '{ext}' không được phép." });

        var originalName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(originalName) || !originalName.Contains('.'))
            originalName = "remote" + ext;

        // Buffer with hard cap to prevent runaway downloads from servers omitting Content-Length.
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var buffer = new MemoryStream();
        var capBuf = new byte[81920];
        long total = 0;
        int read;
        while ((read = await src.ReadAsync(capBuf, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                return BadRequest(new { error = $"Ảnh vượt quá {maxBytes / (1024 * 1024)}MB." });
            await buffer.WriteAsync(capBuf.AsMemory(0, read), ct);
        }
        buffer.Position = 0;

        var userId = Guid.Parse(_users.GetUserId(User)!);
        var result = await _media.CreateFromUploadAsync(buffer, originalName, mime, total, userId, null, ct);

        if (!result.Succeeded)
            return BadRequest(new { error = result.Error });

        var dto = result.Value!;
        return new JsonResult(new
        {
            location = dto.Location,
            fileName = dto.FileName,
            size = dto.Size,
            width = dto.Width,
            height = dto.Height
        });
    }

    private static string MimeToExtension(string mime) => mime.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "image/bmp" => ".bmp",
        _ => ".bin"
    };

    private static async Task<bool> IsBlockedHostAsync(string host, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, ct);
            foreach (var ip in addrs)
                if (IsPrivateOrReserved(ip)) return true;
        }
        catch
        {
            return true;
        }
        return false;
    }

    private static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
            if (b[0] == 0) return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xfe) == 0xfc) return true;
        }
        return false;
    }

    public sealed record RemoteRequest(string? Url);
}
