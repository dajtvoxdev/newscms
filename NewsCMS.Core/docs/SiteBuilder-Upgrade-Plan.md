# Site Builder — Plan nâng cấp

> **Trạng thái 2026-08-30:** Hoàn thành cả 5 phase.
> - Phase 0-1: chặn mất dữ liệu + canvas load từ CompiledHtml — xong (phiên trước).
> - Phase 2: CSS/JS lẻ theo khối, sửa listener nhân bản — xong. Phase 2.5 (Style
>   Manager mở rộng: Bố cục/Kích thước/Chữ/Trang trí/Hiệu ứng + position/z-index/
>   overflow/text-transform/background-image/transition/transform; nút toolbar
>   undo/redo/xem-code/khung-viền/fullscreen) + Phase 2.6 (khối bố cục: Section/
>   Container/Grid 2-3-4/Tiêu đề/Đoạn văn/Nút/Ảnh/Khoảng trống/Đường kẻ) — xong.
> - Phase 3.2: trait dùng chung (orderBy/skip/layout/columns/showExcerpt/showDate/
>   showAuthor/emptyText) qua `SharedBlockProps.cs` + `SHARED_TRAIT_CATALOG` — xong
>   trên 6 block. Phase 3.3: picker banner-position thành select (post/product đã có).
>   Phase 3.4: preview hiện trạng thái rỗng rõ ràng thay vì khối trắng — xong.
> - Phase 4: shell dropdown + xem shell, SEO, Lịch sử — xong (phiên trước).
>   Phase 4.3 (CssDirty): chọn (a) — cờ chỉ set lúc tạo qua MCP, tự gỡ khi lưu,
>   KHÔNG chặn render/không hiện lỗi ở đâu → đã đúng tinh thần "cảnh báo mềm",
>   không cần sửa thêm.
> - Còn lại (thủ công, ngoài code): smoke test sau `restart-app.bat`; dọn trang
>   trùng lặp trước khi test shell (mục "Dữ liệu trùng lặp" cuối file).

Ngày lập: 2026-08-28. Trigger: trang chủ Chu Kafe
(`/Admin/Builder/Edit/62f71ab0-60e5-4d18-b5a5-4bed3a81b212`) mở ra canvas trắng,
không sửa được từng khối, không có CSS/JS riêng cho khối, không cấu hình được
hiển thị dữ liệu.

---

## 1. Chẩn đoán

### 1.1 Bằng chứng dữ liệu

```sql
SELECT Id, Slug, Version, CssDirty,
       LEN(ISNULL(BuilderJson,''))  lenBJ,
       LEN(ISNULL(CompiledHtml,'')) lenHtml,
       LEN(ISNULL(CompiledCss,''))  lenCss
FROM Pages;
```

Kết quả: **19/19 trang có `lenBJ = 0`**. Trang chủ Chu Kafe:
`slug=""`, `Version=18`, `CssDirty=1`, `lenBJ=0`, `lenHtml=10831`, `lenCss=0`,
`LayoutId=NULL`.

### 1.2 Chuỗi nhân quả canvas trắng

| Bước | Vị trí | Sự việc |
|---|---|---|
| 1 | `SiteBuilderApi.cs:362-380`, `:403-415` | `site_apply` ghi `CompiledHtml/CompiledCss/CustomCss/CustomJs/LayoutId` nhưng **không ghi `BuilderJson`**. Chỉ đọc nó ở `:395` để snapshot revision. |
| 2 | `IBuilderPageService.cs:7-19` | `BuilderPageDto` **không có field `CompiledHtml`** → GET API không trả HTML về client. |
| 3 | `Edit.cshtml:317` | `if (project && project.pages && project.pages.length > 0) editor.loadProjectData(project);` → không có `pages` → không load gì. |

### 1.3 Bẫy mất dữ liệu — ƯU TIÊN CAO NHẤT

Canvas trống nhưng nút Lưu vẫn chạy:

- `Edit.cshtml:188` gửi `compiledHtml: editor.getHtml()` → canvas rỗng trả `<body></body>`
- `Edit.cshtml:324` bind `Ctrl+S` vào lệnh lưu
- `BuilderPageService.cs:142` ghi đè `page.CompiledHtml` vô điều kiện

