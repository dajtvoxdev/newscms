# Khảo sát nhận thức — Section + Admin Config

**Date:** 2026-05-28
**Scope:** Theme `HaiLuuNguoc` + Admin Razor Page

## Goal

Bổ sung section "Khảo sát nhận thức" dưới `#pledge-wall` (trang `/cam-ket`) cho phép embed Google Form trực tiếp hoặc nút CTA mở tab mới. Admin có trang cấu hình: bật/tắt, đổi URL, sửa nội dung text.

## Non-goals

- Image generation API runtime (illustration đã được sinh sẵn 1 lần qua API, lưu thành asset tĩnh `wwwroot/survey-bottle.jpg`).
- Lưu trữ history nhiều version khảo sát (chỉ giữ cấu hình hiện tại; mỗi sự kiện admin đổi giá trị tại chỗ).
- Permission code riêng (giai đoạn đầu chỉ `[Authorize]`, đồng nhất với Commitments).

## Architecture

### Storage — SiteSetting key-value (group=`survey`)

Tận dụng `ISiteSettingService` đã có. Keys:

| Key | Type | Default | Note |
|---|---|---|---|
| `survey.enabled` | bool | `false` | Toggle hiện section |
| `survey.displayMode` | string | `"iframe"` | `iframe` \| `link` |
| `survey.url` | string | `""` | URL Google Form (mode `iframe` cần `?embedded=true`) |
| `survey.title` | string | `"Chia sẻ góc nhìn của bạn"` | Title (Serif) |
| `survey.subtitle` | string | `"Hải Lưu Ngược mong muốn lắng nghe bạn"` | Sub (italic) |
| `survey.description` | string | `"Khảo sát chỉ mất khoảng 2-3 phút..."` | Mô tả ngắn |
| `survey.ctaLabel` | string | `"Làm khảo sát"` | Label nút (khi displayMode=`link`) |

Lý do chọn SiteSetting:
- Hạ tầng có sẵn (`AppDbContext.SiteSettings`, `SiteSettingService`).
- Không cần migration.
- Phù hợp YAGNI: chỉ 1 row config, không cần entity riêng.

### Theme (public)

**File:** `NewsCMS.Theme.HaiLuuNguoc/Areas/Theme/Views/Commitment/Index.cshtml`

Chèn ngay sau `</section>` của `#pledge-wall`. Render có điều kiện theo `Model.Survey.Enabled`.

Struct:

```cshtml
<section class="survey-section" id="khao-sat" aria-labelledby="survey-title">
  <div class="survey-card">
    <img class="survey-illustration"
         src="~/_content/NewsCMS.Theme.HaiLuuNguoc/survey-bottle.jpg"
         alt="" loading="lazy" />
    <p class="eyebrow">Khảo sát · Hải Lưu Ngược</p>
    <h2 id="survey-title">@Model.Survey.Title</h2>
    <p class="survey-subtitle">@Model.Survey.Subtitle</p>
    <p class="survey-desc">@Model.Survey.Description</p>

    @if (Model.Survey.DisplayMode == "iframe" && !string.IsNullOrWhiteSpace(Model.Survey.Url))
    {
      <div class="survey-frame">
        <iframe src="@Model.Survey.Url" loading="lazy" title="Khảo sát"></iframe>
      </div>
    }
    else if (!string.IsNullOrWhiteSpace(Model.Survey.Url))
    {
      <a class="button button-primary survey-cta"
         href="@Model.Survey.Url"
         target="_blank" rel="noopener noreferrer">
         @Model.Survey.CtaLabel <span aria-hidden="true">→</span>
      </a>
    }
  </div>
</section>
```

**CSS additions** vào `wwwroot/css/commitment.css`:

- `.survey-section` — full-bleed, padding `clamp(4rem,3rem+5vw,7rem)` top/bottom, background giữ navy (kế thừa từ pledge-wall).
- `.survey-card` — glassmorphism (`backdrop-filter:blur(20px)`, `bg:rgba(255,255,255,0.06)`, border `1px solid rgba(255,255,255,0.18)`, radius `clamp(20px,2.5vw,32px)`, max-width 760px, center, padding `clamp(2rem,3vw,3.5rem)`). Flexbox column, align-items center.
- `.survey-illustration` — width clamp(96px,12vw,160px), drop-shadow nhẹ.
- `.survey-subtitle` — italic, color teal accent `oklch(78% 0.14 200)`.
- `.survey-desc` — line-height 1.7, max-width 56ch, strong → font-weight 700.
- `.survey-frame iframe` — width 100%, border 0, radius 16px, height clamp(620px,80vw,820px) mobile, 720px desktop. bg white.
- `.survey-cta` — kế thừa `button-primary`, thêm icon arrow.
- `@media (max-width: 640px)` — giảm padding card, illustration shrink.

