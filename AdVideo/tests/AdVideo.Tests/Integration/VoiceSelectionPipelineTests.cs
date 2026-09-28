using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdVideo.Tests.Integration;

/// <summary>Bước 4 đọc bằng đúng giọng đã chọn — và dừng rõ ràng khi giọng không còn.</summary>
public sealed class VoiceSelectionPipelineTests
{
    [Fact]
    public async Task Job_chon_giong_clone_cua_chinh_minh_chay_tron()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();
        Guid voiceId = await AddVoiceAsync(host, host.TenantId);

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript), j => j.VoiceProfileId = voiceId);
        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);
        job.Status.Should().Be(JobStatus.Completed, job.FailureReason);
    }

    [Fact]
    public async Task Giong_clone_cua_tenant_khac_bi_chan_o_buoc_giong_doc_du_brief_tro_toi()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();
        Guid voiceOfOther = await AddVoiceAsync(host, Guid.NewGuid());

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript), j => j.VoiceProfileId = voiceOfOther);
        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);
        job.Status.Should().Be(JobStatus.Failed);
        job.CurrentStep.Should().Be(4);
        job.FailureReason.Should().Contain("Giọng đã chọn không còn");
        (await host.ReadCallsAsync(jobId)).Should().BeEmpty("dừng trước khi gọi engine");
    }

    [Fact]
    public async Task Giong_bi_xoa_sau_khi_tao_job_thi_dung_ro_ly_do_khong_doc_bang_giong_khac()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();
        Guid voiceId = await AddVoiceAsync(host, host.TenantId);
        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript), j => j.VoiceProfileId = voiceId);

        await host.InScopeAsync(async sp =>
        {
            AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
            VoiceProfile v = await db.VoiceProfiles.SingleAsync(x => x.Id == voiceId);
            v.IsDeleted = true;
            await db.SaveChangesAsync();
        });

        await host.RunAsync(jobId);

        (await host.ReadJobAsync(jobId)).Status.Should().Be(JobStatus.Failed);
    }

    private static Task<Guid> AddVoiceAsync(PipelineTestHost host, Guid tenantId) =>
        host.InScopeAsync(async sp =>
        {
            var voice = new VoiceProfile
            {
                TenantId = tenantId,
                Name = "Giọng clone",
                Provider = ProviderNames.Fake,
                ProviderVoiceId = $"fake-clone-{Guid.NewGuid():N}",
                Kind = VoiceProfileKind.Cloned,
            };

            AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
            db.VoiceProfiles.Add(voice);
            await db.SaveChangesAsync();

            return voice.Id;
        });
}
