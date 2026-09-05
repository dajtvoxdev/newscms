namespace NewsCMS.Application.KeoBia;

public static class KeoBiaUnitCode
{
    public const string Beer = "beer";
    public const string Peanut = "peanut";
}

public static class KeoBiaUnitRules
{
    public const int BeerUnitPrice = 10_000;
    public const int PeanutUnitPrice = 5_000;
    public const int RegistrationMissedMatchPenaltyCapCups = 20;

    // Midnight 10/07/2026 in Vietnam. Quarter-finals and later always use peanuts,
    // while this timestamp is the fallback for non-match events such as quiz/share.
    public static readonly DateTime PeanutSwitchAtUtc =
        new(2026, 7, 9, 17, 0, 0, DateTimeKind.Utc);

    private static readonly string[] PeanutStages =
    [
        "Quarter-final",
        "Quarter-finals",
        "Quarterfinal",
        "Quarterfinals",
        "Semi-final",
        "Semi-finals",
        "Semifinal",
        "Semifinals",
        "Match for third place",
        "Third-place play-off",
        "Third place play-off",
        "Final"
    ];

    public static string GetUnitCode(string? stage, DateTime occurredAt) =>
        UsesPeanut(stage, occurredAt) ? KeoBiaUnitCode.Peanut : KeoBiaUnitCode.Beer;

    public static string GetCurrentUnitCode() => GetUnitCode(null, DateTime.UtcNow);

    public static bool UsesPeanut(string? stage, DateTime occurredAt)
    {
        if (!string.IsNullOrWhiteSpace(stage)
            && PeanutStages.Contains(stage.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return AsUtc(occurredAt) >= PeanutSwitchAtUtc;
    }

    public static string ShortLabel(string? unitCode) =>
        string.Equals(unitCode, KeoBiaUnitCode.Peanut, StringComparison.OrdinalIgnoreCase)
            ? "gói"
            : "cốc";

    public static string FullLabel(string? unitCode) =>
        string.Equals(unitCode, KeoBiaUnitCode.Peanut, StringComparison.OrdinalIgnoreCase)
            ? "gói lạc"
            : "cốc bia";

    public static string Emoji(string? unitCode) =>
        string.Equals(unitCode, KeoBiaUnitCode.Peanut, StringComparison.OrdinalIgnoreCase)
            ? "🥜"
            : "🍺";

    public static string FormatCount(int count, string? unitCode) =>
        $"{count} {FullLabel(unitCode)}";

    public static string FormatBreakdown(int beerCups, int peanutPacks)
    {
        var parts = new List<string>(2);
        if (beerCups > 0) parts.Add($"{beerCups} cốc bia");
        if (peanutPacks > 0) parts.Add($"{peanutPacks} gói lạc");
        return parts.Count == 0 ? "0 cốc bia + 0 gói lạc" : string.Join(" + ", parts);
    }

    public static int CalculatePaymentAmount(int beerCups, int peanutPacks) =>
        checked(beerCups * BeerUnitPrice + peanutPacks * PeanutUnitPrice);

    public static (int BeerCups, int PeanutPacks) TakeOldestFirst(
        int count,
        int availableBeerCups,
        int availablePeanutPacks)
    {
        var safeCount = Math.Max(0, count);
        var beerCups = Math.Min(Math.Max(0, availableBeerCups), safeCount);
        var peanutPacks = Math.Min(Math.Max(0, availablePeanutPacks), safeCount - beerCups);
        return (beerCups, peanutPacks);
    }

    public static (int BeerCups, int PeanutPacks) ReconcileBalance(
        int currentBalance,
        int attributedBeerCups,
        int attributedPeanutPacks)
    {
        if (currentBalance <= 0) return (0, 0);

        var beerCups = Math.Max(0, attributedBeerCups);
        var peanutPacks = Math.Max(0, attributedPeanutPacks);
        var attributed = beerCups + peanutPacks;

        if (attributed < currentBalance)
        {
            // Missing journal rows are legacy beer so old debt is never silently repriced.
            beerCups += currentBalance - attributed;
        }
        else if (attributed > currentBalance)
        {
            var credits = attributed - currentBalance;
            var beerCredit = Math.Min(beerCups, credits);
            beerCups -= beerCredit;
            credits -= beerCredit;
            peanutPacks = Math.Max(0, peanutPacks - credits);
        }

        return (beerCups, peanutPacks);
    }

    public static (int BeerCups, int PeanutPacks) DecodePaymentBreakdown(int units, int amount)
    {
        if (units <= 0) return (0, 0);

        var priceGap = BeerUnitPrice - PeanutUnitPrice;
        var beerCups = priceGap <= 0
            ? units
            : (amount - units * PeanutUnitPrice) / priceGap;
        beerCups = Math.Clamp(beerCups, 0, units);
        return (beerCups, units - beerCups);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
