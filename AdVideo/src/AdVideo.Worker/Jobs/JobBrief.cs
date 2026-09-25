using System.Text.Json;

namespace AdVideo.Worker.Jobs;

/// <summary>
/// Brief của khách, đọc từ cột <c>AdVideoJob.BriefJson</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao không dùng lại DTO của API.</b> Worker không tham chiếu <c>AdVideo.Api</c> — chiều
/// phụ thuộc đó buộc mỗi lần sửa một bước pipeline phải deploy lại API. Quan trọng hơn: brief
/// được lưu NGUYÊN VĂN làm bằng chứng khách đã gửi gì (R3), nên một job nằm trong hàng đợi từ
/// trước có thể mang hình dạng JSON của phiên bản cũ. Đọc rời từng trường và bỏ qua trường lạ là
/// cách để một job cũ không làm chết worker sau khi API đổi DTO.
/// </para>
/// <para>
/// <b>Mọi trường đều có giá trị mặc định an toàn.</b> API đã kiểm dữ liệu trước khi tạo job, nên
/// ở đây thiếu trường nghĩa là brief cũ hoặc bị sửa tay trong DB — và trong cả hai trường hợp,
/// job phải fail với lý do đọc được ở bước 1 chứ không phải ném exception ở bước 6.
/// </para>
/// </remarks>
public sealed record JobBrief
{
    /// <summary>Khoá của brief trong <c>PipelineContext.Bag</c>.</summary>
    public const string BagKey = "brief";

    public string Prompt { get; init; } = string.Empty;
    public string? ProductName { get; init; }
    public string Script { get; init; } = string.Empty;
    public IReadOnlyList<string> ProductImages { get; init; } = [];
    public IReadOnlyList<string> SceneReferences { get; init; } = [];
    public string? TalentImageUrl { get; init; }

    /// <summary>
    /// Khách có muốn giữ tiếng động gốc không. <c>"full"</c> được đọc như <c>"sfx_only"</c>.
    /// </summary>
    /// <remarks>
    /// Giữ nguyên thoại của model trong khi đã có voice-over là hai lớp tiếng chồng nhau — D3
    /// chọn tắt thoại, giữ tiếng động. Quyết định cuối cùng do
    /// <see cref="AdVideo.Core.Providers.NativeSoundTranslator"/> đưa ra, vì nó còn phụ thuộc
    /// provider có tách được hai thứ đó không.
    /// </remarks>
    public bool WantsSoundEffects { get; init; }

    public string? VoiceProfileId { get; init; }
    public double VoiceSpeed { get; init; } = 1.0;
    public string? CallbackUrl { get; init; }

    /// <summary>Đọc brief. Không bao giờ ném — JSON hỏng trả về brief rỗng để bước 1 fail có lý do.</summary>
    public static JobBrief Parse(string? briefJson)
    {
        if (string.IsNullOrWhiteSpace(briefJson))
        {
            return new JobBrief();
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(briefJson);
            JsonElement root = doc.RootElement;

            JsonElement brief = Child(root, "brief");
            JsonElement assets = Child(root, "assets");
            JsonElement voice = Child(root, "voice");
            JsonElement audio = Child(root, "audio");

            return new JobBrief
            {
                Prompt = Text(brief, "prompt") ?? string.Empty,
                ProductName = Text(brief, "product_name"),
                Script = Text(voice, "script") ?? string.Empty,
                ProductImages = Urls(assets, "product_images"),
                SceneReferences = Urls(assets, "scene_reference"),
                TalentImageUrl = Text(Child(assets, "talent"), "image_url"),
                WantsSoundEffects = Text(audio, "native_sound") is { } native
                    && native.Trim().ToLowerInvariant() is "sfx_only" or "full",
                VoiceProfileId = Text(voice, "voice_profile_id"),
                VoiceSpeed = Number(voice, "speed") ?? 1.0,
                CallbackUrl = Text(root, "callback_url"),
            };
        }
        catch (JsonException)
        {
            return new JobBrief();
        }
    }

    /// <summary>Những gì thiếu để chạy được pipeline. Rỗng nghĩa là brief dùng được.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Script))
        {
            problems.Add("brief không có lời thoại (voice.script) — sprint này chưa có bước LLM tự viết lời.");
        }

        if (ProductImages.Count == 0)
        {
            problems.Add("brief không có ảnh sản phẩm (assets.product_images).");
        }

        if (string.IsNullOrWhiteSpace(Prompt))
        {
            problems.Add("brief không có mô tả (brief.prompt).");
        }

        return problems;
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement child)
            ? child
            : default;

    private static string? Text(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static double? Number(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
            ? number
            : null;

    private static IReadOnlyList<string> Urls(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var urls = new List<string>(array.GetArrayLength());

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } url && !string.IsNullOrWhiteSpace(url))
            {
                urls.Add(url.Trim());
            }
        }

        return urls;
    }
}
