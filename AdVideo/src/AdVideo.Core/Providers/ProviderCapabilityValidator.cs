using AdVideo.Core.Enums;

namespace AdVideo.Core.Providers;

/// <summary>Kết quả kiểm tra một provider có đáp ứng yêu cầu không.</summary>
/// <param name="IsSatisfied">Có dùng được không. <c>false</c> thì API trả 422.</param>
/// <param name="BlockingReasons">Lý do chặn, viết cho NGƯỜI DÙNG đọc, không phải cho dev.</param>
/// <param name="Warnings">Không chặn nhưng nên log — ví dụ nhiều chủ thể hơn mức đã đo.</param>
/// <param name="Suggestions">Provider nào làm được việc này. Luôn điền khi <see cref="IsSatisfied"/> = false.</param>
/// <param name="RecommendedShotDurationSeconds">Bậc thời lượng nên dùng, để bước 5 không phải tính lại.</param>
public sealed record CapabilityCheckResult(
    bool IsSatisfied,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ProviderSuggestion> Suggestions,
    int? RecommendedShotDurationSeconds)
{
    public static CapabilityCheckResult Ok(int? recommendedDuration, IReadOnlyList<string>? warnings = null) =>
        new(true, [], warnings ?? [], [], recommendedDuration);

    public static CapabilityCheckResult Reject(
        IReadOnlyList<string> reasons,
        IReadOnlyList<ProviderSuggestion> suggestions,
        IReadOnlyList<string>? warnings = null) =>
        new(false, reasons, warnings ?? [], suggestions, null);
}

/// <summary>
/// So yêu cầu nghiệp vụ với capability của provider. Thuần hàm — không I/O, không trạng thái.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lớp này thực thi Luật 2: "API phải chuẩn hoá, không chuyển tiếp."</b> Chạy TRƯỚC khi gọi
/// provider, để một yêu cầu bất khả thi bị từ chối bằng câu tiếng Việt đọc được thay vì bằng
/// một khối JSON lỗi của nhà cung cấp mà khách không hiểu.
/// </para>
/// <para>
/// Chạy trước cũng có nghĩa là <b>không tốn tiền</b>: một request sai bị chặn ở đây thì không
/// có lời gọi TTS hay video nào phát sinh. Đó là khác biệt giữa 422 và một hoá đơn bất ngờ.
/// </para>
/// </remarks>
public static class ProviderCapabilityValidator
{
    public static CapabilityCheckResult Check(
        VideoRequirements requirements,
        VideoProviderCapability candidate,
        IReadOnlyList<VideoProviderCapability>? alternatives = null)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(candidate);

        alternatives ??= [];
        var blocking = new List<string>();
        var warnings = new List<string>();

        // Lưới thời lượng rỗng là lỗi cấu hình, không phải lỗi của người dùng.
        // Nói rõ để người vận hành sửa DB thay vì đi tìm trong code.
        if (candidate.AllowedDurationSeconds.Count == 0)
        {
            return CapabilityCheckResult.Reject(
                [$"Provider '{candidate.Provider}' chưa được cấu hình lưới thời lượng trong DB. Đây là lỗi vận hành, không phải lỗi yêu cầu."],
                []);
        }

        if (!candidate.SupportedAspectRatios.Contains(requirements.AspectRatio))
        {
            blocking.Add(
                $"{candidate.Provider} ({candidate.ModelId}) không hỗ trợ khung hình " +
                $"{requirements.AspectRatio.ToProviderString()}. Hỗ trợ: " +
                $"{string.Join(", ", candidate.SupportedAspectRatios.Select(r => r.ToProviderString()))}.");
        }

        if (!candidate.ServesTiers.Contains(requirements.Tier))
        {
            blocking.Add(
                $"Chất lượng {TierLabel(requirements.Tier)} không dùng được với {candidate.Provider}. " +
                $"Provider này phục vụ: {string.Join(", ", candidate.ServesTiers.Select(TierLabel))}.");
        }

        // Ca hay bị nhất: khách muốn video có người nhưng provider tự chặn ảnh có mặt người.
        // Veo được ghi nhận là chặn thẳng. Mức độ từ chối là dữ liệu Sprint 0 cần thu.
        if (requirements.HasPerson && !candidate.AcceptsHumanFaces)
        {
            blocking.Add(
                $"{candidate.Provider} không nhận ảnh có mặt người, nên không làm được video có người xuất hiện.");
        }

        if (requirements.NeedsFrameChaining && !candidate.SupportsFrameChaining)
        {
            blocking.Add(
                $"Định dạng này cần nối khung hình giữa các đoạn liên tiếp, nhưng {candidate.Provider} không hỗ trợ. " +
                "Đây là yêu cầu của định dạng daily — xem Sprint 3.");
        }

        // Video ngắn hơn bậc thời lượng nhỏ nhất thì không thể làm: không provider nào cắt bớt
        // một lần gọi 4 giây xuống 1,5 giây mà vẫn giữ chất lượng.
        var shortest = candidate.AllowedDurationSeconds[0];
        if (requirements.TargetDurationSeconds < shortest)
        {
            blocking.Add(
                $"Video {requirements.TargetDurationSeconds} giây ngắn hơn bậc ngắn nhất mà {candidate.Provider} " +
                $"hỗ trợ ({shortest} giây). Chọn thời lượng từ {shortest} giây trở lên.");
        }

