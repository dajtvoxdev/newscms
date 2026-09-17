using System.Text;

namespace NewsCMS.Application.Ai;

/// <summary>
/// Ảnh chụp nội dung site hiện tại để làm ngữ cảnh cho AI. Mọi truy vấn dựng nên
/// snapshot này đều đi qua AppDbContext nên đã được lọc theo site hiện tại —
/// không có đường nào lẫn nội dung của site khác vào prompt.
/// </summary>
public record SiteContextSnapshot(
    string? SiteName,
    string? SiteDescription,
    IReadOnlyList<string> Categories,
    IReadOnlyList<SiteContextPost> RecentPosts,
    IReadOnlyList<string> Products)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(SiteName)
        && string.IsNullOrWhiteSpace(SiteDescription)
        && Categories.Count == 0
        && RecentPosts.Count == 0
        && Products.Count == 0;
}

/// <param name="BodyExcerpt">Trích đoạn thân bài đã cắt ngắn — chỉ để AI bắt giọng văn.</param>
public record SiteContextPost(string Title, string? Excerpt, string? BodyExcerpt);

public interface ISiteContextBuilder
{
    Task<SiteContextSnapshot> BuildAsync(CancellationToken ct = default);
}

/// <summary>
/// Đổi snapshot thành khối text chèn vào system prompt. Tách riêng khỏi service
/// để unit test được phần cắt ngắn và định dạng — phần dễ hỏng im lặng nhất.
/// </summary>
public static class SiteContextFormatter
{
    /// <summary>Số bài cũ lấy làm mẫu giọng văn. Nhiều hơn thì tốn token mà ích lợi giảm nhanh.</summary>
    public const int MaxPosts = 4;
    public const int MaxProducts = 10;
    public const int ExcerptChars = 150;
    public const int BodyChars = 300;

    /// <summary>Trả về chuỗi rỗng khi không có gì để nói — caller khỏi phải kiểm tra riêng.</summary>
    public static string Format(SiteContextSnapshot snapshot)
    {
        if (snapshot.IsEmpty) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("--- NGỮ CẢNH WEBSITE ĐANG VIẾT ---");

        if (!string.IsNullOrWhiteSpace(snapshot.SiteName))
            sb.AppendLine("Tên website: " + snapshot.SiteName.Trim());
        if (!string.IsNullOrWhiteSpace(snapshot.SiteDescription))
            sb.AppendLine("Mô tả website: " + snapshot.SiteDescription.Trim());

        if (snapshot.Categories.Count > 0)
            sb.AppendLine("Chuyên mục đang dùng: " + string.Join(", ", snapshot.Categories));

        if (snapshot.Products.Count > 0)
            sb.AppendLine("Sản phẩm đang bán: " + string.Join("; ", snapshot.Products));

        if (snapshot.RecentPosts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Một số bài đã đăng gần đây — dùng để hiểu giọng văn và cách đặt tiêu đề của site, KHÔNG sao chép nội dung:");
            var index = 1;
            foreach (var post in snapshot.RecentPosts)
            {
                sb.AppendLine(index + ". " + post.Title);
                if (!string.IsNullOrWhiteSpace(post.Excerpt))
                    sb.AppendLine("   Tóm tắt: " + Truncate(post.Excerpt, ExcerptChars));
                if (!string.IsNullOrWhiteSpace(post.BodyExcerpt))
                    sb.AppendLine("   Trích đoạn: " + Truncate(post.BodyExcerpt, BodyChars));
                index++;
            }
        }

        sb.AppendLine("--- HẾT NGỮ CẢNH ---");
        sb.AppendLine();
        sb.AppendLine("Viết nội dung khớp với website trên: đúng chủ đề, đúng giọng văn, "
            + "và chỉ nhắc tới sản phẩm/chuyên mục có thật trong danh sách. "
            + "Tuyệt đối không bịa thêm sản phẩm, giá, hay chuyên mục không có ở trên.");

        return sb.ToString();
    }

    /// <summary>
    /// Cắt ngắn nhưng gọn ở ranh giới từ, tránh đứt giữa chừng một từ có dấu.
    /// </summary>
    public static string Truncate(string value, int maxChars)
    {
        var text = value.Trim().ReplaceLineEndings(" ");
        // Gộp khoảng trắng liên tiếp để trích đoạn không phí chỗ cho xuống dòng cũ.
        while (text.Contains("  ")) text = text.Replace("  ", " ");

        if (text.Length <= maxChars) return text;

        var cut = text.LastIndexOf(' ', Math.Min(maxChars, text.Length - 1));
        if (cut <= 0) cut = maxChars;
        return text[..cut] + "…";
    }
}
