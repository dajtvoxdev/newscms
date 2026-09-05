using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// Coverage for the star journal: spending a hope/devil star on an open bet, the win/lose
/// cup effect once a starred bet settles, and the devil-star AI-spread eligibility gate.
///
/// Open-for-betting window per <see cref="KeoBia.KeoBiaTestHarness"/>: the match must be
/// Scheduled with KickoffAt between now+20min (lock) and now+1day (open), so every match
/// here kicks off in ~6 hours.
/// </summary>
public sealed class StarJournalTests
{
    // Comfortably inside the betting window (opens at -1 day, locks at -20 minutes).
    private static readonly TimeSpan OpenKickoff = TimeSpan.FromHours(6);

    [Fact]
    public async Task SubmitBetWithHopeStar_WritesUsedHopeStarMarker_AndDecrementsHopeStars()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("alice", hopeStars: 1);
        var match = h.AddMatch(kickoffOffset: OpenKickoff, status: KeoBiaMatchStatus.Scheduled);
        await h.SaveAsync();

        // Act
        var result = await h.Service.SubmitBetByTelegramIdAsync(
            player.TelegramUserId!.Value, match.Id, KeoBiaBetChoice.Home, cups: 1, starType: "hope");

        // Assert
        Assert.True(result.Succeeded, result.Error);

        var used = h.CupLogs(KeoBiaCupChangeType.UsedHopeStar);
        var row = Assert.Single(used);
        Assert.Equal(player.Id, row.PlayerId);
        Assert.Equal(match.Id, row.MatchId);
        Assert.Equal(0, row.Cups);
        Assert.Equal(h.SiteId, row.SiteId);

