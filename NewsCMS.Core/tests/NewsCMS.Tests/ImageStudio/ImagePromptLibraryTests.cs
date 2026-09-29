using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio.Imaging;
using NewsCMS.Infrastructure.Persistence.Seed;

namespace NewsCMS.Tests.ImageStudio;

public class ImagePromptRulesTests
{
    private static readonly List<string> Blocked = ImagePromptTemplateRules.ParseBlockedTerms(ImagePromptTemplateRules.DefaultBlockedTerms);

    private static ImagePromptTemplateInput Input(string prompt, ImagePurpose purpose = ImagePurpose.PostCover, string aspect = "16:9", string title = "Mẫu thử") =>
        new(title, "Tin tức & bài viết", purpose, "mô tả", prompt, aspect, 0);

    [Fact]
    public void Valid_template_passes()
    {
        (List<string> errors, List<string> warnings) = ImagePromptTemplateRules.Validate(Input("Ảnh báo chí về {chu_de}, nắng sớm."), Blocked);

        Assert.Empty(errors);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(ImagePurpose.PostCover, "Ảnh phố cổ", "{chu_de}")]
    [InlineData(ImagePurpose.PostInline, "Ảnh {san_pham}", "{chu_de}")]
    [InlineData(ImagePurpose.ProductMain, "Ảnh về {chu_de}", "{san_pham}")]
    [InlineData(ImagePurpose.ProductGallery, "Ảnh bàn gỗ", "{san_pham}")]
    public void Purpose_requires_its_placeholder(ImagePurpose purpose, string prompt, string expected)
    {
        (List<string> errors, _) = ImagePromptTemplateRules.Validate(Input(prompt, purpose), Blocked);

        Assert.Contains(errors, e => e.Contains(expected));
    }

    [Fact]
    public void Free_banner_and_social_do_not_require_a_placeholder()
    {
        Assert.Empty(ImagePromptTemplateRules.Validate(Input("Hoàng hôn trên biển", ImagePurpose.Free), Blocked).Errors);
        Assert.Empty(ImagePromptTemplateRules.Validate(Input("Hoàng hôn trên biển", ImagePurpose.Social), Blocked).Errors);
    }

    [Fact]
    public void Unknown_placeholders_blocked_terms_and_ad_claims_are_errors()
    {
        (List<string> errors, _) = ImagePromptTemplateRules.Validate(
            Input("Ảnh {chu_de} phong cách Ghibli, sản phẩm tốt nhất, {ten_san_pham}"), Blocked);

        Assert.Contains(errors, e => e.Contains("{ten_san_pham}"));
        Assert.Contains(errors, e => e.Contains("Ghibli"));
        Assert.Contains(errors, e => e.Contains("tốt nhất"));
    }

    [Fact]
    public void Asking_for_text_in_the_image_is_only_a_warning()
    {
        (List<string> errors, List<string> warnings) = ImagePromptTemplateRules.Validate(Input("Ảnh {chu_de} có dòng chữ khuyến mãi"), Blocked);

        Assert.Empty(errors);
        Assert.Single(warnings);
    }

    [Fact]
    public void Aspect_ratio_and_length_limits()
    {
        Assert.Contains(ImagePromptTemplateRules.Validate(Input("Ảnh {chu_de}", aspect: "5:1"), Blocked).Errors, e => e.Contains("Tỉ lệ"));
        Assert.Contains(ImagePromptTemplateRules.Validate(Input("Ảnh {chu_de}" + new string('a', 2001)), Blocked).Errors, e => e.Contains("Prompt dài"));
    }

    [Fact]
    public void Placeholders_fill_find_and_label()
    {
        const string text = "Ảnh {san_pham} của {thuong_hieu} về {chu_de}";

        Assert.Equal(["{chu_de}", "{san_pham}", "{thuong_hieu}"], ImagePromptPlaceholders.FindKnown(text));
        Assert.Equal("Ảnh Mật ong của {thuong_hieu} về {chu_de}",
            ImagePromptPlaceholders.Fill(text, new Dictionary<string, string?> { ["{san_pham}"] = " Mật ong ", ["{chu_de}"] = "  " }));
        Assert.Equal(["{abc}"], ImagePromptPlaceholders.FindUnknown("x {abc} {chu_de} {1}"));
        Assert.Equal("Tên sản phẩm", ImagePromptPlaceholders.Label("{san_pham}"));
    }

