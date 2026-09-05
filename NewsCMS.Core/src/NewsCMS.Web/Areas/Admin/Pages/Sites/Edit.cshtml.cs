using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Web.Areas.Admin.Pages.Sites;

[Authorize(Roles = "SuperAdmin")]
public class EditModel : PageModel
{
    private readonly AppDbContext db;
    private readonly SiteCacheSignal cacheSignal;

    public EditModel(AppDbContext db, SiteCacheSignal cacheSignal)
    {
        this.db = db;
        this.cacheSignal = cacheSignal;
    }

    [BindProperty]
    public SiteInput Input { get; set; } = new();

    [BindProperty]
    public DomainInput NewDomain { get; set; } = new();

    public List<ModuleOption> Modules { get; private set; } = new();
    public List<DomainRow> Domains { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var site = await db.Sites
            .Include(x => x.FeatureModules)
            .ThenInclude(x => x.FeatureModule)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (site == null)
            return NotFound();

        await LoadAsync(site.Id);
        Input = new SiteInput
        {
            Id = site.Id,
            Name = site.Name,
            Slug = site.Slug,
            DefaultTheme = site.DefaultTheme,
            IsActive = site.IsActive,
            EnabledModuleCodes = site.FeatureModules
                .Where(x => x.IsEnabled)
                .Select(x => x.FeatureModule.Code)
                .ToList()
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var site = await db.Sites
            .Include(x => x.FeatureModules)
            .ThenInclude(x => x.FeatureModule)
            .FirstOrDefaultAsync(x => x.Id == Input.Id);

        if (site == null)
            return NotFound();

        await LoadAsync(site.Id);

        if (!ModelState.IsValid)
            return Page();

        var slugExists = await db.Sites.AnyAsync(x => x.Id != Input.Id && x.Slug == Input.Slug);
        if (slugExists)
        {
            ModelState.AddModelError("Input.Slug", "Slug đã tồn tại.");
            return Page();
        }

        site.Name = Input.Name;
        site.Slug = Input.Slug;
        site.DefaultTheme = Input.DefaultTheme;
        site.IsActive = Input.IsActive;
        site.UpdatedAt = DateTime.UtcNow;

        var modules = await db.FeatureModules.ToListAsync();
        foreach (var module in modules)
        {
            var existing = site.FeatureModules.FirstOrDefault(x => x.FeatureModuleId == module.Id);
            var shouldEnable = Input.EnabledModuleCodes.Contains(module.Code);
            if (existing == null)
            {
                db.SiteFeatureModules.Add(new SiteFeatureModule
                {
                    SiteId = site.Id,
                    FeatureModuleId = module.Id,
                    IsEnabled = shouldEnable,
                    EnabledAt = shouldEnable ? DateTime.UtcNow : null
                });
                continue;
            }

            existing.IsEnabled = shouldEnable;
            existing.EnabledAt = shouldEnable ? existing.EnabledAt ?? DateTime.UtcNow : null;
        }

        await db.SaveChangesAsync();
        cacheSignal.Invalidate();
        return RedirectToPage("/Sites/Index", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostAddDomainAsync(Guid id)
    {
        var site = await db.Sites.FirstOrDefaultAsync(x => x.Id == id);
        if (site == null)
            return NotFound();

        var host = SiteHostNormalizer.Normalize(NewDomain.Host);
        if (string.IsNullOrWhiteSpace(host))
        {
            TempData["DomainError"] = "Host không hợp lệ.";
            return RedirectToPage(new { id });
        }

        var taken = await db.SiteDomains.AnyAsync(x => x.Host == host);
        if (taken)
        {
            TempData["DomainError"] = $"Host '{host}' đã được dùng.";
            return RedirectToPage(new { id });
        }

        var isFirst = !await db.SiteDomains.AnyAsync(x => x.SiteId == id);
        var makePrimary = NewDomain.IsPrimary || isFirst;
        if (makePrimary)
        {
            await db.SiteDomains
                .Where(x => x.SiteId == id && x.IsPrimary)
                .ForEachAsync(x => x.IsPrimary = false);
        }

        db.SiteDomains.Add(new SiteDomain { SiteId = id, Host = host, IsPrimary = makePrimary });
        await db.SaveChangesAsync();
        cacheSignal.Invalidate();
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveDomainAsync(Guid id, Guid domainId)
    {
        var domain = await db.SiteDomains.FirstOrDefaultAsync(x => x.Id == domainId && x.SiteId == id);
        if (domain != null)
        {
            db.SiteDomains.Remove(domain);
            await db.SaveChangesAsync();
            cacheSignal.Invalidate();
            TempData["Success"] = $"Đã xóa domain '{domain.Host}'.";
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSetPrimaryAsync(Guid id, Guid domainId)
    {
        var domains = await db.SiteDomains.Where(x => x.SiteId == id).ToListAsync();
        var target = domains.FirstOrDefault(x => x.Id == domainId);
        if (target != null)
        {
            foreach (var d in domains)
                d.IsPrimary = d.Id == domainId;
            await db.SaveChangesAsync();
            cacheSignal.Invalidate();
            TempData["Success"] = $"Đã đặt '{target.Host}' làm domain primary.";
        }
        return RedirectToPage(new { id });
    }

    private async Task LoadAsync(Guid siteId)
    {
        Modules = await db.FeatureModules
            .OrderBy(x => x.SortOrder)
            .Select(x => new ModuleOption(x.Code, x.Name, x.Description))
            .ToListAsync();

        Domains = await db.SiteDomains
            .Where(x => x.SiteId == siteId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Host)
            .Select(x => new DomainRow(x.Id, x.Host, x.IsPrimary))
            .ToListAsync();
    }

    public sealed class SiteInput
    {
        public Guid Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^[a-z0-9-]+$")]
        [MaxLength(120)]
        public string Slug { get; set; } = string.Empty;

        [Required]
        [MaxLength(120)]
        public string DefaultTheme { get; set; } = "Default";

        public bool IsActive { get; set; } = true;

        public List<string> EnabledModuleCodes { get; set; } = new();
    }

    public sealed class DomainInput
    {
        [MaxLength(255)]
        public string Host { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }
    }

    public sealed record ModuleOption(string Code, string Name, string? Description);
    public sealed record DomainRow(Guid Id, string Host, bool IsPrimary);
}
