# Site Builder — Plan nâng cấp v3 (trang chi tiết & template theo URL)

Ngày lập: 2026-09-03. Tiếp nối `SiteBuilder-Upgrade-Plan-v2.md`.

**Vấn đề cần giải:** trang chi tiết (`/tin-tuc/{slug}`, `/san-pham/{slug}`) không dựng/chỉnh
được bằng builder. Đề xuất của người dùng: coi chúng là **trang cấp 3 suy ra từ URL** —
`/tin-tuc/chu-kafe-ngon-31232` → "chi tiết của mục `/tin-tuc`".

---

## 0. Hiện trạng — bằng chứng

### 0.1 Trang chi tiết là HTML hardcode trong C#, không đi qua builder

`PageRenderer.RenderPostAsync` và `RenderProductAsync` không đọc bất kỳ `Page` nào; chúng gọi
`BuildPostHtml` / `BuildProductHtml` — hai hàm `StringBuilder` nối chuỗi inline-style:

| Hàm | File |
|---|---|
| `BuildPostHtml(PostView)` | `src/NewsCMS.Infrastructure/Builder/PageRenderer.cs:186` |
| `BuildProductHtml(ProductView)` | `src/NewsCMS.Infrastructure/Builder/PageRenderer.cs:246` |

Cả hai chỉ nhận `layoutId: null` khi gọi `ComposeAsync` (`PageRenderer.cs:102`, `:130`) → trang
chi tiết **luôn dùng shell mặc định**, không nhận được layout riêng của chuyên mục, và bố cục
thân trang thì hoàn toàn cố định. Muốn đổi một chữ cũng phải sửa C# + rebuild.

### 0.2 Hạ tầng "template" đã khai báo nhưng chưa ai đọc

Ba thứ đã có sẵn trong schema mà **không có một chỗ nào đọc**:

| Khai báo | Vị trí | Số nơi đọc |
|---|---|---|
| `PageKind.CategoryTemplate` / `PostTemplate` / `ArchiveTemplate` | `src/NewsCMS.Domain/Enums/PageKind.cs` | 0 |
| `Category.TemplatePageId` | `src/NewsCMS.Domain/Entities/Content/Category.cs:28` | 0 |
| `Category.LayoutId` | `src/NewsCMS.Domain/Entities/Content/Category.cs:30` | 0 |

Tệ hơn: form tạo trang **đã cho chọn** hai loại đó
(`src/NewsCMS.Web/Areas/Admin/Pages/Builder/Create.cshtml:40-41`) — người dùng chọn "Template
bài viết", trang được tạo với `Kind = PostTemplate`, rồi… không ảnh hưởng gì đến việc render.

### 0.3 Khối động không biết "đang ở trang của bài nào"

```csharp
// src/NewsCMS.Application/Builder/IDynamicBlock.cs
public sealed record DynamicBlockContext(Guid SiteId, string Culture, string? PropsJson);
```

Không có entity hiện tại. Hệ quả trực tiếp: `BreadcrumbBlock` ghi trong doc-comment
*"Nếu không truyền path, dùng path hiện tại từ context (caller set)"*
(`Blocks/BreadcrumbBlock.cs:9`) nhưng context không hề có path, nên
`props.GetString("path") ?? "/"` (`:36`) luôn rơi về `/` → breadcrumb chỉ hiện mỗi "Trang chủ".

Đây là **chặn cứng**: không có context này thì không thể có khối "Tiêu đề bài viết",
"Nội dung bài viết", "Giá sản phẩm" — tức là không thể dựng trang chi tiết bằng builder.

Kèm theo, cache key của `DynamicBlockRenderer` là `(siteId, blockKey, propsHash)`
(`DynamicBlockRenderer.cs:50-57`) — **không có entity**. Nếu thêm context mà quên sửa key,
mọi bài viết sẽ hiện nội dung của bài render đầu tiên.

### 0.4 Map URL ↔ chuyên mục không nhất quán

| Loại | Path sinh ra | Nguồn |
|---|---|---|
| Post | `/{category.PathSlug}/{post.Slug}` | `RouteRegistry.cs:128-131` — theo chuyên mục |
| Product | `/san-pham/{product.Slug}` | `RouteRegistry.cs:193` — **hardcode chuỗi** |

`ProductCategory` thậm chí không có `PathSlug` (so với `Category`), nên URL sản phẩm không thể
phản ánh cây chuyên mục dù muốn.

### 0.5 Route Category resolve xong rồi bị vứt

`UniversalController.Render` xử lý `Page` / `Post` / `Product`, còn `Category` rơi vào
`default: return NotFound()` (`UniversalController.cs:82-85`). Nhưng route Category **vẫn đang
được sinh ra** ở 3 chỗ: `BuilderCategoryService.cs:186`, `SiteBuilderApi.cs:308`,
`SiteTemplateService.cs:168`.

Kiểm chứng trên DB thật:

```
universal-spike | Category | /tin-tuc-moi | 288529DC-...     ← route tồn tại, truy cập trả 404
```

### 0.6 Trạng thái DB hiện tại (site chu-kafe)

```
chu-kafe | Page    | /tin-tuc                                        ← listing: dựng được
chu-kafe | Post    | /tin-tuc/cold-brew-hat-cau-dat                  ← chi tiết: hardcode
chu-kafe | Post    | /tin-tuc/mua-ghe-chu-moi-dong-gia-29k
chu-kafe | Product | /san-pham/lotus-arabica                         ← chi tiết: hardcode
chu-kafe | Product | /san-pham/ethiopia
                     (không có route nào cho /san-pham)              ← listing sản phẩm 404
```