    [Theory]
    [InlineData("post-cover", ImagePurpose.PostCover)]
    [InlineData("post_inline", ImagePurpose.PostInline)]
    [InlineData("ProductMain", ImagePurpose.ProductMain)]
    [InlineData("SOCIAL", ImagePurpose.Social)]
    public void Purposes_parse_every_spelling(string value, ImagePurpose expected) => Assert.Equal(expected, ImagePurposes.Parse(value));

    [Fact]
    public void Purposes_reject_unknown() => Assert.Null(ImagePurposes.Parse("poster"));
}

public class ImagePromptLibraryServiceTests
{
    [Fact]
    public async Task Seeder_adds_templates_and_skills_once_and_never_overwrites()
    {
        using var h = new ImageStudioTestHarness();

        await ImagePromptLibrarySeeder.SeedAsync(h.Db);
        int count = await h.Db.ImagePromptTemplates.CountAsync();
        ImagePromptTemplate edited = await h.Db.ImagePromptTemplates.FirstAsync();
        edited.Prompt = "Quản trị đã sửa {chu_de}";
        await h.Db.SaveChangesAsync();

        await ImagePromptLibrarySeeder.SeedAsync(h.Db);

        Assert.True(count >= 15);
        Assert.Equal(count, await h.Db.ImagePromptTemplates.CountAsync());
        Assert.Equal("Quản trị đã sửa {chu_de}", (await h.Db.ImagePromptTemplates.SingleAsync(x => x.Id == edited.Id)).Prompt);
        Assert.Equal(3, await h.Db.AiSkills.CountAsync(x => x.Key.StartsWith("imagestudio_")));
        Assert.Single(await h.Db.ImagePromptLibrarySettings.ToListAsync());

        // Mọi mẫu mặc định phải qua đúng luật áp cho mẫu quản trị viết.
        List<string> blocked = ImagePromptTemplateRules.ParseBlockedTerms(ImagePromptTemplateRules.DefaultBlockedTerms);
        foreach (ImagePromptTemplate t in await h.Db.ImagePromptTemplates.AsNoTracking().Where(x => x.Id != edited.Id).ToListAsync())
        {
            var input = new ImagePromptTemplateInput(t.Title, t.Category, t.Purpose, t.Description, t.Prompt, t.AspectRatio, 0);
            Assert.True(ImagePromptTemplateRules.Validate(input, blocked).Errors.Count == 0, t.Title);
        }
    }

