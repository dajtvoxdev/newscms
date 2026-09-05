using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// Catalog khối động cho builder: mỗi khối tự mô tả mình qua <see cref="BlockDescriptor"/>, endpoint
/// này chỉ dịch <c>optionsSource</c> thành danh sách thật rồi trả một cục JSON.
///
/// Vì sao gộp: trước đây builder phải gọi 3 lượt pickers (chuyên mục bài, chuyên mục sản phẩm, vị trí
/// banner) rồi tự khai báo lại toàn bộ palette + trait trong <c>dynamicBlockDefs()</c>. Thêm khối mà
/// quên sửa file JS là khối biến mất khỏi builder mà không có lỗi nào báo. Nay palette dựng từ đây.
/// </summary>
[Authorize(Permissions.Builder.PageView)]
[Route("admin/api/builder/blocks")]
public class BuilderBlocksApiController : ControllerBase
{
    /// <summary>Trần cho danh sách options động — dropdown, không phải bảng dữ liệu.</summary>
    private const int MaxOptions = 200;

    private readonly IDynamicBlockRegistry _registry;
    private readonly AppDbContext _db;

    public BuilderBlocksApiController(IDynamicBlockRegistry registry, AppDbContext db)
    {
        _registry = registry;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Catalog(CancellationToken ct = default)
    {
        // Chỉ nạp nguồn nào thực sự có khối dùng tới: site không bán hàng thì không truy vấn
        // ProductCategories, site không có tag thì không quét Tags.
        var needed = _registry.All
            .SelectMany(b => b.Descriptor.Props)
            .Select(p => p.OptionsSource)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var options = new Dictionary<string, List<BlockPropOption>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in needed)
            options[source!] = await LoadOptionsAsync(source!, ct);

        var blocks = _registry.All
            .OrderBy(b => b.Descriptor.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Descriptor.Label, StringComparer.OrdinalIgnoreCase)
            .Select(b => Serialize(b, options))
            .ToList();

        return Ok(new { blocks, options, sitePresets = await LoadSitePresetsAsync(ct) });
    }

    /// <summary>
    /// Preset RIÊNG CỦA SITE đã lưu (Phase 6.4): mỗi cái là một khối kéo-thả có props gắn sẵn, dùng
    /// lại component type của khối gốc qua <c>handler</c> (= data-nc-block trong BuilderJson). Chỉ trả
    /// preset có handler khớp một khối động đang tồn tại — khối bị gỡ khỏi code thì preset cũ vô dụng.
    /// </summary>
    private async Task<List<object>> LoadSitePresetsAsync(CancellationToken ct)
    {
        var known = _registry.All.Select(b => b.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = await _db.BlockDefinitions.AsNoTracking()
            .Where(d => d.Kind == BlockKind.Dynamic && d.DynamicHandler != null && !d.IsDeleted)
            .OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Key, d.Name, d.Category, d.Icon, d.DynamicHandler, d.BuilderJson })
            .ToListAsync(ct);

