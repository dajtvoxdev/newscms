using System.Text;
using System.Text.Json;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>
/// "Cải thiện prompt" và "Gợi ý từ nội dung" — một lần gọi chat qua kết nối AI mặc định, prompt hệ thống
/// nằm trong skill (quản trị sửa ở trang Kỹ năng AI).
/// </summary>
/// <remarks>
/// Kết quả là chữ do AI viết nên luôn được làm sạch: bỏ dấu ngoặc/markdown bao ngoài, cắt độ dài. Prompt
/// không được mất chỗ giữ người dùng đã có — mất thì trả lại lỗi thay vì âm thầm đổi ý mẫu.
/// </remarks>
public sealed class ImagePromptAssistant : IImagePromptAssistant
{
    public const int MaxInputLength = 4000;

    private readonly IAiCompletionService _ai;

    public ImagePromptAssistant(IAiCompletionService ai) => _ai = ai;

    public async Task<Result<string>> EnhanceAsync(string prompt, ImagePurpose purpose, CancellationToken ct = default)
    {
        string text = prompt?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return Result<string>.Failure("Hãy viết vài chữ mô tả trước, rồi mới cải thiện.");
        }

        if (text.Length > MaxInputLength)
        {
            return Result<string>.Failure($"Mô tả dài quá {MaxInputLength} ký tự.");
        }

        var request = new StringBuilder();
        request.AppendLine($"Mục đích ảnh: {ImagePurposes.Label(purpose)}.");
        request.AppendLine("Mô tả của người dùng:");
        request.AppendLine(text);

        Result<AiGenerationResult> result = await _ai.GenerateAsync(new AiGenerationRequest(
            AiTaskKeys.ImageStudioPromptEnhance, AiGenerationStyle.Plain, null, request.ToString(), null, "vi", null), ct);

        if (!result.Succeeded)
        {
            return Result<string>.Failure($"AI không trả lời: {result.Error}");
        }

        string improved = Clean(result.Value!.Content, ImageStudioRules.MaxPromptLength);

        if (improved.Length == 0)
        {
            return Result<string>.Failure("AI trả về rỗng. Thử lại.");
        }

        List<string> lost = ImagePromptPlaceholders.FindKnown(text).Except(ImagePromptPlaceholders.FindKnown(improved)).ToList();

        if (lost.Count > 0)
        {
            return Result<string>.Failure($"AI làm mất chỗ giữ {string.Join(", ", lost)}. Thử lại, hoặc điền chỗ giữ trước khi cải thiện.");
        }

        return Result<string>.Success(improved);
    }

    public async Task<Result<ImagePromptSuggestionDto>> SuggestAsync(ImagePromptSuggestInput input, CancellationToken ct = default)
    {
        string title = input.Title?.Trim() ?? string.Empty;
        string excerpt = input.Excerpt?.Trim() ?? string.Empty;

        if (title.Length == 0 && excerpt.Length == 0)
        {
            return Result<ImagePromptSuggestionDto>.Failure("Chưa có nội dung để gợi ý — nhập tiêu đề hoặc tóm tắt trước.");
        }

        var request = new StringBuilder();
        request.AppendLine($"Mục đích ảnh: {ImagePurposes.Label(input.Purpose)}.");
        request.AppendLine($"Tiêu đề / tên: {Truncate(title, 300)}");

        if (excerpt.Length > 0)
        {
            request.AppendLine($"Tóm tắt / mô tả: {Truncate(excerpt, 1500)}");
        }

        Result<AiGenerationResult> result = await _ai.GenerateAsync(new AiGenerationRequest(
            AiTaskKeys.ImageStudioSuggestPrompt, AiGenerationStyle.Plain, title, request.ToString(), null, "vi", null), ct);

        if (!result.Succeeded)
        {
            return Result<ImagePromptSuggestionDto>.Failure($"AI không trả lời: {result.Error}");
        }

        string content = result.Value!.Content.Trim();
        int start = content.IndexOf('{');
        int end = content.LastIndexOf('}');

        if (start >= 0 && end > start)
        {
            try
            {
                Suggestion? parsed = JsonSerializer.Deserialize<Suggestion>(content[start..(end + 1)], new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (!string.IsNullOrWhiteSpace(parsed?.Prompt))
                {
                    return Result<ImagePromptSuggestionDto>.Success(new ImagePromptSuggestionDto(
                        Clean(parsed.Prompt, ImageStudioRules.MaxPromptLength),
                        string.IsNullOrWhiteSpace(parsed.Alt) ? null : Clean(parsed.Alt, 200),
                        string.IsNullOrWhiteSpace(parsed.Caption) ? null : Clean(parsed.Caption, 200)));
                }
            }
            catch (JsonException)
            {
                // Rơi xuống: dùng nguyên văn làm prompt.
            }
        }

        // Model không theo hợp đồng JSON: vẫn dùng được câu trả lời làm prompt.
        string plain = Clean(content, ImageStudioRules.MaxPromptLength);

        return plain.Length == 0
            ? Result<ImagePromptSuggestionDto>.Failure("AI trả về rỗng. Thử lại.")
            : Result<ImagePromptSuggestionDto>.Success(new ImagePromptSuggestionDto(plain, null, null));
    }

    /// <summary>Bỏ khối ``` và dấu ngoặc kép bao ngoài, gộp khoảng trắng thừa, cắt độ dài.</summary>
    internal static string Clean(string text, int max)
    {
        string result = text.Trim();

        if (result.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLine = result.IndexOf('\n');
            result = firstLine >= 0 ? result[(firstLine + 1)..] : result.Trim('`');
            result = result.TrimEnd().TrimEnd('`').Trim();
        }

        result = result.Trim().Trim('"', '“', '”', '\'').Trim();

        return Truncate(result, max);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max].TrimEnd();

    private sealed record Suggestion(string? Prompt, string? Alt, string? Caption);
}
