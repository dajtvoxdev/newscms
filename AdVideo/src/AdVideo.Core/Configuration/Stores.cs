using AdVideo.Core.Entities;
using AdVideo.Core.Providers;

namespace AdVideo.Core.Configuration;

/// <summary>
/// Đọc <see cref="SystemSetting"/> có cache. Cài đặt thật ở Infrastructure.
/// </summary>
/// <remarks>
/// Mọi hàm đều nhận <c>fallback</c>: setting thiếu trong DB thì hệ thống vẫn chạy được với giá
/// trị an toàn, thay vì ném exception giữa một job đang render. Nhưng fallback phải là giá trị
/// BẢO TOÀN (ví dụ <c>MaxConcurrentShots</c> fallback = 1), không phải giá trị tiện.
/// </remarks>
public interface ISettingsStore
{
    Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default);
    Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken cancellationToken = default);
    Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default);
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Ghi setting và làm hỏng cache. Đổi setting không được đòi restart.</summary>
    Task SetAsync(string key, string value, SettingValueType valueType, string description,
        bool isProvisional = false, CancellationToken cancellationToken = default);

    /// <summary>Mọi setting đang là TẠM (chưa kiểm chứng). Sprint 0 chạy xong thì danh sách này phải rỗng dần.</summary>
    Task<IReadOnlyList<SystemSetting>> GetProvisionalAsync(CancellationToken cancellationToken = default);

    /// <summary>Làm hỏng cache, ví dụ sau khi seed hoặc sau khi người vận hành sửa DB bằng tay.</summary>
    void Invalidate();
}

/// <summary>Một credential đã giải mã, sẵn sàng dùng. Chỉ tồn tại trong bộ nhớ, không bao giờ log.</summary>
public sealed record ResolvedCredential(
    string Provider,
    string ModelId,
    ProviderCategory Category,
    string Endpoint,
    string ApiKey,
    CredentialScope Scope,
    Guid? TenantId,
    int Priority)
{
    /// <summary>Để hiện trong log/UI mà không lộ secret.</summary>
    public string MaskedKey => ApiKey.Length <= 4
        ? "****"
        : $"****{ApiKey[^4..]}";

    /// <summary>
    /// ToString bị chặn có chủ đích.
    /// </summary>
    /// <remarks>
    /// Một record C# có ToString mặc định in ra mọi thuộc tính — nghĩa là
    /// <c>logger.LogInformation("{Credential}", credential)</c> sẽ ghi API key vào log.
    /// Chặn ở đây rẻ hơn nhiều so với rà mọi chỗ log.
    /// </remarks>
    public override string ToString() => $"{Provider}/{ModelId} key={MaskedKey}";
}

/// <summary>Lấy credential + capability từ DB. Thay thế hoàn toàn việc đọc key từ appsettings/env.</summary>
public interface ICredentialStore
{
    /// <summary>Credential cho một provider cụ thể. Ưu tiên scope Tenant rồi tới System.</summary>
    Task<ResolvedCredential?> GetAsync(string provider, ProviderCategory category,
        Guid? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>Mọi credential video đang bật, xếp theo <see cref="ResolvedCredential.Priority"/>.</summary>
    Task<IReadOnlyList<ResolvedCredential>> ListActiveAsync(ProviderCategory category,
        Guid? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>Capability video của một provider. Null nếu không có credential nào đang bật.</summary>
    Task<VideoProviderCapability?> GetVideoCapabilityAsync(string provider, CancellationToken cancellationToken = default);

    Task<TtsProviderCapability?> GetTtsCapabilityAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>Mọi capability video đang bật — đầu vào cho <see cref="ProviderCapabilityValidator"/> để gợi ý phương án thay thế.</summary>
    Task<IReadOnlyList<VideoProviderCapability>> GetAllVideoCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Nạp credential lần đầu. Mã hoá key trước khi ghi.</summary>
    Task UpsertAsync(ProviderCredential credential, string plainApiKey, CancellationToken cancellationToken = default);

    void Invalidate();
}

/// <summary>Lấy prompt active theo code (D7).</summary>
public interface IPromptStore
{
    /// <summary>Prompt active của một code. Null nếu chưa seed.</summary>
    Task<PromptTemplate?> GetActiveAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Nội dung prompt active. Trả <paramref name="fallback"/> nếu không có — để pipeline không chết vì thiếu seed.</summary>
    Task<string> GetActiveContentAsync(string code, string fallback, CancellationToken cancellationToken = default);

    /// <summary>Mọi prompt active theo loại.</summary>
    Task<IReadOnlyList<PromptTemplate>> ListActiveAsync(PromptKind kind, CancellationToken cancellationToken = default);

    /// <summary>Thêm version mới và đặt active. Version cũ giữ lại để rollback và để tái hiện video cũ.</summary>
    Task<PromptTemplate> AddVersionAsync(string code, PromptKind kind, string content,
        string changeNote, string? formatCode = null, CancellationToken cancellationToken = default);

    /// <summary>Rollback về một version cũ bằng cách đảo cờ active.</summary>
    Task ActivateVersionAsync(string code, int version, CancellationToken cancellationToken = default);

    void Invalidate();
}
