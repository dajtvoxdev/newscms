using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AdVideo.Core.Providers.Descriptors;

/// <summary>
/// Mô tả khai báo một provider — thêm nhà cung cấp bằng cách dán JSON vào DB thay vì viết adapter C#.
/// </summary>
/// <remarks>
/// <para>
/// <b>Câu kiểm định biên giới:</b> descriptor chỉ phục vụ provider có hình dạng
/// <i>submit → (poll) → đọc JSON</i>; xác thực bằng header tĩnh; kết quả là URL hoặc base64 nằm
/// trong cùng một cây JSON. Mọi thứ khác (zip, OAuth2, ký HMAC, WebSocket, multipart) viết C#.
/// Giữ câu này thì schema không phình ra thành một ngôn ngữ lập trình trong cột DB.
/// </para>
/// <para>
/// <b>Descriptor cố tình KHÔNG khai được:</b> luật retry và circuit breaker (luật về tiền, không
/// phải đặc điểm provider), và việc chọn provider (Luật 3) — descriptor không bao giờ được tự khai
/// "luôn chọn tôi".
/// </para>
/// <para>
/// Record này chỉ là hình dạng. Kiểm hợp lệ ở <see cref="DescriptorValidator"/>; đọc từ chuỗi ở
/// <see cref="ProviderDescriptorParser"/>.
/// </para>
/// </remarks>
public sealed record ProviderDescriptor
{
    /// <summary>Giá trị duy nhất được chấp nhận của <see cref="Schema"/>.</summary>
    public const string SchemaV1 = "advideo.provider/v1";

    public required string Schema { get; init; }

    /// <summary>Tên provider, bằng đúng <c>ProviderCredential.Provider</c>. Một descriptor = một tên.</summary>
    public required string Name { get; init; }

    public required DescriptorKind Kind { get; init; }

    public required DescriptorTransport Transport { get; init; }

    /// <summary>Biến bổ sung mà <see cref="VideoRequest"/>/<see cref="TtsRequest"/> không có, ví dụ <c>resolution</c>.</summary>
    public JsonObject? Defaults { get; init; }

    public DescriptorConstraints? Constraints { get; init; }

    /// <summary>Bảng đổi giá trị cho bộ lọc <c>|map:tên</c>, ví dụ <c>Portrait9x16 → "9:16"</c>.</summary>
    public Dictionary<string, Dictionary<string, string>>? ValueMaps { get; init; }

    /// <summary>
    /// Nguyên văn <see cref="VideoProviderCapability"/> hoặc <see cref="TtsProviderCapability"/>.
    /// </summary>
    /// <remarks>
    /// Loader ghi khối này thẳng vào <c>ProviderCredential.CapabilityJson</c>, nên
    /// <see cref="ProviderCapabilityValidator"/> và Luật 3 không cần biết descriptor tồn tại.
    /// </remarks>
    public JsonObject? Capability { get; init; }

    public required DescriptorSubmit Submit { get; init; }

    public DescriptorPoll? Poll { get; init; }

    public required DescriptorResult Result { get; init; }

    public DescriptorErrors? Errors { get; init; }

    public DescriptorCost? Cost { get; init; }
}

public enum DescriptorKind
{
    Video = 0,
    Tts = 1,
}

public sealed record DescriptorTransport
{
    /// <summary>Gốc URL. Bắt buộc https, và host phải nằm trong allowlist ở <c>SystemSetting</c> — không phải trong descriptor.</summary>
    public required string BaseUrl { get; init; }

    public DescriptorAuth? Auth { get; init; }

    /// <summary>Header tĩnh. Giá trị chỉ được tham chiếu <c>{{secret.*}}</c>, không được chứa key thật.</summary>
    public Dictionary<string, string>? Headers { get; init; }

    public int RequestTimeoutSeconds { get; init; } = 120;
}

public sealed record DescriptorAuth
{
    public DescriptorAuthLocation In { get; init; } = DescriptorAuthLocation.Header;

    /// <summary>Tên header (hoặc tham số query), ví dụ <c>Authorization</c>, <c>xi-api-key</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Tiền tố, ví dụ <c>Bearer</c> hoặc <c>Key</c>. Bỏ trống = không tiền tố.</summary>
    public string? Scheme { get; init; }

