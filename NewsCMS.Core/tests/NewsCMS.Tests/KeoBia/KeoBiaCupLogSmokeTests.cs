using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// Smoke coverage proving the harness wires KeoBiaService to a real AppDbContext and that the
/// per-match journaling fundamentals hold: wrong bets and missed matches get logged, logs are
/// site-stamped, and re-running the recompute is idempotent.
/// </summary>
public sealed class KeoBiaCupLogSmokeTests
{
    [Fact]
    public async Task SettlingWrongBet_WritesWrongBetCupLog_StampedToSite()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("alice");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        await h.SaveAsync();

        // Away wins -> Home pick is wrong.
        var result = await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        Assert.True(result.Succeeded, result.Error);

        var wrong = h.CupLogs(KeoBiaCupChangeType.WrongBet);
        var row = Assert.Single(wrong);
        Assert.Equal(player.Id, row.PlayerId);
        Assert.Equal(match.Id, row.MatchId);
        Assert.Equal(1, row.Cups);
        Assert.Equal(h.SiteId, row.SiteId);
        Assert.NotEqual(Guid.Empty, row.SiteId);
    }

    [Fact]
    public async Task RecalculatingTwice_IsIdempotent_NoDuplicateLogs()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("bob");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, isSettled: false);
        await h.SaveAsync();

        await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        var afterFirst = h.CupLogs(KeoBiaCupChangeType.WrongBet).Count;

        // Re-applying the same result must not append another wrong_bet row.
        await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        var afterSecond = h.CupLogs(KeoBiaCupChangeType.WrongBet).Count;

        Assert.Equal(1, afterFirst);
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task FinishedMatchWithoutBet_JournalsMissedMatchForVerifiedPlayer()
    {
        using var h = new KeoBiaTestHarness();
        var bettor = h.AddPlayer("better");
        var absentee = h.AddPlayer("absent");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        h.AddBet(bettor, match, choice: KeoBiaBetChoice.Home, isSettled: false);
        await h.SaveAsync();

        // UpdateResult recalculates all verified players, so the absentee accrues a missed match.
        await h.Service.UpdateResultAsync(match.Id, 1, 0, KeoBiaMatchStatus.Finished);

        var missed = h.CupLogs(KeoBiaCupChangeType.MissedMatch);
        var row = Assert.Single(missed);
        Assert.Equal(absentee.Id, row.PlayerId);
        Assert.Equal(match.Id, row.MatchId);
        Assert.Equal(1, row.Cups);
    }
}
