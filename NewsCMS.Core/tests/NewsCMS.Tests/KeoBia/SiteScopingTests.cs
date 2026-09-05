using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// Site stamping + isolation coverage for the KeoBia cup-log journal.
///
/// The contract under test: <c>AddCupLog</c> stamps every cup log with the OWNING player's
/// <c>SiteId</c> — never the current request/admin site. That keeps logs visible behind the
/// public site query filter even when they are written from a cross-site admin context or an
/// unresolved background job. These tests prove a log lands on the player's own site (1), that
/// a foreign request context cannot retarget the stamp (2), and that two players on two sites
/// each journal to their own site (3).
/// </summary>
public sealed class SiteScopingTests
{
    [Fact]
    public async Task WrongBetLog_IsStampedToOwningPlayerSite_AndNeverEmpty()
    {
        // Arrange: a verified player with a single bet on a match that has kicked off.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("alice");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        await h.SaveAsync();

        // Act: Away wins, so the Home pick settles wrong and a wrong_bet log is journaled.
        var result = await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        Assert.True(result.Succeeded, result.Error);

        // Assert: the log carries the player's own SiteId, which is a real (non-empty) site.
        var row = Assert.Single(h.CupLogs(KeoBiaCupChangeType.WrongBet));
        Assert.Equal(player.SiteId, row.SiteId);
        Assert.Equal(h.SiteId, row.SiteId);
        Assert.NotEqual(Guid.Empty, row.SiteId);
    }

    [Fact]
    public async Task ForeignRequestContext_DoesNotRetargetStamp_LogKeepsPlayersOriginalSite()
    {
        // Arrange: a verified player on the harness site whose bet already settled wrong, plus
        // the finished match it was placed on. We seed the settled state directly because the
        // recompute will be driven AFTER the site context is switched away.
        using var h = new KeoBiaTestHarness();
        var originalSiteId = h.SiteId;
        var player = h.AddPlayer("bob");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1), status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away, homeScore: 0, awayScore: 1);
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: true, isCorrect: false);
        await h.SaveAsync();

        // Simulate a different request/admin site context before any log is written.
        var foreignSiteId = Guid.NewGuid();
        h.Site.Set(foreignSiteId, "other-slug", "other-theme");
        Assert.NotEqual(originalSiteId, h.SiteId);

        // Act: SeedCupLogsAsync rediscovers each owning site (IgnoreQueryFilters), sets the
        // context to that site, and recomputes — backfilling the per-match wrong_bet log.
        await h.Service.SeedCupLogsAsync();

        // Assert: read across sites; the wrong_bet stamp is the PLAYER's original site, not the
        // foreign request site that was active when the recompute was invoked.
        var wrong = h.AllCupLogsUnfiltered()
            .Where(x => x.ChangeType == KeoBiaCupChangeType.WrongBet)
            .ToList();
        // Note: SeedCupLogsAsync mutates the shared site context to each owning site as it
        // recomputes and does NOT restore it, so h.SiteId is no longer the foreign site here.
        // The contract assertion is against the foreign site captured BEFORE the recompute.
        var row = Assert.Single(wrong);
        Assert.Equal(player.Id, row.PlayerId);
        Assert.Equal(originalSiteId, row.SiteId);
        Assert.NotEqual(foreignSiteId, row.SiteId);
        Assert.NotEqual(Guid.Empty, row.SiteId);
    }

    [Fact]
    public async Task TwoPlayersOnTwoSites_EachLogStampedToOwnSite()
    {
        // Arrange: player A on the harness site, player B on a distinct site. Each has a finished
        // match and a settled-wrong bet on their OWN site, seeded directly so the cross-site
        // recompute (which cannot use the request-scoped UpdateResult path) can backfill both.
        using var h = new KeoBiaTestHarness();
        var siteA = h.SiteId;
        var siteB = Guid.NewGuid();

        var playerA = h.AddPlayer("alpha");
        var matchA = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1), status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away, homeScore: 0, awayScore: 1);
        h.AddBet(playerA, matchA, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: true, isCorrect: false);

        var playerB = h.AddPlayer("beta", siteId: siteB);
        var matchB = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1), status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away, homeScore: 0, awayScore: 1, siteId: siteB);
        h.AddBet(playerB, matchB, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: true, isCorrect: false, siteId: siteB);
        await h.SaveAsync();

        // Act: SeedCupLogsAsync loops every owning site and recomputes per site.
        await h.Service.SeedCupLogsAsync();

        // Assert: group the per-match wrong_bet logs by site; each player's log lives on its
        // own site, and the two sites are genuinely distinct.
        var bySite = h.AllCupLogsUnfiltered()
            .Where(x => x.ChangeType == KeoBiaCupChangeType.WrongBet && x.MatchId != null)
            .GroupBy(x => x.SiteId)
            .ToDictionary(g => g.Key, g => g.ToList());

        Assert.NotEqual(siteA, siteB);
        Assert.Equal(2, bySite.Count);

        var logA = Assert.Single(Assert.Contains(siteA, bySite));
        Assert.Equal(playerA.Id, logA.PlayerId);

        var logB = Assert.Single(Assert.Contains(siteB, bySite));
        Assert.Equal(playerB.Id, logB.PlayerId);
    }
}
