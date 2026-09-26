using System.Net;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers;

/// <summary>
/// Chặn mọi request tới host không nằm trong <see cref="SettingKeys.ProviderHostAllowlist"/>, kể cả
/// khi bị chuyển hướng.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lỗ đang bịt:</b> adapter fal từng gắn <c>Authorization: Key …</c> vào client rồi GET thẳng
/// <c>status_url</c>/<c>response_url</c> lấy từ <i>thân phản hồi của provider</i>. Một phản hồi bị
/// sửa là key đi tới host của kẻ tấn công. Với provider khai báo, URL còn là <b>dữ liệu người vận
/// hành gõ vào</b> — nên lớp này phải có trước descriptor đầu tiên.
/// </para>
/// <para>
/// <b>Tự theo redirect, có kiểm từng bước.</b> Client dùng handler này phải tắt
/// <c>AllowAutoRedirect</c>: redirect tự động xảy ra BÊN TRONG handler gốc, sau lưng lớp này — một
/// 302 sang <c>169.254.169.254</c> sẽ lọt. Chuyển sang host khác thì bỏ mọi header của request cũ,
/// để header xác thực không đi theo.
/// </para>
/// <para>
/// Setting đọc qua <see cref="ISettingsStore"/> (có cache) trong một scope riêng mỗi request: handler
/// sống lâu hơn scope của request, còn store là scoped.
/// </para>
/// </remarks>
public sealed class SsrfGuardingHandler : DelegatingHandler
{
    /// <summary>Số lần chuyển hướng tối đa. CDN thường chỉ một lần; năm lần đã là dấu hiệu vòng lặp.</summary>
    public const int MaxRedirects = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SsrfGuardingHandler> _logger;

    // Một tham chiếu duy nhất: handler dùng chung giữa nhiều request song song, và cặp (chuỗi gốc,
    // bản đã parse) phải được thay cùng lúc.
    private CacheEntry _cache = new(null, ProviderHostAllowlist.Parse(null));

    public SsrfGuardingHandler(IServiceScopeFactory scopeFactory, ILogger<SsrfGuardingHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ProviderHostAllowlist allowlist = await LoadAllowlistAsync(cancellationToken);

        EnsureAllowed(allowlist, request.RequestUri!);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        for (int hop = 0; hop < MaxRedirects && IsRedirect(response.StatusCode); hop++)
        {
            if (response.Headers.Location is not { } location)
            {
                return response;
            }

            Uri current = response.RequestMessage?.RequestUri ?? request.RequestUri!;
            Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);

            // 307/308 giữ nguyên phương thức và thân; gửi lại một POST tạo job là trả tiền hai lần.
            // Chỉ tự theo khi request là đọc, hoặc 301/302/303 (vốn đổi thành GET).
            bool keepsMethod = response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
            bool isRead = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

            if (keepsMethod && !isRead)
            {
                return response;
            }

            EnsureAllowed(allowlist, next);

            var follow = new HttpRequestMessage(isRead ? request.Method : HttpMethod.Get, next);

            if (string.Equals(current.Authority, next.Authority, StringComparison.OrdinalIgnoreCase)
                && current.Scheme == next.Scheme)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                {
                    follow.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            response.Dispose();
            response = await base.SendAsync(follow, cancellationToken);
        }

        return response;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private void EnsureAllowed(ProviderHostAllowlist allowlist, Uri uri)
    {
        if (allowlist.Explain(uri) is { } reason)
        {
            _logger.LogWarning("Chặn request tới {Authority}: {Reason}", uri.GetLeftPart(UriPartial.Authority), reason);

            throw new ProviderHostBlockedException(reason);
        }
    }

    private async Task<ProviderHostAllowlist> LoadAllowlistAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        string? raw = await scope.ServiceProvider
            .GetRequiredService<ISettingsStore>()
            .GetStringAsync(SettingKeys.ProviderHostAllowlist, cancellationToken);

        CacheEntry cache = _cache;

        if (string.Equals(raw, cache.Raw, StringComparison.Ordinal))
        {
            return cache.List;
        }

        ProviderHostAllowlist parsed = ProviderHostAllowlist.Parse(raw);

        foreach (string invalid in parsed.InvalidEntries)
        {
            _logger.LogWarning("Mục \"{Entry}\" trong setting {Key} không đọc được — bỏ qua.", invalid, SettingKeys.ProviderHostAllowlist);
        }

        _cache = new CacheEntry(raw, parsed);

        return parsed;
    }

    private sealed record CacheEntry(string? Raw, ProviderHostAllowlist List);
}
