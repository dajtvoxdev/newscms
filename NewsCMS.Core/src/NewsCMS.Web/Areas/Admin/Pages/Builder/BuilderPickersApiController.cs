using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// API tra cứu dữ liệu cho các panel của builder: chuyên mục, bài viết, sản phẩm, vị trí banner,
/// và danh sách shell layout (kèm HTML để xem ngữ cảnh header/footer trong canvas).
///
/// Chỉ đọc nên dùng quyền thấp nhất (Builder.PageView). Riêng shell layout cố tình KHÔNG đòi
/// Builder.LayoutManage: người chỉ được sửa trang vẫn cần thấy shell bọc quanh trang của mình.
/// </summary>
[Authorize(Permissions.Builder.PageView)]
[Route("admin/api/builder/pickers")]
public class BuilderPickersApiController : ControllerBase
{
    /// <summary>Trần số bản ghi mỗi truy vấn picker — panel là danh sách chọn, không phải bảng dữ liệu.</summary>
    private const int MaxTake = 50;

    private readonly AppDbContext _db;

    public BuilderPickersApiController(AppDbContext db) => _db = db;

    /// <summary>GET ?type=post|product — danh sách chuyên mục {slug, name} sắp theo Order.</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> Categories([FromQuery] string type = "post", CancellationToken ct = default)
    {
        if (type.Equals("product", StringComparison.OrdinalIgnoreCase))
        {
            var productCats = await _db.ProductCategories.AsNoTracking()
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.Order).ThenBy(c => c.Name)
                .Select(c => new { c.Slug, c.Name })
                .ToListAsync(ct);
            return Ok(productCats);
        }

