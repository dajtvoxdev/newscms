using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>
/// Model + key đã giải mã của kết nối — chỗ duy nhất trong Xưởng ảnh chạm vào key. Runner và nút
/// "Chạy thử" đều đi qua đây, nên luật (model phải bật, kết nối phải có key, URL phải hợp lệ) chỉ viết một lần.
/// </summary>
public sealed class ImageModelCredentials
{
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<ImageModelCredentials> _logger;

    public ImageModelCredentials(AppDbContext db, IDataProtectionProvider dataProtection, ILogger<ImageModelCredentials> logger)
    {
        _db = db;
        // Cùng purpose với AiConnectionService — key được mã hoá ở màn hình Kết nối AI.
        _protector = dataProtection.CreateProtector("NewsCMS.Ai.ApiKey");
        _logger = logger;
    }

    public async Task<Result<(ImageModel Model, ImageProviderContext Context)>> ResolveAsync(Guid modelId, bool requireActive, CancellationToken ct = default)
    {
        ImageModel? model = await _db.ImageModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modelId, ct);

        if (model is null)
        {
            return Fail("Model tạo ảnh không còn tồn tại.");
        }

        if (requireActive && !model.IsActive)
        {
            return Fail($"Model \"{model.Name}\" đang tắt.");
        }

        // Fake không gọi mạng nên không cần kết nối.
        if (model.Adapter == ImageProviderAdapter.Fake)
        {
            return Result<(ImageModel, ImageProviderContext)>.Success((model, ToContext(model, "fake://local", string.Empty)));
        }

        var connection = await _db.AiConnections.AsNoTracking()
            .Where(c => c.Id == model.ConnectionId && !c.IsDeleted)
            .Select(c => new { c.Name, c.BaseUrl, c.ApiKeyEncrypted, c.IsActive })
            .FirstOrDefaultAsync(ct);

        if (connection is null)
        {
            return Fail($"Kết nối AI của model \"{model.Name}\" không còn tồn tại.");
        }

        if (!connection.IsActive)
        {
            return Fail($"Kết nối AI \"{connection.Name}\" đang tắt.");
        }

        if (string.IsNullOrEmpty(connection.ApiKeyEncrypted))
        {
            return Fail($"Kết nối AI \"{connection.Name}\" chưa có API key.");
        }

        if (!ImageStudioRules.IsAllowedBaseUrl(connection.BaseUrl, out string? urlError))
        {
            return Fail(urlError!);
        }

        string apiKey;

        try
        {
            apiKey = _protector.Unprotect(connection.ApiKeyEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không giải mã được API key của kết nối cho model ảnh {ModelId}.", model.Id);
            return Fail($"Không giải mã được API key của kết nối \"{connection.Name}\". Nhập lại key ở trang Kết nối AI.");
        }

        return Result<(ImageModel, ImageProviderContext)>.Success((model, ToContext(model, connection.BaseUrl, apiKey)));
    }

    private static ImageProviderContext ToContext(ImageModel model, string baseUrl, string apiKey) =>
        new(baseUrl, apiKey, model.ModelId, model.Quality, model.OutputFormat, model.TimeoutSeconds, model.ExtraParamsJson);

    private static Result<(ImageModel, ImageProviderContext)> Fail(string error) =>
        Result<(ImageModel, ImageProviderContext)>.Failure(error);
}
