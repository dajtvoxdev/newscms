using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdVideo.Tests.Api;

/// <summary>Giọng đọc qua HTTP: danh sách, clone có xác nhận quyền, cách ly tenant, chọn giọng khi tạo video.</summary>
public sealed class VoiceEndpointsTests
{
    /// <summary>File WAV hợp lệ tối thiểu (header RIFF/WAVE) — đủ để qua nhận dạng định dạng.</summary>
    private static readonly byte[] Wav = [.."RIFF"u8, 36, 0, 0, 0, .."WAVE"u8, .."fmt "u8, 16, 0, 0, 0, 1, 0, 1, 0, 0x40, 0x1F, 0, 0, 0x80, 0x3E, 0, 0, 2, 0, 16, 0, .."data"u8, 0, 0, 0, 0];

    private const string Consent = "Giọng của chị Lan, chủ cửa hàng, đồng ý bằng văn bản ngày 28/09.";

    [Fact]
    public async Task Danh_sach_co_giong_co_san_va_khong_lo_ten_engine()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        HttpResponseMessage response = await client.GetAsync("/v1/voices");

        response.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        string raw = await response.Content.ReadAsStringAsync();
        JsonElement list = JsonDocument.Parse(raw).RootElement;

        list.GetArrayLength().Should().Be(2);
        list.EnumerateArray().Should().OnlyContain(v => v.GetProperty("kind").GetString() == "preset");
        raw.Should().NotContain("\"provider\"", "khách chọn giọng, không chọn nhà cung cấp");
    }

    [Fact]
    public async Task Clone_co_xac_nhan_thi_luu_mau_lam_bang_chung_va_chi_tenant_do_thay()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (Guid tenantA, string keyA) = await host.CreateTenantAsync("A");
        (_, string keyB) = await host.CreateTenantAsync("B");
        using HttpClient a = host.CreateTenantClient(keyA);
        using HttpClient b = host.CreateTenantClient(keyB);

        HttpResponseMessage created = await CloneAsync(a, "Giọng chị Lan", Consent, confirmed: true, ("lan.wav", Wav));

        created.StatusCode.Should().Be(HttpStatusCode.Created, host.Errors);
        JsonElement voice = await ApiTestHost.ReadJsonAsync(created);
        Guid voiceId = voice.GetProperty("id").GetGuid();
        voice.GetProperty("kind").GetString().Should().Be("cloned");
        voice.GetProperty("preview_url").GetString().Should().NotBeNullOrEmpty("nghe lại mẫu đã tải lên");

        VoiceProfile row = await host.InScopeAsync(sp => sp.GetRequiredService<AdVideoDbContext>().VoiceProfiles.SingleAsync(v => v.Id == voiceId));
        row.TenantId.Should().Be(tenantA);
        row.ConsentStatement.Should().Be(Consent);
        row.ConsentedBy.Should().Be("marketing@site-a");
        row.ProviderVoiceId.Should().StartWith("fake-clone-");
        row.SampleObjectKey.Should().StartWith($"{tenantA:N}/voices/");

        (await host.InScopeAsync(sp => sp.GetRequiredService<IStorageService>().ExistsAsync(Buckets.Voice, row.SampleObjectKey!)))
            .Should().BeTrue();

        JsonElement ofB = await ApiTestHost.ReadJsonAsync(await b.GetAsync("/v1/voices"));
        ofB.EnumerateArray().Should().NotContain(v => v.GetProperty("id").GetGuid() == voiceId, "giọng clone chỉ tenant tạo ra mới thấy");

        (await b.DeleteAsync($"/v1/voices/{voiceId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        HttpResponseMessage useByB = await ApiTestHost.PostJsonAsync(b, "/v1/ad-videos", WithVoice(voiceId), "k-b");
        useByB.StatusCode.Should().Be(HttpStatusCode.BadRequest, "tenant khác không dùng được giọng clone của A");
    }

    [Fact]
    public async Task Thieu_xac_nhan_quyen_dung_giong_thi_tu_choi_va_khong_goi_engine()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        (await CloneAsync(client, "x", Consent, confirmed: false, ("a.wav", Wav))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CloneAsync(client, "x", "ok", confirmed: true, ("a.wav", Wav))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "lời xác nhận quá ngắn");
        (await CloneAsync(client, "x", Consent, confirmed: true, ("a.wav", "<html>khong phai audio</html>"u8.ToArray())))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        int clones = await host.InScopeAsync(sp => sp.GetRequiredService<AdVideoDbContext>().VoiceProfiles.CountAsync(v => v.Kind == VoiceProfileKind.Cloned));
        clones.Should().Be(0);
    }

    [Fact]
    public async Task Vuot_so_giong_clone_toi_da_thi_422()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        await host.InScopeAsync(sp => sp.GetRequiredService<ISettingsStore>().SetAsync(
            SettingKeys.MaxClonedVoicesPerTenant, "1", SettingValueType.Int, "test"));

        (await CloneAsync(client, "một", Consent, true, ("a.wav", Wav))).StatusCode.Should().Be(HttpStatusCode.Created, host.Errors);
        (await CloneAsync(client, "hai", Consent, true, ("b.wav", Wav))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Xoa_giong_clone_xoa_ca_ben_engine_va_bien_khoi_danh_sach()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        Guid voiceId = (await ApiTestHost.ReadJsonAsync(await CloneAsync(client, "tạm", Consent, true, ("a.wav", Wav)))).GetProperty("id").GetGuid();
        string providerVoiceId = await host.InScopeAsync(sp => sp.GetRequiredService<AdVideoDbContext>().VoiceProfiles.Where(v => v.Id == voiceId).Select(v => v.ProviderVoiceId).SingleAsync());

        (await client.DeleteAsync($"/v1/voices/{voiceId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement list = await ApiTestHost.ReadJsonAsync(await client.GetAsync("/v1/voices"));
        list.EnumerateArray().Should().NotContain(v => v.GetProperty("id").GetGuid() == voiceId);

        var fake = (AdVideo.Infrastructure.Providers.Fake.FakeProviderVoices)host.Services.GetServices<IProviderVoices>().Single();
        fake.Deleted.Should().Contain(providerVoiceId);
    }

    [Fact]
    public async Task Tao_video_voi_giong_co_san_ghi_giong_vao_job()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        Guid preset = (await ApiTestHost.ReadJsonAsync(await client.GetAsync("/v1/voices")))[0].GetProperty("id").GetGuid();

        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", WithVoice(preset), "k-voice");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, host.Errors);
        Guid jobId = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("job_id").GetGuid();

        AdVideoJob job = await host.InScopeAsync(sp => sp.GetRequiredService<AdVideoDbContext>().Jobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId));
        job.VoiceProfileId.Should().Be(preset);
    }

    [Fact]
    public async Task Id_giong_sai_dang_thi_400()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        string body = AdVideoEndpointsTests.ValidRequest.Replace(
            "\"voice\": { \"script\":", "\"voice\": { \"voice_profile_id\": \"21m00Tcm4TlvDq8ikWAM\", \"script\":", StringComparison.Ordinal);

        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", body, "k-sai");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("GET /v1/voices");
    }

    // ------------------------------------------------------------ quản trị

    [Fact]
    public async Task Quan_tri_nhap_giong_tu_thu_vien_engine_sua_va_go()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient admin = await host.CreateOperatorClientAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient tenant = host.CreateTenantClient(key);

        JsonElement library = await ApiTestHost.ReadJsonAsync(await admin.GetAsync("/v1/admin/voices/library?provider=fake"));
        library.GetArrayLength().Should().Be(2);
        library.EnumerateArray().Should().OnlyContain(v => v.GetProperty("already_added").GetBoolean(), "hai giọng giả đã seed sẵn");

        HttpResponseMessage added = await ApiTestHost.SendJsonAsync(admin, HttpMethod.Post, "/v1/admin/voices",
            new { name = "Nữ · miền Trung", provider = "fake", provider_voice_id = "fake-nu-trung", sort_order = 1 });
        added.StatusCode.Should().Be(HttpStatusCode.Created, host.Errors);
        Guid id = (await ApiTestHost.ReadJsonAsync(added)).GetProperty("id").GetGuid();

        (await ApiTestHost.SendJsonAsync(admin, HttpMethod.Post, "/v1/admin/voices",
            new { name = "trùng", provider = "fake", provider_voice_id = "fake-nu-trung" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        JsonElement forTenant = await ApiTestHost.ReadJsonAsync(await tenant.GetAsync("/v1/voices"));
        forTenant[0].GetProperty("id").GetGuid().Should().Be(id, "sort_order nhỏ nhất đứng đầu — cũng là giọng mặc định");

        (await ApiTestHost.SendJsonAsync(admin, HttpMethod.Put, $"/v1/admin/voices/{id}", new { is_active = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await ApiTestHost.ReadJsonAsync(await tenant.GetAsync("/v1/voices"))).EnumerateArray()
            .Should().NotContain(v => v.GetProperty("id").GetGuid() == id);

        (await admin.DeleteAsync($"/v1/admin/voices/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Giong_clone_cua_khach_khong_hien_trong_thu_vien_de_nhap_lam_giong_co_san()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient admin = await host.CreateOperatorClientAsync();
        (Guid tenantId, _) = await host.CreateTenantAsync();

        // Giọng clone của khách nằm trong thư viện tài khoản engine; giả lập bằng một voice id trùng
        // với giọng giả trong thư viện.
        await host.InScopeAsync(async sp =>
        {
            AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
            db.VoiceProfiles.Add(new VoiceProfile { TenantId = tenantId, Name = "của khách", Provider = ProviderNames.Fake, ProviderVoiceId = "fake-nam-nam", Kind = VoiceProfileKind.Cloned });
            await db.SaveChangesAsync();
        });

        JsonElement library = await ApiTestHost.ReadJsonAsync(await admin.GetAsync("/v1/admin/voices/library?provider=fake"));

        library.EnumerateArray().Should().NotContain(v => v.GetProperty("voice_id").GetString() == "fake-nam-nam");
    }

    private static Task<HttpResponseMessage> CloneAsync(HttpClient client, string name, string consent, bool confirmed, params (string Name, byte[] Bytes)[] files)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(name), "name" },
            { new StringContent(consent), "consent_statement" },
            { new StringContent(confirmed ? "true" : "false"), "consent_confirmed" },
            { new StringContent("marketing@site-a"), "consented_by" },
        };

        foreach ((string fileName, byte[] bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "files", fileName);
        }

        return client.PostAsync("/v1/voices", form);
    }

    private static string WithVoice(Guid voiceId) =>
        AdVideoEndpointsTests.ValidRequest.Replace(
            "\"voice\": { \"script\":", $"\"voice\": {{ \"voice_profile_id\": \"{voiceId}\", \"script\":", StringComparison.Ordinal);
}
