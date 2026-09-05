using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// End-to-end reconciliation coverage: proves the per-match journal (wrong_bet + missed_match),
/// the player aggregate fields (WrongBets / PenaltyCups / SharedCups), the loss leaderboard
/// (<see cref="KeoBiaService.GetLossLeaderboardAsync"/>) and the player history loss figure
/// (<see cref="KeoBiaService.GetPlayerHistoryAsync"/>) all agree on the authoritative loss:
///   loss = sum(settled wrong-bet cups) + aggregate penalties - aggregate credits.
/// Reversal entries (negative cups) are exercised so the leaderboard's SUM-based ActualMissed
/// stays reversal-safe.
/// </summary>
public sealed class LossReconciliationTests
{
    [Fact]
    public async Task LossLeaderboard_OrdersByNetLostCups_AfterReceivedBeer()
    {
        // Arrange: one player has higher gross loss but received enough beer to show lower net loss.
        using var h = new KeoBiaTestHarness();
        var netSeventeen = h.AddPlayer("net-17");
        var netEighteen = h.AddPlayer("net-18");
        var match = h.AddMatch(status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away);

        netSeventeen.WrongBets = 20;
        netSeventeen.TotalBets = 20;
        netSeventeen.TotalCups = 20;
        netEighteen.WrongBets = 18;
        netEighteen.TotalBets = 18;
        netEighteen.TotalCups = 18;

        h.AddBet(netSeventeen, match, cups: 20, isSettled: true, isCorrect: false);
        h.AddBet(netEighteen, match, cups: 18, isSettled: true, isCorrect: false);
        h.AddCupLogRaw(new KeoBiaCupLog
        {
            PlayerId = netSeventeen.Id,
            ChangeType = KeoBiaCupChangeType.ReceivedBeer,
            Cups = -3,
            Reason = "Gift offset",
            BalanceAfter = 17
        });
        await h.SaveAsync();

        // Act.
        var board = await h.Service.GetLossLeaderboardAsync();

        // Assert: rank follows the displayed net LostCups, not the pre-gift gross value.
        var ordered = board.Where(x => x.Id == netSeventeen.Id || x.Id == netEighteen.Id).ToList();
        Assert.Equal(netEighteen.Id, ordered[0].Id);
        Assert.Equal(18, ordered[0].LostCups);
        Assert.Equal(netSeventeen.Id, ordered[1].Id);
        Assert.Equal(17, ordered[1].LostCups);
    }

