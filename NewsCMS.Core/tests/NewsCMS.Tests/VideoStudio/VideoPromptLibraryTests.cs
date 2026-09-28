using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Application.VideoStudio;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.VideoStudio;
using NewsCMS.Infrastructure.Persistence.Seed;
using NewsCMS.Infrastructure.VideoStudio;

namespace NewsCMS.Tests.VideoStudio;

/// <summary>Kho mẫu brief: luật nội dung, mẫu mặc định, cập nhật theo trend bằng AI.</summary>
public sealed class VideoPromptLibraryTests
{
    // ------------------------------------------------------------ luật

    [Fact]
    public void Mau_hop_le_khong_co_loi()
    {
        Assert.Empty(VideoPromptTemplateRules.Validate(Valid()));
    }

    [Theory]
    [InlineData("Sản phẩm tốt nhất cho bạn")]
    [InlineData("Hiệu quả 100% sau một tuần")]
    [InlineData("Chúng tôi cam kết hoàn tiền")]
    [InlineData("Thương hiệu số 1 Việt Nam")]
    public void Cum_quang_cao_tuyet_doi_bi_chan(string script)
    {
        List<string> errors = VideoPromptTemplateRules.Validate(Valid() with { ScriptTemplate = script });

        Assert.Contains(errors, e => e.Contains("Luật Quảng cáo"));
    }

    [Fact]
    public void Tieng_Viet_binh_thuong_co_chu_nhat_khong_bi_chan()
    {
        Assert.Empty(VideoPromptTemplateRules.Validate(Valid() with { ScriptTemplate = "Nhất định phải thử {san_pham}, thống nhất một hương vị." }));
    }

    [Fact]
    public void Nganh_nhay_cam_va_thong_so_sai_bi_chan()
    {
        List<string> errors = VideoPromptTemplateRules.Validate(Valid() with
        {
            ScenePrompt = "Bàn tiệc với rượu vang",
            AspectRatio = "4:3",
            DurationSeconds = 300,
        });

        Assert.Contains(errors, e => e.Contains("rượu"));
        Assert.Contains(errors, e => e.Contains("Khung hình"));
        Assert.Contains(errors, e => e.Contains("Thời lượng"));
    }

    [Fact]
    public void So_trung_tieu_de_bo_dau_va_hoa_thuong()
    {
        Assert.Equal(
            VideoPromptTemplateRules.NormalizeTitle("Mở hộp  CHẬM!"),
            VideoPromptTemplateRules.NormalizeTitle("mo hop cham"));
    }

    // ------------------------------------------------------------ mẫu mặc định + kho

    [Fact]
    public async Task Seed_mau_mac_dinh_hop_le_va_chay_lai_khong_nhan_ban_khong_ghi_de()
    {
        using var h = new AdVideoTestHarness();

        await VideoPromptLibrarySeeder.SeedAsync(h.Db);
        List<VideoPromptTemplate> seeded = await h.Db.VideoPromptTemplates.ToListAsync();

        Assert.True(seeded.Count >= 10);
        Assert.All(seeded, t =>
        {
            var input = new VideoPromptTemplateInput(t.Title, t.Category, t.Description, t.ScenePrompt, t.ScriptTemplate, t.AspectRatio, t.DurationSeconds, t.HasPerson, 0);
            Assert.Empty(VideoPromptTemplateRules.Validate(input));
            Assert.Contains(VideoPromptTemplate.ProductPlaceholder, t.ScriptTemplate);
        });

        // Quản trị sửa prompt của skill — seed lại không được ghi đè.
        AiSkill skill = await h.Db.AiSkills.SingleAsync(x => x.Key == AiTaskKeys.VideoStudioTrendTemplates);
        skill.SystemPrompt = "prompt của quản trị";
        await h.Db.SaveChangesAsync();

        await VideoPromptLibrarySeeder.SeedAsync(h.Db);

        Assert.Equal(seeded.Count, await h.Db.VideoPromptTemplates.CountAsync());
        Assert.Equal("prompt của quản trị", (await h.Db.AiSkills.SingleAsync(x => x.Key == AiTaskKeys.VideoStudioTrendTemplates)).SystemPrompt);
        Assert.Equal(1, await h.Db.VideoPromptLibrarySettings.CountAsync());
    }

    [Fact]
    public async Task Form_chi_thay_mau_dang_hien_va_chua_het_han_trend_moi_len_dau()
    {
        using var h = new AdVideoTestHarness();
        var library = new VideoPromptLibraryService(h.Db);

        h.Db.VideoPromptTemplates.AddRange(
            Row("Mặc định", VideoPromptTemplateSource.Default, VideoPromptTemplateStatus.Published),
            Row("Trend mới", VideoPromptTemplateSource.Trend, VideoPromptTemplateStatus.Published, DateTime.UtcNow.AddDays(3)),
            Row("Trend hết hạn", VideoPromptTemplateSource.Trend, VideoPromptTemplateStatus.Published, DateTime.UtcNow.AddMinutes(-1)),
            Row("Chờ duyệt", VideoPromptTemplateSource.Trend, VideoPromptTemplateStatus.PendingReview, DateTime.UtcNow.AddDays(3)),
            Row("Đã ẩn", VideoPromptTemplateSource.Manual, VideoPromptTemplateStatus.Hidden));
        await h.Db.SaveChangesAsync();

        IReadOnlyList<VideoPromptTemplateDto> published = await library.GetPublishedAsync();

        Assert.Equal(["Trend mới", "Mặc định"], published.Select(t => t.Title));
    }

