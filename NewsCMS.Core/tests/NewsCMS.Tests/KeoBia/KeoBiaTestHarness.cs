using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NewsCMS.Application.KeoBia;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.KeoBia;
using NewsCMS.Infrastructure.KeoBia;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// In-memory harness for exercising <see cref="KeoBiaService"/> end to end against a real
/// <see cref="AppDbContext"/> (EF Core InMemory provider) with a controllable site context.
///
/// Usage:
///   using var h = new KeoBiaTestHarness();          // one isolated DB per harness
///   var p = h.AddPlayer("alice");                    // seed entities (auto-stamped to h.SiteId)
///   var m = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
///   await h.SaveAsync();
///   await h.Service.UpdateResultAsync(m.Id, 1, 0, KeoBiaMatchStatus.Finished);
///   var logs = h.CupLogs();                           // fresh read of all cup logs (this site)
///
/// Every seeded entity gets <see cref="SiteId"/> unless you override it, so the global site
/// query filter sees them. Use <see cref="NewContext"/> for assertions to avoid identity-map
/// staleness; the InMemory store is shared across contexts via a single root.
/// </summary>
public sealed class KeoBiaTestHarness : IDisposable
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _dbName = "keobia-" + Guid.NewGuid().ToString("N");
    private readonly List<AppDbContext> _contexts = new();

    public FakeCurrentSite Site { get; }
    public Guid SiteId => Site.SiteId;
    public AppDbContext Db { get; }
    public KeoBiaService Service { get; }

    public KeoBiaTestHarness(
        Guid? siteId = null,
        IEnumerable<KeyValuePair<string, string?>>? configuration = null,
        KeoBiaTelegramOptions? telegramOptions = null)
    {
        Site = new FakeCurrentSite(siteId ?? Guid.NewGuid());
        Db = NewContext();
        var configBuilder = new ConfigurationBuilder();
        if (configuration is not null)
            configBuilder.AddInMemoryCollection(configuration);
        Service = new KeoBiaService(
            Db,
            new NullFileStorage(),
            Options.Create(telegramOptions ?? new KeoBiaTelegramOptions()),
            new TestHttpClientFactory(),
            configBuilder.Build(),
            currentSite: Site);
    }

    /// <summary>Opens an additional context on the same shared InMemory store (for assertions).</summary>
    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName, _root)
            .ConfigureWarnings(w => w
                .Ignore(InMemoryEventId.TransactionIgnoredWarning)
                // Each harness builds its own provider; across the whole suite that trips EF's
                // >20-providers perf advisory. Harmless in tests — suppress so it isn't thrown.
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .EnableSensitiveDataLogging()
            .Options;
        var ctx = new AppDbContext(options, Site);
        _contexts.Add(ctx);
        return ctx;
    }

    // ---- Seed helpers (added to the service's context; call SaveAsync to persist) ----

    public KeoBiaPlayer AddPlayer(
        string name,
        long? telegramUserId = 1,
        int hopeStars = 0,
        int devilStars = 0,
        int missedMatchCredit = 0,
        bool isBlocked = false,
        Guid? siteId = null,
        DateTime? createdAt = null)
    {
        var player = new KeoBiaPlayer
        {
            SiteId = siteId ?? SiteId,
            PublicKey = "pk-" + Guid.NewGuid().ToString("N")[..8],
            DisplayName = name,
            TelegramUserId = telegramUserId,
            TelegramVerifiedAt = telegramUserId.HasValue ? DateTime.UtcNow : null,
            HopeStars = hopeStars,
            DevilStars = devilStars,
            MissedMatchCredit = missedMatchCredit,
            IsBlocked = isBlocked,
            CreatedAt = createdAt ?? KeoBiaUnitRules.PeanutSwitchAtUtc.AddTicks(-1),
        };
        Db.KeoBiaPlayers.Add(player);
        return player;
    }

    public KeoBiaMatch AddMatch(
        TimeSpan? kickoffOffset = null,
        string status = KeoBiaMatchStatus.Scheduled,
        string? resultChoice = null,
        int? homeScore = null,
        int? awayScore = null,
        Guid? siteId = null,
        string stage = "Group",
        DateTime? kickoffAt = null)
    {
        var match = new KeoBiaMatch
        {
            SiteId = siteId ?? SiteId,
            ExternalId = "ext-" + Guid.NewGuid().ToString("N")[..8],
            Stage = stage,
            HomeName = "Home " + Guid.NewGuid().ToString("N")[..4],
            HomeCode = "HOM",
            AwayName = "Away " + Guid.NewGuid().ToString("N")[..4],
            AwayCode = "AWY",
            Venue = "Stadium",
            KickoffAt = kickoffAt ?? DateTime.UtcNow + (kickoffOffset ?? TimeSpan.FromHours(-1)),
            Status = status,
            ResultChoice = resultChoice,
            HomeScore = homeScore,
            AwayScore = awayScore,
        };
        Db.KeoBiaMatches.Add(match);
        return match;
    }

    public KeoBiaBet AddBet(
        KeoBiaPlayer player,
        KeoBiaMatch match,
        string choice = KeoBiaBetChoice.Home,
        int cups = 1,
        string? starType = null,
        bool isSettled = false,
        bool? isCorrect = null,
        Guid? siteId = null)
    {
        var bet = new KeoBiaBet
        {
            SiteId = siteId ?? SiteId,
            PlayerId = player.Id,
            MatchId = match.Id,
            Choice = choice,
            Cups = cups,
            StarType = starType,
            IsSettled = isSettled,
            IsCorrect = isCorrect,
            SettledAt = isSettled ? DateTime.UtcNow : null,
        };
        Db.KeoBiaBets.Add(bet);
        return bet;
    }

    public KeoBiaCupLog AddCupLogRaw(KeoBiaCupLog log)
    {
        if (log.SiteId == Guid.Empty) log.SiteId = SiteId;
        Db.KeoBiaCupLogs.Add(log);
        return log;
    }

    public Task SaveAsync() => Db.SaveChangesAsync();

    // ---- Assertion helpers (fresh read of the shared store) ----

    /// <summary>All cup logs for the current site, optionally filtered by change type.</summary>
    public List<KeoBiaCupLog> CupLogs(string? changeType = null)
    {
        using var ctx = NewContext();
        var q = ctx.KeoBiaCupLogs.AsNoTracking().AsQueryable();
        if (changeType is not null) q = q.Where(x => x.ChangeType == changeType);
        return q.ToList();
    }

    /// <summary>All cup logs across every site, ignoring the site query filter.</summary>
    public List<KeoBiaCupLog> AllCupLogsUnfiltered()
    {
        using var ctx = NewContext();
        return ctx.KeoBiaCupLogs.IgnoreQueryFilters().AsNoTracking().ToList();
    }

    /// <summary>Net cups logged for a given change type across all (player,match) rows.</summary>
    public int NetCups(string changeType) => CupLogs(changeType).Sum(x => x.Cups);

    public KeoBiaPlayer ReloadPlayer(Guid id)
    {
        using var ctx = NewContext();
        return ctx.KeoBiaPlayers.AsNoTracking().Single(x => x.Id == id);
    }

    public void Dispose()
    {
        foreach (var ctx in _contexts) ctx.Dispose();
    }
}