        return rows
            .Where(r => known.Contains(r.DynamicHandler!))
            .Select(r => (object)new
            {
                id = r.Id,
                key = r.Key,
                handler = r.DynamicHandler,
                label = r.Name,
                category = r.Category,
                icon = r.Icon,
                props = ExtractProps(r.BuilderJson)
            })
            .ToList();
    }

    /// <summary>
    /// Lấy lại props đã lưu từ placeholder <c>data-nc-props="..."</c>. Trả "{}" khi thiếu — client
    /// vẫn kéo được, chỉ là preset rỗng (không tệ hơn khối gốc).
    /// </summary>
    private static string ExtractProps(string? builderJson)
    {
        if (string.IsNullOrEmpty(builderJson)) return "{}";
        var m = System.Text.RegularExpressions.Regex.Match(
            builderJson, "data-nc-props=\"([^\"]*)\"");
        if (!m.Success) return "{}";
        return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    /// <summary>
    /// Props được gửi kèm <c>options</c> đã giải quyết sẵn để client không phải tra chéo — nhưng
    /// vẫn giữ <c>optionsSource</c> để client biết ô nào có thể refresh khi dữ liệu đổi.
    /// </summary>
    private static object Serialize(IDynamicBlock block, IReadOnlyDictionary<string, List<BlockPropOption>> options)
    {
        var d = block.Descriptor;
        return new
        {
            key = block.Key,
            label = d.Label,
            category = d.Category,
            description = d.Description,
            icon = d.IconSvg,
            defaults = d.DefaultPropsJson,
            // Loại nội dung khối dùng được ("product", "post"…); null/rỗng = hợp mọi loại.
            // Palette dựa vào đây để không cho kéo "Giá sản phẩm" vào template bài viết — khối sai
            // loại chỉ im lặng trả rỗng nên không có tín hiệu nào cho người dùng.
            supportedTypes = d.SupportedTypes,
            presets = d.Presets.Select(p => new
            {
                key = p.Key,
                label = p.Label,
                thumb = p.ThumbSvg,
                props = p.PropsJson
            }),
            props = d.Props.Select(p => new
            {
                name = p.Name,
                type = p.Type,
                label = p.Label,
                group = p.Group,
                @default = p.DefaultValue,
                optionsSource = p.OptionsSource,
                itemSource = p.ItemSource,
                min = p.Min,
                max = p.Max,
                placeholder = p.Placeholder,
                hint = p.Hint,
                options = Resolve(p, options).Select(o => new { value = o.Value, name = o.Name })
            })
        };
    }

    private static IReadOnlyList<BlockPropOption> Resolve(
        BlockPropDescriptor prop, IReadOnlyDictionary<string, List<BlockPropOption>> options)
    {
        if (prop.Options is { Count: > 0 }) return prop.Options;
        if (!string.IsNullOrEmpty(prop.OptionsSource) && options.TryGetValue(prop.OptionsSource, out var dyn))
            return dyn;
        return Array.Empty<BlockPropOption>();
    }

    private async Task<List<BlockPropOption>> LoadOptionsAsync(string source, CancellationToken ct) => source switch
    {
        BlockOptionSources.PostCategories => await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.Type == CategoryType.Post)
            .OrderBy(c => c.Order).ThenBy(c => c.Name)
            .Take(MaxOptions)
            .Select(c => new BlockPropOption(c.Slug, c.Name))
            .ToListAsync(ct),

        BlockOptionSources.ProductCategories => await _db.ProductCategories.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Order).ThenBy(c => c.Name)
            .Take(MaxOptions)
            .Select(c => new BlockPropOption(c.Slug, c.Name))
            .ToListAsync(ct),

        // Chỉ vị trí ĐANG có banner: trait cũ là ô text tự do, gõ sai một ký tự là khối rỗng
        // mà không báo gì.
        BlockOptionSources.BannerPositions => await _db.Banners.AsNoTracking()
            .Where(b => b.Position != null && b.Position != "")
            .GroupBy(b => b.Position!)
            .OrderBy(g => g.Key)
            .Take(MaxOptions)
            .Select(g => new BlockPropOption(g.Key, g.Key + " (" + g.Count() + ")"))
            .ToListAsync(ct),

        BlockOptionSources.Tags => await _db.Tags.AsNoTracking()
            .OrderBy(t => t.Name)
            .Take(MaxOptions)
            .Select(t => new BlockPropOption(t.Slug, t.Name))
            .ToListAsync(ct),

        BlockOptionSources.MediaFolders => await LoadMediaFoldersAsync(ct),

        // Chỉ vị trí ĐANG có menu — gõ tay "heder" thì khối im lặng không render, đúng cái bẫy
        // mà banner-positions đã gặp.
        BlockOptionSources.MenuLocations => await _db.Menus.AsNoTracking()
            .Where(m => m.Location != null && m.Location != "")
            .OrderBy(m => m.Location)
            .Take(MaxOptions)
            .Select(m => new BlockPropOption(m.Location, m.Name))
            .ToListAsync(ct),

        _ => []
    };

    /// <summary>
    /// Thư mục media hiển thị theo đường dẫn ("Ảnh sản phẩm / Cà phê") để hai thư mục trùng tên ở
    /// hai nhánh khác nhau không bị nhìn giống hệt nhau trong dropdown.
    /// </summary>
    private async Task<List<BlockPropOption>> LoadMediaFoldersAsync(CancellationToken ct)
    {
        var rows = await _db.MediaFolders.AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new { f.Id, f.Name, f.ParentId })
            .ToListAsync(ct);

        var byId = rows.ToDictionary(r => r.Id);

        string Path(Guid id)
        {
            var parts = new List<string>();
            var cur = id;
            // Chặn vòng lặp cha-con hỏng dữ liệu: mỗi id chỉ đi qua một lần.
            var seen = new HashSet<Guid>();
            while (byId.TryGetValue(cur, out var node) && seen.Add(cur))
            {
                parts.Insert(0, node.Name);
                if (node.ParentId is not { } parent) break;
                cur = parent;
            }
            return string.Join(" / ", parts);
        }

        return rows
            .Select(r => new BlockPropOption(r.Id.ToString(), Path(r.Id)))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxOptions)
            .ToList();
    }
}
