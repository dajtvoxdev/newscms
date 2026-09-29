namespace NewsCMS.Domain.Entities.ImageStudio;

/// <summary>Cách nói chuyện với nhà cung cấp — mỗi giá trị là một adapter C#.</summary>
public enum ImageProviderAdapter
{
    /// <summary><c>POST {base}/images/generations</c> và <c>/images/edits</c>: OpenAI, 9Router và gateway tương thích.</summary>
    OpenAiImages = 0,

    /// <summary>Gemini <c>generateContent</c> trả ảnh (chưa có adapter — Đợt 3).</summary>
    Gemini = 1,

    /// <summary>fal.ai queue API (chưa có adapter — Đợt 3).</summary>
    FalQueue = 2,

    /// <summary>Vẽ ảnh giả bằng ImageSharp, không gọi mạng. Chỉ chạy khi <c>ImageStudio:EnableFakeProvider = true</c>.</summary>
    Fake = 99,
}

/// <summary>Model làm được gì. Người dùng chỉ thấy chế độ mà model đang chọn hỗ trợ.</summary>
[Flags]
public enum ImageCapabilities
{
    None = 0,
    TextToImage = 1,
    ReferenceImages = 2,
    MaskEdit = 4,
    InstructionEdit = 8,
}

/// <summary>Quy ước mask của provider — hai nhà cung cấp lớn làm ngược nhau.</summary>
public enum MaskConvention
{
    None = 0,

    /// <summary>PNG RGBA, alpha = 0 ở vùng được sửa (OpenAI).</summary>
    AlphaZeroIsEdit = 1,

    /// <summary>Ảnh xám, trắng ở vùng được sửa (fal fill/inpaint).</summary>
    WhiteIsEdit = 2,
}

public enum ImageJobMode
{
    /// <summary>Tạo mới từ mô tả.</summary>
    Generate = 0,

    /// <summary>Tạo mới có ảnh tham chiếu.</summary>
    Reference = 1,

    /// <summary>Sửa ảnh có sẵn theo vùng đánh số.</summary>
    RegionEdit = 2,
}

/// <summary>Ảnh dùng vào đâu — chọn mẫu và khung mặc định theo đây.</summary>
public enum ImagePurpose
{
    Free = 0,
    PostCover = 1,
    PostInline = 2,
    ProductMain = 3,
    ProductGallery = 4,
    Banner = 5,
    Social = 6,
}

public enum ImageJobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Canceled = 4,
}

public enum RegionStrategy
{
    /// <summary>Một lần gọi cho mọi vùng.</summary>
    Single = 0,

    /// <summary>Mỗi vùng một lần gọi, nối tiếp nhau — chính xác hơn, tốn N lượt.</summary>
    Sequential = 1,
}
