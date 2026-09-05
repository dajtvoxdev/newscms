using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Common;
using NewsCMS.Application.KeoBia;

namespace NewsCMS.Infrastructure.KeoBia;

public sealed class OpenFootballWorldCupSyncService : IKeoBiaOpenFootballSyncService
{
    public const string HttpClientName = "keobia-openfootball";
    public const string DefaultUrl = "https://raw.githubusercontent.com/openfootball/worldcup.json/master/2026/worldcup.json";

    private const string ETagCacheKey = "keobia:openfootball:worldcup2026:etag";
    private const string LastModifiedCacheKey = "keobia:openfootball:worldcup2026:last-modified";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly IKeoBiaService _keoBia;
    private readonly ILogger<OpenFootballWorldCupSyncService> _logger;

    public OpenFootballWorldCupSyncService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IMemoryCache cache,
        IKeoBiaService keoBia,
        ILogger<OpenFootballWorldCupSyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _cache = cache;
        _keoBia = keoBia;
        _logger = logger;
    }

    public async Task<Result<KeoBiaImportResultDto>> SyncAsync(bool force = false, CancellationToken ct = default)
    {
        var url = _configuration["KeoBia:OpenFootball:Url"];
        if (string.IsNullOrWhiteSpace(url)) url = DefaultUrl;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!force)
            {
                if (_cache.TryGetValue<string>(ETagCacheKey, out var etag)
                    && !string.IsNullOrWhiteSpace(etag)
                    && EntityTagHeaderValue.TryParse(etag, out var tag))
                {
                    request.Headers.IfNoneMatch.Add(tag);
                }

                if (_cache.TryGetValue<DateTimeOffset>(LastModifiedCacheKey, out var lastModified))
                    request.Headers.IfModifiedSince = lastModified;
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return Result<KeoBiaImportResultDto>.Success(new KeoBiaImportResultDto(
                    0, 0, 0, 0, "Nguồn OpenFootball chưa thay đổi."));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result<KeoBiaImportResultDto>.Failure(
                    $"OpenFootball trả về HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (response.Headers.ETag is not null)
                _cache.Set(ETagCacheKey, response.Headers.ETag.ToString(), TimeSpan.FromDays(1));
            if (response.Content.Headers.LastModified.HasValue)
                _cache.Set(LastModifiedCacheKey, response.Content.Headers.LastModified.Value, TimeSpan.FromDays(1));

            var json = await response.Content.ReadAsStringAsync(ct);
            // Schedule-only: Football-Data is now the result authority for bia settlement.
            return await _keoBia.SyncOpenFootballWorldCupAsync(json, "OpenFootball World Cup 2026", applyResults: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenFootball World Cup sync failed.");
            return Result<KeoBiaImportResultDto>.Failure($"Không đồng bộ được OpenFootball: {ex.Message}");
        }
    }
}
