using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.VideoStudio;

namespace NewsCMS.Tests.VideoStudio;

/// <summary>
/// NewsCMS nói chuyện với một AdVideo giả: DB InMemory, DataProtection tạm, và một handler HTTP ghi
/// lại mọi request để test soi header, đường dẫn, thân.
/// </summary>
public sealed class AdVideoTestHarness : IDisposable
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _dbName = "advideo-" + Guid.NewGuid().ToString("N");

    public AdVideoTestHarness()
    {
        Site = new TestSite(Guid.NewGuid());
        DataProtection = new EphemeralDataProtectionProvider();
        Http = new RecordingHandler();
        Db = NewContext();
        Keys = new AdVideoKeyStore(Db, DataProtection);
        Admin = new AdVideoAdminClient(new HandlerFactory(Http), Keys);
        Client = new AdVideoClient(new HandlerFactory(Http), Keys, Site);
        Connection = new AdVideoConnectionService(Db, Keys, Admin, new HandlerFactory(Http), Site);
    }

    public TestSite Site { get; }
    public IDataProtectionProvider DataProtection { get; }
    public RecordingHandler Http { get; }
    public AppDbContext Db { get; }
    public AdVideoKeyStore Keys { get; }
    public AdVideoAdminClient Admin { get; }
    public AdVideoClient Client { get; }
    public AdVideoConnectionService Connection { get; }

    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName, _root)
            .ConfigureWarnings(w => w
                .Ignore(InMemoryEventId.TransactionIgnoredWarning)
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        return new AppDbContext(options, Site);
    }

    /// <summary>Thêm một site vào DB (bảng Sites không site-scoped).</summary>
    public async Task<Guid> AddSiteAsync(string name, Guid? id = null)
    {
        var site = new Site { Id = id ?? Guid.NewGuid(), Name = name, Slug = name.ToLowerInvariant().Replace(' ', '-') };
        Db.Sites.Add(site);
        await Db.SaveChangesAsync();

        return site.Id;
    }

    public void Dispose() => Db.Dispose();

    public sealed class TestSite(Guid siteId) : ICurrentSite
    {
        public Guid SiteId { get; private set; } = siteId;
        public string Slug { get; private set; } = "test";
        public string Theme { get; private set; } = "Default";
        public bool IsResolved => true;

        public void Set(Guid siteId, string slug, string theme)
        {
            SiteId = siteId;
            Slug = slug;
            Theme = theme;
        }
    }

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

/// <summary>Một request đã gửi, chép lại đủ để assert sau khi request gốc đã bị dispose.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body);

public sealed class RecordingHandler : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Trả lời theo request. Mặc định 404 cho mọi thứ.</summary>
    public Func<RecordedRequest, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, body);
        Requests.Add(recorded);

        return Respond(recorded);
    }
}
