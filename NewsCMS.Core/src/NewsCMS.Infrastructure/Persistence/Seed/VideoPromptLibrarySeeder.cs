using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.VideoStudio;

namespace NewsCMS.Infrastructure.Persistence.Seed;

/// <summary>
/// Kho prompt VideoStudio: mẫu mặc định, skill AI sinh mẫu theo trend, dòng cấu hình.
/// </summary>
/// <remarks>
/// Chỉ THÊM khi chưa có, không ghi đè: quản trị viên sửa mẫu mặc định hay sửa prompt của skill thì
/// lần seed sau không được xoá công của họ. Mẫu mặc định nhận ra theo tiêu đề.
/// </remarks>
public static class VideoPromptLibrarySeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.VideoPromptLibrarySettings.AnyAsync(ct))
        {
            db.VideoPromptLibrarySettings.Add(new VideoPromptLibrarySettings
            {
                // Tắt mặc định: cần kết nối AI trước, và mỗi lần chạy tốn tiền gọi model.
                TrendAutoUpdateEnabled = false,
                Focus = "Thị trường Việt Nam; video dọc ngắn trên TikTok, Facebook Reels, YouTube Shorts; cửa hàng nhỏ và thương hiệu địa phương.",
            });
        }

        if (!await db.AiSkills.AnyAsync(x => x.Key == AiTaskKeys.VideoStudioTrendTemplates, ct))
        {
            db.AiSkills.Add(new AiSkill
            {
                Key = AiTaskKeys.VideoStudioTrendTemplates,
                Name = "Kho prompt video: mẫu theo xu hướng",
                Description = "Tác vụ nền của VideoStudio gọi skill này để sinh mẫu brief video quảng cáo theo xu hướng đang lên. Bật công cụ tìm web (9Router/Firecrawl) để có xu hướng thật.",
                Kind = AiSkillKind.Prompt,
                IsActive = true,
                SortOrder = 40,
                SystemPrompt = TrendSystemPrompt,
                UserPromptTemplate = "{content}",
                Temperature = 0.7,
                MaxTokens = 6000,
                UseTools = true,
                Targets = "videostudio.templates",
            });
        }

        HashSet<string> existing = (await db.VideoPromptTemplates.IgnoreQueryFilters()
                .Where(x => x.Source == VideoPromptTemplateSource.Default)
                .Select(x => x.Title)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        int order = 0;

        foreach (VideoPromptTemplate template in Defaults())
        {
            order += 10;

            if (existing.Contains(template.Title))
            {
                continue;
            }

            template.Source = VideoPromptTemplateSource.Default;
            template.Status = VideoPromptTemplateStatus.Published;
            template.SortOrder = order;
            db.VideoPromptTemplates.Add(template);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Prompt hệ thống của skill trend. Hợp đồng JSON ở đây là thứ <c>VideoPromptTrendService</c> đọc —
    /// quản trị viên sửa giọng văn thoải mái nhưng giữ nguyên tên trường.
    /// </summary>
    public const string TrendSystemPrompt = """
        Bạn là chuyên gia nội dung video quảng cáo ngắn cho thị trường Việt Nam.
        Nhiệm vụ: tìm các xu hướng (trend) định dạng video ngắn ĐANG lên trong 1–2 tuần gần đây — kiểu mở đầu (hook), cách quay, cấu trúc kể chuyện, âm hưởng — rồi chuyển mỗi xu hướng thành một MẪU BRIEF để cửa hàng nhỏ tự làm video quảng cáo sản phẩm của họ.
        Nếu có công cụ tìm web, hãy dùng nó để kiểm chứng xu hướng và ghi link nguồn. Không bịa link.

        Quy tắc cho mỗi mẫu:
        - Dùng đúng chỗ giữ {san_pham} cho tên sản phẩm trong scene_prompt và script. Không nêu thương hiệu, người nổi tiếng hay bài hát có bản quyền cụ thể.
        - scene_prompt: tả cảnh quay bằng tiếng Việt thường như nói với người quay phim (bối cảnh, ánh sáng, chuyển động máy, cận cảnh gì). 1–4 câu. Không yêu cầu chữ xuất hiện trong hình.
        - script: lời thoại đọc thành tiếng, 25–60 từ, tự nhiên, có lời kêu gọi hành động nhẹ ở cuối.
        - KHÔNG dùng cụm quảng cáo tuyệt đối hoặc cam kết: "nhất", "số 1", "duy nhất", "tốt nhất", "rẻ nhất", "100%", "cam kết", "chữa khỏi", "đảm bảo". Không nói về giá cụ thể.
        - Không nội dung y tế, cờ bạc, rượu bia, thuốc lá, chính trị, nhạy cảm.
        - aspect_ratio: "9:16", "1:1" hoặc "16:9". duration_seconds: số nguyên 6–60.
        - category: một trong: Ăn uống, Mỹ phẩm & làm đẹp, Thời trang, Công nghệ, Nhà cửa & đời sống, Mẹ & bé, Du lịch & lưu trú, Giáo dục, Dịch vụ, Sự kiện & khuyến mãi, Khác.

        Chỉ trả về MỘT mảng JSON hợp lệ, không markdown, không giải thích. Mỗi phần tử:
        {"title": "...", "category": "...", "description": "hợp với sản phẩm nào, vì sao hiệu quả (1 câu)", "trend_name": "tên xu hướng", "scene_prompt": "...", "script": "...", "aspect_ratio": "9:16", "duration_seconds": 15, "has_person": false, "source_urls": ["https://..."]}
        """;

    private static IEnumerable<VideoPromptTemplate> Defaults()
    {
        yield return T("Cận cảnh món ăn nóng hổi", "Ăn uống",
            "Món ăn, đồ uống: khói, nước sốt, tiếng xèo xèo làm người xem thèm ngay giây đầu.",
            "Cận cảnh {san_pham} vừa ra lò trên bàn gỗ, hơi nóng bốc lên, ánh sáng ấm buổi chiều. Máy quay tiến chậm vào món ăn, một chiếc đũa gắp lên cho thấy độ mềm và nước sốt óng ánh.",
            "Mỗi sáng, {san_pham} được làm mới từ nguyên liệu tươi trong ngày. Thơm, nóng, đậm vị — ghé quán hoặc đặt ngay hôm nay để thưởng thức khi còn nóng hổi nhé!",
            15, false);

        yield return T("Pha chế đồ uống từng bước", "Ăn uống",
            "Quán cà phê, trà sữa, nước ép: quy trình pha chế đẹp mắt, nhịp nhanh.",
            "Quầy pha chế sáng sủa. Quay từ trên xuống cảnh rót sữa, đá rơi vào ly, lớp kem phủ lên {san_pham}. Cắt nhanh theo nhịp, kết thúc bằng ly thành phẩm xoay chậm trên quầy.",
            "Một ly {san_pham} được pha tay từng bước: đá mát, vị đậm, lớp kem mịn. Ghé quán hôm nay, hoặc đặt giao tận nơi chỉ vài phút thôi!",
            12, false);

        yield return T("Trước — sau khi dùng", "Mỹ phẩm & làm đẹp",
            "Mỹ phẩm, chăm sóc cá nhân: so sánh cảm nhận trước và sau, không hứa hẹn kết quả tuyệt đối.",
            "Phòng tắm sáng, gương sạch. Cận cảnh tay lấy một lượng {san_pham} vừa đủ, thoa nhẹ lên da. Cảnh làn da căng mịn dưới ánh sáng tự nhiên, sản phẩm đặt cạnh chậu cây xanh.",
            "Chỉ vài phút mỗi tối cùng {san_pham}, làn da được chăm sóc nhẹ nhàng và thấy mềm mại hơn mỗi ngày. Thử ngay để cảm nhận sự khác biệt của riêng bạn!",
            15, true);

        yield return T("Mở hộp chậm (unboxing)", "Công nghệ",
            "Đồ công nghệ, phụ kiện, quà tặng: nhịp chậm, âm thanh mở hộp, lộ dần chi tiết.",
            "Bàn làm việc tối giản, nền tối, một luồng sáng chiếu nghiêng. Hai tay mở chậm hộp {san_pham}, nhấc sản phẩm ra, xoay để thấy các chi tiết và chất liệu. Máy quay cận cảnh từng cạnh.",
            "Mở hộp {san_pham}: thiết kế gọn, hoàn thiện chắc tay, dùng được ngay. Món đồ nhỏ giúp ngày làm việc của bạn nhẹ nhàng hơn — xem chi tiết và đặt hàng ngay!",
            15, false);

        yield return T("Phối đồ 3 kiểu", "Thời trang",
            "Quần áo, phụ kiện: một món đồ, ba cách phối, chuyển cảnh theo nhịp.",
            "Phòng thử đồ sáng, gương lớn. Người mẫu mặc {san_pham} phối ba kiểu: đi làm, đi chơi, dạo phố. Mỗi kiểu một cảnh xoay người, chuyển cảnh nhanh bằng cú vẫy tay qua ống kính.",
            "Một chiếc {san_pham}, ba phong cách: đi làm thanh lịch, đi chơi năng động, dạo phố thoải mái. Chọn size của bạn và đặt ngay hôm nay!",
            15, true);

        yield return T("Một ngày cùng sản phẩm", "Nhà cửa & đời sống",
            "Đồ gia dụng, nội thất: cho thấy sản phẩm trong nhịp sống thật từ sáng tới tối.",
            "Căn hộ ấm cúng, ánh sáng tự nhiên. Chuỗi cảnh ngắn: buổi sáng, buổi trưa, buổi tối, mỗi cảnh {san_pham} xuất hiện đúng lúc cần dùng. Máy quay mượt, tông màu ấm.",
            "Từ sáng đến tối, {san_pham} luôn sẵn sàng giúp bạn. Gọn gàng, dễ dùng, hợp với mọi góc nhà — đặt ngay để tổ ấm tiện nghi hơn!",
            20, false);

        yield return T("Mẹ tin dùng", "Mẹ & bé",
            "Sản phẩm cho bé: không khí gia đình, nhẹ nhàng, an tâm.",
            "Phòng ngủ sáng màu pastel, ánh nắng dịu. Tay người mẹ cầm {san_pham} đặt cạnh em bé đang chơi, cận cảnh chất liệu mềm và chi tiết bo tròn. Nhịp quay chậm, êm.",
            "Chọn {san_pham} cho bé yêu: chất liệu mềm mại, thiết kế an toàn, mẹ dùng thật yên tâm. Tìm hiểu thêm và đặt hàng ngay hôm nay!",
            15, true);

        yield return T("Góc view nghỉ dưỡng", "Du lịch & lưu trú",
            "Homestay, khách sạn, tour: mở đầu bằng khung cảnh đẹp, sau đó là tiện nghi.",
            "Bình minh trên ban công nhìn ra núi đồi, sương mỏng. Máy quay lướt từ khung cảnh vào phòng nghỉ {san_pham} gọn gàng, ly trà nóng trên bàn, rèm cửa bay nhẹ.",
            "Thức dậy giữa mây núi, nhâm nhi ly trà nóng tại {san_pham}. Một kỳ nghỉ yên bình đang chờ bạn — đặt phòng sớm cho chuyến đi sắp tới nhé!",
            20, false);

        yield return T("3 điều bạn chưa biết", "Giáo dục",
            "Khoá học, dịch vụ tư vấn: mở đầu bằng câu hỏi, nêu nhanh ba ý, kết bằng lời mời.",
            "Bàn học gọn gàng, sổ tay và laptop. Ba cảnh cận: trang sổ ghi chú, màn hình bài học của {san_pham}, tay đánh dấu hoàn thành. Chuyển cảnh nhanh theo từng ý.",
            "Bạn đã biết ba điều giúp học nhanh hơn chưa? Học đều mỗi ngày, luyện qua ví dụ thật, và có người đồng hành. {san_pham} giúp bạn cả ba — đăng ký học thử ngay!",
            20, true);

        yield return T("Hậu trường làm dịch vụ", "Dịch vụ",
            "Spa, salon, sửa chữa, vệ sinh: cho thấy tay nghề và quy trình cẩn thận.",
            "Không gian cửa hàng sạch sẽ, ánh sáng trắng. Cận cảnh đôi tay kỹ thuật viên thực hiện {san_pham} từng bước tỉ mỉ, dụng cụ xếp ngay ngắn. Kết thúc bằng cảnh khách hàng hài lòng.",
            "Mỗi dịch vụ {san_pham} đều được làm tỉ mỉ theo quy trình rõ ràng, bởi đội ngũ nhiều kinh nghiệm. Đặt lịch hôm nay để được phục vụ chu đáo!",
            15, true);

        yield return T("Đếm ngược ưu đãi", "Sự kiện & khuyến mãi",
            "Chương trình khuyến mãi có thời hạn: nhịp nhanh, cảm giác không nên bỏ lỡ.",
            "Nền màu tươi sáng, {san_pham} xếp thành tháp ở giữa khung hình. Cắt nhanh các góc cận sản phẩm, ánh đèn nhấp nháy nhẹ theo nhịp, kết thúc bằng cảnh sản phẩm được gói quà.",
            "Ưu đãi {san_pham} chỉ diễn ra trong thời gian ngắn! Số lượng có hạn, nhanh tay đặt ngay để nhận quà tặng kèm hấp dẫn nhé!",
            12, false);

        yield return T("Câu chuyện người làm ra sản phẩm", "Khác",
            "Thương hiệu địa phương, thủ công: kể chuyện chân thật tạo thiện cảm.",
            "Xưởng nhỏ ấm áp, ánh sáng từ cửa sổ. Người làm nghề chăm chút {san_pham} bằng tay, cận cảnh đôi tay và chất liệu. Kết thúc bằng sản phẩm hoàn thiện đặt trên kệ gỗ.",
            "Mỗi {san_pham} là thành quả của đôi tay tỉ mỉ và nhiều năm gắn bó với nghề. Ủng hộ sản phẩm địa phương — đặt hàng để mang câu chuyện này về nhà bạn!",
            20, true);
    }

    private static VideoPromptTemplate T(
        string title, string category, string description, string scene, string script, int duration, bool hasPerson) => new()
    {
        Title = title,
        Category = category,
        Description = description,
        ScenePrompt = scene,
        ScriptTemplate = script,
        AspectRatio = "9:16",
        DurationSeconds = duration,
        HasPerson = hasPerson,
    };
}