/// <summary>Settable <see cref="ICurrentSite"/> for tests.</summary>
public sealed class FakeCurrentSite : ICurrentSite
{
    public FakeCurrentSite(Guid siteId)
    {
        SiteId = siteId;
        IsResolved = siteId != Guid.Empty;
    }

    public Guid SiteId { get; private set; }
    public string Slug { get; private set; } = "test-site";
    public string Theme { get; private set; } = "test-theme";
    public bool IsResolved { get; private set; }

    public void Set(Guid siteId, string slug, string theme)
    {
        SiteId = siteId;
        Slug = slug ?? string.Empty;
        Theme = theme ?? string.Empty;
        IsResolved = true;
    }
}

/// <summary>No-op file storage; cup-log paths never touch it.</summary>
public sealed class NullFileStorage : IFileStorage
{
    public Task<string> SaveAsync(Stream content, string originalFileName, string subFolder = "general", CancellationToken ct = default)
        => Task.FromResult($"{subFolder}/test/{Guid.NewGuid():N}");
    public string GetPublicUrl(string key) => "/uploads/" + key;
    public string GetLocalPath(string key) => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ncms-test-uploads", key);
    public Task MoveToTrashAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    public Task RestoreFromTrashAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Returns an HttpClient whose requests fail fast; Telegram avatar fetches degrade to null.</summary>
public sealed class TestHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new FailingHandler());

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
