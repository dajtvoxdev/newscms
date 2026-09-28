using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdVideo.Tests.Api;

/// <summary>
/// Ba cửa mà app cần ngoài tạo/xem job: tải ảnh lên kho, liệt kê job, huỷ job.
/// </summary>
public sealed class UploadAndJobListTests
{
    // ------------------------------------------------------------ upload

    [Fact]
    public async Task Tai_anh_len_luu_vao_adv_uploads_duoi_thu_muc_tenant()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (Guid tenantId, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        HttpResponseMessage response = await UploadAsync(client, StubImageHandler.OnePixelPng, "anh.png");

        response.StatusCode.Should().Be(HttpStatusCode.Created, host.Errors);
        JsonElement body = await ApiTestHost.ReadJsonAsync(response);
        Guid assetId = body.GetProperty("asset_id").GetGuid();
        body.GetProperty("content_type").GetString().Should().Be("image/png");
        body.GetProperty("reused").GetBoolean().Should().BeFalse();

        MediaAsset asset = await host.InScopeAsync(sp =>
            sp.GetRequiredService<AdVideoDbContext>().MediaAssets.SingleAsync(a => a.Id == assetId));

        asset.TenantId.Should().Be(tenantId);
        asset.Bucket.Should().Be(Buckets.Uploads);
        asset.ObjectKey.Should().StartWith($"{tenantId:N}/unassigned/productimage/");

        bool stored = await host.InScopeAsync(sp =>
            sp.GetRequiredService<IStorageService>().ExistsAsync(asset.Bucket, asset.ObjectKey));
        stored.Should().BeTrue();
    }

