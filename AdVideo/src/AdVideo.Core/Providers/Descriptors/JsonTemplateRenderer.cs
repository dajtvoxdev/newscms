using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AdVideo.Core.Providers.Descriptors;

/// <summary>Một biểu thức <c>{{biến|lọc|lọc:tham_số}}</c> đã tách.</summary>
public sealed record TemplateExpression(string Variable, IReadOnlyList<TemplateFilter> Filters, string Raw);

/// <summary>Một bộ lọc trong biểu thức. <see cref="Argument"/> chỉ có ở <c>map:tên</c>.</summary>
public sealed record TemplateFilter(string Name, string? Argument);

/// <summary>Biến và bảng đổi giá trị cho một lần dựng.</summary>
public sealed record TemplateContext(
    IReadOnlyDictionary<string, JsonNode?> Variables,
    IReadOnlyDictionary<string, Dictionary<string, string>>? ValueMaps = null);

/// <summary>Lỗi lúc dựng: biến lạ, giá trị không có trong bảng map, ép kiểu không được.</summary>
public sealed class TemplateRenderException(string message) : Exception(message);

/// <summary>
/// Dựng thân request (và đường dẫn, header) từ mẫu trong descriptor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thay biến trên cây JSON đã parse, không nối chuỗi rồi parse lại.</b> Một prompt chứa
/// <c>"</c> hay <c>}</c> không phá được cấu trúc body — đây là chống template injection bằng
/// <b>cấu trúc</b>, không bằng bộ lọc ký tự.
/// </para>
/// <para>Quy tắc:</para>
/// <list type="bullet">
/// <item>Chuỗi chỉ gồm đúng một <c>{{biến}}</c> giữ <b>kiểu tự nhiên</b>: <c>"{{seconds}}"</c> → <c>6</c>.
/// Ép kiểu bằng hậu tố: <c>|string |int |bool |not |urlencode |map:tên</c>.</item>
/// <item>Biến null hoặc rỗng thì <b>cặp khoá–giá trị biến mất</b>, không gửi <c>null</c>. Chuỗi
/// ghép (<c>"Bearer {{x}}"</c>) mà một biến rỗng thì cả chuỗi biến mất — gửi <c>"Bearer "</c> là
/// một lỗi 401 khó đọc.</item>
/// <item><c>{"@when": "{{image_url}}", ...}</c>: nhánh chỉ có mặt khi biểu thức cho giá trị "thật"
/// (khác null, khác rỗng, khác <c>false</c>, khác mảng rỗng).</item>
/// <item><c>{"@each": "{{image_urls}}", "@item": {...}}</c>: dựng mảng; trong <c>@item</c> có thêm
/// biến <c>item</c> và <c>index</c>.</item>
/// <item>Mảng mẫu mà mọi phần tử đều biến mất thì cả khoá biến mất (<c>["{{id}}"]</c> với id rỗng).</item>
/// <item>Giá trị literal trong mẫu (kể cả <c>null</c> và <c>[]</c>) giữ nguyên.</item>
/// </list>
/// </remarks>
public static partial class JsonTemplateRenderer
{
    public const string WhenKey = "@when";
    public const string EachKey = "@each";
    public const string ItemKey = "@item";
    public const string ItemVariable = "item";
    public const string IndexVariable = "index";

    /// <summary>Tên các bộ lọc được hỗ trợ.</summary>
    public static readonly IReadOnlySet<string> KnownFilters =
        new HashSet<string>(StringComparer.Ordinal) { "string", "int", "bool", "not", "urlencode", "map" };

    [GeneratedRegex(@"\{\{(.*?)\}\}", RegexOptions.Singleline)]
    private static partial Regex ExpressionPattern();

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*(\.[a-z0-9_]+)?$")]
    private static partial Regex VariablePattern();

