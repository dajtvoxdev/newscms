using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Media;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Qc;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Media;
using AdVideo.Worker.Jobs;

namespace AdVideo.Worker.Steps;

/// <summary>
/// Bước 9 — chấm chất lượng video thành phẩm trước khi giao.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chấm trên file ĐÃ NẰM TRONG KHO, không phải bản tạm trên đĩa worker.</b> Tải lại một lần
/// nữa là cách duy nhất để biết thứ khách sắp tải về đúng là thứ mình vừa dựng — một lần upload
/// đứt giữa chừng cho ra đúng một file thiếu byte mà mọi phép kiểm trên bản tạm đều đạt.
/// </para>
/// <para>
/// <b>Nhãn AI trượt là fail, không phải cảnh báo.</b> Luật TTNT 2025 đặt nghĩa vụ lên bên triển
/// khai với mức phạt tới 2 tỉ đồng, và nghĩa vụ đó không chuyển sang khách bằng điều khoản được.
/// Giao một video thiếu nhãn là giao một rủi ro pháp lý chứ không phải một sản phẩm chưa hoàn
/// thiện.
/// </para>
/// <para>
/// <b>Phép kiểm không chặn vẫn được ghi log đầy đủ.</b> Độ phân giải lệch hay LUFS trôi không
/// đáng để chặn một video dùng được, nhưng nếu chúng bắt đầu trượt hàng loạt thì đó là tín hiệu
/// sớm của một thứ khác đang hỏng.
/// </para>
/// </remarks>
public sealed class QcStep : IPipelineStep
{
    private readonly IMediaInspector _inspector;
    private readonly ISettingsStore _settings;
    private readonly JobArtifacts _artifacts;
    private readonly ILogger<QcStep> _logger;

    public QcStep(
        IMediaInspector inspector,
        ISettingsStore settings,
        JobArtifacts artifacts,
        ILogger<QcStep> logger)
    {
        _inspector = inspector;
        _settings = settings;
        _artifacts = artifacts;
        _logger = logger;
    }

    public int Order => 9;

    public JobStatus RunningStatus => JobStatus.QualityChecking;

    public string DisplayName => "Đang kiểm tra chất lượng";

    public bool ShouldRun(PipelineContext context) => true;

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.FinalVideoKey is not { } finalKey)
        {
            return StepResult.Fail("Chưa có video thành phẩm — bước 8 phải chạy trước bước kiểm tra.");
        }

        AiLabelSpec? label = context.AiLabel();

        if (label is null)
        {
            // Không tự dựng lại đặc tả: một đặc tả dựng ở đây sẽ luôn hợp lệ và phép kiểm nhãn
            // thành ra tự chấm chính nó.
            return StepResult.Fail("Không có đặc tả nhãn AI từ bước 8 nên không kiểm chứng được nhãn.");
        }

        string localPath = Path.Combine(Path.GetTempPath(), $"advideo-qc-{context.JobId:N}.mp4");

        try
        {
            await _artifacts.DownloadToFileAsync(Buckets.Final, finalKey, localPath, cancellationToken);

            MediaProbeResult probe = await _inspector.ProbeAsync(
                localPath,

                // Đo LUFS phải giải mã toàn bộ audio nên tốn thời gian — chấp nhận được vì mỗi
                // job chỉ đo đúng một lần, trên đúng một file.
                measureLoudness: true,

                // Dò khung đen là thêm một lần giải mã toàn bộ HÌNH, đắt hơn hẳn. Sprint 1 chưa
                // có crossfade nên nguồn sinh khung đen chưa tồn tại; bật khi có.
                detectBlackFrames: false,
                cancellationToken);

            QcThresholds thresholds = await LoadThresholdsAsync(cancellationToken);
            (int width, int height) = context.AspectRatio.ToResolution();

            QcReport report = QualityChecker.Evaluate(
                probe, thresholds, label, width, height, context.FinalDurationSeconds);

            LogReport(context, report);

            if (!report.IsPassed)
            {
                return StepResult.Fail(DescribeFailure(report));
            }

            return StepResult.Ok();
        }
        catch (FileNotFoundException ex)
        {
            return StepResult.Fail($"Không tải được video thành phẩm để kiểm tra: {ex.Message}", ex.ToString());
        }
        finally
        {
            DeleteQuietly(localPath);
        }
    }

    private async Task<QcThresholds> LoadThresholdsAsync(CancellationToken cancellationToken)
    {
        decimal targetLufs = await _settings.GetDecimalAsync(SettingKeys.TargetLoudnessLufs, -14m, cancellationToken);
        int driftMs = await _settings.GetIntAsync(SettingKeys.MaxLipSyncDriftMs, 200, cancellationToken);

        return new QcThresholds
        {
            TargetLoudnessLufs = (double)targetLufs,
            MaxLipSyncDriftSeconds = driftMs / 1000.0,
        };
    }

    /// <summary>Lý do fail viết cho người đọc: nói rõ phép kiểm nào trượt và đo được bao nhiêu.</summary>
    private static string DescribeFailure(QcReport report)
    {
        IEnumerable<string> lines = report.FailedBlocking.Select(c =>
            $"{c.Check}: cần {c.Expected}, đo được {c.Actual}." +
            (c.Message is null ? string.Empty : $" {c.Message}"));

        return $"Video không đạt kiểm tra chất lượng. {string.Join(" ", lines)}";
    }

    private void LogReport(PipelineContext context, QcReport report)
    {
        _logger.LogInformation("job_id={JobId} {Summary}", context.JobId, report.Summary);

        foreach (QcCheck check in report.Failed.Where(c => !c.IsBlocking))
        {
            _logger.LogWarning(
                "job_id={JobId} QC {Check} không đạt (không chặn): cần {Expected}, đo được {Actual}.",
                context.JobId,
                check.Check,
                check.Expected,
                check.Actual);
        }
    }

    private void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Không xoá được file tạm {Path}.", path);
        }
    }
}
