using System.Text.Json.Nodes;

namespace AdVideo.Core.Providers.Descriptors;

/// <summary>Lượng dùng của MỘT lần gọi, đầu vào để tính tiền.</summary>
/// <param name="Variables">Biến đã dựng cho lần gọi — để tra <see cref="DescriptorRateTable.Variable"/>.</param>
/// <param name="Seconds">Số giây video bị tính tiền.</param>
/// <param name="Characters">Số ký tự bị tính tiền (TTS).</param>
/// <param name="ReferenceImages">Số ảnh tham chiếu đã gửi.</param>
public sealed record DescriptorUsage(
    IReadOnlyDictionary<string, JsonNode?> Variables,
    int Seconds = 0,
    int Characters = 0,
    int ReferenceImages = 0);

/// <summary>
/// Tính tiền một lần gọi theo khối <c>cost</c> của descriptor.
/// </summary>
/// <remarks>
/// <para>
/// Con số này là tiền <b>tự suy</b>, không phải tiền provider báo — nó vào sổ với
/// <c>CostIsReported = false</c>. Provider nào tự báo giá thì khai <c>result.reportedCostPath</c>.
/// </para>
/// <para>
/// <b>Giá trị biến không có trong bảng giá thì lấy mức CAO NHẤT của bảng.</b> Ước sai về phía cao
/// làm trần chi tiêu chặt hơn thực tế; ước sai về phía thấp làm trần thấp hơn hoá đơn — tức là
/// không còn là trần.
/// </para>
/// </remarks>
public static class DescriptorCostCalculator
{
    public static decimal Calculate(DescriptorCost cost, DescriptorUsage usage)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(usage);

        decimal rate = ResolveRate(cost, usage.Variables);
        decimal total = rate * Quantity(cost.Unit, usage);

        foreach (DescriptorCostExtra extra in cost.Extras)
        {
            total += extra.RateUsd * Quantity(extra.Unit, usage);
        }

        return total;
    }

    /// <summary>Đơn giá chính cao nhất có thể — dùng để đối chiếu với capability.</summary>
    public static decimal MaxRate(DescriptorCost cost)
    {
        ArgumentNullException.ThrowIfNull(cost);

        return cost.RateBy is { Table.Count: > 0 } table
            ? table.Table.Values.Max()
            : cost.RateUsd ?? 0m;
    }

    /// <summary>
    /// Giá mỗi giây video trong trường hợp đắt nhất, đã chia đều phí theo lần gọi cho clip NGẮN nhất.
    /// </summary>
    /// <remarks>
    /// <c>CostEstimator</c> dự toán bằng <c>CostPerSecondUsd × số giây</c>. Capability khai thấp hơn
    /// con số này là dự toán thấp hơn hoá đơn — <see cref="DescriptorValidator"/> chặn trường hợp đó.
    /// Biến đã cố định (khối <c>defaults</c>) thì dùng đúng đơn giá của nó; biến phụ thuộc request
    /// thì lấy mức cao nhất của bảng.
    /// </remarks>
    public static decimal WorstCasePerSecond(
        DescriptorCost cost,
        int shortestClipSeconds,
        int maxReferenceImages,
        IReadOnlyDictionary<string, JsonNode?>? fixedVariables = null)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shortestClipSeconds);

        var usage = new DescriptorUsage(
            fixedVariables ?? new Dictionary<string, JsonNode?>(),
            Seconds: shortestClipSeconds,
            ReferenceImages: Math.Max(0, maxReferenceImages));

        return Calculate(cost, usage) / shortestClipSeconds;
    }

    private static decimal ResolveRate(DescriptorCost cost, IReadOnlyDictionary<string, JsonNode?> variables)
    {
        if (cost.RateBy is not { } rateBy)
        {
            return cost.RateUsd ?? 0m;
        }

        string? key = variables.TryGetValue(rateBy.Variable, out JsonNode? value)
            ? JsonPathReader.AsString(value)
            : null;

        return key is not null && rateBy.Table.TryGetValue(key, out decimal rate)
            ? rate
            : MaxRate(cost);
    }

    private static decimal Quantity(DescriptorCostUnit unit, DescriptorUsage usage) => unit switch
    {
        DescriptorCostUnit.PerSecond => usage.Seconds,
        DescriptorCostUnit.Per1000Chars => usage.Characters / 1000m,
        DescriptorCostUnit.PerReferenceImage => usage.ReferenceImages,
        _ => 1m,
    };
}
