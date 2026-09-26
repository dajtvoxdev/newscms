using System.Net;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Providers;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdVideo.Tests.Providers;

/// <summary>P2 — mọi request tới provider và mọi lần tải file đều qua allowlist, kể cả sau redirect.</summary>
public class SsrfGuardingHandlerTests
{
    private static (HttpClient Client, ScriptedHttpHandler Inner, InMemorySettingsStore Settings) Build(
        Func<HttpRequestMessage, string?, HttpResponseMessage> respond,
        string? allowlist = "api.example.com, *.cdn.example.com")
    {
        var settings = new InMemorySettingsStore();

        if (allowlist is not null)
        {
            settings.Values[SettingKeys.ProviderHostAllowlist] = allowlist;
        }

        ServiceProvider sp = new ServiceCollection()
            .AddScoped<ISettingsStore>(_ => settings)
            .BuildServiceProvider();

        var inner = new ScriptedHttpHandler(respond);
        var guard = new SsrfGuardingHandler(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<SsrfGuardingHandler>.Instance)
        {
            InnerHandler = inner,
        };

        return (new HttpClient(guard), inner, settings);
    }

    private static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);

        return response;
    }

    [Fact]
    public async Task Host_trong_allowlist_thi_di_qua()
    {
        (HttpClient client, ScriptedHttpHandler inner, _) = Build((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        using HttpResponseMessage response = await client.GetAsync("https://api.example.com/v1/x");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("https://evil.example.net/steal")]
    [InlineData("http://api.example.com/v1")]
    public async Task Host_ngoai_allowlist_bi_chan_truoc_khi_ra_mang(string url)
    {
        (HttpClient client, ScriptedHttpHandler inner, _) = Build((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        await FluentActions.Awaiting(() => client.GetAsync(url)).Should().ThrowAsync<ProviderHostBlockedException>();

        inner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Thieu_setting_thi_chan_het()
    {
        (HttpClient client, _, _) = Build((_, _) => new HttpResponseMessage(HttpStatusCode.OK), allowlist: null);

        await FluentActions.Awaiting(() => client.GetAsync("https://api.example.com/v1")).Should().ThrowAsync<ProviderHostBlockedException>();
    }

    [Fact]
    public async Task Doi_setting_thi_co_hieu_luc_ngay()
    {
        (HttpClient client, _, InMemorySettingsStore settings) = Build((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        (await client.GetAsync("https://api.example.com/")).StatusCode.Should().Be(HttpStatusCode.OK);

        settings.Values[SettingKeys.ProviderHostAllowlist] = "other.example.com";

        await FluentActions.Awaiting(() => client.GetAsync("https://api.example.com/")).Should().ThrowAsync<ProviderHostBlockedException>();
    }

    [Fact]
    public async Task Redirect_sang_host_bi_chan_thi_nem()
    {
        (HttpClient client, ScriptedHttpHandler inner, _) = Build((_, _) => Redirect("http://169.254.169.254/"));

        await FluentActions.Awaiting(() => client.GetAsync("https://api.example.com/file")).Should().ThrowAsync<ProviderHostBlockedException>();

        inner.Requests.Should().ContainSingle("request thứ hai tới host bị chặn không bao giờ được gửi");
    }

    [Fact]
    public async Task Redirect_sang_host_khac_duoc_phep_thi_theo_nhung_bo_header_xac_thuc()
    {
        (HttpClient client, ScriptedHttpHandler inner, _) = Build((request, _) =>
            request.RequestUri!.Host == "api.example.com"
                ? Redirect("https://v3.cdn.example.com/a.mp4")
                : new HttpResponseMessage(HttpStatusCode.OK));

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/file");
        request.Headers.TryAddWithoutValidation("Authorization", "Key bi-mat-12345678");

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Requests.Should().HaveCount(2);
        inner.Requests[0].Headers.Should().ContainKey("Authorization");
        inner.Requests[1].Uri.Host.Should().Be("v3.cdn.example.com");
        inner.Requests[1].Headers.Should().NotContainKey("Authorization");
    }

    [Fact]
    public async Task Redirect_cung_origin_thi_giu_header_va_hieu_duong_dan_tuong_doi()
    {
        (HttpClient client, ScriptedHttpHandler inner, _) = Build((request, _) =>
            request.RequestUri!.AbsolutePath == "/a"
                ? Redirect("/b", HttpStatusCode.TemporaryRedirect)
                : new HttpResponseMessage(HttpStatusCode.OK));

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/a");
        request.Headers.TryAddWithoutValidation("Authorization", "Key x");

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        inner.Requests[1].Uri.AbsolutePath.Should().Be("/b");
        inner.Requests[1].Headers.Should().ContainKey("Authorization");
    }

    [Fact]
    public async Task POST_bi_307_thi_khong_tu_gui_lai()
    {
        (HttpClient client, ScriptedHttpHandler inner, _) = Build((_, _) => Redirect("https://api.example.com/other", HttpStatusCode.TemporaryRedirect));

        using HttpResponseMessage response = await client.PostAsync("https://api.example.com/jobs", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        inner.Requests.Should().ContainSingle("gửi lại POST tạo job là trả tiền hai lần");
    }

    [Fact]
    public async Task POST_bi_303_thi_theo_bang_GET()
    {
        (HttpClient client, ScriptedHttpHandler inner, _) = Build((request, _) =>
            request.Method == HttpMethod.Post ? Redirect("https://api.example.com/result", HttpStatusCode.SeeOther) : new HttpResponseMessage(HttpStatusCode.OK));

        (await client.PostAsync("https://api.example.com/jobs", new StringContent("{}"))).StatusCode.Should().Be(HttpStatusCode.OK);

        inner.Requests[1].Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task Redirect_khong_co_Location_hoac_vong_lap_thi_tra_phan_hoi_cuoi()
    {
        (HttpClient noLocation, _, _) = Build((_, _) => new HttpResponseMessage(HttpStatusCode.Found));

        (await noLocation.GetAsync("https://api.example.com/")).StatusCode.Should().Be(HttpStatusCode.Found);

        (HttpClient loop, ScriptedHttpHandler inner, _) = Build((_, _) => Redirect("https://api.example.com/again"));

        (await loop.GetAsync("https://api.example.com/")).StatusCode.Should().Be(HttpStatusCode.Found);
        inner.Requests.Should().HaveCount(SsrfGuardingHandler.MaxRedirects + 1);
    }
}
