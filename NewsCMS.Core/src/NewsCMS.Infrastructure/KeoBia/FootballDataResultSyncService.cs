using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.Common;
using NewsCMS.Application.KeoBia;

namespace NewsCMS.Infrastructure.KeoBia;

// Pulls the full World Cup fixture + results feed from football-data.org (v4) and hands
// the raw JSON to KeoBiaService, which reconciles it against existing matches and settles
// bia. This is the single source of truth for both schedule (incl. knockout teams that
// resolve only after the group stage) and match results.
public sealed class FootballDataResultSyncService : IKeoBiaFootballDataSyncService
{
    public const string HttpClientName = "keobia-footballdata";
    public const string DefaultBaseUrl = "https://api.football-data.org/v4";
    public const string DefaultCompetition = "WC";

    private const string ETagCacheKey = "keobia:footballdata:worldcup2026:etag";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly IKeoBiaService _keoBia;
    private readonly ILogger<FootballDataResultSyncService> _logger;

    public FootballDataResultSyncService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IMemoryCache cache,
        IKeoBiaService keoBia,
        ILogger<FootballDataResultSyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _cache = cache;
        _keoBia = keoBia;
        _logger = logger;
    }

    public async Task<Result<KeoBiaImportResultDto>> SyncAsync(bool force = false, CancellationToken ct = default)
    {
        var token = _configuration["KeoBia:FootballData:ApiToken"];
        if (string.IsNullOrWhiteSpace(token))
            return Result<KeoBiaImportResultDto>.Failure("Chưa cấu hình KeoBia:FootballData:ApiToken.");

        var baseUrl = _configuration["KeoBia:FootballData:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = DefaultBaseUrl;
        var competition = _configuration["KeoBia:FootballData:Competition"];
        if (string.IsNullOrWhiteSpace(competition)) competition = DefaultCompetition;

        var url = $"{baseUrl.TrimEnd('/')}/competitions/{competition.Trim()}/matches";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("X-Auth-Token", token.Trim());

            if (!force
                && _cache.TryGetValue<string>(ETagCacheKey, out var etag)
                && !string.IsNullOrWhiteSpace(etag)
                && EntityTagHeaderValue.TryParse(etag, out var tag))
            {
                request.Headers.IfNoneMatch.Add(tag);
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return Result<KeoBiaImportResultDto>.Success(new KeoBiaImportResultDto(
                    0, 0, 0, 0, "Nguồn Football-Data chưa thay đổi."));
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return Result<KeoBiaImportResultDto>.Failure("Football-Data giới hạn tần suất (HTTP 429), thử lại sau.");

            if (!response.IsSuccessStatusCode)
            {
                return Result<KeoBiaImportResultDto>.Failure(
                    $"Football-Data trả về HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (response.Headers.ETag is not null)
                _cache.Set(ETagCacheKey, response.Headers.ETag.ToString(), TimeSpan.FromDays(1));

            var json = await response.Content.ReadAsStringAsync(ct);
            return await _keoBia.SyncFootballDataAsync(json, "Football-Data World Cup 2026", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Football-Data World Cup sync failed.");
            return Result<KeoBiaImportResultDto>.Failure($"Không đồng bộ được Football-Data: {ex.Message}");
        }
    }
}
