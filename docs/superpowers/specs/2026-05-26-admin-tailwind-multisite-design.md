# Admin Tailwind + Multi-site Foundation Design

## Goal

Rebuild the NewsCMS admin interface without Bootstrap, using Tailwind for a consistent management UI. Add a global super admin model, per-site admin accounts, and per-site feature/module switches so future modules such as products can be enabled only for selected sites.

## Scope

In scope:

- Replace Bootstrap-based admin layout with Tailwind-powered admin shell.
- Standardize admin sidebar, topbar, cards, tables, forms, buttons, badges, and empty states.
- Add `Site` data model.
- Add optional site ownership on `AppUser`.
- Keep global `SuperAdmin` role and site-scoped `Admin` role.
- Add per-site feature flags/modules.
- Seed a default site, global super admin, and default site admin.
- Add super admin UI for site management and module enable/disable.

Out of scope for this phase:

- Full multi-tenant content isolation across all content tables.
- Public theme redesign.
- Product module implementation.
- Billing or plan management.

## Admin UI Design

The current admin shell in `NewsCMS.Web/Areas/Admin/Shared/_AdminLayout.cshtml` uses Bootstrap CDN and inline styles. The new shell will use a local Tailwind build owned by `NewsCMS.Web`.

Visual direction: modern editorial CMS dashboard, light workspace, dark structured sidebar, high contrast content panels, restrained blue/indigo primary color, clear status colors.

Admin shell:

- Fixed-width dark sidebar with grouped navigation.
- Topbar with page title, current user, current site context, and logout.
- Main content on soft gray background.
- Shared card/table/form/button styles via Tailwind utility classes and small Razor partial conventions.
- Responsive sidebar behavior for tablet/mobile where practical.

Initial surfaces to update:

- Shared admin layout.
- Sidebar navigation.
- Dashboard.
- Posts index/create/edit.
- Media upload.
- TinyMCE wrapper styling.

## Site and Account Model

Add `Site` entity under site domain:

- `Id`
- `Name`
- `Slug`
- `PrimaryDomain`
- `DefaultTheme`
- `IsActive`
- `CreatedAt`
- `UpdatedAt`

Update `AppUser`:

- Add `Guid? SiteId`.
- `SiteId == null` means global/system user, intended for super admin.
- `SiteId != null` means user belongs to one site.

Roles:

- `SuperAdmin`: global role, full platform access, cannot be deleted.
- `Admin`: site admin role, manages assigned site.
- Existing `Editor` and `Author` remain for future site-scoped editorial permissions.

Seeding:

- Ensure default site exists.
- Ensure global super admin exists with `SiteId == null`.
- Ensure default admin exists for default site with role `Admin`.

## Feature Flags / Site Modules

Add module catalog and site feature assignment.

Recommended data shape:

- `FeatureModule`
  - `Id`
  - `Code` such as `news`, `products`
  - `Name`
  - `Description`
  - `IsSystem`
  - `SortOrder`
- `SiteFeatureModule`
  - `SiteId`
  - `FeatureModuleId`
  - `IsEnabled`
  - `EnabledAt`

Initial seeded modules:

- `news` enabled by default for default site.
- `products` disabled by default, present as future-ready module.

Super admin can enable/disable modules per site. Site admins only see nav/features enabled for their site. Disabled modules should not appear in sidebar and should block direct access later when module-specific pages exist.

## Admin Pages

Add super admin site management area:

- `/Admin/Sites/Index`: list sites, status, domain, enabled modules, admin account summary.
- `/Admin/Sites/Create`: create site and default site admin.
- `/Admin/Sites/Edit`: update site metadata, active status, enabled modules.

Create site flow:

1. Super admin enters site name, slug/domain/theme.
2. Super admin enters default admin email, username, temporary password.
3. System creates `Site`.
4. System creates site admin user with `SiteId` and role `Admin`.
5. System enables selected modules.

Default behavior if admin fields are omitted during seed only:

- Username: `admin-{slug}`.
- Email: `admin@{slug}.local`.
- Temporary password remains config-driven or seed-only dev default.

## Authorization Rules

- `SuperAdmin` can access site management pages and all admin modules.
- Site `Admin` can access normal admin pages for assigned site.
- Site `Admin` cannot access site management pages.
- Feature-gated pages should check whether the user's current site has the module enabled.

This phase may gate sidebar visibility first and add deeper page policies for newly added module pages. Full content-level tenant enforcement is reserved for the later multi-tenant phase.

## Data Flow

Login:

- User signs in through existing Identity flow.
- Admin layout reads user identity and role.
- If user is super admin, current context is platform/global.
- If user has `SiteId`, admin context is assigned site.

Site creation:

- Razor Page handler validates input.
- EF Core transaction creates site, admin user, role assignment, and module assignments.
- On failure, transaction rolls back and shows validation/error message.

Feature visibility:

- Sidebar checks role and enabled site modules.
- Super admin sees platform section and can see module controls.
- Site admin sees only enabled modules for their site.

## Error Handling

- Validate duplicate slug/domain before creating site.
- Validate duplicate admin email/username before creating admin user.
- Show friendly validation messages in Razor Pages.
- Use Identity error messages for password/user creation failures.
- Use EF Core transaction for site + admin + modules so partial site creation does not persist.

## Testing and Verification

Build checks:

- `dotnet build` for solution/project.
- Tailwind build command for admin CSS.

Manual browser verification:

- Login as super admin.
- Verify redesigned admin shell.
- Verify site list/create/edit.
- Create site with admin account.
- Enable `news`, disable `products`.
- Login as site admin.
- Verify platform site management is hidden/blocked.
- Verify enabled module navigation appears and disabled module navigation does not.

Future tests:

- Unit/integration coverage for site creation service.
- Authorization tests for super admin vs site admin.
- Feature flag visibility tests.

## Migration Notes

Existing database gains new site/module tables and `Users.SiteId`. Existing super admin remains global with null `SiteId`. Existing admin/editor/author users without a site should be reviewed later; for this phase they can remain global until assigned.