### Controller

**File:** `NewsCMS.Theme.HaiLuuNguoc/Areas/Theme/Controllers/CommitmentController.cs`

- Inject thêm `ISiteSettingService`.
- Trong `Index()`: gọi `_settings.GetGroupAsync("survey", ct)` → map dict sang `SurveyConfigDto`.
- Thêm property `Survey` vào `CommitmentPageViewModel`.

**New DTO** trong file Controller (record):
```csharp
public sealed record SurveyConfigDto(
    bool Enabled,
    string DisplayMode,
    string Url,
    string Title,
    string Subtitle,
    string Description,
    string CtaLabel);
```

Helper local parse dict → dto với defaults.

### Admin Razor Page

**Folder:** `NewsCMS.Web/Areas/Admin/Pages/Survey/`

**`Index.cshtml.cs`:**
- `[Authorize]`
- Inject `ISiteSettingService`.
- `OnGetAsync`: `_settings.GetGroupAsync("survey")` → bind vào `Input`.
- `OnPostAsync`: validate `Input`, gọi `_settings.SetAsync(key, value, "survey")` cho từng field.
- Validation:
  - `Url`: bắt buộc khi `Enabled=true`. Regex `^https://docs\.google\.com/forms/.+`.
  - `Title`, `Description`, `CtaLabel`: bắt buộc, maxLength 200/1000/80.
  - `DisplayMode` ∈ `{iframe, link}`.
- Success → `TempData["Success"]` + redirect Self.

**`Input` class fields:** `Enabled`, `DisplayMode`, `Url`, `Title`, `Subtitle`, `Description`, `CtaLabel`.

**`Index.cshtml`:**
- Form Tailwind-style giống các trang admin khác (`admin-card`, `admin-btn-primary`).
- Toggle Enabled (checkbox).
- Radio group DisplayMode (`iframe` / `link`).
- Input URL với hint "Dán URL có dạng `https://docs.google.com/forms/.../viewform?embedded=true` cho chế độ nhúng".
- 4 input/textarea cho text fields.
- Submit button.
- Hiển thị `TempData["Success"]` / `TempData["Error"]`.

### Sidebar

**File:** `NewsCMS.Web/Areas/Admin/Shared/_Sidebar.cshtml`

Thêm 1 dòng trong group "Tương tác", ngay dưới link Commitments:

```cshtml
<a class="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold @Active("/admin/Survey")" href="/Admin/Survey">
    <span class="h-2 w-2 rounded-full bg-amber-400"></span>
    Khảo sát nhận thức
</a>
```

## Files Touched

**New:**
- `NewsCMS.Web/Areas/Admin/Pages/Survey/Index.cshtml`
- `NewsCMS.Web/Areas/Admin/Pages/Survey/Index.cshtml.cs`
- `NewsCMS.Theme.HaiLuuNguoc/wwwroot/survey-bottle.jpg` (đã tạo sẵn 78KB)

**Edit:**
- `NewsCMS.Theme.HaiLuuNguoc/Areas/Theme/Controllers/CommitmentController.cs` — thêm survey load.
- `NewsCMS.Theme.HaiLuuNguoc/Areas/Theme/Views/Commitment/Index.cshtml` — thêm section.
- `NewsCMS.Theme.HaiLuuNguoc/wwwroot/css/commitment.css` — thêm survey styles.
- `NewsCMS.Web/Areas/Admin/Shared/_Sidebar.cshtml` — thêm link.

## Error Handling

- Theme: nếu `Enabled=false` → không render section. Nếu `Enabled=true` mà `Url` rỗng → render fallback message "Đang cập nhật".
- Admin POST: ModelState invalid → trả lại form với errors. Service throw → catch + `TempData["Error"]`.
- SiteSettingService đã có sẵn fallback default cho `GetAsync<T>` → safe khi key chưa tồn tại.

## Testing

- Manual smoke: build solution, run, navigate `/cam-ket` → confirm section hiển thị khi enabled.
- Admin: vào `/Admin/Survey` → đổi URL → save → reload public page → confirm thay đổi.
- Iframe mode: paste Google Form URL với `?embedded=true` → confirm load.
- Link mode: switch displayMode → confirm CTA mở tab mới với `target=_blank rel=noopener`.
- Responsive: kiểm tra 320/768/1024/1440.

## Security

- URL validated server-side (regex Google Forms only).
- iframe sandbox: thêm `sandbox="allow-scripts allow-forms allow-same-origin allow-popups"` để hạn chế.
- Anti-forgery token mặc định trên Razor Page POST.
- `rel="noopener noreferrer"` trên external link mode.
