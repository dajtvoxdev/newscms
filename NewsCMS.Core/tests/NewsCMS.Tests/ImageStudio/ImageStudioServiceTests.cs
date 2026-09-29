using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Application.ImageStudio.Providers;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio;
using NewsCMS.Infrastructure.ImageStudio.Imaging;
using NewsCMS.Infrastructure.ImageStudio.Jobs;

namespace NewsCMS.Tests.ImageStudio;

public class ImageStudioServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static ImageJobCreateInput Input(Guid modelId, string key = "key-00000001", int count = 2, string aspect = "16:9", string prompt = "Cà phê muối Huế") =>
        new(key, modelId, prompt, aspect, count, ImagePurpose.PostCover);

    [Fact]
    public async Task Create_is_refused_until_the_site_is_enabled()
    {
        using var h = new ImageStudioTestHarness();
        ImageModel model = await h.AddScriptedModelAsync();

        Result<ImageJobDto> result = await h.Service().CreateAsync(Input(model.Id), UserId);
        ImageStudioFormDto form = await h.Service().GetFormAsync(UserId);

        Assert.False(result.Succeeded);
        Assert.Contains("chưa được bật", result.Error);
        Assert.False(form.Enabled);
        Assert.Empty(h.Db.ImageJobs.IgnoreQueryFilters());
    }

    [Fact]
    public async Task Create_saves_a_queued_job_with_final_prompt_size_and_estimate()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync(brandStyle: "tông nâu ấm");
        ImageModel model = await h.AddScriptedModelAsync(price: 0.05m);

        Result<ImageJobDto> result = await h.Service().CreateAsync(Input(model.Id, count: 3), UserId);

        Assert.True(result.Succeeded, result.Error);
        ImageJobDto job = result.Value!;
        Assert.Equal(ImageJobStatus.Queued, job.Status);
        Assert.Equal("1536x1024", job.Size);
        Assert.Equal(0.15m, job.EstimatedCostUsd);
        Assert.Contains("tông nâu ấm", job.FinalPrompt);
        Assert.Equal(model.Name, job.ModelName);
        Assert.True(h.Queue.Reader.TryRead(out Guid queued));
        Assert.Equal(job.Id, queued);

        ImageJob saved = await h.Db.ImageJobs.SingleAsync();
        Assert.Equal(h.Site.SiteId, saved.SiteId);
        Assert.Equal(UserId, saved.CreatedBy);
    }

    [Fact]
    public async Task Create_with_the_same_key_returns_the_same_job()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel model = await h.AddScriptedModelAsync();

        Result<ImageJobDto> first = await h.Service().CreateAsync(Input(model.Id, key: "same-key-123"), UserId);
        Result<ImageJobDto> second = await h.Service().CreateAsync(Input(model.Id, key: "same-key-123", prompt: "khác hẳn"), UserId);

        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal(1, await h.Db.ImageJobs.CountAsync());
    }

    [Theory]
    [InlineData("", "16:9", 1, "mô tả")]
    [InlineData("táo", "5:1", 1, "Tỉ lệ")]
    [InlineData("táo", "1:1", 0, "Số ảnh")]
    [InlineData("táo", "1:1", 5, "Số ảnh")]
    public async Task Create_validates_input(string prompt, string aspect, int count, string expected)
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel model = await h.AddScriptedModelAsync();

        Result<ImageJobDto> result = await h.Service().CreateAsync(new ImageJobCreateInput("key-validate", model.Id, prompt, aspect, count), UserId);

        Assert.False(result.Succeeded);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public async Task Create_refuses_inactive_models_and_model_variant_caps()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel off = await h.AddScriptedModelAsync(isActive: false);
        ImageModel small = await h.AddScriptedModelAsync(maxVariants: 1);

        Result<ImageJobDto> inactive = await h.Service().CreateAsync(Input(off.Id, key: "key-inactive"), UserId);
        Result<ImageJobDto> tooMany = await h.Service().CreateAsync(Input(small.Id, key: "key-toomany", count: 2), UserId);

        Assert.Contains("không còn dùng được", inactive.Error);
        Assert.Contains("từ 1 đến 1", tooMany.Error);
    }

    [Fact]
    public async Task Monthly_quota_counts_pending_variants_and_produced_images()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync(monthly: 5);
        ImageModel model = await h.AddScriptedModelAsync();

        Result<ImageJobDto> a = await h.Service().CreateAsync(Input(model.Id, key: "quota-a-001", count: 3), UserId);
        Result<ImageJobDto> b = await h.Service().CreateAsync(Input(model.Id, key: "quota-b-001", count: 3), UserId);
        Result<ImageJobDto> c = await h.Service().CreateAsync(Input(model.Id, key: "quota-c-001", count: 2), UserId);

        Assert.True(a.Succeeded);
        Assert.False(b.Succeeded);
        Assert.Contains("còn 2 lượt", b.Error);
        Assert.True(c.Succeeded);
        Assert.Equal(0, (await h.Service().GetFormAsync(UserId)).MonthlyRemaining);
    }

    [Fact]
    public async Task Daily_quota_is_per_user()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync(daily: 2);
        ImageModel model = await h.AddScriptedModelAsync();
        Guid other = Guid.NewGuid();

        Assert.True((await h.Service().CreateAsync(Input(model.Id, key: "daily-a-001", count: 2), UserId)).Succeeded);
        Result<ImageJobDto> again = await h.Service().CreateAsync(Input(model.Id, key: "daily-b-001", count: 1), UserId);
        Result<ImageJobDto> otherUser = await h.Service().CreateAsync(Input(model.Id, key: "daily-c-001", count: 2), other);

        Assert.Contains("hết lượt tạo ảnh hôm nay", again.Error);
        Assert.True(otherUser.Succeeded);
    }

    [Fact]
    public async Task Form_lists_only_active_models_the_server_can_run()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel active = await h.AddScriptedModelAsync();
        await h.AddScriptedModelAsync(isActive: false);
        h.Db.ImageModels.Add(new ImageModel { Name = "Gemini chưa có adapter", Adapter = ImageProviderAdapter.Gemini, ModelId = "g", Capabilities = ImageCapabilities.TextToImage, IsActive = true });
        await h.Db.SaveChangesAsync();

        ImageStudioFormDto form = await h.Service().GetFormAsync(UserId);

        Assert.True(form.Enabled);
        ImageModelOptionDto option = Assert.Single(form.Models);
        Assert.Equal(active.Id, option.Id);
    }

    [Fact]
    public async Task Jobs_of_another_site_are_invisible()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel model = await h.AddScriptedModelAsync();
        Guid jobId = (await h.Service().CreateAsync(Input(model.Id), UserId)).Value!.Id;

        h.Site.Set(Guid.NewGuid(), "other", "Default");
        using var otherDb = h.NewContext();

        Assert.Empty(await h.Service(otherDb).GetJobsAsync([jobId]));
        Assert.Null(await h.Service(otherDb).GetJobAsync(jobId));
    }

    [Fact]
    public async Task Cancel_only_works_while_queued()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel model = await h.AddScriptedModelAsync();
        Guid queued = (await h.Service().CreateAsync(Input(model.Id, key: "cancel-a-01"), UserId)).Value!.Id;
        Guid running = (await h.Service().CreateAsync(Input(model.Id, key: "cancel-b-01"), UserId)).Value!.Id;
        (await h.Db.ImageJobs.SingleAsync(j => j.Id == running)).Status = ImageJobStatus.Running;
        await h.Db.SaveChangesAsync();

        Assert.True((await h.Service().CancelAsync(queued)).Succeeded);
        Assert.False((await h.Service().CancelAsync(running)).Succeeded);
        Assert.Equal(ImageJobStatus.Canceled, (await h.NewContext().ImageJobs.SingleAsync(j => j.Id == queued)).Status);
    }

    [Fact]
    public async Task Run_then_promote_puts_a_labelled_image_into_the_ai_folder_once()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel model = await h.AddScriptedModelAsync();
        Guid jobId = (await h.Service().CreateAsync(Input(model.Id, count: 2), UserId)).Value!.Id;

        await h.Runner().RunAsync(jobId);

        ImageJobDto job = (await h.Service(h.NewContext()).GetJobAsync(jobId))!;
        Assert.Equal(ImageJobStatus.Succeeded, job.Status);
        Assert.Equal(2, job.Outputs.Count);

        using var db = h.NewContext();
        ImageStudioService service = h.Service(db);
        Result<ImagePromoteResultDto> promoted = await service.PromoteAsync(job.Outputs[0].Id, UserId, "Ly cà phê muối");
        Result<ImagePromoteResultDto> again = await service.PromoteAsync(job.Outputs[0].Id, UserId, null);

        Assert.True(promoted.Succeeded, promoted.Error);
        Assert.Equal(promoted.Value!.MediaId, again.Value!.MediaId);

        Media media = await db.Medias.Include(m => m.Folder).SingleAsync();
        Assert.Equal(MediaOrigins.AiGenerated, media.Origin);
        Assert.Equal(jobId, media.AiJobId);
        Assert.Equal("Ly cà phê muối", media.AltText);
        Assert.Equal(ImageStudioService.AiFolderName, media.Folder!.Name);
        Assert.Equal(AiImageFinalizer.TrainedAlgorithmicMedia,
            AiImageFinalizer.ReadDigitalSourceType(await File.ReadAllBytesAsync(h.Storage.GetLocalPath(media.StorageKey))));
    }

    [Fact]
    public async Task Promote_refuses_outputs_of_another_site()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel model = await h.AddScriptedModelAsync();
        Guid jobId = (await h.Service().CreateAsync(Input(model.Id, count: 1), UserId)).Value!.Id;
        await h.Runner().RunAsync(jobId);
        Guid outputId = (await h.NewContext().ImageJobOutputs.SingleAsync()).Id;

        h.Site.Set(Guid.NewGuid(), "other", "Default");
        Result<ImagePromoteResultDto> result = await h.Service(h.NewContext()).PromoteAsync(outputId, UserId, null);

        Assert.False(result.Succeeded);
        Assert.Empty(h.NewContext().Medias.IgnoreQueryFilters());
    }
}

