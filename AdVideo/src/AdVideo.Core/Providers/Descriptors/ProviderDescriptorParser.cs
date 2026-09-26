using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AdVideo.Core.Providers.Descriptors;

/// <summary>Kết quả đọc một descriptor. <see cref="Descriptor"/> chỉ khác null khi không có lỗi nào.</summary>
public sealed record DescriptorParseResult(
    ProviderDescriptor? Descriptor,
    JsonObject? Raw,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Descriptor is not null && Errors.Count == 0;

    /// <summary>Khối capability dạng chuỗi, để ghi vào <c>ProviderCredential.CapabilityJson</c>.</summary>
    public string? CapabilityJson => Descriptor?.Capability?.ToJsonString(ProviderDescriptorParser.JsonOptions);
}

/// <summary>
/// Đọc chuỗi JSON (chấp nhận comment kiểu jsonc và dấu phẩy thừa) thành <see cref="ProviderDescriptor"/> rồi kiểm.
/// </summary>
public static class ProviderDescriptorParser
{
    /// <summary>Trần độ dài. Descriptor dài hơn thế gần như chắc chắn đang cố làm việc của code C#.</summary>
    public const int MaxLength = 64 * 1024;

    /// <summary>
    /// Cùng quy ước với <c>AdVideoJson.Options</c> của Infrastructure (camelCase, enum dạng chuỗi),
    /// để capability đọc ở đây và đọc từ <c>CapabilityJson</c> ra cùng một kết quả.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 32,
    };

    public static DescriptorParseResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail("Descriptor rỗng.");
        }

        if (json.Length > MaxLength)
        {
            return Fail($"Descriptor dài {json.Length} ký tự, vượt trần {MaxLength}.");
        }

        JsonObject raw;

        try
        {
            if (JsonNode.Parse(json, documentOptions: DocumentOptions) is not JsonObject obj)
            {
                return Fail("Descriptor phải là một object JSON.");
            }

            raw = obj;
        }
        catch (JsonException ex)
        {
            return Fail($"JSON không hợp lệ: {ex.Message}");
        }

        ProviderDescriptor? descriptor;

        try
        {
            descriptor = raw.Deserialize<ProviderDescriptor>(JsonOptions);
        }
        catch (JsonException ex)
        {
            return new DescriptorParseResult(null, raw, [$"Sai hình dạng descriptor: {ex.Message}"]);
        }

        // "required" của C# chỉ đòi khoá có mặt; "transport": null vẫn lọt. Chặn ở đây để validator
        // không phải phòng thủ null ở mọi dòng.
        if (descriptor is null || descriptor.Transport is null || descriptor.Submit is null || descriptor.Result is null)
        {
            return new DescriptorParseResult(null, raw, ["transport, submit và result là bắt buộc và không được null."]);
        }

        IReadOnlyList<string> errors = DescriptorValidator.Validate(descriptor, raw);

        if (errors.Count > 0)
        {
            return new DescriptorParseResult(null, raw, errors);
        }

        Materialize(descriptor.Defaults);
        Materialize(descriptor.Capability);
        Materialize(descriptor.Submit.Body?.Template);

        foreach (DescriptorCondition condition in descriptor.Submit.FailWhen)
        {
            Materialize(condition.EqualTo);
            Materialize(condition.NotEqualTo);
        }

        return new DescriptorParseResult(descriptor, raw, errors);
    }

    /// <summary>
    /// Duyệt hết cây một lần để <see cref="JsonObject"/>/<see cref="JsonArray"/> dựng xong dữ liệu nội bộ.
    /// </summary>
    /// <remarks>
    /// Chúng khởi tạo LƯỜI ở lần đọc đầu tiên, và bước khởi tạo đó không an toàn đa luồng. Descriptor
    /// đã parse nằm trong cache dùng chung giữa nhiều job chạy song song, nên phải khởi tạo xong
    /// trước khi vào cache — sau đó chỉ còn đọc.
    /// </remarks>
    private static void Materialize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    Materialize(property.Value);
                }

                break;

            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Materialize(item);
                }

                break;
        }
    }

    private static DescriptorParseResult Fail(string error) => new(null, null, [error]);
}
