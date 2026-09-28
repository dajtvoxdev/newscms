using System.Net;
using System.Text.Json;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Domain.Entities.VideoStudio;
using NewsCMS.Infrastructure.VideoStudio;

namespace NewsCMS.Tests.VideoStudio;

public sealed class AdVideoClientTests
{
    private const string OperatorKey = "advop_khoa-quan-tri-rat-bi-mat-0000000000";

    // ------------------------------------------------------------ đọc lỗi của AdVideo

    [Fact]
    public void Loi_kiem_du_lieu_dang_object_duoc_gop_du_moi_truong()
    {
        const string body = """
            {"title":"Request không hợp lệ","status":400,
             "errors":{"brief.prompt":["Bắt buộc."],"assets.product_images":["Cần ít nhất một ảnh."]}}
            """;

        string message = AdVideoHttp.DescribeFailure(HttpStatusCode.BadRequest, body, null);

        Assert.Contains("Request không hợp lệ", message);
        Assert.Contains("brief.prompt: Bắt buộc.", message);
        Assert.Contains("assets.product_images: Cần ít nhất một ảnh.", message);
    }

    [Fact]
    public void Loi_dang_mang_va_ky_tu_thieu_cua_font_deu_hien_ra()
    {
        const string body = """
            {"title":"Font không dùng được","detail":"Font thiếu 2 ký tự","errors":["a","b"],"missing_characters":["ă","đ"]}
            """;

        string message = AdVideoHttp.DescribeFailure(HttpStatusCode.UnprocessableEntity, body, null);

        Assert.Contains("Font thiếu 2 ký tự", message);
        Assert.Contains("Ký tự thiếu: ăđ", message);
    }

    [Fact]
    public void Tu_choi_key_thi_noi_ro_phai_kiem_key_nao()
    {
        string message = AdVideoHttp.DescribeFailure(HttpStatusCode.Unauthorized, "{}", null);

        Assert.Contains("401", message);
        Assert.Contains("operator key", message);
    }

    [Fact]
    public void Than_khong_phai_JSON_van_ra_cau_doc_duoc()
    {
        string message = AdVideoHttp.DescribeFailure(HttpStatusCode.BadGateway, "<html>nginx</html>", null);

        Assert.Contains("502", message);
    }

    // ------------------------------------------------------------ client quản trị

    [Fact]
    public async Task Chua_cau_hinh_ket_noi_thi_khong_goi_mang_va_chi_duong()
    {
        using var h = new AdVideoTestHarness();

        Result<IReadOnlyList<AdVideoSettingDto>> result = await h.Admin.GetSettingsAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("Cấu hình AdVideo", result.Error);
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Goi_quan_tri_mang_operator_key_va_doc_snake_case()
    {
        using var h = new AdVideoTestHarness();
        Assert.True((await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test:5080/", OperatorKey))).Succeeded);

        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.OK, """
            [{"key":"MaxConcurrentShots","value":"1","value_type":"int","description":"d","is_provisional":true,"min_value":"1","max_value":"8"}]
            """);

        Result<IReadOnlyList<AdVideoSettingDto>> result = await h.Admin.GetSettingsAsync();

        Assert.True(result.Succeeded, result.Error);
        AdVideoSettingDto setting = Assert.Single(result.Value!);
        Assert.Equal("MaxConcurrentShots", setting.Key);
        Assert.True(setting.IsProvisional);

        RecordedRequest request = Assert.Single(h.Http.Requests);
        Assert.Equal("http://advideo.test:5080/v1/admin/settings", request.Uri.ToString());
        Assert.Equal(OperatorKey, request.Headers["X-AdVideo-Operator-Key"]);
        Assert.False(request.Headers.ContainsKey("X-AdVideo-Key"), "request quản trị không mang tenant key");
    }

    [Fact]
    public async Task Nap_key_provider_gui_snake_case_va_bo_truong_trong()
    {
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));

        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.OK, """
            {"credential":{"id":"6f1a6c4e-7a39-4a1b-9d7b-0c6e8f3e2a11","provider":"kling","model_id":"m","category":"video","has_key":true,"masked_key":"****1234","is_active":true,"priority":0},"key_changed":true,"notices":[]}
            """);

        Result<AdVideoCredentialSaved> result = await h.Admin.SaveCredentialAsync(
            new AdVideoCredentialInput("Kling", "fal-1234", null, null, "{\"modelId\":\"m\"}", null, true, "  "));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("****1234", result.Value!.Credential.MaskedKey);