    /// <summary>Mọi biểu thức trong một chuỗi, theo thứ tự xuất hiện.</summary>
    public static IReadOnlyList<string> FindExpressions(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ExpressionPattern().Matches(text).Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>Tách một biểu thức (phần nằm giữa <c>{{</c> và <c>}}</c>).</summary>
    public static bool TryParseExpression(string raw, out TemplateExpression? expression, out string? error)
    {
        ArgumentNullException.ThrowIfNull(raw);

        expression = null;
        error = null;

        string[] parts = raw.Split('|', StringSplitOptions.TrimEntries);
        string variable = parts[0];

        if (!VariablePattern().IsMatch(variable))
        {
            error = $"Tên biến \"{variable}\" không hợp lệ trong {{{{{raw}}}}}. Chỉ dùng chữ thường, số, gạch dưới.";

            return false;
        }

        var filters = new List<TemplateFilter>();

        foreach (string part in parts.Skip(1))
        {
            int colon = part.IndexOf(':', StringComparison.Ordinal);
            string name = colon < 0 ? part : part[..colon].Trim();
            string? argument = colon < 0 ? null : part[(colon + 1)..].Trim();

            if (!KnownFilters.Contains(name))
            {
                error = $"Bộ lọc \"{name}\" không tồn tại trong {{{{{raw}}}}}. Có: {string.Join(", ", KnownFilters)}.";

                return false;
            }

            if ((name == "map") != !string.IsNullOrEmpty(argument))
            {
                error = name == "map"
                    ? $"Bộ lọc map cần tên bảng, ví dụ |map:aspect, trong {{{{{raw}}}}}."
                    : $"Bộ lọc \"{name}\" không nhận tham số trong {{{{{raw}}}}}.";

                return false;
            }

            filters.Add(new TemplateFilter(name, argument));
        }

        expression = new TemplateExpression(variable, filters, raw);

        return true;
    }

    /// <summary>
    /// Dựng một cây mẫu. Trả <c>false</c> khi cả gốc biến mất (ví dụ mẫu là <c>"{{x}}"</c> và x rỗng).
    /// </summary>
    public static bool TryRender(JsonNode? template, TemplateContext context, out JsonNode? result)
    {
        ArgumentNullException.ThrowIfNull(context);

        return TryRenderNode(template, context.Variables, context, out result);
    }

    /// <summary>
    /// Dựng một chuỗi phẳng (đường dẫn, giá trị header). Null nếu có biến rỗng.
    /// </summary>
    /// <param name="template">Chuỗi mẫu.</param>
    /// <param name="context">Biến.</param>
    /// <param name="asUrlPath">
    /// True cho đường dẫn URL: giá trị biến được mã hoá TỪNG ĐOẠN (giữ <c>/</c> để model id kiểu
    /// <c>fal-ai/kling-video/v3</c> dùng được), và đoạn <c>.</c>/<c>..</c> bị từ chối — để một id
    /// do provider trả về kiểu <c>../admin?x=</c> không đổi được đích của request. Đừng dùng
    /// <c>|urlencode</c> trong đường dẫn: giá trị sẽ bị mã hoá hai lần.
    /// </param>
    public static string? RenderString(string template, TemplateContext context, bool asUrlPath = false)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        bool missing = false;

        string rendered = ExpressionPattern().Replace(template, match =>
        {
            JsonNode? value = Evaluate(match.Groups[1].Value, context.Variables, context);
            string? text = ToText(value);

            if (string.IsNullOrEmpty(text))
            {
                missing = true;

                return string.Empty;
            }

            return asUrlPath ? EncodePathValue(text, match.Value) : text;
        });

        return missing ? null : rendered;
    }

    private static string EncodePathValue(string value, string expression)
    {
        string[] segments = value.Split('/');

        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new TemplateRenderException(
                $"Giá trị của {expression} chứa đoạn \".\" hoặc \"..\" — từ chối dựng đường dẫn để không thoát khỏi baseUrl.");
        }

        return string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    /// <summary>Giá trị "thật" theo nghĩa của <c>@when</c> và bộ lọc <c>bool</c>.</summary>
    public static bool IsTruthy(JsonNode? value) => value switch
    {
        null => false,
        JsonArray array => array.Count > 0,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.False => false,
            JsonValueKind.String => v.GetValue<string>().Length > 0,
            _ => true,
        },

