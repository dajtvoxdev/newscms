using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaPlayer : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public string PublicKey { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? AvatarUrl { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    // Telegram identity (verified via Telegram Login Widget). Required before betting.
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? TelegramFirstName { get; set; }
    public string? TelegramPhotoUrl { get; set; }
    public DateTime? TelegramVerifiedAt { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public int TotalBets { get; set; }
    public int TotalCups { get; set; }
    public int CorrectBets { get; set; }
    public int WrongBets { get; set; }
    // Auto-penalty cups for skipping matches (matches skipped before verifying +
    // every N consecutive matches skipped after verifying). Counts toward "bia mất".
    public int PenaltyCups { get; set; }
    public int SharedCups { get; set; }
    public int MissedMatchCredit { get; set; }
    public int HopeStars { get; set; }
    public int DevilStars { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime? UnitTransitionNoticeAcknowledgedAt { get; set; }
    public DateTime? UnitTransitionTelegramSentAt { get; set; }
    public DateTime? StoppedPlayingAt { get; set; }

    public ICollection<KeoBiaBet> Bets { get; set; } = new List<KeoBiaBet>();
    public ICollection<KeoBiaChatMessage> ChatMessages { get; set; } = new List<KeoBiaChatMessage>();
}
