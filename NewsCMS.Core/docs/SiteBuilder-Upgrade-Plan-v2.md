# Site Builder — Plan nâng cấp v2 (module dữ liệu & trải nghiệm cấu hình)

Ngày lập: 2026-08-30. Tiếp nối `SiteBuilder-Upgrade-Plan.md` (5 phase đã xong).

Trigger: (a) canvas trang chủ Chu Kafe vỡ bố cục — một ảnh khổng lồ đè lên cả trang;
(b) yêu cầu "kéo module tin tức / sản phẩm / thư viện ảnh vào rồi cấu hình cách hiển thị,
chọn chuyên mục hoặc tất cả" hiện còn khó dùng.

---

## 0. Đã sửa trong phiên này

### 0.1 Canvas vỡ bố cục — lệch realm giữa trang admin và iframe

**Bằng chứng.** Trong canvas: `masonryInited = true`, `is-pin-ready` đã gắn, nhưng
`Masonry.items.length === 0`, `.chu-pin-grid` cao `0px`, cả 17 `.chu-pin-item`
(`position:absolute`) không có inline `left/top` nên chồng nhau tại góc trên-trái của
ancestor có `position` gần nhất → tràn ra ngoài section.

**Nguyên nhân.** GrapesJS dựng DOM canvas bằng `document` của **trang admin** rồi append
sang iframe. Node được adopt nhưng **giữ nguyên prototype của realm cha**, nên trong iframe:

```
el instanceof iframeWindow.HTMLElement  → false
el instanceof topWindow.HTMLElement     → true
```

`fizzy-ui-utils.filterFindElements` (dùng chung bởi Masonry / imagesLoaded / Isotope) lọc
phần tử con bằng đúng phép thử `elem instanceof HTMLElement` → loại sạch 17 item.

**Cách sửa.** `ncBridgeRealms()` trong `Areas/Admin/Pages/Builder/Edit.cshtml`, gọi ở bước 0
của `ncHydrateCanvas()` — trước khi nạp vendor script. Định nghĩa `Symbol.hasInstance` cho
các constructor DOM của iframe để chấp nhận **cả hai** realm. Không gán đè global
(`win.HTMLElement = parent.HTMLElement`) vì như thế node do chính iframe tạo — `new Image()`,
`doc.createElement()` — lại thành "không hợp lệ".

Sau khi sửa: `gridH = 1795px`, item xếp 4 cột, `left: 0% / 24.99% / 49.99% / 74.99%`,
không còn phần tử nào tràn ra ngoài `body` của canvas.

> Chỉ ảnh hưởng canvas builder. Trang public chạy một realm duy nhất nên chưa bao giờ dính
> lỗi này — đó là lý do bug chỉ thấy trong builder.

### 0.2 Catalog block lệch giữa server và client

`DynamicBlockRegistry` có 8 khối, `dynamicBlockDefs()` trong
`wwwroot/js/admin/nc-builder-extensions.js` chỉ khai báo 6. Hai khối thiếu
(`pdf-flipbook`, `breadcrumb`) không được nhận diện nên **không có preview, không có trait,
không kéo-thả được** — trên trang chủ nó hiện ra như một hộp rỗng cao 40px.

Đã thêm hai def còn thiếu. Sau khi sửa, flipbook render đúng trong canvas
(`.chu-flip` cao 579px, `page-flip` chạy được nhờ bước 3 của `ncHydrateCanvas`).

Đây là **triệu chứng**, không phải bệnh — xem mục 2.1.

### 0.3 Quan sát chưa xử lý: cờ "Có thay đổi chưa lưu" luôn bật

Mở trang xong, chưa thao tác gì, `#nc-status-text` đã là "Có thay đổi chưa lưu" ở mọi mốc
3s / 6s / 10s / 16s. Nguyên nhân: `editor.on('change:changesCount', markDirty)`
(`Edit.cshtml:458`) bắt luôn các thay đổi do chính `loadCanvas()` và `ncHydrateCanvas()` sinh
ra. Hệ quả: chỉ báo trạng thái mất ý nghĩa và `noticeOnUnload` cảnh báo sai mỗi lần rời trang.
→ Phase 1 (quick win).

