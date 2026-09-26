using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdVideo.Core.Configuration;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using AdVideo.Core.Security;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Providers.Declarative;

/// <summary>Kết quả một vòng submit → poll → lấy kết quả của provider khai báo.</summary>
public sealed record DescriptorExecution
{
    public required bool IsSuccess { get; init; }

    public VideoFailureKind FailureKind { get; init; } = VideoFailureKind.None;

    public string? FailureReason { get; init; }

    /// <summary>Đã che key. Adapter gọi không cần che lại.</summary>
    public string? RawError { get; init; }

    public int? RetryAfterSeconds { get; init; }

    public string? ProviderRequestId { get; init; }

    /// <summary>Cây JSON chứa kết quả: phản hồi fetch, hoặc poll, hoặc submit — cái nào sau cùng.</summary>
    public JsonNode? ResultJson { get; init; }

    /// <summary>File nhị phân tải từ <c>result.contentPath</c>.</summary>
    public byte[]? ContentBytes { get; init; }

    /// <summary>URL mà poll <c>probeUrl</c> xác nhận đã tồn tại.</summary>
    public Uri? ProbedUrl { get; init; }

    /// <summary>Header của phản hồi submit — số ký tự bị tính tiền, request id.</summary>
    public IReadOnlyDictionary<string, string> SubmitHeaders { get; init; } = new Dictionary<string, string>();

    public static DescriptorExecution Fail(VideoFailureKind kind, string reason, string? rawError = null, int? retryAfter = null) =>
        new() { IsSuccess = false, FailureKind = kind, FailureReason = reason, RawError = rawError, RetryAfterSeconds = retryAfter };
}

/// <summary>
/// Chạy phần giao thức dây của một <see cref="ProviderDescriptor"/>: dựng request, gọi, poll có trần,
/// lấy kết quả, và quy lỗi.
/// </summary>
/// <remarks>
/// <para>
/// <b>Key chỉ gắn khi URL cùng origin với <c>transport.baseUrl</c></b> và gắn theo từng request —
/// URL lấy từ thân phản hồi trỏ sang host khác thì vẫn gọi được (nếu nằm trong allowlist) nhưng
/// không mang key. Allowlist do <see cref="SsrfGuardingHandler"/> trên HttpClient kiểm.
/// </para>
/// <para>
/// <b>Không retry ở đây.</b> Luật retry là luật về tiền, không phải đặc điểm provider; nó nằm ở
/// bước gọi (<c>ShotMaxRetries</c>) và Polly (chỉ GET/429). Lớp này chỉ phân loại cho đúng.
/// </para>
/// </remarks>
public sealed class DescriptorHttpExecutor
{
    private readonly HttpClient _http;
    private readonly ProviderDescriptor _descriptor;
    private readonly ResolvedCredential _credential;
    private readonly ICredentialStore _credentials;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Uri _baseUri;