    [Fact]
    public async Task Published_list_filters_by_purpose_status_expiry_and_source_image()
    {
        using var h = new ImageStudioTestHarness();
        h.Db.ImagePromptTemplates.AddRange(
            Row("Bìa", ImagePurpose.PostCover),
            Row("Tự do", ImagePurpose.Free),
            Row("Sản phẩm", ImagePurpose.ProductMain),
            Row("Chờ duyệt", ImagePurpose.PostCover, ImagePromptTemplateStatus.PendingReview),
            Row("Hết hạn", ImagePurpose.PostCover, expiresAt: DateTime.UtcNow.AddDays(-1)),
            Row("Sửa ảnh", ImagePurpose.ProductMain, requiresSource: true));
        await h.Db.SaveChangesAsync();

        IReadOnlyList<ImagePromptTemplateDto> cover = await h.Library().GetPublishedAsync(ImagePurpose.PostCover);
        IReadOnlyList<ImagePromptTemplateDto> all = await h.Library().GetPublishedAsync();

        Assert.Equal(["Bìa", "Tự do"], cover.Select(t => t.Title));
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task Create_validates_with_blocked_terms_from_settings_and_returns_warnings()
    {
        using var h = new ImageStudioTestHarness();
        h.Db.ImagePromptLibrarySettings.Add(new ImagePromptLibrarySettings { BlockedTerms = "Thương hiệu X" });
        await h.Db.SaveChangesAsync();

        Result<ImagePromptTemplateSaved> blocked = await h.Library().CreateAsync(
            new ImagePromptTemplateInput("Mẫu", "Khác", ImagePurpose.Free, null, "Ảnh kiểu thương hiệu x", "1:1", 0));
        Result<ImagePromptTemplateSaved> ok = await h.Library().CreateAsync(
            new ImagePromptTemplateInput("Mẫu", "Khác", ImagePurpose.Free, null, "Ảnh có slogan to", "1:1", 0));

        Assert.False(blocked.Succeeded);
        Assert.Contains("Thương hiệu X", blocked.Error);
        Assert.True(ok.Succeeded);
        Assert.Single(ok.Value!.Warnings);
        Assert.Equal("manual", ok.Value.Template.Source);
    }

    [Fact]
    public async Task Settings_round_trip_and_validation()
    {
        using var h = new ImageStudioTestHarness();
        var dto = new ImagePromptLibrarySettingsDto(true, 12, 5, 7, true, "Tết", "Ghibli\n Disney \nGhibli", true, 4, null);

        Assert.True((await h.Library().SaveSettingsAsync(dto)).Succeeded);
        Assert.False((await h.Library().SaveSettingsAsync(dto with { TrendIntervalHours = 1 })).Succeeded);
        Assert.False((await h.Library().SaveSettingsAsync(dto with { DemoModelId = Guid.NewGuid() })).Succeeded);

        ImagePromptLibrarySettingsDto saved = await h.Library().GetSettingsAsync();
        Assert.Equal("Ghibli\nDisney", saved.BlockedTerms);
        Assert.True(saved.AutoDemoForTrend);
        Assert.Equal(4, saved.MaxAutoDemosPerRun);
    }

    internal static ImagePromptTemplate Row(string title, ImagePurpose purpose, ImagePromptTemplateStatus status = ImagePromptTemplateStatus.Published,
        DateTime? expiresAt = null, bool requiresSource = false, ImagePromptTemplateSource source = ImagePromptTemplateSource.Manual) => new()
    {
        Title = title,
        Category = "Khác",
        Purpose = purpose,
        Prompt = "Ảnh {chu_de} {san_pham}",
        AspectRatio = "1:1",
        Status = status,
        Source = source,
        ExpiresAt = expiresAt,
        RequiresSourceImage = requiresSource,
    };
}

public class ImagePromptTrendTests
{
    private static async Task SettingsAsync(ImageStudioTestHarness h, bool requireReview = false, bool enabled = true, bool autoDemo = false)
    {
        h.Db.ImagePromptLibrarySettings.Add(new ImagePromptLibrarySettings
        {
            TrendAutoUpdateEnabled = enabled,
            RequireReview = requireReview,
            AutoDemoForTrend = autoDemo,
            MaxAutoDemosPerRun = 5,
            BlockedTerms = ImagePromptTemplateRules.DefaultBlockedTerms,
        });
        await h.Db.SaveChangesAsync();
    }

    private const string AiReply = """
        Kết quả:
        ```json
        [
          {"title":"Ảnh sản phẩm trên nền vải linen","category":"Sản phẩm","purpose":"product_main","description":"Mộc mạc","trend_name":"Linen mộc",
           "prompt":"{san_pham} đặt trên vải linen màu kem, nắng chiếu xiên","aspect_ratio":"1:1","source_urls":["https://example.com/linen","javascript:alert(1)"]},
          {"title":"Thiếu chỗ giữ","category":"Sản phẩm","purpose":"product_main","prompt":"Hũ mật ong trên bàn","aspect_ratio":"1:1"},
          {"title":"Phong cách hoạt hình","category":"Mạng xã hội","purpose":"social","prompt":"Ảnh {chu_de} phong cách Ghibli","aspect_ratio":"1:1"},
          {"title":"Mục đích lạ","category":"Khác","purpose":"poster","prompt":"Ảnh {chu_de}","aspect_ratio":"1:1"},
          {"title":"Ảnh báo chí tả thực","category":"Tin tức & bài viết","purpose":"post_cover","prompt":"Ảnh {chu_de}","aspect_ratio":"16:9"}
        ]
        ```
        """;