public class ImageJobRunnerTests
{
    private static async Task<(ImageStudioTestHarness H, Guid JobId, ImageModel Model)> QueuedJobAsync(int count = 2)
    {
        var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel model = await h.AddScriptedModelAsync(price: 0.04m);
        Result<ImageJobDto> created = await h.Service().CreateAsync(new ImageJobCreateInput("runner-key-01", model.Id, "Hũ mật ong", "1:1", count), Guid.NewGuid());
        return (h, created.Value!.Id, model);
    }

    [Fact]
    public async Task Run_saves_every_variant_and_one_ledger_row_per_call()
    {
        var (h, jobId, _) = await QueuedJobAsync(count: 3);
        using var _h = h;

        await h.Runner().RunAsync(jobId);

        using var db = h.NewContext();
        ImageJob job = await db.ImageJobs.Include(j => j.Outputs).SingleAsync();
        Assert.Equal(ImageJobStatus.Succeeded, job.Status);
        Assert.Null(job.Error);
        Assert.Equal(3, job.Outputs.Count);
        Assert.Equal([0, 1, 2], job.Outputs.Select(o => o.Index).OrderBy(i => i));
        Assert.All(job.Outputs, o => Assert.True(File.Exists(h.Storage.GetLocalPath(o.StorageKey))));
        Assert.Equal(0.12m, job.CostUsd);
        Assert.Equal(3, await db.ImageProviderCalls.CountAsync(c => c.JobId == jobId && c.Operation == "generate"));
        Assert.All(h.Scripted.Requests, r => Assert.Equal("1024x1024", r.Size));
        Assert.All(h.Scripted.Requests, r => Assert.Equal("sk-test-key-1234567890", r.Context.ApiKey));
    }

