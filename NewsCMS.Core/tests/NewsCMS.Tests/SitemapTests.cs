using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Web.Pages;

namespace NewsCMS.Tests;

public sealed class SitemapTests
{
    [Fact]
    public async Task OnGetAsync_IncludesCommitmentPage()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        var model = new SitemapModel(db, new FakeCurrentSite("HaiLuuNguoc"))
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Request =
                    {
                        Scheme = "https",
                        Host = new HostString("hailuunguoc.vn")
                    }
                }
            }
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Contains(model.Urls, x => x.Loc == "https://hailuunguoc.vn/cam-ket");
    }

    [Fact]
    public async Task OnGetAsync_DoesNotIncludeCommitmentPage_ForOtherThemes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        var model = new SitemapModel(db, new FakeCurrentSite("PhuPhucYaka"))
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Request =
                    {
                        Scheme = "https",
                        Host = new HostString("phuphucyaka.vn")
                    }
                }
            }
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.DoesNotContain(model.Urls, x => x.Loc == "https://phuphucyaka.vn/cam-ket");
    }

    private sealed class FakeCurrentSite(string theme) : ICurrentSite
    {
        public Guid SiteId { get; private set; } = Guid.NewGuid();
        public string Slug { get; private set; } = "test";
        public string Theme { get; private set; } = theme;
        public bool IsResolved => true;

        public void Set(Guid siteId, string slug, string theme)
        {
            SiteId = siteId;
            Slug = slug;
            Theme = theme;
        }
    }
}