    /// <summary>THAM CHIẾU tới secret, ví dụ <c>{{secret.api_key}}</c>. Không bao giờ là key thật.</summary>
    public required string ValueRef { get; init; }
}

public enum DescriptorAuthLocation
{
    Header = 0,
    Query = 1,
}

public sealed record DescriptorConstraints
{
    /// <summary>Số ký tự tối đa của văn bản gửi đi (prompt hoặc lời thoại). Vượt thì từ chối trước khi gọi.</summary>
    public int? MaxInputChars { get; init; }

    /// <summary>Số ảnh tham chiếu tối đa gửi đi. Dư thì cắt bớt.</summary>
    public int? MaxReferenceImages { get; init; }
}

public sealed record DescriptorSubmit
{
    public string Method { get; init; } = "POST";

    /// <summary>Đường dẫn nối sau <see cref="DescriptorTransport.BaseUrl"/>. Biến trong đường dẫn được urlencode tự động.</summary>
    public required string Path { get; init; }

    public DescriptorBody? Body { get; init; }

    public IReadOnlyList<int> SuccessStatus { get; init; } = [200, 201, 202];

    /// <summary>Điều kiện coi phản hồi 2xx là LỖI — nhiều cổng trả 200 kèm thân báo lỗi.</summary>
    public IReadOnlyList<DescriptorCondition> FailWhen { get; init; } = [];
}

public sealed record DescriptorBody
{
    public DescriptorBodyKind Kind { get; init; } = DescriptorBodyKind.Json;

    /// <summary>Cây JSON mẫu. Thay biến trên cây đã parse, không nối chuỗi — xem <see cref="JsonTemplateRenderer"/>.</summary>
    public JsonNode? Template { get; init; }
}

public enum DescriptorBodyKind
{
    Json = 0,
    None = 1,
}

/// <summary>Một điều kiện trên phản hồi JSON. Đường dẫn không tồn tại thì điều kiện KHÔNG khớp.</summary>
public sealed record DescriptorCondition
{
    public required string Path { get; init; }

    [JsonPropertyName("equals")]
    public JsonNode? EqualTo { get; init; }

    [JsonPropertyName("notEquals")]
    public JsonNode? NotEqualTo { get; init; }

    /// <summary>True: khớp khi giá trị tồn tại và khác null.</summary>
    public bool? Exists { get; init; }
}

public sealed record DescriptorPoll
{
    public DescriptorPollMode Mode { get; init; } = DescriptorPollMode.None;

    /// <summary>Đường dẫn hỏi trạng thái, cho <see cref="DescriptorPollMode.PathTemplate"/>.</summary>
    public string? Path { get; init; }

    /// <summary>Đường dẫn trong phản hồi submit chứa URL hỏi trạng thái, cho <see cref="DescriptorPollMode.UrlFromSubmit"/>/<see cref="DescriptorPollMode.ProbeUrl"/>.</summary>
    public string? UrlPath { get; init; }

    public int IntervalSeconds { get; init; } = 5;

    /// <summary>Trần thời gian chờ. BẮT BUỘC khi có poll — vòng poll không trần là job treo vô hạn.</summary>
    public int? MaxWaitSeconds { get; init; }

    public string? StatusPath { get; init; }

    public Dictionary<string, DescriptorPollState>? StatusMap { get; init; }
}

public enum DescriptorPollMode
{
    /// <summary>Đồng bộ: phản hồi submit chính là kết quả.</summary>
    None = 0,

    /// <summary>Hỏi <c>baseUrl + path</c>, trong path có <c>{{provider_request_id}}</c>.</summary>
    PathTemplate = 1,

    /// <summary>Hỏi URL mà phản hồi submit trả về (fal.ai: <c>status_url</c>).</summary>
    UrlFromSubmit = 2,

    /// <summary>URL trong phản hồi submit chính là file kết quả; hỏi tới khi nó tồn tại.</summary>
    ProbeUrl = 3,
}

public enum DescriptorPollState
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

public sealed record DescriptorResult
{
    /// <summary>Đường dẫn tới id phía provider trong phản hồi submit.</summary>
    public string? RequestIdPath { get; init; }

