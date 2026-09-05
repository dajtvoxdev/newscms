using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

public sealed class UnitTransitionTests
{
    private static readonly DateTime BeforeSwitch =
        KeoBiaUnitRules.PeanutSwitchAtUtc.AddMinutes(-1);

    private static readonly DateTime AtSwitch =
        KeoBiaUnitRules.PeanutSwitchAtUtc;

    [Fact]
    public void UnitRules_KeepOldRoundsAsBeer_AndUsePeanutsFromQuarterFinals()
    {
        Assert.Equal(KeoBiaUnitCode.Beer, KeoBiaUnitRules.GetUnitCode("Round of 16", BeforeSwitch));
        Assert.Equal(KeoBiaUnitCode.Peanut, KeoBiaUnitRules.GetUnitCode("Quarter-final", AtSwitch));
        Assert.Equal(KeoBiaUnitCode.Peanut, KeoBiaUnitRules.GetUnitCode(null, AtSwitch));
        Assert.Equal("0 cốc bia + 0 gói lạc", KeoBiaUnitRules.FormatBreakdown(0, 0));
        Assert.Equal("1 cốc bia + 2 gói lạc", KeoBiaUnitRules.FormatBreakdown(1, 2));
    }

    [Fact]
    public async Task PaymentPrompt_AndHistory_PreserveBeerAndPeanutUnits()
    {
        using var h = CreatePaymentHarness();
        var player = h.AddPlayer("mixed-units", telegramUserId: 20260710);
        var oldMatch = h.AddMatch(
            status: KeoBiaMatchStatus.Finished,
            resultChoice: KeoBiaBetChoice.Away,
            stage: "Round of 16",
            kickoffAt: BeforeSwitch);
        var newMatch = h.AddMatch(
            status: KeoBiaMatchStatus.Finished,
            resultChoice: KeoBiaBetChoice.Away,
            stage: "Quarter-final",
            kickoffAt: AtSwitch);

        h.AddBet(player, oldMatch, cups: 1, isSettled: true, isCorrect: false);
        h.AddBet(player, newMatch, cups: 1, isSettled: true, isCorrect: false);
        h.AddCupLogRaw(WrongBetLog(player, oldMatch, BeforeSwitch));
        h.AddCupLogRaw(WrongBetLog(player, newMatch, AtSwitch));
        await h.SaveAsync();

        var prompt = await h.Service.GetBeerPaymentPromptAsync(20260710);
        var history = await h.Service.GetPlayerHistoryAsync(player.PublicKey);
        var leaderboard = await h.Service.GetLossLeaderboardAsync();
        var adminPlayers = await h.Service.SearchPlayersAsync("mixed-units");
        var recentBets = await h.Service.SearchBetsAsync(player.Id, null);

        Assert.True(prompt.Succeeded, prompt.Error);
        Assert.Equal(1, prompt.Value!.OutstandingBeerCups);
        Assert.Equal(1, prompt.Value.OutstandingPeanutPacks);

        Assert.True(history.Succeeded, history.Error);
        Assert.Contains(history.Value!.Items, x => x.MatchId == oldMatch.Id && x.UnitCode == KeoBiaUnitCode.Beer);
        Assert.Contains(history.Value.Items, x => x.MatchId == newMatch.Id && x.UnitCode == KeoBiaUnitCode.Peanut);

        var entry = Assert.Single(leaderboard);
        Assert.Equal(1, entry.LostBeerCups);
        Assert.Equal(1, entry.LostPeanutPacks);

        var adminEntry = Assert.Single(adminPlayers.Items);
        Assert.Equal(1, adminEntry.TotalBeerCups);
        Assert.Equal(1, adminEntry.TotalPeanutPacks);
        Assert.Equal(1, adminEntry.OutstandingBeerCups);
        Assert.Equal(1, adminEntry.OutstandingPeanutPacks);
        Assert.Contains(recentBets.Items, x => x.UnitCode == KeoBiaUnitCode.Beer);
        Assert.Contains(recentBets.Items, x => x.UnitCode == KeoBiaUnitCode.Peanut);
    }

