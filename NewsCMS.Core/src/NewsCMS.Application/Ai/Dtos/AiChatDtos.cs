namespace NewsCMS.Application.Ai.Dtos;

/// <summary>Một lượt hội thoại trò chuyện soạn bài.</summary>
/// <param name="ContentType">Loại nội dung: "post" (bài viết) hoặc "product" (sản phẩm).</param>
/// <param name="Messages">Lịch sử user/assistant xen kẽ, lượt cuối là tin nhắn mới nhất của người dùng.</param>
/// <param name="CurrentTitle">Tiêu đề hiện tại trên form, để AI tham khảo khi sửa.</param>
/// <param name="CurrentExcerpt">Tóm tắt hiện tại trên form.</param>
/// <param name="CurrentBody">Nội dung hiện tại trên form.</param>
/// <param name="ConnectionId">Kết nối AI chỉ định; null = dùng kết nối mặc định.</param>
/// <param name="IncludeSiteContext">
/// Gửi kèm tên/mô tả site, chuyên mục, sản phẩm và vài bài gần đây để AI viết
/// khớp với website. Mặc định bật; client tắt được bằng ô tích trong khung chat.
/// </param>
public record AiChatRequest(
    string ContentType,
    IReadOnlyList<AiChatMessage> Messages,
    string? CurrentTitle,
    string? CurrentExcerpt,
    string? CurrentBody,
    Guid? ConnectionId,
    bool IncludeSiteContext = true);

public record AiChatMessage(string Role, string Content);

/// <summary>
/// Kết quả trả về client: AssistantText là lời nhắn trong khung chat,
/// còn Title/Excerpt/Body là 3 phần người dùng có thể bấm apply riêng.
/// SiteContext là khối ngữ cảnh đã gửi kèm (rỗng nếu người dùng tắt) — trả về
/// để khung chat hiện được "AI đang thấy gì", giúp truy khi AI viết sai.
/// </summary>
public record AiChatResult(string AssistantText, string Title, string Excerpt, string Body, string SiteContext = "");
