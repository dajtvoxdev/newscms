using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// Coverage for <c>KeoBiaService.ShareBeerAsync</c>: the giver/receiver journal pair
/// (shared_beer / received_beer), penalty reduction on the receiver, and the guard rails
/// (self-gift, unknown keys, out-of-range cup counts) that must fail without writing logs.
/// </summary>
public sealed class ShareBeerJournalTests
{
    [Fact]
    public async Task ShareBeer_WritesGiverSharedBeerAndReceiverReceivedBeer_AndBumpsSharedCups()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var giver = h.AddPlayer("giver");
        var receiver = h.AddPlayer("receiver");
        await h.SaveAsync();

        // Act
        var result = await h.Service.ShareBeerAsync(
            new KeoBiaShareBeerDto(giver.PublicKey, receiver.PublicKey, Cups: 3));

        // Assert
        Assert.True(result.Succeeded, result.Error);

        var shared = h.CupLogs(KeoBiaCupChangeType.SharedBeer);
        var sharedRow = Assert.Single(shared);
        Assert.Equal(giver.Id, sharedRow.PlayerId);
        Assert.Equal(3, sharedRow.Cups);
        Assert.Equal(giver.SiteId, sharedRow.SiteId);
        Assert.NotEqual(Guid.Empty, sharedRow.SiteId);
        // SharedCups (3) folds into the snapshot, so a real, non-zero balance is stamped.
        Assert.NotEqual(0, sharedRow.BalanceAfter);

        var received = h.CupLogs(KeoBiaCupChangeType.ReceivedBeer);
        var receivedRow = Assert.Single(received);
        Assert.Equal(receiver.Id, receivedRow.PlayerId);
        Assert.Equal(-3, receivedRow.Cups);
        Assert.Equal(receiver.SiteId, receivedRow.SiteId);

        // The giver's SharedCups grows by the gifted amount.
        Assert.Equal(3, h.ReloadPlayer(giver.Id).SharedCups);
    }

    [Fact]
    public async Task ShareBeer_ReducesReceiverPenaltyCups_ByMinOfPenaltyAndGift()
    {
        // Arrange: give the receiver a penalty by making them miss a finished, kicked-off match.
        using var h = new KeoBiaTestHarness();
        var giver = h.AddPlayer("giver");
        var receiver = h.AddPlayer("receiver");
        var bettor = h.AddPlayer("bettor");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        // A bettor anchors the match so UpdateResultAsync has someone to settle while it also
        // recomputes the absentee receiver, accruing exactly one missed-match penalty cup.
        h.AddBet(bettor, match, choice: KeoBiaBetChoice.Home, isSettled: false);
        await h.SaveAsync();

        await h.Service.UpdateResultAsync(match.Id, 1, 0, KeoBiaMatchStatus.Finished);
        Assert.Equal(1, h.ReloadPlayer(receiver.Id).PenaltyCups);

        // Act: gift 5 cups; only min(penalty=1, 5)=1 is forgiven.
        var result = await h.Service.ShareBeerAsync(
            new KeoBiaShareBeerDto(giver.PublicKey, receiver.PublicKey, Cups: 5));

        // Assert
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(0, h.ReloadPlayer(receiver.Id).PenaltyCups);

        var receivedRow = Assert.Single(h.CupLogs(KeoBiaCupChangeType.ReceivedBeer));
        Assert.Equal(-5, receivedRow.Cups);
    }

    [Fact]
    public async Task ShareBeer_ToSelf_Fails_AndWritesNoLogs()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("solo");
        await h.SaveAsync();

        // Act
        var result = await h.Service.ShareBeerAsync(
            new KeoBiaShareBeerDto(player.PublicKey, player.PublicKey, Cups: 1));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.SharedBeer));
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.ReceivedBeer));
        Assert.Empty(h.AllCupLogsUnfiltered());
    }

    [Fact]
    public async Task ShareBeer_WithUnknownKeys_Fails_AndWritesNoLogs()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var giver = h.AddPlayer("giver");
        await h.SaveAsync();

        // Act: receiver key does not match any player.
        var result = await h.Service.ShareBeerAsync(
            new KeoBiaShareBeerDto(giver.PublicKey, "pk-does-not-exist", Cups: 2));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Empty(h.AllCupLogsUnfiltered());
    }

    [Fact]
    public async Task ShareBeer_WithCupsBelowRange_Fails_AndWritesNoLogs()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var giver = h.AddPlayer("giver");
        var receiver = h.AddPlayer("receiver");
        await h.SaveAsync();

        // Act
        var result = await h.Service.ShareBeerAsync(
            new KeoBiaShareBeerDto(giver.PublicKey, receiver.PublicKey, Cups: 0));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Empty(h.AllCupLogsUnfiltered());
    }

    [Fact]
    public async Task ShareBeer_WithCupsAboveRange_Fails_AndWritesNoLogs()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var giver = h.AddPlayer("giver");
        var receiver = h.AddPlayer("receiver");
        await h.SaveAsync();

        // Act
        var result = await h.Service.ShareBeerAsync(
            new KeoBiaShareBeerDto(giver.PublicKey, receiver.PublicKey, Cups: 100));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Empty(h.AllCupLogsUnfiltered());
    }
}
