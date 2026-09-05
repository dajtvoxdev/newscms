# Admin Tailwind Multi-site Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the admin UI with Tailwind and add a super-admin-managed site foundation with per-site admin accounts and feature/module switches.

**Architecture:** Keep this phase as a foundation layer: add site/module entities and admin management UI without full content tenant isolation. Use Razor Pages for admin screens, EF Core for persistence, ASP.NET Identity for users/roles, and Tailwind CLI for local CSS compilation.

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core, ASP.NET Core Identity, Tailwind CSS CLI, SQL Server/SQLite via existing EF provider, Playwright/manual browser verification.

---

## File Structure

Create:

- `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/Site.cs` — platform site entity.
- `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/FeatureModule.cs` — feature/module catalog entity.
- `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/SiteFeatureModule.cs` — many-to-many module assignment entity.
- `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Configurations/SiteConfiguration.cs` — EF mapping for `Site`.
- `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Configurations/FeatureModuleConfiguration.cs` — EF mapping for `FeatureModule` and `SiteFeatureModule`.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Index.cshtml` — super admin site list.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Index.cshtml.cs` — site list PageModel.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Create.cshtml` — create site form.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Create.cshtml.cs` — create site handler.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Edit.cshtml` — edit site + modules form.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Edit.cshtml.cs` — edit handler.
- `NewsCMS.Core/src/NewsCMS.Web/package.json` — Tailwind build scripts.
- `NewsCMS.Core/src/NewsCMS.Web/tailwind.config.js` — Tailwind content paths.
- `NewsCMS.Core/src/NewsCMS.Web/Styles/admin.css` — Tailwind source CSS.
- `NewsCMS.Core/src/NewsCMS.Web/wwwroot/css/admin.css` — compiled admin CSS output.

Modify:

- `NewsCMS.Core/src/NewsCMS.Domain/Entities/Identity/AppUser.cs` — add optional `SiteId` relationship.
- `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/SiteEntities.cs` — keep existing site content entities; do not add new types here to avoid bloating file.
- `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/AppDbContext.cs` — add DbSets and relationship mapping references.
- `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Seed/DbSeeder.cs` — seed default site, modules, global super admin, default site admin.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Shared/_AdminLayout.cshtml` — remove Bootstrap CDN and use Tailwind admin shell.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Shared/_Sidebar.cshtml` — role-aware + feature-aware navigation.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Index.cshtml` — Tailwind dashboard.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Posts/Index.cshtml` — Tailwind table/filter/buttons.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Posts/Create.cshtml` — Tailwind form/cards.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Posts/Edit.cshtml` — Tailwind form/cards.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Media/Upload.cshtml` — Tailwind upload UI.
- `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Shared/_TinyMce.cshtml` — ensure editor container fits new UI.

Do not create tests project in this phase unless time allows. Verification uses `dotnet build`, Tailwind build, and browser/manual checks because current repo has no test project.

---

### Task 1: Add Site and Feature Module Domain Model

**Files:**
- Create: `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/Site.cs`
- Create: `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/FeatureModule.cs`
- Create: `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/SiteFeatureModule.cs`
- Modify: `NewsCMS.Core/src/NewsCMS.Domain/Entities/Identity/AppUser.cs`

- [ ] **Step 1: Create `Site` entity**

Write `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/Site.cs`:

```csharp
using NewsCMS.Domain.Common;
using NewsCMS.Domain.Entities.Identity;

namespace NewsCMS.Domain.Entities.Site;

public class Site : BaseEntity
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? PrimaryDomain { get; set; }
    public string DefaultTheme { get; set; } = "Default";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<SiteFeatureModule> FeatureModules { get; set; } = new List<SiteFeatureModule>();
}
```

- [ ] **Step 2: Create `FeatureModule` entity**

Write `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/FeatureModule.cs`:

```csharp
using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Site;

public class FeatureModule : BaseEntity
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public ICollection<SiteFeatureModule> Sites { get; set; } = new List<SiteFeatureModule>();
}
```

- [ ] **Step 3: Create `SiteFeatureModule` entity**

Write `NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/SiteFeatureModule.cs`:

```csharp
namespace NewsCMS.Domain.Entities.Site;

public class SiteFeatureModule
{
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = default!;
    public Guid FeatureModuleId { get; set; }
    public FeatureModule FeatureModule { get; set; } = default!;
    public bool IsEnabled { get; set; }
    public DateTime? EnabledAt { get; set; }
}
```

- [ ] **Step 4: Add site relation to `AppUser`**

Modify `NewsCMS.Core/src/NewsCMS.Domain/Entities/Identity/AppUser.cs` so `AppUser` becomes:

```csharp
using Microsoft.AspNetCore.Identity;
using NewsCMS.Domain.Entities.Site;

namespace NewsCMS.Domain.Entities.Identity;