Trang `/san-pham` **không tồn tại** — chỉ có chi tiết mà không có trang cha. Đây chính là dấu
hiệu của việc "map category URL chưa hợp lý" mà bạn nhận ra.

### 0.7 Trang chi tiết không có SEO meta

`PageRenderer.BuildHtml` chỉ phát `<title>` (`PageRenderer.cs:369-370`). Không có
`meta description`, `og:*`, `canonical`, JSON-LD — dù `ISeoMetaService.GenerateJsonLdAsync`
đã được viết sẵn và `SeoMetas` đã có dữ liệu (SitemapGenerator đang đọc bảng này). Với trang
bài viết/sản phẩm thì đây là thiếu sót nặng hơn trang landing.

---

## 1. Nguyên nhân gốc

Chỉ có ba, và chúng xếp tầng lên nhau:

1. **Không có bước "URL → chọn template page".** Renderer nhảy thẳng từ `RouteType` sang một
   hàm C# cố định, bỏ qua toàn bộ tầng `Page`.
2. **Không có ngữ cảnh entity trong khối động.** Kể cả khi chọn được template page, các khối
   trong đó không biết phải hiển thị bài nào.
3. **Prefix URL không phải dữ liệu.** `/san-pham` nằm trong mã nguồn chứ không nằm ở đâu đó
   người dùng sửa được, nên "trang cha" của chi tiết sản phẩm không tồn tại như một thực thể.

---

## 2. Mô hình đề xuất

### 2.1 Ý tưởng cốt lõi: template là **trang con của trang listing**

```
/                        Page  (Landing)          "Trang chủ"
└── /tin-tuc             Page  (Landing)          "Tin tức"          ← trang cấp 2, listing
    └── (template)       Page  (PostTemplate)     "Chi tiết bài viết" ← trang cấp 3, KHÔNG có URL riêng
        ⇒ phục vụ mọi URL dạng /tin-tuc/{slug}
└── /san-pham            Page  (Landing)          "Sản phẩm"
    └── (template)       Page  (ProductTemplate)  "Chi tiết sản phẩm"
        ⇒ phục vụ mọi URL dạng /san-pham/{slug}
```

Template page là một `Page` bình thường: mở bằng builder, kéo khối, lưu, publish, có revision —
**không cần thêm một loại màn hình mới nào**. Khác biệt duy nhất: nó không có `SiteRoute` của
riêng mình, và nó chứa các khối "trường dữ liệu" đọc từ entity đang được yêu cầu.

Prefix URL **suy ra từ slug của trang cha**, không lưu trùng lặp. Đổi `/tin-tuc` → `/tin-bai`
thì URL chi tiết đi theo, và `SyncPostRouteAsync` vốn đã tự sinh Redirect 301
(`RouteRegistry.cs:145-157`) nên link cũ không chết.

### 2.2 Thay đổi schema

```csharp
// Page (SiteEntities.cs) — thêm 2 cột
/// <summary>Trang cha trong cây site. Template chi tiết trỏ về trang listing của nó.</summary>
public Guid? ParentPageId { get; set; }
/// <summary>Template mặc định toàn site cho Kind này khi không tìm được template theo trang cha.</summary>
public bool IsDefaultTemplate { get; set; }
```

```csharp
// PageKind — thêm 1 giá trị (CategoryTemplate/PostTemplate/ArchiveTemplate đã có)
ProductTemplate = 6
```

```csharp
// ProductCategory — thêm 1 cột, đồng bộ với Category
public string? PathSlug { get; set; }
public Guid? TemplatePageId { get; set; }
```

Một migration duy nhất. Tất cả cột đều nullable/có default → **không đụng dữ liệu đang chạy**.

### 2.3 Thuật toán resolve (thêm `ITemplateResolver`)

Request `/tin-tuc/cold-brew-hat-cau-dat`:

```
1. RouteRegistry.ResolveAsync(path)          → (Post, postId)      [đã có, đã cache]
2. TemplateResolver.ForPostAsync(postId, path):
   a. parentPath = path.Substring(0, path.LastIndexOf('/'))  = "/tin-tuc"
      parentPageId = ResolveAsync(parentPath) →
          RouteType.Page     → TargetId
          RouteType.Category → Category.TemplatePageId của chuyên mục đó
      → Page WHERE ParentPageId = parentPageId
             AND Kind = PostTemplate AND đã publish AND CompiledHtml khác rỗng
   b. Page WHERE Kind = PostTemplate AND IsDefaultTemplate AND đã publish
   c. null
3. templatePage != null  → render template với RouteContext(...)
   templatePage == null  → BuildPostHtml(post)          ← đường cũ, giữ nguyên
```

Bước (c) là **cam kết tương thích ngược**: site nào chưa tạo template thì hành vi không đổi
một pixel.

> **Sửa so với bản nháp đầu:** bản đầu đặt `post.Category.TemplatePageId` làm bước ưu tiên
> nhất. Sai — doc-comment của chính cột đó (`Category.cs:28`) nói rõ nó là *template **listing**
> cho chuyên mục này*, không phải template chi tiết. Dùng nhầm thì trang bài viết lại render ra
> trang danh sách. Cột này nay chỉ được `ForCategoryAsync` dùng. Nhu cầu "mỗi chuyên mục một
> template chi tiết riêng" đã được `ParentPageId` lo: mỗi chuyên mục có trang listing riêng thì
> template con của nó cũng riêng.

Điều kiện "**CompiledHtml khác rỗng**" ở bước (a)/(b) không phải chi tiết vụn: template rỗng
nguy hiểm hơn không có template — nó thắng bước tra rồi render ra trang trắng, trong khi
"không có template" còn rơi về HTML dựng sẵn.

### 2.3b Trang template không được có URL riêng

