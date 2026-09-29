using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Infrastructure.Persistence.Seed;

/// <summary>
/// Kho mẫu Xưởng ảnh: dòng cấu hình, ba skill AI (trend, cải thiện prompt, gợi ý từ nội dung), mẫu mặc định.
/// </summary>
/// <remarks>
/// Chỉ THÊM khi chưa có, không ghi đè: quản trị sửa mẫu mặc định hay prompt của skill thì lần seed sau
/// không xoá công của họ. Mẫu mặc định nhận ra theo tiêu đề. Mẫu seed <b>không kèm ảnh demo</b> (không
/// commit ảnh vào repo) — sau khi deploy, quản trị bấm "Tạo demo cho mọi mẫu chưa có".
/// </remarks>
public static class ImagePromptLibrarySeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.ImagePromptLibrarySettings.AnyAsync(ct))
        {
            db.ImagePromptLibrarySettings.Add(new ImagePromptLibrarySettings
            {
                // Tắt mặc định: cần kết nối AI trước, và mỗi lần chạy tốn tiền gọi model.
                TrendAutoUpdateEnabled = false,
                Focus = "Thị trường Việt Nam; ảnh cho bài viết tin tức, blog và ảnh sản phẩm của cửa hàng nhỏ; phong cách ảnh đang được chuộng trên Facebook, Instagram, TikTok.",
                BlockedTerms = ImagePromptTemplateRules.DefaultBlockedTerms,
            });
        }

        AddSkill(db, await db.AiSkills.AnyAsync(x => x.Key == AiTaskKeys.ImageStudioTrendTemplates, ct), new AiSkill
        {
            Key = AiTaskKeys.ImageStudioTrendTemplates,
            Name = "Kho mẫu ảnh: mẫu theo xu hướng",
            Description = "Tác vụ nền của Xưởng ảnh gọi skill này để sinh mẫu prompt ảnh theo phong cách đang lên. Bật công cụ tìm web (9Router/Firecrawl) để có xu hướng thật.",
            Kind = AiSkillKind.Prompt,
            IsActive = true,
            SortOrder = 41,
            SystemPrompt = TrendSystemPrompt,
            UserPromptTemplate = "{content}",
            Temperature = 0.8,
            MaxTokens = 6000,
            UseTools = true,
            Targets = "imagestudio.templates",
        });

        AddSkill(db, await db.AiSkills.AnyAsync(x => x.Key == AiTaskKeys.ImageStudioPromptEnhance, ct), new AiSkill
        {
            Key = AiTaskKeys.ImageStudioPromptEnhance,
            Name = "Xưởng ảnh: cải thiện prompt",
            Description = "Nút \"Cải thiện prompt\" trong modal tạo ảnh: viết lại mô tả của người dùng cho chi tiết hơn (bố cục, ánh sáng, góc máy, chất liệu).",
            Kind = AiSkillKind.Prompt,
            IsActive = true,
            SortOrder = 42,
            SystemPrompt = EnhanceSystemPrompt,
            UserPromptTemplate = "{content}",
            Temperature = 0.6,
            MaxTokens = 800,
            Targets = "imagestudio.prompt",
        });

        AddSkill(db, await db.AiSkills.AnyAsync(x => x.Key == AiTaskKeys.ImageStudioSuggestPrompt, ct), new AiSkill
        {
            Key = AiTaskKeys.ImageStudioSuggestPrompt,
            Name = "Xưởng ảnh: gợi ý prompt từ nội dung",
            Description = "Nút \"Gợi ý từ nội dung\": đọc tiêu đề/tóm tắt bài viết hoặc tên/mô tả sản phẩm, đề xuất prompt ảnh, alt và chú thích.",
            Kind = AiSkillKind.Prompt,
            IsActive = true,
            SortOrder = 43,
            SystemPrompt = SuggestSystemPrompt,
            UserPromptTemplate = "{content}",
            Temperature = 0.6,
            MaxTokens = 800,
            Targets = "imagestudio.prompt",
        });

        HashSet<string> existing = (await db.ImagePromptTemplates.IgnoreQueryFilters()
                .Where(x => x.Source == ImagePromptTemplateSource.Default)
                .Select(x => x.Title)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        int order = 0;

        foreach (ImagePromptTemplate template in Defaults())
        {
            order += 10;

            if (existing.Contains(template.Title))
            {
                continue;
            }

            template.Source = ImagePromptTemplateSource.Default;
            template.Status = ImagePromptTemplateStatus.Published;
            template.SortOrder = order;
            db.ImagePromptTemplates.Add(template);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void AddSkill(AppDbContext db, bool exists, AiSkill skill)
    {
        if (!exists)
        {
            db.AiSkills.Add(skill);
        }
    }

    /// <summary>
    /// Prompt hệ thống của skill trend. Hợp đồng JSON là thứ <c>ImagePromptTrendService</c> đọc — quản
    /// trị sửa giọng văn thoải mái nhưng giữ nguyên tên trường.
    /// </summary>
    public const string TrendSystemPrompt = """
        Bạn là giám đốc hình ảnh cho các website tin tức và cửa hàng nhỏ ở Việt Nam.
        Nhiệm vụ: tìm các PHONG CÁCH ẢNH đang được chuộng trong 1–2 tuần gần đây (cách chụp sản phẩm, kiểu minh hoạ, bảng màu, bố cục, xu hướng mùa/lễ) rồi chuyển mỗi phong cách thành một MẪU PROMPT tạo ảnh bằng AI.
        Nếu có công cụ tìm web, hãy dùng nó để kiểm chứng xu hướng và ghi link nguồn. Không bịa link.

        Quy tắc cho mỗi mẫu:
        - purpose là một trong: "post_cover" (ảnh bìa bài viết), "post_inline" (ảnh minh hoạ trong bài), "product_main" (ảnh sản phẩm chính), "product_gallery" (ảnh sản phẩm bối cảnh), "banner", "social".
        - Mẫu post_cover / post_inline BẮT BUỘC có chỗ giữ {chu_de}; mẫu product_main / product_gallery BẮT BUỘC có chỗ giữ {san_pham}. Có thể dùng thêm {mo_ta}, {thuong_hieu}. Không dùng chỗ giữ nào khác.
        - prompt: tiếng Việt, 2–5 câu, tả rõ chủ thể, bối cảnh, bố cục, góc máy, ánh sáng, bảng màu, chất liệu, phong cách.
        - KHÔNG yêu cầu chữ, logo hay watermark trong ảnh. KHÔNG nêu tên người thật, người nổi tiếng, nhân vật hoạt hình có bản quyền, thương hiệu, hay "theo phong cách" của nghệ sĩ/hãng phim cụ thể.
        - KHÔNG dùng cụm quảng cáo tuyệt đối: "nhất", "số 1", "duy nhất", "100%", "cam kết", "đảm bảo".
        - Không nội dung y tế, cờ bạc, rượu bia, thuốc lá, chính trị, bạo lực, nhạy cảm.
        - aspect_ratio: một trong "1:1", "16:9", "9:16", "4:3", "3:4", "3:2", "2:3".
        - category: một trong: Tin tức & bài viết, Sản phẩm, Ăn uống, Mỹ phẩm & làm đẹp, Thời trang, Công nghệ, Nhà cửa & đời sống, Du lịch & lưu trú, Giáo dục, Sự kiện & khuyến mãi, Mạng xã hội, Khác.

        Chỉ trả về MỘT mảng JSON hợp lệ, không markdown, không giải thích. Mỗi phần tử:
        {"title": "...", "category": "...", "purpose": "product_main", "description": "ảnh ra sao, hợp với gì (1 câu)", "trend_name": "tên xu hướng", "prompt": "...", "aspect_ratio": "1:1", "source_urls": ["https://..."]}
        """;

    public const string EnhanceSystemPrompt = """
        Bạn là chuyên gia viết prompt cho model tạo ảnh.
        Viết lại mô tả ảnh của người dùng cho chi tiết và rõ ràng hơn: chủ thể, bối cảnh, bố cục, góc máy, ánh sáng, bảng màu, chất liệu, phong cách, độ sâu trường ảnh.
        Giữ nguyên ý của người dùng. Giữ nguyên mọi chỗ giữ dạng {chu_de}, {san_pham}, {mo_ta}, {thuong_hieu} nếu có.
        Không thêm chữ, logo, watermark vào ảnh (trừ khi người dùng yêu cầu). Không thêm tên người thật, nhân vật có bản quyền hay thương hiệu.
        Viết tiếng Việt, tối đa 120 từ, một đoạn văn.
        Chỉ trả về prompt đã viết lại — không lời chào, không giải thích, không dấu ngoặc kép bao quanh.
        """;

    public const string SuggestSystemPrompt = """
        Bạn là biên tập viên hình ảnh. Từ nội dung bài viết hoặc sản phẩm được cung cấp, đề xuất MỘT ảnh phù hợp.
        - prompt: tiếng Việt, 2–4 câu tả ảnh cần tạo (chủ thể, bối cảnh, bố cục, ánh sáng, phong cách), hợp với mục đích được nêu. Không yêu cầu chữ/logo trong ảnh. Không tên người thật, nhân vật có bản quyền, thương hiệu.
        - alt: câu mô tả ảnh cho người khiếm thị, tối đa 125 ký tự.
        - caption: chú thích ngắn dưới ảnh, tối đa 100 ký tự.
        Chỉ trả về MỘT đối tượng JSON, không markdown: {"prompt": "...", "alt": "...", "caption": "..."}
        """;

    private static IEnumerable<ImagePromptTemplate> Defaults()
    {
        // Ảnh bìa bài viết
        yield return T("Ảnh báo chí tả thực", "Tin tức & bài viết", ImagePurpose.PostCover, "16:9",
            "Ảnh chụp kiểu phóng sự, chân thực, hợp bài tin tức và phân tích.",
            "Ảnh chụp phong cách báo chí về {chu_de}, bối cảnh đời thực ở Việt Nam, ánh sáng tự nhiên, màu sắc trung thực, bố cục ngang có chủ thể rõ ở một phần ba khung hình, hậu cảnh hơi mờ.");

        yield return T("Minh hoạ phẳng (flat)", "Tin tức & bài viết", ImagePurpose.PostCover, "16:9",
            "Minh hoạ vector phẳng, gọn, hợp bài giải thích, hướng dẫn, công nghệ.",
            "Minh hoạ vector phẳng về {chu_de}, hình khối đơn giản, bảng màu hài hoà 4–5 màu, nền sạch, nhiều khoảng trống, phong cách biên tập hiện đại, không có chữ.");

        yield return T("Ảnh khái niệm tối giản", "Tin tức & bài viết", ImagePurpose.PostCover, "16:9",
            "Một vật thể mang tính biểu tượng trên nền trơn — hợp bài kinh tế, xã hội, quan điểm.",
            "Ảnh khái niệm tối giản thể hiện {chu_de}: một vật thể mang tính biểu tượng đặt giữa nền màu trơn, ánh sáng studio mềm, bóng đổ nhẹ, bố cục cân đối, nhiều khoảng trống.");

        yield return T("Toàn cảnh đời sống Việt", "Tin tức & bài viết", ImagePurpose.PostCover, "16:9",
            "Góc rộng đường phố, chợ, làng quê — hợp bài du lịch, văn hoá, đời sống.",
            "Ảnh góc rộng về {chu_de} trong khung cảnh đời sống Việt Nam, giờ vàng buổi chiều, màu ấm, chiều sâu rõ với tiền cảnh và hậu cảnh, cảm giác chân thực và gần gũi.");

        // Ảnh trong bài
        yield return T("Minh hoạ bước hướng dẫn", "Tin tức & bài viết", ImagePurpose.PostInline, "4:3",
            "Ảnh cận cảnh thao tác tay — hợp bài hướng dẫn từng bước.",
            "Ảnh cận cảnh đôi tay đang thực hiện một bước trong {chu_de}, góc chụp từ trên xuống chéo 45 độ, bàn làm việc gọn gàng, ánh sáng cửa sổ mềm, hậu cảnh mờ.");

        yield return T("Sơ đồ ý tưởng isometric", "Tin tức & bài viết", ImagePurpose.PostInline, "4:3",
            "Minh hoạ 3D isometric, không chữ — hợp giải thích quy trình, hệ thống.",
            "Minh hoạ 3D isometric về {chu_de}, các khối và biểu tượng nối với nhau bằng mũi tên, màu pastel, nền trắng, bóng mềm, không có chữ hay số.");

        yield return T("Cận cảnh chi tiết", "Tin tức & bài viết", ImagePurpose.PostInline, "3:2",
            "Macro một chi tiết đắt giá — hợp bài ẩm thực, thủ công, sản phẩm.",
            "Ảnh macro cận cảnh chi tiết đặc trưng của {chu_de}, độ sâu trường ảnh nông, kết cấu bề mặt rõ nét, ánh sáng bên hông làm nổi khối.");

        // Sản phẩm chính
        yield return T("Studio nền trắng", "Sản phẩm", ImagePurpose.ProductMain, "1:1",
            "Ảnh sản phẩm chuẩn sàn thương mại điện tử.",
            "Ảnh sản phẩm {san_pham} đặt chính giữa trên nền trắng tinh, ánh sáng studio ba điểm, bóng đổ mềm dưới đáy, sắc nét, màu trung thực, không có vật thể khác.");

        yield return T("Nền pastel tối giản", "Sản phẩm", ImagePurpose.ProductMain, "1:1",
            "Nền màu trơn pastel, hiện đại — hợp mỹ phẩm, đồ dùng, phụ kiện.",
            "Ảnh sản phẩm {san_pham} trên nền màu pastel trơn, đặt nghiêng nhẹ, ánh sáng mềm tạo bóng dài, phong cách tối giản hiện đại, nhiều khoảng trống quanh sản phẩm.");

        yield return T("Sản phẩm trên bục (podium)", "Sản phẩm", ImagePurpose.ProductMain, "3:4",
            "Bục hình khối + đạo cụ hình học — hợp ra mắt sản phẩm, quà tặng.",
            "Ảnh sản phẩm {san_pham} đặt trên bục hình trụ màu kem, xung quanh vài khối hình học và lá cây xanh, ánh sáng studio ấm, bóng mềm, bố cục cân đối.");

        yield return T("Flat lay từ trên xuống", "Sản phẩm", ImagePurpose.ProductMain, "1:1",
            "Bày biện nhìn thẳng từ trên xuống — hợp thời trang, văn phòng phẩm, đồ ăn đóng gói.",
            "Ảnh flat lay nhìn thẳng từ trên xuống, {san_pham} ở trung tâm, xung quanh là vài đạo cụ liên quan sắp xếp gọn gàng, nền vải lanh sáng màu, ánh sáng tự nhiên đều.");

        // Sản phẩm bối cảnh
        yield return T("Lifestyle trên bàn gỗ", "Sản phẩm", ImagePurpose.ProductGallery, "4:3",
            "Sản phẩm trong không gian sống ấm cúng.",
            "Ảnh lifestyle {san_pham} đặt trên bàn gỗ sáng màu cạnh cửa sổ, vài vật dụng đời thường làm hậu cảnh mờ, nắng sớm chiếu xiên, cảm giác ấm áp và gần gũi.");

        yield return T("Trong tay người dùng", "Sản phẩm", ImagePurpose.ProductGallery, "3:4",
            "Bàn tay cầm sản phẩm — cho thấy kích thước thật.",
            "Ảnh cận cảnh bàn tay cầm {san_pham}, hậu cảnh ngoài trời mờ nhẹ, ánh sáng tự nhiên, không lộ mặt người, tập trung vào sản phẩm và kích thước thật.");

        yield return T("Không khí Tết", "Sự kiện & khuyến mãi", ImagePurpose.ProductGallery, "1:1",
            "Bối cảnh Tết Nguyên đán: mai, đào, bánh chưng, màu đỏ vàng.",
            "Ảnh {san_pham} trong không gian Tết Nguyên đán Việt Nam: cành mai vàng, bao lì xì đỏ, khay mứt, ánh sáng ấm, màu đỏ và vàng chủ đạo, cảm giác sum vầy.");

        yield return T("Đêm Trung thu", "Sự kiện & khuyến mãi", ImagePurpose.ProductGallery, "1:1",
            "Đèn lồng, bánh trung thu, ánh trăng — cho chiến dịch mùa thu.",
            "Ảnh {san_pham} bên đèn lồng giấy và bánh trung thu, ánh trăng và đèn vàng ấm, hậu cảnh tối mờ có đốm sáng bokeh, không khí đêm Trung thu Việt Nam.");

        // Banner, mạng xã hội
        yield return T("Banner chừa chỗ chữ", "Sự kiện & khuyến mãi", ImagePurpose.Banner, "16:9",
            "Chủ thể lệch phải, nửa trái trống để đặt chữ bằng HTML.",
            "Ảnh banner ngang về {chu_de}, chủ thể đặt lệch sang phần ba bên phải, nửa bên trái là nền màu trơn nhẹ để đặt chữ, ánh sáng sáng sủa, màu tươi.");

        yield return T("Ảnh vuông bắt mắt cho mạng xã hội", "Mạng xã hội", ImagePurpose.Social, "1:1",
            "Màu tương phản, chủ thể to — dừng tay người lướt feed.",
            "Ảnh vuông về {chu_de}, chủ thể lớn chiếm phần lớn khung hình, màu sắc tương phản mạnh, nền đơn giản, ánh sáng rực rỡ, phong cách trẻ trung năng động.");
    }

    private static ImagePromptTemplate T(string title, string category, ImagePurpose purpose, string aspect, string description, string prompt) => new()
    {
        Title = title,
        Category = category,
        Purpose = purpose,
        AspectRatio = aspect,
        Description = description,
        Prompt = prompt,
    };
}
