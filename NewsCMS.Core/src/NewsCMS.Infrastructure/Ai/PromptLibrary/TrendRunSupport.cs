using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Ai.PromptLibrary;

/// <summary>
/// Phần giống nhau của các lần "cập nhật kho mẫu theo trend" (video, ảnh): đọc mảng JSON AI trả về,
/// làm sạch link nguồn, ghi chú lần chạy, tính lịch, biết có công cụ tìm web không.
/// </summary>
/// <remarks>Mỗi kho giữ khoá chống chạy chồng riêng — kho ảnh đang chạy không chặn kho video.</remarks>
public static class TrendRunSupport
{
    public const int MaxNotesLength = 8000;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Đến lịch chạy chưa. Tính từ lần chạy gần nhất <b>kể cả lần lỗi</b>: AI hỏng thì không bị gọi lại
    /// mỗi lượt kiểm rồi tốn tiền.
    /// </summary>
    public static bool IsDue(bool enabled, int intervalHours, DateTime? lastStartedAt, DateTime now) =>
        enabled && (lastStartedAt is null || now - lastStartedAt.Value >= TimeSpan.FromHours(Math.Max(intervalHours, 1)));

    /// <summary>Đọc mảng mẫu từ câu trả lời của AI — chịu được khối ```json, chữ thừa hai đầu, hoặc object bọc mảng.</summary>
    public static List<T>? ParseArray<T>(string content, out string? error)
    {
        error = null;
        string text = content.Trim();
        int start = text.IndexOf('[');
        int end = text.LastIndexOf(']');

        if (start < 0 || end <= start)
        {
            error = "không thấy mảng JSON trong câu trả lời.";
            return null;
        }

        try
        {
            List<T>? items = JsonSerializer.Deserialize<List<T>>(text[start..(end + 1)], Json);

            if (items is null || items.Count == 0)
            {
                error = "mảng rỗng.";
                return null;
            }

            return items;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Chỉ giữ link http(s), tối đa 5, mỗi link ≤ 500 ký tự; mỗi dòng một link.</summary>
    public static string? CleanUrls(IReadOnlyList<string>? urls)
    {
        if (urls is null)
        {
            return null;
        }

        List<string> clean = urls
            .Where(u => Uri.TryCreate(u?.Trim(), UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .Select(u => u.Trim())
            .Where(u => u.Length <= 500)
            .Distinct()
            .Take(5)
            .ToList();

        return clean.Count == 0 ? null : string.Join('\n', clean);
    }

    public static string Append(string? notes, string line)
    {
        string result = string.IsNullOrEmpty(notes) ? line : notes + "\n" + line;

        return result.Length > MaxNotesLength ? result[..MaxNotesLength] : result;
    }

    public static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Skill trend đang bật <c>UseTools</c> và có ít nhất một công cụ tìm web đang bật.</summary>
    public static async Task<bool> HasWebSearchAsync(AppDbContext db, string skillKey, CancellationToken ct)
    {
        bool skillUsesTools = await db.AiSkills.AnyAsync(
            x => x.Key == skillKey && x.IsActive && !x.IsDeleted && x.UseTools, ct);

        return skillUsesTools && await db.AiSkills.AnyAsync(
            x => x.Kind == AiSkillKind.Tool && x.IsActive && !x.IsDeleted && x.ToolType != null && x.ToolType.EndsWith("_search"), ct);
    }
}