`Page.Slug` là bắt buộc và duy nhất, nên template vẫn có slug (`tin-tuc-chi-tiet`) và
`SyncPageRouteAsync` sẽ vô tư sinh `SiteRoute` cho nó. Hậu quả: template truy cập được trực
tiếp → render không entity → mọi khối dữ liệu rỗng → **một trang trắng lọt vào sitemap**.

`RouteRegistry.SyncPageRouteAsync` vì vậy phải bỏ qua trang template (và gỡ route cũ nếu một
trang thường được đổi `Kind` thành template), rồi `Invalidate()` **vô điều kiện** — đổi
`ParentPageId` hay publish một template không đụng tới path nào, nhưng cache
`parentPath → template` thì vẫn phải bỏ.

Cache mapping `template:{siteId}:{culture}:{kind}:{parentPath}` bằng `IMemoryCache` +
`SiteCacheSignal` — đúng pattern `RouteRegistry.ResolveAsync` đang dùng
(`RouteRegistry.cs:38-53`), nên chi phí thêm ở request nóng là 0 query.

Route `Category` (mục 0.5) dùng đúng chuỗi này với `Kind = CategoryTemplate`, khác mỗi việc
fallback cuối là `NotFound()` như hiện nay.

### 2.4 Ngữ cảnh entity cho khối động

```csharp
// IDynamicBlock.cs
public sealed record RouteContext(
    RouteType RouteType,
    Guid EntityId,
    string Slug,
    string Path,
    Guid? CategoryId,
    string? CategorySlug);

public sealed record DynamicBlockContext(
    Guid SiteId, string Culture, string? PropsJson,
    RouteContext? Route = null);          // ← tham số có default: mọi call site cũ vẫn biên dịch
```

`BlockDescriptor` thêm `bool EntityScoped = false`. `DynamicBlockRenderer` nối `EntityId` vào
cache key **chỉ khi** khối khai `EntityScoped` — khối `post-list` / `site-menu` trong cùng
template vẫn dùng chung một cache entry cho mọi bài (`DynamicBlockRenderer.cs:50-57`).

> Bỏ sót chỗ này là bug nghiêm trọng nhất của cả plan: mọi bài viết sẽ hiện tiêu đề của bài
> được render đầu tiên, và nó chỉ lộ ra trên production sau 5 phút cache.

### 2.5 Bộ khối "trường dữ liệu" mới

Nhóm `Category: "Chi tiết"` trong palette — chỉ hữu dụng trong template page:

| Key | Dùng cho | Props chính |
|---|---|---|
| `entity-title` | Post, Product, Category | `tag` (h1/h2), `align`, `size` |
| `entity-image` | Post, Product | `ratio`, `radius`, `fit` |
| `entity-content` | Post, Product | `maxWidth` — xuất `Content`/`Description` đã sanitize |
| `entity-meta` | Post | `showDate`, `showCategory`, `showAuthor`, `showViews` |
| `entity-excerpt` | Post, Product | — |
| `product-price` | Product | `showCompareAt`, `size` |
| `product-gallery` | Product | `preset` (grid/carousel), `lightbox` |
| `product-stock` | Product | `inStockText`, `outOfStockText` |
| `related-posts` | Post | tái dùng `BlockDataFilter`, tự nhét `excludeIds = [current]` |
| `breadcrumb` | tất cả | **nâng cấp**: `path` rỗng → lấy `context.Route.Path` |

Tất cả tuân thủ quy tắc đã chốt ở v2 §3.2: phát **inline style**, không sinh class Tailwind
ngoài safelist, không dựa vào JS để có bố cục.

### 2.6 Prefix sản phẩm thành dữ liệu

`RouteRegistry.cs:193` đổi từ hằng `"/san-pham/"` sang:

1. `ProductCategory.PathSlug` nếu có (opt-in, mặc định null),
2. `SiteSetting["catalog:detailPrefix"]`, **default `"san-pham"`**.

Giữ nguyên default nghĩa là **URL hiện tại không đổi**. Site nào muốn `/cua-hang/{slug}` thì
đổi setting; `SyncProductRouteAsync` đã sẵn logic ghi Redirect 301 khi path đổi
(`RouteRegistry.cs:207-219`) nên chỉ cần một job resync.

---

## 3. Phân phase

### Phase 1 — Hạ tầng resolve + context ✅ **ĐÃ XONG** (2026-09-03)

- Migration: `Page.ParentPageId`, `Page.IsDefaultTemplate`, `PageKind.ProductTemplate`,
  `ProductCategory.PathSlug`, `ProductCategory.TemplatePageId`.
- `RouteContext` + tham số thứ 4 của `DynamicBlockContext`.
- `DynamicBlockRenderer`: cache key theo `EntityScoped`.
- `ITemplateResolver` + `TemplateResolver` (cache theo pattern `RouteRegistry`).
- `PageRenderer.RenderPostAsync` / `RenderProductAsync`: thử template trước, fallback
  `BuildPostHtml` / `BuildProductHtml` nguyên vẹn.
- `UniversalController`: nhánh `RouteType.Category` → `RenderCategoryAsync`.

Kết thúc phase này **chưa ai thấy gì thay đổi** — đó là mục tiêu. Đường cũ vẫn chạy.

**File đã đổi:** `PageKind.cs` (+`PageKindExtensions.IsTemplate`), `SiteEntities.cs`,
`ProductCategory.cs`, `IDynamicBlock.cs` (+`RouteContext`), `BlockDescriptor.cs`
(+`EntityScoped`), `IPageRenderer.cs`, `ITemplateResolver.cs` (mới),
`TemplateResolver.cs` (mới), `DynamicBlockRenderer.cs`, `PageRenderer.cs`, `RouteRegistry.cs`,
`PageConfiguration.cs`, `CatalogConfigurations.cs`, `DependencyInjection.cs`,
`UniversalController.cs`, migration `20260903070615_AddDetailPageTemplates`.