    [Fact]
    public async Task Tai_lai_cung_anh_thi_tra_asset_cu_trong_cung_tenant_nhung_khong_chia_se_voi_tenant_khac()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string keyA) = await host.CreateTenantAsync("A");
        (_, string keyB) = await host.CreateTenantAsync("B");
        using HttpClient a = host.CreateTenantClient(keyA);
        using HttpClient b = host.CreateTenantClient(keyB);

        Guid first = (await ApiTestHost.ReadJsonAsync(await UploadAsync(a, StubImageHandler.OnePixelPng, "1.png"))).GetProperty("asset_id").GetGuid();

        HttpResponseMessage again = await UploadAsync(a, StubImageHandler.OnePixelPng, "2.png");
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ApiTestHost.ReadJsonAsync(again)).GetProperty("asset_id").GetGuid().Should().Be(first);

        HttpResponseMessage otherTenant = await UploadAsync(b, StubImageHandler.OnePixelPng, "1.png");
        otherTenant.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ApiTestHost.ReadJsonAsync(otherTenant)).GetProperty("asset_id").GetGuid().Should().NotBe(first);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", "anh.png")]
    [InlineData("GIF89a khong nhan", "anh.gif")]
    public async Task File_khong_phai_JPEG_PNG_WebP_bi_tu_choi_du_duoi_file_noi_gi(string content, string name)
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        HttpResponseMessage response = await UploadAsync(client, System.Text.Encoding.UTF8.GetBytes(content), name);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Operator_key_khong_tai_anh_len_duoc()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        using HttpClient client = await host.CreateOperatorClientAsync();

        (await UploadAsync(client, StubImageHandler.OnePixelPng, "a.png")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------ tạo job bằng ảnh đã tải lên

    [Fact]
    public async Task Tao_job_chi_bang_anh_da_tai_len_thi_202_va_brief_giu_id()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        Guid assetId = (await ApiTestHost.ReadJsonAsync(await UploadAsync(client, StubImageHandler.OnePixelPng, "a.png")))
            .GetProperty("asset_id").GetGuid();

        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", RequestWithImageIds(assetId.ToString()), "k-up");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, host.Errors);
        Guid jobId = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("job_id").GetGuid();

        AdVideoJob job = await host.InScopeAsync(sp => sp.GetRequiredService<AdVideoDbContext>().Jobs.SingleAsync(j => j.Id == jobId));
        job.BriefJson.Should().Contain(assetId.ToString());
    }

    [Fact]
    public async Task Id_anh_cua_tenant_khac_trong_nhu_khong_ton_tai()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string keyA) = await host.CreateTenantAsync("A");
        (_, string keyB) = await host.CreateTenantAsync("B");
        using HttpClient a = host.CreateTenantClient(keyA);
        using HttpClient b = host.CreateTenantClient(keyB);

        Guid assetOfA = (await ApiTestHost.ReadJsonAsync(await UploadAsync(a, StubImageHandler.OnePixelPng, "a.png")))
            .GetProperty("asset_id").GetGuid();

        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(b, "/v1/ad-videos", RequestWithImageIds(assetOfA.ToString()), "k-b");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("assets.product_image_ids", out _).Should().BeTrue();
        host.Jobs.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Id_anh_sai_dang_thi_400_doc_duoc()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        HttpResponseMessage response = await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", RequestWithImageIds("khong-phai-guid"), "k-sai");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("POST /v1/uploads");
    }

    // ------------------------------------------------------------ danh sách

    [Fact]
    public async Task Danh_sach_phan_trang_loc_trang_thai_va_chi_thay_job_cua_minh()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string keyA) = await host.CreateTenantAsync("A");
        (_, string keyB) = await host.CreateTenantAsync("B");
        using HttpClient a = host.CreateTenantClient(keyA);
        using HttpClient b = host.CreateTenantClient(keyB);

        for (int i = 0; i < 3; i++)
        {
            (await ApiTestHost.PostJsonAsync(a, "/v1/ad-videos", AdVideoEndpointsTests.ValidRequest, $"k-{i}"))
                .StatusCode.Should().Be(HttpStatusCode.Accepted, host.Errors);
        }

        JsonElement page1 = await ApiTestHost.ReadJsonAsync(await a.GetAsync("/v1/ad-videos?page_size=2"));
        page1.GetProperty("items").GetArrayLength().Should().Be(2);
        page1.GetProperty("total").GetInt32().Should().Be(3);

        JsonElement page2 = await ApiTestHost.ReadJsonAsync(await a.GetAsync("/v1/ad-videos?page_size=2&page=2"));
        page2.GetProperty("items").GetArrayLength().Should().Be(1);

        JsonElement queued = await ApiTestHost.ReadJsonAsync(await a.GetAsync("/v1/ad-videos?status=queued"));
        queued.GetProperty("total").GetInt32().Should().Be(3);

        JsonElement completed = await ApiTestHost.ReadJsonAsync(await a.GetAsync("/v1/ad-videos?status=completed"));
        completed.GetProperty("total").GetInt32().Should().Be(0);

        (await a.GetAsync("/v1/ad-videos?status=xong-roi")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        JsonElement ofB = await ApiTestHost.ReadJsonAsync(await b.GetAsync("/v1/ad-videos"));
        ofB.GetProperty("total").GetInt32().Should().Be(0, "danh sách đi qua global filter như mọi truy vấn khác");
    }

    // ------------------------------------------------------------ huỷ

    [Fact]
    public async Task Huy_job_dang_cho_thi_thanh_cancelled_huy_lan_hai_thi_409()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        Guid jobId = (await ApiTestHost.ReadJsonAsync(
                await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", AdVideoEndpointsTests.ValidRequest, "k-huy")))
            .GetProperty("job_id").GetGuid();

        HttpResponseMessage cancelled = await client.PostAsync($"/v1/ad-videos/{jobId}/cancel", null);

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, host.Errors);
        (await ApiTestHost.ReadJsonAsync(cancelled)).GetProperty("status").GetString().Should().Be("cancelled");

        (await client.PostAsync($"/v1/ad-videos/{jobId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Job_da_xong_khong_bi_huy_de_len()
    {
        // Worker vừa xong đúng lúc khách bấm huỷ: video đã tính tiền phải giữ trạng thái completed.
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string key) = await host.CreateTenantAsync();
        using HttpClient client = host.CreateTenantClient(key);

        Guid jobId = (await ApiTestHost.ReadJsonAsync(
                await ApiTestHost.PostJsonAsync(client, "/v1/ad-videos", AdVideoEndpointsTests.ValidRequest, "k-xong")))
            .GetProperty("job_id").GetGuid();

        await host.InScopeAsync(async sp =>
        {
            AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
            AdVideoJob job = await db.Jobs.SingleAsync(j => j.Id == jobId);
            job.Status = JobStatus.Completed;
            await db.SaveChangesAsync();
        });

        (await client.PostAsync($"/v1/ad-videos/{jobId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        AdVideoJob after = await host.InScopeAsync(sp => sp.GetRequiredService<AdVideoDbContext>().Jobs.SingleAsync(j => j.Id == jobId));
        after.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task Tenant_khac_huy_job_thi_404_va_job_khong_doi()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        (_, string keyA) = await host.CreateTenantAsync("A");
        (_, string keyB) = await host.CreateTenantAsync("B");
        using HttpClient a = host.CreateTenantClient(keyA);
        using HttpClient b = host.CreateTenantClient(keyB);

        Guid jobOfA = (await ApiTestHost.ReadJsonAsync(
                await ApiTestHost.PostJsonAsync(a, "/v1/ad-videos", AdVideoEndpointsTests.ValidRequest, "k-a")))
            .GetProperty("job_id").GetGuid();

        (await b.PostAsync($"/v1/ad-videos/{jobOfA}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        AdVideoJob job = await host.InScopeAsync(sp => sp.GetRequiredService<AdVideoDbContext>().Jobs.SingleAsync(j => j.Id == jobOfA));
        job.Status.Should().Be(JobStatus.Queued);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string fileName)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);

        // Cố ý khai sai kiểu: server phải nhận dạng bằng nội dung, không tin header.
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", fileName);

        return client.PostAsync("/v1/uploads", content);
    }

    private static string RequestWithImageIds(string id) =>
        AdVideoEndpointsTests.ValidRequest.Replace(
            "\"assets\": { \"product_images\": [\"https://anh-gia.test/ca-phe.png\"] }",
            $"\"assets\": {{ \"product_image_ids\": [\"{id}\"] }}",
            StringComparison.Ordinal);
}
