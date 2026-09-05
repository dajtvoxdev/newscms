using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Web.Areas.Admin.Pages.Sites;

[Authorize(Roles = "SuperAdmin")]
public class CreateModel : PageModel
{
    private readonly AppDbContext db;
    private readonly UserManager<AppUser> userManager;
    private readonly SiteCacheSignal cacheSignal;

    public CreateModel(AppDbContext db, UserManager<AppUser> userManager, SiteCacheSignal cacheSignal)
    {
        this.db = db;
        this.userManager = userManager;
        this.cacheSignal = cacheSignal;
    }

    [BindProperty]
    public SiteInput Input { get; set; } = new();

    public List<ModuleOption> Modules { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadModulesAsync();
        Input.EnabledModuleCodes.Add("news");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadModulesAsync();

        if (!ModelState.IsValid)
            return Page();

        var slugExists = await db.Sites.AnyAsync(x => x.Slug == Input.Slug);
        if (slugExists)
        {
            ModelState.AddModelError("Input.Slug", "Slug đã tồn tại.");
            return Page();
        }

        var normalizedHost = SiteHostNormalizer.Normalize(Input.PrimaryDomain);
        if (!string.IsNullOrWhiteSpace(normalizedHost))
        {
            var domainExists = await db.SiteDomains.AnyAsync(x => x.Host == normalizedHost);
            if (domainExists)
            {
                ModelState.AddModelError("Input.PrimaryDomain", $"Host '{normalizedHost}' đã được dùng.");
                return Page();
            }
        }

        var adminExists = await userManager.FindByNameAsync(Input.AdminUserName) != null
            || await userManager.FindByEmailAsync(Input.AdminEmail) != null;
        if (adminExists)
        {
            ModelState.AddModelError("Input.AdminEmail", "Admin username hoặc email đã tồn tại.");
            return Page();
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        var site = new Site
        {
            Name = Input.Name,
            Slug = Input.Slug,
            PrimaryDomain = string.IsNullOrWhiteSpace(normalizedHost) ? null : normalizedHost,
            DefaultTheme = Input.DefaultTheme,
            IsActive = Input.IsActive
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(normalizedHost))
        {
            db.SiteDomains.Add(new SiteDomain
            {
                SiteId = site.Id,
                Host = normalizedHost,
                IsPrimary = true
            });
            await db.SaveChangesAsync();
        }

        var selectedModules = await db.FeatureModules
            .Where(x => Input.EnabledModuleCodes.Contains(x.Code))
            .ToListAsync();

        foreach (var module in selectedModules)
        {
            db.SiteFeatureModules.Add(new SiteFeatureModule
            {
                SiteId = site.Id,
                FeatureModuleId = module.Id,
                IsEnabled = true,
                EnabledAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var admin = new AppUser
        {
            UserName = Input.AdminUserName,
            Email = Input.AdminEmail,
            EmailConfirmed = true,
            FullName = Input.AdminFullName,
            SiteId = site.Id,
            IsActive = true
        };

        var createResult = await userManager.CreateAsync(admin, Input.AdminPassword);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            await transaction.RollbackAsync();
            return Page();
        }

        await userManager.AddToRoleAsync(admin, "Admin");
        await transaction.CommitAsync();

        return RedirectToPage("/Sites/Index", new { area = "Admin" });
    }

    private async Task LoadModulesAsync()
    {
        Modules = await db.FeatureModules
            .OrderBy(x => x.SortOrder)
            .Select(x => new ModuleOption(x.Code, x.Name, x.Description))
            .ToListAsync();
    }

    public sealed class SiteInput
    {
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^[a-z0-9-]+$")]
        [MaxLength(120)]
        public string Slug { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? PrimaryDomain { get; set; }

        [Required]
        [MaxLength(120)]
        public string DefaultTheme { get; set; } = "Default";

        public bool IsActive { get; set; } = true;

        public List<string> EnabledModuleCodes { get; set; } = new();

        [Required]
        [MaxLength(100)]
        public string AdminUserName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string AdminEmail { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string AdminFullName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string AdminPassword { get; set; } = string.Empty;
    }

    public sealed record ModuleOption(string Code, string Name, string? Description);
}
