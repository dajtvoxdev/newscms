using System.Text.Json;

namespace NewsCMS.Application.Builder;

/// <summary>
/// Sinh JSON Schema cho props của một khối động từ <see cref="BlockDescriptor.Props"/>.
///
/// Dùng cho MCP <c>site_list_blocks</c>: agent chỉ nhìn thấy tên khối thì phải đoán props, đoán sai
/// là khối render rỗng mà không có lỗi nào báo. Có schema thì agent biết đúng tên prop, kiểu, và
/// tập giá trị hợp lệ của select.
/// </summary>
public static class BlockPropsSchema
{
    public static string? Build(BlockDescriptor descriptor)
    {
        if (descriptor.Props.Count == 0) return null;

        var properties = new Dictionary<string, object>(descriptor.Props.Count);
        foreach (var prop in descriptor.Props)
            properties[prop.Name] = Describe(prop);

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["title"] = descriptor.Label,
            ["description"] = descriptor.Description,
            ["additionalProperties"] = false,
            ["properties"] = properties
        };

        return JsonSerializer.Serialize(schema);
    }

    private static Dictionary<string, object> Describe(BlockPropDescriptor prop)
    {
        var node = new Dictionary<string, object> { ["title"] = prop.Label };

        switch (prop.Type)
        {
            case BlockPropTypes.Number:
                node["type"] = "integer";
                if (prop.Min is { } min) node["minimum"] = min;
                if (prop.Max is { } max) node["maximum"] = max;
                break;

            case BlockPropTypes.Checkbox:
                node["type"] = "boolean";
                break;

            case BlockPropTypes.MultiSelect:
                node["type"] = "array";
                node["items"] = ItemSchema(prop);
                break;

            case BlockPropTypes.ItemPicker:
                node["type"] = "array";
                node["items"] = new Dictionary<string, object> { ["type"] = "string", ["format"] = "uuid" };
                break;

            default:
                node["type"] = "string";
                break;
        }

        // Options ĐỘNG (chuyên mục, thẻ, thư mục…) cố tình không thành enum: danh sách thay đổi theo
        // dữ liệu site, khoá cứng vào schema sẽ sai ngay lần thêm chuyên mục kế tiếp.
        if (prop.Options is { Count: > 0 } && prop.Type is BlockPropTypes.Select)
            node["enum"] = prop.Options.Select(o => o.Value).ToArray();

        var hint = Hint(prop);
        if (hint is not null) node["description"] = hint;

        if (prop.DefaultValue is not null) node["default"] = prop.DefaultValue;

        return node;
    }

    private static Dictionary<string, object> ItemSchema(BlockPropDescriptor prop)
    {
        var items = new Dictionary<string, object> { ["type"] = "string" };
        if (prop.Options is { Count: > 0 })
            items["enum"] = prop.Options.Select(o => o.Value).ToArray();
        return items;
    }

    /// <summary>
    /// Ghép Hint của khối với gợi ý nguồn dữ liệu, để agent biết giá trị hợp lệ lấy ở đâu khi
    /// options không thể khoá cứng vào schema.
    /// </summary>
    private static string? Hint(BlockPropDescriptor prop)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(prop.Hint)) parts.Add(prop.Hint!);

        var source = prop.OptionsSource switch
        {
            BlockOptionSources.PostCategories => "Giá trị = slug chuyên mục bài viết.",
            BlockOptionSources.ProductCategories => "Giá trị = slug chuyên mục sản phẩm.",
            BlockOptionSources.BannerPositions => "Giá trị = Position của banner.",
            BlockOptionSources.Tags => "Giá trị = slug thẻ.",
            BlockOptionSources.MediaFolders => "Giá trị = Id thư mục media (GUID).",
            _ => null
        } ?? prop.ItemSource switch
        {
            BlockItemSources.Post => "Mảng Id bài viết (GUID), giữ đúng thứ tự đã chọn.",
            BlockItemSources.Product => "Mảng Id sản phẩm (GUID), giữ đúng thứ tự đã chọn.",
            BlockItemSources.Media => "Mảng Id ảnh (GUID), giữ đúng thứ tự đã chọn.",
            _ => null
        };
        if (source is not null) parts.Add(source);

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