        var postCats = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.Type == CategoryType.Post)
            .OrderBy(c => c.Order).ThenBy(c => c.Name)
            .Select(c => new { c.Slug, c.Name })
            .ToListAsync(ct);
        return Ok(postCats);
    }

    /// <summary>GET ?search= — bài viết đã publish, cho trait chọn bài cụ thể.</summary>
    [HttpGet("posts")]
    public async Task<IActionResult> Posts(
        [FromQuery] string? search = null, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        var query = _db.Posts.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == PostStatus.Published);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(p => p.Title.Contains(s) || p.Slug.Contains(s));
        }

        var posts = await query
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .Take(Math.Clamp(take, 1, MaxTake))
            .Select(p => new { p.Id, p.Title, p.Slug, CategorySlug = p.Category.Slug })
            .ToListAsync(ct);
        return Ok(posts);
    }

    /// <summary>GET ?search= — sản phẩm đã publish, cho trait chọn sản phẩm cụ thể.</summary>
    [HttpGet("products")]
    public async Task<IActionResult> Products(
        [FromQuery] string? search = null, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        var query = _db.Products.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == ProductStatus.Published);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(p => p.Name.Contains(s) || p.Slug.Contains(s));
        }

        var products = await query
            .OrderBy(p => p.SortOrder).ThenByDescending(p => p.CreatedAt)
            .Take(Math.Clamp(take, 1, MaxTake))
            .Select(p => new { p.Id, p.Name, p.Slug, CategorySlug = p.ProductCategory.Slug })
            .ToListAsync(ct);
        return Ok(products);
    }

    /// <summary>
    /// GET — các giá trị Position đang thực sự có banner. Trước đây trait banner-slider là ô text
    /// tự do, gõ sai một ký tự là block render rỗng mà không báo gì.
    /// </summary>
    [HttpGet("banner-positions")]
    public async Task<IActionResult> BannerPositions(CancellationToken ct = default)
    {
        var positions = await _db.Banners.AsNoTracking()
            .Where(b => b.Position != null && b.Position != "")
            .GroupBy(b => b.Position)
            .Select(g => new { Position = g.Key, Count = g.Count() })
            .OrderBy(x => x.Position)
            .ToListAsync(ct);
        return Ok(positions);
    }

    /// <summary>GET — thẻ (tag) {slug, name} cho trait chọn nhiều thẻ.</summary>
    [HttpGet("tags")]
    public async Task<IActionResult> Tags(CancellationToken ct = default)
    {
        var tags = await _db.Tags.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new { t.Slug, t.Name })
            .ToListAsync(ct);
        return Ok(tags);
    }

    /// <summary>
    /// GET — thư mục media, nhãn là đường dẫn đầy đủ ("Ảnh sản phẩm / Cà phê") để hai thư mục trùng
    /// tên ở hai nhánh khác nhau không nhìn giống hệt nhau trong dropdown.
    /// </summary>
    [HttpGet("media-folders")]
    public async Task<IActionResult> MediaFolders(CancellationToken ct = default)
    {
        var rows = await _db.MediaFolders.AsNoTracking()
            .Select(f => new { f.Id, f.Name, f.ParentId })
            .ToListAsync(ct);

        var byId = rows.ToDictionary(r => r.Id);

        string PathOf(Guid id)
        {
            var parts = new List<string>();
            var cur = id;
            // Chặn vòng lặp cha-con nếu dữ liệu hỏng: mỗi id chỉ đi qua một lần.
            var seen = new HashSet<Guid>();
            while (byId.TryGetValue(cur, out var node) && seen.Add(cur))
            {
                parts.Insert(0, node.Name);
                if (node.ParentId is not { } parent) break;
                cur = parent;
            }
            return string.Join(" / ", parts);
        }

        var folders = rows
            .Select(r => new { id = r.Id, name = PathOf(r.Id) })
            .OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Ok(folders);
    }

    /// <summary>
    /// GET ?type=post|product|media&amp;q=&amp;ids= — nguồn dữ liệu chung cho trait <c>nc-item-picker</c>.
    ///
    /// Một endpoint cho cả ba loại vì client cần đúng hai việc giống nhau ở mọi loại: tìm theo từ khoá,
    /// và tra lại nhãn của các id đã lưu trong props (nếu chỉ có <c>q</c> thì mở panel lần sau sẽ thấy
    /// một dãy Guid trần). Trả về hình dạng thống nhất {id, label, sub, thumb}.
    /// </summary>
    [HttpGet("items")]
    public async Task<IActionResult> Items(
        [FromQuery] string type = "post",
        [FromQuery] string? q = null,
        [FromQuery] string? ids = null,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        var wanted = ParseIds(ids);
        var limit = wanted.Count > 0 ? Math.Clamp(wanted.Count, 1, MaxTake) : Math.Clamp(take, 1, MaxTake);
        var term = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var items = type.ToLowerInvariant() switch
        {
            "product" => await ProductItemsAsync(term, wanted, limit, ct),
            "media" => await MediaItemsAsync(term, wanted, limit, ct),
            _ => await PostItemsAsync(term, wanted, limit, ct)
        };

        // Tra theo ids: giữ đúng thứ tự người dùng đã kéo, vì SQL IN không bảo toàn thứ tự.
        if (wanted.Count > 0)
        {
            var rank = new Dictionary<Guid, int>(wanted.Count);
            for (var i = 0; i < wanted.Count; i++) rank.TryAdd(wanted[i], i);
            items = items.OrderBy(x => rank.TryGetValue(x.Id, out var i) ? i : int.MaxValue).ToList();
        }

        return Ok(items);
    }

    private async Task<List<PickerItem>> PostItemsAsync(
        string? term, IReadOnlyList<Guid> ids, int take, CancellationToken ct)
    {
        var query = _db.Posts.AsNoTracking().Where(p => !p.IsDeleted);
        if (ids.Count > 0) query = query.Where(p => ids.Contains(p.Id));
        else
        {
            query = query.Where(p => p.Status == PostStatus.Published);
            if (term is not null) query = query.Where(p => p.Title.Contains(term) || p.Slug.Contains(term));
            query = query.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt);
        }

        return await query.Take(take)
            .Select(p => new PickerItem(p.Id, p.Title, p.Category.Name,
                p.FeaturedImage != null ? p.FeaturedImage.FilePath : null))
            .ToListAsync(ct);
    }

    private async Task<List<PickerItem>> ProductItemsAsync(
        string? term, IReadOnlyList<Guid> ids, int take, CancellationToken ct)
    {
        var query = _db.Products.AsNoTracking().Where(p => !p.IsDeleted);
        if (ids.Count > 0) query = query.Where(p => ids.Contains(p.Id));
        else
        {
            query = query.Where(p => p.Status == ProductStatus.Published);
            if (term is not null) query = query.Where(p => p.Name.Contains(term) || p.Slug.Contains(term));
            query = query.OrderBy(p => p.SortOrder).ThenByDescending(p => p.CreatedAt);
        }

        return await query.Take(take)
            .Select(p => new PickerItem(p.Id, p.Name, p.ProductCategory.Name,
                p.Thumbnail != null ? p.Thumbnail.FilePath : null))
            .ToListAsync(ct);
    }

    private async Task<List<PickerItem>> MediaItemsAsync(
        string? term, IReadOnlyList<Guid> ids, int take, CancellationToken ct)
    {
        var query = _db.Medias.AsNoTracking().Where(m => !m.IsDeleted && m.Kind == "image");
        if (ids.Count > 0) query = query.Where(m => ids.Contains(m.Id));
        else
        {
            if (term is not null)
                query = query.Where(m => m.FileName.Contains(term)
                    || (m.Title != null && m.Title.Contains(term))
                    || (m.AltText != null && m.AltText.Contains(term)));
            query = query.OrderByDescending(m => m.CreatedAt);
        }

        return await query.Take(take)
            .Select(m => new PickerItem(
                m.Id,
                m.Title != null && m.Title != "" ? m.Title : m.FileName,
                m.Folder != null ? m.Folder.Name : null,
                m.FilePath))
            .ToListAsync(ct);
    }

    /// <summary>Danh sách id dạng CSV; bỏ qua phần tử không parse được thay vì trả 400 cả lô.</summary>
    private static List<Guid> ParseIds(string? csv)
    {
        var result = new List<Guid>();
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Guid.TryParse(part, out var id) && id != Guid.Empty && !result.Contains(id))
                result.Add(id);
        return result;
    }

    /// <param name="Sub">Dòng phụ (chuyên mục / thư mục) để phân biệt hai bản ghi trùng tên.</param>
    public sealed record PickerItem(Guid Id, string Label, string? Sub, string? Thumb);

    /// <summary>GET — shell layout của site, cho dropdown chọn shell trên topbar builder.</summary>
    [HttpGet("layouts")]
    public async Task<IActionResult> Layouts(CancellationToken ct = default)
    {
        var layouts = await _db.SiteLayouts.AsNoTracking()
            .Where(l => !l.IsDeleted && l.Kind == LayoutKind.Shell)
            .OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name)
            .Select(l => new { l.Id, l.Key, l.Name, l.IsDefault })
            .ToListAsync(ct);
        return Ok(layouts);
    }

    /// <summary>
    /// GET — HTML+CSS của một shell để canvas dựng ngữ cảnh header/footer quanh nội dung trang.
    /// id rỗng/Guid.Empty → shell mặc định, khớp cách <c>PageRenderer.ComposeAsync</c> chọn layout.
    /// </summary>
    [HttpGet("layouts/{id}/html")]
    public async Task<IActionResult> LayoutHtml(string id, CancellationToken ct = default)
    {
        var layout = Guid.TryParse(id, out var layoutId) && layoutId != Guid.Empty
            ? await _db.SiteLayouts.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == layoutId && !l.IsDeleted, ct)
            : await _db.SiteLayouts.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Kind == LayoutKind.Shell && l.IsDefault && !l.IsDeleted, ct);

        if (layout is null) return Ok(new { compiledHtml = (string?)null, compiledCss = (string?)null });

        return Ok(new
        {
            id = layout.Id,
            name = layout.Name,
            compiledHtml = layout.CompiledHtml,
            compiledCss = layout.CompiledCss
        });
    }
}
