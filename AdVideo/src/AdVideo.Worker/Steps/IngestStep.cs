using System.Net;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Media;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Worker.Jobs;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Worker.Steps;

/// <summary>
/// Bước 1 — đọc brief và kéo ảnh của khách về kho của mình.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao phải tải ảnh về thay vì chuyển thẳng URL cho provider.</b> URL của khách có thể là
/// link tạm, có thể đổi nội dung giữa chừng, có thể chết ngay sau khi job vào hàng đợi. Provider
/// đọc được hay không thì ta không kiểm soát, nhưng một job fail ở phút thứ ba vì cái ảnh biến
/// mất là thứ hoàn toàn tránh được bằng một lần tải ở phút đầu tiên.
/// </para>
/// <para>
/// <b>Ảnh được tải bằng HTTP tới địa chỉ do khách cung cấp.</b> Sprint 1 chỉ chặn ở mức giao thức
/// (http/https) và kích thước; khách đã xác thực bằng API key nên đây là rủi ro chấp nhận được ở
/// giai đoạn này. Khi mở cho người dùng tự đăng ký thì phải chặn thêm địa chỉ nội bộ
/// (169.254.x, 10.x, localhost) — nếu không, endpoint này thành công cụ dò mạng nội bộ.
/// </para>
/// </remarks>
public sealed class IngestStep : IPipelineStep
{
    /// <summary>Tên HttpClient dành riêng cho việc tải ảnh của khách.</summary>
    public const string HttpClientName = "advideo-ingest";

    /// <summary>Trần kích thước một ảnh — cùng một con số với cửa upload (<see cref="ProductImageFormat.MaxBytes"/>).</summary>
    private const long MaxImageBytes = ProductImageFormat.MaxBytes;

    private readonly AdVideoDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JobArtifacts _artifacts;
    private readonly ILogger<IngestStep> _logger;

    public IngestStep(
        AdVideoDbContext db,
        IHttpClientFactory httpClientFactory,
        JobArtifacts artifacts,
        ILogger<IngestStep> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _artifacts = artifacts;
        _logger = logger;
    }

    public int Order => 1;

    public JobStatus RunningStatus => JobStatus.Ingesting;

    public string DisplayName => "Đang nạp ảnh và brief";

