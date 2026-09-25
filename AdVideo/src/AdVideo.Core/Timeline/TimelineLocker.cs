using AdVideo.Core.Providers;

namespace AdVideo.Core.Timeline;

/// <summary>
/// Bước 5 — khoá timeline: gán thời lượng video cho từng đoạn thoại theo lưới của provider.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao bước này tồn tại và vì sao thứ tự 4 → 5 → 6 không đảo được.</b> Provider không nhận
/// thời lượng tuỳ ý: Veo có bậc 4/6/8, Kling chỉ có 5/10. Nhưng câu thoại dài bao nhiêu là do
/// TTS quyết định, và chỉ biết được SAU khi đã sinh audio. Nên phải: có audio thật (4) →
/// suy ra thời lượng shot (5) → rồi mới render hình (6). Làm ngược lại thì hình dài 8 giây
/// mà tiếng dài 9,3 giây, và không có cách sửa nào không xấu.
/// </para>
/// <para>
/// <b>Luôn làm tròn LÊN.</b> Shot 6 giây chứa thoại 4,8 giây thì thừa 1,2 giây để bù bằng
/// hold-frame hoặc Ken Burns. Shot 4 giây chứa thoại 4,8 giây thì phải cắt mất lời — không
/// chấp nhận được. Phần dư KHÔNG được bù bằng cách kéo giãn video: kéo giãn làm chuyển động
/// trông say sóng.
/// </para>
/// <para>
/// Thuần hàm, không I/O, không trạng thái — nên mọi nhánh đều test được. Đây là class phải
/// đạt 100% line+branch.
/// </para>
/// </remarks>
public static class TimelineLocker
{
    /// <summary>
    /// Dung sai khi làm tròn lên. Độ dài audio từ TTS có nhiễu làm tròn ở chữ số thập phân cuối;
    /// không có dung sai thì thoại dài đúng 4,0000001 giây sẽ bị đẩy lên bậc 6 thay vì 4.
    /// </summary>
    public const double DurationToleranceSeconds = 0.05;

    /// <summary>Phần dư ngắn hơn mức này thì coi như khớp, không cần bù.</summary>
    public const double NoPaddingThresholdSeconds = 0.05;

    /// <summary>Phần dư dài hơn mức này thì dùng Ken Burns thay vì giữ khung đứng yên.</summary>
    /// <remarks>Khung đứng yên quá 0,75 giây trông như video bị đơ — người xem sẽ tưởng lỗi.</remarks>
    public const double KenBurnsThresholdSeconds = 0.75;

    public static TimelinePlan Lock(TimelineLockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DurationGrid.Count == 0)
        {
            return TimelinePlan.Reject(["Lưới thời lượng rỗng — provider chưa được cấu hình bậc thời lượng trong DB."]);
        }

        if (request.NarrationDurationSeconds <= 0)
        {
            return TimelinePlan.Reject(
                [$"Độ dài thoại là {request.NarrationDurationSeconds} giây. Không khoá được timeline khi chưa có audio thật — thứ tự 4 → 5 → 6 không đảo được."]);
        }

        if (request.MaxShots <= 0)
        {
            return TimelinePlan.Reject([$"MaxShots = {request.MaxShots} là vô nghĩa."]);
        }

        var segments = request.PresetSegments;
        var warnings = new List<string>();
        bool midSentenceCut = false;

        if (segments is null)
        {
            (segments, midSentenceCut) = SplitAtSilence(request, warnings);
        }

        if (segments.Count == 0)
        {
            return TimelinePlan.Reject(["Không chia được đoạn thoại nào."], warnings);
        }

        if (segments.Count > request.MaxShots)
        {
            return TimelinePlan.Reject(
                [
                    $"Thoại dài {request.NarrationDurationSeconds:0.##} giây cần {segments.Count} shot, " +
                    $"vượt trần {request.MaxShots}. Bậc dài nhất của provider là {request.DurationGrid.Max()} giây. " +
                    "Rút ngắn lời thoại hoặc dùng provider có bậc thời lượng dài hơn.",
                ],
                warnings);
        }