    [Fact]
    public async Task Them_mau_tay_qua_cung_luat_va_dem_luot_dung()
    {
        using var h = new AdVideoTestHarness();
        var library = new VideoPromptLibraryService(h.Db);

        Result<VideoPromptTemplateDto> bad = await library.CreateAsync(Valid() with { ScriptTemplate = "Rẻ nhất thị trường" });
        Assert.False(bad.Succeeded);

        Result<VideoPromptTemplateDto> ok = await library.CreateAsync(Valid());
        Assert.True(ok.Succeeded, ok.Error);
        Assert.Equal("manual", ok.Value!.Source);

        await library.RecordUsageAsync(ok.Value.Id);
        await library.RecordUsageAsync(ok.Value.Id);
        await library.RecordUsageAsync(Guid.NewGuid()); // mẫu đã xoá: không lỗi

        Assert.Equal(2, (await library.GetAsync(ok.Value.Id))!.UsageCount);
    }

    // ------------------------------------------------------------ cập nhật theo trend

    [Fact]
    public async Task Cap_nhat_trend_luu_mau_dat_loai_mau_sai_va_ghi_lai_ly_do()
    {
        using var h = new AdVideoTestHarness();
        await VideoPromptLibrarySeeder.SeedAsync(h.Db);
        await SetSettingsAsync(h, requireReview: false);

        var ai = new FakeAi("""
            Đây là kết quả:
            ```json
            [
              {"title":"POV khách quen gọi món","category":"Ăn uống","description":"Quán ăn","trend_name":"POV khách quen",
               "scene_prompt":"Góc nhìn thứ nhất bước vào quán, {san_pham} được bưng ra.","script":"Khách quen nào cũng gọi {san_pham} mỗi sáng. Ghé thử nhé!",
               "aspect_ratio":"9:16","duration_seconds":"12","has_person":true,"source_urls":["https://example.com/trend","javascript:alert(1)"]},
              {"title":"Sai luật","category":"Mỹ phẩm & làm đẹp","scene_prompt":"Cảnh","script":"{san_pham} trắng da 100%","aspect_ratio":"9:16","duration_seconds":15},
              {"title":"Thiếu chỗ giữ tên","category":"Ăn uống","scene_prompt":"Cảnh","script":"Bánh mì Hùng ngon lắm","aspect_ratio":"9:16","duration_seconds":15},
              {"title":"Mở hộp chậm (unboxing)","category":"Công nghệ","scene_prompt":"Cảnh","script":"Mở hộp {san_pham}","aspect_ratio":"9:16","duration_seconds":15}
            ]
            ```
            """);
        var trends = new VideoPromptTrendService(h.Db, ai, NullLogger<VideoPromptTrendService>.Instance);

        Result<VideoPromptTrendRunDto> result = await trends.RefreshAsync("admin");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value!.Added);
        Assert.Equal(3, result.Value.Rejected);
        Assert.Contains("100%", result.Value.Notes);
        Assert.Contains("{san_pham}", result.Value.Notes);
        Assert.Contains("Trùng tiêu đề", result.Value.Notes);
        Assert.False(result.Value.UsedWebSearch);
        Assert.Contains("Chưa bật công cụ tìm web", result.Value.Notes);

        VideoPromptTemplate added = await h.Db.VideoPromptTemplates.SingleAsync(x => x.Source == VideoPromptTemplateSource.Trend);
        Assert.Equal(VideoPromptTemplateStatus.Published, added.Status);
        Assert.Equal(12, added.DurationSeconds);
        Assert.Equal("https://example.com/trend", added.SourceUrls);
        Assert.NotNull(added.ExpiresAt);