→ **Một lần bấm Lưu / Ctrl+S trên màn hình đang trắng sẽ xoá sạch HTML trang.**
Hiện chưa mất vì `PageRevisions` có snapshot, nhưng phải chặn trước khi làm gì khác.

### 1.4 Các lỗi/thiếu sót khác

| # | Vấn đề | Vị trí |
|---|---|---|
| 4 | `canvas: { styles: [] }` — iframe không nạp `/css/site-utilities.css` lẫn token CSS, trong khi `PageRenderer.cs:311-312` nạp cả hai cho trang public → canvas không bao giờ giống trang thật | `Edit.cshtml:278` |
| 5 | `customCss` phình vô hạn: mỗi lần lưu prepend toàn bộ `nc-builder.css` (260 dòng); lần load sau `setCode()` đọc lại rồi prepend tiếp | `Edit.cshtml:169-179`, `nc-builder-extensions.js:763` |
| 6 | Builder không bao giờ ghi `LayoutId` → không gán được shell từ builder | `BuilderPageService.cs:139-147` |
| 7 | `CompiledCss=0` + `CssDirty=1` trên mọi trang; chỉ trình duyệt compile được CSS, server không có đường tự sinh | `nc-tailwind.js:137-148` |
| 8 | CSS/JS **chỉ có mức trang** (modal `#nc-code-modal`); không có mức khối | `nc-builder-extensions.js:461-557` |
| 9 | Trait khối động dựng lại toàn bộ collection mỗi `component:selected` và đăng ký `traitsColl.on('change:value')` không gỡ → nhân bản listener | `nc-builder-extensions.js:314-364` |
| 10 | 5 sector / 24 property Style Manager; không Layer Manager, Selector Manager, panel undo/redo/preview | `Edit.cshtml:269-276` |
| 11 | `blocks` chỉ có 6 khối động, không có khối bố cục cơ bản (section/grid/cột/nút/ảnh) | `nc-builder-extensions.js:96-172` |

### 1.5 Quyết định kiến trúc

**`CompiledHtml` là nguồn sự thật; `BuilderJson` chỉ là cache của GrapesJS.**

Mọi thứ đang chạy — MCP `site_apply`, `PageRenderer`, `PageRevisions`,
`DynamicBlockRenderer` — đều đi qua `CompiledHtml`. Đi backfill `BuilderJson`
sẽ phải viết bộ chuyển HTML→project-data và duy trì hai nguồn sự thật vĩnh viễn.

→ Chọn hướng: **builder load từ `CompiledHtml`**, `BuilderJson` chỉ dùng làm
cache tăng tốc khi hợp lệ. GrapesJS `editor.setComponents(html)` parse HTML
thành component tree đầy đủ và chạy `isComponent` của mọi custom type, nên
`data-nc-block` vẫn được nhận diện.

---

## Phase 0 — Chặn mất dữ liệu (làm ngay, ~1h)

Không có tính năng mới, chỉ dựng rào.

1. **`Edit.cshtml` — chặn lưu khi canvas rỗng**
   Trong `saveProject()`, trước khi build payload:
   ```js
   var html = editor.getHtml();
   if (!hadContentOnLoad === false && isEffectivelyEmpty(html)) {
       setStatus('error', 'Canvas trống — từ chối lưu để không xoá nội dung trang.');
       return false;
   }
   ```
   `isEffectivelyEmpty` = strip `<body>`/`<div>` wrapper rỗng, `trim()` còn `''`.
   Cờ `hadContentOnLoad` set từ kết quả `loadProject()`.

2. **`BuilderPageService.UpdateAsync` — guard phía server**
   Nếu `request.CompiledHtml` rỗng/chỉ có wrapper **và** `page.CompiledHtml`
   đang có nội dung → `Result.Failure("Từ chối ghi đè bằng nội dung rỗng.")`.
   Đây là lưới an toàn cho cả đường API/MCP, không chỉ UI.

3. **Snapshot trước mọi `UpdateAsync`**
   Hiện chỉ `PublishAsync` và `site_apply` tạo `PageRevision`. Thêm snapshot vào
   `UpdateAsync` khi `CompiledHtml` thay đổi, `Note = "auto: trước khi lưu nháp"`.