        return Build(request, segments, warnings, midSentenceCut);
    }

    /// <summary>
    /// Chia thoại thành các đoạn, mỗi đoạn ngắn hơn bậc dài nhất của lưới, ưu tiên cắt ở chỗ im lặng.
    /// </summary>
    /// <remarks>
    /// Cắt ở im lặng không phải để đẹp — nó là điều kiện để <see cref="AudioLayoutStrategy.PadPerShot"/>
    /// không tạo ra khoảng lặng giữa câu. Phần video thừa luôn nằm ở CUỐI shot; nếu cuối shot là
    /// một chỗ ngừng tự nhiên thì người nghe không nhận ra, còn nếu cuối shot là giữa một từ
    /// thì khoảng lặng đó nghe như vấp.
    /// </remarks>
    private static (List<NarrationSegment> Segments, bool MidSentenceCut) SplitAtSilence(
        TimelineLockRequest request, List<string> warnings)
    {
        var grid = NormalizeGrid(request.DurationGrid);
        var maxStep = grid[^1];
        var total = request.NarrationDurationSeconds;
        var segments = new List<NarrationSegment>();
        bool midSentenceCut = false;

        // Thoại ngắn hơn một bậc → một shot duy nhất. Sprint 1 luôn rơi vào nhánh này.
        if (total <= maxStep + DurationToleranceSeconds)
        {
            segments.Add(new NarrationSegment(string.Empty, 0, total));
            return (segments, false);
        }

        var words = request.WordTimings
            .Where(w => w.EndSeconds > w.StartSeconds)
            .OrderBy(w => w.StartSeconds)
            .ToList();

        if (words.Count == 0)
        {
            // Không có mốc thời gian thì không biết chỗ nào im lặng. Chia đều theo thời gian
            // là phương án cuối — chất lượng sẽ tệ và phải nói rõ, không im lặng mà làm.
            warnings.Add(
                "Không có mốc thời gian theo từ nên không cắt ở chỗ im lặng được; chia đều theo thời gian. " +
                "Thoại sẽ bị cắt giữa câu. Đây là dấu hiệu TTS provider không trả timestamps.");
            return (SplitEvenly(total, maxStep), true);
        }

        int start = 0;
        while (start < words.Count)
        {
            // Tìm từ xa nhất sao cho đoạn [start..end] vẫn lọt vào một bậc lưới.
            int end = start;
            while (end + 1 < words.Count &&
                   words[end + 1].EndSeconds - words[start].StartSeconds <= maxStep + DurationToleranceSeconds)
            {
                end++;
            }

            bool isLast = end == words.Count - 1;
            double segStart = words[start].StartSeconds;
            double segEnd = words[end].EndSeconds;

            if (!isLast)
            {
                // Lùi về chỗ im lặng gần nhất trước điểm cắt. Nếu không có thì cắt ngay tại
                // ranh giới từ và ghi nhận — thà biết mình cắt giữa câu còn hơn tưởng là không.
                int cutAt = FindSilenceCut(words, start, end, request.SilenceThresholdSeconds);
                if (cutAt < 0)
                {
                    midSentenceCut = true;
                    warnings.Add(
                        $"Không tìm thấy khoảng lặng ≥ {request.SilenceThresholdSeconds:0.##}s trước từ thứ {end + 1}; " +
                        "shot bị cắt ngay ranh giới từ. Đoạn này sẽ nghe hụt.");
                    cutAt = end;
                }

                end = cutAt;
                segEnd = words[end].EndSeconds;

                // Ở đây TỪNG có một nhánh "phần còn lại ngắn hơn bậc nhỏ nhất thì gộp luôn vào
                // shot này". Nhánh đó không bao giờ chạy được, nên đã bỏ: vòng lặp phía trên chỉ
                // dừng sớm khi có một từ kết thúc sau segStart + maxStep, tức tổng thoại còn lại
                // dài hơn maxStep; mà muốn gộp thì tổng đó phải NGẮN hơn maxStep. Hai điều kiện
                // loại trừ nhau, nên giữ lại chỉ là giữ một đoạn code không test được.
                //
                // Hệ quả thật (chấp nhận được): đuôi thoại ngắn vẫn thành một shot riêng, và
                // phần dư của nó được bù bằng Ken Burns như mọi phần dư khác.
            }

            segments.Add(new NarrationSegment(JoinWords(words, start, end), segStart, segEnd));

            if (isLast) break;
            start = end + 1;
        }

        return (segments, midSentenceCut);
    }

    /// <summary>
    /// Tìm chỉ số từ cuối cùng của đoạn sao cho giữa nó và từ kế tiếp có khoảng lặng đủ dài.
    /// Trả về −1 nếu không có chỗ nào.
    /// </summary>
    private static int FindSilenceCut(List<WordTiming> words, int start, int end, double threshold)
    {
        for (int i = end; i > start; i--)
        {
            var gap = words[i].StartSeconds - words[i - 1].EndSeconds;
            if (gap >= threshold) return i - 1;
        }
        return -1;
    }

    private static List<NarrationSegment> SplitEvenly(double total, double maxStep)
    {
        var count = (int)Math.Ceiling(total / maxStep);
        var size = total / count;
        var result = new List<NarrationSegment>(count);
        for (int i = 0; i < count; i++)
        {
            var from = Math.Round(i * size, 3);
            var to = i == count - 1 ? total : Math.Round((i + 1) * size, 3);
            result.Add(new NarrationSegment(string.Empty, from, to));
        }
        return result;
    }

    /// <remarks>
    /// Không kiểm tra <c>start &gt; end</c>: chỗ cắt luôn nằm trong đoạn (<see cref="FindSilenceCut"/>
    /// trả về chỉ số ≥ start), và nếu có sai thì <c>Take</c> với số âm cũng trả chuỗi rỗng chứ
    /// không ném. Một lớp phòng thủ không chạy được là một lớp phòng thủ không kiểm chứng được.
    /// </remarks>
    private static string JoinWords(List<WordTiming> words, int start, int end) =>
        string.Join(' ', words.Skip(start).Take(end - start + 1).Select(w => w.Word));

    private static TimelinePlan Build(
        TimelineLockRequest request,
        IReadOnlyList<NarrationSegment> segments,
        List<string> warnings,
        bool midSentenceCut)
    {
        var grid = NormalizeGrid(request.DurationGrid);
        var shots = new List<ShotPlan>(segments.Count);
        double videoCursor = 0;
        double maxOffset = 0;

        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var span = seg.EndSeconds - seg.StartSeconds;

            if (span <= 0)
            {
                return TimelinePlan.Reject(
                    [$"Đoạn thoại thứ {i} có độ dài {span:0.###} giây — mốc thời gian từ TTS bị chồng lấn hoặc sai thứ tự."],
                    warnings);
            }

            var videoDuration = ProviderCapabilityValidator.SmallestDurationAtLeast(
                (int)Math.Ceiling(span - DurationToleranceSeconds), grid);

            if (videoDuration is null)
            {
                return TimelinePlan.Reject(
                    [
                        $"Đoạn thoại thứ {i} dài {span:0.##} giây, vượt bậc dài nhất của provider ({grid[^1]} giây). " +
                        "Phải chia nhỏ đoạn này — kiểm tra lại lớp đạo diễn.",
                    ],
                    warnings);
            }

            var padding = videoDuration.Value - span;

            // PadPerShot: thoại bắt đầu cùng lúc với shot → offset bằng 0 theo cấu tạo.
            // ContinuousAudio: thoại giữ vị trí tự nhiên → offset là khoảng cách giữa hai mốc.
            var audioStartInTimeline = request.Layout == AudioLayoutStrategy.PadPerShot
                ? videoCursor
                : seg.StartSeconds;
            var offset = Math.Abs(videoCursor - seg.StartSeconds);
            if (request.Layout == AudioLayoutStrategy.PadPerShot) offset = 0;
            maxOffset = Math.Max(maxOffset, offset);

            shots.Add(new ShotPlan
            {
                Index = i,
                NarrationStartSeconds = seg.StartSeconds,
                NarrationEndSeconds = seg.EndSeconds,
                VideoDurationSeconds = videoDuration.Value,
                SpokenText = seg.Text,
                VideoStartSeconds = videoCursor,
                AudioStartInTimelineSeconds = audioStartInTimeline,
                Padding = ChoosePadding(padding),
                LipSyncOffsetSeconds = Math.Round(offset, 4),
            });

            videoCursor += videoDuration.Value;
        }

        if (maxOffset > request.MaxLipSyncOffsetSeconds)
        {
            return TimelinePlan.Reject(
                [
                    $"Lệch lip-sync lớn nhất là {maxOffset * 1000:0} ms, vượt ngưỡng " +
                    $"{request.MaxLipSyncOffsetSeconds * 1000:0} ms. Với lưới thời lượng {string.Join("/", grid)} giây " +
                    $"và thoại {request.NarrationDurationSeconds:0.##} giây thì không thể giữ audio liên tục — " +
                    $"dùng {nameof(AudioLayoutStrategy.PadPerShot)} để chèn phần dư thành im lặng ở cuối mỗi shot.",
                ],
                warnings);
        }

        if (midSentenceCut)
        {
            warnings.Add("Có shot bị cắt giữa câu. Xem lại lời thoại hoặc tăng SilenceThresholdSeconds.");
        }

        return new TimelinePlan
        {
            IsAcceptable = true,
            Shots = shots,
            Reasons = [],
            TotalVideoSeconds = videoCursor,
            TotalNarrationSeconds = request.NarrationDurationSeconds,
            MaxLipSyncOffsetSeconds = Math.Round(maxOffset, 4),
            Warnings = warnings,
            HasMidSentenceCut = midSentenceCut,
        };
    }

    private static PaddingStrategy ChoosePadding(double paddingSeconds) => paddingSeconds switch
    {
        <= NoPaddingThresholdSeconds => PaddingStrategy.None,
        <= KenBurnsThresholdSeconds => PaddingStrategy.HoldLastFrame,
        _ => PaddingStrategy.KenBurns,
    };

    /// <summary>Lưới thời lượng đã sắp tăng dần và bỏ trùng. Provider cấu hình sai thứ tự là chuyện có thật.</summary>
    private static IReadOnlyList<int> NormalizeGrid(IReadOnlyList<int> grid)
    {
        var normalized = grid.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        return normalized.Count == 0 ? grid : normalized;
    }
}
