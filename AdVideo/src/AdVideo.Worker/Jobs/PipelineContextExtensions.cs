using AdVideo.Core.Media;
using AdVideo.Core.Pipeline;
using AdVideo.Core.Providers;

namespace AdVideo.Worker.Jobs;

/// <summary>
/// Những thứ các bước gửi cho nhau qua <see cref="PipelineContext.Bag"/>.
/// </summary>
/// <remarks>
/// Dùng hàm có kiểu thay vì gõ thẳng chuỗi khoá ở mỗi bước: một khoá gõ sai không gây lỗi biên
/// dịch, nó chỉ làm bước sau đọc ra <c>null</c> và âm thầm chạy theo nhánh mặc định — đúng kiểu
/// lỗi chỉ lộ ra khi xem video thành phẩm.
/// </remarks>
public static class PipelineContextExtensions
{
    private const string NativeSoundKey = "native_sound_decision";
    private const string AiLabelKey = "ai_label_spec";

    /// <summary>Brief đã đọc ở bước 1.</summary>
    /// <exception cref="InvalidOperationException">Bước 1 chưa chạy — đó là lỗi thứ tự bước, không phải lỗi dữ liệu.</exception>
    public static JobBrief Brief(this PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Bag.TryGetValue(JobBrief.BagKey, out object? value) && value is JobBrief brief
            ? brief
            : throw new InvalidOperationException(
                "Chưa có brief trong ngữ cảnh: bước 1 (nạp) phải chạy trước mọi bước khác.");
    }

    public static void SetBrief(this PipelineContext context, JobBrief brief)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Bag[JobBrief.BagKey] = brief;
    }

    /// <summary>Quyết định về tiếng gốc của provider, chốt ở bước 6 và dùng lại ở bước 8.</summary>
    /// <remarks>
    /// Chốt một lần chứ không tính lại ở bước 8: bước 6 đã gửi tham số đi theo quyết định đó, và
    /// nếu bước 8 tính ra kết quả khác thì video sẽ được trộn theo một giả định mà provider
    /// không hề biết.
    /// </remarks>
    public static NativeSoundDecision? NativeSound(this PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Bag.TryGetValue(NativeSoundKey, out object? value) ? value as NativeSoundDecision : null;
    }

    public static void SetNativeSound(this PipelineContext context, NativeSoundDecision decision)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Bag[NativeSoundKey] = decision;
    }

    /// <summary>Đặc tả nhãn AI đã vẽ lên video ở bước 8.</summary>
    /// <remarks>
    /// Bước 9 chấm đúng đặc tả này chứ không tự dựng lại một cái mới: hai bước tự tính riêng thì
    /// QC đang chấm một thứ khác với thứ đã nằm trên video — và nó sẽ luôn đạt, kể cả khi bước 8
    /// vẽ sai.
    /// </remarks>
    public static AiLabelSpec? AiLabel(this PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Bag.TryGetValue(AiLabelKey, out object? value) ? value as AiLabelSpec : null;
    }

    public static void SetAiLabel(this PipelineContext context, AiLabelSpec label)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Bag[AiLabelKey] = label;
    }
}
