using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Common;
using NewsCMS.Application.Content;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio.Jobs;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>
/// Tạo ảnh cho site hiện tại: kiểm tra, trừ hạn mức, lưu job, xếp hàng; đưa ảnh đã chọn vào thư viện.
/// </summary>
/// <remarks>
/// <para>
/// Mọi truy vấn job đi qua global filter site — id job của site khác coi như không tồn tại.
/// </para>
/// <para>
/// <b>Hạn mức kiểm dưới khoá theo site</b>: hai lần bấm cùng lúc không cùng lọt qua chỗ "còn 1 lượt".
/// Khoá nằm trong tiến trình; chạy nhiều instance thì có thể vượt vài ảnh — chấp nhận được với hạn
/// mức tính theo tháng.
/// </para>
/// </remarks>
public sealed partial class ImageStudioService : IImageStudioService
{
    public const string AiFolderName = "Ảnh AI";

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SiteLocks = new();

    private readonly AppDbContext _db;
    private readonly IImageProviderRegistry _registry;
    private readonly IImageJobQueue _queue;
    private readonly IMediaService _media;
    private readonly IFileStorage _storage;
    private readonly ILogger<ImageStudioService> _logger;

    public ImageStudioService(
        AppDbContext db,
        IImageProviderRegistry registry,
        IImageJobQueue queue,
        IMediaService media,
        IFileStorage storage,
        ILogger<ImageStudioService> logger)
    {
        _db = db;
        _registry = registry;
        _queue = queue;
        _media = media;
        _storage = storage;
        _logger = logger;
    }

    public async Task<ImageStudioFormDto> GetFormAsync(Guid userId, CancellationToken ct = default)
    {
        ImageStudioSiteSettings? settings = await _db.ImageStudioSiteSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        List<ImageModelOptionDto> models = await GetModelOptionsAsync(ct);

        string? disabled = settings is not { Enabled: true }
            ? "Xưởng ảnh AI chưa được bật cho site này. Liên hệ quản trị nền tảng."
            : models.Count == 0
                ? "Chưa có model tạo ảnh nào đang bật. Liên hệ quản trị nền tảng."
                : null;

        (int? monthly, int? daily) = settings is null ? (null, null) : await RemainingAsync(settings, userId, ct);

        return new ImageStudioFormDto(
            disabled is null,
            disabled,
            models,
            ImageStudioRules.AspectRatios,
            monthly,
            daily,
            settings?.CoverAspect ?? "16:9",
            settings?.ProductAspect ?? "1:1");
    }

    public async Task<Result<ImageJobDto>> CreateAsync(ImageJobCreateInput input, Guid userId, CancellationToken ct = default)
    {
        string key = input.IdempotencyKey?.Trim() ?? string.Empty;

        if (!IdempotencyKeyPattern().IsMatch(key))
        {
            return Result<ImageJobDto>.Failure("Thiếu mã chống gửi trùng. Tải lại trang rồi thử lại.");
        }

        // Gửi lại cùng khoá (bấm hai lần, mạng chập chờn) → trả job cũ, không tạo và tính tiền lần nữa.
        if (await FindByKeyAsync(key, ct) is { } existing)
        {
            return Result<ImageJobDto>.Success(existing);
        }

        ImageStudioSiteSettings? settings = await _db.ImageStudioSiteSettings.AsNoTracking().FirstOrDefaultAsync(ct);

        if (settings is not { Enabled: true })
        {
            return Result<ImageJobDto>.Failure("Xưởng ảnh AI chưa được bật cho site này. Liên hệ quản trị nền tảng.");
        }

        string prompt = input.Prompt?.Trim() ?? string.Empty;

        if (prompt.Length == 0)
        {
            return Result<ImageJobDto>.Failure("Hãy mô tả ảnh cần tạo.");
        }

        if (prompt.Length > ImageStudioRules.MaxPromptLength)
        {
            return Result<ImageJobDto>.Failure($"Mô tả dài quá {ImageStudioRules.MaxPromptLength} ký tự.");
        }

        if (!ImageStudioRules.AspectRatios.Contains(input.AspectRatio))
        {
            return Result<ImageJobDto>.Failure("Tỉ lệ khung không hợp lệ.");
        }

        // Chỗ giữ của mẫu: {thuong_hieu} server tự điền bằng tên site; chỗ khác còn trống thì báo, không gửi
        // nguyên chữ "{san_pham}" cho model vẽ.
        if (prompt.Contains(ImagePromptTemplate.BrandPlaceholder, StringComparison.Ordinal))
        {
            string siteName = await _db.Sites.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Id == _db.CurrentSiteId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct) ?? string.Empty;
            prompt = ImagePromptPlaceholders.Fill(prompt, new Dictionary<string, string?> { [ImagePromptTemplate.BrandPlaceholder] = siteName });
        }

