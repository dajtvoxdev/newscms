using NewsCMS.Application.Site;
using NewsCMS.Domain.Common;
using NewsCMS.Domain.Entities.Content;

namespace NewsCMS.Tests;

public sealed class MultiSiteBehaviorTests
{
    [Fact]
    public void MediaEntities_AreSiteScoped()
    {
        Assert.IsAssignableFrom<ISiteScoped>(new Media());
        Assert.IsAssignableFrom<ISiteScoped>(new MediaFolder());
    }

    [Fact]
    public void DashboardModuleState_ProductsOnly_DoesNotShowNews()
    {
        var state = DashboardModuleState.FromEnabledModules(["products"]);

        Assert.False(state.ShowNews);
        Assert.True(state.ShowProducts);
    }
}