    public DescriptorHttpExecutor(
        HttpClient http,
        ProviderDescriptor descriptor,
        ResolvedCredential credential,
        ICredentialStore credentials,
        ILogger logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _descriptor = descriptor;
        _credential = credential;
        _credentials = credentials;
        _logger = logger;
        _delay = delay ?? Task.Delay;
        _baseUri = new Uri(descriptor.Transport.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<DescriptorExecution> ExecuteAsync(TemplateContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await RunAsync(context, cancellationToken);
        }
        catch (ProviderHostBlockedException ex)
        {
            // Lỗi cấu hình, không phải lỗi tạm thời: thử lại lần nào cũng bị chặn.
            return DescriptorExecution.Fail(VideoFailureKind.ProviderUnavailable, $"{_descriptor.Name}: {ex.Message}");
        }
        catch (TemplateRenderException ex)
        {
            // Dữ liệu request không dựng được thành body theo descriptor (ví dụ tỉ lệ khung không có
            // trong valueMaps) — provider này không phục vụ được request này.
            return DescriptorExecution.Fail(VideoFailureKind.ProviderUnavailable, $"{_descriptor.Name}: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DescriptorExecution.Fail(
                VideoFailureKind.Transient,
                $"{_descriptor.Name} không trả lời trong {_descriptor.Transport.RequestTimeoutSeconds} giây.");
        }
        catch (HttpRequestException ex)
        {
            return DescriptorExecution.Fail(VideoFailureKind.Transient, $"Không gọi được {_descriptor.Name}: {ex.Message}", Redact(ex.Message));
        }
    }

    private async Task<DescriptorExecution> RunAsync(TemplateContext context, CancellationToken cancellationToken)
    {
        DescriptorSubmit submit = _descriptor.Submit;

        string? path = JsonTemplateRenderer.RenderString(submit.Path, context, asUrlPath: true);

        if (path is null)
        {
            return DescriptorExecution.Fail(VideoFailureKind.ProviderUnavailable, $"{_descriptor.Name}: thiếu biến để dựng submit.path.");
        }

        HttpContent? content = null;

        if (submit.Body is { Kind: DescriptorBodyKind.Json, Template: { } template })
        {
            JsonTemplateRenderer.TryRender(template, context, out JsonNode? body);
            content = new StringContent(body?.ToJsonString() ?? "{}", Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage submitResponse = await SendAsync(new HttpMethod(submit.Method), Resolve(path), context, content, cancellationToken);

        string submitRaw = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? submitJson = TryParse(submitRaw);

        if (!submit.SuccessStatus.Contains((int)submitResponse.StatusCode))
        {
            return await HttpFailureAsync(submitResponse, submitJson, submitRaw, "gửi yêu cầu", cancellationToken);
        }

        if (submitJson is null)
        {
            return DescriptorExecution.Fail(VideoFailureKind.Unknown, $"{_descriptor.Name} trả phản hồi không phải JSON khi gửi yêu cầu.", Redact(Truncate(submitRaw)));
        }

        foreach (DescriptorCondition condition in submit.FailWhen)
        {
            if (Matches(condition, submitJson))
            {
                return DescriptorExecution.Fail(
                    DescriptorErrorMapper.FromFailedBody(_descriptor.Errors, submitJson, submitRaw),
                    $"{_descriptor.Name} trả {(int)submitResponse.StatusCode} nhưng thân báo lỗi ({condition.Path}).",
                    Redact(Truncate(submitRaw)));
            }
        }

        Dictionary<string, string> headers = submitResponse.Headers
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);

        string? requestId = (_descriptor.Result.RequestIdPath is { } idPath ? JsonPathReader.ReadString(submitJson, idPath) : null)
            ?? (_descriptor.Result.RequestIdHeader is { } idHeader && headers.TryGetValue(idHeader, out string? fromHeader) ? fromHeader : null);

        TemplateContext afterSubmit = WithVariable(context, DescriptorVariables.ProviderRequestId, requestId);

        (DescriptorExecution? pollFailure, JsonNode? pollJson, Uri? probedUrl) =
            await PollAsync(submitJson, afterSubmit, cancellationToken);

        if (pollFailure is not null)
        {
            return pollFailure with { ProviderRequestId = requestId };
        }

        JsonNode? resultJson = pollJson ?? submitJson;

        if (_descriptor.Result.FetchUrlPath is { } fetchPath)
        {
            if (ReadUrl(submitJson, fetchPath) is not { } fetchUrl)
            {
                return DescriptorExecution.Fail(VideoFailureKind.Unknown, $"{_descriptor.Name}: không thấy URL kết quả tại {fetchPath}.", Redact(Truncate(submitRaw)));
            }

            using HttpResponseMessage fetched = await SendAsync(HttpMethod.Get, fetchUrl, afterSubmit, null, cancellationToken);
            string fetchedRaw = await fetched.Content.ReadAsStringAsync(cancellationToken);

            if (!fetched.IsSuccessStatusCode)
            {
                return (await HttpFailureAsync(fetched, TryParse(fetchedRaw), fetchedRaw, "lấy kết quả", cancellationToken)) with { ProviderRequestId = requestId };
            }

            resultJson = TryParse(fetchedRaw);
        }

        byte[]? contentBytes = null;

        if (_descriptor.Result.ContentPath is { } contentPath)
        {
            string? rendered = JsonTemplateRenderer.RenderString(contentPath, afterSubmit, asUrlPath: true);

            if (rendered is null)
            {
                return DescriptorExecution.Fail(VideoFailureKind.Unknown, $"{_descriptor.Name}: thiếu provider_request_id để tải kết quả.");
            }

            using HttpResponseMessage file = await SendAsync(HttpMethod.Get, Resolve(rendered), afterSubmit, null, cancellationToken);

            if (!file.IsSuccessStatusCode)
            {
                string fileRaw = await file.Content.ReadAsStringAsync(cancellationToken);

                return (await HttpFailureAsync(file, TryParse(fileRaw), fileRaw, "tải file kết quả", cancellationToken)) with { ProviderRequestId = requestId };
            }

            contentBytes = await file.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        return new DescriptorExecution
        {
            IsSuccess = true,
            ProviderRequestId = requestId,
            ResultJson = resultJson,
            ContentBytes = contentBytes,
            ProbedUrl = probedUrl,
            SubmitHeaders = headers,
        };
    }

    private async Task<(DescriptorExecution? Failure, JsonNode? Json, Uri? ProbedUrl)> PollAsync(
        JsonNode submitJson,
        TemplateContext context,
        CancellationToken cancellationToken)
    {
        DescriptorPoll poll = _descriptor.Poll ?? new DescriptorPoll();

        if (poll.Mode == DescriptorPollMode.None)
        {
            return (null, null, null);
        }

        Uri? url = poll.Mode == DescriptorPollMode.PathTemplate
            ? JsonTemplateRenderer.RenderString(poll.Path!, context, asUrlPath: true) is { } rendered ? Resolve(rendered) : null
            : ReadUrl(submitJson, poll.UrlPath!);

        if (url is null)
        {
            return (DescriptorExecution.Fail(VideoFailureKind.Unknown, $"{_descriptor.Name}: không dựng được URL hỏi trạng thái.", Redact(Truncate(submitJson.ToJsonString()))), null, null);
        }

        TimeSpan interval = TimeSpan.FromSeconds(poll.IntervalSeconds);

        // Trần tính bằng SỐ LẦN hỏi, không bằng đồng hồ: mỗi lần hỏi cách nhau đúng interval, và
        // cách đếm này cho cùng một kết quả trong test lẫn khi chạy thật.
        int maxPolls = Math.Max(1, poll.MaxWaitSeconds!.Value / poll.IntervalSeconds);

        for (int attempt = 0; attempt < maxPolls; attempt++)
        {
            if (attempt > 0)
            {
                await _delay(interval, cancellationToken);
            }

            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, url, context, null, cancellationToken);

            if (poll.Mode == DescriptorPollMode.ProbeUrl)
            {
                if (response.IsSuccessStatusCode)
                {
                    return (null, null, url);
                }

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Accepted)
                {
                    continue;
                }
            }

            string raw = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonNode? json = TryParse(raw);

            if (!response.IsSuccessStatusCode)
            {
                return (await HttpFailureAsync(response, json, raw, "hỏi trạng thái", cancellationToken), null, null);
            }

            string? status = JsonPathReader.ReadString(json, poll.StatusPath!);
            DescriptorPollState state = MapState(poll, status);

            if (state == DescriptorPollState.Succeeded)
            {
                return (null, json, null);
            }

            if (state == DescriptorPollState.Failed)
            {
                return (DescriptorExecution.Fail(
                    DescriptorErrorMapper.FromFailedBody(_descriptor.Errors, json, raw),
                    $"{_descriptor.Name} báo job thất bại (trạng thái \"{status}\").",
                    Redact(Truncate(raw))), null, null);
            }
        }

        // Hết trần mà provider vẫn đang render: KHÔNG retry — thử lại là trả tiền lần hai trong khi
        // lần một có thể vẫn đang chạy và vẫn tính tiền.
        return (DescriptorExecution.Fail(
            VideoFailureKind.ProviderUnavailable,
            $"{_descriptor.Name} chưa xong sau {poll.MaxWaitSeconds} giây (poll.maxWaitSeconds)."), null, null);
    }

    private DescriptorPollState MapState(DescriptorPoll poll, string? status)
    {
        if (status is not null && poll.StatusMap is { } map)
        {
            foreach ((string key, DescriptorPollState state) in map)
            {
                if (string.Equals(key, status, StringComparison.OrdinalIgnoreCase))
                {
                    return state;
                }
            }
        }

        // Trạng thái lạ coi là chưa xong: provider thêm một trạng thái trung gian mới thì job vẫn
        // chạy tiếp, còn nếu nó không bao giờ xong thì trần maxWaitSeconds chặn.
        _logger.LogDebug("{Provider}: trạng thái \"{Status}\" không có trong statusMap — coi là đang chờ.", _descriptor.Name, status);

        return DescriptorPollState.Pending;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri url,
        TemplateContext context,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        bool sameOrigin = ProviderHttp.IsSameOrigin(url, _baseUri);

        if (sameOrigin && _descriptor.Transport.Auth is { In: DescriptorAuthLocation.Query } queryAuth
            && RenderSecret(queryAuth.ValueRef, context) is { } queryValue)
        {
            var builder = new UriBuilder(url);
            string pair = $"{Uri.EscapeDataString(queryAuth.Name)}={Uri.EscapeDataString(queryValue)}";
            builder.Query = string.IsNullOrEmpty(builder.Query) ? pair : builder.Query.TrimStart('?') + "&" + pair;
            url = builder.Uri;
        }

        var request = new HttpRequestMessage(method, url) { Content = content };

        if (sameOrigin)
        {
            if (_descriptor.Transport.Auth is { In: DescriptorAuthLocation.Header } auth
                && RenderSecret(auth.ValueRef, context) is { } value)
            {
                request.Headers.TryAddWithoutValidation(
                    auth.Name,
                    string.IsNullOrWhiteSpace(auth.Scheme) ? value : $"{auth.Scheme} {value}");
            }

            foreach ((string name, string template) in _descriptor.Transport.Headers ?? [])
            {
                if (RenderSecret(template, context) is { } headerValue)
                {
                    request.Headers.TryAddWithoutValidation(name, headerValue);
                }
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_descriptor.Transport.RequestTimeoutSeconds));

        return await _http.SendAsync(request, timeout.Token);
    }

    private string? RenderSecret(string template, TemplateContext context) =>
        JsonTemplateRenderer.RenderString(
            template,
            WithVariable(context, DescriptorVariables.SecretApiKey, _credential.ApiKey));

    private async Task<DescriptorExecution> HttpFailureAsync(
        HttpResponseMessage response,
        JsonNode? json,
        string raw,
        string phase,
        CancellationToken cancellationToken)
    {
        VideoFailureKind kind = DescriptorErrorMapper.FromResponse(_descriptor.Errors, response.StatusCode, json, raw);

        if (DescriptorErrorMapper.ShouldDeactivate(_descriptor.Errors, response.StatusCode))
        {
            await _credentials.DeactivateAsync(
                _credential.Provider,
                $"{_descriptor.Name} trả {(int)response.StatusCode} khi {phase} (errors.deactivateCredentialOn).",
                cancellationToken);
        }

        return DescriptorExecution.Fail(
            kind,
            $"{_descriptor.Name} trả {(int)response.StatusCode} khi {phase}.",
            Redact(Truncate(raw)),
            ProviderFailureMapper.ReadRetryAfter(response));
    }

    private Uri Resolve(string relativePath) => new(_baseUri, relativePath.TrimStart('/'));

    private Uri? ReadUrl(JsonNode json, string path)
    {
        string? raw = JsonPathReader.ReadString(json, path);

        if (raw is null)
        {
            return null;
        }

        if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? absolute) && absolute.Scheme is "http" or "https")
        {
            return absolute;
        }

