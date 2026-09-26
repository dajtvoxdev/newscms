using System.Net;
using System.Text.Json.Nodes;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;

namespace AdVideo.Infrastructure.Providers.Declarative;

/// <summary>
/// Quy lỗi của provider khai báo về <see cref="VideoFailureKind"/> theo khối <c>errors</c>.
/// </summary>
/// <remarks>
/// Thứ tự: mã trong thân (<c>byBodyCode</c>) → mã HTTP cụ thể → lớp mã (<c>4xx</c>) → luật chung
/// của <see cref="ProviderFailureMapper"/> (biết 429, 402, 401, 5xx, <c>content_policy</c>…) →
/// <c>errors.default</c>. Luật chung đứng TRƯỚC default để một descriptor khai sơ sài vẫn không
/// retry khi hết tiền.
/// </remarks>
public static class DescriptorErrorMapper
{
    public static VideoFailureKind FromResponse(DescriptorErrors? errors, HttpStatusCode status, JsonNode? body, string? rawBody)
    {
        if (FromBodyCode(errors, body) is { } byCode)
        {
            return byCode;
        }

        if (FromStatusTable(errors, status) is { } byStatus)
        {
            return byStatus;
        }

        VideoFailureKind generic = ProviderFailureMapper.FromStatus(status, rawBody);

        return generic != VideoFailureKind.Unknown ? generic : errors?.Default ?? VideoFailureKind.Unknown;
    }

    /// <summary>Phản hồi 2xx mà thân báo lỗi (<c>failWhen</c>) hoặc poll báo <c>failed</c>.</summary>
    public static VideoFailureKind FromFailedBody(DescriptorErrors? errors, JsonNode? body, string? rawBody) =>
        FromBodyCode(errors, body) ?? ProviderFailureMapper.FromFailedJobBody(rawBody);

    /// <summary>Mã HTTP này có nằm trong <c>deactivateCredentialOn</c> không.</summary>
    public static bool ShouldDeactivate(DescriptorErrors? errors, HttpStatusCode status)
    {
        if (errors is null)
        {
            return false;
        }

        string exact = ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string range = $"{exact[0]}xx";

        return errors.DeactivateCredentialOn.Any(k => k == exact || k == range);
    }

    private static VideoFailureKind? FromBodyCode(DescriptorErrors? errors, JsonNode? body)
    {
        if (errors?.ByBodyCode is not { } map || body is null)
        {
            return null;
        }

        string? code = JsonPathReader.ReadString(body, map.Path);

        if (code is null)
        {
            return null;
        }

        foreach ((string key, VideoFailureKind kind) in map.Table)
        {
            if (string.Equals(key, code, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }

    private static VideoFailureKind? FromStatusTable(DescriptorErrors? errors, HttpStatusCode status)
    {
        if (errors?.ByStatus is not { } table)
        {
            return null;
        }

        string exact = ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (table.TryGetValue(exact, out VideoFailureKind kind))
        {
            return kind;
        }

        return table.TryGetValue($"{exact[0]}xx", out VideoFailureKind rangeKind) ? rangeKind : null;
    }
}