    [Fact]
    public async Task Refresh_keeps_valid_templates_and_explains_every_rejection()
    {
        using var h = new ImageStudioTestHarness();
        await SettingsAsync(h);
        h.Db.ImagePromptTemplates.Add(ImagePromptLibraryServiceTests.Row("Ảnh báo chí tả thực", ImagePurpose.PostCover));
        await h.Db.SaveChangesAsync();
        var ai = new FakeAi(AiReply);

        Result<ImagePromptTrendRunDto> result = await h.Trends(ai).RefreshAsync("admin");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value!.Added);
        Assert.Equal(4, result.Value.Rejected);
        Assert.Contains("{san_pham}", result.Value.Notes);
        Assert.Contains("Ghibli", result.Value.Notes);
        Assert.Contains("poster", result.Value.Notes);
        Assert.Contains("Trùng tiêu đề", result.Value.Notes);
        Assert.Equal(AiTaskKeys.ImageStudioTrendTemplates, ai.LastRequest!.SkillKey);
        Assert.Contains("Ảnh báo chí tả thực", ai.LastRequest.Content);

        ImagePromptTemplate added = await h.Db.ImagePromptTemplates.SingleAsync(x => x.Source == ImagePromptTemplateSource.Trend);
        Assert.Equal(ImagePurpose.ProductMain, added.Purpose);
        Assert.Equal(ImagePromptTemplateStatus.Published, added.Status);
        Assert.Equal("https://example.com/linen", added.SourceUrls);
        Assert.NotNull(added.ExpiresAt);
    }

    [Fact]
    public async Task Review_mode_and_expiry()
    {
        using var h = new ImageStudioTestHarness();
        await SettingsAsync(h, requireReview: true);
        h.Db.ImagePromptTemplates.Add(ImagePromptLibraryServiceTests.Row("Trend cũ", ImagePurpose.Free, expiresAt: DateTime.UtcNow.AddDays(-1), source: ImagePromptTemplateSource.Trend));
        await h.Db.SaveChangesAsync();

        Result<ImagePromptTrendRunDto> result = await h.Trends(new FakeAi(AiReply)).RefreshAsync("schedule");

        Assert.Equal(1, result.Value!.Expired);
        Assert.Equal(2, result.Value.Added);
        Assert.Equal(ImagePromptTemplateStatus.Hidden, (await h.Db.ImagePromptTemplates.SingleAsync(x => x.Title == "Trend cũ")).Status);
        Assert.All(await h.Db.ImagePromptTemplates.Where(x => x.Source == ImagePromptTemplateSource.Trend && x.Title != "Trend cũ").ToListAsync(),
            t => Assert.Equal(ImagePromptTemplateStatus.PendingReview, t.Status));
    }

    [Fact]
    public async Task Scheduled_run_creates_demos_but_manual_run_does_not()
    {
        using var h = new ImageStudioTestHarness();
        await SettingsAsync(h, autoDemo: true);
        await h.AddScriptedModelAsync(price: 0.02m);

        Result<ImagePromptTrendRunDto> manual = await h.Trends(new FakeAi(AiReply)).RefreshAsync("admin");
        Assert.Equal(0, manual.Value!.DemosCreated);

        h.Db.ImagePromptTemplates.RemoveRange(h.Db.ImagePromptTemplates);
        await h.Db.SaveChangesAsync();

        Result<ImagePromptTrendRunDto> scheduled = await h.Trends(new FakeAi(AiReply)).RefreshAsync(ImagePromptTrendService.ScheduleTrigger);

        // Kho trống nên cả hai mẫu hợp lệ đều được thêm, và mỗi mẫu có ảnh demo.
        Assert.Equal(2, scheduled.Value!.Added);
        Assert.Equal(2, scheduled.Value.DemosCreated);
        Assert.All(await h.NewContext().ImagePromptTemplates.Where(x => x.Source == ImagePromptTemplateSource.Trend).ToListAsync(),
            t => Assert.NotNull(t.DemoImageUrl));
        Assert.Equal(2, await h.Db.ImageProviderCalls.CountAsync(c => c.Operation == "demo"));
    }