    [Fact]
    public async Task Rate_limit_is_retried_once_and_both_calls_are_logged()
    {
        var (h, jobId, _) = await QueuedJobAsync(count: 1);
        using var _h = h;
        h.Scripted.Enqueue(ScriptedImageProvider.Fail(ImageProviderErrorCodes.RateLimited, 429));

        await h.Runner().RunAsync(jobId);

        using var db = h.NewContext();
        ImageJob job = await db.ImageJobs.Include(j => j.Outputs).SingleAsync();
        Assert.Equal(ImageJobStatus.Succeeded, job.Status);
        Assert.Single(job.Outputs);
        List<ImageProviderCall> calls = await db.ImageProviderCalls.OrderBy(c => c.CreatedAt).ToListAsync();
        Assert.Equal(2, calls.Count);
        Assert.Equal(0.04m, calls.Sum(c => c.CostUsd));
    }

    [Fact]
    public async Task Content_policy_is_not_retried_and_fails_the_job()
    {
        var (h, jobId, _) = await QueuedJobAsync(count: 1);
        using var _h = h;
        h.Scripted.Enqueue(ScriptedImageProvider.Fail(ImageProviderErrorCodes.ContentPolicy, 400, "Nội dung bị từ chối"));

        await h.Runner().RunAsync(jobId);

        using var db = h.NewContext();
        ImageJob job = await db.ImageJobs.SingleAsync();
        Assert.Equal(ImageJobStatus.Failed, job.Status);
        Assert.Equal("Nội dung bị từ chối", job.Error);
        Assert.Single(h.Scripted.Requests);
        Assert.Equal(0, job.CostUsd);
    }

