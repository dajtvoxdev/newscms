using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaQuestionVote : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid QuestionId { get; set; }
    public KeoBiaQuestion Question { get; set; } = default!;
    public Guid PlayerId { get; set; }
    public KeoBiaPlayer Player { get; set; } = default!;
    public string ChoiceKey { get; set; } = default!;

    // Set when the admin reveals the answer; the vote itself never writes a cup log.
    public bool IsSettled { get; set; }
    public bool? IsCorrect { get; set; }
    public DateTime? SettledAt { get; set; }

    // Signed cups already applied for this question: negative = reward, positive = penalty.
    // Reconciliation pointer so a re-reveal applies only the delta to KeoBiaCupLog.
    public int RewardedCups { get; set; }
}