    [Fact]
    public async Task Ai_failure_or_garbage_records_a_failed_run()
    {
        using var h = new ImageStudioTestHarness();
        await SettingsAsync(h);

        Result<ImagePromptTrendRunDto> r1 = await h.Trends(new FakeAi(null, "Không tìm thấy kết nối AI khả dụng.")).RefreshAsync("admin");
        Result<ImagePromptTrendRunDto> r2 = await h.Trends(new FakeAi("Xin lỗi, tôi không làm được.")).RefreshAsync("admin");

        Assert.False(r1.Succeeded);
        Assert.Contains("kết nối AI", r1.Error);
        Assert.False(r2.Succeeded);
        Assert.Equal(0, await h.Db.ImagePromptTemplates.CountAsync());
        Assert.Equal(2, await h.Db.ImagePromptTrendRuns.CountAsync(x => x.Status == ImagePromptTrendRunStatus.Failed));
    }

    [Fact]
    public async Task Due_only_when_enabled_and_last_run_is_old_enough()
    {
        using var h = new ImageStudioTestHarness();
        ImagePromptTrendService trends = h.Trends(new FakeAi("[]"));

        Assert.False(await trends.IsDueAsync(), "chưa có cấu hình = tắt");

        await SettingsAsync(h);
        Assert.True(await trends.IsDueAsync());

        h.Db.ImagePromptTrendRuns.Add(new ImagePromptTrendRun { Trigger = "schedule", Status = ImagePromptTrendRunStatus.Failed, StartedAt = DateTime.UtcNow.AddHours(-2) });
        await h.Db.SaveChangesAsync();
        Assert.False(await trends.IsDueAsync(), "lần lỗi gần đây vẫn tính");
    }
}

public class ImagePromptDemoTests
{
    private static async Task<ImagePromptTemplate> TemplateAsync(ImageStudioTestHarness h, string category = "Ăn uống")
    {
        var t = new ImagePromptTemplate
        {
            Title = "Mẫu demo",
            Category = category,
            Purpose = ImagePurpose.ProductMain,
            Prompt = "Ảnh {san_pham} trên nền trắng",
            AspectRatio = "16:9",
            Status = ImagePromptTemplateStatus.Published,
        };
        h.Db.ImagePromptTemplates.Add(t);
        await h.Db.SaveChangesAsync();
        return t;
    }

    [Fact]
    public async Task Generate_fills_samples_uses_default_model_logs_cost_and_replaces_old_file()
    {
        using var h = new ImageStudioTestHarness();
        ImageModel model = await h.AddScriptedModelAsync(price: 0.03m);
        ImagePromptTemplate t = await TemplateAsync(h);

        Result<ImagePromptTemplateDto> first = await h.Demos().GenerateAsync(t.Id);
        string firstKey = (await h.NewContext().ImagePromptTemplates.SingleAsync()).DemoStorageKey!;
        Result<ImagePromptTemplateDto> second = await h.Demos().GenerateAsync(t.Id);

        Assert.True(first.Succeeded, first.Error);
        Assert.StartsWith("Tạo bằng", second.Value!.DemoSource);
        Assert.Contains("Hộp bánh pía", h.Scripted.Requests[0].Prompt);
        Assert.DoesNotContain("{san_pham}", h.Scripted.Requests[0].Prompt);
        Assert.Equal("1536x1024", h.Scripted.Requests[0].Size);
        Assert.False(File.Exists(h.Storage.GetLocalPath(firstKey)), "file demo cũ phải bị xoá");

        ImagePromptTemplate saved = await h.NewContext().ImagePromptTemplates.SingleAsync();
        Assert.True(File.Exists(h.Storage.GetLocalPath(saved.DemoStorageKey!)));
        Assert.Equal(0.06m, await h.Db.ImageProviderCalls.Where(c => c.Operation == "demo").SumAsync(c => c.CostUsd));
        Assert.Equal(model.Id, (await h.Db.ImageProviderCalls.FirstAsync()).ImageModelId);
    }