**Kiểm chứng.** 148/148 test xanh (thêm `TemplateResolverTests` 15 ca,
`EntityBlockCacheTests` 4 ca, `DetailPageRenderTests` 10 ca). Test cache theo entity đã được
xác nhận là bắt được lỗi thật: tạm ép `entityKey = "_"` thì đúng một ca đỏ, trả lại thì xanh.
Trên site chu-kafe đang chạy, HTML của `/`, `/tin-tuc`, `/tin-tuc/{slug}`, `/san-pham/{slug}`
**giống hệt trước khi đổi code**, chỉ khác giá trị nonce CSP (vốn ngẫu nhiên mỗi request);
`/san-pham` vẫn 404 và sitemap vẫn đúng 19 URL.

### Phase 2 — Khối trường dữ liệu ✅ **ĐÃ XONG** (2026-09-03)

10 khối ở §2.5. Ưu tiên theo thứ tự: `entity-title` → `entity-image` → `entity-content` →
`entity-meta` → `breadcrumb` (nâng cấp) → `product-price` → `product-gallery` →
`related-posts` → `product-stock` → `entity-excerpt`.

Mỗi khối tự khai `BlockDescriptor` nên palette builder và MCP `site_list_blocks` tự có
(thành quả Phase 2 của v2 — catalog server-driven).

**Đã triển khai:** 9 khối mới (`EntityTitleBlock`, `EntityImageBlock`, `EntityContentBlock`,
`EntityMetaBlock`, `EntityExcerptBlock`, `ProductPriceBlock`, `ProductGalleryBlock`,
`ProductStockBlock`, `RelatedPostsBlock`) + nâng cấp `BreadcrumbBlock` (path rỗng → lấy từ
`context.Route.Path`). Tất cả đăng ký DI (`DependencyInjection.cs:139-147`).

**Review (2026-09-03):** kiểm tra chéo 10/10 khối `EntityScoped: true` trùng khớp với chỗ đọc
`context.Route`; khối không đọc Route không dính cờ. Hai khe hở CSS-injection đã vá:

- `EntityImageBlock`: prop `fit` (thô) và `maxHeight` đổ thẳng vào `style="..."` → allowlist
  cho `fit`, regex chặn payload cho `maxHeight`.
- `EntityTitleBlock`, `ProductPriceBlock`: prop `color` thô → qua `BlockCss.Safe()` (helper mới,
  allowlist ký tự CSS + chặn `url()` / `expression()` / `import`).

Thêm 8 test chống hồi quy cho khe hở này (payload thật bị chặn, giá trị mẫu trong Hint vẫn qua).
117/117 test builder xanh.

**Vấn đề đã biết, chưa xử lý trong phase này** (xem Phase 2.5): các switch
`if RouteType == Post … else if Product …` rải ở 9 khối. Thêm loại nội dung thứ ba phải sửa
mỗi khối một chỗ, và quên thì khối im lặng trả rỗng.

**File:** `src/NewsCMS.Infrastructure/Builder/Blocks/Entity*.cs`, `Product*.cs`,
`BreadcrumbBlock.cs`, `SharedBlockDescriptors.cs`, `BlockCss.cs` (mới),
`tests/NewsCMS.Tests/EntityBlocksTests.cs` (mới, 35 ca).

### Phase 2.5 — Registry loại nội dung ✅ **ĐÃ XONG** (2026-09-04)

**Trigger:** nếu chỉ có Post + Product thì switch 2 nhánh vẫn đọc được. Nhưng hiện tại một
loại mới = **sửa 19 chỗ**:

| File | Số chỗ | Việc |
|---|---|---|
| `TemplateResolver.cs` | 6 | thêm `ForXxxAsync` ~30 dòng, bản sao của `ForPostAsync` |
| `RouteRegistry.cs` | 4 | thêm `SyncXxxRouteAsync` ~55 dòng, bản sao của `SyncPostRouteAsync` |
| `BreadcrumbBlock.cs` | 4 | 2 nhánh switch |
| `UniversalController.cs` | 2 | thêm `case` |
| `EntityTitleBlock` / `Content` / `Image` / `Meta` / `Excerpt` | 2 mỗi file | thêm nhánh `else if (RouteType == ...)` |
| `RouteType`, `PageKind` | enum | thêm member |
| 5 khối `entity-*` × N loại mới | | mỗi khối lại nhân thêm |

Quên một khối thì nó **im lặng trả rỗng** (`EntityContentBlock.cs:105-118` rơi xuống
`return string.Empty`), không exception, không log — đúng bẫy mà v2 §0.2 đã dính với catalog
block khai hai nơi.

**Giải pháp:** registry loại nội dung (cùng pattern `IDynamicBlockRegistry` / `IAiToolRegistry`
đã dùng hai lần trong codebase):

```csharp
public interface IContentType
{
    string Key { get; }                  // "post" | "product" | "event"
    RouteType RouteType { get; }
    PageKind TemplateKind { get; }
    Task<ContentDetail?> LoadAsync(Guid id, CancellationToken ct);
    Task<string> BuildPathAsync(Guid id, CancellationToken ct);
}

public sealed record ContentDetail(
    Guid Id, string Slug, string Title, string? Body, string? Excerpt,
    string? ImageUrl, int? ImageWidth, int? ImageHeight, string? ImageAlt,
    DateTime? PublishedAt, Guid? CategoryId, string? CategorySlug, string? CategoryName,
    IReadOnlyDictionary<string, object?> Extra);   // giá, tồn kho, gallery…

public sealed class ContentTypeRegistry : IContentTypeRegistry { … }
```

