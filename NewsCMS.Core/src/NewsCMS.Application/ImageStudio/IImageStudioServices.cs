using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Application.ImageStudio;

/// <summary>Model tạo ảnh do quản trị nền tảng cấu hình — dùng chung mọi site.</summary>
public interface IImageModelService
{
    Task<IReadOnlyList<ImageModelDto>> GetAllAsync(CancellationToken ct = default);

    Task<ImageModelDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ImageConnectionOptionDto>> GetConnectionsAsync(CancellationToken ct = default);

    /// <summary>Adapter đã có code (kể cả Fake khi được bật) — form chỉ cho chọn các adapter này.</summary>
    IReadOnlyList<ImageProviderAdapter> GetAvailableAdapters();

    Task<Result<Guid>> CreateAsync(ImageModelUpsertDto dto, CancellationToken ct = default);

    Task<Result> UpdateAsync(ImageModelUpsertDto dto, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gọi model một lần với prompt cố định (tính tiền, có ghi sổ) và lưu kết quả vào model.</summary>
    Task<Result<ImageModelTestResultDto>> TestAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Bật Xưởng ảnh cho từng site và đặt hạn mức — việc của quản trị nền tảng.</summary>
public interface IImageStudioSiteService
{
    Task<IReadOnlyList<ImageStudioSiteRowDto>> GetSitesAsync(CancellationToken ct = default);

    Task<Result> SaveAsync(ImageStudioSiteQuotaInput input, CancellationToken ct = default);
}

/// <summary>Tạo ảnh cho site hiện tại.</summary>
public interface IImageStudioService
{
    /// <summary>Những gì form tạo ảnh cần: bật chưa, model chọn được, còn bao nhiêu lượt.</summary>
    Task<ImageStudioFormDto> GetFormAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra, trừ hạn mức, lưu job và xếp hàng. Trả về ngay — ảnh sinh ở nền, form hỏi trạng thái
    /// bằng <see cref="GetJobsAsync"/>. Gửi lại cùng <c>IdempotencyKey</c> thì nhận lại job cũ.
    /// </summary>
    Task<Result<ImageJobDto>> CreateAsync(ImageJobCreateInput input, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<ImageJobDto>> GetJobsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task<ImageJobDto?> GetJobAsync(Guid id, CancellationToken ct = default);

    Task<PagedList<ImageJobListItemDto>> SearchAsync(ImageJobStatus? status, string? keyword, int page, int pageSize, CancellationToken ct = default);

    /// <summary>"Dùng ảnh này": đưa một ảnh của job vào thư viện media. Bấm lại trả lại media cũ.</summary>
    Task<Result<ImagePromoteResultDto>> PromoteAsync(Guid outputId, Guid userId, string? altText, CancellationToken ct = default);

    /// <summary>Chỉ huỷ được job còn đang chờ — job đang chạy đã gửi đi provider, huỷ không lấy lại được tiền.</summary>
    Task<Result> CancelAsync(Guid jobId, CancellationToken ct = default);
}
