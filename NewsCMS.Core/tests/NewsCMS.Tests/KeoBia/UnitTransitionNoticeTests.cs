using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

public sealed class UnitTransitionNoticeTests
{
    [Fact]
    public async Task Continue_AcknowledgesNotice_AndPlayerCanStopLater()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("continue-player");
        await h.SaveAsync();

        var before = await h.Service.GetUnitTransitionNoticeAsync(player.PublicKey);
        var decision = await h.Service.RespondUnitTransitionNoticeAsync(player.PublicKey, false);
        var after = await h.Service.GetUnitTransitionNoticeAsync(player.PublicKey);
        var repeatedStop = await h.Service.RespondUnitTransitionNoticeAsync(player.PublicKey, true);

        Assert.True(before.Succeeded);
        Assert.True(before.Value!.ShouldShow);
        Assert.True(decision.Succeeded);
        Assert.False(decision.Value!.IsBlocked);
        Assert.False(after.Value!.ShouldShow);
        Assert.True(repeatedStop.Succeeded, repeatedStop.Error);
        Assert.True(repeatedStop.Value!.IsBlocked);
        Assert.True(h.ReloadPlayer(player.Id).IsBlocked);
    }

    [Fact]
    public async Task Stop_BlocksPlayer_RemovesFutureBets_AndKeepsEarlierMissDebt()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("stop-player");
        var pastMatch = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        var futureMatch = h.AddMatch(kickoffOffset: TimeSpan.FromHours(2));
        h.AddBet(player, futureMatch);
        await h.SaveAsync();

        Assert.True((await h.Service.UpdateResultAsync(
            pastMatch.Id,
            1,
            0,
            KeoBiaMatchStatus.Finished)).Succeeded);
        Assert.Equal(1, h.ReloadPlayer(player.Id).PenaltyCups);

        var decision = await h.Service.RespondUnitTransitionNoticeAsync(player.PublicKey, true);

        Assert.True(decision.Succeeded, decision.Error);
        Assert.True(decision.Value!.IsBlocked);
        var reloaded = h.ReloadPlayer(player.Id);
        Assert.True(reloaded.IsBlocked);
        Assert.NotNull(reloaded.StoppedPlayingAt);
        Assert.Equal(1, reloaded.PenaltyCups);
        using var db = h.NewContext();
        Assert.DoesNotContain(db.KeoBiaBets, x => x.PlayerId == player.Id && x.MatchId == futureMatch.Id);
        Assert.DoesNotContain(await h.Service.GetAllTelegramUserIdsAsync(), x => x == player.TelegramUserId);
    }

    [Fact]
    public async Task TelegramNotice_IsReturnedUntilMarkedSent()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("telegram-notice", telegramUserId: 20260710);
        await h.SaveAsync();

        Assert.Contains(20260710, await h.Service.GetPendingUnitTransitionTelegramUserIdsAsync());

        var marked = await h.Service.MarkUnitTransitionTelegramSentAsync(20260710);

        Assert.True(marked.Succeeded, marked.Error);
        Assert.DoesNotContain(20260710, await h.Service.GetPendingUnitTransitionTelegramUserIdsAsync());
    }
}
