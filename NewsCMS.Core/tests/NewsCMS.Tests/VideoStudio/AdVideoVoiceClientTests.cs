using System.Net;
using System.Text;
using System.Text.Json;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Domain.Entities.VideoStudio;

namespace NewsCMS.Tests.VideoStudio;

/// <summary>Giọng đọc: chọn giọng khi tạo video, clone giọng kèm xác nhận, quản trị giọng có sẵn.</summary>
public sealed class AdVideoVoiceClientTests
{
    private const string OperatorKey = "advop_khoa-quan-tri-rat-bi-mat-0000000000";
    private const string TenantKey = "adv_key-cua-site-nay";

    private static async Task<AdVideoTestHarness> LinkedAsync()
    {
        var h = new AdVideoTestHarness();
        await h.Connection.SaveAsync(new AdVideoConnectionInput("http://advideo.test", OperatorKey));
        h.Db.SiteAdVideoTenants.Add(new SiteAdVideoTenant
        {
            SiteId = h.Site.SiteId,
            AdVideoTenantId = Guid.NewGuid(),
            ApiKeyEncrypted = h.Keys.ProtectTenantKey(TenantKey),
            ApiKeyPrefix = "adv_key-cua-",
        });
        await h.Db.SaveChangesAsync();

        return h;
    }

    [Fact]
    public async Task Danh_sach_giong_doc_du_loai_va_link_nghe_thu()
    {
        using AdVideoTestHarness h = await LinkedAsync();
        Guid id = Guid.NewGuid();
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.OK, $$"""
            [{"id":"{{id}}","name":"Giọng chị Lan","kind":"cloned","preview_url":"http://minio/sample.wav?sig=1","created_at":"2026-09-28T10:00:00Z"},
             {"id":"{{Guid.NewGuid()}}","name":"Nữ · miền Bắc","description":"ấm","kind":"preset","created_at":"2026-09-28T10:00:00Z"}]
            """);

        Result<IReadOnlyList<AdVideoVoiceDto>> result = await h.Client.ListVoicesAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(2, result.Value!.Count);
        Assert.True(result.Value[0].IsCloned);
        Assert.Equal("http://minio/sample.wav?sig=1", result.Value[0].PreviewUrl);
        Assert.False(result.Value[1].IsCloned);

