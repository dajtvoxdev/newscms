# NewsCMS.Core

Bộ core CMS cho các trang tin tức / landing page, viết bằng **ASP.NET Core 8 + EF Core + Razor Pages**. Khi bắt đầu một dự án mới, bạn chỉ cần:

1. Clone (hoặc add submodule) bộ core này vào solution.
2. Tạo CSDL trống, cấu hình connection string trong `appsettings.json`.
3. Chạy `dotnet ef database update` để tạo schema + seed SuperAdmin.
4. `dotnet run --project src/NewsCMS.Web` → có ngay trang `/admin` đầy đủ.
5. Tạo một Razor Class Library mới làm theme/landing rồi set `ActiveTheme` trong appsettings.

## Cấu trúc solution

```
NewsCMS.Core/
├── src/
│   ├── NewsCMS.Domain/          Entity, enum, interface thuần
│   ├── NewsCMS.Application/     Service, DTO, validator
│   ├── NewsCMS.Infrastructure/  EF Core, Identity, Storage, Email
│   ├── NewsCMS.Shared/          Constants (Permissions), Theming contracts
│   ├── NewsCMS.Web/             Host: Areas/Admin (Razor Pages) + landing fallback
│   └── NewsCMS.Theme.Default/   Theme mẫu Bootstrap 5 (Razor Class Library)
├── tests/
├── docs/
└── NewsCMS.Core.sln
```

## Module có sẵn

| Module | Mô tả |
|--------|-------|
| Identity & RBAC | User/Role/Permission, đăng nhập, đổi mật khẩu, 2FA |
| Content | Post, Category, Tag, Media library, **editor TinyMCE 7** + sanitizer + auto-slug |
| Site | Menu, Banner, Page tĩnh, Site settings |
| SEO | Meta tags theo entity, sitemap.xml, robots.txt, redirect |
| Form | Form builder + submission |
| Audit | Log mọi CRUD quan trọng |

Phần soạn bài: xem `docs/Editor.md`. Lưu trữ media: upload thẳng vào `wwwroot/uploads/`, URL lưu DB.

## Tạo landing page mới

Xem `docs/AddingNewTheme.md` (hoặc tài liệu kiến trúc đi kèm `NewsCMS_Core_KienTruc_Roadmap.docx`).

Tóm tắt:

```bash
dotnet new razorclasslib -n NewsCMS.Theme.MySite -o src/NewsCMS.Theme.MySite
dotnet sln add src/NewsCMS.Theme.MySite
dotnet add src/NewsCMS.Web reference src/NewsCMS.Theme.MySite
```

Sau đó implement `IThemeModule` trong `ThemeStartup.cs` và set:

```json
"Site": { "ActiveTheme": "MySite" }
```

## Lệnh hữu ích

```bash
# Restore + build
dotnet restore
dotnet build

# Tạo migration mới
dotnet ef migrations add <Name> --project src/NewsCMS.Infrastructure --startup-project src/NewsCMS.Web

# Cập nhật DB
dotnet ef database update --project src/NewsCMS.Infrastructure --startup-project src/NewsCMS.Web

# Chạy
dotnet run --project src/NewsCMS.Web

# Test
dotnet test
```

## Tài khoản mặc định (sau khi seed)

- Username: `admin`
- Password: `Admin@123` (**đổi ngay sau lần đăng nhập đầu tiên**)

## License

MIT — tự do sử dụng cho dự án cá nhân và thương mại.
