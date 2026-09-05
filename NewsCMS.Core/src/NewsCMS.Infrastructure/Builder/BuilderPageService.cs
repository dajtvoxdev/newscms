using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Builder;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// CRUD + publish/revision cho trang builder. HTML sanitize bằng ContentSanitizer.SanitizeBuilder
/// trước khi lưu CompiledHtml. CustomJs giữ nguyên (không nằm trong HTML, không qua sanitizer).
/// Mọi query đều scope theo site hiện tại qua global query filter của AppDbContext.
/// </summary>
public sealed class BuilderPageService : IBuilderPageService
{
    private readonly AppDbContext _db;
    private readonly ContentSanitizer _sanitizer;
    private readonly IRouteRegistry _routeRegistry;

    public BuilderPageService(AppDbContext db, ContentSanitizer sanitizer, IRouteRegistry routeRegistry)
    {
        _db = db;
        _sanitizer = sanitizer;
        _routeRegistry = routeRegistry;
    }

    public async Task<Result<BuilderPageDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return Result<BuilderPageDto>.Failure("Trang không tồn tại.");
        return Result<BuilderPageDto>.Success(Map(page));
    }

    public async Task<PagedList<BuilderPageDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = _db.Pages.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(p => p.Title.Contains(s) || p.Slug.Contains(s));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new BuilderPageDto(
                p.Id, p.Title, p.Slug, p.BuilderJson, p.CompiledCss, p.CustomCss, p.CustomJs,
                p.Kind.ToString(), p.Status.ToString(), p.Version, p.CssDirty, p.PublishedAt,
                null, p.LayoutId, p.ParentPageId, p.IsDefaultTemplate))
            .ToListAsync(ct);

        return new PagedList<BuilderPageDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task<Result<BuilderPageDto>> CreateAsync(BuilderPageSaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<BuilderPageDto>.Failure("Tiêu đề không được để trống.");

        var storedSlug = NormalizeSlugForStorage(request.Slug);

        // Homepage (rỗng/home/index) → chỉ cho 1 trang chủ mỗi site.
        if (IsHomeSlug(storedSlug))
        {
            var homeExists = await _db.Pages.AnyAsync(
                p => p.Slug == "" || p.Slug.ToLower() == "home" || p.Slug.ToLower() == "index", ct);
            if (homeExists)
                return Result<BuilderPageDto>.Failure("Đã có trang chủ (/). Mỗi site chỉ có một trang chủ — hãy sửa trang chủ hiện có thay vì tạo thêm.");
        }
        else
        {
            var slugExists = await _db.Pages.AnyAsync(p => p.Slug.ToLower() == storedSlug.ToLower(), ct);
            if (slugExists)
                return Result<BuilderPageDto>.Failure($"Slug '{storedSlug}' đã tồn tại.");
        }

        Guid? parentPageId = request.ParentPageId == Guid.Empty ? null : request.ParentPageId;
        if (parentPageId.HasValue)
        {
            var parentExists = await _db.Pages.AnyAsync(p => p.Id == parentPageId.Value, ct);
            if (!parentExists)
                return Result<BuilderPageDto>.Failure("Trang cha không tồn tại.");
        }

        var page = new Page
        {
            Title = request.Title.Trim(),
            Slug = storedSlug,
            Content = string.Empty, // legacy field — giữ rỗng cho tương thích
            BuilderJson = request.BuilderJson,
            CompiledHtml = _sanitizer.SanitizeBuilder(request.CompiledHtml),
            CompiledCss = request.CompiledCss,
            CustomCss = request.CustomCss,
            CustomJs = request.CustomJs,
            Kind = ParseKind(request.Kind),
            LayoutId = request.LayoutId == Guid.Empty ? null : request.LayoutId,
            ParentPageId = parentPageId,
            IsDefaultTemplate = request.IsDefaultTemplate ?? false,
            Status = BuilderPageStatus.Draft,
            Version = 0,
            CssDirty = false,
            IsPublished = false
        };

        _db.Pages.Add(page);
        await _db.SaveChangesAsync(ct);

        return Result<BuilderPageDto>.Success(Map(page));
    }

    public async Task<Result<BuilderPageDto>> UpdateAsync(Guid id, BuilderPageSaveRequest request, CancellationToken ct = default)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return Result<BuilderPageDto>.Failure("Trang không tồn tại.");

        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<BuilderPageDto>.Failure("Tiêu đề không được để trống.");

        var storedSlug = NormalizeSlugForStorage(request.Slug);

        // Kiểm tra slug trùng (trừ chính page đang sửa). Homepage chỉ cho 1.
        if (!string.Equals(NormalizeSlugForStorage(page.Slug), storedSlug, StringComparison.OrdinalIgnoreCase))
        {
            if (IsHomeSlug(storedSlug))
            {
                var homeExists = await _db.Pages.AnyAsync(
                    p => p.Id != id && (p.Slug == "" || p.Slug.ToLower() == "home" || p.Slug.ToLower() == "index"), ct);
                if (homeExists)
                    return Result<BuilderPageDto>.Failure("Đã có trang chủ (/). Mỗi site chỉ có một trang chủ.");
            }
            else
            {
                var slugExists = await _db.Pages.AnyAsync(
                    p => p.Id != id && p.Slug.ToLower() == storedSlug.ToLower(), ct);
                if (slugExists)
                    return Result<BuilderPageDto>.Failure($"Slug '{storedSlug}' đã tồn tại.");
            }
        }

        var incomingHtml = _sanitizer.SanitizeBuilder(request.CompiledHtml);

        // Lưới an toàn cuối: chặn ghi đè trang đang có nội dung bằng HTML rỗng. Bảo vệ cả đường
        // API/MCP, không chỉ UI builder.
        if (IsEffectivelyEmptyHtml(incomingHtml) && !IsEffectivelyEmptyHtml(page.CompiledHtml))
            return Result<BuilderPageDto>.Failure(
                "Từ chối lưu: nội dung gửi lên rỗng nhưng trang hiện đang có nội dung. " +
                "Hãy tải lại builder trước khi sửa.");

        // Snapshot bản cũ trước khi HTML bị thay — trước đây chỉ Publish và site_apply mới tạo
        // revision, nên một lần lưu nháp sai là không có đường về.
        if (!string.Equals(page.CompiledHtml, incomingHtml, StringComparison.Ordinal))
        {
            _db.PageRevisions.Add(new PageRevision
            {
                PageId = page.Id,
                Version = await NextRevisionVersionAsync(page.Id, page.Version, ct),
                BuilderJson = page.BuilderJson,
                CompiledHtml = page.CompiledHtml,
                CompiledCss = page.CompiledCss,
                CustomCss = page.CustomCss,
                CustomJs = page.CustomJs,
                Note = "auto: bản trước khi lưu từ builder"
            });
        }

        page.Title = request.Title.Trim();
        page.Slug = storedSlug;
        page.BuilderJson = request.BuilderJson;
        page.CompiledHtml = incomingHtml;
        page.CompiledCss = request.CompiledCss;
        page.CustomCss = request.CustomCss;
        page.CustomJs = request.CustomJs;
        page.Kind = ParseKind(request.Kind);
        if (request.LayoutId is { } layoutId)
            page.LayoutId = layoutId == Guid.Empty ? null : layoutId;

        if (request.ParentPageId.HasValue)
        {
            if (request.ParentPageId.Value == Guid.Empty)
            {
                page.ParentPageId = null;
            }
            else
            {
                var targetParentId = request.ParentPageId.Value;
                if (targetParentId == id)
                    return Result<BuilderPageDto>.Failure("Trang không thể làm cha của chính nó.");

                // Kiểm tra vòng lặp phân cấp (A -> B -> A)
                var current = targetParentId;
                var seen = new HashSet<Guid> { id };
                for (int depth = 0; depth < 10; depth++)
                {
                    if (!seen.Add(current))
                        return Result<BuilderPageDto>.Failure("Phát hiện vòng lặp phân cấp cha-con (A → B → A).");

                    var nextParent = await _db.Pages.AsNoTracking()
                        .Where(p => p.Id == current)
                        .Select(p => p.ParentPageId)
                        .FirstOrDefaultAsync(ct);

                    if (nextParent is null || nextParent == Guid.Empty)
                        break;

                    current = nextParent.Value;
                }

                page.ParentPageId = targetParentId;
            }
        }

        if (request.IsDefaultTemplate.HasValue)
        {
            page.IsDefaultTemplate = request.IsDefaultTemplate.Value;
        }

        page.UpdatedAt = DateTime.UtcNow;

        // Lưu qua builder nghĩa là nc-tailwind.js đã compile utility vào CompiledCss, nên trang
        // không còn thiếu CSS. Trang tạo qua MCP/API để CssDirty = true cho tới lần lưu này.
        if (!string.IsNullOrWhiteSpace(request.CompiledCss))
            page.CssDirty = false;

        await _db.SaveChangesAsync(ct);

        // Đồng bộ route nếu slug đổi (tạo redirect 301 từ path cũ).
        await _routeRegistry.SyncPageRouteAsync(id, ct);

        return Result<BuilderPageDto>.Success(Map(page));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return Result.Failure("Trang không tồn tại.");

        page.IsDeleted = true;
        page.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _routeRegistry.Invalidate();
        return Result.Success();
    }

    public async Task<Result<BuilderPageDto>> PublishAsync(Guid id, BuilderPagePublishRequest request, CancellationToken ct = default)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return Result<BuilderPageDto>.Failure("Trang không tồn tại.");

        page.Version = await NextRevisionVersionAsync(page.Id, page.Version + 1, ct);
        page.Status = BuilderPageStatus.Published;
        page.IsPublished = true;
        page.PublishedAt = DateTime.UtcNow;
        page.UpdatedAt = DateTime.UtcNow;

        // Snapshot revision trước khi lưu.
        _db.PageRevisions.Add(new PageRevision
        {
            PageId = page.Id,
            Version = page.Version,
            BuilderJson = page.BuilderJson,
            CompiledHtml = page.CompiledHtml,
            CompiledCss = page.CompiledCss,
            CustomCss = page.CustomCss,
            CustomJs = page.CustomJs,
            Note = request?.Note
        });

        await _db.SaveChangesAsync(ct);
        await _routeRegistry.SyncPageRouteAsync(id, ct);

        return Result<BuilderPageDto>.Success(Map(page));
    }

    public async Task<IReadOnlyList<PageRevisionDto>> GetRevisionsAsync(Guid pageId, CancellationToken ct = default)
    {
        return await _db.PageRevisions.AsNoTracking()
            .Where(r => r.PageId == pageId)
            .OrderByDescending(r => r.Version)
            .Select(r => new PageRevisionDto(r.Id, r.Version, r.Note, r.CreatedAt, r.CreatedBy))
            .ToListAsync(ct);
    }

    public async Task<Result<BuilderPageDto>> RestoreRevisionAsync(Guid pageId, int version, CancellationToken ct = default)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return Result<BuilderPageDto>.Failure("Trang không tồn tại.");

        var revision = await _db.PageRevisions
            .FirstOrDefaultAsync(r => r.PageId == pageId && r.Version == version, ct);
        if (revision is null) return Result<BuilderPageDto>.Failure($"Revision v{version} không tồn tại.");

        page.BuilderJson = revision.BuilderJson;
        page.CompiledHtml = revision.CompiledHtml;
        page.CompiledCss = revision.CompiledCss;
        page.CustomCss = revision.CustomCss;
        page.CustomJs = revision.CustomJs;
        page.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result<BuilderPageDto>.Success(Map(page));
    }

    /// <summary>
    /// Số version trống kế tiếp cho PageRevisions của một trang.
    ///
    /// Vì sao cần: IX_PageRevisions_PageId_Version là unique, nhưng snapshot khi lưu nháp gắn
    /// Version = page.Version (version của bản CŨ) còn publish gắn version mới. Lưu hai lần liên
    /// tiếp, hoặc lưu ngay sau khi publish, sẽ đâm vào version đã có và ném DbUpdateException —
    /// builder chỉ hiện "Lỗi không xác định". Lấy max hiện có + 1 khi version mong muốn đã bị chiếm.
    /// </summary>
    private async Task<int> NextRevisionVersionAsync(Guid pageId, int preferred, CancellationToken ct)
    {
        var max = await _db.PageRevisions.AsNoTracking()
            .Where(r => r.PageId == pageId)
            .Select(r => (int?)r.Version)
            .MaxAsync(ct) ?? 0;
        return preferred > max ? preferred : max + 1;
    }

    private static BuilderPageDto Map(Page p) => new(
        p.Id, p.Title, p.Slug, p.BuilderJson, p.CompiledCss, p.CustomCss, p.CustomJs,
        p.Kind.ToString(), p.Status.ToString(), p.Version, p.CssDirty, p.PublishedAt,
        p.CompiledHtml, p.LayoutId, p.ParentPageId, p.IsDefaultTemplate);

    /// <summary>
    /// HTML không còn nội dung nào: bỏ hết thẻ thì không còn chữ, và cũng không còn thẻ mang nội
    /// dung tự thân (ảnh/svg/video) hay placeholder khối động.
    ///
    /// Vì sao cần: GrapesJS trả về "&lt;body&gt;&lt;/body&gt;" khi canvas trắng. Trang dựng qua MCP
    /// không có BuilderJson nên trước đây canvas mở lên là trắng — một cú Lưu sẽ ghi chuỗi rỗng đó
    /// lên CompiledHtml và xoá sạch trang.
    /// </summary>
    internal static bool IsEffectivelyEmptyHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return true;

        if (html.Contains("data-nc-block", StringComparison.OrdinalIgnoreCase)) return false;
        if (Regex.IsMatch(html, @"<(img|svg|iframe|video|audio|canvas|input|hr|picture|source)\b",
                RegexOptions.IgnoreCase)) return false;

        var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " "));
        return string.IsNullOrWhiteSpace(text);
    }

    private static string NormalizeSlugForStorage(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return "";
        slug = slug.Trim();
        if (slug.Equals("home", StringComparison.OrdinalIgnoreCase)
            || slug.Equals("index", StringComparison.OrdinalIgnoreCase))
            return "";
        return slug;
    }

    private static bool IsHomeSlug(string? slug) =>
        string.IsNullOrWhiteSpace(slug)
        || slug!.Equals("home", StringComparison.OrdinalIgnoreCase)
        || slug.Equals("index", StringComparison.OrdinalIgnoreCase);

    private static PageKind ParseKind(string? kind) =>
        Enum.TryParse<PageKind>(kind, ignoreCase: true, out var k) ? k : PageKind.Static;
}
