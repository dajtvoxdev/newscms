using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaBeerPayment : BaseEntity, ISiteScoped, ISoftDelete
{
    public Guid SiteId { get; set; }
    public Guid PlayerId { get; set; }
    public string Code { get; set; } = default!;
    public int Cups { get; set; }
    public int CoinAmount { get; set; }
    public string Status { get; set; } = KeoBiaBeerPaymentStatus.Pending;
    public string QrUrl { get; set; } = default!;
    public string TransferContent { get; set; } = default!;
    public string? ProviderTransactionId { get; set; }
    public string? ProviderPayloadJson { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public KeoBiaPlayer Player { get; set; } = default!;
}

public static class KeoBiaBeerPaymentStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";
}
