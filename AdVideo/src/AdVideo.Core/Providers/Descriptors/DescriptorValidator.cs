using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AdVideo.Core.Providers.Descriptors;

/// <summary>
/// Kiểm một descriptor trước khi cho vào DB.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mọi lỗi phải lộ ra lúc LƯU, không phải lúc GỌI.</b> Lúc gọi là lúc đang có job của khách và
/// có thể đã tốn tiền. Validator trả <b>toàn bộ</b> lỗi một lượt, để người vận hành sửa một lần
/// thay vì dán đi dán lại.
/// </para>
/// <para>Ba luật bảo mật (mục 2.4 của kế hoạch) được kiểm ở đây:</para>
/// <list type="number">
/// <item>Host hợp lệ KHÔNG khai trong descriptor — descriptor chỉ khai <c>baseUrl</c> https; việc
/// host đó có được gọi hay không do allowlist trong <c>SystemSetting</c> quyết định lúc chạy.</item>
/// <item><c>{{secret.*}}</c> chỉ hợp lệ trong <c>transport.auth</c> và <c>transport.headers</c>.</item>
/// <item>Chuỗi trông giống key thật (<c>sk-…</c>, hex ≥ 32, JWT…) → từ chối lưu.</item>
/// </list>
/// </remarks>
public static partial class DescriptorValidator
{
    private static readonly string[] AllowedMethods = ["POST", "PUT", "GET"];

    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Cookie", "Proxy-Authorization",
    };

    private static readonly HashSet<string> SecretLikeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_key", "apikey", "api-key", "x-api-key", "xi-api-key", "token", "access_token", "secret",
        "password", "authorization", "key",
    };

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{1,63}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^[!#$%&'*+.^_`|~0-9A-Za-z-]+$")]
    private static partial Regex HeaderNamePattern();

    [GeneratedRegex(@"^([1-5]xx|[1-5][0-9]{2})$")]
    private static partial Regex StatusKeyPattern();

    [GeneratedRegex(@"^(sk[-_][A-Za-z0-9_-]{16,}|xi-[A-Za-z0-9]{20,}|[0-9a-fA-F]{32,}|eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\..*)$")]
    private static partial Regex KnownKeyPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{32,}$")]
    private static partial Regex LongOpaqueToken();

    /// <summary>Kiểm descriptor đã parse. <paramref name="raw"/> là cây gốc, để quét secret và biến ở mọi chỗ.</summary>
    public static IReadOnlyList<string> Validate(ProviderDescriptor descriptor, JsonNode raw)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(raw);

        var errors = new List<string>();

        ValidateIdentity(descriptor, errors);
        ValidateTransport(descriptor.Transport, errors);

        HashSet<string> defaults = ValidateDefaults(descriptor, errors);

        ValidateValueMaps(descriptor, errors);
        ValidateConstraints(descriptor.Constraints, errors);
        ValidateSubmit(descriptor.Submit, errors);
        ValidatePoll(descriptor, errors);
        ValidateResult(descriptor, errors);
        ValidateErrors(descriptor.Errors, errors);
        ValidateCost(descriptor, defaults, errors);
        ValidateCapability(descriptor, errors);
        ValidateTemplates(descriptor, defaults, errors);
        ScanForSecrets(raw, "", errors);

        return errors;
    }

    private static void ValidateIdentity(ProviderDescriptor d, List<string> errors)
    {
        if (d.Schema != ProviderDescriptor.SchemaV1)
        {
            errors.Add($"schema phải là \"{ProviderDescriptor.SchemaV1}\", nhận \"{d.Schema}\".");
        }

        if (!NamePattern().IsMatch(d.Name ?? ""))
        {
            errors.Add("name chỉ gồm chữ thường, số, dấu chấm, gạch ngang, gạch dưới; dài 2–64 ký tự.");
        }
        else if (d.Name == ProviderNames.Fake)
        {
            errors.Add($"name \"{ProviderNames.Fake}\" dành cho provider giả dựng sẵn, không khai báo được.");
        }
    }

    private static void ValidateTransport(DescriptorTransport transport, List<string> errors)
    {
        if (!Uri.TryCreate(transport.BaseUrl, UriKind.Absolute, out Uri? baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("transport.baseUrl phải là URL tuyệt đối dùng https.");
        }
        else if (!string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            errors.Add("transport.baseUrl không được chứa thông tin đăng nhập, query hay fragment.");
        }

        if (transport.RequestTimeoutSeconds is < 5 or > 600)
        {
            errors.Add("transport.requestTimeoutSeconds phải trong khoảng 5–600.");
        }

        if (transport.Auth is { } auth)
        {
            if (auth.In == DescriptorAuthLocation.Header ? !HeaderNamePattern().IsMatch(auth.Name ?? "") : string.IsNullOrWhiteSpace(auth.Name))
            {
                errors.Add("transport.auth.name không hợp lệ.");
            }

            if (!(auth.ValueRef ?? "").Contains("{{" + DescriptorVariables.SecretApiKey + "}}", StringComparison.Ordinal))
            {
                errors.Add($"transport.auth.valueRef phải tham chiếu {{{{{DescriptorVariables.SecretApiKey}}}}}, không bao giờ chứa key thật.");
            }
        }

        foreach ((string name, string value) in transport.Headers ?? [])
        {
            if (!HeaderNamePattern().IsMatch(name))
            {
                errors.Add($"transport.headers: tên header \"{name}\" không hợp lệ.");
            }
            else if (string.IsNullOrEmpty(value))
            {
                errors.Add($"transport.headers.{name}: giá trị rỗng. Bỏ hẳn header nếu không cần.");
            }
            else if (ForbiddenHeaders.Contains(name))
            {
                errors.Add($"transport.headers: không được đặt header \"{name}\".");
            }
            else if (SecretLikeKeys.Contains(name)
                && !(value ?? "").Contains("{{" + DescriptorVariables.SecretApiKey + "}}", StringComparison.Ordinal))
            {
                errors.Add($"transport.headers.{name}: header mang secret phải tham chiếu {{{{{DescriptorVariables.SecretApiKey}}}}}, không chứa key thật.");
            }
        }
    }

    private static HashSet<string> ValidateDefaults(ProviderDescriptor d, List<string> errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string key, JsonNode? value) in d.Defaults ?? [])
        {
            if (!IdentifierPattern().IsMatch(key))
            {
                errors.Add($"defaults.{key}: tên biến chỉ gồm chữ thường, số, gạch dưới.");
            }
            else if (DescriptorVariables.Reserved.Contains(key))
            {
                errors.Add($"defaults.{key}: trùng tên biến dựng sẵn, sẽ che mất giá trị thật của request.");
            }
            else if (value is JsonObject)
            {
                errors.Add($"defaults.{key}: chỉ nhận giá trị vô hướng hoặc mảng.");
            }
            else
            {
                names.Add(key);
            }
        }

        return names;
    }

    private static void ValidateValueMaps(ProviderDescriptor d, List<string> errors)
    {
        foreach (string name in (d.ValueMaps ?? []).Keys)
        {
            if (!IdentifierPattern().IsMatch(name))
            {
                errors.Add($"valueMaps.{name}: tên bảng chỉ gồm chữ thường, số, gạch dưới.");
            }
        }
    }

    private static void ValidateConstraints(DescriptorConstraints? c, List<string> errors)
    {
        if (c?.MaxInputChars is <= 0)
        {
            errors.Add("constraints.maxInputChars phải > 0.");
        }

        if (c?.MaxReferenceImages is < 0)
        {
            errors.Add("constraints.maxReferenceImages không được âm.");
        }
    }

    private static void ValidateSubmit(DescriptorSubmit submit, List<string> errors)
    {
        if (!AllowedMethods.Contains(submit.Method, StringComparer.Ordinal))
        {
            errors.Add($"submit.method phải là một trong {string.Join("/", AllowedMethods)} (viết hoa).");
        }

        ValidateRelativePath(submit.Path, "submit.path", errors);

        if (submit.SuccessStatus.Count == 0 || submit.SuccessStatus.Any(s => s is < 200 or > 299))
        {
            errors.Add("submit.successStatus phải có ít nhất một mã và mọi mã nằm trong 200–299.");
        }

        DescriptorBody body = submit.Body ?? new DescriptorBody { Kind = DescriptorBodyKind.None };

        if (body.Kind == DescriptorBodyKind.Json && body.Template is null)
        {
            errors.Add("submit.body.template bắt buộc khi body.kind = json.");
        }

        if (submit.Method == "GET" && body.Kind == DescriptorBodyKind.Json)
        {
            errors.Add("submit.method = GET không gửi được body; đặt body.kind = none.");
        }

        for (int i = 0; i < submit.FailWhen.Count; i++)
        {
            DescriptorCondition condition = submit.FailWhen[i];

            ValidatePath(condition.Path, $"submit.failWhen[{i}].path", errors);

            int kinds = (condition.EqualTo is not null ? 1 : 0) + (condition.NotEqualTo is not null ? 1 : 0) + (condition.Exists is not null ? 1 : 0);

            if (kinds != 1)
            {
                errors.Add($"submit.failWhen[{i}]: khai đúng MỘT trong equals / notEquals / exists.");
            }
        }
    }

    private static void ValidatePoll(ProviderDescriptor d, List<string> errors)
    {
        DescriptorPoll poll = d.Poll ?? new DescriptorPoll();

        if (poll.Mode == DescriptorPollMode.None)
        {
            return;
        }

        if (poll.MaxWaitSeconds is not { } maxWait || maxWait is < 1 or > 3600)
        {
            errors.Add("poll.maxWaitSeconds BẮT BUỘC khi có poll, trong khoảng 1–3600. Vòng poll không trần là job treo vô hạn.");
        }

        if (poll.IntervalSeconds is < 1 or > 300)
        {
            errors.Add("poll.intervalSeconds phải trong khoảng 1–300.");
        }
        else if (poll.MaxWaitSeconds is { } wait && poll.IntervalSeconds > wait)
        {
            errors.Add("poll.intervalSeconds không được lớn hơn poll.maxWaitSeconds.");
        }

        switch (poll.Mode)
        {
            case DescriptorPollMode.PathTemplate:
                ValidateRelativePath(poll.Path, "poll.path", errors);

                if (d.Result.RequestIdPath is null)
                {
                    errors.Add("poll.mode = pathTemplate cần result.requestIdPath để có {{provider_request_id}}.");
                }

                break;

            case DescriptorPollMode.UrlFromSubmit or DescriptorPollMode.ProbeUrl:
                ValidatePath(poll.UrlPath, "poll.urlPath", errors);

                break;
        }

        if (poll.Mode == DescriptorPollMode.ProbeUrl)
        {
            return;
        }

        ValidatePath(poll.StatusPath, "poll.statusPath", errors);

        if (poll.StatusMap is not { Count: > 0 } map || !map.Values.Contains(DescriptorPollState.Succeeded))
        {
            errors.Add("poll.statusMap phải có ít nhất một trạng thái ánh xạ sang \"succeeded\".");
        }
    }

    private static void ValidateResult(ProviderDescriptor d, List<string> errors)
    {
        DescriptorResult r = d.Result;

        ValidateOptionalPath(r.RequestIdPath, "result.requestIdPath", errors);
        ValidateOptionalPath(r.FetchUrlPath, "result.fetchUrlPath", errors);
        ValidateOptionalPath(r.VideoUrlPath, "result.videoUrlPath", errors);
        ValidateOptionalPath(r.VideoBase64Path, "result.videoBase64Path", errors);
        ValidateOptionalPath(r.AudioBase64Path, "result.audioBase64Path", errors);
        ValidateOptionalPath(r.AudioUrlPath, "result.audioUrlPath", errors);
        ValidateOptionalPath(r.ReportedCostPath, "result.reportedCostPath", errors);

        if (r.ContentPath is not null)
        {
            ValidateRelativePath(r.ContentPath, "result.contentPath", errors);
        }

        if (r.BilledCharactersHeader is { } header && !HeaderNamePattern().IsMatch(header))
        {
            errors.Add("result.billedCharactersHeader không phải tên header hợp lệ.");
        }

        if (d.Kind == DescriptorKind.Video)
        {
            if (r.VideoUrlPath is null && r.VideoBase64Path is null && r.ContentPath is null)
            {
                errors.Add("Provider video cần một trong result.videoUrlPath / videoBase64Path / contentPath.");
            }

            if (r.AudioBase64Path is not null || r.AudioUrlPath is not null || r.Alignment is not null)
            {
                errors.Add("Provider video không khai được audio hay alignment trong result.");
            }
        }
        else
        {
            if (r.AudioBase64Path is null && r.AudioUrlPath is null)
            {
                errors.Add("Engine TTS cần result.audioBase64Path hoặc result.audioUrlPath.");
            }

            if (r.VideoUrlPath is not null || r.VideoBase64Path is not null || r.ContentPath is not null)
            {
                errors.Add("Engine TTS không khai được video trong result.");
            }
        }

        if (r.ContentPath is not null && r.RequestIdPath is null)
        {
            errors.Add("result.contentPath cần result.requestIdPath để có {{provider_request_id}}.");
        }

        if (r.Alignment is { } a)
        {
            ValidatePath(a.TextPath, "result.alignment.textPath", errors);
            ValidatePath(a.StartsPath, "result.alignment.startsPath", errors);
            ValidatePath(a.EndsPath, "result.alignment.endsPath", errors);
        }
    }

    private static void ValidateErrors(DescriptorErrors? e, List<string> errors)
    {
        if (e is null)
        {
            return;
        }

        if (e.ByBodyCode is { } byCode)
        {
            ValidatePath(byCode.Path, "errors.byBodyCode.path", errors);

            if (byCode.Table.Values.Contains(VideoFailureKind.None))
            {
                errors.Add("errors.byBodyCode.table không được ánh xạ sang None.");
            }
        }

        foreach ((string key, VideoFailureKind kind) in e.ByStatus ?? [])
        {
            if (!StatusKeyPattern().IsMatch(key))
            {
                errors.Add($"errors.byStatus: khoá \"{key}\" phải là mã HTTP 3 chữ số hoặc dạng \"4xx\".");
            }

            if (kind == VideoFailureKind.None)
            {
                errors.Add($"errors.byStatus.{key} không được ánh xạ sang None.");
            }
        }

        if (e.Default == VideoFailureKind.None)
        {
            errors.Add("errors.default không được là None.");
        }

        foreach (string key in e.DeactivateCredentialOn.Where(k => !StatusKeyPattern().IsMatch(k)))
        {
            errors.Add($"errors.deactivateCredentialOn: \"{key}\" phải là mã HTTP 3 chữ số hoặc dạng \"4xx\".");
        }
    }

    private static void ValidateCost(ProviderDescriptor d, HashSet<string> defaults, List<string> errors)
    {
        if (d.Cost is not { } cost)
        {
            return;
        }

        DescriptorCostUnit[] mainUnits = d.Kind == DescriptorKind.Video
            ? [DescriptorCostUnit.PerSecond, DescriptorCostUnit.PerRequest]
            : [DescriptorCostUnit.Per1000Chars, DescriptorCostUnit.PerRequest];

        if (!mainUnits.Contains(cost.Unit))
        {
            errors.Add($"cost.unit của provider {d.Kind.ToString().ToLowerInvariant()} phải là một trong {string.Join("/", mainUnits)}.");
        }

        if ((cost.RateUsd is null) == (cost.RateBy is null))
        {
            errors.Add("cost: khai đúng MỘT trong rateUsd và rateBy.");
        }

        if (cost.RateUsd is < 0)
        {
            errors.Add("cost.rateUsd không được âm.");
        }

        if (cost.RateBy is { } rateBy)
        {
            if (rateBy.Table.Count == 0 || rateBy.Table.Values.Any(v => v < 0))
            {
                errors.Add("cost.rateBy.table phải có ít nhất một dòng và không có giá âm.");
            }

            if (!DescriptorVariables.For(d.Kind).Contains(rateBy.Variable) && !defaults.Contains(rateBy.Variable))
            {
                errors.Add($"cost.rateBy.variable \"{rateBy.Variable}\" không phải biến đã biết.");
            }
        }

        foreach (DescriptorCostExtra extra in cost.Extras)
        {
            if (extra.Unit is not (DescriptorCostUnit.PerReferenceImage or DescriptorCostUnit.PerRequest))
            {
                errors.Add("cost.extras[].unit chỉ nhận perReferenceImage hoặc perRequest.");
            }

            if (extra.RateUsd < 0)
            {
                errors.Add("cost.extras[].rateUsd không được âm.");
            }
        }
    }

    private static void ValidateCapability(ProviderDescriptor d, List<string> errors)
    {
        if (d.Capability is null)
        {
            errors.Add("capability bắt buộc: không có nó thì registry bỏ qua provider và Luật 3 không chọn được.");

            return;
        }

        try
        {
            if (d.Kind == DescriptorKind.Video)
            {
                VideoProviderCapability caps = d.Capability.Deserialize<VideoProviderCapability>(ProviderDescriptorParser.JsonOptions)!;

                CheckCapabilityName(caps.Provider, d.Name, errors);

                if (caps.AllowedDurationSeconds.Count == 0 || caps.AllowedDurationSeconds.Any(s => s <= 0))
                {
                    errors.Add("capability.allowedDurationSeconds phải có ít nhất một giá trị > 0.");
                }
                else if (d.Cost is { } cost && cost.Unit is DescriptorCostUnit.PerSecond or DescriptorCostUnit.PerRequest)
                {
                    int maxImages = d.Constraints?.MaxReferenceImages ?? caps.MaxReferenceImages;
                    Dictionary<string, JsonNode?> fixedVariables = (d.Defaults ?? []).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                    decimal worst = DescriptorCostCalculator.WorstCasePerSecond(cost, caps.AllowedDurationSeconds.Min(), maxImages, fixedVariables);

                    if (caps.CostPerSecondUsd < worst)
                    {
                        errors.Add(
                            $"capability.costPerSecondUsd = {caps.CostPerSecondUsd} thấp hơn giá xấu nhất theo khối cost ({worst:0.####} USD/giây). " +
                            "Dự toán dựa trên capability — khai thấp là trần chi tiêu thấp hơn hoá đơn.");
                    }
                }
            }
            else
            {
                TtsProviderCapability caps = d.Capability.Deserialize<TtsProviderCapability>(ProviderDescriptorParser.JsonOptions)!;

                CheckCapabilityName(caps.Provider, d.Name, errors);

                if ((caps.HasWordTimings || caps.HasCharacterTimings) && d.Result.Alignment is null)
                {
                    errors.Add("capability khai có mốc thời gian nhưng result.alignment trống — bước 5 sẽ fail cứng.");
                }

                if (caps.HasCharacterTimings && d.Result.Alignment?.Format == DescriptorAlignmentFormat.Words)
                {
                    errors.Add("capability.hasCharacterTimings = true nhưng alignment.format = words: không suy được mốc ký tự từ mốc từ.");
                }

                if (d.Cost is { Unit: DescriptorCostUnit.Per1000Chars } cost && caps.CostPer1000CharsUsd < DescriptorCostCalculator.MaxRate(cost))
                {
                    errors.Add(
                        $"capability.costPer1000CharsUsd = {caps.CostPer1000CharsUsd} thấp hơn đơn giá cao nhất của khối cost ({DescriptorCostCalculator.MaxRate(cost)}).");
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"capability không đọc được thành manifest {d.Kind.ToString().ToLowerInvariant()}: {ex.Message}");
        }
    }

    private static void CheckCapabilityName(string provider, string name, List<string> errors)
    {
        if (!string.Equals(provider, name, StringComparison.Ordinal))
        {
            errors.Add($"capability.provider (\"{provider}\") phải trùng name (\"{name}\").");
        }
    }

    private static void ValidateTemplates(ProviderDescriptor d, HashSet<string> defaults, List<string> errors)
    {
        var requestScope = new HashSet<string>(DescriptorVariables.For(d.Kind), StringComparer.Ordinal);
        requestScope.UnionWith(defaults);

        var afterSubmitScope = new HashSet<string>(requestScope, StringComparer.Ordinal) { DescriptorVariables.ProviderRequestId };

        var transportScope = new HashSet<string>(requestScope, StringComparer.Ordinal) { DescriptorVariables.SecretApiKey };

        var maps = new HashSet<string>((d.ValueMaps ?? []).Keys, StringComparer.Ordinal);

        if (d.Transport.Auth is { } auth)
        {
            CheckString(auth.ValueRef, "transport.auth.valueRef", transportScope, maps, errors);
        }

        foreach ((string name, string value) in d.Transport.Headers ?? [])
        {
            CheckString(value, $"transport.headers.{name}", transportScope, maps, errors);
        }

        CheckString(d.Submit.Path, "submit.path", requestScope, maps, errors);

        if (d.Submit.Body?.Template is { } template)
        {
            CheckTemplateTree(template, "submit.body.template", requestScope, maps, errors);
        }

        if (d.Poll?.Path is { } pollPath)
        {
            CheckString(pollPath, "poll.path", afterSubmitScope, maps, errors);
        }

        if (d.Result.ContentPath is { } contentPath)
        {
            CheckString(contentPath, "result.contentPath", afterSubmitScope, maps, errors);
        }
    }

    private static void CheckTemplateTree(JsonNode? node, string where, HashSet<string> scope, HashSet<string> maps, List<string> errors)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue(JsonTemplateRenderer.WhenKey, out JsonNode? when))
                {
                    CheckSingleExpression(when, $"{where}.{JsonTemplateRenderer.WhenKey}", scope, maps, errors);
                }

                if (obj.TryGetPropertyValue(JsonTemplateRenderer.EachKey, out JsonNode? each))
                {
                    CheckSingleExpression(each, $"{where}.{JsonTemplateRenderer.EachKey}", scope, maps, errors);

                    var itemScope = new HashSet<string>(scope, StringComparer.Ordinal)
                    {
                        JsonTemplateRenderer.ItemVariable,
                        JsonTemplateRenderer.IndexVariable,
                    };

                    foreach ((string key, JsonNode? _) in obj.Where(p => p.Key is not (JsonTemplateRenderer.EachKey or JsonTemplateRenderer.ItemKey or JsonTemplateRenderer.WhenKey)))
                    {
                        errors.Add($"{where}.{key}: object có {JsonTemplateRenderer.EachKey} chỉ được có {JsonTemplateRenderer.ItemKey} (và {JsonTemplateRenderer.WhenKey}).");
                    }

                    if (obj.TryGetPropertyValue(JsonTemplateRenderer.ItemKey, out JsonNode? item))
                    {
                        CheckTemplateTree(item, $"{where}.{JsonTemplateRenderer.ItemKey}", itemScope, maps, errors);
                    }

                    return;
                }

                foreach ((string key, JsonNode? value) in obj)
                {
                    if (key == JsonTemplateRenderer.WhenKey)
                    {
                        continue;
                    }

                    if (key == JsonTemplateRenderer.ItemKey || (key.StartsWith('@') && key != JsonTemplateRenderer.WhenKey))
                    {
                        errors.Add($"{where}.{key}: khoá điều khiển không hợp lệ ở đây.");

                        continue;
                    }

                    CheckTemplateTree(value, $"{where}.{key}", scope, maps, errors);
                }

                break;

            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    CheckTemplateTree(array[i], $"{where}[{i}]", scope, maps, errors);
                }

                break;

            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                CheckString(value.GetValue<string>(), where, scope, maps, errors);

                break;
        }
    }

    private static void CheckSingleExpression(JsonNode? node, string where, HashSet<string> scope, HashSet<string> maps, List<string> errors)
    {
        string text = node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";
        IReadOnlyList<string> expressions = JsonTemplateRenderer.FindExpressions(text);

        if (expressions.Count != 1 || text.Trim() != $"{{{{{expressions[0]}}}}}")
        {
            errors.Add($"{where} phải là đúng một biểu thức dạng \"{{{{tên}}}}\".");

            return;
        }

        CheckString(text, where, scope, maps, errors);
    }

    private static void CheckString(string? text, string where, HashSet<string> scope, HashSet<string> maps, List<string> errors)
    {
        if (text is null)
        {
            return;
        }

        foreach (string raw in JsonTemplateRenderer.FindExpressions(text))
        {
            if (!JsonTemplateRenderer.TryParseExpression(raw, out TemplateExpression? expression, out string? error))
            {
                errors.Add($"{where}: {error}");

                continue;
            }

            string variable = expression!.Variable;

            if (variable.StartsWith(DescriptorVariables.SecretPrefix, StringComparison.Ordinal) && !scope.Contains(variable))
            {
                errors.Add(variable == DescriptorVariables.SecretApiKey
                    ? $"{where}: {{{{{variable}}}}} chỉ được dùng trong transport.auth và transport.headers."
                    : $"{where}: secret \"{variable}\" không tồn tại; chỉ có {DescriptorVariables.SecretApiKey}.");
            }
            else if (!scope.Contains(variable))
            {
                errors.Add($"{where}: biến \"{variable}\" không tồn tại ở đây.");
            }

            foreach (TemplateFilter filter in expression.Filters.Where(f => f.Name == "map" && !maps.Contains(f.Argument!)))
            {
                errors.Add($"{where}: không có bảng valueMaps.{filter.Argument}.");
            }
        }
    }

    private static void ScanForSecrets(JsonNode? node, string where, List<string> errors)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach ((string key, JsonNode? value) in obj)
                {
                    string path = where.Length == 0 ? key : $"{where}.{key}";

                    if (SecretLikeKeys.Contains(key)
                        && value is JsonValue v
                        && v.GetValueKind() == JsonValueKind.String
                        && StripExpressions(v.GetValue<string>()).Trim().Length > 0
                        && !IsInsideTransport(where))
                    {
                        errors.Add($"{path}: khoá tên \"{key}\" mang giá trị literal. Dùng {{{{{DescriptorVariables.SecretApiKey}}}}} thay cho key thật.");
                    }

                    ScanForSecrets(value, path, errors);
                }

                break;

            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    ScanForSecrets(array[i], $"{where}[{i}]", errors);
                }

                break;

            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                if (LooksLikeSecret(value.GetValue<string>()))
                {
                    errors.Add($"{where}: giá trị trông như một API key thật. Descriptor không được chứa secret — dùng {{{{{DescriptorVariables.SecretApiKey}}}}}.");
                }

                break;
        }
    }

    /// <summary>
    /// Chuỗi có chứa token trông như key thật không. Công khai để lớp ghi log dùng lại cùng một luật.
    /// </summary>
    public static bool LooksLikeSecret(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (string token in StripExpressions(text).Split([' ', '\t', '\n', '\r', ',', ';', '"', '\''], StringSplitOptions.RemoveEmptyEntries))
        {
            if (KnownKeyPattern().IsMatch(token))
            {
                return true;
            }

            if (LongOpaqueToken().IsMatch(token) && token.Any(char.IsDigit) && token.Any(char.IsLetter))
            {
                return true;
            }
        }

        return false;
    }

    /// <remarks>
    /// Auth và headers có luật riêng ở <see cref="ValidateTransport"/>: header mang secret phải tham
    /// chiếu <c>{{secret.api_key}}</c>, còn tiền tố literal như <c>Bearer</c> là hợp lệ.
    /// </remarks>
    private static bool IsInsideTransport(string where) =>
        where is "transport.auth" or "transport.headers"
        || where.StartsWith("transport.auth.", StringComparison.Ordinal)
        || where.StartsWith("transport.headers.", StringComparison.Ordinal);

    private static string StripExpressions(string text)
    {
        foreach (string raw in JsonTemplateRenderer.FindExpressions(text))
        {
            text = text.Replace("{{" + raw + "}}", " ", StringComparison.Ordinal);
        }

        return text;
    }

    private static void ValidateRelativePath(string? path, string where, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal))
        {
            errors.Add($"{where} phải là đường dẫn tương đối bắt đầu bằng \"/\" — host chỉ đến từ transport.baseUrl.");
        }
    }

    private static void ValidatePath(string? path, string where, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"{where} bắt buộc.");
        }
        else if (!JsonPathReader.TryParse(path, out string? error))
        {
            errors.Add($"{where}: {error}");
        }
    }

    private static void ValidateOptionalPath(string? path, string where, List<string> errors)
    {
        if (path is not null)
        {
            ValidatePath(path, where, errors);
        }
    }
}
