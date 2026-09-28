using NewsCMS.Application.Common;
using NewsCMS.Application.Site;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Infrastructure.VideoStudio;

/// <summary>
/// Gọi API video của AdVideo bằng tenant key của <b>site hiện tại</b>.
/// </summary>
/// <remarks>
/// Site lấy từ <see cref="ICurrentSite"/>, không bao giờ từ tham số: trang của site A không có cách
/// nào gọi AdVideo bằng key của site B. Cách ly phía AdVideo (tenant filter) là lớp thứ hai.
/// </remarks>
public sealed class AdVideoClient : IAdVideoClient
{
    private readonly IHttpClientFactory _http;
    private readonly AdVideoKeyStore _keys;
    private readonly ICurrentSite _site;

    public AdVideoClient(IHttpClientFactory http, AdVideoKeyStore keys, ICurrentSite site)
    {
        _http = http;
        _keys = keys;
        _site = site;
    }

    public async Task<Result<AdVideoUploadDto>> UploadImageAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        (HttpClient? client, string? error) = await ClientAsync(ct);

        if (client is null)
        {
            return Result<AdVideoUploadDto>.Failure(error!);
        }

        using (client)
        {
            var form = new MultipartFormDataContent();
            var file = new StreamContent(content);

            // AdVideo nhận dạng ảnh bằng nội dung, không bằng header — gửi octet-stream cho thật thà.
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", Path.GetFileName(fileName));

            return await AdVideoHttp.SendAsync<AdVideoUploadDto>(client, HttpMethod.Post, "v1/uploads", form, ct);
        }
    }

    public async Task<Result<AdVideoJobDto>> CreateJobAsync(CreateAdVideoInput input, CancellationToken ct = default)
    {
        (HttpClient? client, string? error) = await ClientAsync(ct);

        if (client is null)
        {
            return Result<AdVideoJobDto>.Failure(error!);
        }

        var body = new
        {
            brief = new
            {
                product_name = string.IsNullOrWhiteSpace(input.ProductName) ? null : input.ProductName.Trim(),
                prompt = input.Prompt.Trim(),
                duration_seconds = input.DurationSeconds,
                aspect_ratio = input.AspectRatio,
                language = "vi",
            },
            assets = new { product_image_ids = input.ProductImageIds.Select(id => id.ToString()).ToArray() },
            voice = new { script = input.Script.Trim() },
            audio = new { native_sound = input.NativeSound },
            options = new { quality = input.Quality, has_person = input.HasPerson },
        };

        using (client)
        {
            return await AdVideoHttp.SendAsync<AdVideoJobDto>(
                client,
                HttpMethod.Post,
                "v1/ad-videos",
                AdVideoHttp.JsonBody(body),
                ct,
                new Dictionary<string, string> { ["Idempotency-Key"] = input.IdempotencyKey });
        }
    }

    public async Task<Result<AdVideoJobPageDto>> ListJobsAsync(string? status, int page, int pageSize, CancellationToken ct = default)
    {
        string query = $"v1/ad-videos?page={Math.Max(page, 1)}&page_size={Math.Clamp(pageSize, 1, 100)}";

        if (!string.IsNullOrWhiteSpace(status))
        {
            query += $"&status={Uri.EscapeDataString(status)}";
        }

        return await CallAsync<AdVideoJobPageDto>(HttpMethod.Get, query, ct);
    }

    public Task<Result<AdVideoJobDto>> GetJobAsync(Guid jobId, CancellationToken ct = default) =>
        CallAsync<AdVideoJobDto>(HttpMethod.Get, $"v1/ad-videos/{jobId}", ct);

    public Task<Result<AdVideoJobDto>> CancelJobAsync(Guid jobId, CancellationToken ct = default) =>
        CallAsync<AdVideoJobDto>(HttpMethod.Post, $"v1/ad-videos/{jobId}/cancel", ct);

    private async Task<Result<T>> CallAsync<T>(HttpMethod method, string path, CancellationToken ct)
    {
        (HttpClient? client, string? error) = await ClientAsync(ct);

        if (client is null)
        {
            return Result<T>.Failure(error!);
        }

        using (client)
        {
            return await AdVideoHttp.SendAsync<T>(client, method, path, null, ct);
        }
    }

    private async Task<(HttpClient? Client, string? Error)> ClientAsync(CancellationToken ct)
    {
        if (!_site.IsResolved || _site.SiteId == Guid.Empty)
        {
            return (null, "Chưa chọn site. Chọn site ở góc trên rồi thử lại.");
        }

        (AdVideoEndpoint? endpoint, string? error) = await _keys.GetTenantEndpointAsync(_site.SiteId, ct);

        return endpoint is null ? (null, error) : (AdVideoHttp.CreateClient(_http, endpoint), null);
    }
}