**Sau đó:**

- **5 khối `entity-*` bỏ sạch switch**, đọc thẳng `detail.Title`/`detail.Body`. Thêm loại mới,
  chúng chạy ngay, không sửa dòng nào — đây là chỗ ăn tiền nhất.
- **`TemplateResolver`**: 3 `ForXxxAsync` → 1 `ForEntityAsync`. `PageRenderer`: 3 → 1.
  `RouteRegistry`: 3 `Sync*` → 1.
- Khối chuyên biệt (`product-price`, `product-gallery`, `product-stock`) khai
  `SupportedTypes = ["product"]` → **ẩn khỏi palette** khi template sai loại. Hiện nay kéo
  `product-price` vào template bài viết thì nó im lặng rỗng (`ProductPriceBlock.cs:102`).
- `RouteType` giữ nguyên là enum (persist dạng string trong `SiteRoutes`); thêm `EntityType`
  tự do thì phải migration + rewrite dữ liệu, không đáng.

Thêm loại mới sau đó = **1 class + 1 dòng DI + 2 enum member** thay vì 13 file.

**Nên làm trước Phase 3** (chứ không phải sau): Phase 3 (UI cây trang) và Phase 4 (SEO) đều
đụng đúng những switch này — để sau là sửa hai lần, và số khối `entity-*` chỉ tăng thêm.

**Không làm nếu** bạn chắc Post + Product là hết — switch 2 nhánh chưa thành gánh, chi phí chỉ
thực sự cắn từ loại thứ ba trở đi.

**File:** `src/NewsCMS.Application/Builder/IContentType.cs` (mới),
`src/NewsCMS.Infrastructure/Builder/ContentTypes/` (mới), `src/NewsCMS.Infrastructure/Builder/Blocks/Entity*.cs`
(sửa), `TemplateResolver.cs`, `RouteRegistry.cs`, `UniversalController.cs`, `DependencyInjection.cs`.

#### Kết quả triển khai

Mục tiêu chính **đạt**: 5 khối `entity-*` + `BreadcrumbBlock` đã bỏ sạch switch
`if Post … else if Product …`, nay chỉ gọi `_contentTypes.LoadDetailAsync(...)`. Khối không còn
nhận `AppDbContext`. `ForEntityAsync` hợp nhất 3 hàm `ForXxxAsync`. Thêm `IContentTypeRegistry`
với **cache `ContentDetail` theo request** — 6 khối trên một trang chi tiết chỉ còn 1 query
(cải tiến ngoài plan, tốt). 9 test `ContentTypeRegistryTests`.

#### Review 2026-09-04 — 3 lỗi phải vá

**1. App không khởi động được — mọi trang 500.** `BlockDescriptor.cs:37` bị cắt giữa chừng
(thiếu tham số `SupportedTypes` và dấu `);`) khiến **solution không build**. Sau khi vá cú pháp,
app vẫn chết ngay lúc dựng DI:

```
InvalidOperationException: Unable to activate type 'EntityTitleBlock'.
The following constructors are ambiguous
```

Nguyên nhân: 5 khối `entity-*` được thêm constructor thứ hai `(AppDbContext)` bên cạnh
`(IContentTypeRegistry)`. Cả hai cùng một tham số và cùng resolve được → `ServiceProvider` không
chọn được. **Toàn bộ test vẫn xanh** vì test `new` khối trực tiếp, không đi qua DI — đúng khoảng
mù mà `BuilderDiWiringTests` (mới, 4 ca) nay bịt lại. Đã gỡ 5 constructor shim.

**2. Bản vá CSS-injection của Phase 2 bị revert.** `BlockCss.cs` còn trong repo nhưng **không nơi
nào gọi `BlockCss.Safe`**; `color` / `maxHeight` / `fit` quay lại đổ thẳng vào `style="..."`.
8 test chặn payload cũng bị xoá cùng lúc (`EntityBlocksTests` 35 → 26 ca). Đã vá lại cả 4 chỗ và
khôi phục test (nay 36 ca), kèm ghi chú trong test để lần refactor sau không lặp lại.

**3. `SupportedTypes` khai mà chưa ai đọc.** 4 khối khai `SupportedTypes: ["product"|"post"]`
nhưng `BuilderBlocksApiController` chưa serialize và palette JS chưa lọc — kéo "Giá sản phẩm" vào
template bài viết vẫn im lặng trả rỗng. Đúng loại bẫy đã gặp ở Phase 1 với
`Category.TemplatePageId`. **Chưa làm** — thuộc Phase 3 (UI builder), đã ghi vào đó.

#### Còn nợ: danh sách content type vẫn khai ở 5 nơi

Sau khi gỡ 5 shim, còn **5 bản sao** hardcode `[PostContentType, ProductContentType,
CategoryContentType]`: `PageRenderer`, `RouteRegistry`, `TemplateResolver` (đều ở nhánh fallback
của tham số `IContentTypeRegistry? contentTypes = null`), `BreadcrumbBlock`, và
`BuilderRenderFixture` (test).

Nghĩa là lời hứa "thêm loại mới = 1 class + 1 dòng DI" **chưa đạt** — vẫn là 1 class + 1 dòng DI
+ 4 danh sách phải nhớ sửa. Trong production các fallback là code chết (DI luôn inject), nhưng
`GoogleFontTests` và `ShellRenderingTests` đang chạy vào chúng, nên mỗi nơi tự dựng một registry
riêng ⇒ cache per-request không được chia sẻ.

