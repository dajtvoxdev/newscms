using System.Net;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Pipeline;
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

    /// <summary>Trần kích thước một ảnh. Lớn hơn thì gần như chắc chắn là nhầm file, không phải ảnh sản phẩm.</summary>
    private const long MaxImageBytes = 15L * 1024 * 1024;

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
        context.VoiceId = brief.VoiceProfileId;

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

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg",
    };
}
