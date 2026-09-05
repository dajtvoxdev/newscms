using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// Coverage for the wrong_bet reconcile/reversal path in <see cref="NewsCMS.Infrastructure.KeoBia.KeoBiaService"/>.
/// A wrong_bet log is append-only and reconciled by delta per (player, match): settling a bet
/// wrong writes +1; making it no longer wrong (flipping the result so the pick becomes correct, or
/// un-finishing the match) appends a -1 reversal so the NET cups for that match return to 0.
/// </summary>
public sealed class ReconcileReversalTests
{
    [Fact]
    public async Task FlippingResultSoPickBecomesCorrect_AppendsReversal_NetsWrongBetToZero()
    {
        // Arrange: player bets Home; match kicked off already.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("alice");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        await h.SaveAsync();

        // Act: Away wins -> Home pick wrong (+1); then flip to Home wins -> pick correct (-1 reversal).
        var wrongResult = await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        Assert.True(wrongResult.Succeeded, wrongResult.Error);
        Assert.Equal(1, h.NetCups(KeoBiaCupChangeType.WrongBet));

        var flipResult = await h.Service.UpdateResultAsync(match.Id, 1, 0, KeoBiaMatchStatus.Finished);
        Assert.True(flipResult.Succeeded, flipResult.Error);

        // Assert: net wrong_bet cups for that (player, match) reconciled back to 0 via a -1 reversal row.
        var wrongLogs = h.CupLogs(KeoBiaCupChangeType.WrongBet)
            .Where(x => x.PlayerId == player.Id && x.MatchId == match.Id)
            .ToList();
        Assert.Equal(0, wrongLogs.Sum(x => x.Cups));
        Assert.Contains(wrongLogs, x => x.Cups == 1);
        Assert.Contains(wrongLogs, x => x.Cups == -1);

        // Player stat reflects the now-correct pick: no wrong bets remain.
        Assert.Equal(0, h.ReloadPlayer(player.Id).WrongBets);
    }

    [Fact]
    public async Task UnFinishingMatch_AppendsReversal_NetsWrongBetToZero_AndUnsettlesBet()
    {
        // Arrange: player bets Home; match kicked off already.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("bob");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        await h.SaveAsync();

        // Act: Away wins -> Home pick wrong (+1); then un-finish by moving back to Scheduled.
        var wrongResult = await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        Assert.True(wrongResult.Succeeded, wrongResult.Error);
        Assert.Equal(1, h.NetCups(KeoBiaCupChangeType.WrongBet));

        var unFinishResult = await h.Service.UpdateResultAsync(match.Id, null, null, KeoBiaMatchStatus.Scheduled);
        Assert.True(unFinishResult.Succeeded, unFinishResult.Error);

        // Assert: un-finishing clears ResultChoice, so the bet is no longer wrong -> -1 reversal, net 0.
        var wrongLogs = h.CupLogs(KeoBiaCupChangeType.WrongBet)
            .Where(x => x.PlayerId == player.Id && x.MatchId == match.Id)
            .ToList();
        Assert.Equal(0, wrongLogs.Sum(x => x.Cups));
        Assert.Contains(wrongLogs, x => x.Cups == -1);

        // The bet is unsettled again and no longer counts as a wrong bet.
        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(0, reloaded.WrongBets);
    }

    [Fact]
    public async Task FlippingResult_ReversesOnlyTheNewlyCorrectPlayer_OtherStaysWrong()
    {
        // Arrange: two players bet on the same match, both picks losing on the first result.
        using var h = new KeoBiaTestHarness();
        var homePicker = h.AddPlayer("home-picker");
        var drawPicker = h.AddPlayer("draw-picker");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        h.AddBet(homePicker, match, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        h.AddBet(drawPicker, match, choice: KeoBiaBetChoice.Draw, cups: 1, isSettled: false);
        await h.SaveAsync();

        // Act: Away wins (0-1) -> both Home and Draw picks are wrong -> two +1 rows.
        var settle = await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        Assert.True(settle.Succeeded, settle.Error);

        var afterSettleHome = h.CupLogs(KeoBiaCupChangeType.WrongBet).Where(x => x.PlayerId == homePicker.Id).Sum(x => x.Cups);
        var afterSettleDraw = h.CupLogs(KeoBiaCupChangeType.WrongBet).Where(x => x.PlayerId == drawPicker.Id).Sum(x => x.Cups);
        Assert.Equal(1, afterSettleHome);
        Assert.Equal(1, afterSettleDraw);

        // Flip to Home wins (1-0) -> Home pick becomes correct, Draw pick still wrong.
        var flip = await h.Service.UpdateResultAsync(match.Id, 1, 0, KeoBiaMatchStatus.Finished);
        Assert.True(flip.Succeeded, flip.Error);

        // Assert: the home picker gets a -1 reversal (net 0); the draw picker keeps the single +1.
        var homeLogs = h.CupLogs(KeoBiaCupChangeType.WrongBet).Where(x => x.PlayerId == homePicker.Id).ToList();
        Assert.Equal(0, homeLogs.Sum(x => x.Cups));
        Assert.Contains(homeLogs, x => x.Cups == -1);

        var drawLogs = h.CupLogs(KeoBiaCupChangeType.WrongBet).Where(x => x.PlayerId == drawPicker.Id).ToList();
        Assert.Equal(1, drawLogs.Sum(x => x.Cups));
        Assert.Equal(1, Assert.Single(drawLogs).Cups);

        Assert.Equal(0, h.ReloadPlayer(homePicker.Id).WrongBets);
        Assert.Equal(1, h.ReloadPlayer(drawPicker.Id).WrongBets);
    }
}
