namespace NewsCMS.Tests.KeoBia;

public sealed class NavbarUnitSummaryTests
{
    [Fact]
    public void Navbar_ShowsCollectedAndWithdrawnBeerAndPeanutTotals()
    {
        var view = ReadCanvasView();
        var navbar = Slice(
            view,
            "<div class=\"btn-opta btn-total-cups btn-total-cups--split\"",
            "</nav>");

        AssertTotalsAreSourcedCorrectly(view);
        Assert.Contains("data-lossboard-total-beer>@totalLostBeerCups</strong>", navbar);
        Assert.Contains("data-lossboard-total-peanut>@totalLostPeanutPacks</strong>", navbar);
        Assert.Contains("data-navbar-paid-beer>@totalPaidBeerCups</strong>", navbar);
        Assert.Contains("data-navbar-paid-peanut>@totalPaidPeanutPacks</strong>", navbar);
        AssertCollectedAndWithdrawnOrder(navbar, "data-navbar-paid-beer");
    }

    [Fact]
    public void Leaderboard_ShowsCollectedAndWithdrawnBeerAndPeanutTotals()
    {
        var view = ReadCanvasView();
        var summary = Slice(
            view,
            "<div class=\"lossboard-summary\">",
            "<div class=\"lossboard-list\">");

        AssertTotalsAreSourcedCorrectly(view);
        Assert.Contains("data-lossboard-total-beer>@totalLostBeerCups</strong>", summary);
        Assert.Contains("data-lossboard-total-peanut>@totalLostPeanutPacks</strong>", summary);
        Assert.Contains("data-lossboard-paid-beer>@totalPaidBeerCups</strong>", summary);
        Assert.Contains("data-lossboard-paid-peanut>@totalPaidPeanutPacks</strong>", summary);
        AssertCollectedAndWithdrawnOrder(summary, "data-lossboard-paid-beer");
    }

    [Fact]
    public void SummaryStyles_UseCacheVersionAndResponsiveBreakpoints()
    {
        var layout = ReadProjectFile(
            "src", "NewsCMS.Theme.KeoBia2026", "Areas", "KeoBia2026", "Views", "Shared", "_Layout.cshtml");
        var styles = ReadProjectFile(
            "src", "NewsCMS.Theme.KeoBia2026", "wwwroot", "css", "site.css");

        var responsiveStyles = Slice(
            styles,
            "@media (max-width: 760px) {\n    .modal-card--leaderboard {",
            "/* Restore the desktop three-column board");

        Assert.Contains("site.css?v=20260713-unit-summary1", layout);
        Assert.Contains("site.js?v=20260713-unit-summary1", layout);
        Assert.Contains("@media (max-width: 1700px) {\n    .appbar {\n        grid-template-columns: 1fr;", styles);
        Assert.Contains("grid-template-columns: minmax(150px, 0.72fr) repeat(2, minmax(0, 1fr));", styles);
        Assert.Contains("gap: 16px;", styles);
        Assert.Contains(".modal-card--leaderboard > .lossboard-summary {\n        grid-template-columns: minmax(0, 1fr);", responsiveStyles);
        Assert.Contains("@media (max-width: 360px) {\n    .modal-card--leaderboard > .lossboard-summary .lossboard-summary-units {\n        grid-template-columns: minmax(0, 1fr);", styles);
    }

    private static void AssertTotalsAreSourcedCorrectly(string view)
    {
        Assert.Contains("var totalLostBeerCups = Model.LossLeaderboard.Sum(x => x.LostBeerCups);", view);
        Assert.Contains("var totalLostPeanutPacks = Model.LossLeaderboard.Sum(x => x.LostPeanutPacks);", view);
        Assert.Contains("var totalPaidBeerCups = Model.LossLeaderboard.Sum(x => x.PaidLegacyBeerCups);", view);
        Assert.Contains("var totalPaidPeanutPacks = Model.LossLeaderboard.Sum(x => x.PaidPeanutPacks);", view);
    }

    private static void AssertCollectedAndWithdrawnOrder(string section, string paidHook)
    {
        var collectedLabelIndex = section.IndexOf("<em>đã gom</em>", StringComparison.Ordinal);
        var paidValueIndex = section.IndexOf(paidHook, StringComparison.Ordinal);
        var withdrawnLabelIndex = section.IndexOf("<em>đã rút</em>", StringComparison.Ordinal);

        Assert.True(collectedLabelIndex >= 0, "Missing ĐÃ GOM label.");
        Assert.True(paidValueIndex > collectedLabelIndex, "Withdrawn totals must follow collected totals.");
        Assert.True(withdrawnLabelIndex > paidValueIndex, "ĐÃ RÚT label must follow withdrawn totals.");
    }

    private static string ReadCanvasView() => ReadProjectFile(
        "src", "NewsCMS.Theme.KeoBia2026", "Areas", "KeoBia2026", "Views", "Home", "_Canvas.cshtml");

    private static string ReadProjectFile(params string[] relativePath)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

        return File.ReadAllText(Path.Combine([projectRoot, .. relativePath]));
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");

        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End marker not found after: {start}");
        return source[startIndex..endIndex];
    }
}
