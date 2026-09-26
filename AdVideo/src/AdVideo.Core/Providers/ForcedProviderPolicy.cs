namespace AdVideo.Core.Providers;

/// <summary>
/// Quyết định khách có được ép một provider cụ thể qua <c>options.provider</c> hay không.
/// </summary>
/// <remarks>
/// <para>
/// Luật 3 nói khách không chọn provider. <c>options.provider</c> tồn tại chỉ để người vận hành
/// chẩn đoán, nên mặc định nó <b>đóng</b>: allowlist rỗng nghĩa là không ai ép được gì. Người
/// vận hành mở từng tên một bằng setting <c>ForceableVideoProviders</c>, không phải bằng deploy.
/// </para>
/// <para>
/// <b>Provider giả không bao giờ ép được ở Production</b>, kể cả khi ai đó lỡ ghi nó vào allowlist:
/// khách ép được provider giả là khách nhận về video testsrc2 mà hệ thống báo thành công.
/// </para>
/// <para>
/// Đây mới là cửa thứ nhất. Qua được cửa này, provider vẫn phải qua
/// <see cref="ProviderCapabilityValidator"/> như mọi provider tự chọn — ép tên không phải là ép
/// được năng lực mà provider không có.
/// </para>
/// </remarks>
public static class ForcedProviderPolicy
{
    /// <summary>Tách chuỗi allowlist dạng <c>"kling, seedance"</c>. Rỗng hoặc null = danh sách rỗng.</summary>
    public static IReadOnlyList<string> ParseAllowlist(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv
                .Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>
    /// Lý do từ chối, hoặc <c>null</c> nếu được phép đi tiếp tới bước kiểm năng lực.
    /// </summary>
    public static string? Check(string forcedProvider, IReadOnlyList<string> allowlist, bool isProduction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(forcedProvider);
        ArgumentNullException.ThrowIfNull(allowlist);

        if (isProduction && string.Equals(forcedProvider, ProviderNames.Fake, StringComparison.OrdinalIgnoreCase))
        {
            return "Không được ép provider giả trên môi trường production. Bỏ trống options.provider để hệ thống tự chọn.";
        }

        if (!allowlist.Contains(forcedProvider, StringComparer.OrdinalIgnoreCase))
        {
            return $"Provider \"{forcedProvider}\" không nằm trong danh sách được phép chỉ định. " +
                   "Bỏ trống options.provider để hệ thống tự chọn.";
        }

        return null;
    }
}