---

## 1. Chẩn đoán "khó dùng, thiếu linh hoạt"

| # | Điểm nghẽn | Bằng chứng |
|---|---|---|
| 1 | **Catalog block khai báo 2 nơi.** Thêm khối server phải nhớ sửa tay file JS; quên là khối biến mất khỏi builder. | `DynamicBlockRegistry` (8 khối) vs `nc-builder-extensions.js` (6 khối trước khi sửa) |
| 2 | **Không có "kiểu hiển thị" cho khối dữ liệu.** Chỉ có trait thô: `layout` (grid/list/carousel) + `columns` + 3 checkbox. Người dùng phải tự mường tượng kết quả. | `SharedBlockProps.ContainerStyle()` |
| 3 | **Bộ lọc nguồn dữ liệu quá mỏng.** Chỉ `categorySlug` (một chuyên mục) + `count` + `orderBy`; `featuredOnly` chỉ có ở 2/6 khối. Không có: nhiều chuyên mục, tag, chọn tay bài/sản phẩm, gồm chuyên mục con, loại trừ. | Bảng props ở mục 3.3 |
| 4 | **Không có khối Thư viện ảnh.** 8 khối động hiện có không khối nào lấy dữ liệu từ `Medias`/`MediaFolders`. | `Builder/Blocks/*.cs` |
| 5 | **Panel cấu hình là trait mặc định của GrapesJS.** Danh sách phẳng, không nhóm, không xem trước; select chuyên mục là single-select. | `nc-builder-extensions.js` phần traits |
| 6 | **`orderBy: "manual"` không có UI chọn tay.** Server đã hiểu giá trị này nhưng chỉ diễn giải thành "nổi bật trước / theo SortOrder" — người dùng không tự sắp được. | `PostListBlock.cs:39`, `ProductGridBlock.cs:36` |

---

## 2. Phương án nâng cấp

### Phase 1 — Quick wins (0,5 ngày)

1. **Tắt cờ dirty giả.** Chỉ gắn `change:changesCount` sau khi `loadCanvas()` + lần hydrate
   đầu hoàn tất; đặt lại `dirty = false` và `setStatus('saved', …)` ngay sau đó.
2. **Test chống hồi quy realm.** Một test Playwright mở builder trang chủ, khẳng định
   `.chu-pin-grid` cao > 0 và không phần tử nào tràn khỏi `body` canvas — chốt lại 0.1.
3. **Test parity catalog.** Test so `DynamicBlockRegistry.All` với danh sách key trong
   `nc-builder-extensions.js`; lệch là fail — chốt lại 0.2 cho tới khi Phase 2 xoá hẳn nguồn
   trùng lặp.

### Phase 2 — Một nguồn sự thật cho catalog block (2 ngày) ⭐ nền tảng

Cho `IDynamicBlock` **tự mô tả** thay vì mô tả rải rác ở JS.

```csharp
public interface IDynamicBlock
{
    string Key { get; }
    BlockDescriptor Descriptor { get; }          // MỚI
    Task<string> RenderAsync(DynamicBlockContext ctx, CancellationToken ct = default);
}

public sealed record BlockDescriptor(
    string Label,                  // "Danh sách bài viết"
    string Category,               // "Dữ liệu" | "Bố cục" | "Nội dung"
    string Description,
    string IconSvg,
    IReadOnlyList<BlockPresetDescriptor> Presets,   // Phase 3
    IReadOnlyList<BlockPropDescriptor> Props);      // type/name/label/options/default/min/max/group
```

- Endpoint mới `GET /admin/api/builder/blocks` trả cả catalog (đã điền sẵn options động cho
  chuyên mục / vị trí banner — gộp luôn 3 lần fetch của `loadPickers()`).
