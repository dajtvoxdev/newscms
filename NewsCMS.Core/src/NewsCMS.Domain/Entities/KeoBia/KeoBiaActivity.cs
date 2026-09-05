using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.KeoBia;

public class KeoBiaActivity : BaseEntity, ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid? PlayerId { get; set; }
    public Guid? MatchId { get; set; }
    public string ActivityType { get; set; } = default!;
    public string PlayerName { get; set; } = default!;
    public string? AvatarUrl { get; set; }
    public string Text { get; set; } = default!;
    public string Badge { get; set; } = default!;
    public string? Choice { get; set; }
    public string? ChoiceLabel { get; set; }
    public int? Cups { get; set; }
}

public static class KeoBiaActivityType
{
    public const string Join = "join";
    public const string Prediction = "prediction";
}
