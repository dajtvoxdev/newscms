using System.Net;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Domain.Entities.VideoStudio;

namespace NewsCMS.Tests.VideoStudio;

public sealed class AdVideoConnectionServiceTests
{
    private const string OperatorKey = "advop_khoa-quan-tri-rat-bi-mat-0000000000";

    [Fact]
    public async Task Dan_nham_tenant_key_vao_o_operator_key_bi_chan_voi_loi_giai_thich()
    {
        using var h = new AdVideoTestHarness();

        Result result = await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", "adv_day-la-key-cua-site"));

        Assert.False(result.Succeeded);
        Assert.Contains("advop_", result.Error);
    }

    [Fact]
    public async Task Operator_key_luu_ma_hoa_va_chi_hien_prefix()
    {
        using var h = new AdVideoTestHarness();

        Assert.True((await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test/", OperatorKey, 30))).Succeeded);

        AdVideoConnection row = await h.NewContext().AdVideoConnections.SingleAsync();
        Assert.NotEqual(OperatorKey, row.OperatorKeyEncrypted);
        Assert.DoesNotContain("rat-bi-mat", row.OperatorKeyEncrypted);
        Assert.Equal("http://advideo.test", row.BaseUrl);

        AdVideoConnectionDto dto = await h.Connection.GetAsync();
        Assert.True(dto.IsConfigured);
        Assert.Equal("advop_khoa-q", dto.OperatorKeyPrefix);
        Assert.Equal(30, dto.TimeoutSeconds);
    }

    [Fact]
    public async Task Sua_dia_chi_khong_nhap_lai_key_thi_giu_key_cu()
    {
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://a.test", OperatorKey));
        string before = (await h.NewContext().AdVideoConnections.SingleAsync()).OperatorKeyEncrypted!;

        Assert.True((await h.Connection.SaveAsync(new AdVideoConnectionInput("http://b.test", null))).Succeeded);

        AdVideoConnection after = await h.NewContext().AdVideoConnections.SingleAsync();
        Assert.Equal("http://b.test", after.BaseUrl);
        Assert.Equal(before, after.OperatorKeyEncrypted);
    }

    [Fact]
    public async Task Ket_noi_site_tao_tenant_ben_AdVideo_va_cat_key_ma_hoa_vao_dung_site()
    {
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));
        Guid siteId = await h.AddSiteAsync("CHU Kafe");
        Guid tenantId = Guid.NewGuid();

        h.Http.Respond = r => r.Uri.AbsolutePath.EndsWith("/v1/admin/tenants")
            ? RecordingHandler.Json(HttpStatusCode.Created, $$"""
                {"tenant":{"id":"{{tenantId}}","name":"CHU Kafe","api_key_prefix":"adv_AbCdEfGh","is_active":true,"created_at":"2026-09-28T10:00:00Z"},"api_key":"adv_AbCdEfGh-bi-mat"}
                """)
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        Result result = await h.Connection.LinkSiteAsync(siteId);

        Assert.True(result.Succeeded, result.Error);

        SiteAdVideoTenant link = await h.NewContext().SiteAdVideoTenants.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(siteId, link.SiteId);
        Assert.Equal(tenantId, link.AdVideoTenantId);
        Assert.DoesNotContain("bi-mat", link.ApiKeyEncrypted);

        (var endpoint, _) = await h.Keys.GetTenantEndpointAsync(siteId, default);
        Assert.Equal("adv_AbCdEfGh-bi-mat", endpoint!.Key);

        AdVideoSiteLinkDto listed = Assert.Single(await h.Connection.GetSiteLinksAsync());
        Assert.True(listed.IsLinked);
        Assert.Equal("adv_AbCdEfGh", listed.ApiKeyPrefix);

        Assert.False((await h.Connection.LinkSiteAsync(siteId)).Succeeded, "kết nối hai lần không được đẻ hai tenant");
    }

    [Fact]
    public async Task AdVideo_tu_choi_tao_tenant_thi_khong_ghi_lien_ket_nua_voi()
    {
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));
        Guid siteId = await h.AddSiteAsync("Site");
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.Unauthorized, "{}", "application/problem+json");

        Result result = await h.Connection.LinkSiteAsync(siteId);

        Assert.False(result.Succeeded);
        Assert.Empty(await h.NewContext().SiteAdVideoTenants.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Bo_ket_noi_ma_khong_tat_duoc_tenant_thi_giu_lien_ket()
    {
        // Xoá liên kết khi tenant còn sống là mất dấu một key vẫn dùng được.
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));
        Guid siteId = await h.AddSiteAsync("Site");
        h.Db.SiteAdVideoTenants.Add(new SiteAdVideoTenant { SiteId = siteId, AdVideoTenantId = Guid.NewGuid(), ApiKeyEncrypted = h.Keys.ProtectTenantKey("adv_x"), ApiKeyPrefix = "adv_x" });
        await h.Db.SaveChangesAsync();
        h.Http.Respond = _ => throw new HttpRequestException("Connection refused");

        Result result = await h.Connection.UnlinkSiteAsync(siteId);

        Assert.False(result.Succeeded);
        Assert.Single(await h.NewContext().SiteAdVideoTenants.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Cap_lai_key_thay_key_da_luu()
    {
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));
        Guid siteId = await h.AddSiteAsync("Site");
        Guid tenantId = Guid.NewGuid();
        h.Db.SiteAdVideoTenants.Add(new SiteAdVideoTenant { SiteId = siteId, AdVideoTenantId = tenantId, ApiKeyEncrypted = h.Keys.ProtectTenantKey("adv_cu"), ApiKeyPrefix = "adv_cu" });
        await h.Db.SaveChangesAsync();

        h.Http.Respond = r => RecordingHandler.Json(HttpStatusCode.OK, $$"""
            {"tenant":{"id":"{{tenantId}}","name":"Site","api_key_prefix":"adv_MoiMoiMo","is_active":true,"created_at":"2026-09-28T10:00:00Z"},"api_key":"adv_MoiMoiMo-moi"}
            """);

        Assert.True((await h.Connection.RotateSiteKeyAsync(siteId)).Succeeded);

        Assert.EndsWith($"/v1/admin/tenants/{tenantId}/rotate-key", h.Http.Requests.Single().Uri.AbsolutePath);
        (var endpoint, _) = await h.Keys.GetTenantEndpointAsync(siteId, default);
        Assert.Equal("adv_MoiMoiMo-moi", endpoint!.Key);
    }

    [Fact]
    public async Task Kiem_tra_ket_noi_sai_key_thi_bao_loi_du_healthz_khoe()
    {
        // /healthz không cần key — chỉ gọi healthz thì key sai vẫn "xanh".
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));

        h.Http.Respond = r => r.Uri.AbsolutePath == "/healthz"
            ? RecordingHandler.Json(HttpStatusCode.OK, """{"status":"healthy","checks":{"database":"ok"}}""")
            : RecordingHandler.Json(HttpStatusCode.Unauthorized, """{"title":"Thiếu hoặc sai operator key"}""", "application/problem+json");

        Result<AdVideoHealthDto> result = await h.Connection.TestAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("401", result.Error);
    }
}
