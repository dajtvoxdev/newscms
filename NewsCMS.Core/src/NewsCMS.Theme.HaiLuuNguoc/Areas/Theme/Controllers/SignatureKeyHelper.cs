using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace NewsCMS.Theme.HaiLuuNguoc.Areas.Theme.Controllers;

internal static class SignatureKeyHelper
{
    public static string Sha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static string IpHashFromRequest(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
        var fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(fwd))
        {
            var first = fwd.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first)) ip = first;
        }
        return Sha256(ip);
    }
}
