using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Persistence.Seeding;

/// <summary>
/// Nạp prompt khởi đầu. Sprint 1 chỉ cần một bản: negative prompt toàn cục.
/// </summary>
/// <remarks>
/// Các prompt của bước 2 (đạo diễn) và bước 9 (QC bằng LLM) chưa nạp ở đây vì hai bước đó không
/// chạy trong Sprint 1. Nạp sẵn prompt cho một bước chưa có mã sẽ tạo ấn tượng sai rằng bước đó
/// đã hoạt động.
/// </remarks>
public sealed class PromptTemplateSeeder
{
    /// <summary>Code của negative prompt toàn cục. Trùng với giá trị seed của <c>GlobalNegativePromptCode</c>.</summary>
    public const string GlobalNegativeCode = "global.negative";

    /// <summary>
    /// Negative prompt toàn cục (D4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Vì sao nửa đầu toàn là chữ nghĩa:</b> mọi model video hiện tại đều vẽ sai dấu tiếng Việt —
    /// "khuyến mãi" ra thành chuỗi ký tự gần giống mà không phải tiếng Việt, và không có cách nào
    /// sửa bằng prompt. Nên MỌI chữ trên màn hình đều do FFmpeg vẽ ở bước 8, còn model bị cấm vẽ
    /// chữ. Đây là lớp chặn thứ nhất; lớp thứ hai là bước 9 QC kiểm tra khung hình.
    /// </para>
    /// <para>
    /// <b>Vì sao viết bằng tiếng Anh:</b> negative prompt đi thẳng vào model, và mọi model ở đây
    /// đều được huấn luyện chủ yếu bằng tiếng Anh. Dịch phần này sang tiếng Việt là làm nó yếu đi.
    /// </para>
    /// </remarks>
    public const string GlobalNegativeContent =
        "text, letters, words, captions, subtitles, on-screen typography, written signage, " +
        "watermark, logo, brand mark, numbers on screen, user interface overlay, " +
        "distorted hands, extra fingers, malformed limbs, deformed face, " +
        "warped product label, morphing objects, flickering, jitter, " +
        "low resolution, blurry, oversaturated, jpeg artifacts";

    private readonly AdVideoDbContext _db;
    private readonly ILogger<PromptTemplateSeeder> _logger;

    public PromptTemplateSeeder(AdVideoDbContext db, ILogger<PromptTemplateSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Nạp prompt còn thiếu. Trả về số bản vừa thêm.</summary>
    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        int added = 0;

        foreach (PromptSeed seed in Definitions)
        {
            // Kiểm tra theo Code chứ không theo (Code, Version): đã có bản nào của code này nghĩa là
            // prompt đó đang được quản lý bằng tay. Seeder chen thêm một bản v1 vào giữa lịch sử
            // phiên bản của người khác thì số hiệu bản không còn nói lên thứ tự thời gian nữa.
            bool exists = await _db.PromptTemplates
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Code == seed.Code, cancellationToken);

            if (exists)
            {
                continue;
            }

            // Bản seed được bật NGAY, khác với AddVersionAsync (tạo ra ở trạng thái tắt): đây là
            // trạng thái ban đầu của hệ thống, không phải một đề xuất chờ duyệt. Không bật thì
            // GetActiveContentAsync rơi về fallback trong mã nguồn và cảnh báo mỗi lần gọi.
            _db.PromptTemplates.Add(new PromptTemplate
            {
                Code = seed.Code,
                Version = 1,
                Kind = seed.Kind,
                Content = seed.Content,
                ChangeNote = seed.ChangeNote,
                IsActive = true,
            });

            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Đã nạp {Count} prompt vào bảng PromptTemplates.", added);
        }

        return added;
    }

    /// <summary>Danh sách prompt khởi đầu. Công khai để test đối chiếu mà không cần DB.</summary>
    public static IReadOnlyList<PromptSeed> Definitions { get; } =
    [
        new(
            GlobalNegativeCode,
            PromptKind.GlobalNegative,
            GlobalNegativeContent,
            "Bản đầu tiên. Cấm model vẽ chữ vì không model nào vẽ đúng dấu tiếng Việt (D4)."),
    ];

    /// <summary>Một dòng seed prompt.</summary>
    public sealed record PromptSeed(string Code, PromptKind Kind, string Content, string ChangeNote);
}
