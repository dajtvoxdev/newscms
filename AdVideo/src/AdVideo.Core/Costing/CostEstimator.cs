using AdVideo.Core.Providers;

namespace AdVideo.Core.Costing;

/// <summary>Dự toán chi phí một job, tách theo từng khoản để log ra đọc được.</summary>
/// <param name="ShotCount">Số shot phải render.</param>
/// <param name="BilledVideoSeconds">Tổng số giây bị tính tiền — luôn ≥ thời lượng khách yêu cầu.</param>
/// <param name="VideoCostUsd">Tiền video, chưa tính render lại.</param>
/// <param name="TtsCostUsd">Tiền giọng đọc.</param>
/// <param name="EstimatedCostUsd">Tổng dự kiến khi mọi thứ trót lọt ngay lần đầu.</param>
/// <param name="WorstCaseCostUsd">Tổng khi mọi shot đều phải render lại tối đa số lần cho phép.</param>
public sealed record CostEstimate(
    int ShotCount,
    int BilledVideoSeconds,
    decimal VideoCostUsd,
    decimal TtsCostUsd,
    decimal EstimatedCostUsd,
    decimal WorstCaseCostUsd);

/// <summary>
/// Dự toán chi phí <b>trước</b> khi gọi provider (D8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao phải dự toán trước chứ không cộng sau.</b> Cộng sau thì con số chỉ đúng vào lúc
/// tiền đã tiêu xong. Một brief 180 giây trên provider chỉ render được clip 8 giây là 23 lần gọi;
/// nếu mỗi lần gọi đều thất bại và được retry, số tiền nhân lên trước khi có ai kịp nhìn.
/// </para>
/// <para>
/// <b>Vì sao gác cổng bằng <see cref="CostEstimate.WorstCaseCostUsd"/> chứ không phải dự toán
/// thường.</b> Trần chi tiêu tồn tại để trả lời câu "tối đa mất bao nhiêu", và câu trả lời đó
/// phải tính cả retry. Gác bằng dự toán thường thì một job sát trần vẫn vượt trần ngay khi có
/// một shot phải render lại — nghĩa là cái trần không chặn đúng thứ nó sinh ra để chặn.
/// </para>
/// <para>
/// Thuần hàm, không I/O: mọi nhánh test được mà không cần mock.
/// </para>
/// </remarks>
public static class CostEstimator
{
    /// <summary>
    /// Tốc độ đọc tiếng Việt, ký tự mỗi giây, dùng khi chưa có lời thoại thật.
    /// </summary>
    /// <remarks>
    /// Khoảng 150 từ/phút với từ tiếng Việt trung bình 5 ký tự kể cả dấu cách. Đây là số
    /// <b>phỏng đoán</b> — Sprint 0 (đo bằng key thật) bị hoãn nên chưa có số đo. Nó chỉ ảnh
    /// hưởng tới dự toán tiền TTS, vốn nhỏ hơn tiền video vài bậc, nên sai vài chục phần trăm
    /// không làm hỏng quyết định gác cổng.
    /// </remarks>
    public const double VietnameseCharsPerSecond = 12.5;

    public static CostEstimate Estimate(
        int targetDurationSeconds,
        VideoProviderCapability videoCapability,
        TtsProviderCapability? ttsCapability,
        int maxRetriesPerShot,
        string? script = null)
    {
        ArgumentNullException.ThrowIfNull(videoCapability);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetDurationSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetriesPerShot);

        IReadOnlyList<int> shotDurations = PlanShotDurations(targetDurationSeconds, videoCapability);

        int billedSeconds = shotDurations.Sum();
        decimal videoCost = billedSeconds * videoCapability.CostPerSecondUsd;

        int scriptChars = script?.Length
            ?? (int)Math.Ceiling(targetDurationSeconds * VietnameseCharsPerSecond);

        decimal ttsCost = ttsCapability is null
            ? 0m
            : scriptChars * ttsCapability.CostPer1000CharsUsd / 1000m;

        // Retry chỉ nhân tiền VIDEO. Lời thoại đã sinh một lần là dùng cho mọi lần render lại —
        // và đó cũng là lý do thứ tự 4 → 5 → 6 không đảo được.
        decimal worstCase = videoCost * (1 + maxRetriesPerShot) + ttsCost;

        return new CostEstimate(
            shotDurations.Count,
            billedSeconds,
            Round(videoCost),
            Round(ttsCost),
            Round(videoCost + ttsCost),
            Round(worstCase));
    }

    /// <summary>
    /// Chia thời lượng mong muốn thành các shot theo đúng lưới thời lượng provider cho phép.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Đây là bản <b>dự toán</b>, không phải bản khoá timeline: nó chia theo thời lượng khách
    /// yêu cầu, còn <c>TimelineLocker</c> (bước 5) chia lại theo độ dài audio <b>thật</b>. Hai
    /// chỗ cùng chia nhưng chia theo hai nguồn sự thật khác nhau, và cố gộp làm một sẽ buộc
    /// bước dự toán phải chờ TTS — nghĩa là tiêu tiền TTS trước khi kiểm tra trần chi tiêu.
    /// </para>
    /// <para>
    /// <b>Luôn làm tròn LÊN bậc gần nhất.</b> Provider tính tiền theo bậc, không theo giây thật:
    /// xin 5 giây trên lưới 4/6/8 là trả tiền 6 giây. Dự toán làm tròn xuống sẽ luôn thấp hơn
    /// hoá đơn, và một cái trần luôn thấp hơn hoá đơn thì không phải là trần.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<int> PlanShotDurations(
        int targetDurationSeconds, VideoProviderCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        List<int> grid = capability.AllowedDurationSeconds.Where(d => d > 0).Distinct().Order().ToList();

        if (grid.Count == 0)
        {
            throw new ArgumentException(
                $"Provider {capability.Provider}/{capability.ModelId} không khai báo lưới thời lượng nào. " +
                "Không có lưới thì không tính được tiền, và không tính được tiền thì không được phép gọi.",
                nameof(capability));
        }

        int longest = grid[^1];
        var shots = new List<int>();
        int remaining = targetDurationSeconds;

        while (remaining > longest)
        {
            shots.Add(longest);
            remaining -= longest;
        }

        // Phần dư: bậc nhỏ nhất đủ chứa nó.
        shots.Add(grid.First(d => d >= remaining));

        return shots;
    }

    /// <summary>Làm tròn tiền về 4 chữ số thập phân — đủ cho đơn giá cỡ phần nghìn đô mỗi giây.</summary>
    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