4. **Xoá bẫy `Ctrl+S`** cho tới khi Phase 1 xong (`Edit.cshtml:324`).

**Nghiệm thu:** mở builder trang chủ, bấm Lưu → báo lỗi rõ ràng, `lenHtml` trong
DB vẫn 10831.

---

## Phase 1 — Canvas hiển thị đúng nội dung thật (~1 ngày)

Mục tiêu: mở `/Admin/Builder/Edit/62f71ab0…` thấy đúng trang chủ Chu Kafe như
trang public.

1. **Trả HTML về client**
   Thêm `string? CompiledHtml` vào `BuilderPageDto` (`IBuilderPageService.cs:7`)
   và vào cả hai projection ở `BuilderPageService.cs:52` + `:235`.
   Lưu ý: `ListAsync` không nên trả HTML (nặng) — tách `BuilderPageListItemDto`
   riêng, hoặc `null` HTML trong list.

2. **Fallback load trong `Edit.cshtml:306-327`**
   ```js
   if (project && project.pages && project.pages.length > 0) {
       editor.loadProjectData(project);
   } else if (data.compiledHtml) {
       editor.setComponents(data.compiledHtml);
       editor.setStyle(data.compiledCss || '');
   }
   ```
   `setComponents` chạy `isComponent` → mọi `data-nc-block` được gán đúng type,
   trait panel hoạt động ngay. Bỏ hẳn comment sai ở
   `nc-builder-extensions.js:228-233` ("LoadProjectData đi đường khác").

3. **Nạp đúng CSS vào canvas** (`Edit.cshtml:278`)
   ```js
   canvas: { styles: ['/css/site-utilities.css', TOKEN_CSS_URL, '/css/nc-builder.css'] }
   ```
   Khớp chính xác thứ tự của `PageRenderer.BuildHtml` (`:310-313`).
   Bỏ đoạn tự append `<link id="nc-builder-css-link">` ở
   `nc-builder-extensions.js:746-755` (thành thừa).

4. **Sửa bug `customCss` phình vô hạn** (`Edit.cshtml:169-179`)
   Ngừng nhồi `nc-builder.css` vào `customCss`. Thay bằng: `PageRenderer` luôn
   nạp `/css/nc-builder.css` như một link tĩnh khi HTML trang có `ncv-*`
   (kiểm ở server, một lần). `customCss` từ nay chỉ chứa CSS do người dùng gõ.
   Kèm script dọn dữ liệu cũ: strip mọi block `nc-builder.css` đã bị nhồi.

5. **Render dynamic block trong canvas**
   Sau `setComponents`, duyệt các node `[data-nc-block]` gọi
   `/admin/api/builder/block-preview/{key}` để hiện nội dung thật thay vì
   placeholder rỗng. Dùng lại `schedulePreview()` sẵn có
   (`nc-builder-extensions.js:244`).

6. **Bật lại `Ctrl+S`.**

**Nghiệm thu:** canvas hiển thị hero + các section Chu Kafe, giống ảnh
`chukafe-home-final.png`; Lưu → `lenHtml` không giảm; xem trang public không đổi.

---

## Phase 2 — Sửa được từng khối (~2 ngày)

Mục tiêu: click một section → panel bên phải có Nội dung / Kiểu / CSS riêng /
JS riêng của **chính khối đó**.

1. **Chuẩn hoá section: `data-nc-section`**
   Định nghĩa component type `nc-section` cho mọi `<section>` / `<div>` cấp 1
   trong wrapper. Mỗi section được cấp một id ổn định `data-nc-sid="s-xxxxxxx"`
   (sinh khi thiếu, giữ nguyên khi có). Đây là khoá để gắn CSS/JS riêng.

2. **CSS riêng theo khối**
   - Trait mới `nc-block-css` mở CodeMirror (tái dùng hạ tầng modal ở
     `nc-builder-extensions.js:461-557`, tách thành hàm dùng chung).
   - Lưu vào `data-nc-css` trên chính element (base64 hoặc JSON-escaped) →
     đi theo `CompiledHtml`, không cần cột DB mới, `site_apply` và revision
     tự động mang theo.
   - Lúc lưu: client gom mọi `data-nc-css`, wrap trong
     `[data-nc-sid="s-xxx"] { … }` (scope tự động, không rò rỉ ra khối khác),
     nối vào `compiledCss`.
   - Canvas: inject `<style id="nc-sec-css">` cập nhật realtime khi gõ.

