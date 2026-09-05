using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Engagement;

public enum CommitmentSignatureKind
{
    Drawn = 0,
    Uploaded = 1
}

public class Commitment : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }

    public string DisplayName { get; set; } = default!;
    public string Message { get; set; } = default!;
    public CommitmentSignatureKind SignatureKind { get; set; }
    public string SignatureUrl { get; set; } = default!;
    public string SignerKey { get; set; } = default!;
    public string IpHash { get; set; } = default!;
    public string UserAgent { get; set; } = default!;
    public bool IsHidden { get; set; }
}
