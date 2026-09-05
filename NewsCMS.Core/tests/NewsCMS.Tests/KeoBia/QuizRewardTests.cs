using NewsCMS.Application.KeoBia;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Tests.KeoBia;

/// <summary>
/// Quick Q&A reward coverage: a correct answer credits <c>quiz_correct</c> cups (clamped so the
/// player's displayed loss never goes below 0), a wrong answer credits nothing, the reveal is
/// re-runnable (delta reconcile), and casting a vote never writes a cup log.
/// </summary>
public sealed class QuizRewardTests
{
    private static readonly List<KeoBiaQuestionChoiceDto> TwoChoices = new()
    {
        new KeoBiaQuestionChoiceDto("a", "Đội A"),
        new KeoBiaQuestionChoiceDto("b", "Đội B")
    };

    /// <summary>Seed a settled wrong bet so the player carries `cups` of base loss.</summary>
    private static void GiveBaseLoss(KeoBiaTestHarness h, KeoBiaPlayer player, int cups)
    {
        var match = h.AddMatch(status: KeoBiaMatchStatus.Finished, resultChoice: KeoBiaBetChoice.Away);
        h.AddBet(player, match, choice: KeoBiaBetChoice.Home, cups: cups, isSettled: true, isCorrect: false);
    }

    [Fact]
    public async Task Reveal_RewardsCorrectVoter_ClampedToLoss_LeavesWrongVoterUntouched()
    {
        using var h = new KeoBiaTestHarness();
        var correct = h.AddPlayer("correct", telegramUserId: 101);
        var wrong = h.AddPlayer("wrong", telegramUserId: 102);
        GiveBaseLoss(h, correct, 5);
        GiveBaseLoss(h, wrong, 5);
        await h.SaveAsync();

        var created = await h.Service.CreateQuestionAsync(new KeoBiaCreateQuestionDto("Ai vô địch?", TwoChoices, 3, 0, null));
        Assert.True(created.Succeeded, created.Error);
        var qid = created.Value!.Id;

        Assert.True((await h.Service.SubmitQuizVoteAsync(correct.PublicKey, qid, "a")).Succeeded);
        Assert.True((await h.Service.SubmitQuizVoteAsync(wrong.PublicKey, qid, "b")).Succeeded);

        // No cup log is written until the answer is revealed.
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.QuizCorrect));
        Assert.Empty(h.CupLogs(KeoBiaCupChangeType.QuizWrong));

        var reveal = await h.Service.RevealQuestionAnswerAsync(qid, "a");
        Assert.True(reveal.Succeeded, reveal.Error);
        Assert.Equal(1, reveal.Value!.RewardedCount);
        Assert.Equal(3, reveal.Value.TotalRewardCups);

        var quizLogs = h.CupLogs(KeoBiaCupChangeType.QuizCorrect);
        var correctLog = Assert.Single(quizLogs, x => x.PlayerId == correct.Id);
        Assert.Equal(-3, correctLog.Cups);
        Assert.DoesNotContain(quizLogs, x => x.PlayerId == wrong.Id);
        Assert.DoesNotContain(h.CupLogs(KeoBiaCupChangeType.QuizWrong), x => x.PlayerId == wrong.Id);

        var board = await h.Service.GetLossLeaderboardAsync();
        Assert.Equal(5 - 3, Assert.Single(board, x => x.Id == correct.Id).LostCups);
        Assert.Equal(3, Assert.Single(board, x => x.Id == correct.Id).QuizRewardCups);
        Assert.Equal(5, Assert.Single(board, x => x.Id == wrong.Id).LostCups);
    }

    [Fact]
    public async Task Reveal_DoesNotPushLossBelowZero_NoRowWhenNothingToReward()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("zero-loss", telegramUserId: 201); // no losing bets => base loss 0
        await h.SaveAsync();

        var created = await h.Service.CreateQuestionAsync(new KeoBiaCreateQuestionDto("Q?", TwoChoices, 5, 0, null));
        var qid = created.Value!.Id;
        Assert.True((await h.Service.SubmitQuizVoteAsync(player.PublicKey, qid, "a")).Succeeded);

        var reveal = await h.Service.RevealQuestionAnswerAsync(qid, "a");
        Assert.True(reveal.Succeeded, reveal.Error);
        Assert.Equal(0, reveal.Value!.RewardedCount);

        // Clamp at 0: nothing to reduce => no quiz_correct row, displayed loss stays 0.
        Assert.DoesNotContain(h.CupLogs(KeoBiaCupChangeType.QuizCorrect), x => x.PlayerId == player.Id);
        var board = await h.Service.GetLossLeaderboardAsync();
        Assert.Equal(0, Assert.Single(board, x => x.Id == player.Id).LostCups);
    }

    [Fact]
    public async Task ReReveal_WithDifferentAnswer_ReversesAndReGrants()
    {
        using var h = new KeoBiaTestHarness();
        var votedA = h.AddPlayer("voted-a", telegramUserId: 301);
        var votedB = h.AddPlayer("voted-b", telegramUserId: 302);
        GiveBaseLoss(h, votedA, 5);
        GiveBaseLoss(h, votedB, 5);
        await h.SaveAsync();

        var created = await h.Service.CreateQuestionAsync(new KeoBiaCreateQuestionDto("Q?", TwoChoices, 2, 0, null));
        var qid = created.Value!.Id;
        Assert.True((await h.Service.SubmitQuizVoteAsync(votedA.PublicKey, qid, "a")).Succeeded);
        Assert.True((await h.Service.SubmitQuizVoteAsync(votedB.PublicKey, qid, "b")).Succeeded);

        // First reveal: A correct.
        Assert.True((await h.Service.RevealQuestionAnswerAsync(qid, "a")).Succeeded);
        // Second reveal with the other answer: A becomes wrong (reversed), B becomes correct.
        Assert.True((await h.Service.RevealQuestionAnswerAsync(qid, "b")).Succeeded);

        var quizLogs = h.CupLogs(KeoBiaCupChangeType.QuizCorrect).Concat(h.CupLogs(KeoBiaCupChangeType.QuizWrong));
        Assert.Equal(0, quizLogs.Where(x => x.PlayerId == votedA.Id).Sum(x => x.Cups));   // net reversed
        Assert.Equal(-2, quizLogs.Where(x => x.PlayerId == votedB.Id).Sum(x => x.Cups));  // now granted

        var board = await h.Service.GetLossLeaderboardAsync();
        Assert.Equal(5, Assert.Single(board, x => x.Id == votedA.Id).LostCups);     // back to full loss
        Assert.Equal(5 - 2, Assert.Single(board, x => x.Id == votedB.Id).LostCups); // reduced
    }

    [Fact]
    public async Task MultipleCorrectQuizRewards_DoNotBankBeyondCurrentLoss()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("no-bank", telegramUserId: 401);
        GiveBaseLoss(h, player, 5);
        await h.SaveAsync();

        var first = await h.Service.CreateQuestionAsync(new KeoBiaCreateQuestionDto("Q1?", TwoChoices, 3, 0, null));
        var second = await h.Service.CreateQuestionAsync(new KeoBiaCreateQuestionDto("Q2?", TwoChoices, 3, 0, null));
        Assert.True((await h.Service.SubmitQuizVoteAsync(player.PublicKey, first.Value!.Id, "a")).Succeeded);
        Assert.True((await h.Service.SubmitQuizVoteAsync(player.PublicKey, second.Value!.Id, "a")).Succeeded);

        Assert.True((await h.Service.RevealQuestionAnswerAsync(first.Value.Id, "a")).Succeeded);
        Assert.True((await h.Service.RevealQuestionAnswerAsync(second.Value.Id, "a")).Succeeded);

        var quizNet = h.CupLogs(KeoBiaCupChangeType.QuizCorrect)
            .Where(x => x.PlayerId == player.Id)
            .Sum(x => x.Cups);
        Assert.Equal(-5, quizNet);

        var board = await h.Service.GetLossLeaderboardAsync();
        var row = Assert.Single(board, x => x.Id == player.Id);
        Assert.Equal(0, row.LostCups);
        Assert.Equal(5, row.QuizRewardCups);
    }

    [Fact]
    public async Task Reveal_WrongAnswerWithPenalty_AddsQuizWrongLogAndBalance()
    {
        using var h = new KeoBiaTestHarness();
        var player = h.AddPlayer("wrong-penalty", telegramUserId: 501);
        GiveBaseLoss(h, player, 5);
        await h.SaveAsync();

        var created = await h.Service.CreateQuestionAsync(new KeoBiaCreateQuestionDto("Q?", TwoChoices, 3, 2, null));
        var qid = created.Value!.Id;
        Assert.True((await h.Service.SubmitQuizVoteAsync(player.PublicKey, qid, "b")).Succeeded);

        var reveal = await h.Service.RevealQuestionAnswerAsync(qid, "a");
        Assert.True(reveal.Succeeded, reveal.Error);
        Assert.Equal(1, reveal.Value!.PenalizedCount);
        Assert.Equal(2, reveal.Value.TotalPenaltyCups);

        var log = Assert.Single(h.CupLogs(KeoBiaCupChangeType.QuizWrong), x => x.PlayerId == player.Id);
        Assert.Equal(2, log.Cups);

        var board = await h.Service.GetLossLeaderboardAsync();
        Assert.Equal(7, Assert.Single(board, x => x.Id == player.Id).LostCups);
    }
}