    [Fact]
    public async Task Partial_success_keeps_the_good_images_and_explains_the_rest()
    {
        var (h, jobId, _) = await QueuedJobAsync(count: 2);
        using var _h = h;
        h.Scripted.Enqueue(ScriptedImageProvider.Fail(ImageProviderErrorCodes.BadRequest, 400, "Sai tham số"));

        await h.Runner().RunAsync(jobId);

        ImageJob job = await h.NewContext().ImageJobs.Include(j => j.Outputs).SingleAsync();
        Assert.Equal(ImageJobStatus.Succeeded, job.Status);
        Assert.Single(job.Outputs);
        Assert.Contains("1/2", job.Error);
    }

    [Fact]
    public async Task Garbage_bytes_are_charged_but_not_saved()
    {
        var (h, jobId, _) = await QueuedJobAsync(count: 1);
        using var _h = h;
        h.Scripted.Enqueue(ImageProviderResult.Success(new ImageData("không phải ảnh"u8.ToArray(), "image/png"), 200, 5));

        await h.Runner().RunAsync(jobId);

        using var db = h.NewContext();
        ImageJob job = await db.ImageJobs.SingleAsync();
        ImageProviderCall call = await db.ImageProviderCalls.SingleAsync();
        Assert.Equal(ImageJobStatus.Failed, job.Status);
        Assert.Equal(ImageProviderErrorCodes.InvalidResponse, call.ErrorCode);
        Assert.Equal(0.04m, call.CostUsd);
    }

