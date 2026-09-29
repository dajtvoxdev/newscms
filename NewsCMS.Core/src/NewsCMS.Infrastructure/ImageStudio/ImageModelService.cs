using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio.Imaging;
using NewsCMS.Infrastructure.ImageStudio.Jobs;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>Cấu hình model tạo ảnh (SuperAdmin). Dùng chung mọi site.</summary>
public sealed class ImageModelService : IImageModelService
{
    public const string TestPrompt = "Ảnh thử kết nối: một quả táo đỏ trên bàn gỗ sáng màu, ánh sáng tự nhiên, nền đơn giản.";
    public const string TestFolder = "ai-images/tests";

    private readonly AppDbContext _db;
    private readonly IImageProviderRegistry _registry;
    private readonly ImageModelCredentials _credentials;
    private readonly IFileStorage _storage;

    public ImageModelService(AppDbContext db, IImageProviderRegistry registry, ImageModelCredentials credentials, IFileStorage storage)
    {
        _db = db;
        _registry = registry;
        _credentials = credentials;
        _storage = storage;
    }

    public async Task<IReadOnlyList<ImageModelDto>> GetAllAsync(CancellationToken ct = default)
    {
        List<ImageModel> models = await _db.ImageModels.AsNoTracking()
            .OrderByDescending(m => m.IsDefault).ThenBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToListAsync(ct);

        Dictionary<Guid, string> connections = await ConnectionNamesAsync(ct);

        return models.Select(m => ToDto(m, connections)).ToList();
    }

    public async Task<ImageModelDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ImageModel? model = await _db.ImageModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

