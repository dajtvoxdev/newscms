# Hướng dẫn: Thêm một theme/landing page mới

NewsCMS.Core thiết kế để bạn không bao giờ phải sửa code core khi tạo landing page mới. Mọi giao diện công khai đều là một **Razor Class Library (RCL)** implement interface `IThemeModule`.

## Bước 1 - Tạo project theme

```bash
cd src
dotnet new razorclasslib -n NewsCMS.Theme.MySite
```

Sau đó mở file `NewsCMS.Theme.MySite.csproj` và thay khối `<Project>` cho gọn:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <AddRazorSupportForMvc>true</AddRazorSupportForMvc>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\NewsCMS.Application\NewsCMS.Application.csproj" />
    <ProjectReference Include="..\NewsCMS.Shared\NewsCMS.Shared.csproj" />
  </ItemGroup>
</Project>
```

## Bước 2 - Thêm vào solution và tham chiếu từ Web

```bash
dotnet sln NewsCMS.Core.sln add src/NewsCMS.Theme.MySite/NewsCMS.Theme.MySite.csproj
dotnet add src/NewsCMS.Web/NewsCMS.Web.csproj reference src/NewsCMS.Theme.MySite/NewsCMS.Theme.MySite.csproj
```

## Bước 3 - Tạo `ThemeStartup`

```csharp
public class ThemeStartup : IThemeModule
{
    public string Name => "MySite";
    public string DisplayName => "Trang tin My Site";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection s) { /* service riêng nếu cần */ }

    public void ConfigureRoutes(IEndpointRouteBuilder e)
    {
        e.MapControllerRoute("home", "", new { area = "Theme", controller = "Home", action = "Index" });
    }
}
```

## Bước 4 - Tạo Controller + View

Tổ chức theo Area `Theme`:

```
NewsCMS.Theme.MySite/
└── Areas/Theme/
    ├── Controllers/
    │   ├── HomeController.cs
    │   └── PostController.cs
    └── Views/
        ├── _ViewStart.cshtml
        ├── _ViewImports.cshtml
        ├── Home/Index.cshtml
        ├── Post/Detail.cshtml
        └── Shared/_Layout.cshtml
```

Controller inject `IPostService`, `IMenuService`, `IBannerService`... để lấy dữ liệu.

## Bước 5 - Kích hoạt theme

`src/NewsCMS.Web/appsettings.json`:

```json
"Site": {
  "ActiveTheme": "MySite"
}
```

Hoặc đổi từ trang Admin → Cấu hình → Site → ActiveTheme.

## Bước 6 - Build và chạy

```bash
dotnet build
dotnet run --project src/NewsCMS.Web
```

Mở `https://localhost:5001/` — bạn sẽ thấy landing page mới. `/admin` vẫn dùng UI quản trị mặc định của core.

## Mẹo

- **Override view của theme khác**: copy file vào `Areas/Theme/Views/...` cùng đường dẫn, view của theme đang active sẽ thắng nhờ `ThemeViewLocationExpander`.
- **Static asset**: file trong `wwwroot/` của RCL truy cập qua `/_content/{ProjectName}/css/...`.
- **Có nhiều theme cùng lúc**: tham chiếu nhiều RCL, đổi `ActiveTheme` để chuyển nhanh - hữu ích khi A/B test hoặc dựng nhiều site dùng chung core.
- **Theme có entity riêng**: tự tạo DbContext + migration của theme, hoặc kế thừa `AppDbContext` bằng `partial`.