    [Fact]
    public async Task Jobs_not_queued_are_left_alone()
    {
        var (h, jobId, _) = await QueuedJobAsync(count: 1);
        using var _h = h;
        (await h.Db.ImageJobs.SingleAsync()).Status = ImageJobStatus.Canceled;
        await h.Db.SaveChangesAsync();

        await h.Runner().RunAsync(jobId);

        Assert.Empty(h.Scripted.Requests);
        Assert.Equal(ImageJobStatus.Canceled, (await h.NewContext().ImageJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task Missing_api_key_fails_with_a_readable_reason()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        Guid connection = await h.AddConnectionAsync(apiKey: null);
        var model = new ImageModel { Name = "Không key", ConnectionId = connection, Adapter = ImageProviderAdapter.OpenAiImages, ModelId = "m", Capabilities = ImageCapabilities.TextToImage, IsActive = true };
        h.Db.ImageModels.Add(model);
        await h.Db.SaveChangesAsync();
        Guid jobId = (await h.Service().CreateAsync(new ImageJobCreateInput("no-key-0001", model.Id, "táo", "1:1", 1), Guid.NewGuid())).Value!.Id;

        await h.Runner().RunAsync(jobId);

        ImageJob job = await h.NewContext().ImageJobs.SingleAsync();
        Assert.Equal(ImageJobStatus.Failed, job.Status);
        Assert.Contains("chưa có API key", job.Error);
        Assert.Empty(h.Scripted.Requests);
    }

    [Fact]
    public async Task Fake_provider_draws_an_image_with_the_requested_orientation()
    {
        using var h = new ImageStudioTestHarness();
        await h.EnableSiteAsync();
        ImageModel model = await h.AddFakeModelAsync();
        Guid jobId = (await h.Service().CreateAsync(new ImageJobCreateInput("fake-key-001", model.Id, "Bình minh trên biển", "16:9", 1), Guid.NewGuid())).Value!.Id;

        await h.Runner().RunAsync(jobId);

        ImageJobOutput output = await h.NewContext().ImageJobOutputs.SingleAsync();
        Assert.True(output.Width > output.Height);
    }
}

public class ImageModelServiceTests
{
    private static ImageModelUpsertDto Dto(Guid connectionId, string sizes = "1024x1024\n1536x1024", string? extra = null, ImageProviderAdapter adapter = ImageProviderAdapter.OpenAiImages, bool isDefault = false) =>
        new(null, "GPT Image", "Chất lượng cao", connectionId, adapter, "gpt-image-1", ImageCapabilities.TextToImage, 0, MaskConvention.None,
            sizes, 4, "high", "png", 0.04m, 180, extra, true, isDefault, 0);

    [Theory]
    [InlineData("1024x1024\nlarge", null, "Kích thước không hợp lệ")]
    [InlineData("1024x1024", "{không phải json", "không phải JSON")]
    [InlineData("1024x1024", "[1,2]", "JSON object")]
    public async Task Create_validates_sizes_and_extra_params(string sizes, string? extra, string expected)
    {
        using var h = new ImageStudioTestHarness();
        Guid connection = await h.AddConnectionAsync();

        Result<Guid> result = await h.ModelService().CreateAsync(Dto(connection, sizes, extra));

        Assert.False(result.Succeeded);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public async Task Create_refuses_adapters_without_code_and_insecure_connections()
    {
        using var h = new ImageStudioTestHarness();
        Guid secure = await h.AddConnectionAsync();
        Guid insecure = await h.AddConnectionAsync("http://api.example.com/v1");

        Result<Guid> gemini = await h.ModelService().CreateAsync(Dto(secure, adapter: ImageProviderAdapter.Gemini));
        Result<Guid> http = await h.ModelService().CreateAsync(Dto(insecure));

        Assert.Contains("chưa được hỗ trợ", gemini.Error);
        Assert.Contains("https", http.Error);
    }

    [Fact]
    public async Task Only_one_default_model()
    {
        using var h = new ImageStudioTestHarness();
        Guid connection = await h.AddConnectionAsync();
        ImageModelService service = h.ModelService();

        Guid first = (await service.CreateAsync(Dto(connection, isDefault: true))).Value;
        Guid second = (await service.CreateAsync(Dto(connection, isDefault: true))).Value;

        IReadOnlyList<ImageModelDto> all = await service.GetAllAsync();
        Assert.False(all.Single(m => m.Id == first).IsDefault);
        Assert.True(all.Single(m => m.Id == second).IsDefault);
    }

    [Fact]
    public async Task Test_calls_the_model_logs_the_cost_and_stores_the_outcome()
    {
        using var h = new ImageStudioTestHarness();
        ImageModel model = await h.AddScriptedModelAsync(price: 0.02m);

        Result<ImageModelTestResultDto> ok = await h.ModelService().TestAsync(model.Id);
        h.Scripted.Enqueue(ScriptedImageProvider.Fail(ImageProviderErrorCodes.Auth, 401, "Sai key"));
        Result<ImageModelTestResultDto> bad = await h.ModelService().TestAsync(model.Id);

        Assert.True(ok.Value!.Ok);
        Assert.NotNull(ok.Value.PreviewUrl);
        Assert.False(bad.Value!.Ok);
        Assert.Equal("Sai key", bad.Value.Error);

        using var db = h.NewContext();
        ImageModel saved = await db.ImageModels.SingleAsync();
        Assert.False(saved.LastTestOk);
        Assert.Equal("Sai key", saved.LastTestError);
        Assert.Equal(2, await db.ImageProviderCalls.CountAsync(c => c.Operation == "test"));
        Assert.Equal(0.02m, await db.ImageProviderCalls.SumAsync(c => c.CostUsd));
    }

    [Fact]
    public async Task Delete_is_soft_and_clears_default()
    {
        using var h = new ImageStudioTestHarness();
        Guid connection = await h.AddConnectionAsync();
        Guid id = (await h.ModelService().CreateAsync(Dto(connection, isDefault: true))).Value;

        await h.ModelService().DeleteAsync(id);

        using var db = h.NewContext();
        Assert.Empty(await db.ImageModels.ToListAsync());
        ImageModel deleted = await db.ImageModels.IgnoreQueryFilters().SingleAsync();
        Assert.True(deleted.IsDeleted);
        Assert.False(deleted.IsDefault);
    }
}

public class ImageStudioSiteServiceTests
{
    [Fact]
    public async Task Sites_list_shows_enabled_state_and_this_month_usage()
    {
        using var h = new ImageStudioTestHarness();
        Guid siteA = h.Site.SiteId;
        h.Db.Sites.Add(new NewsCMS.Domain.Entities.Site.Site { Id = siteA, Name = "A", Slug = "a" });
        h.Db.Sites.Add(new NewsCMS.Domain.Entities.Site.Site { Id = Guid.NewGuid(), Name = "B", Slug = "b" });
        h.Db.ImageProviderCalls.Add(new ImageProviderCall { SiteId = siteA, ModelId = "m", Operation = "generate", ImagesReturned = 1, CostUsd = 0.04m });
        h.Db.ImageProviderCalls.Add(new ImageProviderCall { SiteId = siteA, ModelId = "m", Operation = "generate", ImagesReturned = 1, CostUsd = 0.04m, CreatedAt = DateTime.UtcNow.AddMonths(-2) });
        await h.Db.SaveChangesAsync();
        var service = new ImageStudioSiteService(h.Db);

        Assert.True((await service.SaveAsync(new ImageStudioSiteQuotaInput(siteA, true, 100, 10))).Succeeded);
        Assert.False((await service.SaveAsync(new ImageStudioSiteQuotaInput(siteA, true, -1, 10))).Succeeded);
        Assert.False((await service.SaveAsync(new ImageStudioSiteQuotaInput(Guid.NewGuid(), true, 1, 1))).Succeeded);

        IReadOnlyList<ImageStudioSiteRowDto> rows = await service.GetSitesAsync();
        ImageStudioSiteRowDto a = rows.Single(r => r.SiteId == siteA);
        Assert.True(a.Enabled);
        Assert.Equal(100, a.MonthlyImageQuota);
        Assert.Equal(1, a.ImagesThisMonth);
        Assert.Equal(0.04m, a.CostThisMonthUsd);
        Assert.False(rows.Single(r => r.SiteName == "B").Enabled);
    }
}