**Đề xuất (nhỏ, ~1 giờ):** bỏ giá trị mặc định `= null` của tham số `contentTypes`, để
constructor bắt buộc; sửa 2 test đó truyền `fx.ContentTypes`. Khi ấy danh sách chỉ còn ở
`DependencyInjection.cs` và fixture test.

#### Kiểm chứng

139/139 test builder xanh (thêm `BuilderDiWiringTests` 4 ca, `EntityBlocksTests` 26 → 36).
Test DI đã xác nhận bắt được lỗi thật: dựng lại constructor gây ambiguity → 3 ca đỏ; gỡ ra → xanh.
Build Release 0 lỗi; app khởi động lại được và `/`, `/tin-tuc`, `/tin-tuc/{slug}`,
`/san-pham/{slug}` đều trả 200.

### Phase 3 — Trải nghiệm admin (2 ngày)

1. **Cây trang.** `Builder/Index.cshtml` hiện phân cấp theo `ParentPageId`, badge `Template`
   thay cho cột `Kind` phẳng hiện tại (`Index.cshtml:47`).
2. **Tạo template.** Nút *"Thêm trang chi tiết"* ngay trên dòng trang listing → tạo sẵn
   `ParentPageId` + `Kind` đúng, mở thẳng builder. Bỏ dropdown `Kind` rời rạc ở
   `Create.cshtml:37-42` (nguồn nhầm lẫn hiện tại).
3. **Xem trước với dữ liệu thật.** Template page mở trong builder cần một entity mẫu:
   dropdown *"Xem trước với: [bài viết]"* dùng `GET /admin/api/builder/pickers/posts`
   (đã tồn tại); id gửi kèm vào `POST /admin/api/builder/block-preview/{key}` →
   `BlockPreviewApiController` dựng `RouteContext`. Không có mẫu → khối hiện placeholder
   *"(Tiêu đề bài viết)"* thay vì rỗng.
4. **Gắn template cho chuyên mục.** Ô chọn `TemplatePageId` + `LayoutId` trong form sửa
   chuyên mục — hai cột đã có sẵn từ đầu, chỉ thiếu UI.
5. **Lọc palette theo `SupportedTypes`** (nợ từ Phase 2.5). 4 khối đã khai
   `SupportedTypes: ["product"|"post"]` nhưng chưa ai đọc: `BuilderBlocksApiController.Serialize`
   phải trả trường này, và palette JS ẩn/làm mờ khối không hợp với `Kind` của template đang mở.
   Không có nó, kéo "Giá sản phẩm" vào template bài viết chỉ ra một khoảng trống im lặng.

**File:** `Builder/Index.cshtml(.cs)`, `Create.cshtml(.cs)`, `Edit.cshtml`,
`BlockPreviewApiController.cs`, `wwwroot/js/admin/nc-builder-extensions.js`,
form chuyên mục.

### Phase 4 — SEO cho trang chi tiết (1 ngày)

- `PageRenderer.ComposeAsync` nhận `(entityType, entityId)`, gọi `ISeoMetaService` →
  phát `meta description`, `og:title/description/image/type`, `twitter:card`, `canonical`,
  và JSON-LD từ `GenerateJsonLdAsync` (đã viết sẵn, chưa ai gọi).
- Fallback khi chưa có `SeoMeta`: description ← `post.Excerpt` / `product.ShortDescription`,
  og:image ← ảnh đại diện, canonical ← `route.Path`.
- Áp cho **cả** trang landing lẫn trang chi tiết — hiện landing cũng đang thiếu.

**File:** `PageRenderer.cs`, `SeoMetaService.cs`.

### Phase 5 — MCP + prefix dữ liệu (1 ngày)

- `PageSpec` thêm `ParentSlug`, `IsDefaultTemplate` (`ISiteTemplateService.cs:25`);
  `SiteBuilderApi.ApplyPagesAsync` xử lý — thứ tự apply phải đặt trang cha trước con.
- `CategorySpec` thêm `TemplatePageSlug`.
- `site_preview_page` nhận `sampleSlug` tuỳ chọn để preview template.
- Prefix sản phẩm theo §2.6 + lệnh resync route.

**File:** `ISiteTemplateService.cs`, `SiteBuilderApi.cs`, `SiteBuilderMcpTools.cs`,
`RouteRegistry.cs`.

### Phase 6 — Dọn dẹp & di trú (0,5 ngày)

- Tạo trang `/san-pham` cho chu-kafe (đang 404) + template chi tiết cho `/tin-tuc` và
  `/san-pham`, dựng lại đúng giao diện `BuildPostHtml` / `BuildProductHtml` hiện tại bằng
  khối builder → so ảnh trước/sau.
- Sau khi xác nhận, đánh dấu `BuildPostHtml` / `BuildProductHtml` là fallback legacy
  (giữ code, thêm ghi chú) — **không xoá**, vì `abc-test` và `universal-spike` vẫn dựa vào.

**Tổng: ~9 ngày công.**

---

## 3b. Review tổng thể — 2026-09-04

Rà toàn bộ Phase 1 → 6 sau khi báo hoàn thành. Build 12 project 0 lỗi, 162/162 test builder xanh,
app khởi động và mọi loại trang trả 200.

### Đã kiểm chứng còn nguyên (không bị revert như lần trước)