    public bool ShouldRun(PipelineContext context) => true;

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        AdVideoJob? job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == context.JobId, cancellationToken);

        if (job is null)
        {
            return StepResult.Fail($"Không tìm thấy job {context.JobId} trong DB.");
        }

        JobBrief brief = JobBrief.Parse(job.BriefJson);
        IReadOnlyList<string> problems = brief.Problems();

        if (problems.Count > 0)
        {
            // Không retry: brief sẽ không tự đầy đủ lên ở lần chạy sau.
            return StepResult.Fail(string.Join(" ", problems));
        }

        context.SetBrief(brief);
        context.NarrationText = brief.Script;

        // Ảnh đã tải lên trước (POST /v1/uploads): đã nằm trong adv-uploads nên dùng thẳng object
        // đó, không tải lại, không chép. Đi TRƯỚC ảnh URL để thứ tự ảnh ổn định giữa các lần chạy.
        if (brief.ProductImageIds.Count > 0)
        {
            StepResult? uploaded = await UseUploadedImagesAsync(context, job, brief.ProductImageIds, cancellationToken);

            if (uploaded is not null)
            {
                return uploaded;
            }
        }

        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);

        for (int i = 0; i < brief.ProductImages.Count; i++)
        {
            string url = brief.ProductImages[i];

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return StepResult.Fail($"Ảnh sản phẩm thứ {i + 1} không phải URL http/https: \"{url}\".");
            }

            try
            {
                using HttpResponseMessage response = await http.GetAsync(
                    uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // 4xx là lỗi của dữ liệu đầu vào (link sai, link riêng tư) — chạy lại vẫn thế.
                    // 5xx là máy chủ của khách đang trục trặc — chạy lại có thể được.
                    string reason =
                        $"Không tải được ảnh sản phẩm thứ {i + 1} ({url}): máy chủ trả {(int)response.StatusCode}.";

                    return response.StatusCode >= HttpStatusCode.InternalServerError
                        ? StepResult.Retry(reason)
                        : StepResult.Fail(reason);
                }

                string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return StepResult.Fail(
                        $"Ảnh sản phẩm thứ {i + 1} ({url}) không phải ảnh — máy chủ trả về \"{contentType}\".");
                }

                if (response.Content.Headers.ContentLength is { } declared && declared > MaxImageBytes)
                {
                    return StepResult.Fail(
                        $"Ảnh sản phẩm thứ {i + 1} nặng {declared / 1024 / 1024} MB, vượt giới hạn {MaxImageBytes / 1024 / 1024} MB.");
                }

                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);

                // Đọc vào bộ nhớ có giới hạn thay vì đẩy thẳng stream lên storage: client S3 cần
                // biết độ dài để ký, và máy chủ không khai Content-Length là chuyện thường.
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[81920];
                int read;

                while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + read > MaxImageBytes)
                    {
                        return StepResult.Fail(
                            $"Ảnh sản phẩm thứ {i + 1} vượt giới hạn {MaxImageBytes / 1024 / 1024} MB.");
                    }

                    await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                }

                buffer.Position = 0;

                MediaAsset asset = await _artifacts.SaveAsync(
                    context,
                    AssetKind.ProductImage,
                    $"product-{i:00}{ExtensionFor(contentType)}",
                    buffer,
                    contentType,
                    cancellationToken);

                context.ProductImageKeys.Add(asset.ObjectKey);
            }
            catch (HttpRequestException ex)
            {
                return StepResult.Retry(
                    $"Không tải được ảnh sản phẩm thứ {i + 1} ({url}): {ex.Message}", ex.ToString());
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return StepResult.Retry($"Quá hạn khi tải ảnh sản phẩm thứ {i + 1} ({url}).", ex.ToString());
            }
        }

        _logger.LogInformation(
            "job_id={JobId} nạp xong {Count} ảnh sản phẩm, lời thoại {Chars} ký tự.",
            context.JobId,
            context.ProductImageKeys.Count,
            context.NarrationText?.Length ?? 0);

        return StepResult.Ok();
    }

    /// <summary>Gắn ảnh đã tải lên vào job. Null = xong; khác null = lý do dừng.</summary>
    /// <remarks>
    /// <b>Lọc tenant bằng tay, tường minh</b>, dù runner đã ghim tenant cho global filter: bước này
    /// quyết định ảnh nào được gửi cho provider, và "filter đang bật" là một điều kiện nằm ở
    /// file khác. Dòng <c>TenantId == job.TenantId</c> đọc thấy ngay tại chỗ. API đã kiểm lúc nhận
    /// job; kiểm lại ở đây vì brief trong DB mới là thứ worker chạy theo.
    /// </remarks>
    private async Task<StepResult?> UseUploadedImagesAsync(
        PipelineContext context, AdVideoJob job, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        List<Guid> wanted = ids.ToList();

        Dictionary<Guid, MediaAsset> assets = await _db.MediaAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => wanted.Contains(a.Id)
                && a.TenantId == job.TenantId
                && !a.IsDeleted
                && a.Kind == AssetKind.ProductImage)
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        for (int i = 0; i < wanted.Count; i++)
        {
            if (!assets.TryGetValue(wanted[i], out MediaAsset? asset))
            {
                // Không retry: ảnh đã bị xoá sẽ không tự quay lại.
                return StepResult.Fail($"Ảnh đã tải lên {wanted[i]} không còn (bị xoá hoặc không thuộc tài khoản này).");
            }

            if (asset.Bucket != Buckets.Uploads)
            {
                return StepResult.Fail($"Ảnh {asset.Id} nằm ở bucket {asset.Bucket}, không phải {Buckets.Uploads} — dữ liệu không nhất quán.");
            }

            context.ProductImageKeys.Add(asset.ObjectKey);
        }

        return null;
    }

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg",
    };
}
