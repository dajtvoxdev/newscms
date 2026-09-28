using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdVideo.Tests.Integration;

/// <summary>Bước 1 với ảnh đã tải lên qua <c>POST /v1/uploads</c>: dùng thẳng object trong kho, không tải lại.</summary>
public sealed class UploadedImageIngestTests
{
    [Fact]
    public async Task Job_chi_co_anh_da_tai_len_chay_tron_ma_khong_goi_mang_lan_nao()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        Guid assetId = await SeedUploadAsync(host, host.TenantId);

        Guid jobId = await host.CreateJobAsync(TestBriefs.WithUploadedImages(assetId));

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);
        job.Status.Should().Be(JobStatus.Completed, job.FailureReason);
        host.Images.Requests.Should().BeEmpty("ảnh đã nằm trong kho — không có URL nào để tải");

        List<MediaAsset> assetsOfJob = await host.ReadAssetsAsync(jobId);
        assetsOfJob.Should().NotContain(a => a.Kind == AssetKind.ProductImage,
            "dùng lại object đã tải lên, không chép ra một bản thứ hai cho mỗi job");
    }

    [Fact]
    public async Task Anh_cua_tenant_khac_bi_chan_o_buoc_mot_du_brief_trong_DB_tro_toi_no()
    {
        // API đã chặn lúc nhận job; đây là lưới thứ hai cho brief bị sửa tay trong DB.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        Guid assetOfOther = await SeedUploadAsync(host, Guid.NewGuid());

        Guid jobId = await host.CreateJobAsync(TestBriefs.WithUploadedImages(assetOfOther));

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);
        job.Status.Should().Be(JobStatus.Failed);
        job.CurrentStep.Should().Be(1);
        job.FailureReason.Should().Contain("không thuộc tài khoản này");
        (await host.ReadCallsAsync(jobId)).Should().BeEmpty("dừng trước khi tiêu đồng nào");
    }

    private static Task<Guid> SeedUploadAsync(PipelineTestHost host, Guid tenantId) =>
        host.InScopeAsync(async sp =>
        {
            var asset = new MediaAsset
            {
                TenantId = tenantId,
                Kind = AssetKind.ProductImage,
                Bucket = Buckets.Uploads,
                ObjectKey = string.Empty,
                ContentType = "image/png",
                SizeBytes = StubImageHandler.OnePixelPng.Length,
            };

            asset.ObjectKey = IStorageService.BuildKey(tenantId, null, AssetKind.ProductImage, $"{asset.Id:N}.png");

            using var content = new MemoryStream(StubImageHandler.OnePixelPng);
            await sp.GetRequiredService<IStorageService>().UploadAsync(asset.Bucket, asset.ObjectKey, content, "image/png");

            AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync();

            return asset.Id;
        });
}
