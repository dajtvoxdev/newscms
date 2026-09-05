using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public static class KeoBiaQuestionStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";
    public const string Revealed = "Revealed";
}

public class KeoBiaQuestion : BaseEntity, ISoftDelete, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string Text { get; set; } = default!;

    // JSON array of { "key": "a", "label": "..." }. Choice keys are colon-free
    // so Telegram callback data "q:{id}:{key}" stays unambiguous.
    public string ChoicesJson { get; set; } = "[]";

    // Credit a correct voter gets; its beer/peanut type is fixed when the answer is revealed.
    public int RewardCups { get; set; }

    public int PenaltyCups { get; set; }

    // Null until the admin reveals the answer.
    public string? CorrectChoiceKey { get; set; }

    public string Status { get; set; } = KeoBiaQuestionStatus.Open;

    // Optional auto-close; voting locks at this time (enforced at vote/query time).
    public DateTime? ClosesAt { get; set; }
    public DateTime? RevealedAt { get; set; }

    // Set after broadcasting to the Telegram group, so the message can be edited later.
    public string? TelegramChatId { get; set; }
    public long? TelegramMessageId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<KeoBiaQuestionVote> Votes { get; set; } = new List<KeoBiaQuestionVote>();
}