        return model is null ? null : ToDto(model, await ConnectionNamesAsync(ct));
    }

    public async Task<IReadOnlyList<ImageConnectionOptionDto>> GetConnectionsAsync(CancellationToken ct = default) =>
        await _db.AiConnections.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Name)
            .Select(c => new ImageConnectionOptionDto(c.Id, c.Name, c.BaseUrl, c.ApiKeyEncrypted != null && c.ApiKeyEncrypted != ""))
            .ToListAsync(ct);

    public IReadOnlyList<ImageProviderAdapter> GetAvailableAdapters() => _registry.Available;

    public async Task<Result<Guid>> CreateAsync(ImageModelUpsertDto dto, CancellationToken ct = default)
    {
        string? error = await ValidateAsync(dto, ct);

        if (error is not null)
        {
            return Result<Guid>.Failure(error);
        }

        var model = new ImageModel();
        Apply(model, dto);

        if (model.IsDefault)
        {
            await ClearDefaultAsync(null, ct);
        }

        _db.ImageModels.Add(model);
        await _db.SaveChangesAsync(ct);

        return Result<Guid>.Success(model.Id);
    }

    public async Task<Result> UpdateAsync(ImageModelUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.Id is not { } id)
        {
            return Result.Failure("Thiếu mã model.");
        }

        ImageModel? model = await _db.ImageModels.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (model is null)
        {
            return Result.Failure("Không tìm thấy model.");
        }

        string? error = await ValidateAsync(dto, ct);

        if (error is not null)
        {
            return Result.Failure(error);
        }

        Apply(model, dto);

        if (model.IsDefault)
        {
            await ClearDefaultAsync(model.Id, ct);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ImageModel? model = await _db.ImageModels.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (model is null)
        {
            return Result.Failure("Không tìm thấy model.");
        }

        // Xoá mềm: job cũ vẫn đọc được tên model (ImageJob.ModelName) và sổ chi phí vẫn trỏ về được.
        model.IsDeleted = true;
        model.DeletedAt = DateTime.UtcNow;
        model.IsDefault = false;
        model.IsActive = false;
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<ImageModelTestResultDto>> TestAsync(Guid id, CancellationToken ct = default)
    {
        Result<(ImageModel Model, ImageProviderContext Context)> resolved = await _credentials.ResolveAsync(id, requireActive: false, ct);

        if (!resolved.Succeeded)
        {
            return await SaveTestAsync(id, new ImageModelTestResultDto(false, null, resolved.Error, 0), ct);
        }

        (ImageModel model, ImageProviderContext context) = resolved.Value;
        IImageProvider? provider = _registry.Get(model.Adapter);

        if (provider is null)
        {
            return await SaveTestAsync(id, new ImageModelTestResultDto(false, null, "Loại kết nối này chưa được hỗ trợ trên máy chủ.", 0), ct);
        }

        string size = ImageStudioRules.ResolveSize("1:1", ImageStudioRules.ParseSizes(model.SupportedSizes, out _));
        ImageProviderResult result = await provider.GenerateAsync(new ImageGenerateRequest(context, TestPrompt, size), ct);

        // Chạy thử cũng tốn tiền thật — ghi sổ như mọi lần gọi khác, tính vào site đang mở.
        _db.ImageProviderCalls.Add(ImageJobRunner.NewCall(null, model, result, "test", _db.CurrentSiteId));

        if (!result.Ok)
        {
            return await SaveTestAsync(id, new ImageModelTestResultDto(false, null, result.ErrorMessage, result.DurationMs), ct);
        }

        try
        {
            FinalizedImage image = AiImageFinalizer.Finalize(result.Image!.Bytes, model.OutputFormat, AiImageFinalizer.TrainedAlgorithmicMedia);
            await using var stream = new MemoryStream(image.Bytes);
            string key = await _storage.SaveAsync(stream, $"test.{image.Extension}", TestFolder, ct);

            return await SaveTestAsync(id, new ImageModelTestResultDto(true, _storage.GetPublicUrl(key), null, result.DurationMs), ct);
        }
        catch (InvalidDataException ex)
        {
            return await SaveTestAsync(id, new ImageModelTestResultDto(false, null, ex.Message, result.DurationMs), ct);
        }
    }

    private async Task<Result<ImageModelTestResultDto>> SaveTestAsync(Guid id, ImageModelTestResultDto outcome, CancellationToken ct)
    {
        ImageModel? model = await _db.ImageModels.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (model is not null)
        {
            model.LastTestedAt = DateTime.UtcNow;
            model.LastTestOk = outcome.Ok;
            model.LastTestError = outcome.Error is { Length: > 1000 } e ? e[..1000] : outcome.Error;
        }

        await _db.SaveChangesAsync(ct);

        return Result<ImageModelTestResultDto>.Success(outcome);
    }

    private async Task<string?> ValidateAsync(ImageModelUpsertDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Trim().Length > 150)
        {
            return "Tên hiển thị bắt buộc, tối đa 150 ký tự.";
        }

        if (dto.Description?.Trim().Length > 500)
        {
            return "Mô tả tối đa 500 ký tự.";
        }

        if (string.IsNullOrWhiteSpace(dto.ModelId) || dto.ModelId.Trim().Length > 200)
        {
            return "Model id bắt buộc, tối đa 200 ký tự.";
        }

        if (!_registry.Available.Contains(dto.Adapter))
        {
            return "Loại kết nối này chưa được hỗ trợ trên máy chủ.";
        }

        if (dto.Adapter != ImageProviderAdapter.Fake)
        {
            var connection = await _db.AiConnections.AsNoTracking()
                .Where(c => c.Id == dto.ConnectionId && !c.IsDeleted)
                .Select(c => new { c.BaseUrl })
                .FirstOrDefaultAsync(ct);

            if (connection is null)
            {
                return "Chọn kết nối AI chứa API key cho model này.";
            }

            if (!ImageStudioRules.IsAllowedBaseUrl(connection.BaseUrl, out string? urlError))
            {
                return urlError;
            }
        }

        if (dto.Capabilities == ImageCapabilities.None)
        {
            return "Chọn ít nhất một năng lực của model.";
        }

        List<string> sizes = ImageStudioRules.ParseSizes(dto.SupportedSizes, out List<string> invalid);

        if (invalid.Count > 0)
        {
            return $"Kích thước không hợp lệ: {string.Join(", ", invalid)}. Dùng dạng 1024x1024, 16:9 hoặc auto.";
        }

        if (sizes.Count == 0)
        {
            return "Khai ít nhất một kích thước model nhận.";
        }

        if (!ImageStudioRules.OutputFormats.Contains(dto.OutputFormat?.Trim().ToLowerInvariant() ?? string.Empty))
        {
            return "Định dạng ảnh phải là png, jpeg hoặc webp.";
        }

        if (dto.Quality?.Trim().Length > 20)
        {
            return "Mức chất lượng tối đa 20 ký tự.";
        }

        if (dto.MaxVariants is < 1 or > ImageStudioRules.MaxVariantsHardCap)
        {
            return $"Số ảnh mỗi lần phải từ 1 đến {ImageStudioRules.MaxVariantsHardCap}.";
        }

        if (dto.MaxReferenceImages is < 0 or > 16)
        {
            return "Số ảnh tham chiếu từ 0 đến 16.";
        }

        if (dto.TimeoutSeconds is < 30 or > 900)
        {
            return "Thời gian chờ từ 30 đến 900 giây.";
        }

        if (dto.PricePerImageUsd is < 0 or > 100)
        {
            return "Giá mỗi ảnh từ 0 đến 100 USD.";
        }

        if (!string.IsNullOrWhiteSpace(dto.ExtraParamsJson))
        {
            try
            {
                if (JsonNode.Parse(dto.ExtraParamsJson) is not JsonObject)
                {
                    return "Tham số bổ sung phải là một JSON object, ví dụ {\"background\": \"auto\"}.";
                }
            }
            catch (JsonException)
            {
                return "Tham số bổ sung không phải JSON hợp lệ.";
            }
        }

        return null;
    }

    private static void Apply(ImageModel model, ImageModelUpsertDto dto)
    {
        model.Name = dto.Name.Trim();
        model.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        model.ConnectionId = dto.Adapter == ImageProviderAdapter.Fake ? Guid.Empty : dto.ConnectionId;
        model.Adapter = dto.Adapter;
        model.ModelId = dto.ModelId.Trim();
        model.Capabilities = dto.Capabilities;
        model.MaxReferenceImages = dto.MaxReferenceImages;
        model.MaskConvention = dto.MaskConvention;
        model.SupportedSizes = string.Join('\n', ImageStudioRules.ParseSizes(dto.SupportedSizes, out _));
        model.MaxVariants = dto.MaxVariants;
        model.Quality = string.IsNullOrWhiteSpace(dto.Quality) ? null : dto.Quality.Trim();
        model.OutputFormat = dto.OutputFormat.Trim().ToLowerInvariant();
        model.PricePerImageUsd = dto.PricePerImageUsd;
        model.TimeoutSeconds = dto.TimeoutSeconds;
        model.ExtraParamsJson = string.IsNullOrWhiteSpace(dto.ExtraParamsJson) ? null : dto.ExtraParamsJson.Trim();
        model.IsActive = dto.IsActive;
        model.IsDefault = dto.IsDefault && dto.IsActive;
        model.SortOrder = dto.SortOrder;
    }

    private async Task ClearDefaultAsync(Guid? keepId, CancellationToken ct)
    {
        List<ImageModel> current = await _db.ImageModels.Where(m => m.IsDefault && m.Id != keepId).ToListAsync(ct);

        foreach (ImageModel m in current)
        {
            m.IsDefault = false;
        }
    }

    private async Task<Dictionary<Guid, string>> ConnectionNamesAsync(CancellationToken ct) =>
        await _db.AiConnections.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

    private ImageModelDto ToDto(ImageModel m, IReadOnlyDictionary<Guid, string> connections) => new(
        m.Id,
        m.Name,
        m.Description,
        m.ConnectionId,
        connections.GetValueOrDefault(m.ConnectionId),
        m.Adapter,
        m.ModelId,
        m.Capabilities,
        m.MaxReferenceImages,
        m.MaskConvention,
        m.SupportedSizes,
        m.MaxVariants,
        m.Quality,
        m.OutputFormat,
        m.PricePerImageUsd,
        m.TimeoutSeconds,
        m.ExtraParamsJson,
        m.IsActive,
        m.IsDefault,
        m.SortOrder,
        _registry.Get(m.Adapter) is not null,
        m.LastTestedAt,
        m.LastTestOk,
        m.LastTestError);
}
