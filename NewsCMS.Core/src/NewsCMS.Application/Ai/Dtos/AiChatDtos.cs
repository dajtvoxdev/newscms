namespace NewsCMS.Application.Ai.Dtos;

/// <summary>Một lượt hội thoại trò chuyện soạn bài.</summary>
/// <param name="ContentType">Loại nội dung: "post" (bài viết) hoặc "product" (sản phẩm).</param>
/// <param name="Messages">Lịch sử user/assistant xen kẽ, lượt cuối là tin nhắn mới nhất của người dùng.</param>
/// <param name="CurrentTitle">Tiêu đề hiện tại trên form, để AI tham khảo khi sửa.</param>
/// <param name="CurrentExcerpt">Tóm tắt hiện tại trên form.</param>
/// <param name="CurrentBody">Nội dung hiện tại trên form.</param>
/// <param name="ConnectionId">Kết nối AI chỉ định; null = dùng kết nối mặc định.</param>
public record AiChatRequest(
    string ContentType,
    IReadOnlyList<AiChatMessage> Messages,
    string? CurrentTitle,
    string? CurrentExcerpt,
    string? CurrentBody,
    Guid? ConnectionId);

public record AiChatMessage(string Role, string Content);

/// <summary>
/// Kết quả trả về client: AssistantText là lời nhắn trong khung chat,
/// còn Title/Excerpt/Body là 3 phần người dùng có thể bấm apply riêng.
/// </summary>
public record AiChatResult(string AssistantText, string Title, string Excerpt, string Body);