    [Fact]
    public async Task Generate_without_models_explains_why()
    {
        using var h = new ImageStudioTestHarness();
        ImagePromptTemplate t = await TemplateAsync(h);

        Result<ImagePromptTemplateDto> result = await h.Demos().GenerateAsync(t.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Chưa có model", result.Error);
    }

    [Fact]
    public async Task Upload_reencodes_and_rejects_non_images()
    {
        using var h = new ImageStudioTestHarness();
        ImagePromptTemplate t = await TemplateAsync(h);
        byte[] big = ImageStudioTestHarness.Png(2000, 1000);

        Result<ImagePromptTemplateDto> ok = await h.Demos().UploadAsync(t.Id, new MemoryStream(big), big.Length);
        Result<ImagePromptTemplateDto> bad = await h.Demos().UploadAsync(t.Id, new MemoryStream("<svg/>"u8.ToArray()), 6);

        Assert.True(ok.Succeeded, ok.Error);
        Assert.Equal("Tải lên", ok.Value!.DemoSource);
        using var image = SixLabors.ImageSharp.Image.Load(await File.ReadAllBytesAsync(h.Storage.GetLocalPath((await h.NewContext().ImagePromptTemplates.SingleAsync()).DemoStorageKey!)));
        Assert.Equal(ImagePromptDemoService.DemoMaxEdge, image.Width);
        Assert.False(bad.Succeeded);
    }

    [Fact]
    public async Task Missing_demos_estimate_batch_and_remove()
    {
        using var h = new ImageStudioTestHarness();
        await h.AddScriptedModelAsync(price: 0.05m);
        await TemplateAsync(h);
        await TemplateAsync(h, "Công nghệ");
        h.Db.ImagePromptTemplates.Add(ImagePromptLibraryServiceTests.Row("Ẩn", ImagePurpose.Free, ImagePromptTemplateStatus.Hidden));
        await h.Db.SaveChangesAsync();

        ImageDemoEstimateDto estimate = await h.Demos().EstimateMissingAsync();
        Result<ImageDemoBatchResultDto> batch = await h.Demos().GenerateMissingAsync(10);

        Assert.Equal(2, estimate.Count);
        Assert.Equal(0.10m, estimate.EstimatedCostUsd);
        Assert.Equal(2, batch.Value!.Created);
        Assert.Equal(0, (await h.Demos(h.NewContext()).EstimateMissingAsync()).Count);

        Guid first = (await h.NewContext().ImagePromptTemplates.FirstAsync(x => x.DemoStorageKey != null)).Id;
        Assert.True((await h.Demos(h.NewContext()).RemoveAsync(first)).Succeeded);
        Assert.Null((await h.NewContext().ImagePromptTemplates.SingleAsync(x => x.Id == first)).DemoImageUrl);
    }

    [Fact]
    public async Task Batch_stops_after_repeated_failures()
    {
        using var h = new ImageStudioTestHarness();
        await h.AddScriptedModelAsync();
        for (int i = 0; i < 5; i++)
        {
            await TemplateAsync(h);
        }

        for (int i = 0; i < 5; i++)
        {
            h.Scripted.Enqueue(ScriptedImageProvider.Fail(ImageProviderErrorCodes.Auth, 401, "Sai key"));
        }

        Result<ImageDemoBatchResultDto> batch = await h.Demos().GenerateMissingAsync(10);

        Assert.Equal(0, batch.Value!.Created);
        Assert.Equal(3, batch.Value.Failed);
        Assert.Contains("Dừng", batch.Value.Notes);
    }
}

public class ImagePromptAssistantTests
{
    [Fact]
    public async Task Enhance_cleans_the_reply_and_keeps_placeholders()
    {
        var ai = new FakeAi("```\n\"Ảnh {san_pham} trên bàn gỗ sồi, nắng sớm xiên qua cửa sổ, góc 45 độ.\"\n```");
        var assistant = new ImagePromptAssistant(ai);

        Result<string> result = await assistant.EnhanceAsync("ảnh {san_pham} trên bàn", ImagePurpose.ProductMain);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("Ảnh {san_pham} trên bàn gỗ sồi, nắng sớm xiên qua cửa sổ, góc 45 độ.", result.Value);
        Assert.Equal(AiTaskKeys.ImageStudioPromptEnhance, ai.LastRequest!.SkillKey);
        Assert.Contains("Ảnh sản phẩm chính", ai.LastRequest.Content);
    }