        List<string> unfilled = ImagePromptPlaceholders.FindKnown(prompt);

        if (unfilled.Count > 0)
        {
            return Result<ImageJobDto>.Failure(
                $"Mô tả còn chỗ trống {string.Join(", ", unfilled.Select(t => $"{t} ({ImagePromptPlaceholders.Label(t)})"))} — điền vào ô tương ứng hoặc sửa mô tả.");
        }

        ImageModel? model = await _db.ImageModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == input.ModelId && m.IsActive, ct);

        if (model is null || _registry.Get(model.Adapter) is null)
        {
            return Result<ImageJobDto>.Failure("Model đã chọn không còn dùng được. Chọn model khác.");
        }

        if (!model.Capabilities.HasFlag(ImageCapabilities.TextToImage))
        {
            return Result<ImageJobDto>.Failure($"Model \"{model.Name}\" không tạo ảnh từ mô tả.");
        }

        int maxVariants = Math.Min(Math.Max(model.MaxVariants, 1), ImageStudioRules.MaxVariantsHardCap);

        if (input.VariantCount < 1 || input.VariantCount > maxVariants)
        {
            return Result<ImageJobDto>.Failure($"Số ảnh mỗi lần phải từ 1 đến {maxVariants}.");
        }

        Guid siteId = _db.CurrentSiteId;
        SemaphoreSlim gate = SiteLocks.GetOrAdd(siteId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        ImageJob job;

        try
        {
            (int? monthly, int? daily) = await RemainingAsync(settings, userId, ct);

            if (monthly is { } m && input.VariantCount > m)
            {
                return Result<ImageJobDto>.Failure(m == 0
                    ? "Site đã dùng hết lượt tạo ảnh của tháng này."
                    : $"Site chỉ còn {m} lượt tạo ảnh trong tháng này.");
            }

            if (daily is { } d && input.VariantCount > d)
            {
                return Result<ImageJobDto>.Failure(d == 0
                    ? "Bạn đã dùng hết lượt tạo ảnh hôm nay."
                    : $"Bạn chỉ còn {d} lượt tạo ảnh hôm nay.");
            }

            List<string> sizes = ImageStudioRules.ParseSizes(model.SupportedSizes, out _);

            job = new ImageJob
            {
                SiteId = siteId,
                Mode = ImageJobMode.Generate,
                Purpose = input.Purpose,
                ImageModelId = model.Id,
                ModelName = model.Name,
                TemplateId = input.TemplateId,
                UserPrompt = prompt,
                FinalPrompt = ImageStudioRules.BuildFinalPrompt(prompt, input.Purpose, settings.BrandStyle),
                AspectRatio = input.AspectRatio,
                Size = ImageStudioRules.ResolveSize(input.AspectRatio, sizes),
                Quality = model.Quality,
                VariantCount = input.VariantCount,
                ContextType = NormalizeContextType(input.ContextType),
                ContextId = input.ContextId,
                Status = ImageJobStatus.Queued,
                QueuedAt = DateTime.UtcNow,
                EstimatedCostUsd = model.PricePerImageUsd * input.VariantCount,
                IdempotencyKey = key,
                CreatedBy = userId,
            };

            _db.ImageJobs.Add(job);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Hai request cùng khoá chạy song song: unique index chặn bản thứ hai → trả bản đã lưu.
                _db.Entry(job).State = EntityState.Detached;

                if (await FindByKeyAsync(key, ct) is { } raced)
                {
                    return Result<ImageJobDto>.Success(raced);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }

        _queue.Enqueue(job.Id);

        if (job.TemplateId is { } templateId)
        {
            await ImagePromptLibraryService.RecordUsageAsync(_db, templateId, ct);
        }

        return Result<ImageJobDto>.Success(ToDto(job));
    }

    public async Task<IReadOnlyList<ImageJobDto>> GetJobsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // List thay vì mảng: xem ghi chú ở global.json về Contains trong LINQ.
        List<Guid> idList = ids.Distinct().Take(50).ToList();

        List<ImageJob> jobs = await _db.ImageJobs.AsNoTracking()
            .Include(j => j.Outputs)
            .Where(j => idList.Contains(j.Id))
            .ToListAsync(ct);

        return jobs.Select(ToDto).ToList();
    }

    public async Task<ImageJobDto?> GetJobAsync(Guid id, CancellationToken ct = default)
    {
        ImageJob? job = await _db.ImageJobs.AsNoTracking().Include(j => j.Outputs).FirstOrDefaultAsync(j => j.Id == id, ct);

        return job is null ? null : ToDto(job);
    }

    public async Task<PagedList<ImageJobListItemDto>> SearchAsync(ImageJobStatus? status, string? keyword, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<ImageJob> query = _db.ImageJobs.AsNoTracking();

        if (status is { } s)
        {
            query = query.Where(j => j.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            string k = keyword.Trim();
            query = query.Where(j => j.UserPrompt.Contains(k) || j.ModelName.Contains(k));
        }

        int total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(j => j.QueuedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new
            {
                j.Id, j.Status, j.Purpose, j.ModelName, j.UserPrompt, j.VariantCount, j.CostUsd, j.QueuedAt,
                OutputCount = j.Outputs.Count,
                Thumb = j.Outputs.Where(o => !o.IsPurged).OrderBy(o => o.Index).Select(o => o.Url).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return new PagedList<ImageJobListItemDto>
        {
            Items = rows.Select(r => new ImageJobListItemDto(r.Id, r.Status, r.Purpose, r.ModelName, r.UserPrompt, r.VariantCount, r.OutputCount, r.CostUsd, r.QueuedAt, r.Thumb)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
        };
    }

    public async Task<Result<ImagePromoteResultDto>> PromoteAsync(Guid outputId, Guid userId, string? altText, CancellationToken ct = default)
    {
        // Đi từ job (có filter site) xuống output: output của site khác không tìm thấy.
        var row = await _db.ImageJobs
            .SelectMany(j => j.Outputs.Where(o => o.Id == outputId), (j, o) => new { Job = j, Output = o })
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            return Result<ImagePromoteResultDto>.Failure("Không tìm thấy ảnh.");
        }

        ImageJobOutput output = row.Output;

        if (output.PromotedMediaId is { } promotedId
            && await _db.Medias.AsNoTracking().FirstOrDefaultAsync(m => m.Id == promotedId, ct) is { } already)
        {
            return Result<ImagePromoteResultDto>.Success(new ImagePromoteResultDto(already.Id, already.FilePath, already.AltText, already.Width, already.Height));
        }

        if (output.IsPurged)
        {
            return Result<ImagePromoteResultDto>.Failure("Ảnh này đã bị dọn vì quá hạn giữ. Hãy tạo lại.");
        }

        string path;

        try
        {
            path = _storage.GetLocalPath(output.StorageKey);
        }
        catch (NotImplementedException)
        {
            return Result<ImagePromoteResultDto>.Failure("Kho lưu trữ hiện tại chưa hỗ trợ đưa ảnh AI vào thư viện.");
        }

        if (!File.Exists(path))
        {
            return Result<ImagePromoteResultDto>.Failure("Không tìm thấy file ảnh trên máy chủ. Hãy tạo lại.");
        }

        Guid folderId = await EnsureAiFolderAsync(ct);
        string extension = Path.GetExtension(output.StorageKey);
        string fileName = ImageStudioRules.MediaFileName(row.Job.QueuedAt, output.Index, extension);

        Result<MediaUploadResultDto> uploaded;

        await using (FileStream stream = File.OpenRead(path))
        {
            uploaded = await _media.CreateFromUploadAsync(stream, fileName, output.MimeType, stream.Length, userId, folderId, ct);
        }

        if (!uploaded.Succeeded)
        {
            return Result<ImagePromoteResultDto>.Failure(uploaded.Error ?? "Không đưa được ảnh vào thư viện.");
        }

        Media? media = await _db.Medias.FirstOrDefaultAsync(m => m.Id == uploaded.Value!.Id, ct);

        if (media is null)
        {
            return Result<ImagePromoteResultDto>.Failure("Không đưa được ảnh vào thư viện.");
        }

        string title = ImageStudioRules.DefaultAltText(row.Job.UserPrompt);
        string alt = string.IsNullOrWhiteSpace(altText) ? title : altText.Trim();

        media.Origin = row.Job.Mode == ImageJobMode.RegionEdit ? MediaOrigins.AiEdited : MediaOrigins.AiGenerated;
        media.AiJobId = row.Job.Id;
        media.AltText = alt.Length <= 500 ? alt : alt[..500];
        media.Title = title;

        output.PromotedMediaId = media.Id;
        output.PromotedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Ảnh AI {OutputId} của job {JobId} đã vào thư viện media {MediaId}.", output.Id, row.Job.Id, media.Id);

        return Result<ImagePromoteResultDto>.Success(new ImagePromoteResultDto(media.Id, media.FilePath, media.AltText, media.Width, media.Height));
    }

    public async Task<Result> CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        ImageJob? job = await _db.ImageJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job is null)
        {
            return Result.Failure("Không tìm thấy yêu cầu tạo ảnh.");
        }

        if (job.Status != ImageJobStatus.Queued)
        {
            return Result.Failure("Chỉ huỷ được yêu cầu đang chờ. Yêu cầu đang chạy đã gửi tới nhà cung cấp.");
        }

        job.Status = ImageJobStatus.Canceled;
        job.FinishedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure("Yêu cầu vừa bắt đầu chạy, không huỷ được nữa.");
        }

        return Result.Success();
    }

    private async Task<List<ImageModelOptionDto>> GetModelOptionsAsync(CancellationToken ct)
    {
        List<ImageModel> models = await _db.ImageModels.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.IsDefault).ThenBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToListAsync(ct);

        return models
            .Where(m => _registry.Get(m.Adapter) is not null && m.Capabilities.HasFlag(ImageCapabilities.TextToImage))
            .Select(m => new ImageModelOptionDto(
                m.Id, m.Name, m.Description, m.PricePerImageUsd,
                Math.Min(Math.Max(m.MaxVariants, 1), ImageStudioRules.MaxVariantsHardCap),
                m.Capabilities, m.IsDefault))
            .ToList();
    }

    /// <summary>
    /// Lượt còn lại (null = không giới hạn). Đã dùng = ảnh đã sinh + ảnh đang chờ/đang chạy (tính
    /// trước theo số biến thể, để nhiều job xếp hàng cùng lúc không vượt hạn mức).
    /// </summary>
    private async Task<(int? Monthly, int? Daily)> RemainingAsync(ImageStudioSiteSettings settings, Guid userId, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        DateTime monthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime dayStart = now.Date;

        int? monthly = null;
        int? daily = null;

        if (settings.MonthlyImageQuota > 0)
        {
            int used = await UsedAsync(_db.ImageJobs.Where(j => j.QueuedAt >= monthStart), ct);
            monthly = Math.Max(0, settings.MonthlyImageQuota - used);
        }

        if (settings.PerUserDailyQuota > 0)
        {
            int used = await UsedAsync(_db.ImageJobs.Where(j => j.QueuedAt >= dayStart && j.CreatedBy == userId), ct);
            daily = Math.Max(0, settings.PerUserDailyQuota - used);
        }

        return (monthly, daily);
    }

    private static async Task<int> UsedAsync(IQueryable<ImageJob> jobs, CancellationToken ct)
    {
        int pending = await jobs
            .Where(j => j.Status == ImageJobStatus.Queued || j.Status == ImageJobStatus.Running)
            .SumAsync(j => (int?)j.VariantCount, ct) ?? 0;

        int produced = await jobs.SelectMany(j => j.Outputs).CountAsync(ct);

        return pending + produced;
    }

    private async Task<ImageJobDto?> FindByKeyAsync(string key, CancellationToken ct)
    {
        ImageJob? job = await _db.ImageJobs.AsNoTracking().Include(j => j.Outputs).FirstOrDefaultAsync(j => j.IdempotencyKey == key, ct);

        return job is null ? null : ToDto(job);
    }

    private async Task<Guid> EnsureAiFolderAsync(CancellationToken ct)
    {
        MediaFolder? folder = await _db.MediaFolders.FirstOrDefaultAsync(f => f.Name == AiFolderName && f.ParentId == null, ct);

        if (folder is null)
        {
            folder = new MediaFolder { Name = AiFolderName };
            _db.MediaFolders.Add(folder);
            await _db.SaveChangesAsync(ct);
        }

        return folder.Id;
    }

    private static string? NormalizeContextType(string? contextType) => contextType?.Trim().ToLowerInvariant() switch
    {
        "post" => "post",
        "product" => "product",
        "media" => "media",
        _ => null,
    };

    internal static ImageJobDto ToDto(ImageJob job) => new(
        job.Id,
        job.Status,
        job.Mode,
        job.Purpose,
        job.ModelName,
        job.UserPrompt,
        job.FinalPrompt,
        job.AspectRatio,
        job.Size,
        job.VariantCount,
        job.EstimatedCostUsd,
        job.CostUsd,
        job.Error,
        job.QueuedAt,
        job.StartedAt,
        job.FinishedAt,
        job.CreatedBy,
        job.Outputs
            .OrderBy(o => o.Index)
            .Select(o => new ImageJobOutputDto(o.Id, o.Index, o.IsPurged ? null : o.Url, o.Width, o.Height, o.Bytes, o.PromotedMediaId, o.IsPurged))
            .ToList());

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex IdempotencyKeyPattern();
}