        RecordedRequest request = h.Http.Requests.Single();
        Assert.EndsWith("/v1/voices", request.Uri.AbsolutePath);
        Assert.Equal(TenantKey, request.Headers["X-AdVideo-Key"]);
    }

    [Fact]
    public async Task Tao_video_co_chon_giong_gui_voice_profile_id()
    {
        using AdVideoTestHarness h = await LinkedAsync();
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.Accepted, $$"""
            {"job_id":"{{Guid.NewGuid()}}","status":"queued","current_step":0,"progress_percent":0,"duration_seconds":12,"estimated_cost_usd":0,"actual_cost_usd":0,"created_at":"2026-09-28T10:00:00Z"}
            """);

        Guid voice = Guid.NewGuid();
        await h.Client.CreateJobAsync(new CreateAdVideoInput(
            "k", null, "Cảnh", "Lời", 12, "9:16", "standard", false, "off", [Guid.NewGuid()], voice));

        using JsonDocument body = JsonDocument.Parse(h.Http.Requests.Single().Body!);
        Assert.Equal(voice.ToString(), body.RootElement.GetProperty("voice").GetProperty("voice_profile_id").GetString());
    }

    [Fact]
    public async Task Khong_chon_giong_thi_khong_gui_truong_giong()
    {
        using AdVideoTestHarness h = await LinkedAsync();
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.Accepted, $$"""
            {"job_id":"{{Guid.NewGuid()}}","status":"queued","current_step":0,"progress_percent":0,"duration_seconds":12,"estimated_cost_usd":0,"actual_cost_usd":0,"created_at":"2026-09-28T10:00:00Z"}
            """);

        await h.Client.CreateJobAsync(new CreateAdVideoInput(
            "k", null, "Cảnh", "Lời", 12, "9:16", "standard", false, "off", [Guid.NewGuid()]));

        using JsonDocument body = JsonDocument.Parse(h.Http.Requests.Single().Body!);
        Assert.False(body.RootElement.GetProperty("voice").TryGetProperty("voice_profile_id", out _), "null = giọng mặc định bên AdVideo");
    }

    [Fact]
    public async Task Clone_gui_multipart_du_xac_nhan_va_moi_file()
    {
        using AdVideoTestHarness h = await LinkedAsync();
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.Created, $$"""
            {"id":"{{Guid.NewGuid()}}","name":"Giọng chị Lan","kind":"cloned","created_at":"2026-09-28T10:00:00Z"}
            """);

        Result<AdVideoVoiceDto> result = await h.Client.CloneVoiceAsync(new CloneVoiceInput(
            " Giọng chị Lan ",
            null,
            "Chị Lan đồng ý bằng văn bản ngày 28/09.",
            true,
            "marketing01",
            [
                new AdVideoVoiceSample("C:\\ghi-am\\mau-1.wav", () => new MemoryStream(Encoding.ASCII.GetBytes("RIFF1"))),
                new AdVideoVoiceSample("mau-2.mp3", () => new MemoryStream(Encoding.ASCII.GetBytes("ID3xx"))),
            ]));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("Giọng chị Lan", result.Value!.Name);

        RecordedRequest request = h.Http.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/v1/voices", request.Uri.AbsolutePath);
        Assert.Equal(TenantKey, request.Headers["X-AdVideo-Key"]);

        string body = request.Body!;
        Assert.Contains("name=consent_statement", body);
        Assert.Contains("Chị Lan đồng ý bằng văn bản ngày 28/09.", body);
        Assert.Contains("name=consented_by", body);
        Assert.Contains("marketing01", body);
        Assert.Contains("RIFF1", body);
        Assert.Contains("ID3xx", body);
        Assert.Equal(2, CountOf(body, "name=files"));
        Assert.DoesNotContain("name=description", body);
    }

    [Fact]
    public async Task Clone_khong_co_file_thi_khong_goi_mang()
    {
        using AdVideoTestHarness h = await LinkedAsync();

        Result<AdVideoVoiceDto> result = await h.Client.CloneVoiceAsync(new CloneVoiceInput("x", null, "đồng ý đầy đủ", true, "u", []));

        Assert.False(result.Succeeded);
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Clone_bi_AdVideo_tu_choi_thi_hien_ly_do()
    {
        using AdVideoTestHarness h = await LinkedAsync();
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.UnprocessableEntity, """
            {"title":"Đã đủ số giọng clone","errors":["Site đã có 5/5 giọng clone. Xoá bớt giọng cũ."]}
            """, "application/problem+json");

        Result<AdVideoVoiceDto> result = await h.Client.CloneVoiceAsync(new CloneVoiceInput(
            "x", null, "đồng ý đầy đủ", true, "u", [new AdVideoVoiceSample("a.wav", () => new MemoryStream([1]))]));

        Assert.False(result.Succeeded);
        Assert.Contains("5/5", result.Error);
    }

    [Fact]
    public async Task Xoa_giong_goi_DELETE_bang_key_site()
    {
        using AdVideoTestHarness h = await LinkedAsync();
        h.Http.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        Guid id = Guid.NewGuid();

        Result result = await h.Client.DeleteVoiceAsync(id);

        Assert.True(result.Succeeded, result.Error);
        RecordedRequest request = h.Http.Requests.Single();
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.EndsWith($"/v1/voices/{id}", request.Uri.AbsolutePath);
    }

    // ------------------------------------------------------------ quản trị

    [Fact]
    public async Task Them_giong_co_san_mang_operator_key_va_snake_case()
    {
        using AdVideoTestHarness h = await LinkedAsync();
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.Created, $$"""
            {"id":"{{Guid.NewGuid()}}","name":"Rachel","provider":"elevenlabs","provider_voice_id":"21m00","is_active":true,"sort_order":10,"created_at":"2026-09-28T10:00:00Z"}
            """);

        Result<AdVideoVoicePresetDto> result = await h.Admin.AddVoicePresetAsync(
            new AdVideoVoicePresetInput("Rachel", null, "elevenlabs", "21m00", "https://cdn/p.mp3", null, true));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("21m00", result.Value!.ProviderVoiceId);

        RecordedRequest request = h.Http.Requests.Single();
        Assert.Equal(OperatorKey, request.Headers["X-AdVideo-Operator-Key"]);
        Assert.EndsWith("/v1/admin/voices", request.Uri.AbsolutePath);

        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal("21m00", body.RootElement.GetProperty("provider_voice_id").GetString());
        Assert.Equal("https://cdn/p.mp3", body.RootElement.GetProperty("preview_url").GetString());
        Assert.False(body.RootElement.TryGetProperty("sort_order", out _), "không gửi thứ tự = AdVideo xếp cuối");
    }

    [Fact]
    public async Task Sua_giong_xoa_trang_mo_ta_thi_gui_chuoi_rong_de_xoa()
    {
        using AdVideoTestHarness h = await LinkedAsync();
        Guid id = Guid.NewGuid();
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.OK, $$"""
            {"id":"{{id}}","name":"Rachel","provider":"elevenlabs","provider_voice_id":"21m00","is_active":false,"sort_order":1,"created_at":"2026-09-28T10:00:00Z"}
            """);

        await h.Admin.UpdateVoicePresetAsync(id, new AdVideoVoicePresetInput("Rachel", "", null, null, "", 1, false));

        RecordedRequest request = h.Http.Requests.Single();
        Assert.Equal(HttpMethod.Put, request.Method);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal("", body.RootElement.GetProperty("description").GetString());
        Assert.False(body.RootElement.GetProperty("is_active").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("provider", out _));
    }

    [Fact]
    public async Task Thu_vien_giong_cua_engine_doc_nhan_va_da_them()
    {
        using AdVideoTestHarness h = await LinkedAsync();
        h.Http.Respond = _ => RecordingHandler.Json(HttpStatusCode.OK, """
            [{"voice_id":"v1","name":"Rachel","category":"premade","labels":{"gender":"female"},"already_added":true}]
            """);

        Result<IReadOnlyList<AdVideoProviderVoiceDto>> result = await h.Admin.GetProviderVoiceLibraryAsync(" ElevenLabs ");

        Assert.True(result.Succeeded, result.Error);
        AdVideoProviderVoiceDto voice = Assert.Single(result.Value!);
        Assert.True(voice.AlreadyAdded);
        Assert.Equal("female", voice.Labels!["gender"]);
        Assert.Equal("provider=elevenlabs", h.Http.Requests.Single().Uri.Query.TrimStart('?'));
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0;

        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
