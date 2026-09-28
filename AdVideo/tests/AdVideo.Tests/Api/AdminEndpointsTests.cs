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

/// <summary>
/// API quản trị <c>/v1/admin</c>: ai vào được, và mỗi thao tác ghi đúng thứ mà CLI vẫn ghi.
/// </summary>
public sealed class AdminEndpointsTests
{
    // ------------------------------------------------------------ xác thực

    [Fact]
    public async Task Tenant_key_khong_mo_duoc_cua_quan_tri()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string tenantKey) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(tenantKey);

        HttpResponseMessage response = await client.GetAsync("/v1/admin/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Tenant_key_dat_vao_header_operator_cung_khong_qua()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string tenantKey) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateOperatorClient(tenantKey);

        (await client.GetAsync("/v1/admin/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Operator_key_khong_mo_duoc_cua_cua_khach()
    {
        // Operator không mang tenant nào. Lọt qua được thì endpoint của khách ném vì thiếu claim
        // tenant (500) — hoặc tệ hơn, chạy với tenant rỗng.
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        (await client.GetAsync($"/v1/ad-videos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Operator_key_da_thu_hoi_bi_tu_choi_ngay()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateOperatorKeyAsync(isActive: false);
        using HttpClient client = host.CreateOperatorClient(key);

        (await client.GetAsync("/v1/admin/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Operator_key_hop_le_vao_duoc_va_ghi_lan_dung_cuoi()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (Guid id, string key) = await host.CreateOperatorKeyAsync();
        using HttpClient client = host.CreateOperatorClient(key);

        HttpResponseMessage response = await client.GetAsync("/v1/admin/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        JsonElement settings = await ApiTestHost.ReadJsonAsync(response);
        settings.EnumerateArray().Select(s => s.GetProperty("key").GetString())
            .Should().Contain([SettingKeys.MaxConcurrentShots, SettingKeys.AiLabelOverlayText, SettingKeys.AiLabelFontObjectKey]);

        OperatorKey row = await host.InScopeAsync(sp =>
            sp.GetRequiredService<AdVideoDbContext>().OperatorKeys.SingleAsync(k => k.Id == id));

        row.LastUsedAt.Should().NotBeNull();
    }

    // ------------------------------------------------------------ setting

    [Fact]
    public async Task Setting_ngoai_khoang_bi_tu_choi_trong_khoang_thi_ghi_va_lam_moi_cache()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        // Đọc một lần để giá trị cũ nằm trong cache — phép kiểm cache chỉ có nghĩa khi có cache để hỏng.
        (await ReadSettingAsync(host, SettingKeys.MaxConcurrentShots)).Should().Be(1);

        HttpResponseMessage tooHigh = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, $"/v1/admin/settings/{SettingKeys.MaxConcurrentShots}", new { value = "64" });

        tooHigh.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        HttpResponseMessage ok = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, $"/v1/admin/settings/{SettingKeys.MaxConcurrentShots}", new { value = "3" });

        ok.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        JsonElement body = await ApiTestHost.ReadJsonAsync(ok);
        body.GetProperty("value").GetString().Should().Be("3");
        body.GetProperty("is_provisional").GetBoolean().Should().BeFalse("người vận hành gõ số là đã quyết định số đó");

        (await ReadSettingAsync(host, SettingKeys.MaxConcurrentShots)).Should().Be(3, "đổi setting không được đòi restart");
    }

    [Fact]
    public async Task Setting_so_thap_phan_doc_theo_dau_cham_khong_theo_culture_may()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        HttpResponseMessage response = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, $"/v1/admin/settings/{SettingKeys.MaxCostPerJobUsd}", new { value = "0.5" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);

        decimal stored = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ISettingsStore>().GetDecimalAsync(SettingKeys.MaxCostPerJobUsd, -1m));

        stored.Should().Be(0.5m);
    }

    [Fact]
    public async Task Khoa_la_thi_404_chu_khong_tao_moi()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        HttpResponseMessage response = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, "/v1/admin/settings/MaxConcurentShots", new { value = "2" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Khong_tat_duoc_nhan_AI_bang_chu_rong_va_khong_sua_tay_khoa_font()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        (await ApiTestHost.SendJsonAsync(
                client, HttpMethod.Put, $"/v1/admin/settings/{SettingKeys.AiLabelOverlayText}", new { value = "   " }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await ApiTestHost.SendJsonAsync(
                client, HttpMethod.Put, $"/v1/admin/settings/{SettingKeys.AiLabelFontObjectKey}", new { value = "fonts/bat-ky.ttf" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        HttpResponseMessage ok = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, $"/v1/admin/settings/{SettingKeys.AiLabelOverlayText}", new { value = "  Video tạo bởi AI " });

        ok.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        (await ApiTestHost.ReadJsonAsync(ok)).GetProperty("value").GetString().Should().Be("Video tạo bởi AI");
    }

    // ------------------------------------------------------------ credential

    [Fact]
    public async Task Nap_key_provider_chi_tra_ve_ban_che_va_key_ma_hoa_trong_DB()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        HttpResponseMessage response = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, "/v1/admin/credentials/kling", new { api_key = "fal-bi-mat-cua-khach-1234", priority = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        string raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("fal-bi-mat", "key gốc không bao giờ đi ngược ra khỏi API");

        JsonElement credential = JsonDocument.Parse(raw).RootElement.GetProperty("credential");
        credential.GetProperty("masked_key").GetString().Should().Be("****1234");
        credential.GetProperty("category").GetString().Should().Be("video");
        credential.GetProperty("priority").GetInt32().Should().Be(5);

        string list = await client.GetStringAsync("/v1/admin/credentials");
        list.Should().NotContain("fal-bi-mat");

        ResolvedCredential? resolved = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICredentialStore>().GetAsync(ProviderNames.Kling, ProviderCategory.Video));

        resolved!.ApiKey.Should().Be("fal-bi-mat-cua-khach-1234", "store giải mã được đúng key đã nạp qua API");
    }

    [Fact]
    public async Task Sua_credential_khong_gui_key_thi_giu_key_va_giu_cac_truong_khong_gui()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, "/v1/admin/credentials/kling", new { api_key = "fal-key-goc-9999", priority = 7, note = "gói trả trước" });

        HttpResponseMessage second = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, "/v1/admin/credentials/kling", new { is_active = false });

        second.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        JsonElement body = await ApiTestHost.ReadJsonAsync(second);

        body.GetProperty("key_changed").GetBoolean().Should().BeFalse();
        JsonElement credential = body.GetProperty("credential");
        credential.GetProperty("masked_key").GetString().Should().Be("****9999");
        credential.GetProperty("priority").GetInt32().Should().Be(7, "tắt provider không được làm mất thứ tự ưu tiên");
        credential.GetProperty("notes").GetString().Should().Be("gói trả trước");
        credential.GetProperty("is_active").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Provider_la_khong_co_descriptor_thi_422()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        HttpResponseMessage response = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, "/v1/admin/credentials/khong-ton-tai", new { api_key = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Tat_credential_thi_registry_khong_con_thay()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        await ApiTestHost.SendJsonAsync(client, HttpMethod.Put, "/v1/admin/credentials/kling", new { api_key = "k-1234" });

        HttpResponseMessage response = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Post, "/v1/admin/credentials/kling/deactivate", new { reason = "hết tiền" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        IReadOnlyList<ResolvedCredential> active = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICredentialStore>().ListActiveAsync(ProviderCategory.Video));

        active.Should().NotContain(c => c.Provider == ProviderNames.Kling);
    }

    // ------------------------------------------------------------ descriptor

    [Fact]
    public async Task Descriptor_dan_nguyen_van_file_co_comment_luu_duoc_bat_duoc_chay_kho_duoc()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        string file = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "providers", "fal-kling.json"));

        HttpResponseMessage created = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Post, "/v1/admin/descriptors", new { descriptor_json = file, note = "thử qua API" });

        created.StatusCode.Should().Be(HttpStatusCode.Created, host.Errors);
        JsonElement row = await ApiTestHost.ReadJsonAsync(created);
        row.GetProperty("is_active").GetBoolean().Should().BeFalse("bản mới luôn nằm tắt cho tới khi bật tường minh");
        int version = row.GetProperty("version").GetInt32();

        HttpResponseMessage preview = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Post, "/v1/admin/descriptors/preview", new { provider = "kling", version });

        preview.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        string previewBody = await preview.Content.ReadAsStringAsync();
        previewBody.Should().Contain("queue.fal.run");

        (await ApiTestHost.SendJsonAsync(client, HttpMethod.Post, $"/v1/admin/descriptors/kling/versions/{version}/activate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        JsonElement list = await ApiTestHost.ReadJsonAsync(await client.GetAsync("/v1/admin/descriptors?provider=kling"));
        list.EnumerateArray().Should().ContainSingle(d => d.GetProperty("is_active").GetBoolean());

        JsonElement full = await ApiTestHost.ReadJsonAsync(await client.GetAsync($"/v1/admin/descriptors/kling/versions/{version}"));
        full.GetProperty("descriptor").GetProperty("name").GetString().Should().Be("kling");
    }

    [Fact]
    public async Task Descriptor_hong_thi_422_liet_ke_loi_va_khong_luu()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        HttpResponseMessage response = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Post, "/v1/admin/descriptors", new { descriptor = new { schema = "advideo.provider/v1", name = "hong" } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);

        JsonElement list = await ApiTestHost.ReadJsonAsync(await client.GetAsync("/v1/admin/descriptors"));
        list.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Bat_ban_descriptor_khong_ton_tai_thi_404()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        (await ApiTestHost.SendJsonAsync(client, HttpMethod.Post, "/v1/admin/descriptors/kling/versions/9/activate", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------ prompt

    [Fact]
    public async Task Them_prompt_bat_buoc_ghi_ly_do_va_bat_ngay_duoc()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        (await ApiTestHost.SendJsonAsync(
                client, HttpMethod.Post, "/v1/admin/prompts/dao_dien_test/versions", new { kind = "director", content = "Bạn là đạo diễn." }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "thiếu change_note");

        HttpResponseMessage v1 = await ApiTestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            "/v1/admin/prompts/dao_dien_test/versions",
            new { kind = "director", content = "Bạn là đạo diễn.", change_note = "bản đầu", activate = true });

        v1.StatusCode.Should().Be(HttpStatusCode.Created, host.Errors);

        await ApiTestHost.SendJsonAsync(
            client,
            HttpMethod.Post,
            "/v1/admin/prompts/dao_dien_test/versions",
            new { kind = "director", content = "Bạn là đạo diễn quảng cáo.", change_note = "rõ vai hơn", activate = true });

        (await ApiTestHost.SendJsonAsync(client, HttpMethod.Post, "/v1/admin/prompts/dao_dien_test/versions/1/activate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "rollback về bản 1");

        string? active = await host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<IPromptStore>().GetActiveAsync("dao_dien_test"))?.Content);

        active.Should().Be("Bạn là đạo diễn.");
    }

    // ------------------------------------------------------------ tenant

    [Fact]
    public async Task Tao_tenant_tra_key_mot_lan_khong_cache_va_key_dung_duoc_ngay()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient admin = await host.CreateOperatorClientAsync();

        HttpResponseMessage created = await ApiTestHost.SendJsonAsync(admin, HttpMethod.Post, "/v1/admin/tenants", new { name = "CHU Kafe" });

        created.StatusCode.Should().Be(HttpStatusCode.Created, host.Errors);
        created.Headers.CacheControl?.NoStore.Should().BeTrue("phản hồi mang secret không được nằm trong cache");

        JsonElement body = await ApiTestHost.ReadJsonAsync(created);
        string apiKey = body.GetProperty("api_key").GetString()!;
        Guid tenantId = body.GetProperty("tenant").GetProperty("id").GetGuid();

        using HttpClient tenant = host.CreateTenantClient(apiKey);
        (await tenant.GetAsync($"/v1/ad-videos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "key hợp lệ: qua xác thực, chỉ không có job");

        string list = await admin.GetStringAsync("/v1/admin/tenants");
        list.Should().NotContain(apiKey).And.Contain(tenantId.ToString());
    }

    [Fact]
    public async Task Cap_lai_key_thi_key_cu_chet_ngay_tat_tenant_thi_key_moi_cung_chet()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient admin = await host.CreateOperatorClientAsync();
        (Guid tenantId, string oldKey) = await host.CreateTenantAsync();

        HttpResponseMessage rotated = await ApiTestHost.SendJsonAsync(admin, HttpMethod.Post, $"/v1/admin/tenants/{tenantId}/rotate-key", null);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        string newKey = (await ApiTestHost.ReadJsonAsync(rotated)).GetProperty("api_key").GetString()!;

        using (HttpClient withOld = host.CreateTenantClient(oldKey))
        {
            (await withOld.GetAsync($"/v1/ad-videos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using HttpClient withNew = host.CreateTenantClient(newKey);
        (await withNew.GetAsync($"/v1/ad-videos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        HttpResponseMessage deactivated = await ApiTestHost.SendJsonAsync(
            admin, HttpMethod.Post, $"/v1/admin/tenants/{tenantId}/deactivate", new { reason = "nợ cước" });

        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ApiTestHost.ReadJsonAsync(deactivated)).GetProperty("note").GetString().Should().Contain("nợ cước");

        (await withNew.GetAsync($"/v1/ad-videos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------ font nhãn AI

    [Fact]
    public async Task Tai_font_tieng_Viet_len_thi_luu_MinIO_theo_sha_va_ghi_setting()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        HttpResponseMessage response = await UploadFontAsync(client, await File.ReadAllBytesAsync(ExternalTools.FontFile), "DejaVuSans.ttf");

        response.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        JsonElement body = await ApiTestHost.ReadJsonAsync(response);
        string objectKey = body.GetProperty("object_key").GetString()!;
        objectKey.Should().MatchRegex("^fonts/[0-9a-f]{64}\\.ttf$");

        bool stored = await host.InScopeAsync(sp => sp.GetRequiredService<IStorageService>().ExistsAsync(Buckets.System, objectKey));
        stored.Should().BeTrue();

        string? setting = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ISettingsStore>().GetStringAsync(SettingKeys.AiLabelFontObjectKey));
        setting.Should().Be(objectKey);

        JsonElement current = await ApiTestHost.ReadJsonAsync(await client.GetAsync("/v1/admin/assets/label-font"));
        current.GetProperty("source").GetString().Should().Be("minio");

        (await client.DeleteAsync("/v1/admin/assets/label-font")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        current = await ApiTestHost.ReadJsonAsync(await client.GetAsync("/v1/admin/assets/label-font"));
        current.GetProperty("source").GetString().Should().Be("config");
    }

    [Fact]
    public async Task File_khong_phai_font_bi_tu_choi_va_khong_doi_setting()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        HttpResponseMessage response = await UploadFontAsync(client, "<html>khong phai font</html>"u8.ToArray(), "font.ttf");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        string? setting = await host.InScopeAsync(sp =>
            sp.GetRequiredService<ISettingsStore>().GetStringAsync(SettingKeys.AiLabelFontObjectKey));
        setting.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Doi_chu_nhan_sang_ky_tu_font_dang_dung_khong_co_thi_bi_chan()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        (await UploadFontAsync(client, await File.ReadAllBytesAsync(ExternalTools.FontFile), "DejaVuSans.ttf"))
            .StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);

        // U+1F916 (mặt robot) không có trong DejaVu Sans: drawtext sẽ vẽ ô vuông mà không báo lỗi.
        HttpResponseMessage response = await ApiTestHost.SendJsonAsync(
            client, HttpMethod.Put, $"/v1/admin/settings/{SettingKeys.AiLabelOverlayText}", new { value = "Tạo bởi AI \U0001F916" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static Task<HttpResponseMessage> UploadFontAsync(HttpClient client, byte[] bytes, string fileName)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);

        return client.PostAsync("/v1/admin/assets/label-font", content);
    }

    private static Task<int> ReadSettingAsync(ApiTestHost host, string key) =>
        host.InScopeAsync(sp => sp.GetRequiredService<ISettingsStore>().GetIntAsync(key, -1));
}