public class AppUser : IdentityUser<Guid>
{
    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
```

Leave `AppRole`, `Permission`, and `RolePermission` unchanged below it.

- [ ] **Step 5: Build domain project**

Run:

```bash
dotnet build NewsCMS.Core/src/NewsCMS.Domain/NewsCMS.Domain.csproj
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/Site.cs NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/FeatureModule.cs NewsCMS.Core/src/NewsCMS.Domain/Entities/Site/SiteFeatureModule.cs NewsCMS.Core/src/NewsCMS.Domain/Entities/Identity/AppUser.cs
git commit -m "feat: add site module domain model"
```

---

### Task 2: Add EF Core Mapping and DbSets

**Files:**
- Create: `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Configurations/SiteConfiguration.cs`
- Create: `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Configurations/FeatureModuleConfiguration.cs`
- Modify: `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Create site mapping**

Write `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Configurations/SiteConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Site;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable("Sites");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Slug)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.PrimaryDomain)
            .HasMaxLength(255);

        builder.Property(x => x.DefaultTheme)
            .HasMaxLength(120)
            .IsRequired();

        builder.HasIndex(x => x.Slug)
            .IsUnique();

        builder.HasIndex(x => x.PrimaryDomain)
            .IsUnique()
            .HasFilter("[PrimaryDomain] IS NOT NULL");

        builder.HasMany(x => x.Users)
            .WithOne(x => x.Site)
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 2: Create feature module mapping**

Write `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Configurations/FeatureModuleConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Site;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class FeatureModuleConfiguration : IEntityTypeConfiguration<FeatureModule>, IEntityTypeConfiguration<SiteFeatureModule>
{
    public void Configure(EntityTypeBuilder<FeatureModule> builder)
    {
        builder.ToTable("FeatureModules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.HasIndex(x => x.Code)
            .IsUnique();
    }

    public void Configure(EntityTypeBuilder<SiteFeatureModule> builder)
    {
        builder.ToTable("SiteFeatureModules");
        builder.HasKey(x => new { x.SiteId, x.FeatureModuleId });

        builder.HasOne(x => x.Site)
            .WithMany(x => x.FeatureModules)
            .HasForeignKey(x => x.SiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FeatureModule)
            .WithMany(x => x.Sites)
            .HasForeignKey(x => x.FeatureModuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 3: Add DbSets**

Modify `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/AppDbContext.cs`. Add under `// Site` section:

```csharp
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<FeatureModule> FeatureModules => Set<FeatureModule>();
    public DbSet<SiteFeatureModule> SiteFeatureModules => Set<SiteFeatureModule>();
```

The section should become:

```csharp
    // Site
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<FeatureModule> FeatureModules => Set<FeatureModule>();
    public DbSet<SiteFeatureModule> SiteFeatureModules => Set<SiteFeatureModule>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
```

- [ ] **Step 4: Build infrastructure project**

Run:

```bash
dotnet build NewsCMS.Core/src/NewsCMS.Infrastructure/NewsCMS.Infrastructure.csproj
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Configurations/SiteConfiguration.cs NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Configurations/FeatureModuleConfiguration.cs NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat: map site feature modules"
```

---

### Task 3: Seed Default Site, Modules, Super Admin, Site Admin

**Files:**
- Modify: `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Seed/DbSeeder.cs`

- [ ] **Step 1: Replace seed role/user/site section**

Modify `DbSeeder.SeedAsync` after permissions are saved. Keep permission seeding and content/category seeding. Replace current roles/super user/site settings block with this code:

```csharp
        // 2. Roles
        await EnsureRole(roleManager, "SuperAdmin", "Toàn quyền hệ thống", isSystem: true);
        await EnsureRole(roleManager, "Admin", "Quản trị viên site");
        await EnsureRole(roleManager, "Editor", "Biên tập viên");
        await EnsureRole(roleManager, "Author", "Cộng tác viên");

        // 3. Feature modules
        var newsModule = await EnsureFeatureModule(db, "news", "Tin tức", "Quản lý bài viết, chuyên mục, tag và media", isSystem: true, sortOrder: 10);
        var productsModule = await EnsureFeatureModule(db, "products", "Sản phẩm", "Quản lý sản phẩm trong các site thương mại", isSystem: false, sortOrder: 20);

        // 4. Default site
        var defaultSite = await db.Sites.FirstOrDefaultAsync(x => x.Slug == "default");
        if (defaultSite == null)
        {
            defaultSite = new Site
            {
                Name = "Tin tức NewsCMS",
                Slug = "default",
                DefaultTheme = "Default",
                IsActive = true
            };
            db.Sites.Add(defaultSite);
            await db.SaveChangesAsync();
        }

        await EnsureSiteFeature(db, defaultSite.Id, newsModule.Id, true);
        await EnsureSiteFeature(db, defaultSite.Id, productsModule.Id, false);

        // 5. Gán toàn bộ quyền cho SuperAdmin
        var superRole = await roleManager.FindByNameAsync("SuperAdmin");
        if (superRole != null)
        {
            var allPerms = await db.Permissions.ToListAsync();
            var assigned = await db.RolePermissions.Where(x => x.RoleId == superRole.Id).Select(x => x.PermissionId).ToListAsync();
            foreach (var p in allPerms)
            {
                if (!assigned.Contains(p.Id))
                    db.RolePermissions.Add(new RolePermission { RoleId = superRole.Id, PermissionId = p.Id });
            }
            await db.SaveChangesAsync();
        }

        // 6. Super user mặc định
        const string superEmail = "admin@newscms.local";
        var super = await userManager.FindByNameAsync("admin");
        if (super == null)
        {
            super = new AppUser
            {
                UserName = "admin",
                Email = superEmail,
                EmailConfirmed = true,
                FullName = "Super Admin",
                SiteId = null,
                IsActive = true
            };
            var createRes = await userManager.CreateAsync(super, "Admin@123");
            if (createRes.Succeeded)
                await userManager.AddToRoleAsync(super, "SuperAdmin");
        }
        else if (super.SiteId != null)
        {
            super.SiteId = null;
            await userManager.UpdateAsync(super);
        }

        // 7. Default site admin
        var defaultAdmin = await userManager.FindByNameAsync("admin-default");
        if (defaultAdmin == null)
        {
            defaultAdmin = new AppUser
            {
                UserName = "admin-default",
                Email = "admin@default.local",
                EmailConfirmed = true,
                FullName = "Default Site Admin",
                SiteId = defaultSite.Id,
                IsActive = true
            };
            var createRes = await userManager.CreateAsync(defaultAdmin, "Admin@123");
            if (createRes.Succeeded)
                await userManager.AddToRoleAsync(defaultAdmin, "Admin");
        }

        // 8. Seed cấu hình site cơ bản
        if (!await db.SiteSettings.AnyAsync())
        {
            db.SiteSettings.AddRange(
                new SiteSetting { Key = "Site.Name", Value = defaultSite.Name, Group = "general" },
                new SiteSetting { Key = "Site.Description", Value = "Trang tin tức demo", Group = "general" },
                new SiteSetting { Key = "Site.Logo", Value = "/img/logo.png", Group = "general" },
                new SiteSetting { Key = "Site.ActiveTheme", Value = defaultSite.DefaultTheme, Group = "theme" }
            );
            await db.SaveChangesAsync();
        }
```

- [ ] **Step 2: Add helper methods**

Add these private methods below `EnsureRole`:

```csharp
    private static async Task<FeatureModule> EnsureFeatureModule(
        AppDbContext db,
        string code,
        string name,
        string description,
        bool isSystem,
        int sortOrder)
    {
        var module = await db.FeatureModules.FirstOrDefaultAsync(x => x.Code == code);
        if (module != null)
            return module;

        module = new FeatureModule
        {
            Code = code,
            Name = name,
            Description = description,
            IsSystem = isSystem,
            SortOrder = sortOrder
        };
        db.FeatureModules.Add(module);
        await db.SaveChangesAsync();
        return module;
    }

    private static async Task EnsureSiteFeature(AppDbContext db, Guid siteId, Guid featureModuleId, bool isEnabled)
    {
        var siteFeature = await db.SiteFeatureModules.FindAsync(siteId, featureModuleId);
        if (siteFeature == null)
        {
            db.SiteFeatureModules.Add(new SiteFeatureModule
            {
                SiteId = siteId,
                FeatureModuleId = featureModuleId,
                IsEnabled = isEnabled,
                EnabledAt = isEnabled ? DateTime.UtcNow : null
            });
            await db.SaveChangesAsync();
            return;
        }

        siteFeature.IsEnabled = isEnabled;
        siteFeature.EnabledAt = isEnabled ? siteFeature.EnabledAt ?? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 3: Build infrastructure project**

Run:

```bash
dotnet build NewsCMS.Core/src/NewsCMS.Infrastructure/NewsCMS.Infrastructure.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Seed/DbSeeder.cs
git commit -m "feat: seed sites and feature modules"
```

---

### Task 4: Add EF Migration

**Files:**
- Create: EF migration files under `NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Migrations/`

- [ ] **Step 1: Create migration**

Run from `NewsCMS.Core/src/NewsCMS.Web` if EF tooling requires startup project:

```bash
dotnet ef migrations add AddSitesAndFeatureModules --project ../NewsCMS.Infrastructure/NewsCMS.Infrastructure.csproj --startup-project . --output-dir Persistence/Migrations
```

If `dotnet ef` is not installed, install local/global tool only after user approval. Do not hand-write migration unless EF tooling is unavailable and user approves.

Expected: migration creates `Sites`, `FeatureModules`, `SiteFeatureModules`, adds `SiteId` to `Users`, and updates model snapshot.

- [ ] **Step 2: Review generated migration**

Open generated migration and confirm it contains:

```csharp
migrationBuilder.AddColumn<Guid>(
    name: "SiteId",
    table: "Users",
    type: "uniqueidentifier",
    nullable: true);

migrationBuilder.CreateTable(
    name: "Sites",
```

Also confirm tables `FeatureModules` and `SiteFeatureModules` exist.

- [ ] **Step 3: Build solution**

Run:

```bash
dotnet build NewsCMS.Core/NewsCMS.Core.sln
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Migrations NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat: add site feature migration"
```

---

### Task 5: Add Tailwind Build Pipeline

**Files:**
- Create: `NewsCMS.Core/src/NewsCMS.Web/package.json`
- Create: `NewsCMS.Core/src/NewsCMS.Web/tailwind.config.js`
- Create: `NewsCMS.Core/src/NewsCMS.Web/Styles/admin.css`
- Create: `NewsCMS.Core/src/NewsCMS.Web/wwwroot/css/admin.css`

- [ ] **Step 1: Create package.json**

Write `NewsCMS.Core/src/NewsCMS.Web/package.json`:

```json
{
  "scripts": {
    "css:admin": "tailwindcss -i ./Styles/admin.css -o ./wwwroot/css/admin.css --minify",
    "css:admin:watch": "tailwindcss -i ./Styles/admin.css -o ./wwwroot/css/admin.css --watch"
  },
  "devDependencies": {
    "@tailwindcss/cli": "^4.1.0",
    "tailwindcss": "^4.1.0"
  }
}
```

- [ ] **Step 2: Create Tailwind config**

Write `NewsCMS.Core/src/NewsCMS.Web/tailwind.config.js`:

```js
module.exports = {
  content: [
    './Areas/Admin/**/*.cshtml',
    './Areas/Admin/**/*.cshtml.cs',
    './Pages/**/*.cshtml',
    './Views/**/*.cshtml'
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif']
      },
      boxShadow: {
        soft: '0 18px 60px rgba(15, 23, 42, 0.08)'
      }
    }
  }
};
```

- [ ] **Step 3: Create Tailwind source CSS**

Write `NewsCMS.Core/src/NewsCMS.Web/Styles/admin.css`:

```css
@import "tailwindcss";

@layer base {
  html {
    font-family: Inter, ui-sans-serif, system-ui, sans-serif;
  }

  body {
    @apply bg-slate-100 text-slate-900 antialiased;
  }

  a {
    @apply transition-colors;
  }
}

@layer components {
  .admin-card {
    @apply rounded-2xl border border-slate-200 bg-white shadow-soft;
  }

  .admin-label {
    @apply text-sm font-semibold text-slate-700;
  }

  .admin-input {
    @apply w-full rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm text-slate-900 outline-none transition focus:border-indigo-500 focus:ring-4 focus:ring-indigo-100;
  }

  .admin-select {
    @apply w-full rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm text-slate-900 outline-none transition focus:border-indigo-500 focus:ring-4 focus:ring-indigo-100;
  }

  .admin-btn-primary {
    @apply inline-flex items-center justify-center rounded-xl bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-indigo-700 focus:outline-none focus:ring-4 focus:ring-indigo-200;
  }

  .admin-btn-secondary {
    @apply inline-flex items-center justify-center rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700 shadow-sm transition hover:border-slate-300 hover:bg-slate-50 focus:outline-none focus:ring-4 focus:ring-slate-200;
  }

  .admin-badge {
    @apply inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold;
  }
}
```

- [ ] **Step 4: Install npm dependencies**

Run:

```bash
npm install --prefix NewsCMS.Core/src/NewsCMS.Web
```

Expected: `package-lock.json` created and dependencies installed.

- [ ] **Step 5: Build admin CSS**

Run:

```bash
npm run css:admin --prefix NewsCMS.Core/src/NewsCMS.Web
```

Expected: `NewsCMS.Core/src/NewsCMS.Web/wwwroot/css/admin.css` generated.

- [ ] **Step 6: Commit**

```bash
git add NewsCMS.Core/src/NewsCMS.Web/package.json NewsCMS.Core/src/NewsCMS.Web/package-lock.json NewsCMS.Core/src/NewsCMS.Web/tailwind.config.js NewsCMS.Core/src/NewsCMS.Web/Styles/admin.css NewsCMS.Core/src/NewsCMS.Web/wwwroot/css/admin.css
git commit -m "feat: add admin tailwind pipeline"
```

---

### Task 6: Replace Admin Layout and Sidebar with Tailwind

**Files:**
- Modify: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Shared/_AdminLayout.cshtml`
- Modify: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Shared/_Sidebar.cshtml`

- [ ] **Step 1: Replace admin layout**

Replace `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Shared/_AdminLayout.cshtml` with:

```cshtml
@{
    var title = ViewData["Title"] ?? "Quản trị";
    var userName = User.Identity?.Name ?? "Admin";
}
<!DOCTYPE html>
<html lang="vi">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>@title · NewsCMS Admin</title>
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap" rel="stylesheet">
    <link rel="stylesheet" href="~/css/admin.css" asp-append-version="true" />
    @await RenderSectionAsync("Head", required: false)
</head>
<body>
    <div class="min-h-screen bg-slate-100 lg:flex">
        <aside class="border-r border-slate-800 bg-slate-950 text-slate-100 lg:fixed lg:inset-y-0 lg:left-0 lg:w-72">
            <div class="flex h-20 items-center gap-3 border-b border-white/10 px-6">
                <div class="flex h-11 w-11 items-center justify-center rounded-2xl bg-indigo-500 text-lg font-black text-white">N</div>
                <div>
                    <div class="text-base font800 font-extrabold tracking-tight text-white">NewsCMS</div>
                    <div class="text-xs font-medium text-slate-400">Admin Console</div>
                </div>
            </div>
            <div class="px-4 py-5">
                <partial name="_Sidebar" />
            </div>
        </aside>

        <main class="min-h-screen flex-1 lg:pl-72">
            <header class="sticky top-0 z-30 border-b border-slate-200 bg-white/90 backdrop-blur">
                <div class="flex min-h-20 items-center justify-between gap-4 px-5 py-4 lg:px-8">
                    <div>
                        <p class="text-xs font-semibold uppercase tracking-[0.24em] text-indigo-600">Quản trị</p>
                        <h1 class="mt-1 text-2xl font-extrabold tracking-tight text-slate-950">@title</h1>
                    </div>
                    <div class="flex items-center gap-3">
                        <div class="hidden rounded-2xl border border-slate-200 bg-slate-50 px-4 py-2 text-right sm:block">
                            <div class="text-sm font-semibold text-slate-900">@userName</div>
                            <div class="text-xs text-slate-500">Đang đăng nhập</div>
                        </div>
                        <form method="post" asp-area="Identity" asp-page="/Account/Logout">
                            <button class="admin-btn-secondary" type="submit">Đăng xuất</button>
                        </form>
                    </div>
                </div>
            </header>

            <section class="px-5 py-6 lg:px-8 lg:py-8">
                @RenderBody()
            </section>
        </main>
    </div>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

After writing, fix `font800` typo immediately by changing it to `font-extrabold`. Final line must be:

```cshtml
<div class="text-base font-extrabold tracking-tight text-white">NewsCMS</div>
```

- [ ] **Step 2: Replace sidebar**

Replace `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Shared/_Sidebar.cshtml` with:

```cshtml
@{
    string Active(string startsWith) =>
        (Context.Request.Path.Value ?? "").StartsWith(startsWith, StringComparison.OrdinalIgnoreCase)
            ? "bg-white/10 text-white shadow-sm"
            : "text-slate-300 hover:bg-white/5 hover:text-white";

    var isSuperAdmin = User.IsInRole("SuperAdmin");
}
<nav class="space-y-6">
    <div class="space-y-1">
        <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Index")" asp-area="Admin" asp-page="/Index">
            <span class="h-2 w-2 rounded-full bg-indigo-400"></span>
            Dashboard
        </a>
    </div>

    @if (isSuperAdmin)
    {
        <div>
            <div class="px-3 text-xs font-bold uppercase tracking-[0.2em] text-slate-500">Nền tảng</div>
            <div class="mt-2 space-y-1">
                <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Sites")" asp-area="Admin" asp-page="/Sites/Index">
                    <span class="h-2 w-2 rounded-full bg-sky-400"></span>
                    Sites
                </a>
            </div>
        </div>
    }

    <div>
        <div class="px-3 text-xs font-bold uppercase tracking-[0.2em] text-slate-500">Nội dung</div>
        <div class="mt-2 space-y-1">
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Posts")" asp-area="Admin" asp-page="/Posts/Index">
                <span class="h-2 w-2 rounded-full bg-emerald-400"></span>
                Bài viết
            </a>
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Categories")" asp-area="Admin" asp-page="/Categories/Index">Chuyên mục</a>
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Tags")" asp-area="Admin" asp-page="/Tags/Index">Tag</a>
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Media")" asp-area="Admin" asp-page="/Media/Index">Media</a>
        </div>
    </div>

    <div>
        <div class="px-3 text-xs font-bold uppercase tracking-[0.2em] text-slate-500">Giao diện</div>
        <div class="mt-2 space-y-1">
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Menus")" asp-area="Admin" asp-page="/Menus/Index">Menu</a>
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Banners")" asp-area="Admin" asp-page="/Banners/Index">Banner</a>
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Pages")" asp-area="Admin" asp-page="/Pages/Index">Trang tĩnh</a>
        </div>
    </div>

    <div>
        <div class="px-3 text-xs font-bold uppercase tracking-[0.2em] text-slate-500">Hệ thống</div>
        <div class="mt-2 space-y-1">
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Users")" asp-area="Admin" asp-page="/Users/Index">Người dùng</a>
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Roles")" asp-area="Admin" asp-page="/Roles/Index">Vai trò</a>
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Settings")" asp-area="Admin" asp-page="/Settings/Index">Cấu hình</a>
            <a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/AuditLogs")" asp-area="Admin" asp-page="/AuditLogs/Index">Audit log</a>
        </div>
    </div>
</nav>
```

- [ ] **Step 3: Rebuild admin CSS**

Run:

```bash
npm run css:admin --prefix NewsCMS.Core/src/NewsCMS.Web
```

Expected: Tailwind compiles with no errors.

- [ ] **Step 4: Build web project**

Run:

```bash
dotnet build NewsCMS.Core/src/NewsCMS.Web/NewsCMS.Web.csproj
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Shared/_AdminLayout.cshtml NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Shared/_Sidebar.cshtml NewsCMS.Core/src/NewsCMS.Web/wwwroot/css/admin.css
git commit -m "feat: rebuild admin shell with tailwind"
```

---

### Task 7: Add Super Admin Site Management Pages

**Files:**
- Create: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Index.cshtml`
- Create: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Index.cshtml.cs`
- Create: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Create.cshtml`
- Create: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Create.cshtml.cs`
- Create: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Edit.cshtml`
- Create: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Edit.cshtml.cs`

- [ ] **Step 1: Create Sites index PageModel**

Write `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Index.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Areas.Admin.Pages.Sites;

[Authorize(Roles = "SuperAdmin")]
public class IndexModel : PageModel
{
    private readonly AppDbContext db;
    private readonly UserManager<AppUser> userManager;

    public IndexModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        this.db = db;
        this.userManager = userManager;
    }

    public List<SiteRow> Sites { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var sites = await db.Sites
            .Include(x => x.FeatureModules)
            .ThenInclude(x => x.FeatureModule)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var adminUsers = await userManager.Users
            .Where(x => x.SiteId != null)
            .Select(x => new { x.SiteId, x.UserName, x.Email })
            .ToListAsync();

        Sites = sites.Select(site => new SiteRow(
            site.Id,
            site.Name,
            site.Slug,
            site.PrimaryDomain,
            site.IsActive,
            site.FeatureModules
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.FeatureModule.SortOrder)
                .Select(x => x.FeatureModule.Name)
                .ToList(),
            adminUsers
                .Where(x => x.SiteId == site.Id)
                .Select(x => x.Email ?? x.UserName ?? "-")
                .ToList()
        )).ToList();
    }

    public sealed record SiteRow(
        Guid Id,
        string Name,
        string Slug,
        string? PrimaryDomain,
        bool IsActive,
        IReadOnlyList<string> EnabledModules,
        IReadOnlyList<string> Admins);
}
```

- [ ] **Step 2: Create Sites index view**

Write `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Index.cshtml`:

```cshtml
@page
@model NewsCMS.Web.Areas.Admin.Pages.Sites.IndexModel
@{
    ViewData["Title"] = "Sites";
}

<div class="mb-6 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
    <div>
        <p class="text-sm font-semibold text-indigo-600">Quản lý nền tảng</p>
        <h2 class="mt-1 text-2xl font-extrabold tracking-tight text-slate-950">Sites</h2>
    </div>
    <a asp-area="Admin" asp-page="/Sites/Create" class="admin-btn-primary">Tạo site mới</a>
</div>

<div class="admin-card overflow-hidden">
    <table class="w-full min-w-[900px] text-left text-sm">
        <thead class="bg-slate-50 text-xs font-bold uppercase tracking-wide text-slate-500">
            <tr>
                <th class="px-5 py-4">Site</th>
                <th class="px-5 py-4">Domain</th>
                <th class="px-5 py-4">Modules</th>
                <th class="px-5 py-4">Admins</th>
                <th class="px-5 py-4">Trạng thái</th>
                <th class="px-5 py-4"></th>
            </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
            @if (Model.Sites.Count == 0)
            {
                <tr><td colspan="6" class="px-5 py-10 text-center text-slate-500">Chưa có site.</td></tr>
            }
            @foreach (var site in Model.Sites)
            {
                <tr class="bg-white">
                    <td class="px-5 py-4">
                        <div class="font-bold text-slate-950">@site.Name</div>
                        <div class="text-xs text-slate-500">@site.Slug</div>
                    </td>
                    <td class="px-5 py-4 text-slate-600">@(site.PrimaryDomain ?? "-")</td>
                    <td class="px-5 py-4">
                        <div class="flex flex-wrap gap-2">
                            @foreach (var module in site.EnabledModules)
                            {
                                <span class="admin-badge bg-indigo-50 text-indigo-700">@module</span>
                            }
                        </div>
                    </td>
                    <td class="px-5 py-4 text-slate-600">@string.Join(", ", site.Admins)</td>
                    <td class="px-5 py-4">
                        <span class="admin-badge @(site.IsActive ? "bg-emerald-50 text-emerald-700" : "bg-slate-100 text-slate-600")">
                            @(site.IsActive ? "Đang bật" : "Đã tắt")
                        </span>
                    </td>
                    <td class="px-5 py-4 text-right">
                        <a asp-area="Admin" asp-page="/Sites/Edit" asp-route-id="@site.Id" class="admin-btn-secondary">Sửa</a>
                    </td>
                </tr>
            }
        </tbody>
    </table>
</div>
```

- [ ] **Step 3: Create Sites create PageModel**

Write `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Create.cshtml.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Areas.Admin.Pages.Sites;

[Authorize(Roles = "SuperAdmin")]
public class CreateModel : PageModel
{
    private readonly AppDbContext db;
    private readonly UserManager<AppUser> userManager;

    public CreateModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        this.db = db;
        this.userManager = userManager;
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

        if (!string.IsNullOrWhiteSpace(Input.PrimaryDomain))
        {
            var domainExists = await db.Sites.AnyAsync(x => x.PrimaryDomain == Input.PrimaryDomain);
            if (domainExists)
            {
                ModelState.AddModelError("Input.PrimaryDomain", "Domain đã tồn tại.");
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
            PrimaryDomain = string.IsNullOrWhiteSpace(Input.PrimaryDomain) ? null : Input.PrimaryDomain,
            DefaultTheme = Input.DefaultTheme,
            IsActive = Input.IsActive
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

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
```

- [ ] **Step 4: Create Sites create view**

Write `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Create.cshtml`:

```cshtml
@page
@model NewsCMS.Web.Areas.Admin.Pages.Sites.CreateModel
@{
    ViewData["Title"] = "Tạo site";
}

<form method="post" class="space-y-6">
    @Html.AntiForgeryToken()
    <div class="flex items-center justify-between">
        <div>
            <p class="text-sm font-semibold text-indigo-600">Sites</p>
            <h2 class="mt-1 text-2xl font-extrabold tracking-tight text-slate-950">Tạo site mới</h2>
        </div>
        <button type="submit" class="admin-btn-primary">Tạo site</button>
    </div>

    <div asp-validation-summary="ModelOnly" class="rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700"></div>

    <div class="grid gap-6 lg:grid-cols-[1fr_360px]">
        <section class="admin-card p-6">
            <h3 class="text-lg font-bold text-slate-950">Thông tin site</h3>
            <div class="mt-5 grid gap-5 md:grid-cols-2">
                <label class="space-y-2 md:col-span-2">
                    <span class="admin-label">Tên site</span>
                    <input asp-for="Input.Name" class="admin-input" />
                    <span asp-validation-for="Input.Name" class="text-sm text-red-600"></span>
                </label>
                <label class="space-y-2">
                    <span class="admin-label">Slug</span>
                    <input asp-for="Input.Slug" class="admin-input" placeholder="vi-du-site" />
                    <span asp-validation-for="Input.Slug" class="text-sm text-red-600"></span>
                </label>
                <label class="space-y-2">
                    <span class="admin-label">Domain</span>
                    <input asp-for="Input.PrimaryDomain" class="admin-input" placeholder="example.com" />
                    <span asp-validation-for="Input.PrimaryDomain" class="text-sm text-red-600"></span>
                </label>
                <label class="space-y-2">
                    <span class="admin-label">Theme mặc định</span>
                    <input asp-for="Input.DefaultTheme" class="admin-input" />
                    <span asp-validation-for="Input.DefaultTheme" class="text-sm text-red-600"></span>
                </label>
                <label class="flex items-center gap-3 pt-8">
                    <input asp-for="Input.IsActive" class="h-5 w-5 rounded border-slate-300 text-indigo-600 focus:ring-indigo-500" />
                    <span class="admin-label">Kích hoạt site</span>
                </label>
            </div>
        </section>

        <aside class="admin-card p-6">
            <h3 class="text-lg font-bold text-slate-950">Modules</h3>
            <div class="mt-5 space-y-3">
                @foreach (var module in Model.Modules)
                {
                    <label class="flex gap-3 rounded-2xl border border-slate-200 p-4">
                        <input type="checkbox" name="Input.EnabledModuleCodes" value="@module.Code" checked="@Model.Input.EnabledModuleCodes.Contains(module.Code)" class="mt-1 h-5 w-5 rounded border-slate-300 text-indigo-600 focus:ring-indigo-500" />
                        <span>
                            <span class="block font-bold text-slate-900">@module.Name</span>
                            <span class="block text-sm text-slate-500">@module.Description</span>
                        </span>
                    </label>
                }
            </div>
        </aside>
    </div>

    <section class="admin-card p-6">
        <h3 class="text-lg font-bold text-slate-950">Admin mặc định</h3>
        <div class="mt-5 grid gap-5 md:grid-cols-2">
            <label class="space-y-2">
                <span class="admin-label">Username</span>
                <input asp-for="Input.AdminUserName" class="admin-input" />
                <span asp-validation-for="Input.AdminUserName" class="text-sm text-red-600"></span>
            </label>
            <label class="space-y-2">
                <span class="admin-label">Email</span>
                <input asp-for="Input.AdminEmail" class="admin-input" />
                <span asp-validation-for="Input.AdminEmail" class="text-sm text-red-600"></span>
            </label>
            <label class="space-y-2">
                <span class="admin-label">Họ tên</span>
                <input asp-for="Input.AdminFullName" class="admin-input" />
                <span asp-validation-for="Input.AdminFullName" class="text-sm text-red-600"></span>
            </label>
            <label class="space-y-2">
                <span class="admin-label">Mật khẩu tạm</span>
                <input asp-for="Input.AdminPassword" class="admin-input" />
                <span asp-validation-for="Input.AdminPassword" class="text-sm text-red-600"></span>
            </label>
        </div>
    </section>
</form>
```

- [ ] **Step 5: Create Sites edit PageModel**

Write `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Edit.cshtml.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Web.Areas.Admin.Pages.Sites;

[Authorize(Roles = "SuperAdmin")]
public class EditModel : PageModel
{
    private readonly AppDbContext db;

    public EditModel(AppDbContext db)
    {
        this.db = db;
    }

    [BindProperty]
    public SiteInput Input { get; set; } = new();

    public List<ModuleOption> Modules { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var site = await db.Sites
            .Include(x => x.FeatureModules)
            .ThenInclude(x => x.FeatureModule)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (site == null)
            return NotFound();

        await LoadModulesAsync();
        Input = new SiteInput
        {
            Id = site.Id,
            Name = site.Name,
            Slug = site.Slug,
            PrimaryDomain = site.PrimaryDomain,
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
        await LoadModulesAsync();

        if (!ModelState.IsValid)
            return Page();

        var site = await db.Sites
            .Include(x => x.FeatureModules)
            .ThenInclude(x => x.FeatureModule)
            .FirstOrDefaultAsync(x => x.Id == Input.Id);

        if (site == null)
            return NotFound();

        var slugExists = await db.Sites.AnyAsync(x => x.Id != Input.Id && x.Slug == Input.Slug);
        if (slugExists)
        {
            ModelState.AddModelError("Input.Slug", "Slug đã tồn tại.");
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Input.PrimaryDomain))
        {
            var domainExists = await db.Sites.AnyAsync(x => x.Id != Input.Id && x.PrimaryDomain == Input.PrimaryDomain);
            if (domainExists)
            {
                ModelState.AddModelError("Input.PrimaryDomain", "Domain đã tồn tại.");
                return Page();
            }
        }

        site.Name = Input.Name;
        site.Slug = Input.Slug;
        site.PrimaryDomain = string.IsNullOrWhiteSpace(Input.PrimaryDomain) ? null : Input.PrimaryDomain;
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
        public Guid Id { get; set; }

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
    }

    public sealed record ModuleOption(string Code, string Name, string? Description);
}
```

- [ ] **Step 6: Create Sites edit view**

Write `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites/Edit.cshtml`:

```cshtml
@page "{id:guid}"
@model NewsCMS.Web.Areas.Admin.Pages.Sites.EditModel
@{
    ViewData["Title"] = "Sửa site";
}

<form method="post" class="space-y-6">
    @Html.AntiForgeryToken()
    <input asp-for="Input.Id" type="hidden" />

    <div class="flex items-center justify-between">
        <div>
            <p class="text-sm font-semibold text-indigo-600">Sites</p>
            <h2 class="mt-1 text-2xl font-extrabold tracking-tight text-slate-950">Sửa site</h2>
        </div>
        <button type="submit" class="admin-btn-primary">Lưu thay đổi</button>
    </div>

    <div asp-validation-summary="ModelOnly" class="rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700"></div>

    <div class="grid gap-6 lg:grid-cols-[1fr_360px]">
        <section class="admin-card p-6">
            <h3 class="text-lg font-bold text-slate-950">Thông tin site</h3>
            <div class="mt-5 grid gap-5 md:grid-cols-2">
                <label class="space-y-2 md:col-span-2">
                    <span class="admin-label">Tên site</span>
                    <input asp-for="Input.Name" class="admin-input" />
                    <span asp-validation-for="Input.Name" class="text-sm text-red-600"></span>
                </label>
                <label class="space-y-2">
                    <span class="admin-label">Slug</span>
                    <input asp-for="Input.Slug" class="admin-input" />
                    <span asp-validation-for="Input.Slug" class="text-sm text-red-600"></span>
                </label>
                <label class="space-y-2">
                    <span class="admin-label">Domain</span>
                    <input asp-for="Input.PrimaryDomain" class="admin-input" />
                    <span asp-validation-for="Input.PrimaryDomain" class="text-sm text-red-600"></span>
                </label>
                <label class="space-y-2">
                    <span class="admin-label">Theme mặc định</span>
                    <input asp-for="Input.DefaultTheme" class="admin-input" />
                    <span asp-validation-for="Input.DefaultTheme" class="text-sm text-red-600"></span>
                </label>
                <label class="flex items-center gap-3 pt-8">
                    <input asp-for="Input.IsActive" class="h-5 w-5 rounded border-slate-300 text-indigo-600 focus:ring-indigo-500" />
                    <span class="admin-label">Kích hoạt site</span>
                </label>
            </div>
        </section>

        <aside class="admin-card p-6">
            <h3 class="text-lg font-bold text-slate-950">Modules</h3>
            <div class="mt-5 space-y-3">
                @foreach (var module in Model.Modules)
                {
                    <label class="flex gap-3 rounded-2xl border border-slate-200 p-4">
                        <input type="checkbox" name="Input.EnabledModuleCodes" value="@module.Code" checked="@Model.Input.EnabledModuleCodes.Contains(module.Code)" class="mt-1 h-5 w-5 rounded border-slate-300 text-indigo-600 focus:ring-indigo-500" />
                        <span>
                            <span class="block font-bold text-slate-900">@module.Name</span>
                            <span class="block text-sm text-slate-500">@module.Description</span>
                        </span>
                    </label>
                }
            </div>
        </aside>
    </div>
</form>
```

- [ ] **Step 7: Build web project**

Run:

```bash
dotnet build NewsCMS.Core/src/NewsCMS.Web/NewsCMS.Web.csproj
```

Expected: build succeeds.

- [ ] **Step 8: Commit**

```bash
git add NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Sites
git commit -m "feat: add super admin site management"
```

---

### Task 8: Convert Core Admin Pages to Tailwind

**Files:**
- Modify: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Index.cshtml`
- Modify: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Posts/Index.cshtml`
- Modify: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Posts/Create.cshtml`
- Modify: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Posts/Edit.cshtml`
- Modify: `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Media/Upload.cshtml`

- [ ] **Step 1: Replace dashboard view**

Replace `NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Index.cshtml` with Tailwind card/table markup:

```cshtml
@page
@model IndexModel
@{
    ViewData["Title"] = "Dashboard";
}

<div class="grid gap-5 md:grid-cols-2 xl:grid-cols-4">
    <section class="admin-card p-6">
        <div class="text-sm font-semibold text-slate-500">Tổng bài viết</div>
        <div class="mt-3 text-4xl font-extrabold tracking-tight text-slate-950">@Model.TotalPosts</div>
    </section>
    <section class="admin-card p-6">
        <div class="text-sm font-semibold text-slate-500">Chuyên mục</div>
        <div class="mt-3 text-4xl font-extrabold tracking-tight text-slate-950">@Model.TotalCategories</div>
    </section>
    <section class="admin-card p-6">
        <div class="text-sm font-semibold text-slate-500">Người dùng</div>
        <div class="mt-3 text-4xl font-extrabold tracking-tight text-slate-950">@Model.TotalUsers</div>
    </section>
    <section class="admin-card p-6">
        <div class="text-sm font-semibold text-slate-500">Form submission</div>
        <div class="mt-3 text-4xl font-extrabold tracking-tight text-slate-950">@Model.TotalSubmissions</div>
    </section>
</div>

<section class="admin-card mt-6 overflow-hidden">
    <div class="border-b border-slate-100 px-6 py-5">
        <h2 class="text-lg font-bold text-slate-950">Bài viết gần đây</h2>
    </div>
    <table class="w-full min-w-[760px] text-left text-sm">
        <thead class="bg-slate-50 text-xs font-bold uppercase tracking-wide text-slate-500">
            <tr><th class="px-6 py-4">Tiêu đề</th><th class="px-6 py-4">Chuyên mục</th><th class="px-6 py-4">Trạng thái</th><th class="px-6 py-4">Ngày tạo</th></tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
            @if (Model.RecentPosts.Count == 0)
            {
                <tr><td colspan="4" class="px-6 py-10 text-center text-slate-500">Chưa có bài viết nào.</td></tr>
            }
            @foreach (var p in Model.RecentPosts)
            {
                <tr><td class="px-6 py-4 font-semibold text-slate-900">@p.Title</td><td class="px-6 py-4 text-slate-600">@p.CategoryName</td><td class="px-6 py-4 text-slate-600">@p.Status</td><td class="px-6 py-4 text-slate-600">@p.CreatedAt.ToString("dd/MM/yyyy HH:mm")</td></tr>
            }
        </tbody>
    </table>
</section>
```

- [ ] **Step 2: Convert posts index**

Replace Bootstrap classes in `Posts/Index.cshtml` with the same patterns as Sites index: `admin-card`, `admin-input`, `admin-select`, `admin-btn-primary`, `admin-btn-secondary`, table header `bg-slate-50`, row padding `px-5 py-4`, badge `admin-badge bg-slate-100 text-slate-700`.

Use this exact button replacement:

```cshtml
<a asp-area="Admin" asp-page="/Posts/Create" class="admin-btn-primary">Thêm bài</a>
```

Use this exact filter input replacement:

```cshtml
<input class="admin-input" name="q" value="@Model.Keyword" placeholder="Tìm theo tiêu đề..." />
```

Use this exact select replacement:

```cshtml
<select class="admin-select" name="cat">
```

- [ ] **Step 3: Convert create/edit forms**

For `Posts/Create.cshtml` and `Posts/Edit.cshtml`, replace:

- `row g-3` with `grid gap-6 lg:grid-cols-[1fr_360px]`.
- `card shadow-sm` with `admin-card`.
- `card-body` with `p-6`.
- `form-control` with `admin-input`.
- `form-select` with `admin-select`.
- `btn btn-primary w-100` with `admin-btn-primary w-full`.
- `btn btn-outline-secondary` with `admin-btn-secondary`.
- `text-danger small` with `text-sm text-red-600`.
- `text-muted` with `text-slate-500`.

Keep existing JavaScript and TinyMCE section unchanged.

- [ ] **Step 4: Convert media upload page**

Open `Media/Upload.cshtml`, preserve form action/handler names, replace Bootstrap layout/classes with `admin-card`, `admin-input`, `admin-btn-primary`, and slate text classes.

- [ ] **Step 5: Rebuild CSS and build web project**

Run:

```bash
npm run css:admin --prefix NewsCMS.Core/src/NewsCMS.Web
dotnet build NewsCMS.Core/src/NewsCMS.Web/NewsCMS.Web.csproj
```

Expected: CSS compile succeeds; web build succeeds.

- [ ] **Step 6: Commit**

```bash
git add NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Index.cshtml NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Posts/Index.cshtml NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Posts/Create.cshtml NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Posts/Edit.cshtml NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/Media/Upload.cshtml NewsCMS.Core/src/NewsCMS.Web/wwwroot/css/admin.css
git commit -m "feat: refresh admin pages with tailwind"
```

---

### Task 9: Run App and Verify Browser Flows

**Files:**
- No code changes expected unless verification finds defects.

- [ ] **Step 1: Start app**

Run:

```bash
dotnet run --project NewsCMS.Core/src/NewsCMS.Web/NewsCMS.Web.csproj
```

Expected: app starts and logs a local URL.

- [ ] **Step 2: Verify migration/seed**

Watch startup logs. Expected:

- EF migration applies.
- No seeding exception.
- Super admin available: username `admin`, password `Admin@123`.
- Default site admin available: username `admin-default`, password `Admin@123`.

- [ ] **Step 3: Login as super admin**

Open local admin login page and log in as:

```text
username: admin
password: Admin@123
```

Expected:

- Admin dashboard loads.
- New Tailwind shell visible.
- Sidebar includes `Sites`.

- [ ] **Step 4: Create site**

Go to `/Admin/Sites/Create` and create:

```text
Name: Demo Products Site
Slug: demo-products
Domain: demo-products.local
DefaultTheme: Default
Admin username: admin-demo-products
Admin email: admin@demo-products.local
Admin full name: Demo Products Admin
Admin password: Admin@123
Modules: news checked, products checked
```

Expected: redirect to Sites list; new site appears with Tin tức and Sản phẩm modules.

- [ ] **Step 5: Edit modules**

Open created site edit page. Uncheck products and save.

Expected: Sites list shows only Tin tức for that site.

- [ ] **Step 6: Login as site admin**

Logout. Login as:

```text
username: admin-demo-products
password: Admin@123
```

Expected:

- Admin dashboard loads.
- Sidebar does not show `Sites`.
- Normal content navigation remains visible.

- [ ] **Step 7: Direct access authorization check**

While logged in as `admin-demo-products`, navigate to `/Admin/Sites/Index`.

Expected: access denied/forbidden or redirect according to ASP.NET Core Identity behavior. Site management page must not render.

- [ ] **Step 8: Capture screenshots**

Capture screenshots at:

- Dashboard desktop width 1440.
- Sites list desktop width 1440.
- Create site page desktop width 1440.
- Dashboard mobile width 375.

- [ ] **Step 9: Fix defects found during verification**

If CSS missing, rebuild Tailwind and verify `_AdminLayout.cshtml` links `~/css/admin.css`.

If migration fails due SQL Server filtered index syntax on non-SQL provider, adjust `SiteConfiguration` for provider compatibility or edit migration safely.

If logout route fails, check existing Identity area route and preserve prior working logout form behavior.

- [ ] **Step 10: Commit verification fixes**

If fixes were needed:

```bash
git add <changed-files>
git commit -m "fix: stabilize admin multisite verification"
```

---

### Task 10: Final Quality Gate

**Files:**
- No new files unless fixes are required.

- [ ] **Step 1: Build full solution**

Run:

```bash
dotnet build NewsCMS.Core/NewsCMS.Core.sln
```

Expected: build succeeds.

- [ ] **Step 2: Build admin CSS**

Run:

```bash
npm run css:admin --prefix NewsCMS.Core/src/NewsCMS.Web
```

Expected: build succeeds and `wwwroot/css/admin.css` updates only if source changed.

- [ ] **Step 3: Search for Bootstrap CDN use in admin**

Run:

```bash
rg "bootstrap|bootstrap-icons|cdn.jsdelivr.net/npm/bootstrap" NewsCMS.Core/src/NewsCMS.Web/Areas/Admin
```

Expected: no Bootstrap layout dependency remains. If matches remain only in unrelated comments, remove them.

- [ ] **Step 4: Review security-sensitive defaults**

Check `DbSeeder.cs` and site creation UI:

- Dev seed passwords are still only dev defaults.
- New site creation requires explicit password.
- SuperAdmin-only pages use `[Authorize(Roles = "SuperAdmin")]`.
- Site admin `SiteId` is assigned on creation.

- [ ] **Step 5: Final code review**

Run code review agent on changed files. Required because code was modified.

Prompt:

```text
Review the admin Tailwind + multisite foundation changes. Focus on ASP.NET Core Identity correctness, EF Core relationships/migrations, Razor Pages validation, authorization for SuperAdmin vs site Admin, and UI regressions. Report CRITICAL/HIGH/MEDIUM findings only with file:line and fix.
```

- [ ] **Step 6: Fix blocking review findings**

Fix all CRITICAL and HIGH findings. Re-run `dotnet build` and `npm run css:admin` after fixes.

- [ ] **Step 7: Final summary**

Report:

- Files changed.
- Build result.
- Browser verification result.
- Any intentionally deferred work: full content tenant isolation, products module implementation, deeper feature policy enforcement for future module pages.
```