    [Fact]
    public async Task LossLeaderboard_SubtractsPaidBeer_FromOutstandingLoss()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("paid-in-full");
        var match = h.AddMatch(status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away);
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, cups: 45, isSettled: true, isCorrect: false);
        h.AddCupLogRaw(new KeoBiaCupLog
        {
            PlayerId = player.Id,
            ChangeType = KeoBiaCupChangeType.PaidBeer,
            Cups = -45,
            Reason = "Đã nộp 45 cốc bia",
            BalanceAfter = 0
        });
        await h.SaveAsync();

        var row = Assert.Single(await h.Service.GetLossLeaderboardAsync(), x => x.Id == player.Id);

        Assert.Equal(0, row.LostCups);
        Assert.Equal(0, row.LostBeerCups);
        Assert.Equal(45, row.PaidLegacyBeerCups);
    }

    [Fact]
    public async Task LossLeaderboard_RanksHistoricalLossWithoutSubtractingPaidUnits()
    {
        using var h = new KeoBiaTestHarness();
        var paidInFull = h.AddPlayer("paid-in-full");
        var stillOwes = h.AddPlayer("still-owes");
        var match = h.AddMatch(status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away);

        h.AddBet(paidInFull, match, choice: KeoBiaBetChoice.Home, cups: 45, isSettled: true, isCorrect: false);
        h.AddBet(stillOwes, match, choice: KeoBiaBetChoice.Home, cups: 32, isSettled: true, isCorrect: false);
        h.AddCupLogRaw(new KeoBiaCupLog
        {
            PlayerId = paidInFull.Id,
            ChangeType = KeoBiaCupChangeType.PaidBeer,
            Cups = -45,
            Reason = "Đã nộp 45 cốc bia",
            BalanceAfter = 0
        });
        await h.SaveAsync();

        var ordered = (await h.Service.GetLossLeaderboardAsync())
            .Where(x => x.Id == paidInFull.Id || x.Id == stillOwes.Id)
            .ToList();

        Assert.Equal(paidInFull.Id, ordered[0].Id);
        Assert.Equal(45, ordered[0].HistoricalLostBeerCups);
        Assert.Equal(45, ordered[0].HistoricalLostCups);
        Assert.Equal(stillOwes.Id, ordered[1].Id);
        Assert.Equal(32, ordered[1].HistoricalLostCups);
    }

    [Fact]
    public async Task LossLeaderboard_IncludesStoppedPlayersButExcludesManuallyBlockedPlayers()
    {
        using var h = new KeoBiaTestHarness();
        var stopped = h.AddPlayer("stopped", isBlocked: true);
        stopped.StoppedPlayingAt = DateTime.UtcNow;
        var manuallyBlocked = h.AddPlayer("manually-blocked", isBlocked: true);
        await h.SaveAsync();

        var board = await h.Service.GetLossLeaderboardAsync();

        var stoppedEntry = Assert.Single(board, x => x.Id == stopped.Id);
        Assert.NotNull(stoppedEntry.StoppedPlayingAt);
        Assert.DoesNotContain(board, x => x.Id == manuallyBlocked.Id);
    }

    [Fact]
    public async Task AddMissedMatchCredit_WritesLog_AndReducesMissPenalty()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("promo");
        player.PenaltyCups = 96;
        await h.SaveAsync();

        var result = await h.Service.AddMissedMatchCreditAsync(player.Id, 76);

        Assert.True(result.Succeeded, result.Error);
        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(76, reloaded.MissedMatchCredit);
        Assert.Equal(20, reloaded.PenaltyCups);

        var promoLog = Assert.Single(h.CupLogs(KeoBiaCupChangeType.MissedMatchCredit), x => x.PlayerId == player.Id);
        Assert.Equal(-76, promoLog.Cups);

        var board = await h.Service.GetLossLeaderboardAsync();
        var row = Assert.Single(board, x => x.Id == player.Id);
        Assert.Equal(76, row.PromoCups);
        Assert.Equal(20, row.LostCups);
    }

    [Fact]
    public async Task FirstTelegramRegistration_CapsHistoricalMissesAt20_AndFutureMissesAccrue()
    {
        var telegram = new KeoBiaTelegramOptions
        {
            Authority = "https://oauth.telegram.org",
            ClientId = "test-client",
            ClientSecret = "test-secret"
        };
        using var h = new KeoBiaTestHarness(telegramOptions: telegram);
        var player = h.AddPlayer(
            "registration-promo",
            telegramUserId: null,
            createdAt: KeoBiaUnitRules.PeanutSwitchAtUtc);
        for (var i = 0; i < 120; i++)
        {
            h.AddMatch(
                stage: "Round of 16",
                kickoffAt: KeoBiaUnitRules.PeanutSwitchAtUtc.AddHours(-i - 1));
        }
        await h.SaveAsync();

        var result = await h.Service.LinkTelegramViaOidcAsync(new KeoBiaTelegramOidcLinkDto(
            player.PublicKey, player.DisplayName, null, 123456, "registration-promo", null, null, null, null, null));

        Assert.True(result.Succeeded, result.Error);
        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(100, reloaded.MissedMatchCredit);
        Assert.Equal(20, reloaded.PenaltyCups);
        var promotion = Assert.Single(h.CupLogs(KeoBiaCupChangeType.MissedMatchCredit));
        Assert.Equal(-100, promotion.Cups);
        Assert.StartsWith("Khuyến mãi đăng ký miễn phạt", promotion.Reason);

        var laterMatch = h.AddMatch(
            stage: "Quarter-final",
            kickoffAt: KeoBiaUnitRules.PeanutSwitchAtUtc.AddHours(1));
        await h.SaveAsync();
        await h.Service.UpdateResultAsync(laterMatch.Id, 1, 0, KeoBiaMatchStatus.Finished);

        reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(101, reloaded.MissedMatchCredit);
        Assert.Equal(20, reloaded.PenaltyCups);

        var repeat = await h.Service.LinkTelegramViaOidcAsync(new KeoBiaTelegramOidcLinkDto(
            player.PublicKey, player.DisplayName, null, 123456, "registration-promo", null, null, null, null, null));
        Assert.True(repeat.Succeeded, repeat.Error);
        Assert.Equal(2, h.CupLogs(KeoBiaCupChangeType.MissedMatchCredit).Count);
    }

    [Fact]
    public async Task RegistrationPromotion_LeavesOnly20BeerAndWaivesQuarterFinalPeanutDebt()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer(
            "beer-cap-with-peanut",
            createdAt: KeoBiaUnitRules.PeanutSwitchAtUtc);
        for (var i = 0; i < 20; i++)
        {
            h.AddMatch(
                stage: "Round of 16",
                kickoffAt: KeoBiaUnitRules.PeanutSwitchAtUtc.AddHours(-i - 1));
        }
        h.AddMatch(
            stage: "Quarter-final",
            kickoffAt: KeoBiaUnitRules.PeanutSwitchAtUtc.AddHours(1));
        await h.SaveAsync();

        await h.Service.SeedCupLogsAsync();

        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(1, reloaded.MissedMatchCredit);
        Assert.Equal(20, reloaded.PenaltyCups);
        var row = Assert.Single(await h.Service.GetLossLeaderboardAsync(), x => x.Id == player.Id);
        Assert.Equal(20, row.LostBeerCups);
        Assert.Equal(0, row.LostPeanutPacks);
        var promotion = Assert.Single(h.CupLogs(KeoBiaCupChangeType.MissedMatchCredit));
        Assert.Equal(-1, promotion.Cups);
        Assert.Contains("gói lạc", promotion.Reason);
    }

    [Fact]
    public async Task RegistrationPromotion_RepairsCreditThatPreviouslyCountedQuarterFinalMiss()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer(
            "repair-unit-credit",
            missedMatchCredit: 1,
            createdAt: KeoBiaUnitRules.PeanutSwitchAtUtc);
        for (var i = 0; i < 20; i++)
        {
            h.AddMatch(
                stage: "Round of 16",
                kickoffAt: KeoBiaUnitRules.PeanutSwitchAtUtc.AddHours(-i - 1));
        }
        h.AddMatch(
            stage: "Quarter-final",
            kickoffAt: KeoBiaUnitRules.PeanutSwitchAtUtc.AddHours(1));
        h.AddCupLogRaw(new KeoBiaCupLog
        {
            PlayerId = player.Id,
            ChangeType = KeoBiaCupChangeType.MissedMatchCredit,
            Cups = -1,
            Reason = "Khuyến mãi đăng ký miễn phạt 1 cốc bia từ 10/07",
            BalanceAfter = 20,
            CreatedAt = KeoBiaUnitRules.PeanutSwitchAtUtc
        });
        await h.SaveAsync();

        await h.Service.SeedCupLogsAsync();

        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(1, reloaded.MissedMatchCredit);
        Assert.Equal(20, reloaded.PenaltyCups);
        Assert.Equal(-1, h.CupLogs(KeoBiaCupChangeType.MissedMatchCredit).Sum(x => x.Cups));
        var row = Assert.Single(await h.Service.GetLossLeaderboardAsync(), x => x.Id == player.Id);
        Assert.Equal(20, row.LostBeerCups);
        Assert.Equal(0, row.LostPeanutPacks);
    }

    [Fact]
    public async Task TelegramRegistration_BeforeCutoff_DoesNotReceiveAutomaticMissPromotion()
    {
        var telegram = new KeoBiaTelegramOptions
        {
            Authority = "https://oauth.telegram.org",
            ClientId = "test-client",
            ClientSecret = "test-secret"
        };
        using var h = new KeoBiaTestHarness(telegramOptions: telegram);
        var player = h.AddPlayer(
            "before-promotion",
            telegramUserId: null,
            createdAt: KeoBiaUnitRules.PeanutSwitchAtUtc.AddTicks(-1));
        h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));
        await h.SaveAsync();

        var result = await h.Service.LinkTelegramViaOidcAsync(new KeoBiaTelegramOidcLinkDto(
            player.PublicKey, player.DisplayName, null, 123457, "before-promotion", null, null, null, null, null));

        Assert.True(result.Succeeded, result.Error);
        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(0, reloaded.MissedMatchCredit);
        Assert.Equal(1, reloaded.PenaltyCups);
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.MissedMatchCredit));
    }

    [Fact]
    public async Task RegistrationPromotion_PreservesHigherExistingCredit_AndIsIdempotent()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer(
            "higher-credit",
            missedMatchCredit: 100,
            createdAt: KeoBiaUnitRules.PeanutSwitchAtUtc);
        for (var i = 0; i < 96; i++)
        {
            h.AddMatch(
                stage: "Round of 16",
                kickoffAt: KeoBiaUnitRules.PeanutSwitchAtUtc.AddHours(-i - 1));
        }
        await h.SaveAsync();

        await h.Service.SeedCupLogsAsync();
        await h.Service.SeedCupLogsAsync();

        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(100, reloaded.MissedMatchCredit);
        Assert.Equal(0, reloaded.PenaltyCups);
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.MissedMatchCredit));
    }

    [Fact]
    public async Task AddBalanceCredit_ReducesWrongBetDebt_WhenPlayerHasNoMissPenalty()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("wrong-bet-credit");
        var match = h.AddMatch(status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away);
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, cups: 2, isSettled: true, isCorrect: false);
        await h.SaveAsync();

        var result = await h.Service.AddBalanceCreditAsync(player.Id, 2);

        Assert.True(result.Succeeded, result.Error);
        var creditLog = Assert.Single(h.CupLogs(KeoBiaCupChangeType.ReceivedBeer), x => x.PlayerId == player.Id);
        Assert.Equal(-2, creditLog.Cups);
        Assert.Contains("Admin giảm nợ", creditLog.Reason);

        var row = Assert.Single(await h.Service.GetLossLeaderboardAsync(), x => x.Id == player.Id);
        Assert.Equal(0, row.LostCups);
    }

    [Fact]
    public async Task SyncFootballData_PenaltyShootout_StoresRegularTimeAndShootoutScore()
    {
        using var h = new KeoBiaTestHarness();
        var seeded = h.AddMatch(kickoffOffset: null, status: KeoBiaMatchStatus.Scheduled);
        seeded.KickoffAt = new DateTime(2026, 7, 7, 20, 0, 0, DateTimeKind.Utc);
        seeded.HomeName = "Switzerland";
        seeded.HomeCode = "SUI";
        seeded.AwayName = "Colombia";
        seeded.AwayCode = "COL";
        await h.SaveAsync();

        var json = """
        {
          "matches": [
            {
              "id": 537382,
              "status": "FINISHED",
              "utcDate": "2026-07-07T20:00:00Z",
              "stage": "LAST_16",
              "homeTeam": { "name": "Switzerland", "shortName": "Switzerland", "tla": "SUI" },
              "awayTeam": { "name": "Colombia", "shortName": "Colombia", "tla": "COL" },
              "score": {
                "winner": null,
                "duration": "PENALTY_SHOOTOUT",
                "fullTime": { "home": 4, "away": 3 },
                "regularTime": { "home": 0, "away": 0 },
                "extraTime": { "home": 0, "away": 0 },
                "penalties": { "home": 3, "away": 3 }
              }
            }
          ]
        }
        """;

        var result = await h.Service.SyncFootballDataAsync(json, "test");

        Assert.True(result.Succeeded, result.Error);
        using var db = h.NewContext();
        var match = Assert.Single(db.KeoBiaMatches);
        Assert.Equal(0, match.HomeScore);
        Assert.Equal(0, match.AwayScore);
        Assert.Equal(0, match.ResultHomeScore);
        Assert.Equal(0, match.ResultAwayScore);
        Assert.Equal(4, match.PenaltyHomeScore);
        Assert.Equal(3, match.PenaltyAwayScore);
    }

    [Fact]
    public async Task MixedLoss_Journal_Player_And_Leaderboard_AllReconcile()
    {
        // Arrange: one player who will lose two bets, miss one match, and receive a gift.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("loser");
        var giver = h.AddPlayer("giver");

        // Two matches the player bets on (and will get wrong) + one match they skip entirely.
        var lostA = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-3));
        var lostB = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-2));
        var missed = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));

        h.AddBet(player, lostA, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        h.AddBet(player, lostB, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        // giver bets every match so it never accrues a missed-match penalty of its own.
        h.AddBet(giver, lostA, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        h.AddBet(giver, lostB, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        h.AddBet(giver, missed, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        await h.SaveAsync();

        // Act: settle all three matches. Away wins lostA/lostB (player picked Home => wrong),
        // and the skipped match finishing makes the absent player accrue a missed_match.
        Assert.True((await h.Service.UpdateResultAsync(lostA.Id, 0, 1, KeoBiaMatchStatus.Finished)).Succeeded);
        Assert.True((await h.Service.UpdateResultAsync(lostB.Id, 0, 1, KeoBiaMatchStatus.Finished)).Succeeded);
        Assert.True((await h.Service.UpdateResultAsync(missed.Id, 1, 0, KeoBiaMatchStatus.Finished)).Succeeded);

        // Then the player receives a 1-cup gift. ShareBeerAsync writes received_beer(-1) and
        // reduces PenaltyCups by min(penalty, 1); it does NOT re-run recalc, so do it last.
        var giveResult = await h.Service.ShareBeerAsync(new KeoBiaShareBeerDto(giver.PublicKey, player.PublicKey, 1));
        Assert.True(giveResult.Succeeded, giveResult.Error);

        // Assert: journal NETs reconcile to the contract truth.
        var wrongNet = h.CupLogs(KeoBiaCupChangeType.WrongBet)
            .Where(x => x.PlayerId == player.Id).Sum(x => x.Cups);
        var missedNet = h.CupLogs(KeoBiaCupChangeType.MissedMatch)
            .Where(x => x.PlayerId == player.Id).Sum(x => x.Cups);
        var receivedAbs = h.CupLogs(KeoBiaCupChangeType.ReceivedBeer)
            .Where(x => x.PlayerId == player.Id).Sum(x => Math.Abs(x.Cups));

        // Two wrong bets => NET wrong_bet cups == 2, matching the player aggregate WrongBets.
        Assert.Equal(2, wrongNet);
        var reloaded = h.ReloadPlayer(player.Id);
        Assert.Equal(2, reloaded.WrongBets);

        // One missed match accrued. The gift reduced PenaltyCups by 1 (from 1 -> 0), but the
        // missed_match journal is NET +1 because the gift never reverses a missed-match log.
        Assert.Equal(1, missedNet);
        Assert.Equal(1, receivedAbs);
        // PenaltyCups tracks the *current* missed-match driver minus the gift offset (was 1, -1 => 0).
        Assert.Equal(0, reloaded.PenaltyCups);
        Assert.Equal(0, reloaded.SharedCups);

        // Leaderboard agrees: ActualMissed == NET(missed_match), ReceivedCups == abs(received),
        // and the player has used no star items.
        var board = await h.Service.GetLossLeaderboardAsync();
        var row = Assert.Single(board, x => x.Id == player.Id);
        Assert.Equal(missedNet, row.MissedMatches);
        Assert.Equal(receivedAbs, row.ReceivedCups);
        Assert.Equal(0, row.StarItemsUsed);
        // LostCups on the leaderboard = wrong-bet cups + penalty + shared - received.
        // = 2 + 0 + 0 - 1 = 1.
        Assert.Equal(2 + reloaded.PenaltyCups + reloaded.SharedCups - receivedAbs, row.LostCups);
    }

    [Fact]
    public async Task MissedThenCancelled_LeaderboardActualMissed_ExcludesReversedMatch()
    {
        // Arrange: an absentee plus a bettor so the match recalculates verified players.
        using var h = new KeoBiaTestHarness();
        var absentee = h.AddPlayer("absent");
        var bettor = h.AddPlayer("present");

        var keptMissed = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-2));
        var cancelledMissed = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));

        // bettor participates in both matches so it never misses; absentee skips both.
        h.AddBet(bettor, keptMissed, choice: KeoBiaBetChoice.Home, isSettled: false);
        h.AddBet(bettor, cancelledMissed, choice: KeoBiaBetChoice.Home, isSettled: false);
        await h.SaveAsync();

        // Act: finish both -> absentee accrues missed_match on each (+1, +1 => NET 2).
        Assert.True((await h.Service.UpdateResultAsync(keptMissed.Id, 1, 0, KeoBiaMatchStatus.Finished)).Succeeded);
        Assert.True((await h.Service.UpdateResultAsync(cancelledMissed.Id, 1, 0, KeoBiaMatchStatus.Finished)).Succeeded);

        var board = await h.Service.GetLossLeaderboardAsync();
        var before = Assert.Single(board, x => x.Id == absentee.Id);
        Assert.Equal(2, before.MissedMatches);

        // Cancel one match -> it is no longer "missed" -> a -1 reversal is appended (NET 0 for it).
        Assert.True((await h.Service.UpdateResultAsync(cancelledMissed.Id, null, null, KeoBiaMatchStatus.Cancelled)).Succeeded);

        // Assert: there are now both a +1 and a -1 row for the cancelled match (append-only),
        // and the SUM-based leaderboard ActualMissed nets them out to the one remaining match.
        var allMissedRows = h.CupLogs(KeoBiaCupChangeType.MissedMatch)
            .Where(x => x.PlayerId == absentee.Id).ToList();
        Assert.Equal(0, allMissedRows.Where(x => x.MatchId == cancelledMissed.Id).Sum(x => x.Cups));
        Assert.Contains(allMissedRows, x => x.MatchId == cancelledMissed.Id && x.Cups == 1);
        Assert.Contains(allMissedRows, x => x.MatchId == cancelledMissed.Id && x.Cups == -1);

        var boardAfter = await h.Service.GetLossLeaderboardAsync();
        var after = Assert.Single(boardAfter, x => x.Id == absentee.Id);
        Assert.Equal(1, after.MissedMatches);
        Assert.Equal(allMissedRows.Sum(x => x.Cups), after.MissedMatches);
    }

    [Fact]
    public async Task PlayerHistory_LostCups_IsNonNegative_AndMatchesComputedLoss()
    {
        // Arrange: a player with a wrong bet, a missed match, and a received gift so all three
        // loss components are non-zero.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("history");
        var giver = h.AddPlayer("benefactor");

        var lost = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-2));
        var skipped = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1));

        h.AddBet(player, lost, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        // giver covers every match so it is not itself a missed-match absentee.
        h.AddBet(giver, lost, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        h.AddBet(giver, skipped, choice: KeoBiaBetChoice.Home, cups: 1, isSettled: false);
        await h.SaveAsync();

        // Act: away wins the bet match (player wrong); skipped match finishes (player missed).
        Assert.True((await h.Service.UpdateResultAsync(lost.Id, 0, 1, KeoBiaMatchStatus.Finished)).Succeeded);
        Assert.True((await h.Service.UpdateResultAsync(skipped.Id, 1, 0, KeoBiaMatchStatus.Finished)).Succeeded);
        var giveResult = await h.Service.ShareBeerAsync(new KeoBiaShareBeerDto(giver.PublicKey, player.PublicKey, 1));
        Assert.True(giveResult.Succeeded, giveResult.Error);

        // Assert: history.LostCups is internally consistent and non-negative.
        var historyResult = await h.Service.GetPlayerHistoryAsync(player.PublicKey);
        Assert.True(historyResult.Succeeded, historyResult.Error);
        var history = historyResult.Value!;

        var reloaded = h.ReloadPlayer(player.Id);
        var wrongBetCups = history.WrongBets; // MaxCups=1, every wrong settled bet stakes 1 cup.
        var receivedAbs = h.CupLogs(KeoBiaCupChangeType.ReceivedBeer)
            .Where(x => x.PlayerId == player.Id).Sum(x => Math.Abs(x.Cups));

        // GetPlayerHistoryAsync computes lostCups = lostBetCups + Penalty + Shared - received.
        var computed = wrongBetCups + reloaded.PenaltyCups + reloaded.SharedCups - receivedAbs;
        Assert.Equal((decimal)computed, history.LostCups);
        Assert.True(history.LostCups >= 0, $"LostCups should never be negative, was {history.LostCups}");
        Assert.Equal(1, history.WrongBets);
    }

    [Fact]
    public async Task BeerPaymentPrompt_OutstandingCups_IncludesCorrectScoreNetLoss()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("score-payment", telegramUserId: 1391357367);
        var match = h.AddMatch(status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away);

        h.AddBet(player, match, cups: 42, isSettled: true, isCorrect: false);
        h.AddCupLogRaw(new KeoBiaCupLog
        {
            PlayerId = player.Id,
            ChangeType = KeoBiaCupChangeType.QuizCorrect,
            Cups = -1,
            Reason = "Quiz reward",
            BalanceAfter = 46
        });
        h.AddCupLogRaw(new KeoBiaCupLog
        {
            PlayerId = player.Id,
            ChangeType = KeoBiaCupChangeType.CorrectScore,
            Cups = 6,
            Reason = "Wrong score",
            BalanceAfter = 52
        });
        h.AddCupLogRaw(new KeoBiaCupLog
        {
            PlayerId = player.Id,
            ChangeType = KeoBiaCupChangeType.CorrectScore,
            Cups = -2,
            Reason = "Correct score reward",
            BalanceAfter = 50
        });
        await h.SaveAsync();

        var historyResult = await h.Service.GetPlayerHistoryAsync(player.PublicKey);
        var promptResult = await h.Service.GetBeerPaymentPromptAsync(1391357367);

        Assert.True(historyResult.Succeeded, historyResult.Error);
        Assert.True(promptResult.Succeeded, promptResult.Error);
        Assert.Equal(45, historyResult.Value!.LostCups);
        Assert.Equal(historyResult.Value.LostCups, promptResult.Value!.OutstandingCups);
    }
}
