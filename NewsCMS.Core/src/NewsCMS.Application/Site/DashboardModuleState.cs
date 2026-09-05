namespace NewsCMS.Application.Site;

public sealed record DashboardModuleState(bool ShowNews, bool ShowProducts, bool ShowCommitments, bool ShowForms)
{
    public static DashboardModuleState FromEnabledModules(IEnumerable<string> moduleCodes)
    {
        var modules = moduleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var showNews = modules.Contains("news");
        return new DashboardModuleState(
            ShowNews: showNews,
            ShowProducts: modules.Contains("products"),
            ShowCommitments: modules.Contains("engagement") || showNews,
            ShowForms: modules.Contains("forms"));
    }
}