        return Uri.TryCreate(raw, UriKind.Relative, out _) && raw.StartsWith('/') ? new Uri(_baseUri, raw.TrimStart('/')) : null;
    }

    private static bool Matches(DescriptorCondition condition, JsonNode json)
    {
        JsonNode? value = JsonPathReader.Read(json, condition.Path);

        if (condition.Exists is { } exists)
        {
            return (value is not null) == exists;
        }

        if (value is null)
        {
            return false;
        }

        return condition.EqualTo is { } expected ? ValuesEqual(value, expected) : !ValuesEqual(value, condition.NotEqualTo!);
    }

    private static bool ValuesEqual(JsonNode a, JsonNode b)
    {
        if (JsonPathReader.AsDecimal(a) is { } x && JsonPathReader.AsDecimal(b) is { } y
            && a.GetValueKind() == JsonValueKind.Number && b.GetValueKind() == JsonValueKind.Number)
        {
            return x == y;
        }

        return JsonNode.DeepEquals(a, b);
    }

    private static TemplateContext WithVariable(TemplateContext context, string name, string? value)
    {
        var variables = new Dictionary<string, JsonNode?>(context.Variables, StringComparer.Ordinal)
        {
            [name] = value is null ? null : JsonValue.Create(value),
        };

        return context with { Variables = variables };
    }

    private static JsonNode? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? Redact(string? text) => SecretRedactor.RedactKnown(text, _credential.ApiKey);

    private static string Truncate(string value) => value.Length <= 4000 ? value : value[..4000];
}