        // MaxSubjectsReliably = 0 nghĩa là CHƯA ĐO (Sprint 0 T0.5), không phải "không giữ được chủ thể nào".
        // Chỉ cảnh báo khi đã có số đo thật — chặn dựa trên con số chưa đo là chặn bừa.
        if (requirements.SubjectCount > 1 &&
            candidate.MaxSubjectsReliably > 0 &&
            requirements.SubjectCount > candidate.MaxSubjectsReliably)
        {
            warnings.Add(
                $"Yêu cầu {requirements.SubjectCount} chủ thể trong một khung, nhưng {candidate.Provider} " +
                $"chỉ giữ reliably được {candidate.MaxSubjectsReliably}. Chủ thể có thể bị rơi hoặc biến dạng.");
        }

        var recommended = SmallestDurationAtLeast(
            Math.Min(requirements.TargetDurationSeconds, candidate.MaxDurationSeconds),
            candidate.AllowedDurationSeconds);

        if (blocking.Count > 0)
        {
            return CapabilityCheckResult.Reject(
                blocking,
                BuildSuggestions(requirements, candidate, alternatives),
                warnings);
        }

        return CapabilityCheckResult.Ok(recommended, warnings);
    }

    /// <summary>
    /// Bậc thời lượng nhỏ nhất trong lưới mà ≥ số giây yêu cầu. Null nếu lưới không với tới.
    /// </summary>
    /// <remarks>
    /// Luôn LÀM TRÒN LÊN, không bao giờ xuống: shot 5 giây chứa audio 4,8 giây thì thừa 0,2 giây
    /// để bù bằng hold-frame; shot 4 giây chứa audio 4,8 giây thì phải cắt mất lời thoại.
    /// Phần dư bù bằng hold-frame hoặc Ken Burns — <b>không kéo giãn video</b>, vì kéo giãn
    /// làm chuyển động trông say sóng.
    /// </remarks>
    public static int? SmallestDurationAtLeast(int seconds, IReadOnlyList<int> grid)
    {
        if (grid.Count == 0) return null;
        var ordered = grid.OrderBy(x => x).ToList();
        foreach (var step in ordered)
        {
            if (step >= seconds) return step;
        }
        return null; // dài hơn bậc lớn nhất → phải chia shot, không phải lỗi của hàm này
    }

    /// <summary>
    /// Trong các provider khác, tìm những cái đáp ứng yêu cầu — để 422 kèm gợi ý hành động được.
    /// </summary>
    private static IReadOnlyList<ProviderSuggestion> BuildSuggestions(
        VideoRequirements requirements,
        VideoProviderCapability rejected,
        IReadOnlyList<VideoProviderCapability> alternatives)
    {
        var suggestions = new List<ProviderSuggestion>();

        foreach (var alt in alternatives)
        {
            if (string.Equals(alt.Provider, rejected.Provider, StringComparison.OrdinalIgnoreCase)) continue;
            if (alt.AllowedDurationSeconds.Count == 0) continue;

            // Tái dùng chính hàm Check để khỏi lặp luật ở hai nơi — nếu luật đổi, cả hai đổi theo.
            var probe = Check(requirements, alt);
            if (!probe.IsSatisfied) continue;

            var tier = requirements.Tier;
            var cost = probe.RecommendedShotDurationSeconds is { } dur
                ? Math.Ceiling((decimal)requirements.TargetDurationSeconds / dur) * dur * alt.CostPerSecondUsd
                : (decimal?)null;

            suggestions.Add(new ProviderSuggestion(
                alt.Provider,
                tier,
                DescribeWhyItWorks(requirements, alt),
                cost));
        }

        // Rẻ nhất trước: khách thường quan tâm giá hơn tên provider — và Luật 3 nói rằng
        // khách không nên phải biết provider là gì, nên xếp theo giá là thứ tự ít gây nhiễu nhất.
        return suggestions
            .OrderBy(s => s.EstimatedCostUsd ?? decimal.MaxValue)
            .ThenBy(s => s.ProviderName, StringComparer.Ordinal)
            .ToList();
    }

    private static string DescribeWhyItWorks(VideoRequirements requirements, VideoProviderCapability alt)
    {
        if (requirements.HasPerson && alt.AcceptsHumanFaces)
            return $"{alt.Provider} nhận ảnh có mặt người, ở chất lượng {TierLabel(requirements.Tier)}.";
        if (requirements.NeedsFrameChaining && alt.SupportsFrameChaining)
            return $"{alt.Provider} hỗ trợ nối khung hình, cần cho định dạng daily.";
        if (requirements.SubjectCount > 1 && alt.MaxSubjectsReliably >= requirements.SubjectCount)
            return $"{alt.Provider} giữ được {alt.MaxSubjectsReliably} chủ thể trong một khung.";
        return $"{alt.Provider} đáp ứng yêu cầu ở chất lượng {TierLabel(requirements.Tier)}.";
    }

    /// <summary>Nhãn tier bằng tiếng Việt, dùng trong thông báo trả ra API.</summary>
    public static string TierLabel(VideoTier tier) => tier switch
    {
        VideoTier.Draft => "Nháp",
        VideoTier.Standard => "Chuẩn",
        VideoTier.Premium => "Cao cấp",
        _ => tier.ToString(),
    };
}
