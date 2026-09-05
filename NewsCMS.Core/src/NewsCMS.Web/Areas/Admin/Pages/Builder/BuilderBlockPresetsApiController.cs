using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Domain.Entities.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// Lưu cấu hình HIỆN TẠI của một khối động thành preset RIÊNG CỦA SITE (Phase 6.4).
///
/// Preset = một <see cref="BlockDefinition"/> khối động: <c>Kind=Dynamic</c>, <c>DynamicHandler</c>
/// trỏ về key khối gốc, <c>BuilderJson</c> chứa đúng placeholder <c>data-nc-block</c> +
/// <c>data-nc-props</c> mà builder kéo-thả. Vì sao dùng lại bảng này thay vì bảng mới: renderer
/// công khai đã đọc <c>data-nc-block</c>, và catalog (<see cref="BuilderBlocksApiController"/>) đã
/// có sẵn chỗ ghép khối lưu tay — preset chỉ là một khối lưu tay có props gắn sẵn.
///
/// <c>SiteId</c> = site hiện tại (không phải null/global): nút trên panel ghi rõ "preset của site".
/// </summary>
[Authorize(Permissions.Builder.BlockManage)]
[Route("admin/api/builder/block-presets")]
public class BuilderBlockPresetsApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentSite _currentSite;
    private readonly IDynamicBlockRegistry _registry;

    public BuilderBlockPresetsApiController(AppDbContext db, ICurrentSite currentSite, IDynamicBlockRegistry registry)
    {
        _db = db;
        _currentSite = currentSite;
        _registry = registry;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SavePresetRequest? request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Thiếu key khối hoặc tên preset." });

        var block = _registry.Get(request.Key);
        if (block is null)
            return BadRequest(new { error = $"Không có khối động '{request.Key}' để tạo preset." });

        // Chuẩn hoá props: chuỗi rỗng/không hợp lệ → "{}" để BuilderJson luôn parse được.
        var propsJson = NormalizeProps(request.PropsJson);
        var name = request.Name.Trim();

        // Slug key ổn định cho preset để lần lưu lại cùng tên thì cập nhật, không đẻ trùng.
        var presetKey = $"{request.Key}--preset-{Slug(name)}";
        var siteId = _currentSite.SiteId;

        var existing = await _db.BlockDefinitions
            .FirstOrDefaultAsync(d => d.SiteId == siteId && d.Key == presetKey && !d.IsDeleted, ct);

        var builderJson = BuildPlaceholder(request.Key, propsJson);
        var descriptor = block.Descriptor;

        if (existing is not null)
        {
            existing.Name = name;
            existing.BuilderJson = builderJson;
            existing.Category = descriptor.Category;
            existing.Icon = descriptor.IconSvg;
            existing.PropsSchemaJson = BlockPropsSchema.Build(descriptor);
            await _db.SaveChangesAsync(ct);
            return Ok(new { id = existing.Id, key = existing.Key, updated = true });
        }

        var def = new BlockDefinition
        {
            SiteId = siteId,
            Key = presetKey,
            Name = name,
            Category = descriptor.Category,
            Icon = descriptor.IconSvg,
            BuilderJson = builderJson,
            Kind = BlockKind.Dynamic,
            DynamicHandler = request.Key,
            PropsSchemaJson = BlockPropsSchema.Build(descriptor)
        };
        _db.BlockDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return Created($"/admin/api/builder/block-presets/{def.Id}", new { id = def.Id, key = def.Key, updated = false });
    }

    private static string NormalizeProps(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(raw);
            return raw;
        }
        catch (System.Text.Json.JsonException)
        {
            return "{}";
        }
    }

    /// <summary>
    /// Placeholder kéo-thả giống hệt cái builder tạo cho khối động: một div rỗng mang
    /// <c>data-nc-block</c> + <c>data-nc-props</c>, để renderer công khai thay ruột lúc render.
    /// props đi vào attribute nên phải HTML-encode dấu nháy/nhọn.
    /// </summary>
    private static string BuildPlaceholder(string key, string propsJson)
    {
        var k = System.Net.WebUtility.HtmlEncode(key);
        var p = System.Net.WebUtility.HtmlEncode(propsJson);
        return $"<div data-nc-block=\"{k}\" data-nc-props=\"{p}\"></div>";
    }

    /// <summary>Slug ASCII đơn giản cho phần đuôi key — chỉ cần ổn định, không hiển thị cho người dùng.</summary>
    private static string Slug(string s)
    {
        var chars = s.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return string.IsNullOrEmpty(slug) ? Guid.NewGuid().ToString("n")[..8] : slug;
    }

    public sealed record SavePresetRequest(string Key, string Name, string? PropsJson);
}