    [Fact]
    public async Task Enhance_refuses_when_ai_drops_a_placeholder_or_input_is_empty()
    {
        var assistant = new ImagePromptAssistant(new FakeAi("Ảnh hũ mật ong trên bàn gỗ"));

        Assert.Contains("{san_pham}", (await assistant.EnhanceAsync("ảnh {san_pham}", ImagePurpose.Free)).Error);
        Assert.False((await assistant.EnhanceAsync("   ", ImagePurpose.Free)).Succeeded);
    }

    [Fact]
    public async Task Suggest_reads_json_or_falls_back_to_plain_text()
    {
        var json = new ImagePromptAssistant(new FakeAi("Đây: {\"prompt\":\"Ảnh bát phở nghi ngút khói\",\"alt\":\"Bát phở bò\",\"caption\":\"Phở sáng Hà Nội\"}"));
        var plain = new ImagePromptAssistant(new FakeAi("Ảnh bát phở nghi ngút khói trên bàn gỗ"));

        Result<ImagePromptSuggestionDto> a = await json.SuggestAsync(new ImagePromptSuggestInput(ImagePurpose.PostCover, "Phở Hà Nội", "Bài về phở"));
        Result<ImagePromptSuggestionDto> b = await plain.SuggestAsync(new ImagePromptSuggestInput(ImagePurpose.PostCover, "Phở Hà Nội", null));
        Result<ImagePromptSuggestionDto> empty = await plain.SuggestAsync(new ImagePromptSuggestInput(ImagePurpose.PostCover, " ", null));

        Assert.Equal(("Ảnh bát phở nghi ngút khói", "Bát phở bò", "Phở sáng Hà Nội"), (a.Value!.Prompt, a.Value.Alt, a.Value.Caption));
        Assert.Equal("Ảnh bát phở nghi ngút khói trên bàn gỗ", b.Value!.Prompt);
        Assert.Null(b.Value.Alt);
        Assert.False(empty.Succeeded);
    }
}

public class ImageStudioTemplateUsageTests
{
    [Fact]
    public async Task Brand_placeholder_is_filled_with_the_site_name_and_other_blanks_are_refused()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        h.Db.Sites.Add(new NewsCMS.Domain.Entities.Site.Site { Id = h.Site.SiteId, Name = "Cửa hàng Mộc", Slug = "moc" });
        await h.Db.SaveChangesAsync();
        ImageModel model = await h.AddScriptedModelAsync();

        Result<ImageJobDto> blank = await h.Service().CreateAsync(new ImageJobCreateInput("tpl-blank-01", model.Id, "Ảnh {san_pham} của {thuong_hieu}", "1:1", 1), Guid.NewGuid());
        Result<ImageJobDto> ok = await h.Service().CreateAsync(new ImageJobCreateInput("tpl-ok-0001", model.Id, "Ảnh hũ mật ong của {thuong_hieu}", "1:1", 1), Guid.NewGuid());

        Assert.False(blank.Succeeded);
        Assert.Contains("{san_pham} (Tên sản phẩm)", blank.Error);
        Assert.True(ok.Succeeded, ok.Error);
        Assert.Equal("Ảnh hũ mật ong của Cửa hàng Mộc", ok.Value!.UserPrompt);
    }

    [Fact]
    public async Task Creating_from_a_template_counts_one_use()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel model = await h.AddScriptedModelAsync();
        ImagePromptTemplate t = ImagePromptLibraryServiceTests.Row("Mẫu", ImagePurpose.Free);
        h.Db.ImagePromptTemplates.Add(t);
        await h.Db.SaveChangesAsync();

        await h.Service().CreateAsync(new ImageJobCreateInput("tpl-use-001", model.Id, "Ảnh hoàng hôn", "1:1", 1, TemplateId: t.Id), Guid.NewGuid());
        await h.Service().CreateAsync(new ImageJobCreateInput("tpl-use-001", model.Id, "Ảnh hoàng hôn", "1:1", 1, TemplateId: t.Id), Guid.NewGuid());

        Assert.Equal(1, (await h.NewContext().ImagePromptTemplates.SingleAsync()).UsageCount);
    }
}

internal sealed class FakeAi(string? content, string? error = null) : IAiCompletionService
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