3. **JS riêng theo khối**
   - Cùng cơ chế, attribute `data-nc-js`, **gate bằng `Builder.Code.Manage`**
     (client ẩn tab, server chặn ở `BuilderPageApiController.cs:43-45` — mở rộng
     kiểm tra sang cả `CompiledHtml` chứa `data-nc-js`).
   - Lúc lưu: gom thành các IIFE có scope
     `(function(root){ … })(document.querySelector('[data-nc-sid="s-xxx"]'))`
     nối vào `customJs` để `PageRenderer.cs:326` nhả ra cuối `<body>` với nonce.
   - **Không** chạy JS trong canvas builder (giữ nguyên nguyên tắc ở
     `nc-builder-extensions.js:574-575`).
   - `ContentSanitizer.SanitizeBuilder` phải whitelist `data-nc-css` /
     `data-nc-js` / `data-nc-sid`, nếu không nó strip mất. **Kiểm tra điểm này
     trước khi code.**

4. **Sửa bug listener nhân bản** (`nc-builder-extensions.js:314-364`)
   Chuyển sang đăng ký `traitsColl.on('change:value')` **một lần** ở scope
   editor, đọc component từ `editor.getSelected()`. Bỏ `propListener()` rỗng
   ở `:367`.

5. **Panel builder đầy đủ** (`Edit.cshtml`)
   Bật Layer Manager (điều hướng cây khối), Selector Manager (class), panel
   undo/redo/preview/xem-code. Mở rộng Style Manager: thêm sector Flex/Grid,
   position, transition, và các property còn thiếu (`border-width`,
   `text-transform`, `overflow`, `z-index`, `background-image`).

6. **Thư viện khối bố cục**
   Bổ sung vào `BlockManager`: Section, Container, Grid 2/3/4 cột, Heading,
   Text, Nút, Ảnh, Khoảng trống, Divider — dựng bằng class Tailwind + token
   (`var(--color-brand-500)`, `var(--radius-card)`) để bám design system.

**Nghiệm thu:** chọn hero Chu Kafe → đổi màu nền qua Style Manager, thêm CSS
riêng `@media (max-width:640px){…}`, Lưu, Xuất bản → trang public đổi đúng, các
section khác không bị ảnh hưởng.

---

## Phase 3 — Cấu hình hiển thị dữ liệu (~1.5 ngày)

1. **Sửa trait khối động thành ổn định** — đã nêu ở Phase 2 mục 4. Sau đó test
   đủ 6 khối: `post-list`, `news-grid`, `product-grid`, `coffee-bean-grid`,
   `category-list`, `banner-slider`.

2. **Bổ sung trait dùng chung cho mọi khối động:**
   `orderBy` (mới nhất / xem nhiều / thủ công), `skip` (phân trang/lệch),
   `layout` (grid / list / carousel), `columns`, `showExcerpt`, `showDate`,
   `showAuthor`, `emptyText` (chuỗi hiện khi không có dữ liệu).
   Server: đọc trong `DynamicBlockContext.PropsJson`, mỗi
   `IDynamicBlock.RenderAsync` xử lý. Có sẵn 8 block ở
   `Infrastructure/Builder/Blocks/`.

3. **Picker thay vì gõ tay:** modal chọn chuyên mục / bài / sản phẩm / banner cụ
   thể. Mở rộng `BuilderPickersApiController.cs` (hiện chỉ có `categories`).
   Trait `type: 'nc-picker'`.

4. **Preview đáng tin trong canvas:** thay `previewPlaceholderHtml`
   (`nc-builder-extensions.js:236`) bằng render thật ngay khi thả khối; hiện
   trạng thái rỗng rõ ràng ("Chuyên mục X chưa có bài") thay vì khối trắng —
   đây là lý do người dùng tưởng builder hỏng.

5. **`site_apply` giữ props:** kiểm tra `ContentSanitizer` không nuốt
   `data-nc-props` khi qua `SiteBuilderApi.cs:348`.

