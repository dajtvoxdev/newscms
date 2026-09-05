using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Builder;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

[Authorize(Permissions.Builder.PageView)]
public class IndexModel : PageModel
{
    private readonly IBuilderPageService _service;

    public IndexModel(IBuilderPageService service) => _service = service;

    public IReadOnlyList<BuilderPageDto> Items { get; set; } = Array.Empty<BuilderPageDto>();
    public IReadOnlyList<PageTreeItem> TreeItems { get; set; } = Array.Empty<PageTreeItem>();
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public string? Search { get; set; }

    public sealed record PageTreeItem(
        BuilderPageDto Page,
        int Level,
        string? ParentTitle);

    public async Task OnGetAsync(int page = 1, string? search = null, CancellationToken ct = default)
    {
        Search = search;
        CurrentPage = Math.Max(1, page);
        int take = string.IsNullOrWhiteSpace(search) ? 100 : 20;
        var list = await _service.ListAsync(CurrentPage, take, search, ct);
        Items = list.Items;
        TotalPages = list.TotalPages;

        var titleLookup = Items.ToDictionary(p => p.Id, p => p.Title);
        if (string.IsNullOrWhiteSpace(search))
        {
            var childrenByParent = Items
                .Where(p => p.ParentPageId.HasValue && p.ParentPageId.Value != Guid.Empty)
                .GroupBy(p => p.ParentPageId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var roots = Items.Where(p => !p.ParentPageId.HasValue || p.ParentPageId.Value == Guid.Empty || !titleLookup.ContainsKey(p.ParentPageId.Value)).ToList();
            var tree = new List<PageTreeItem>();

            void AddSubtree(BuilderPageDto item, int level)
            {
                var parentTitle = item.ParentPageId.HasValue && titleLookup.TryGetValue(item.ParentPageId.Value, out var pt) ? pt : null;
                tree.Add(new PageTreeItem(item, level, parentTitle));
                if (childrenByParent.TryGetValue(item.Id, out var children))
                {
                    foreach (var child in children)
                    {
                        AddSubtree(child, level + 1);
                    }
                }
            }

            foreach (var root in roots)
            {
                AddSubtree(root, 0);
            }

            TreeItems = tree;
        }
        else
        {
            TreeItems = Items.Select(p => new PageTreeItem(
                p,
                0,
                p.ParentPageId.HasValue && titleLookup.TryGetValue(p.ParentPageId.Value, out var pt) ? pt : null
            )).ToList();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        if (!User.HasClaim(Permissions.Prefix, Permissions.Builder.PageDelete))
            return Forbid();

        var result = await _service.DeleteAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã xoá trang." : result.Error;
        return RedirectToPage();
    }
}