        // JsonObject: có mặt là "thật", kể cả object rỗng.
        _ => true,
    };

    private static bool TryRenderNode(
        JsonNode? node,
        IReadOnlyDictionary<string, JsonNode?> variables,
        TemplateContext context,
        out JsonNode? result)
    {
        switch (node)
        {
            case JsonObject obj:
                return TryRenderObject(obj, variables, context, out result);

            case JsonArray array:
                var rendered = new JsonArray();

                foreach (JsonNode? item in array)
                {
                    if (TryRenderNode(item, variables, context, out JsonNode? child))
                    {
                        rendered.Add(child);
                    }
                }

                // Mảng mẫu có phần tử mà dựng xong rỗng hết (["{{id}}"] với id trống) thì cả khoá
                // biến mất, như một biến rỗng. Mảng rỗng literal ([]) thì giữ nguyên.
                result = rendered;

                return array.Count == 0 || rendered.Count > 0;

            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                return TryRenderString(value.GetValue<string>(), variables, context, out result);

            default:
                result = node?.DeepClone();

                return true;
        }
    }

    private static bool TryRenderObject(
        JsonObject obj,
        IReadOnlyDictionary<string, JsonNode?> variables,
        TemplateContext context,
        out JsonNode? result)
    {
        result = null;

        if (obj.TryGetPropertyValue(WhenKey, out JsonNode? condition)
            && !IsTruthy(EvaluateCondition(condition, variables, context)))
        {
            return false;
        }

        if (obj.TryGetPropertyValue(EachKey, out JsonNode? source))
        {
            return TryRenderEach(obj, source, variables, context, out result);
        }

        var rendered = new JsonObject();

        foreach (KeyValuePair<string, JsonNode?> property in obj)
        {
            if (property.Key == WhenKey)
            {
                continue;
            }

            if (TryRenderNode(property.Value, variables, context, out JsonNode? child))
            {
                rendered[property.Key] = child;
            }
        }

        result = rendered;

        return true;
    }

    private static bool TryRenderEach(
        JsonObject obj,
        JsonNode? source,
        IReadOnlyDictionary<string, JsonNode?> variables,
        TemplateContext context,
        out JsonNode? result)
    {
        result = null;

        if (EvaluateCondition(source, variables, context) is not JsonArray items || items.Count == 0)
        {
            return false;
        }

        obj.TryGetPropertyValue(ItemKey, out JsonNode? itemTemplate);

        var rendered = new JsonArray();

        for (int i = 0; i < items.Count; i++)
        {
            var scoped = new Dictionary<string, JsonNode?>(variables.Count + 2, StringComparer.Ordinal);

            foreach (KeyValuePair<string, JsonNode?> pair in variables)
            {
                scoped[pair.Key] = pair.Value;
            }

            scoped[ItemVariable] = items[i];
            scoped[IndexVariable] = JsonValue.Create(i);

            if (itemTemplate is null)
            {
                rendered.Add(items[i]?.DeepClone());
            }
            else if (TryRenderNode(itemTemplate, scoped, context, out JsonNode? child))
            {
                rendered.Add(child);
            }
        }

        result = rendered;

        return true;
    }

    private static JsonNode? EvaluateCondition(
        JsonNode? condition,
        IReadOnlyDictionary<string, JsonNode?> variables,
        TemplateContext context)
    {
        if (condition is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            string text = value.GetValue<string>();
            IReadOnlyList<string> expressions = FindExpressions(text);

            if (expressions.Count == 1 && text.Trim() == $"{{{{{expressions[0]}}}}}")
            {
                return Evaluate(expressions[0], variables, context);
            }
        }

        throw new TemplateRenderException(
            $"{WhenKey}/{EachKey} phải là đúng một biểu thức dạng \"{{{{tên}}}}\", nhận được: {condition?.ToJsonString() ?? "null"}.");
    }

    private static bool TryRenderString(
        string text,
        IReadOnlyDictionary<string, JsonNode?> variables,
        TemplateContext context,
        out JsonNode? result)
    {
        IReadOnlyList<string> expressions = FindExpressions(text);

        if (expressions.Count == 0)
        {
            result = JsonValue.Create(text);

            return true;
        }

        // Đúng một biểu thức chiếm trọn chuỗi: giữ kiểu tự nhiên của giá trị.
        if (expressions.Count == 1 && text == $"{{{{{expressions[0]}}}}}")
        {
            JsonNode? value = Evaluate(expressions[0], variables, context);

            if (value is null || (value is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Length == 0))
            {
                result = null;

                return false;
            }

            result = value.DeepClone();

            return true;
        }

        var builder = new StringBuilder();
        int last = 0;

        foreach (Match match in ExpressionPattern().Matches(text))
        {
            builder.Append(text, last, match.Index - last);

            string? part = ToText(Evaluate(match.Groups[1].Value, variables, context));

            if (string.IsNullOrEmpty(part))
            {
                result = null;

                return false;
            }

            builder.Append(part);
            last = match.Index + match.Length;
        }

        builder.Append(text, last, text.Length - last);
        result = JsonValue.Create(builder.ToString());

        return true;
    }

    private static JsonNode? Evaluate(
        string raw,
        IReadOnlyDictionary<string, JsonNode?> variables,
        TemplateContext context)
    {
        if (!TryParseExpression(raw, out TemplateExpression? expression, out string? error))
        {
            throw new TemplateRenderException(error!);
        }

        if (!variables.TryGetValue(expression!.Variable, out JsonNode? value))
        {
            throw new TemplateRenderException($"Biến \"{expression.Variable}\" không được cung cấp.");
        }

        foreach (TemplateFilter filter in expression.Filters)
        {
            value = ApplyFilter(filter, value, context, raw);
        }

        return value;
    }

    private static JsonNode? ApplyFilter(TemplateFilter filter, JsonNode? value, TemplateContext context, string raw)
    {
        switch (filter.Name)
        {
            case "bool":
                return JsonValue.Create(IsTruthy(value));

            case "not":
                return JsonValue.Create(!IsTruthy(value));
        }

        if (value is null)
        {
            return null;
        }

        switch (filter.Name)
        {
            case "string":
                return JsonValue.Create(ToText(value) ?? value.ToJsonString());

            case "urlencode":
                return JsonValue.Create(Uri.EscapeDataString(ToText(value) ?? value.ToJsonString()));

            case "int":
                if (JsonPathReader.AsDecimal(value) is { } number && number == decimal.Truncate(number))
                {
                    return JsonValue.Create((long)number);
                }

                if (value is JsonValue b && b.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                {
                    return JsonValue.Create(b.GetValueKind() == JsonValueKind.True ? 1L : 0L);
                }

                throw new TemplateRenderException($"Không ép được {value.ToJsonString()} thành số nguyên trong {{{{{raw}}}}}.");

            default: // map
                string key = ToText(value) ?? value.ToJsonString();

                if (context.ValueMaps is null || !context.ValueMaps.TryGetValue(filter.Argument!, out Dictionary<string, string>? map))
                {
                    throw new TemplateRenderException($"Không có bảng valueMaps.{filter.Argument} cho {{{{{raw}}}}}.");
                }

                return map.TryGetValue(key, out string? mapped)
                    ? JsonValue.Create(mapped)
                    : throw new TemplateRenderException(
                        $"Giá trị \"{key}\" không có trong valueMaps.{filter.Argument} ({{{{{raw}}}}}). Bổ sung bảng map trong descriptor.");
        }
    }

    private static string? ToText(JsonNode? value) =>
        value switch
        {
            null => null,
            JsonValue v => JsonPathReader.AsString(v),
            _ => value.ToJsonString(),
        };

    /// <summary>Đổi một giá trị CLR thường gặp sang <see cref="JsonNode"/> để làm biến.</summary>
    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        decimal d => JsonValue.Create(d),
        double d => JsonValue.Create(d),
        Guid g => JsonValue.Create(g.ToString("D", CultureInfo.InvariantCulture)),
        IEnumerable<string> list => new JsonArray(list.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
        _ => throw new ArgumentException($"Kiểu {value.GetType().Name} chưa được hỗ trợ làm biến mẫu.", nameof(value)),
    };
}