- `nc-builder-extensions.js` dựng palette + traits **từ response**, xoá
  `dynamicBlockDefs()`.
- `ListBlocksAsync` (MCP `site_list_blocks`) trả `PropsSchemaJson` sinh từ chính
  `Descriptor.Props` thay vì `null` cho khối code-first → agent MCP và builder UI dùng chung
  một mô tả.

**Đây là phase phải làm trước**, vì Phase 3–5 đều thêm props/preset và sẽ nhân đôi công việc
nếu catalog còn khai báo hai nơi.

### Phase 3 — Preset "kiểu hiển thị" (2 ngày)

Mỗi khối dữ liệu khai báo vài preset đặt tên sẵn; chọn preset = set một lượt các prop bên dưới,
vẫn chỉnh tay được sau đó.

```csharp
new BlockPresetDescriptor(
    Key: "featured-hero",
    Label: "1 bài lớn + 4 bài nhỏ",
    ThumbSvg: "…",
    Props: new() { ["layout"] = "featured", ["count"] = 5, ["showExcerpt"] = true });
```

Preset đề xuất theo khối:

| Khối | Preset |
|---|---|
| `post-list` / `news-grid` | Lưới 3 cột · Lưới 4 cột · Danh sách ngang (ảnh trái) · Carousel · 1 lớn + 4 nhỏ · Danh sách chỉ tiêu đề |
| `product-grid` | Lưới 4 cột · Lưới 3 cột ảnh lớn · Carousel · Masonry (kiểu `chu-pin-grid`) |
| `gallery` (Phase 5) | Masonry · Lưới vuông · Carousel · Justified rows |
| `category-list` | Chip ngang · Thẻ có ảnh · Danh sách dọc |

- `SharedBlockProps.NormalizeLayout` mở rộng thêm `featured` và `masonry`;
  `ContainerStyle()` sinh CSS tương ứng.
- **Masonry phải render bằng CSS `column-count`, không dùng JS.** Layout do JS quyết định là
  thứ đã gây ra bug 0.1 và luôn mong manh trong canvas WYSIWYG.
- UI: trait type mới `nc-preset-picker` — lưới thumbnail SVG, tái dùng CSS của
  `nc-style-variant` (`nc-variant-wrap` / `nc-variant-opt` trong `nc-builder.css`).

### Phase 4 — Bộ lọc nguồn dữ liệu (2–3 ngày)

Chuẩn hoá vào một `BlockDataFilter` đọc chung như `SharedBlockProps`:

| Prop | Kiểu | Ý nghĩa |
|---|---|---|
| `categorySlugs` | `string[]` | Nhiều chuyên mục; rỗng = tất cả (thay thế `categorySlug`, vẫn đọc key cũ để tương thích ngược) |
| `includeChildCategories` | `bool` | Gộp chuyên mục con |
| `tagSlugs` | `string[]` | Lọc theo tag |
| `featuredOnly` | `bool` | Nâng lên dùng chung cho mọi khối (nay chỉ 2/6 khối có) |
| `itemIds` | `Guid[]` | Chọn tay — làm cho `orderBy: "manual"` có nghĩa thật |
| `excludeIds` | `Guid[]` | Loại trừ (vd không lặp bài đang xem) |
| `publishedWithinDays` | `int` | Chỉ lấy bài mới trong N ngày |

- Endpoint picker mở rộng: `GET /admin/api/builder/pickers/tags`,
  `GET /admin/api/builder/pickers/items?type=post&q=…` (tìm kiếm cho picker chọn tay).
- Trait type mới: `nc-multi-select` (chip nhiều lựa chọn) và `nc-item-picker`
  (ô tìm kiếm + danh sách kéo sắp thứ tự).
- Tương thích ngược: `categorySlug` (số ít) trong `data-nc-props` cũ vẫn phải chạy — mọi trang
  hiện có đều đang dùng key này.

### Phase 5 — Khối Thư viện ảnh (1,5 ngày)