| Hạng mục | Trạng thái |
|---|---|
| `BlockCss.Safe` lọc CSS injection | còn ở 3 khối (`EntityImage`, `EntityTitle`, `ProductPrice`) |
| Constructor shim `(AppDbContext)` gây DI ambiguity | đã sạch, `BuilderDiWiringTests` canh |
| Khối `entity-*` bỏ switch theo loại | còn nguyên, dùng `LoadDetailAsync` |
| `RouteRegistry` hợp nhất `SyncEntityRouteAsync` | xong, path do `IContentType.BuildPathAsync` sinh |
| Prefix sản phẩm thành dữ liệu (§2.6) | xong, ở `ProductContentType.BuildPathAsync` — `PathSlug` → `SiteSetting["catalog:detailPrefix"]` → `"san-pham"` |
| `PageSpec.ParentSlug` / `IsDefaultTemplate`, `CategorySpec.TemplatePageSlug` | xong |
| `site_preview_page` nhận `sampleSlug` | xong |

### Lỗi tìm được và đã vá: SEO phát URL tương đối

`canonical`, `og:url`, `og:image` đều là path tương đối (`/tin-tuc/cold-brew`). Facebook, Zalo và
Google **bỏ qua** giá trị tương đối ở các thẻ này — nghĩa là link chia sẻ mất ảnh/tiêu đề và
canonical không hợp nhất được URL trùng nội dung. Toàn bộ Phase 4 gần như không có tác dụng thật.

Không phải quyết định thiết kế: `SitemapGenerator.cs:42` đã dựng absolute từ `PrimaryDomain` từ
trước, `PageRenderer` chỉ là quên.

Đã thêm `GetBaseUrlAsync` + `ToAbsoluteUrl`. Thứ tự nguồn base URL **`Site.PrimaryDomain` trước,
host request sau** — ngược lại là sai: một site nghe nhiều domain (chu-kafe có cả `tourimate.site`
lẫn `localhost`; hailuunguoc có hai domain), lấy theo host thì mỗi domain tự khai mình là canonical
và mất đúng tác dụng của thẻ. Giá trị đã tuyệt đối (`https://cdn…`, `//cdn…`) giữ nguyên để không
sinh ra `https://site.vn/https://cdn…`.

7 test trong `SeoMetaRenderTests`. Kiểm chứng runtime: `abc-test` (có `PrimaryDomain`) ra
`https://abc-test.example.test/gioi-thieu`; chu-kafe (`PrimaryDomain` rỗng nhưng có `SiteDomains`)
ra `https://tourimate.site/...`.

> Bản vá đầu tiên đặt logic base URL ngay trong `PageRenderer`. Khi trả món nợ số 3 bên dưới,
> phần này được tách ra `ISiteUrlResolver` để sitemap/robots dùng chung — xem mục kế tiếp.

### Ba món nợ đã trả (2026-09-04, sau review tổng)

#### 1. Một nguồn sự thật cho base URL — `ISiteUrlResolver`

Món nợ 3 ("`PrimaryDomain` của chu-kafe NULL") ban đầu được xếp là **dữ liệu**, sửa bằng một
lệnh `UPDATE`. Nhìn lại thì đó là chẩn đoán sai: cùng một câu hỏi *"URL công khai của site này
là gì?"* đang được trả lời **ba lần ở ba nơi**, mỗi nơi một kiểu —
`SitemapGenerator` (`Site.PrimaryDomain`, mặc định `localhost`), `PageRenderer` (`PrimaryDomain`
→ host request), và `Pages/Robots.cshtml.cs` (chỉ `Request.Host`). `UPDATE` một hàng chỉ che ba
đường đi khác nhau, không hợp nhất chúng.

`ISiteUrlResolver.GetBaseUrlAsync()` nay là nơi duy nhất, thứ tự nguồn:

```
1. Site.PrimaryDomain              (khai báo tường minh)
2. SiteDomains WHERE IsPrimary     ← chỗ chu-kafe thật sự có dữ liệu
3. SiteDomains đầu tiên
4. host của request                (site dev chưa khai domain)
```

Bước 2 chính là thứ khiến món nợ này **không còn là việc phải làm bằng tay**: chu-kafe có
`tourimate.site` với `IsPrimary = 1` trong `SiteDomains`, chỉ là `Sites.PrimaryDomain` bỏ trống.
Cache `IMemoryCache` + `SiteCacheSignal` theo đúng pattern `RouteRegistry`.

**Bug thứ hai lộ ra khi kiểm chứng runtime.** Sau khi sửa `SitemapGenerator`, sitemap ra
`https://tourimate.site/` nhưng `robots.txt` vẫn ra `http://localhost:5000/sitemap.xml` — dù cả
hai *đáng lẽ* dùng cùng resolver. Nguyên nhân: `Pages/Robots.cshtml` khai `@page "/robots.txt"`,
là route literal nên **thắng catch-all của theme**; `GenerateRobotsTxtAsync` không bao giờ được
gọi. Hệ quả thật: crawler vào `tourimate.site/robots.txt` được chỉ sang sitemap ở host khác với
domain mà chính sitemap tự khai trong `<loc>`.

Chỉ đọc code không thấy được chỗ này — hai file đều "đúng" khi đọc riêng lẻ.

`RobotsModel` nay nhận `ISiteUrlResolver`; `SitemapGenerator.GenerateRobotsTxtAsync` giữ lại và
ghi chú rõ là đường dự phòng khi Razor Page không được nạp.

#### 2. Gỡ hết fallback `contentTypes = null`

Bỏ `= null` ở `PageRenderer`, `RouteRegistry`, `TemplateResolver`, `BreadcrumbBlock` → còn **1
bản sao duy nhất** trong DI (`BuilderRenderFixture` là bản của test, chấp nhận được). Compiler
chỉ ra chính xác 4 test file đang dựa vào fallback — nay tất cả dùng chung một registry, nên
cache `ContentDetail` per-request được chia sẻ thật.

Đến đây lời hứa của Phase 2.5 mới đạt: **thêm loại mới = 1 class + 1 dòng DI + 2 enum member**.

#### 3. `SupportedTypes` — từ khai báo thành hành vi

