using System.Net;
using System.Text.Json;
using AdVideo.Core.Entities;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdVideo.Tests.Api;

/// <summary>
/// Hợp đồng HTTP của <c>/v1/ad-videos</c> qua host thật: xác thực, chống trùng, kiểm dữ liệu, cách ly tenant.
/// </summary>
public sealed class AdVideoEndpointsTests
{
    /// <summary>Request tối thiểu hợp lệ, cùng hình dạng với <c>samples/minimal-request.json</c>.</summary>
    internal const string ValidRequest = """
        {
          "brief": {
            "product_name": "Cà phê CHU",
            "prompt": "Cận cảnh ly cà phê phin trên bàn gỗ, ánh sáng buổi sáng.",
            "duration_seconds": 12,
            "aspect_ratio": "9:16"
          },
          "assets": { "product_images": ["https://anh-gia.test/ca-phe.png"] },
          "voice": { "script": "Cà phê CHU rang mộc từ hạt Arabica Cầu Đất, thơm mùi ca cao và mật ong." },
          "audio": { "native_sound": "off" },
          "options": { "quality": "standard", "has_person": false }
        }
        """;

    [Fact]
    public async Task Khong_gui_key_thi_401_kem_problem_details()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = host.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync($"/v1/ad-videos/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Key_sai_thi_401()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient("adv_khong-phai-key-that-nao-ca");

        HttpResponseMessage response = await client.GetAsync($"/v1/ad-videos/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tenant_dang_tat_thi_401_du_key_dung()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string apiKey) = await host.CreateTenantAsync(isActive: false);
        using HttpClient client = host.CreateTenantClient(apiKey);

        HttpResponseMessage response = await client.GetAsync($"/v1/ad-videos/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Thieu_idempotency_key_thi_400_va_khong_tao_job()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string apiKey) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(apiKey);

        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", ValidRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Jobs.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_hop_le_thi_202_ghi_job_va_day_vao_hang_doi_dung_mot_lan()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (Guid tenantId, string apiKey) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(apiKey);

        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", ValidRequest, "k-1");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, host.Errors);
        JsonElement body = await ApiTestHost.ReadJsonAsync(response);
        Guid jobId = body.GetProperty("job_id").GetGuid();

        response.Headers.Location!.ToString().Should().Be($"/v1/ad-videos/{jobId}");
        body.GetProperty("status").GetString().Should().Be("queued");

        AdVideoJob job = await host.InScopeAsync(sp =>
            sp.GetRequiredService<AdVideoDbContext>().Jobs.SingleAsync(j => j.Id == jobId));

        job.TenantId.Should().Be(tenantId, "tenant lấy từ key, không từ thân request");
        host.Jobs.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task Gui_lai_cung_key_cung_noi_dung_thi_tra_job_cu_khong_day_hang_doi_lan_hai()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string apiKey) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(apiKey);

        HttpResponseMessage first = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", ValidRequest, "k-lap");
        HttpResponseMessage second = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", ValidRequest, "k-lap");

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ApiTestHost.ReadJsonAsync(second)).GetProperty("job_id").GetGuid()
            .Should().Be((await ApiTestHost.ReadJsonAsync(first)).GetProperty("job_id").GetGuid());

        host.Jobs.Created.Should().ContainSingle("gửi lại vì timeout không được đẻ ra job thứ hai — mỗi job là tiền thật");
    }

    [Fact]
    public async Task Cung_key_khac_noi_dung_thi_409()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string apiKey) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(apiKey);

        await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", ValidRequest, "k-doi");

        string changed = ValidRequest.Replace("Cà phê CHU\"", "Trà CHU\"", StringComparison.Ordinal);
        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", changed, "k-doi");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Du_lieu_sai_thi_400_liet_ke_du_moi_loi_mot_luot()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string apiKey) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(apiKey);

        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", "{}", "k-rong");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");

        errors.TryGetProperty("brief.prompt", out _).Should().BeTrue();
        errors.TryGetProperty("assets.product_images", out _).Should().BeTrue();
        host.Jobs.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Ep_provider_khong_nam_trong_allowlist_thi_422()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string apiKey) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(apiKey);

        string forced = ValidRequest.Replace("\"has_person\": false", "\"has_person\": false, \"provider\": \"kling\"", StringComparison.Ordinal);

        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", forced, "k-ep");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        host.Jobs.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Tenant_B_khong_thay_job_cua_tenant_A()
    {
        // Chiều phủ định của cách ly tenant (B4): filter có chạy trong mọi test khác, nhưng chưa
        // phép kiểm nào chứng minh nó CHẶN — chỉ chứng minh nó cho qua đúng người.
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string keyA) = await host.CreateTenantAsync("A");
        (_, string keyB) = await host.CreateTenantAsync("B");

        using HttpClient clientA = host.CreateTenantClient(keyA);
        using HttpClient clientB = host.CreateTenantClient(keyB);

        HttpResponseMessage created = await ApiTestHost.PostJsonAsync(clientA, "/v1/ad-videos", ValidRequest, "k-a");
        Guid jobOfA = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("job_id").GetGuid();

        (await clientA.GetAsync($"/v1/ad-videos/{jobOfA}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientB.GetAsync($"/v1/ad-videos/{jobOfA}")).StatusCode.Should().Be(
            HttpStatusCode.NotFound, "404 chứ không phải 403: 403 xác nhận rằng job đó tồn tại");
    }

    [Fact]
    public async Task Idempotency_key_cua_tenant_A_khong_tra_job_A_cho_tenant_B()
    {
        // Tra job trùng key đi qua global filter. Nếu filter không áp ở đây thì tenant B đoán
        // trúng key của A là đọc được job của A — và còn nhận nó như job của mình.
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string keyA) = await host.CreateTenantAsync("A");
        (_, string keyB) = await host.CreateTenantAsync("B");

        using HttpClient clientA = host.CreateTenantClient(keyA);
        using HttpClient clientB = host.CreateTenantClient(keyB);

        HttpResponseMessage ofA = await ApiTestHost.PostJsonAsync(clientA, "/v1/ad-videos", ValidRequest, "k-chung");
        Guid jobOfA = (await ApiTestHost.ReadJsonAsync(ofA)).GetProperty("job_id").GetGuid();

        HttpResponseMessage ofB = await ApiTestHost.PostJsonAsync(clientB, "/v1/ad-videos", ValidRequest, "k-chung");

        // Unique index là (TenantId, IdempotencyKey): hai tenant trùng key là hai job riêng.
        ofB.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await ApiTestHost.ReadJsonAsync(ofB)).GetProperty("job_id").GetGuid().Should().NotBe(jobOfA);
        host.Jobs.Created.Should().HaveCount(2);
    }
}