**Nghiệm thu:** thả `news-grid`, chọn chuyên mục, đổi số bài 3→6, canvas cập
nhật ngay và trang public khớp.

---

## Phase 4 — Shell, CSS server-side, SEO (~2 ngày)

1. **Gán layout từ builder**
   Thêm `Guid? LayoutId` vào `BuilderPageSaveRequest`; `UpdateAsync` ghi
   (`BuilderPageService.cs:139-147`). Dropdown shell trên topbar.
   Sửa dữ liệu: trang chủ Chu Kafe đang `LayoutId = NULL`.

2. **Xem shell trong canvas (chế độ ngữ cảnh)**
   Bọc nội dung page trong `CompiledHtml` của shell (chỉ để xem, khoá không
   sửa được — `editable:false, selectable:false`), đúng như
   `PageRenderer.ReplaceMarkerContent` (`:359`). Có toggle bật/tắt.
   Đây là thứ khiến builder "giống trang thật" thay vì một khung trống.

3. **Xoá nợ `CssDirty`**
   Hiện `CompiledCss=0` + `CssDirty=1` trên **tất cả** trang: trang tạo qua MCP
   không có CSS utility riêng, phụ thuộc hoàn toàn `/css/site-utilities.css`.
   Hai lựa chọn:
   - (a) Chấp nhận: `site-utilities.css` là bundle precompile đủ dùng → đổi
     `CssDirty` thành cảnh báo mềm, không phải cờ lỗi.
   - (b) Job nền quét trang `CssDirty=1`, chạy Tailwind CLI server-side sinh
     `CompiledCss`.
   → Khuyến nghị **(a)** trước (rẻ, đúng thực tế), (b) chỉ khi thực sự thấy
   thiếu utility.

4. **SEO + revision trong builder**
   `BuilderSeoApiController` và `GetRevisionsAsync`/`RestoreRevisionAsync` đã
   có server-side nhưng **không có UI trong màn hình Edit**. Thêm 2 nút topbar:
   "SEO" (title/description/og:image) và "Lịch sử" (danh sách version + khôi
   phục). Đây là cách thoát hiểm khi lỡ ghi đè.

5. **`site_apply` ghi `BuilderJson = null` tường minh** khi HTML đổi
   (`SiteBuilderApi.cs:404`), để cache cũ không mâu thuẫn với HTML mới.

---

## Thứ tự thực thi

```
Phase 0  ── chặn mất dữ liệu           1h    ← làm trước tiên, không đàm phán
Phase 1  ── canvas hiển thị đúng        1d    ← giải quyết "trắng tinh"
Phase 2  ── sửa từng khối + CSS/JS lẻ   2d    ← giải quyết yêu cầu chính
Phase 3  ── cấu hình dữ liệu          1.5d
Phase 4  ── shell / SEO / revision      2d
```

## Rủi ro cần canh

- **`ContentSanitizer.SanitizeBuilder`** là điểm chết người: nó strip attribute
  ngoài whitelist. `data-nc-sid` / `data-nc-css` / `data-nc-js` phải được thêm
  vào whitelist **trước** khi code Phase 2, nếu không mọi thứ im lặng biến mất
  lúc lưu.
- **`data-nc-props`** dùng `DeEntitizeValue` chứ không `GetAttributeValue`
  (`DynamicBlockRenderer.cs:48`) — attribute mới chứa JSON/CSS phải theo đúng
  quy ước escape này.
- **`editor.setComponents`** có thể chuẩn hoá lại HTML (self-closing tag, thứ tự
  attribute). Cần test round-trip: load → lưu ngay → diff `CompiledHtml`. Nếu
  lệch nhiều, cân nhắc chỉ ghi `CompiledHtml` khi `changesCount > 0`.
- **Dữ liệu trùng lặp đang tồn tại**: có `Trang chủ (slug="")`,
  `Trang chủ (home)`, `Trang chủ (trang-chu)`, `home-archive-35a0e527`,
  `home-deleted-d44887f2`, 2 bản `Liên hệ`, 4 bản `shell-default`. Nên dọn
  trước Phase 4 để test layout không nhiễu.
