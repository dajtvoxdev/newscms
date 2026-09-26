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

        return errors.Count == 0
            ? new DescriptorParseResult(descriptor, raw, errors)
            : new DescriptorParseResult(null, raw, errors);
    }

    private static DescriptorParseResult Fail(string error) => new(null, null, [error]);
}