    /// <summary>
    /// Đường dẫn trong phản hồi submit chứa URL phải GET để lấy kết quả sau khi poll xong (fal.ai: <c>response_url</c>).
    /// </summary>
    public string? FetchUrlPath { get; init; }

    /// <summary>Đường dẫn (mẫu, nối sau baseUrl) trả thẳng file nhị phân kết quả, ví dụ <c>/videos/{{provider_request_id}}/content</c>.</summary>
    public string? ContentPath { get; init; }

    public string? VideoUrlPath { get; init; }

    public string? VideoBase64Path { get; init; }

    public string? AudioBase64Path { get; init; }

    public string? AudioUrlPath { get; init; }

    public string AudioContentType { get; init; } = "audio/mpeg";

    public DescriptorAlignment? Alignment { get; init; }

    /// <summary>Đường dẫn tới GIÁ mà provider tự báo. Chỉ khai khi provider thật sự báo — xem P0.5.</summary>
    public string? ReportedCostPath { get; init; }

    /// <summary>Header chứa số ký tự bị tính tiền (TTS), ví dụ <c>xi-character-count</c>.</summary>
    public string? BilledCharactersHeader { get; init; }

    /// <summary>
    /// Header chứa id phía provider, khi id không nằm trong thân (ElevenLabs: <c>request-id</c> —
    /// thứ duy nhất cho phép nối ngữ điệu ở lần gọi sau).
    /// </summary>
    public string? RequestIdHeader { get; init; }
}

public sealed record DescriptorAlignment
{
    public DescriptorAlignmentFormat Format { get; init; } = DescriptorAlignmentFormat.Characters;

    /// <summary>Mảng ký tự (hoặc từ) song song với hai mảng mốc.</summary>
    public required string TextPath { get; init; }

    public required string StartsPath { get; init; }

    public required string EndsPath { get; init; }

    public DescriptorTimeUnit TimeUnit { get; init; } = DescriptorTimeUnit.Seconds;
}

public enum DescriptorAlignmentFormat
{
    Characters = 0,
    Words = 1,
}

public enum DescriptorTimeUnit
{
    Seconds = 0,
    Milliseconds = 1,
}

public sealed record DescriptorErrors
{
    /// <summary>Mã lỗi máy đọc được trong thân → loại lỗi. Thắng <see cref="ByStatus"/>.</summary>
    public DescriptorBodyCodeMap? ByBodyCode { get; init; }

    /// <summary>Khoá là mã HTTP (<c>"402"</c>) hoặc cả lớp (<c>"4xx"</c>, <c>"5xx"</c>). Mã cụ thể thắng lớp.</summary>
    public Dictionary<string, VideoFailureKind>? ByStatus { get; init; }

    public VideoFailureKind Default { get; init; } = VideoFailureKind.Unknown;

    /// <summary>Mã HTTP khiến credential bị tắt ngay (ví dụ <c>402</c> hết tiền). Cùng định dạng khoá với <see cref="ByStatus"/>.</summary>
    public IReadOnlyList<string> DeactivateCredentialOn { get; init; } = [];
}

public sealed record DescriptorBodyCodeMap
{
    public required string Path { get; init; }

    public required Dictionary<string, VideoFailureKind> Table { get; init; }
}

public sealed record DescriptorCost
{
    public required DescriptorCostUnit Unit { get; init; }

    /// <summary>Đơn giá cố định. Khai đúng MỘT trong <see cref="RateUsd"/> và <see cref="RateBy"/>.</summary>
    public decimal? RateUsd { get; init; }

    public DescriptorRateTable? RateBy { get; init; }

    public IReadOnlyList<DescriptorCostExtra> Extras { get; init; } = [];
}

public sealed record DescriptorRateTable
{
    /// <summary>Tên biến quyết định đơn giá, ví dụ <c>resolution</c>.</summary>
    public required string Variable { get; init; }

    public required Dictionary<string, decimal> Table { get; init; }
}

public sealed record DescriptorCostExtra
{
    public required DescriptorCostUnit Unit { get; init; }

    public required decimal RateUsd { get; init; }
}

public enum DescriptorCostUnit
{
    PerSecond = 0,
    PerRequest = 1,
    Per1000Chars = 2,
    PerReferenceImage = 3,
}
