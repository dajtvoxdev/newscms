using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// Coverage for admin star adjustments and the admin_adjust journal they produce:
/// UpdatePlayerStarsAsync writes one zero-cup admin_adjust row per changed star type (with the
/// old -> new transition in the reason) and nothing when the value is unchanged; BulkAddStarAsync
/// fans one admin_adjust row out to every verified player and rejects unknown star types without
/// touching state.
/// </summary>
public sealed class AdminAdjustTests
{
    [Fact]
    public async Task UpdatePlayerStarsAsync_ChangingHopeAndDevil_WritesAdminAdjustRowsAndUpdatesPlayer()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("alice", hopeStars: 1, devilStars: 0);
        await h.SaveAsync();

        // Act
        var result = await h.Service.UpdatePlayerStarsAsync(player.Id, hopeStars: 3, devilStars: 2);

        // Assert
        Assert.True(result.Succeeded, result.Error);

        var logs = h.CupLogs(KeoBiaCupChangeType.AdminAdjust);
        Assert.Equal(2, logs.Count);
        Assert.All(logs, row =>
        {
            Assert.Equal(0, row.Cups);
            Assert.Equal(player.Id, row.PlayerId);
            Assert.Null(row.MatchId);
            Assert.Equal(h.SiteId, row.SiteId);
        });

        var hopeLog = Assert.Single(logs, row => row.Reason != null && row.Reason.Contains("Hi Vọng"));
        Assert.Contains("1", hopeLog.Reason!);
        Assert.Contains("3", hopeLog.Reason!);

        var devilLog = Assert.Single(logs, row => row.Reason != null && row.Reason.Contains("Ma Quỷ"));
        Assert.Contains("0", devilLog.Reason!);
        Assert.Contains("2", devilLog.Reason!);

        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(3, reloaded.HopeStars);
        Assert.Equal(2, reloaded.DevilStars);
    }

    [Fact]
    public async Task UpdatePlayerStarsAsync_WithSameCurrentValues_WritesNoLog()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("bob", hopeStars: 2, devilStars: 4);
        await h.SaveAsync();

        // Act: pass the player's current values back unchanged.
        var result = await h.Service.UpdatePlayerStarsAsync(player.Id, hopeStars: 2, devilStars: 4);

        // Assert
        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.AdminAdjust));

        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(2, reloaded.HopeStars);
        Assert.Equal(4, reloaded.DevilStars);
    }

    [Fact]
    public async Task BulkAddStarAsync_AddsHopeToEveryVerifiedPlayer_AndWritesOneAdminAdjustEach()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var first = h.AddPlayer("first", hopeStars: 0);
        var second = h.AddPlayer("second", hopeStars: 1);
        await h.SaveAsync();

        // Act
        var result = await h.Service.BulkAddStarAsync("hope", 2);

        // Assert
        Assert.True(result.Succeeded, result.Error);

        var logs = h.CupLogs(KeoBiaCupChangeType.AdminAdjust);
        Assert.Equal(2, logs.Count);
        Assert.All(logs, row =>
        {
            Assert.Equal(0, row.Cups);
            Assert.Null(row.MatchId);
            Assert.Equal(h.SiteId, row.SiteId);
        });
        Assert.Single(logs, row => row.PlayerId == first.Id);
        Assert.Single(logs, row => row.PlayerId == second.Id);

        Assert.Equal(2, h.ReloadPlayer(first.Id).HopeStars);
        Assert.Equal(3, h.ReloadPlayer(second.Id).HopeStars);
    }

    [Fact]
    public async Task BulkAddStarAsync_WithInvalidStarType_ReturnsFailureAndChangesNothing()
    {
        // Arrange
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("carol", hopeStars: 5, devilStars: 6);
        await h.SaveAsync();

        // Act
        var result = await h.Service.BulkAddStarAsync("bogus", 2);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Loại sao không hợp lệ.", result.Error);

        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.AdminAdjust));

        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(5, reloaded.HopeStars);
        Assert.Equal(6, reloaded.DevilStars);
    }
}
