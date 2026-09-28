using System.Security.Cryptography;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Storage;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdVideo.Tests.Integration;

/// <summary>
/// Nhãn AI cấu hình qua app: font lấy từ kho file, chữ lấy từ DB — máy chạy worker không cần cấu hình font.
/// </summary>
public sealed class LabelConfigTests
{
    private static readonly Dictionary<string, string?> NoMachineFont = new(StringComparer.Ordinal)
    {
        ["AdVideo:Ffmpeg:FontFile"] = string.Empty,
    };

    [Fact]
    public async Task Font_trong_kho_va_chu_nhan_trong_DB_duoc_dung_khi_may_khong_cau_hinh_font()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync(configurationOverrides: NoMachineFont);

        string objectKey = await host.InScopeAsync(async sp =>
        {
            byte[] font = await File.ReadAllBytesAsync(ExternalTools.FontFile);
            string key = $"fonts/{Convert.ToHexString(SHA256.HashData(font)).ToLowerInvariant()}.ttf";

            using var content = new MemoryStream(font);
            await sp.GetRequiredService<IStorageService>().UploadAsync(Buckets.System, key, content, "font/ttf");

            ISettingsStore settings = sp.GetRequiredService<ISettingsStore>();
            await settings.SetAsync(SettingKeys.AiLabelFontObjectKey, key, SettingValueType.String, "test");
            await settings.SetAsync(SettingKeys.AiLabelOverlayText, "Video do AI tạo", SettingValueType.String, "test");

            return key;
        });

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript));

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);
        job.Status.Should().Be(JobStatus.Completed, job.FailureReason);

        lock (host.Log)
        {
            host.Log.Should().Contain(line => line.Contains($"font minio:{objectKey}", StringComparison.Ordinal));
            host.Log.Should().Contain(line => line.Contains("nhãn AI \"Video do AI tạo\"", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Khong_co_font_nao_thi_dung_o_buoc_ghep_va_chi_duong_toi_API()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync(configurationOverrides: NoMachineFont);

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript));

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);
        job.Status.Should().Be(JobStatus.Failed);
        job.CurrentStep.Should().Be(8);
        job.FailureReason.Should().Contain("/v1/admin/assets/label-font");
    }

    [Fact]
    public async Task Khoa_font_tro_toi_object_da_mat_thi_fail_ro_ly_do_khong_ve_bang_font_khac()
    {
        // Lặng lẽ lùi về font cấu hình máy thì người vận hành tưởng video đang vẽ bằng font họ đã
        // chọn (có bản quyền) trong khi thật ra là font khác.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        await host.InScopeAsync(sp => sp.GetRequiredService<ISettingsStore>().SetAsync(
            SettingKeys.AiLabelFontObjectKey, $"fonts/{new string('0', 64)}.ttf", SettingValueType.String, "test"));

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript));

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);
        job.Status.Should().Be(JobStatus.Failed);
        job.FailureReason.Should().Contain("không còn trong bucket");
    }
}