        Assert.Equal(0, h.ReloadPlayer(player.Id).HopeStars);
    }

    [Fact]
    public async Task SettlingStarredBetWrong_WritesHopeStarLosePlusOne_Once()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("bob", hopeStars: 1);
        var match = h.AddMatch(kickoffOffset: OpenKickoff, status: KeoBiaMatchStatus.Scheduled);
        await h.SaveAsync();

        var bet = await h.Service.SubmitBetByTelegramIdAsync(
            player.TelegramUserId!.Value, match.Id, KeoBiaBetChoice.Home, cups: 1, starType: "hope");
        Assert.True(bet.Succeeded, bet.Error);

        // Act: Away wins -> the Home pick is wrong, so the hope star is lost.
        var settle = await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        Assert.True(settle.Succeeded, settle.Error);

        // Assert
        var lose = h.CupLogs(KeoBiaCupChangeType.HopeStarLose);
        var row = Assert.Single(lose);
        Assert.Equal(player.Id, row.PlayerId);
        Assert.Equal(match.Id, row.MatchId);
        Assert.Equal(1, row.Cups);
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.HopeStarWin));

        // Re-running the recompute must not duplicate the once-per-match star effect.
        var again = await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        Assert.True(again.Succeeded, again.Error);
        Assert.Single(h.CupLogs(KeoBiaCupChangeType.HopeStarLose));
    }

    [Fact]
    public async Task SettlingStarredBetCorrect_WritesHopeStarWinMinusOne_Once()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("carol", hopeStars: 1);
        var match = h.AddMatch(kickoffOffset: OpenKickoff, status: KeoBiaMatchStatus.Scheduled);
        await h.SaveAsync();

        var bet = await h.Service.SubmitBetByTelegramIdAsync(
            player.TelegramUserId!.Value, match.Id, KeoBiaBetChoice.Home, cups: 1, starType: "hope");
        Assert.True(bet.Succeeded, bet.Error);

        // Act: Home wins -> the Home pick is correct, so the hope star wins.
        var settle = await h.Service.UpdateResultAsync(match.Id, 2, 0, KeoBiaMatchStatus.Finished);
        Assert.True(settle.Succeeded, settle.Error);

        // Assert
        var win = h.CupLogs(KeoBiaCupChangeType.HopeStarWin);
        var row = Assert.Single(win);
        Assert.Equal(player.Id, row.PlayerId);
        Assert.Equal(match.Id, row.MatchId);
        Assert.Equal(-1, row.Cups);
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.HopeStarLose));

        // Idempotent: re-applying the same result does not append a second win row.
        var again = await h.Service.UpdateResultAsync(match.Id, 2, 0, KeoBiaMatchStatus.Finished);
        Assert.True(again.Succeeded, again.Error);
        Assert.Single(h.CupLogs(KeoBiaCupChangeType.HopeStarWin));
    }

    [Fact]
    public async Task SettlingStarredBetCorrect_ReducesDisplayedLostCups()
    {
        // Arrange: player already has one wrong bet (1 lost cup), then wins a Hope Star bet.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("display", hopeStars: 1);
        var lost = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1), status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away);
        h.AddBet(player, lost, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: true, isCorrect: false);
        var starred = h.AddMatch(kickoffOffset: OpenKickoff, status: KeoBiaMatchStatus.Scheduled);
        await h.SaveAsync();

        var bet = await h.Service.SubmitBetByTelegramIdAsync(
            player.TelegramUserId!.Value, starred.Id, KeoBiaBetChoice.Home, cups: 1, starType: "hope");
        Assert.True(bet.Succeeded, bet.Error);

        // Act: Home wins -> Hope Star writes -1 cup and displayed loss should drop from 1 to 0.
        var settle = await h.Service.UpdateResultAsync(starred.Id, 2, 0, KeoBiaMatchStatus.Finished);
        Assert.True(settle.Succeeded, settle.Error);

        // Assert
        Assert.Equal(-1, h.CupLogs(KeoBiaCupChangeType.HopeStarWin).Where(x => x.PlayerId == player.Id).Sum(x => x.Cups));
        var board = await h.Service.GetLossLeaderboardAsync();
        var row = Assert.Single(board, x => x.Id == player.Id);
        Assert.Equal(-1, row.StarCupEffect);
        Assert.Equal(0, row.LostCups);

        var history = await h.Service.GetPlayerHistoryAsync(player.PublicKey);
        Assert.True(history.Succeeded, history.Error);
        Assert.Equal(0, history.Value!.LostCups);
    }

    [Fact]
    public async Task SubmitBetWithDevilStar_OnBalancedMatch_WritesUsedDevilStarMarker()
    {
        // Arrange: near-equal AI spread (maxDiff = 0 <= 10) so the devil star is allowed.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("dave", devilStars: 1);
        var match = h.AddMatch(kickoffOffset: OpenKickoff, status: KeoBiaMatchStatus.Scheduled);
        match.AiHome = 33;
        match.AiDraw = 33;
        match.AiAway = 33;
        await h.SaveAsync();

        // Act
        var result = await h.Service.SubmitBetByTelegramIdAsync(
            player.TelegramUserId!.Value, match.Id, KeoBiaBetChoice.Draw, cups: 1, starType: "devil");

        // Assert
        Assert.True(result.Succeeded, result.Error);

        var used = h.CupLogs(KeoBiaCupChangeType.UsedDevilStar);
        var row = Assert.Single(used);
        Assert.Equal(player.Id, row.PlayerId);
        Assert.Equal(match.Id, row.MatchId);
        Assert.Equal(0, row.Cups);
        Assert.Equal(h.SiteId, row.SiteId);

        Assert.Equal(0, h.ReloadPlayer(player.Id).DevilStars);
    }

    [Fact]
    public async Task SubmitBetWithDevilStar_OnLopsidedMatch_Fails_AndWritesNoMarker()
    {
        // Arrange: lopsided AI spread (maxDiff = 80 > 10) so the devil star is rejected.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("erin", devilStars: 1);
        var match = h.AddMatch(kickoffOffset: OpenKickoff, status: KeoBiaMatchStatus.Scheduled);
        match.AiHome = 80;
        match.AiDraw = 0;
        match.AiAway = 0;
        await h.SaveAsync();

        // Act
        var result = await h.Service.SubmitBetByTelegramIdAsync(
            player.TelegramUserId!.Value, match.Id, KeoBiaBetChoice.Home, cups: 1, starType: "devil");

        // Assert: rejected with a spread message, no marker written, star not spent.
        Assert.False(result.Succeeded);
        Assert.Contains("chênh", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.UsedDevilStar));
        Assert.Equal(1, h.ReloadPlayer(player.Id).DevilStars);
    }
}