        RecordedRequest request = h.Http.Requests.Single();
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith("/v1/admin/credentials/kling", request.Uri.AbsolutePath);

        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal("fal-1234", body.RootElement.GetProperty("api_key").GetString());
        Assert.Equal("m", body.RootElement.GetProperty("capability").GetProperty("modelId").GetString());
        Assert.False(body.RootElement.TryGetProperty("note", out _), "ghi chú trắng không được ghi đè ghi chú cũ");
        Assert.False(body.RootElement.TryGetProperty("priority", out _), "không gửi priority = giữ priority cũ");
    }

    [Fact]
    public async Task Capability_hong_bi_chan_truoc_khi_goi_mang()
    {
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));

        Result<AdVideoCredentialSaved> result = await h.Admin.SaveCredentialAsync(
            new AdVideoCredentialInput("kling", null, null, null, "{hỏng", null, null, null));

        Assert.False(result.Succeeded);
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task May_chu_AdVideo_khong_chay_thi_ra_loi_doc_duoc_khong_nem()
    {
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));
        h.Http.Respond = _ => throw new HttpRequestException("Connection refused");

        Result<IReadOnlyList<AdVideoSettingDto>> result = await h.Admin.GetSettingsAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("Không kết nối được AdVideo", result.Error);
        Assert.DoesNotContain(OperatorKey, result.Error);
    }

    // ------------------------------------------------------------ client của site

    [Fact]
    public async Task Site_chua_ket_noi_thi_khong_goi_mang()
    {
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));

        Result<AdVideoJobPageDto> result = await h.Client.ListJobsAsync(null, 1, 20);

        Assert.False(result.Succeeded);
        Assert.Contains("chưa được kết nối", result.Error);
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Tao_video_mang_key_cua_dung_site_hien_tai_va_idempotency_key()
    {
        using var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));

        Guid otherSite = Guid.NewGuid();
        h.Db.SiteAdVideoTenants.Add(new SiteAdVideoTenant { SiteId = otherSite, AdVideoTenantId = Guid.NewGuid(), ApiKeyEncrypted = h.Keys.ProtectTenantKey("adv_key-cua-site-khac"), ApiKeyPrefix = "adv_key-cua-" });
        h.Db.SiteAdVideoTenants.Add(new SiteAdVideoTenant { SiteId = h.Site.SiteId, AdVideoTenantId = Guid.NewGuid(), ApiKeyEncrypted = h.Keys.ProtectTenantKey("adv_key-cua-site-nay"), ApiKeyPrefix = "adv_key-cua-" });
        await h.Db.SaveChangesAsync();

        Guid jobId = Guid.NewGuid();
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.Accepted, $$"""
            {"job_id":"{{jobId}}","status":"queued","current_step":0,"progress_percent":0,"duration_seconds":12,"estimated_cost_usd":0,"actual_cost_usd":0,"created_at":"2026-09-28T10:00:00Z"}
            """);

        Guid asset = Guid.NewGuid();
        Result<AdVideoJobDto> result = await h.Client.CreateJobAsync(new CreateAdVideoInput(
            "khoa-form-1", "Cà phê", "Cận cảnh ly cà phê", "Lời thoại", 12, "9:16", "standard", false, "off", [asset]));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(jobId, result.Value!.JobId);

        RecordedRequest request = h.Http.Requests.Single();
        Assert.Equal("adv_key-cua-site-nay", request.Headers["X-AdVideo-Key"]);
        Assert.Equal("khoa-form-1", request.Headers["Idempotency-Key"]);
        Assert.False(request.Headers.ContainsKey("X-AdVideo-Operator-Key"), "gọi của khách không bao giờ mang operator key");

        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal(asset.ToString(), body.RootElement.GetProperty("assets").GetProperty("product_image_ids")[0].GetString());
        Assert.Equal("Cận cảnh ly cà phê", body.RootElement.GetProperty("brief").GetProperty("prompt").GetString());
        Assert.Equal("off", body.RootElement.GetProperty("audio").GetProperty("native_sound").GetString());
    }

    [Fact]
    public async Task Job_da_ket_thuc_thi_trang_ngung_tu_lam_moi()
    {
        var job = new AdVideoJobDto(Guid.NewGuid(), "cancelled", 0, null, 0, null, null, 0, null, 0, 0, null, null, DateTime.UtcNow, null, null);

        Assert.True(job.IsTerminal);
        Assert.False((job with { Status = "rendering_shots" }).IsTerminal);
        await Task.CompletedTask;
    }
}
