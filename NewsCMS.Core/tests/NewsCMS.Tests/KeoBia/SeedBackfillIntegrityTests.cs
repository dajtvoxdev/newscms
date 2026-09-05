using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// Old-data integrity coverage for the one-time per-match journal backfill
/// (<c>SeedCupLogsAsync</c>) and the delta reconcile it relies on. These prove that
/// legacy lump rows are cleaned up exactly once, per-match rows are backfilled to match
/// the authoritative loss, and that re-running the reconcile nets duplicate/stale rows
/// back to the current truth without ever double-counting.
/// </summary>
public sealed class SeedBackfillIntegrityTests
{
    // ---- (1) GUARD SKIP ----

    [Fact]
    public async Task SeedCupLogs_WhenPerMatchWrongBetAlreadyExists_SkipsAndLeavesLogsUntouched()
    {
        // Arrange: a player + finished match that already carries a per-match wrong_bet log.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("legacy-journaled");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1), status: KeoBiaMatchStatus.Finished);
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, isSettled: true, isCorrect: false);
        var existing = h.AddCupLogRaw(new KeoBiaCupLog
        {
            SiteId = h.SiteId,
            PlayerId = player.Id,
            MatchId = match.Id,
            ChangeType = KeoBiaCupChangeType.WrongBet,
            Cups = 1,
            Reason = "Legacy per-match row",
            BalanceAfter = 1,
        });
        await h.SaveAsync();

        var before = h.CupLogs(KeoBiaCupChangeType.WrongBet);

        // Act: the guard sees an existing per-match wrong_bet row and returns immediately.
        await h.Service.SeedCupLogsAsync();

