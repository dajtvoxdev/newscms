using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// Covers the missed_match journal: accrual for verified absentees, the block/unblock
/// reversal cycle, cancellation reversal, and the fact that a bettor never accrues a
/// missed_match row for a match they bet on. Every recompute is driven through
/// <see cref="KeoBiaService.UpdateResultAsync"/> (which recalcs all verified players)
/// or <see cref="KeoBiaService.TogglePlayerBlockAsync"/>.
/// </summary>
public sealed class MissedMatchAndBlockTests
{
    [Fact]
    public async Task TwoKickedOffFinishedMatches_AbsentVerifiedPlayer_JournalsTwoMissedMatchRows()
    {
        using var h = new KeoBiaTestHarness();
        var absentee = h.AddPlayer("absentee");
        var matchA = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-2));
        var matchB = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        await h.SaveAsync();

        // Finishing either match recalcs every verified player, journaling the absences.
        await h.Service.UpdateResultAsync(matchA.Id, 1, 0, KeoBiaMatchStatus.Finished);
        await h.Service.UpdateResultAsync(matchB.Id, 1, 0, KeoBiaMatchStatus.Finished);

        var missed = h.CupLogs(KeoBiaCupChangeType.MissedMatch);
        Assert.Equal(2, missed.Count);
        Assert.All(missed, row => Assert.Equal(absentee.Id, row.PlayerId));
        Assert.All(missed, row => Assert.Equal(1, row.Cups));
        Assert.Equal(new[] { matchA.Id, matchB.Id }.OrderBy(x => x),
            missed.Select(x => x.MatchId!.Value).OrderBy(x => x));
        Assert.Equal(2, h.NetCups(KeoBiaCupChangeType.MissedMatch));
    }

    [Fact]
    public async Task BlockingPlayer_AppendsReversalEntries_DrivingMissedMatchNetToZero()
    {
        using var h = new KeoBiaTestHarness();
        var absentee = h.AddPlayer("absentee");
        var matchA = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-2));
        var matchB = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        await h.SaveAsync();

        await h.Service.UpdateResultAsync(matchA.Id, 1, 0, KeoBiaMatchStatus.Finished);
        await h.Service.UpdateResultAsync(matchB.Id, 1, 0, KeoBiaMatchStatus.Finished);
        Assert.Equal(2, h.NetCups(KeoBiaCupChangeType.MissedMatch));

        // Blocked players are exempt from the missed-match penalty -> -1 reversal per match.
        var result = await h.Service.TogglePlayerBlockAsync(absentee.Id, true);
        Assert.True(result.Succeeded, result.Error);

        Assert.Equal(0, h.NetCups(KeoBiaCupChangeType.MissedMatch));
        // Reversals are appended, not deleted: the original +1 rows plus the -1 reversals remain.
        Assert.Equal(4, h.CupLogs(KeoBiaCupChangeType.MissedMatch).Count);
        Assert.True(h.ReloadPlayer(absentee.Id).IsBlocked);
    }

    [Fact]
    public async Task UnblockingPlayer_ReAccruesMissedMatch_NetReturnsToPositive()
    {
        using var h = new KeoBiaTestHarness();
        var absentee = h.AddPlayer("absentee");
        var matchA = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-2));
        var matchB = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        await h.SaveAsync();

        await h.Service.UpdateResultAsync(matchA.Id, 1, 0, KeoBiaMatchStatus.Finished);
        await h.Service.UpdateResultAsync(matchB.Id, 1, 0, KeoBiaMatchStatus.Finished);

        await h.Service.TogglePlayerBlockAsync(absentee.Id, true);
        Assert.Equal(0, h.NetCups(KeoBiaCupChangeType.MissedMatch));

        // Unblocking restores the penalty -> +1 re-accrual per match, NET back to 2.
        var result = await h.Service.TogglePlayerBlockAsync(absentee.Id, false);
        Assert.True(result.Succeeded, result.Error);

        Assert.Equal(2, h.NetCups(KeoBiaCupChangeType.MissedMatch));
        Assert.False(h.ReloadPlayer(absentee.Id).IsBlocked);
    }

    [Fact]
    public async Task AdminBlockingPlayer_StopsPlayer_AndRemovesFutureBets()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("admin-blocked-player");
        var futureMatch = h.AddMatch(kickoffOffset: TimeSpan.FromHours(2));
        h.AddBet(player, futureMatch);
        await h.SaveAsync();

        var result = await h.Service.StopPlayerAsync(player.Id);

        Assert.True(result.Succeeded, result.Error);
        var reloaded = h.ReloadPlayer(player.Id);
        Assert.True(reloaded.IsBlocked);
        Assert.NotNull(reloaded.StoppedPlayingAt);
        using var db = h.NewContext();
        Assert.DoesNotContain(db.KeoBiaBets, x => x.PlayerId == player.Id && x.MatchId == futureMatch.Id);
    }

    [Fact]
    public async Task CancellingAMissedMatch_DrivesThatMatchMissedNetToZero()
    {
        using var h = new KeoBiaTestHarness();
        var absentee = h.AddPlayer("absentee");
        var matchA = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-2));
        var matchB = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        await h.SaveAsync();

        await h.Service.UpdateResultAsync(matchA.Id, 1, 0, KeoBiaMatchStatus.Finished);
        await h.Service.UpdateResultAsync(matchB.Id, 1, 0, KeoBiaMatchStatus.Finished);
        Assert.Equal(2, h.NetCups(KeoBiaCupChangeType.MissedMatch));

        // Cancelling a match makes it ineligible for the missed-match penalty -> -1 reversal.
        var result = await h.Service.UpdateResultAsync(matchA.Id, null, null, KeoBiaMatchStatus.Cancelled);
        Assert.True(result.Succeeded, result.Error);

        var missed = h.CupLogs(KeoBiaCupChangeType.MissedMatch);
        Assert.Equal(0, missed.Where(x => x.MatchId == matchA.Id).Sum(x => x.Cups));
        Assert.Equal(1, missed.Where(x => x.MatchId == matchB.Id).Sum(x => x.Cups));
        Assert.Equal(1, h.NetCups(KeoBiaCupChangeType.MissedMatch));
    }

    [Fact]
    public async Task PlayerWhoBetOnMatch_HasNoMissedMatchRowForThatMatch()
    {
        using var h = new KeoBiaTestHarness();
        var bettor = h.AddPlayer("bettor");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        h.AddBet(bettor, match, choice: KeoBiaBetChoice.Home, isSettled: false);
        await h.SaveAsync();

        await h.Service.UpdateResultAsync(match.Id, 1, 0, KeoBiaMatchStatus.Finished);

        var missed = h.CupLogs(KeoBiaCupChangeType.MissedMatch);
        Assert.DoesNotContain(missed, x => x.PlayerId == bettor.Id);
        Assert.DoesNotContain(missed, x => x.MatchId == match.Id);
    }
}