`GalleryBlock : IDynamicBlock`, key `gallery`, đọc từ `Medias` / `MediaFolders`.

| Prop | Ghi chú |
|---|---|
| `folderId` / `folderPath` | Lấy cả thư mục |
| `mediaIds` | Chọn tay từng ảnh (dùng `nc-item-picker` của Phase 4) |
| `count`, `columns`, `gap` | |
| `preset` | masonry / square-grid / carousel / justified |
| `lightbox` | Bật xem phóng to |
| `showCaption` | Hiện chú thích từ `Media.Title` / `Alt` |

- Masonry bằng `column-count` (xem Phase 3).
- Lightbox: dùng lại `swiper-bundle` đã vendor sẵn tại `/js/vendor/`, **không thêm lib mới**.
- Phải phát `<img loading="lazy" width height>` để không gây layout shift, giống các khối ảnh
  hiện có.

### Phase 6 — Panel cấu hình khối (2 ngày)

Thay panel trait phẳng bằng một Inspector riêng cho khối động:

1. **Nhóm rõ ràng:** *Kiểu hiển thị* (preset) → *Nguồn dữ liệu* (bộ lọc) → *Hiển thị gì*
   (checkbox) → *Nâng cao*.
2. **Đếm kết quả sống:** endpoint preview trả kèm `matchedCount` để hiện
   "Khớp 12 bài, hiển thị 6" ngay trên panel — giải quyết trường hợp lọc ra 0 kết quả mà không
   biết vì sao.
3. **Preview có debounce** (đã có 250ms) + skeleton thay vì nháy trắng.
4. **Nút "Nhân bản khối"** và **"Lưu thành preset của site"** (ghi vào `BlockDefinitions`,
   bảng này đã tồn tại và đã hỗ trợ `SiteId` null = toàn cục).

---

## 3. Phụ lục

### 3.1 Thứ tự đề xuất

`Phase 1` → `Phase 2` → `Phase 3` → `Phase 4` → `Phase 5` → `Phase 6`.

Phase 2 là điều kiện tiên quyết. Phase 3 mang lại cảm nhận "dễ dùng" rõ nhất trên mỗi giờ bỏ ra
nên nên làm ngay sau đó. Phase 5 (thư viện ảnh) độc lập — có thể chen lên trước Phase 4 nếu cần
gấp cho một site cụ thể.

Tổng: ~10–11 ngày công.

### 3.2 Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| Đổi shape props làm hỏng trang đang chạy | Mọi prop mới đều tuỳ chọn; reader vẫn đọc key số ít cũ. Chạy `site_preview_page` cho mọi trang published trước khi merge |
| Preset sinh CSS ngoài safelist `site-utilities.css` | Preset phát **inline style** như các khối hiện tại, không phát class Tailwind mới |
| Layout phụ thuộc JS lại vỡ trong canvas | Quy tắc: khối động không được dựa vào JS để có bố cục. JS chỉ để tăng cường (lightbox, autoplay) |
| Catalog server-driven làm chậm mở builder | Gộp 3 fetch picker hiện tại vào 1 request catalog → ít request hơn hiện nay |

### 3.3 Props các khối động tại thời điểm lập plan

| Khối | Props riêng | Props dùng chung (`SharedBlockProps`) |
|---|---|---|
| `post-list` | count, categorySlug | orderBy, skip, layout, columns, showExcerpt, showDate, showAuthor, emptyText |
| `news-grid` | count, categorySlug, minCardWidth, ctaLabel, featuredOnly | ↑ |
| `product-grid` | count, categorySlug | ↑ |
| `coffee-bean-grid` | count, categorySlug, featuredOnly | ↑ |
| `category-list` | type | ↑ |
| `banner-slider` | position, limit | ↑ |
| `pdf-flipbook` | src, title, eyebrow, intro, downloadUrl, downloadLabel, eagerPages, showControls | — |
| `breadcrumb` | path | — |
