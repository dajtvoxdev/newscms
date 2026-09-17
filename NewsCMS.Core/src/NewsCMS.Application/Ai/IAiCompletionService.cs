using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;

namespace NewsCMS.Application.Ai;

public interface IAiCompletionService
{
    Task<Result<AiGenerationResult>> GenerateAsync(AiGenerationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Trò chuyện nhiều lượt để soạn bài: nhận cả lịch sử hội thoại và trả về
    /// đồng thời tiêu đề / tóm tắt / nội dung để người dùng apply từng phần.
    /// </summary>
    Task<Result<AiChatResult>> ChatAsync(AiChatRequest request, CancellationToken ct = default);
}
