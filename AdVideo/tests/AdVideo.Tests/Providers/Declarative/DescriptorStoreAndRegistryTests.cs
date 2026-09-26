using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Providers.Declarative;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdVideo.Tests.Providers.Declarative;

/// <summary>
/// P3 — dán JSON vào DB là có provider: store có phiên bản, bật ghi capability vào credential, và
/// registry dựng provider từ descriptor mà không một dòng C# nào biết tên provider đó.
/// </summary>
public class DescriptorStoreAndRegistryTests
{
    [Fact]
    public async Task Them_ban_moi_luon_tat_va_so_ban_tang_dan()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        await host.InScopeAsync(async sp =>
        {
            IDescriptorStore store = sp.GetRequiredService<IDescriptorStore>();
            string json = DescriptorTestKit.Read(DescriptorTestKit.Nova);

            ProviderDescriptorRow v1 = await store.AddVersionAsync(json, "bản đầu");
            ProviderDescriptorRow v2 = await store.AddVersionAsync(json.Replace("\"480p\" }", "\"480p\" } "), "sửa khoảng trắng");

            v1.Version.Should().Be(1);
            v2.Version.Should().Be(2);
            v1.IsActive.Should().BeFalse();
            v1.Sha256.Should().NotBe(v2.Sha256);
            v1.Kind.Should().Be(DescriptorKind.Video);

            (await store.GetActiveAsync("nova-grok-video-15")).Should().BeNull();
            (await store.ListAsync("nova-grok-video-15")).Select(r => r.Version).Should().Equal(2, 1);
            (await store.ListAsync()).Should().HaveCount(2);
        });
    }

    [Fact]
    public async Task Descriptor_hong_khong_vao_duoc_DB()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        await host.InScopeAsync(async sp =>
        {
            IDescriptorStore store = sp.GetRequiredService<IDescriptorStore>();

            var ex = await FluentActions
                .Awaiting(() => store.AddVersionAsync("{\"schema\":\"x\"}", null))
                .Should().ThrowAsync<DescriptorInvalidException>();

            ex.Which.Errors.Should().NotBeEmpty();
            (await store.ListAsync()).Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Bat_descriptor_thi_registry_dung_provider_moi_tu_JSON()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        await host.InScopeAsync(async sp =>
        {
            ICredentialStore credentials = sp.GetRequiredService<ICredentialStore>();
            IDescriptorStore descriptors = sp.GetRequiredService<IDescriptorStore>();

            ProviderDescriptorRow row = await descriptors.AddVersionAsync(DescriptorTestKit.Read(DescriptorTestKit.Nova), null);

            // Credential nạp TRƯỚC khi bật, với capability tạm: bật descriptor phải ghi đè nó.
            await credentials.UpsertAsync(
                new ProviderCredential
                {
                    Provider = "nova-grok-video-15",
                    ModelId = "xai/grok-imagine-video-1.5",
                    Category = ProviderCategory.Video,
                    EncryptedApiKey = string.Empty,
                    CapabilityJson = "{}",
                },
                "nova-key-0123456789");

            IProviderRegistry registry = sp.GetRequiredService<IProviderRegistry>();

            (await registry.GetVideoProvidersAsync()).Should().NotContain(p => p.Name == "nova-grok-video-15",
                "chưa bật descriptor và capability tạm không đọc được thì provider phải vắng mặt");

            await descriptors.ActivateVersionAsync("nova-grok-video-15", row.Version);

            IVideoProvider? nova = await registry.FindVideoProviderAsync("nova-grok-video-15");

            nova.Should().BeOfType<DeclarativeVideoProvider>();
            nova!.Capability.AllowedDurationSeconds.Should().Equal(6, 10);
            nova.Capability.CostPerSecondUsd.Should().Be(0.09m);

            ProviderSelectionResult selection = await registry.SelectVideoProviderAsync(new VideoRequirements
            {
                Tier = VideoTier.Draft,
                HasPerson = false,
                TargetDurationSeconds = 12,
                AspectRatio = AspectRatio.Portrait9x16,
            });

            selection.IsSuccess.Should().BeTrue(string.Join(" ", selection.Reasons));

            (await descriptors.GetActiveAsync("nova-grok-video-15"))!.Version.Should().Be(row.Version);

            await descriptors.DeactivateAsync("nova-grok-video-15");

            (await registry.FindVideoProviderAsync("nova-grok-video-15")).Should().BeNull(
                "tắt descriptor mà không có adapter viết tay thì provider biến khỏi danh sách");
        });
    }

    [Fact]
    public async Task Bat_ban_khac_thi_tat_ban_dang_bat()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        await host.InScopeAsync(async sp =>
        {
            IDescriptorStore store = sp.GetRequiredService<IDescriptorStore>();
            string json = DescriptorTestKit.Read(DescriptorTestKit.ElevenLabs);

            await store.AddVersionAsync(json, null);
            await store.AddVersionAsync(json + " ", null);

            await store.ActivateVersionAsync("elevenlabs", 1);
            await store.ActivateVersionAsync("elevenlabs", 2);

            (await store.ListAsync("elevenlabs")).Where(r => r.IsActive).Select(r => r.Version).Should().Equal(2);

            await FluentActions.Awaiting(() => store.ActivateVersionAsync("elevenlabs", 9))
                .Should().ThrowAsync<InvalidOperationException>();
        });
    }

    [Fact]
    public async Task Tat_credential_ghi_ly_do_va_bo_khoi_danh_sach()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        await host.InScopeAsync(async sp =>
        {
            ICredentialStore credentials = sp.GetRequiredService<ICredentialStore>();

            await credentials.UpsertAsync(
                new ProviderCredential
                {
                    Provider = "kling",
                    ModelId = "m",
                    Category = ProviderCategory.Video,
                    EncryptedApiKey = string.Empty,
                    CapabilityJson = "{}",
                    Notes = new string('x', 990),
                },
                "fal-key-0123456789");

            await credentials.DeactivateAsync("kling", "402 hết tiền");

            (await credentials.ListActiveAsync(ProviderCategory.Video)).Should().BeEmpty();

            ProviderCredential row = await sp.GetRequiredService<AdVideoDbContext>().ProviderCredentials.SingleAsync();
            row.IsActive.Should().BeFalse();
            row.Notes.Should().HaveLength(1000).And.EndWith("402 hết tiền");
        });
    }

    [Fact]
    public async Task Descriptor_dang_bat_ma_khong_con_hop_le_thi_bi_bo_qua()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        await host.InScopeAsync(async sp =>
        {
            AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();

            db.ProviderDescriptors.Add(new ProviderDescriptorRow
            {
                Code = "hong",
                Version = 1,
                Kind = DescriptorKind.Video,
                Json = "{\"schema\":\"cu\"}",
                Sha256 = new string('0', 64),
                IsActive = true,
            });

            await db.SaveChangesAsync();

            (await sp.GetRequiredService<IDescriptorStore>().GetActiveAsync("hong")).Should().BeNull();
        });
    }

    [Fact]
    public void Chay_kho_dung_request_mau_va_che_key()
    {
        DescriptorParseResult parsed = ProviderDescriptorParser.Parse(DescriptorTestKit.Read(DescriptorTestKit.FalKling));

        DescriptorPreviewResult preview = DescriptorPreview.Render(parsed.Descriptor!, ProviderHostAllowlist.Parse("queue.fal.run"));

        preview.Method.Should().Be("POST");
        preview.Url.Should().Be("https://queue.fal.run/fal-ai/kling-video/v3/pro/image-to-video");
        preview.Headers["Authorization"].Should().Be("Key ****");
        preview.Body.Should().Contain("\"duration\": \"5\"").And.Contain("\"image_url\": \"https://example.com/san-pham.jpg\"").And.Contain("cà phê");
        preview.Warnings.Should().ContainSingle().Which.Should().Contain("URL từ phản hồi submit");
    }

    [Fact]
    public void Chay_kho_canh_bao_host_ngoai_allowlist()
    {
        DescriptorParseResult nova = ProviderDescriptorParser.Parse(DescriptorTestKit.Read(DescriptorTestKit.Nova));

        DescriptorPreviewResult preview = DescriptorPreview.Render(nova.Descriptor!, ProviderHostAllowlist.Parse(""));

        preview.Warnings.Should().ContainSingle().Which.Should().Contain("sẽ bị chặn");
        preview.Headers["Authorization"].Should().Be("Bearer ****");

        DescriptorParseResult tts = ProviderDescriptorParser.Parse(DescriptorTestKit.Read(DescriptorTestKit.ElevenLabs));

        DescriptorPreviewResult ttsPreview = DescriptorPreview.Render(tts.Descriptor!, ProviderHostAllowlist.Parse("api.elevenlabs.io"));

        ttsPreview.Warnings.Should().BeEmpty();
        ttsPreview.Url.Should().Contain("/v1/text-to-speech/voice-id-mau/with-timestamps");
        ttsPreview.Body.Should().Contain("req-truoc");
    }
}