        // Danh sách tiêu đề đã có được gửi cho AI để khỏi lặp ý.
        Assert.Contains("Mở hộp chậm (unboxing)", ai.LastRequest!.Content);
        Assert.Equal(AiTaskKeys.VideoStudioTrendTemplates, ai.LastRequest.SkillKey);
    }

    [Fact]
    public async Task Bat_duyet_thi_mau_trend_cho_duyet_va_mau_het_han_bi_an()
    {
        using var h = new AdVideoTestHarness();
        await SetSettingsAsync(h, requireReview: true);
        h.Db.VideoPromptTemplates.Add(Row("Trend cũ", VideoPromptTemplateSource.Trend, VideoPromptTemplateStatus.Published, DateTime.UtcNow.AddDays(-1)));
        await h.Db.SaveChangesAsync();

        var trends = new VideoPromptTrendService(h.Db, new FakeAi("""
            [{"title":"Mẫu mới","category":"Khác","scene_prompt":"Cảnh {san_pham}","script":"Thử {san_pham} ngay","aspect_ratio":"1:1","duration_seconds":10}]
            """), NullLogger<VideoPromptTrendService>.Instance);

        Result<VideoPromptTrendRunDto> result = await trends.RefreshAsync("schedule");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value!.Expired);
        Assert.Equal(VideoPromptTemplateStatus.Hidden, (await h.Db.VideoPromptTemplates.SingleAsync(x => x.Title == "Trend cũ")).Status);
        Assert.Equal(VideoPromptTemplateStatus.PendingReview, (await h.Db.VideoPromptTemplates.SingleAsync(x => x.Title == "Mẫu mới")).Status);
    }

    [Fact]
    public async Task AI_loi_hoac_tra_rac_thi_ghi_lan_chay_that_bai_khong_them_gi()
    {
        using var h = new AdVideoTestHarness();
        await SetSettingsAsync(h, requireReview: false);

        var failing = new VideoPromptTrendService(h.Db, new FakeAi(null, "Không tìm thấy kết nối AI khả dụng."), NullLogger<VideoPromptTrendService>.Instance);
        Result<VideoPromptTrendRunDto> r1 = await failing.RefreshAsync("admin");

        Assert.False(r1.Succeeded);
        Assert.Contains("kết nối AI", r1.Error);

        var garbage = new VideoPromptTrendService(h.Db, new FakeAi("Xin lỗi, tôi không làm được."), NullLogger<VideoPromptTrendService>.Instance);
        Result<VideoPromptTrendRunDto> r2 = await garbage.RefreshAsync("admin");

        Assert.False(r2.Succeeded);
        Assert.Equal(0, await h.Db.VideoPromptTemplates.CountAsync());
        Assert.Equal(2, await h.Db.VideoPromptTrendRuns.CountAsync(x => x.Status == VideoPromptTrendRunStatus.Failed));
    }

    [Fact]
    public async Task Den_lich_chi_khi_bat_va_lan_chay_gan_nhat_du_cu()
    {
        using var h = new AdVideoTestHarness();
        var trends = new VideoPromptTrendService(h.Db, new FakeAi("[]"), NullLogger<VideoPromptTrendService>.Instance);

        await SetSettingsAsync(h, requireReview: false, enabled: false);
        Assert.False(await trends.IsDueAsync());

        await SetSettingsAsync(h, requireReview: false, enabled: true);
        Assert.True(await trends.IsDueAsync(), "chưa chạy lần nào");

        h.Db.VideoPromptTrendRuns.Add(new VideoPromptTrendRun { Trigger = "schedule", Status = VideoPromptTrendRunStatus.Failed, StartedAt = DateTime.UtcNow.AddHours(-2) });
        await h.Db.SaveChangesAsync();
        Assert.False(await trends.IsDueAsync(), "lần lỗi 2 giờ trước vẫn tính — không gọi AI liên tục khi đang hỏng");
    }

    // ------------------------------------------------------------

    private static VideoPromptTemplateInput Valid() => new(
        "Mẫu thử", "Ăn uống", "mô tả", "Cận cảnh {san_pham} trên bàn gỗ", "Thưởng thức {san_pham} ngay hôm nay!", "9:16", 15, false, 0);

    private static VideoPromptTemplate Row(string title, VideoPromptTemplateSource source, VideoPromptTemplateStatus status, DateTime? expiresAt = null) => new()
    {
        Title = title,
        Category = "Khác",
        ScenePrompt = "Cảnh",
        ScriptTemplate = "Lời {san_pham}",
        Source = source,
        Status = status,
        ExpiresAt = expiresAt,
    };

    private static async Task SetSettingsAsync(AdVideoTestHarness h, bool requireReview, bool enabled = true)
    {
        Result result = await new VideoPromptLibraryService(h.Db).SaveSettingsAsync(
            new VideoPromptLibrarySettingsDto(enabled, 24, 5, 14, requireReview, "Việt Nam"));
        Assert.True(result.Succeeded, result.Error);
    }

    private sealed class FakeAi(string? content, string? error = null) : IAiCompletionService
    {
        public AiGenerationRequest? LastRequest { get; private set; }

        public Task<Result<AiGenerationResult>> GenerateAsync(AiGenerationRequest request, CancellationToken ct = default)
        {
            LastRequest = request;

            return Task.FromResult(content is null
                ? Result<AiGenerationResult>.Failure(error!)
                : Result<AiGenerationResult>.Success(new AiGenerationResult(content)));
        }

        public Task<Result<AiChatResult>> ChatAsync(AiChatRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
