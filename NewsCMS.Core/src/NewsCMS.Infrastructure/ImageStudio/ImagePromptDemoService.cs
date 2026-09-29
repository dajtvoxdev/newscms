using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio.Imaging;
using NewsCMS.Infrastructure.ImageStudio.Jobs;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>
/// Ảnh demo cho mẫu prompt: người dùng nhìn ảnh để chọn mẫu, không phải đọc prompt.
/// </summary>
/// <remarks>
/// <para>
/// Tạo demo = chạy mẫu với <b>giá trị ví dụ</b> cho chỗ giữ (theo nhóm của mẫu) bằng model demo (cấu hình ở
/// kho mẫu, mặc định là model mặc định), một ảnh, thu nhỏ còn 768px. Tốn tiền thật nên mỗi lần gọi đều
/// ghi <see cref="ImageProviderCall"/> (<c>Operation = demo</c>).
/// </para>
/// <para>
/// File demo nằm ở <c>ai-images/demos/</c>, không phải <c>Media</c>: mẫu dùng chung mọi site còn thư viện
/// media thì theo site. Đổi demo thì file cũ bị xoá.
/// </para>
/// </remarks>
public sealed class ImagePromptDemoService : IImagePromptDemoService
{
    public const string DemoFolder = "ai-images/demos";
    public const int DemoMaxEdge = 768;
    public const long MaxUploadBytes = 10L * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly ImageModelCredentials _credentials;
    private readonly IImageProviderRegistry _registry;
    private readonly IFileStorage _storage;
    private readonly ILogger<ImagePromptDemoService> _logger;

    public ImagePromptDemoService(
        AppDbContext db,
        ImageModelCredentials credentials,
        IImageProviderRegistry registry,
        IFileStorage storage,
        ILogger<ImagePromptDemoService> logger)
    {
        _db = db;
        _credentials = credentials;
        _registry = registry;
        _storage = storage;
        _logger = logger;
    }

    public async Task<Result<ImagePromptTemplateDto>> GenerateAsync(Guid templateId, CancellationToken ct = default)
    {
        ImagePromptTemplate? template = await _db.ImagePromptTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct);

        if (template is null)
        {
            return Result<ImagePromptTemplateDto>.Failure("Mẫu không còn trong kho.");
        }

        if (template.RequiresSourceImage)
        {
            return Result<ImagePromptTemplateDto>.Failure("Mẫu sửa ảnh cần ảnh gốc — hãy tải ảnh demo lên thay vì tạo.");
        }

        Result<(ImageModel Model, ImageProviderContext Context, IImageProvider Provider)> resolved = await ResolveDemoModelAsync(ct);

        if (!resolved.Succeeded)
        {
            return Result<ImagePromptTemplateDto>.Failure(resolved.Error!);
        }

        (ImageModel model, ImageProviderContext context, IImageProvider provider) = resolved.Value;

        string prompt = ImageStudioRules.BuildFinalPrompt(FillSamples(template), template.Purpose, null);
        string size = ImageStudioRules.ResolveSize(template.AspectRatio, ImageStudioRules.ParseSizes(model.SupportedSizes, out _));

        ImageProviderResult result = await provider.GenerateAsync(new ImageGenerateRequest(context, prompt, size), ct);
        _db.ImageProviderCalls.Add(ImageJobRunner.NewCall(null, model, result, "demo", _db.CurrentSiteId));

        if (!result.Ok)
        {
            await _db.SaveChangesAsync(ct);
            return Result<ImagePromptTemplateDto>.Failure($"Không tạo được ảnh demo: {result.ErrorMessage}");
        }

        try
        {
            FinalizedImage image = AiImageFinalizer.Finalize(result.Image!.Bytes, "jpeg", AiImageFinalizer.TrainedAlgorithmicMedia, DemoMaxEdge);
            await ReplaceDemoAsync(template, image, $"Tạo bằng {model.Name}", ct);
        }
        catch (InvalidDataException ex)
        {
            await _db.SaveChangesAsync(ct);
            return Result<ImagePromptTemplateDto>.Failure($"Không tạo được ảnh demo: {ex.Message}");
        }