    [Fact]
    public async Task AdminBalanceCredit_ReducesLegacyBeerBeforePeanuts()
    {
        using var h = CreatePaymentHarness();
        var player = h.AddPlayer("admin-credit", telegramUserId: 20260712);
        var oldMatch = h.AddMatch(
            status: KeoBiaMatchStatus.Finished,
            resultChoice: KeoBiaBetChoice.Away,
            stage: "Round of 16",
            kickoffAt: BeforeSwitch);
        var newMatch = h.AddMatch(
            status: KeoBiaMatchStatus.Finished,
            resultChoice: KeoBiaBetChoice.Away,
            stage: "Quarter-final",
            kickoffAt: AtSwitch);

        h.AddBet(player, oldMatch, cups: 1, isSettled: true, isCorrect: false);
        h.AddBet(player, newMatch, cups: 1, isSettled: true, isCorrect: false);
        h.AddCupLogRaw(WrongBetLog(player, oldMatch, BeforeSwitch));
        h.AddCupLogRaw(WrongBetLog(player, newMatch, AtSwitch));
        await h.SaveAsync();

        var result = await h.Service.AddBalanceCreditAsync(player.Id, 1);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value!.BeerCups);
        Assert.Equal(0, result.Value.PeanutPacks);
        Assert.Equal(0, result.Value.OutstandingBeerCups);
        Assert.Equal(1, result.Value.OutstandingPeanutPacks);
        Assert.Contains("1 cốc bia", Assert.Single(h.CupLogs(KeoBiaCupChangeType.ReceivedBeer)).Reason);
        var history = await h.Service.GetPlayerCupLogsAsync(player.Id);
        Assert.Equal(KeoBiaUnitCode.Beer, Assert.Single(history, x => x.ChangeType == KeoBiaCupChangeType.ReceivedBeer).UnitCode);
    }

    [Fact]
    public async Task CreatePayment_ChargesLegacyBeerFirst_ThenPeanuts()
    {
        using var h = CreatePaymentHarness();
        var player = h.AddPlayer("mixed-payment", telegramUserId: 20260711);
        var oldMatch = h.AddMatch(
            status: KeoBiaMatchStatus.Finished,
            resultChoice: KeoBiaBetChoice.Away,
            stage: "Round of 16",
            kickoffAt: BeforeSwitch);
        var newMatch = h.AddMatch(
            status: KeoBiaMatchStatus.Finished,
            resultChoice: KeoBiaBetChoice.Away,
            stage: "Quarter-final",
            kickoffAt: AtSwitch);

        h.AddBet(player, oldMatch, cups: 1, isSettled: true, isCorrect: false);
        h.AddBet(player, newMatch, cups: 1, isSettled: true, isCorrect: false);
        h.AddCupLogRaw(WrongBetLog(player, oldMatch, BeforeSwitch));
        h.AddCupLogRaw(WrongBetLog(player, newMatch, AtSwitch));
        await h.SaveAsync();

        var result = await h.Service.CreateBeerPaymentAsync(20260711, 2);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value!.BeerCups);
        Assert.Equal(1, result.Value.PeanutPacks);
        Assert.Equal(15_000, result.Value.CoinAmount);
        Assert.Contains("amount=15000", result.Value.QrUrl, StringComparison.Ordinal);

        var paid = await h.Service.MarkBeerPaymentPaidAsync(result.Value.Id, "test-payment", null);
        Assert.True(paid.Succeeded, paid.Error);
        var logs = await h.Service.GetPlayerCupLogsAsync(player.Id);
        Assert.Contains(logs, x => x.ChangeType == KeoBiaCupChangeType.PaidBeer && x.Cups == -1 && x.UnitCode == KeoBiaUnitCode.Beer);
        Assert.Contains(logs, x => x.ChangeType == KeoBiaCupChangeType.PaidBeer && x.Cups == -1 && x.UnitCode == KeoBiaUnitCode.Peanut);
    }

    [Fact]
    public void ExistingPaymentAmounts_DecodeAsLegacyBeerWithoutMigration()
    {
        var legacy = KeoBiaUnitRules.DecodePaymentBreakdown(3, 30_000);
        var peanut = KeoBiaUnitRules.DecodePaymentBreakdown(3, 15_000);

        Assert.Equal((3, 0), legacy);
        Assert.Equal((0, 3), peanut);
    }

    [Fact]
    public async Task LeaderboardAndHistory_SplitRewardsPenaltiesAndItemsByBeerAndPeanut()
    {
        using var h = CreatePaymentHarness();
        var player = h.AddPlayer("split-effects", telegramUserId: 20260713);
        player.SharedCups = 2;
        player.MissedMatchCredit = 2;

        var oldFinished = h.AddMatch(status: KeoBiaMatchStatus.Finished,
            resultChoice: KeoBiaBetChoice.Away, stage: "Round of 16", kickoffAt: BeforeSwitch);
        var newFinished = h.AddMatch(status: KeoBiaMatchStatus.Finished,
            resultChoice: KeoBiaBetChoice.Away, stage: "Quarter-final", kickoffAt: AtSwitch);
        var oldPending = h.AddMatch(stage: "Round of 16", kickoffAt: BeforeSwitch);
        var newPending = h.AddMatch(stage: "Quarter-final", kickoffAt: AtSwitch);

        h.AddBet(player, oldFinished, cups: 1, isSettled: true, isCorrect: false);
        h.AddBet(player, newFinished, cups: 1, isSettled: true, isCorrect: false);
        h.AddBet(player, oldPending, cups: 1);
        h.AddBet(player, newPending, cups: 1);

        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.SharedBeer, 1, BeforeSwitch, "Tặng 1 cốc bia"));
        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.SharedBeer, 1, AtSwitch, "Tặng 1 gói lạc"));
        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.ReceivedBeer, -1, BeforeSwitch, "Nhận 1 cốc bia"));
        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.ReceivedBeer, -1, AtSwitch, "Nhận 1 gói lạc"));
        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.MissedMatchCredit, -1, BeforeSwitch, "Khuyến mãi cốc bia"));
        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.MissedMatchCredit, -1, AtSwitch, "Khuyến mãi gói lạc"));
        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.QuizCorrect, -1, BeforeSwitch, "Thưởng 1 cốc bia"));
        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.QuizCorrect, -1, AtSwitch, "Thưởng 1 gói lạc"));
        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.QuizWrong, 1, BeforeSwitch, "Phạt 1 cốc bia"));
        h.AddCupLogRaw(EffectLog(player, null, KeoBiaCupChangeType.QuizWrong, 1, AtSwitch, "Phạt 1 gói lạc"));
        h.AddCupLogRaw(EffectLog(player, oldFinished.Id, KeoBiaCupChangeType.HopeStarLose, 1, BeforeSwitch, "Sao Hi Vọng"));
        h.AddCupLogRaw(EffectLog(player, newFinished.Id, KeoBiaCupChangeType.DevilStarWin, -1, AtSwitch, "Sao Ma Quỷ"));
        h.AddCupLogRaw(EffectLog(player, oldFinished.Id, KeoBiaCupChangeType.CorrectScore, 1, BeforeSwitch, "Sai tỉ số"));
        h.AddCupLogRaw(EffectLog(player, newFinished.Id, KeoBiaCupChangeType.CorrectScore, -1, AtSwitch, "Đúng tỉ số"));
        await h.SaveAsync();

        var entry = Assert.Single(await h.Service.GetLossLeaderboardAsync());
        Assert.Equal((1, 1), (entry.SharedBeerCups, entry.SharedPeanutPacks));
        Assert.Equal((1, 1), (entry.ReceivedBeerCups, entry.ReceivedPeanutPacks));
        Assert.Equal((1, 1), (entry.PromoBeerCups, entry.PromoPeanutPacks));
        Assert.Equal((1, -1), (entry.StarBeerCupEffect, entry.StarPeanutPackEffect));
        Assert.Equal((1, 1), (entry.QuizRewardBeerCups, entry.QuizRewardPeanutPacks));
        Assert.Equal((1, 1), (entry.QuizPenaltyBeerCups, entry.QuizPenaltyPeanutPacks));
        Assert.Equal((0, 1), (entry.CorrectScoreRewardBeerCups, entry.CorrectScoreRewardPeanutPacks));
        Assert.Equal((1, 0), (entry.CorrectScorePenaltyBeerCups, entry.CorrectScorePenaltyPeanutPacks));

        var adminPlayer = Assert.Single((await h.Service.SearchPlayersAsync("split-effects")).Items);
        Assert.Equal((1, 1), (adminPlayer.MissedCreditBeerCups, adminPlayer.MissedCreditPeanutPacks));

        var history = await h.Service.GetPlayerHistoryAsync(player.PublicKey);
        Assert.True(history.Succeeded, history.Error);
        Assert.Equal((2, 2), (history.Value!.TotalBeerCups, history.Value.TotalPeanutPacks));
        Assert.Equal((1, 1), (history.Value.PendingBeerCups, history.Value.PendingPeanutPacks));
        Assert.Equal((1, 1), (history.Value.PromoBeerCups, history.Value.PromoPeanutPacks));
        Assert.Equal((1, 0), (history.Value.HopeStarBeerCupEffect, history.Value.HopeStarPeanutPackEffect));
        Assert.Equal((0, -1), (history.Value.DevilStarBeerCupEffect, history.Value.DevilStarPeanutPackEffect));
        Assert.Equal((1, 1), (history.Value.QuizRewardBeerCups, history.Value.QuizRewardPeanutPacks));
        Assert.Equal((1, 1), (history.Value.QuizPenaltyBeerCups, history.Value.QuizPenaltyPeanutPacks));
        Assert.Equal((1, 0), (history.Value.CorrectScorePenaltyBeerCups, history.Value.CorrectScorePenaltyPeanutPacks));
        Assert.Equal((0, 1), (history.Value.CorrectScoreRewardBeerCups, history.Value.CorrectScoreRewardPeanutPacks));
    }

    private static KeoBiaTestHarness CreatePaymentHarness() => new(
        configuration: new Dictionary<string, string?>
        {
            ["KeoBia:Payment:BankCode"] = "VCB",
            ["KeoBia:Payment:AccountNumber"] = "0123456789",
            ["KeoBia:Payment:AccountName"] = "KEO BIA TEST"
        });

    private static KeoBiaCupLog WrongBetLog(KeoBiaPlayer player, KeoBiaMatch match, DateTime occurredAt) => new()
    {
        PlayerId = player.Id,
        MatchId = match.Id,
        ChangeType = KeoBiaCupChangeType.WrongBet,
        Cups = 1,
        Reason = "Wrong bet",
        BalanceAfter = 1,
        CreatedAt = occurredAt
    };

    private static KeoBiaCupLog EffectLog(
        KeoBiaPlayer player,
        Guid? matchId,
        string changeType,
        int cups,
        DateTime occurredAt,
        string reason) => new()
    {
        PlayerId = player.Id,
        MatchId = matchId,
        ChangeType = changeType,
        Cups = cups,
        Reason = reason,
        BalanceAfter = Math.Max(0, cups),
        CreatedAt = occurredAt
    };
}
