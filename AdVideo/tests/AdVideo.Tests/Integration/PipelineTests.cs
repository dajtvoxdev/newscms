using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;
using AdVideo.Core.Qc;
using AdVideo.Core.Storage;
using AdVideo.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdVideo.Tests.Integration;

/// <summary>
/// Chạy trọn pipeline Sprint 1 (bước 1 → 4 → 5 → 6 → 8 → 9) với provider giả và FFmpeg thật.
/// </summary>
/// <remarks>
/// <para>
/// Test unit đã canh từng lớp logic riêng. Cái mà chỉ test này bắt được là những thứ nằm ở chỗ
/// nối: một bước ghi khoá vào <c>Bag</c> mà bước sau đọc bằng tên khác, một object key dựng ở
/// bucket này rồi tải về từ bucket kia, một filter graph hợp lệ trên giấy mà FFmpeg từ chối.
/// </para>
/// <para>
/// <b>FFmpeg ở đây là thật.</b> Không stub, không fake runner. Đổi lại, các test có dựng video
/// chạy bằng phút chứ không bằng mili-giây — nên chỉ những test thật sự cần video thành phẩm mới
/// đi tới bước 8.
/// </para>
/// </remarks>
public class PipelineTests
{
    [Fact]
    public async Task Job_hop_le_chay_het_pipeline_va_cho_ra_video_ffprobe_doc_duoc()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid());

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.FailureReason.Should().BeNull("job hỏng thì lý do phải đọc được ở đây trước khi xem các assert sau");
        job.Status.Should().Be(JobStatus.Completed);
        job.CurrentStep.Should().Be(9, "bước cuối cùng chạy được phải là bước kiểm tra chất lượng");
        job.StartedAt.Should().NotBeNull();
        job.CompletedAt.Should().NotBeNull();
        job.FinalVideoAssetId.Should().NotBeNull();
        job.VoiceAudioAssetId.Should().NotBeNull();

        List<Shot> shots = await host.ReadShotsAsync(jobId);

        shots.Should().HaveCountGreaterThan(1, "lời thoại dài hơn bậc 10 giây nên phải chia nhiều shot");
        shots.Should().OnlyContain(s => s.Status == ShotStatus.Rendered);
        shots.Select(s => s.Index).Should().BeInAscendingOrder();
        shots.Should().OnlyContain(s => s.ClipAssetId != null);

        List<MediaAsset> assets = await host.ReadAssetsAsync(jobId);

        assets.Should().ContainSingle(a => a.Kind == AssetKind.ProductImage);
        assets.Should().ContainSingle(a => a.Kind == AssetKind.VoiceAudio);
        assets.Count(a => a.Kind == AssetKind.ShotClip).Should().Be(shots.Count);

        MediaAsset final = assets.Should().ContainSingle(a => a.Kind == AssetKind.FinalVideo).Which;

        final.Bucket.Should().Be(Buckets.Final);
        File.Exists(host.PathOf(final.Bucket, final.ObjectKey)).Should().BeTrue(
            "hàng lưu trong DB mà không có file trên đĩa là cách hỏng tệ nhất: API vẫn trả link");

        // ffprobe THẬT trên file thành phẩm. Đây là lời khẳng định duy nhất trong cả bộ test rằng
        // thứ ghép ra là một file video phát được, chứ không phải một mớ byte có đuôi .mp4.
        MediaProbeResult probe = await host.ProbeAsync(final.Bucket, final.ObjectKey, measureLoudness: true);

        probe.VideoCodec.Should().Be("h264");
        probe.HasAudio.Should().BeTrue("voice-over là lý do tồn tại của video này");
        probe.Width.Should().Be(1080);
        probe.Height.Should().Be(1920);

        double expectedSeconds = shots.Sum(s => (double)s.VideoDurationSeconds);

        probe.DurationSeconds.Should().BeApproximately(expectedSeconds, 0.5,
            "độ dài thật phải khớp timeline đã khoá — lệch nghĩa là một shot bị nuốt hoặc bị lặp");

        probe.LoudnessLufs.Should().NotBeNull("bước 8 chạy loudnorm nên ebur128 phải đo được");

        // Nhãn AI trong metadata: Luật TTNT 2025 không có nút tắt, và bước 9 đánh trượt job nếu
        // thiếu. Kiểm lại ở đây để nếu một ngày nào đó bước 9 bị nới lỏng thì test này đỏ.
        probe.Metadata.Should().NotBeNull();
        probe.Metadata!.Values.Should().Contain(v => v.Contains("AI", StringComparison.OrdinalIgnoreCase));

        List<ProviderCall> calls = await host.ReadCallsAsync(jobId);

        calls.Should().HaveCount(shots.Count + 1, "mỗi shot một lời gọi video, cộng một lời gọi TTS");
        calls.Should().OnlyContain(c => c.IsSuccess);
        calls.Should().OnlyContain(c => c.Provider == ProviderNames.Fake,
            "test tự động không được gọi provider trả tiền");
        calls.Should().ContainSingle(c => c.Category == ProviderCategory.TextToSpeech);

        job.ActualCostUsd.Should().Be(0m);
    }

    [Fact]
    public async Task Clip_tho_khong_co_tieng_nhung_video_thanh_pham_thi_co()
    {
        // Luật D3: provider giả bật tiếng mặc định và không tách được sfx khỏi thoại, nên hệ thống
        // phải yêu cầu nó tắt tiếng hẳn. Không làm thế thì video cuối có hai giọng nói chồng nhau —
        // giọng của model và giọng đọc thật — và không có phép kiểm tự động nào bắt được.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript));

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);
        job.FailureReason.Should().BeNull();
        job.Status.Should().Be(JobStatus.Completed);

        List<MediaAsset> assets = await host.ReadAssetsAsync(jobId);

        foreach (MediaAsset clip in assets.Where(a => a.Kind == AssetKind.ShotClip))
        {
            clip.HasAudio.Should().BeFalse("clip thô còn tiếng nghĩa là đã quên tắt tiếng của model");

            MediaProbeResult clipProbe = await host.ProbeAsync(clip.Bucket, clip.ObjectKey);
            clipProbe.HasAudio.Should().BeFalse("cờ trong DB có thể đúng trong khi file thì không");
        }

        MediaAsset final = assets.Single(a => a.Kind == AssetKind.FinalVideo);

        (await host.ProbeAsync(final.Bucket, final.ObjectKey)).HasAudio.Should().BeTrue();
    }

    [Fact]
    public async Task Brief_thieu_loi_thoai_thi_dung_o_buoc_mot_truoc_khi_goi_provider()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        Guid jobId = await host.CreateJobAsync(TestBriefs.WithoutScript());

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.Status.Should().Be(JobStatus.Failed);
        job.CurrentStep.Should().Be(1);
        job.FailureReason.Should().Contain("voice.script");

        (await host.ReadCallsAsync(jobId)).Should().BeEmpty(
            "brief sai là lỗi của request, không phải lý do để tiêu một xu nào");
    }

    [Fact]
    public async Task Anh_san_pham_khong_phai_http_thi_bi_tu_choi_chu_khong_doc_file_tren_may_chu()
    {
        // file:// và data: là đường đi kinh điển để bắt máy chủ đọc hộ file của chính nó. Ở đây
        // nó bị chặn trước cả khi có một request nào được gửi đi.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        Guid jobId = await host.CreateJobAsync(TestBriefs.WithLocalFileImage());

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.Status.Should().Be(JobStatus.Failed);
        job.FailureReason.Should().Contain("http/https");
        host.Images.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Anh_san_pham_tra_404_thi_job_fail_ngay_chu_khong_thu_lai()
    {
        // 404 là lỗi vĩnh viễn: link của khách sai. Thử lại ba lần chỉ làm job chậm thêm 15 giây
        // rồi vẫn fail với đúng lý do cũ.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        host.Images.StatusOverrides[TestBriefs.ProductImageUrl] = System.Net.HttpStatusCode.NotFound;

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid());

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.Status.Should().Be(JobStatus.Failed);
        job.FailureReason.Should().Contain("404");
        host.Images.Requests.Should().ContainSingle("404 không được thử lại");
    }

    [Fact]
    public async Task Cham_tran_chi_tieu_thi_dung_truoc_khi_thu_giong_doc()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(), job => job.MaxCostUsd = 0m);

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.Status.Should().Be(JobStatus.Failed);
        job.CurrentStep.Should().Be(4);
        job.FailureReason.Should().Contain("trần");

        (await host.ReadCallsAsync(jobId)).Should().BeEmpty(
            "trần chi tiêu phải chặn TRƯỚC lời gọi, không phải ghi sổ sau khi đã gọi");
    }

    [Fact]
    public async Task TTS_tra_audio_nhung_khong_co_moc_thoi_gian_thi_khong_khoa_timeline()
    {
        // Ca hỏng im lặng nhất của bước 4: provider trả 200 kèm file audio nghe được, nhưng không
        // có mốc theo từ. Bước 5 mà tin vào IsSuccess thì nó chia shot trên một danh sách rỗng và
        // cho ra một timeline sai mà không bước nào báo lỗi.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync(
            fake => fake.TtsWithoutTimings = true);

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript));

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.Status.Should().Be(JobStatus.Failed);
        job.CurrentStep.Should().Be(4);
        job.FailureReason.Should().Contain("mốc thời gian");

        (await host.ReadShotsAsync(jobId)).Should().BeEmpty("không có timeline thì không được ghi shot nào");
    }

    [Fact]
    public async Task Shot_bi_kiem_duyet_tu_choi_thi_fail_ngay_khong_goi_lai_provider()
    {
        await using PipelineTestHost host = await PipelineTestHost.StartAsync(fake =>
        {
            fake.FailShotIndexes.Add(0);
            fake.FailureKind = VideoFailureKind.ContentRejected;
        });

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript));

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.Status.Should().Be(JobStatus.Failed);
        job.CurrentStep.Should().Be(6);

        List<ProviderCall> videoCalls = (await host.ReadCallsAsync(jobId))
            .Where(c => c.Category == ProviderCategory.Video)
            .ToList();

        videoCalls.Should().ContainSingle(
            "nội dung bị từ chối thì gọi lại bao nhiêu lần cũng ra cùng một câu trả lời, chỉ khác ở hoá đơn");
        videoCalls[0].FailureKind.Should().Be(VideoFailureKind.ContentRejected);

        (await host.ReadShotsAsync(jobId)).Should().OnlyContain(s => s.Status == ShotStatus.Failed);
    }

    [Fact]
    public async Task Loi_tam_thoi_o_shot_dau_thi_thu_lai_roi_chay_tiep_den_cung()
    {
        // Phân biệt với test trên: cùng một shot fail, nhưng lỗi tạm thời thì phải thử lại. Gộp
        // hai loại lỗi làm một nghĩa là hoặc bỏ phí những job chỉ vấp một lần, hoặc trả tiền ba
        // lần cho một lời từ chối.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync(fake =>
        {
            fake.FailShotIndexes.Add(0);
            fake.FailureKind = VideoFailureKind.Transient;
            fake.FailTimesBeforeSuccess = 1;
        });

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(TestBriefs.ShortScript));

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.FailureReason.Should().BeNull();
        job.Status.Should().Be(JobStatus.Completed);

        List<ProviderCall> videoCalls = (await host.ReadCallsAsync(jobId))
            .Where(c => c.Category == ProviderCategory.Video)
            .ToList();

        videoCalls.Should().HaveCount(2, "lần một hỏng tạm thời, lần hai thành công");
        videoCalls[0].IsSuccess.Should().BeFalse();
        videoCalls[0].AttemptNumber.Should().Be(1);
        videoCalls[1].IsSuccess.Should().BeTrue();
        videoCalls[1].AttemptNumber.Should().Be(2);

        (await host.ReadShotsAsync(jobId)).Should().OnlyContain(s => s.Status == ShotStatus.Rendered);
    }

    [Fact]
    public async Task Job_da_xong_ma_bi_day_lai_vao_hang_doi_thi_khong_chay_lai()
    {
        // Hangfire giao việc ÍT NHẤT một lần, nên cùng một job id quay lại là chuyện bình thường
        // chứ không phải sự cố. Chạy lại một job đã giao khách nghĩa là gọi provider thêm một lượt
        // và ghi đè file thành phẩm mà khách có thể đang tải.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid(), job =>
        {
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        });

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.Status.Should().Be(JobStatus.Completed);
        job.StartedAt.Should().BeNull("job này chưa từng được bộ chạy đụng tới");

        (await host.ReadCallsAsync(jobId)).Should().BeEmpty();
        host.Images.Requests.Should().BeEmpty();

        host.Log.Should().Contain(line => line.Contains("trạng thái cuối", StringComparison.Ordinal),
            "bỏ qua trong im lặng thì không ai biết hàng đợi đang giao trùng");
    }

    [Fact]
    public async Task Khach_huy_trong_luc_job_dang_chay_thi_dung_o_ranh_gioi_buoc_ke_tiep()
    {
        // Lệnh huỷ đến từ tiến trình khác (API) nên nó chỉ tồn tại dưới dạng một dòng trong DB:
        // bản job mà bộ chạy đang giữ trong bộ nhớ không tự biết. Đặt sẵn trạng thái Cancelled từ
        // đầu thì lại đi vào nhánh "trạng thái cuối" chứ không phải nhánh này — nên phải huỷ đúng
        // lúc bước 1 đang chờ mạng.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        Guid jobId = await host.CreateJobAsync(TestBriefs.Valid());

        host.Images.OnRequestAsync = _ => host.UpdateJobAsync(jobId, j => j.Status = JobStatus.Cancelled);

        await host.RunAsync(jobId);

        AdVideoJob job = await host.ReadJobAsync(jobId);

        job.Status.Should().Be(JobStatus.Cancelled);
        job.FailureReason.Should().BeNull("huỷ theo yêu cầu không phải là thất bại");
        job.CurrentStep.Should().Be(1, "bước 1 đã chạy xong; việc dừng xảy ra trước bước 4");

        (await host.ReadCallsAsync(jobId)).Should().BeEmpty(
            "huỷ mà vẫn gọi provider thì khách bị tính tiền cho thứ họ vừa từ chối");

        host.Log.Should().Contain(line => line.Contains("bị huỷ giữa chừng", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pipeline_dang_ky_du_sau_buoc_va_khong_trung_so_thu_tu()
    {
        // Thứ tự chạy lấy từ IPipelineStep.Order chứ không từ thứ tự đăng ký DI. Hai bước trùng
        // số thì thứ tự giữa chúng do sắp xếp quyết định — im lặng, không lỗi, và bước 5 có thể
        // chạy sau bước 6.
        await using PipelineTestHost host = await PipelineTestHost.StartAsync();

        List<int> orders = await host.InScopeAsync(sp => Task.FromResult(
            sp.GetServices<IPipelineStep>().Select(s => s.Order).ToList()));

        orders.Should().HaveCount(6);
        orders.Should().OnlyHaveUniqueItems();
        orders.OrderBy(o => o).Should().Equal(1, 4, 5, 6, 8, 9);
    }
}