        return Result<ImagePromptTemplateDto>.Success(ImagePromptLibraryService.ToDto(template));
    }

    public async Task<Result<ImagePromptTemplateDto>> UploadAsync(Guid templateId, Stream content, long length, CancellationToken ct = default)
    {
        if (length <= 0 || length > MaxUploadBytes)
        {
            return Result<ImagePromptTemplateDto>.Failure("Ảnh demo phải nhỏ hơn 10 MB.");
        }

        ImagePromptTemplate? template = await _db.ImagePromptTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct);

        if (template is null)
        {
            return Result<ImagePromptTemplateDto>.Failure("Mẫu không còn trong kho.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        try
        {
            FinalizedImage image = AiImageFinalizer.Normalize(buffer.ToArray(), "jpeg", DemoMaxEdge);
            await ReplaceDemoAsync(template, image, "Tải lên", ct);
        }
        catch (InvalidDataException ex)
        {
            return Result<ImagePromptTemplateDto>.Failure(ex.Message);
        }

        return Result<ImagePromptTemplateDto>.Success(ImagePromptLibraryService.ToDto(template));
    }

    public async Task<Result> RemoveAsync(Guid templateId, CancellationToken ct = default)
    {
        ImagePromptTemplate? template = await _db.ImagePromptTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct);

        if (template is null)
        {
            return Result.Failure("Mẫu không còn trong kho.");
        }

        await DeleteFileAsync(template.DemoStorageKey, ct);
        template.DemoStorageKey = null;
        template.DemoImageUrl = null;
        template.DemoSource = null;
        template.DemoUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<ImageDemoEstimateDto> EstimateMissingAsync(CancellationToken ct = default)
    {
        int count = await MissingQuery().CountAsync(ct);
        Result<(ImageModel Model, ImageProviderContext Context, IImageProvider Provider)> resolved = await ResolveDemoModelAsync(ct);

        return resolved.Succeeded
            ? new ImageDemoEstimateDto(count, resolved.Value.Model.PricePerImageUsd * count, resolved.Value.Model.Name, null)
            : new ImageDemoEstimateDto(count, 0, null, resolved.Error);
    }

    public async Task<Result<ImageDemoBatchResultDto>> GenerateMissingAsync(int max, CancellationToken ct = default)
    {
        List<Guid> ids = await MissingQuery()
            .OrderBy(x => x.Status == ImagePromptTemplateStatus.PendingReview ? 0 : 1)
            .ThenBy(x => x.SortOrder)
            .Select(x => x.Id)
            .Take(Math.Clamp(max, 1, 50))
            .ToListAsync(ct);

        return await GenerateManyAsync(ids, ct);
    }

    /// <summary>Tạo demo lần lượt cho các mẫu — dùng cho nút hàng loạt và cho lần chạy trend theo lịch.</summary>
    internal async Task<Result<ImageDemoBatchResultDto>> GenerateManyAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return Result<ImageDemoBatchResultDto>.Success(new ImageDemoBatchResultDto(0, 0, null));
        }

        // Kiểm model một lần: model hỏng thì dừng ngay, không gọi N lần cùng một lỗi.
        Result<(ImageModel Model, ImageProviderContext Context, IImageProvider Provider)> resolved = await ResolveDemoModelAsync(ct);

        if (!resolved.Succeeded)
        {
            return Result<ImageDemoBatchResultDto>.Failure(resolved.Error!);
        }

        int created = 0;
        int failed = 0;
        var notes = new List<string>();

        foreach (Guid id in ids)
        {
            Result<ImagePromptTemplateDto> one = await GenerateAsync(id, ct);

            if (one.Succeeded)
            {
                created++;
            }
            else
            {
                failed++;
                notes.Add(one.Error!);

                // Hết tiền, sai key, bị giới hạn tốc độ: các mẫu sau cũng sẽ lỗi y hệt.
                if (failed >= 3 && created == 0)
                {
                    notes.Add("Dừng sau 3 lỗi liên tiếp.");
                    break;
                }
            }
        }

        return Result<ImageDemoBatchResultDto>.Success(new ImageDemoBatchResultDto(created, failed, notes.Count == 0 ? null : string.Join('\n', notes.Distinct())));
    }

    private IQueryable<ImagePromptTemplate> MissingQuery() =>
        _db.ImagePromptTemplates.AsNoTracking()
            .Where(x => x.DemoStorageKey == null
                        && !x.RequiresSourceImage
                        && (x.Status == ImagePromptTemplateStatus.Published || x.Status == ImagePromptTemplateStatus.PendingReview));

    private async Task<Result<(ImageModel Model, ImageProviderContext Context, IImageProvider Provider)>> ResolveDemoModelAsync(CancellationToken ct)
    {
        ImagePromptLibrarySettings settings = await ImagePromptLibraryService.LoadSettingsAsync(_db, ct);

        List<ImageModel> active = await _db.ImageModels.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.IsDefault).ThenBy(m => m.SortOrder)
            .ToListAsync(ct);

        ImageModel? model = (settings.DemoModelId is { } wanted ? active.FirstOrDefault(m => m.Id == wanted) : null)
                            ?? active.FirstOrDefault(m => m.Capabilities.HasFlag(ImageCapabilities.TextToImage) && _registry.Get(m.Adapter) is not null);

        if (model is null)
        {
            return Result<(ImageModel, ImageProviderContext, IImageProvider)>.Failure("Chưa có model tạo ảnh nào đang bật để tạo ảnh demo.");
        }

        Result<(ImageModel Model, ImageProviderContext Context)> credentials = await _credentials.ResolveAsync(model.Id, requireActive: true, ct);

        if (!credentials.Succeeded)
        {
            return Result<(ImageModel, ImageProviderContext, IImageProvider)>.Failure(credentials.Error!);
        }

        IImageProvider? provider = _registry.Get(model.Adapter);

        return provider is null
            ? Result<(ImageModel, ImageProviderContext, IImageProvider)>.Failure($"Loại kết nối của model \"{model.Name}\" chưa được hỗ trợ trên máy chủ.")
            : Result<(ImageModel, ImageProviderContext, IImageProvider)>.Success((model, credentials.Value.Context, provider));
    }

    private async Task ReplaceDemoAsync(ImagePromptTemplate template, FinalizedImage image, string source, CancellationToken ct)
    {
        string? oldKey = template.DemoStorageKey;

        await using (var stream = new MemoryStream(image.Bytes))
        {
            string key = await _storage.SaveAsync(stream, $"demo.{image.Extension}", DemoFolder, ct);
            template.DemoStorageKey = key;
            template.DemoImageUrl = _storage.GetPublicUrl(key);
        }

        template.DemoSource = source.Length <= 200 ? source : source[..200];
        template.DemoUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await DeleteFileAsync(oldKey, ct);
    }

    private async Task DeleteFileAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        try
        {
            await _storage.DeleteAsync(key, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotImplementedException)
        {
            _logger.LogWarning(ex, "Không xoá được file demo cũ {Key}.", key);
        }
    }

    /// <summary>Giá trị ví dụ cho chỗ giữ — hợp với nhóm của mẫu để ảnh demo trông "thật".</summary>
    internal static string FillSamples(ImagePromptTemplate template)
    {
        (string topic, string product) = template.Category switch
        {
            "Ăn uống" => ("Bữa sáng phở bò Hà Nội", "Hộp bánh pía sầu riêng"),
            "Mỹ phẩm & làm đẹp" => ("Chăm sóc da mùa hanh khô", "Lọ serum thuỷ tinh màu hổ phách"),
            "Thời trang" => ("Phối đồ công sở mùa thu", "Túi xách da màu nâu bò"),
            "Công nghệ" => ("Làm việc từ xa hiệu quả", "Tai nghe không dây màu trắng"),
            "Nhà cửa & đời sống" => ("Ban công xanh nhỏ xinh", "Bình gốm men lam"),
            "Du lịch & lưu trú" => ("Mùa lúa chín ở Mù Cang Chải", "Balo du lịch vải bố"),
            "Giáo dục" => ("Học ngoại ngữ mỗi ngày", "Bộ bút màu gỗ"),
            "Sự kiện & khuyến mãi" => ("Ưu đãi mùa lễ hội", "Hộp quà bọc giấy kraft"),
            "Mạng xã hội" => ("Cà phê cuối tuần", "Ly cà phê muối"),
            _ => ("Cà phê sáng ở phố cổ Hà Nội", "Hũ mật ong rừng"),
        };

        return ImagePromptPlaceholders.Fill(template.Prompt, new Dictionary<string, string?>
        {
            [ImagePromptTemplate.TopicPlaceholder] = topic,
            [ImagePromptTemplate.ProductPlaceholder] = product,
            [ImagePromptTemplate.DescriptionPlaceholder] = "sản phẩm thủ công của một cửa hàng địa phương",
            [ImagePromptTemplate.BrandPlaceholder] = "thương hiệu địa phương",
        });
    }
}