`BuilderBlocksApiController.Serialize` nay trả `supportedTypes`; `Edit.cshtml` truyền
`pageKind`; `nc-builder-extensions.js` lọc palette qua `blockFitsPageKind()`. Lọc cả **site
preset** (mục 6.4 của v2) — preset của `product-price` cũng vô dụng trên template bài viết vì nó
dùng lại component type của khối gốc.

Bảng `KIND_TO_TYPE` map `PostTemplate → "post"`, `ProductTemplate → "product"`; trang thường
(`Landing`/`Static`) và shell **không lọc gì** — khối `entity-*` ở đó vẫn render placeholder xám
như trước.

Test `BlockDescriptors_SupportedTypes_KhopKeyContentType` so khớp mọi giá trị `SupportedTypes`
với `IContentTypeRegistry.FindByKey`. Đây là điểm phòng lỗi thật, không phải test hình thức: gõ
`"products"` thay `"product"` thì khối lặng lẽ biến mất khỏi **mọi** palette. Đã kiểm chứng test
bắt được — tạm gõ sai key thì 2 ca đỏ, trả lại thì xanh.

### Còn nợ

1. **Phase 6 chưa di trú dữ liệu** — chu-kafe vẫn chưa có trang `/san-pham` (404) và chưa có
   template chi tiết nào, nên trang chi tiết đang chạy đường fallback legacy. Đúng như thiết kế
   (fallback hoạt động), nhưng phần "so ảnh trước/sau" của Phase 6 chưa làm được.
2. **`Sites.PrimaryDomain` vẫn NULL ở chu-kafe** — không còn là lỗi nhờ bước 2 của
   `SiteUrlResolver`, nhưng khai tường minh vẫn rõ ràng hơn cho người đọc DB.

### Ngoài phạm vi: 51 test KeoBia đỏ

Không liên quan Phase 1–6 (`KeoBiaService.cs` không đổi từ 2026-07-16). Nguyên nhân môi trường:
máy có SDK 10.0.400 trong khi `Directory.Build.props` pin `net8.0`, EF Core 8 gặp lỗi
`ReadOnlySpan<Guid>` trong `FuncCallInstruction` khi evaluate `ids.Contains(...)` trên runtime 10.

---

## 4. Rủi ro

| Rủi ro | Mức | Giảm thiểu |
|---|---|---|
| Cache khối trả nhầm entity (§2.4) | **Cao** | `EntityScoped` trong cache key + test render 2 bài liên tiếp khẳng định tiêu đề khác nhau |
| Vòng lặp `ParentPageId` (A→B→A) | Trung bình | Chặn khi lưu; `TemplateResolver` giới hạn độ sâu 5 như `LoadMediaFoldersAsync` đã làm với `HashSet<Guid> seen` |
| Template rỗng/chưa publish → trang trắng | Trung bình | Chỉ chọn template `Status = Published` **và** `CompiledHtml` không rỗng; ngược lại rơi về fallback C# |
| Đổi slug trang cha làm chết link chi tiết | Thấp | `SyncPostRouteAsync` đã sinh Redirect 301 sẵn; thêm test |
| Trang cha dùng slug rỗng (trang chủ) | Thấp | ~~không cho làm cha~~ → **đã cho phép**: chuyên mục cấp 1 (`/tin-tuc-moi`) có cha tự nhiên là trang chủ, chặn đi thì không gắn template được. `ParentPath("/")` trả null nên trang chủ vẫn không có cha |
| Khối `entity-*` bị kéo vào trang thường | Thấp | Không có `Route` → render placeholder xám kèm chú thích, không throw (đúng hợp đồng `IDynamicBlock`: *"không được throw"*) |
| Prefix sản phẩm đổi làm mất SEO | Thấp | Default giữ `san-pham`; đổi là hành động chủ động, có 301 |

---

## 5. Test

**Unit / integration** (`tests/NewsCMS.Tests/`, theo mẫu `ShellRenderingTests.cs`):

- `TemplateResolverTests` — 4 nhánh a/b/c/d của §2.3, gồm cả trường hợp template chưa publish.
- `EntityBlockCacheTests` — render `entity-title` cho 2 post khác nhau qua cùng một
  `DynamicBlockRenderer`, khẳng định 2 tiêu đề khác nhau. **Test quan trọng nhất của plan.**
- `DetailPageRenderTests` — site không có template → HTML khớp `BuildPostHtml` (chống hồi quy).
- `CategoryRouteTests` — `/tin-tuc-moi` của `universal-spike` trả 200 khi có
  `CategoryTemplate`, 404 khi không.
- `SeoMetaRenderTests` — có/không `SeoMeta`, kiểm fallback.

**E2E** (`tests/e2e/`, Playwright):

- Mở builder trên một template page, đổi entity mẫu → canvas cập nhật, không lỗi console.
- Kéo `entity-title` vào trang thường → hiện placeholder, không vỡ canvas.

---

## 6. Phụ lục — vì sao không chọn hai phương án khác

**Route pattern động (`/tin-tuc/{slug}` đăng ký như một route ASP.NET).** Bỏ được bảng
`SiteRoutes` cho entity, nhưng mất luôn canonical path, hreflang, Redirect 301 khi đổi slug,
và sitemap (`SitemapGenerator.cs:27-30` đọc thẳng `SiteRoutes`). Đang chạy tốt, không đáng phá.

**Cột `RoutePrefix` trên chính template page.** Nhanh hơn `ParentPageId` một chút nhưng tạo
nguồn sự thật thứ hai cho URL: đổi slug trang listing mà quên sửa prefix là toàn bộ trang chi
tiết 404 trong im lặng. `ParentPageId` không có trạng thái sai đó.