        // Assert: exactly the one original row remains, unchanged.
        var after = h.CupLogs(KeoBiaCupChangeType.WrongBet);
        var row = Assert.Single(after);
        Assert.Equal(existing.Id, row.Id);
        Assert.Equal(1, row.Cups);
        Assert.Equal("Legacy per-match row", row.Reason);
        Assert.Equal(match.Id, row.MatchId);
        Assert.Single(before);
    }

    // ---- (2) LUMP CLEANUP + BACKFILL ----

    [Fact]
    public async Task SeedCupLogs_DeletesLumpWrongBet_BackfillsPerMatch_AndPreservesMissedCredit()
    {
        // Arrange: a verified player who lost two settled bets on finished matches but has
        // NO per-match journal yet — only a legacy matchId-less lump wrong_bet row plus a
        // legacy matchId-less missed_credit promo row.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("legacy-lump");
        var matchA = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-2), status: KeoBiaMatchStatus.Finished);
        var matchB = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1), status: KeoBiaMatchStatus.Finished);
        // Both bets are settled wrong; both matches already have a result so they are not "missed".
        matchA.ResultChoice = KeoBiaBetChoice.Away;
        matchB.ResultChoice = KeoBiaBetChoice.Away;
        h.AddBet(player, matchA, choice: KeoBiaBetChoice.Home, isSettled: true, isCorrect: false);
        h.AddBet(player, matchB, choice: KeoBiaBetChoice.Home, isSettled: true, isCorrect: false);

        var lumpWrong = h.AddCupLogRaw(new KeoBiaCupLog
        {
            SiteId = h.SiteId,
            PlayerId = player.Id,
            MatchId = null,
            ChangeType = KeoBiaCupChangeType.WrongBet,
            Cups = 2,
            Reason = "Legacy lump wrong bets",
            BalanceAfter = 2,
        });
        var promoCredit = h.AddCupLogRaw(new KeoBiaCupLog
        {
            SiteId = h.SiteId,
            PlayerId = player.Id,
            MatchId = null,
            ChangeType = KeoBiaCupChangeType.MissedMatchCredit,
            Cups = -1,
            Reason = "Legacy promo credit",
            BalanceAfter = 1,
        });
        await h.SaveAsync();

        // Act: no per-match journal exists, so the backfill drops the wrong_bet lump,
        // preserves the missed_credit lump, then recomputes per-match rows.
        await h.Service.SeedCupLogsAsync();

        // Assert: the matchId-less wrong_bet lump is gone.
        var wrongLogs = h.CupLogs(KeoBiaCupChangeType.WrongBet);
        Assert.DoesNotContain(wrongLogs, x => x.Id == lumpWrong.Id);
        Assert.All(wrongLogs, x => Assert.NotNull(x.MatchId));

        // One per-match wrong_bet row per wrong bet, net cups == wrong-bet count.
        Assert.Equal(2, wrongLogs.Count);
        Assert.Contains(wrongLogs, x => x.MatchId == matchA.Id);
        Assert.Contains(wrongLogs, x => x.MatchId == matchB.Id);
        Assert.Equal(2, wrongLogs.Sum(x => x.Cups));

        // The missed_credit lump is preserved (never re-journaled, so deleting it would
        // silently inflate the journalled loss).
        var creditLogs = h.CupLogs(KeoBiaCupChangeType.MissedMatchCredit);
        var credit = Assert.Single(creditLogs);
        Assert.Equal(promoCredit.Id, credit.Id);
        Assert.Null(credit.MatchId);
        Assert.Equal(-1, credit.Cups);
    }

    // ---- (3) PARTIAL BACKFILL via recompute ----

    [Fact]
    public async Task Recompute_WithPartialWrongBetJournal_NetsToActualWrongBetCount()
    {
        // Arrange: a player with three settled-wrong bets but only ONE existing per-match
        // wrong_bet log (an incomplete legacy backfill).
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("partial-journal");
        var matchA = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-3), status: KeoBiaMatchStatus.Finished);
        var matchB = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-2), status: KeoBiaMatchStatus.Finished);
        var matchC = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1), status: KeoBiaMatchStatus.Finished);
        foreach (var m in new[] { matchA, matchB, matchC })
        {
            m.ResultChoice = KeoBiaBetChoice.Away;
            h.AddBet(player, m, choice: KeoBiaBetChoice.Home, isSettled: true, isCorrect: false);
        }

        // Only matchA has a per-match wrong_bet row so far.
        h.AddCupLogRaw(new KeoBiaCupLog
        {
            SiteId = h.SiteId,
            PlayerId = player.Id,
            MatchId = matchA.Id,
            ChangeType = KeoBiaCupChangeType.WrongBet,
            Cups = 1,
            Reason = "Existing per-match row for A",
            BalanceAfter = 1,
        });
        await h.SaveAsync();

        // Act: any recompute backfills the missing per-match rows. UpdateResultAsync on one
        // match recalcs every verified player.
        var result = await h.Service.UpdateResultAsync(matchC.Id, 0, 1, KeoBiaMatchStatus.Finished);
        Assert.True(result.Succeeded, result.Error);

        // Assert: net wrong_bet cups equals the player's actual wrong-bet count (3).
        Assert.Equal(3, h.NetCups(KeoBiaCupChangeType.WrongBet));
        var wrongLogs = h.CupLogs(KeoBiaCupChangeType.WrongBet);
        Assert.Equal(3, wrongLogs.Select(x => x.MatchId).Distinct().Count());
        Assert.Equal(3, h.ReloadPlayer(player.Id).WrongBets);
    }

    // ---- (4) DUPLICATE NETTING ----

    [Fact]
    public async Task Recompute_WithDuplicateWrongBetRows_AppendsCorrectionToNetOne()
    {
        // Arrange: a (player, match) carrying TWO legacy wrong_bet rows (net 2) although the
        // bet is only worth one wrong cup.
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("dup-journal");
        var match = h.AddMatch(kickoffOffset: TimeSpan.FromHours(-1), status: KeoBiaMatchStatus.Finished);
        match.ResultChoice = KeoBiaBetChoice.Away;
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, isSettled: true, isCorrect: false);

        h.AddCupLogRaw(new KeoBiaCupLog
        {
            SiteId = h.SiteId,
            PlayerId = player.Id,
            MatchId = match.Id,
            ChangeType = KeoBiaCupChangeType.WrongBet,
            Cups = 1,
            Reason = "Legacy dup row #1",
            BalanceAfter = 1,
        });
        h.AddCupLogRaw(new KeoBiaCupLog
        {
            SiteId = h.SiteId,
            PlayerId = player.Id,
            MatchId = match.Id,
            ChangeType = KeoBiaCupChangeType.WrongBet,
            Cups = 1,
            Reason = "Legacy dup row #2",
            BalanceAfter = 2,
        });
        await h.SaveAsync();

        Assert.Equal(2, h.NetCups(KeoBiaCupChangeType.WrongBet));

        // Act: recompute reconciles the existing net (2) down to the desired truth (1).
        var result = await h.Service.UpdateResultAsync(match.Id, 0, 1, KeoBiaMatchStatus.Finished);
        Assert.True(result.Succeeded, result.Error);

        // Assert: a -1 correction was appended, leaving net cups for this match at 1.
        Assert.Equal(1, h.NetCups(KeoBiaCupChangeType.WrongBet));
        var matchRows = h.CupLogs(KeoBiaCupChangeType.WrongBet).Where(x => x.MatchId == match.Id).ToList();
        Assert.Equal(1, matchRows.Sum(x => x.Cups));
        Assert.Contains(matchRows, x => x.Cups == -1);
    }
}
