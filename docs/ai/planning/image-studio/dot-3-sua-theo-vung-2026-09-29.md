---
phase: planning
status: planned
title: "ImageStudio — Đợt 3: ảnh mẫu và sửa ảnh theo vùng đánh số"
description: Kế hoạch chi tiết Đợt 3 — tải ảnh mẫu, tạo ảnh từ ảnh tham chiếu, khoanh vùng #1 #2… để sửa đúng chỗ, phần ngoài vùng giữ nguyên từng điểm ảnh
---

# Đợt 3 — Ảnh mẫu và sửa theo vùng đánh số

> Trước đó: [dot-2-kho-mau-2026-09-29.md](dot-2-kho-mau-2026-09-29.md) ·
> Kế hoạch tổng: [../2026-09-29-feature-ai-image-studio.md](../2026-09-29-feature-ai-image-studio.md) ·
> Thiết kế: [§5 Sửa ảnh theo vùng](../../design/2026-09-29-feature-ai-image-studio.md#5-sửa-ảnh-theo-vùng-đánh-số), [§8.2 Trình đánh dấu vùng](../../design/2026-09-29-feature-ai-image-studio.md#82-trình-đánh-dấu-vùng--image-region-editorjs)

## 0. Mục tiêu

Người dùng làm được ba việc mới:

1. **Tạo ảnh từ ảnh mẫu** — tải ảnh lên (hoặc chọn trong thư viện), mô tả cần làm gì, model dùng ảnh đó
   làm tham chiếu.
2. **Sửa ảnh theo vùng đánh số** — khoanh vùng #1, #2… bằng khung chữ nhật hoặc cọ, mỗi vùng một chỉ dẫn,
   có vùng "Giữ nguyên". Phần ngoài vùng **giữ nguyên từng điểm ảnh** (ghép lại ảnh gốc sau khi model trả).
3. **Sửa tiếp nhiều lần** — v1 → v2 → v3, so sánh trước/sau, đưa bản nào vào thư viện cũng được, ảnh gốc
   không bị ghi đè.

### Tiêu chí ra khỏi đợt (M3)

Kịch bản chạy thật trên trình duyệt, không lỗi JavaScript:

1. Mở một ảnh trong **Thư viện media → Sửa bằng AI**.
2. Khoanh **#1** (chữ nhật, "đổi áo thành màu đỏ đô") và **#2** (cọ, "xoá logo"), thêm **#3 Giữ nguyên**
   quanh khuôn mặt. Xoá #1 → #2 thành #1, #3 thành #2, chỉ dẫn đi theo vùng.
3. "Xem trước bản đánh dấu" hiện đúng màu và số như bản server gửi cho model.
4. Bấm Tạo → chi phí ước tính hiện trước; nhận ảnh mới; **thanh trượt trước/sau**; "Sửa tiếp" ra v2.
5. "Dùng ảnh này" → thư viện có thêm một `Media` **cùng thư mục** với ảnh gốc, `Origin = ai-edited`,
   metadata `compositeWithTrainedAlgorithmicMedia`; ảnh gốc không đổi.
6. Tick "Sửa lần lượt từng vùng" → chi phí và lượt trừ nhân theo số vùng sửa.
7. Test tự động chứng minh **100% điểm ảnh ngoài vùng (trừ dải viền mềm) giống hệt ảnh gốc**, kể cả khi
   provider vẽ lại toàn bộ ảnh.
8. Chạy được **cả hai đường**: model có mask (`MaskEdit`) và model chỉ sửa bằng lời (`InstructionEdit`,
   gửi kèm ảnh đánh dấu).

## 1. Giả định cho các câu hỏi còn mở

Đợt này bắt đầu được ngay với giả định dưới đây; câu trả lời khác chỉ đổi phạm vi của một bước.

| Câu hỏi | Giả định | Nếu trả lời khác |
|---|---|---|
| **Q1** — có key provider nào ngoài 9Router? | Chỉ chắc có 9Router (chuẩn OpenAI). Adapter `OpenAiImages` làm được **cả hai đường**: `/images/edits` có `mask` và `/images/edits` nhiều ảnh không mask (ảnh gốc + ảnh đánh dấu). Viết thêm adapter **Gemini** (API `generateContent`), kiểm bằng server giả, chạy thật khi có key | Có key fal → thêm `FalImageProvider` (+0,5 ngày), tái dùng cách poll `status_url` của `FalQueueVideoProvider` bên AdVideo |
| **Q5** — tick "Tôi có quyền sử dụng ảnh này"? | **Bắt buộc** khi đầu vào có ảnh *không do Xưởng ảnh tạo* (ảnh tải lên, `Media.Origin = upload`). Hệ thống không nhận ra được ảnh có người thật hay không nên không tách trường hợp. Lưu người tick, thời điểm, IP | Không bắt buộc → chỉ ẩn ô tick, cột vẫn ghi |
| **Q9** — lỗi KeoBia có sẵn | Không sửa trong đợt này | — |
| **Q10** — gói dịch vụ | Không đụng. Hạn mức đợt này chỉ đổi cách **đếm** (theo số lần gọi) | — |

## 2. Phạm vi

**Làm:** tải ảnh lên, chế độ `Reference`, chế độ `RegionEdit` (`Single`, `Sequential`), xử lý ảnh
(`SizeFitter`, mask, ảnh đánh dấu, ghép lại), `EditAsync` cho OpenAI + Gemini + Fake, "Chạy thử sửa ảnh"
ở trang Model, trình đánh dấu vùng, chuỗi phiên bản + so sánh, trang sửa toàn trang, nút "Sửa bằng AI"
và bộ lọc "Ảnh AI" ở thư viện media, mẫu `RequiresSourceImage` / `KeepSubject` hiện trong modal.

**Không làm (để đợt sau):** sửa ảnh trong TinyMCE (Đợt 4); chọn nhanh ảnh của sản phẩm đang sửa (Đợt 5);
tách nền tự động; worker dọn ảnh tải lên/mask/ảnh đánh dấu (Đợt 6 — đến lúc đó file nằm trong
`ai-images/…`, không vào thư viện); adapter fal (chờ Q1).

## 3. Các bước

Thứ tự: **3.0 → 3A → (3B song song 3C) → 3D → 3E → 3F → 3G**. Mỗi bước là một hoặc vài commit, chỉ đẩy
lên `dev` khi test xanh.

| Bước | Nội dung | Ngày |
|---|---|---|
| 3.0 | Trả nợ Đợt 2: "Tạo demo hàng loạt" chạy ở nền | 0,5 |
| 3A | Hợp đồng, dữ liệu, migration, kiểm vùng | 0,5 |
| 3B | Xử lý ảnh: `SizeFitter`, mask, ảnh đánh dấu, ghép lại, câu chỉ dẫn | 1,5 |
| 3C | Adapter sửa ảnh (OpenAI, Gemini, Fake) + "Chạy thử sửa ảnh" | 1 |
| 3D | Tải ảnh lên, service, runner `Reference` / `RegionEdit` | 1 |
| 3E | Modal: tab "Từ ảnh mẫu", tab "Sửa theo vùng" + `image-region-editor.js` | 1,5 |
| 3F | Phiên bản, so sánh trước/sau, trang sửa toàn trang, thư viện media | 1 |
| 3G | Chạy thật theo kịch bản M3, sửa lỗi, tài liệu | 0,5–1 |
| | **Tổng** | **7,5–8** |

> Kế hoạch tổng ghi 6 ngày. Phần tăng: việc 3.0, bảng ảnh tải lên, nút "Chạy thử sửa ảnh", adapter
> Gemini. **Muốn gọn về 6 ngày** thì bỏ trang sửa toàn trang (dùng modal cho cả thư viện media, −0,5) và
> dời Gemini sang Đợt 6 (−1): khi đó hai đường mask / ảnh đánh dấu vẫn chạy bằng adapter OpenAI.

---

### 3.0 — "Tạo demo hàng loạt" chạy ở nền (0,5 ngày)

**Vì sao làm trước:** nút "Tạo demo cho N mẫu" đang giữ request HTTP cho tới khi tạo xong 12 ảnh. Với
model thật (15–40 giây/ảnh) dễ vượt `proxy_read_timeout 300s` của nginx → người dùng thấy 504 dù ảnh vẫn
đang tạo.

| Việc | Chỗ |
|---|---|
| `ImagePromptDemoBatchQueue` (singleton, `Channel` 1 phần tử) + `ImagePromptDemoBatchWorker` (`BackgroundService`, scope mới mỗi lô) gọi `ImagePromptDemoService.GenerateManyAsync` | `Infrastructure/ImageStudio/` |
| Trạng thái lô trong bộ nhớ: `Running`, `Total`, `Done`, `Failed`, `StartedAt`, `LastError`. Mỗi lúc chỉ một lô; bấm khi đang chạy → "Đang tạo ở nền 5/17". Restart thì mất trạng thái, ảnh đã tạo vẫn còn — bấm lại là tạo tiếp phần thiếu | `ImagePromptDemoBatchState` |
| Bỏ trần 12 ảnh/lần bấm; vẫn dừng sau 3 lỗi liên tiếp | `Templates.cshtml.cs` |
| Trang Kho mẫu: thanh tiến độ, tự hỏi `?handler=DemoBatchStatus` mỗi 3 giây, xong thì tải lại bảng | `Templates.cshtml`, JS nhỏ trong trang |
| Nút "Tạo demo" từng mẫu giữ nguyên chạy đồng bộ (một lần gọi, dưới `TimeoutSeconds` của model) | — |

**Test:** lô chạy ở nền và cập nhật đếm; bấm lần hai khi đang chạy không tạo lô mới; dừng sau 3 lỗi.

---

### 3A — Hợp đồng, dữ liệu, kiểm vùng (0,5 ngày)

#### Hợp đồng provider

```csharp
// Application/ImageStudio/Providers/IImageProvider.cs
Task<ImageProviderResult> EditAsync(ImageEditRequest request, CancellationToken ct = default);

public sealed record ImageEditRequest(
    ImageProviderContext Context,
    string Prompt,
    string Size,                          // sau SizeFitter; "" = để model tự chọn (Gemini)
    ImageData Source,                     // ảnh gốc đã chuẩn hoá (đã pad nếu cần)
    ImageData? Mask,                      // theo MaskConvention của model; null khi sửa bằng lời
    ImageData? Annotated,                 // ảnh đánh dấu số vùng; null khi có mask
    IReadOnlyList<ImageData> References); // chế độ Reference: ảnh tham chiếu thêm
```

`ImageProviderErrorCodes` thêm `Unsupported` — gateway không có endpoint sửa ảnh (404/405) → thông báo
*"Model này chưa sửa ảnh được qua kết nối hiện tại — bỏ năng lực 'Sửa theo mask/Sửa bằng lời' ở trang
Model"*, không thử lại.

#### Đầu vào từ trình duyệt

```csharp
// Application/ImageStudio/ImageRegionDtos.cs
public sealed record ImageSourceRef(string Kind, Guid Id);        // media | output | upload

public sealed record ImageRegionInput(
    int Index, string Mode, string Shape,                          // edit|keep, rect|brush
    double X, double Y, double W, double H,                        // rect, chuẩn hoá 0..1
    IReadOnlyList<ImageBrushStroke>? Strokes,
    string? Instruction);

public sealed record ImageBrushStroke(double R, bool Erase, IReadOnlyList<double[]> Points);
```

`ImageJobCreateInput` thêm: `Mode`, `Source` (`ImageSourceRef?`), `References`
(`IReadOnlyList<ImageSourceRef>?`), `Regions`, `GlobalInstruction`, `Strategy`, `PreserveOutside`,
`RightsConfirmed`, `ParentJobId`. Tạo ảnh từ chữ vẫn gửi như cũ (mặc định `Mode = Generate`).

#### Kiểm vùng — `ImageRegionValidator` (thuần hàm, Application)

- 1–8 vùng; `Index` liên tục từ 1; toạ độ trong [0, 1]; chữ nhật `W, H > 0`.
- Vùng `edit` phải có chỉ dẫn (≤ 300 ký tự); vùng `keep` bỏ qua chỉ dẫn. Chỉ dẫn chung ≤ 500 ký tự.
- Diện tích mỗi vùng ≥ 0,1% ảnh (chữ nhật tính trực tiếp; cọ ước lượng theo độ dài nét × bán kính —
  sau khi dựng mask, runner kiểm lại bằng số điểm ảnh thật: *"Vùng #2 trống sau khi tẩy"*).
- Cọ: bán kính 0,002–0,2; tổng ≤ 4.000 điểm, ≤ 200 nét (JS đã rút gọn nét trước khi gửi).
- Có ít nhất một vùng `edit`, **hoặc** có chỉ dẫn chung kèm ≥ 1 vùng `keep` ("đổi mọi thứ trừ vùng giữ").
- `Sequential` chỉ khi có ≥ 2 vùng `edit`; khi đó số biến thể ép về 1.
- Lỗi trả tiếng Việt, gắn số vùng: *"Vùng #3: chưa có chỉ dẫn."*

`ImageStudioRules` thêm hằng: `MaxUploadBytes = 15 MB`, `MaxUploadMegapixels = 40`, `MaxRegions = 8`,
`MaxRegionInstruction = 300`, `MaxGlobalInstruction = 500`, `MinRegionArea = 0.001`,
`MaxBrushPoints = 4000`, `MaxReferenceImages = 4` (trần cứng, còn lấy min với `ImageModel.MaxReferenceImages`),
`WorkingMaxEdge = 2048`.

Bảng màu vùng `ImageRegionPalette` (8 màu tương phản cao) đặt ở Application: server vẽ ảnh đánh dấu bằng
nó, và trang render nó ra `data-palette` cho JS — **một nguồn duy nhất**, nên "Xem trước" giống hệt ảnh
gửi đi.

#### Dữ liệu — migration `AddImageRegionEdit`

| Thay đổi | Để làm gì |
|---|---|
| Bảng mới **`ImageUploads`** (`SiteId`, `StorageKey`, `Url`, `FileName`, `Width`, `Height`, `Bytes`, `MimeType`, `UploadedBy`, `CreatedAt`, `IsPurged`), index `(SiteId, CreatedAt)` | Ảnh tải lên để sửa **không vào thư viện media** (thư viện chỉ nhận kết quả người dùng chọn). Theo site, không phải của riêng ai — đồng nghiệp "Sửa tiếp" được |
| `ImageJob.SourceUploadId` | Nguồn thứ ba bên cạnh `SourceMediaId`, `SourceOutputId` (đúng một trong ba) |
| `ImageJob.SourceStorageKey` | Bản chụp ảnh gốc **đã chuẩn hoá** lúc chạy: so sánh trước/sau và "Sửa tiếp" vẫn đúng dù `Media` gốc bị xoá hay thay |
| `ImageJob.ReferenceMediaIdsJson` → **`ReferencesJson`** (`[{kind, id}]`) | Tham chiếu có thể là media, output hoặc ảnh tải lên. Cột cũ chưa dùng nên đổi thẳng |
| `ImageJob.GlobalInstruction` (500), `Warning` (500) | Chỉ dẫn chung; cảnh báo khi job xong một phần (`Sequential` dừng giữa chừng) |
| `ImageJob.QuotaUnits` (int) — migration gán `= VariantCount` cho dòng cũ | Hạn mức đếm theo **số lần gọi**: `biến thể × số bước`. Truy vấn hạn mức đổi từ `Sum(VariantCount)` sang `Sum(QuotaUnits)` |
| `ImageJob.RightsConfirmedAt`, `RightsIp` (45) | Lưu vết tick quyền sử dụng (người tick = `CreatedBy`) |
| `ImageModel.LastEditTestedAt`, `LastEditTestOk`, `LastEditTestError` | Kết quả "Chạy thử sửa ảnh", tách khỏi lần chạy thử tạo ảnh |

`RegionsJson` lưu **bản đã kiểm và chuẩn hoá** (sắp theo `Index`, làm tròn 4 chữ số) — "Sửa tiếp" và
"Tạo lại" đọc lại được nguyên vẹn.

**Test:** validator (từng luật ở trên), đổi cột không làm hỏng test Đợt 1–2, hạn mức cộng `QuotaUnits`.

---

### 3B — Xử lý ảnh (1,5 ngày)

Tất cả nằm ở `Infrastructure/ImageStudio/Imaging/`, là hàm thuần trên `Image<Rgba32>` / `Image<L8>`, test
bằng ảnh sinh trong code. **Không thêm gói NuGet**: chỉ dùng ImageSharp 3.1.5 đang có. Tô chữ nhật, tô
nét cọ (đóng dấu hình tròn dọc đoạn thẳng), nới vùng và làm mờ viền (`GaussianBlur` có sẵn) đều tự viết
được; số trên huy hiệu vẽ bằng **phông điểm ảnh 5×7 nhúng trong code** (chỉ cần chữ số 1–8) nên không cần
SixLabors.Fonts và file phông trên máy chủ.

| Lớp | Việc | Test chính |
|---|---|---|
| **`SourceImageLoader`** | Đọc byte từ storage (media / output / upload) → `Image.Identify` trước khi giải mã (chặn > 40 MP) → xoay theo EXIF, bỏ metadata, sRGB, co về cạnh dài ≤ 2048 (dùng lại `AiImageFinalizer.Normalize`) | Ảnh 90° EXIF ra đúng hướng; ảnh 50 MP bị chặn mà không giải mã; metadata GPS bị xoá |
| **`SizeFitter`** | `Plan(w, h, supportedSizes)` → kích thước model gần nhất **cùng hướng** (theo log tỉ lệ, như `ResolveSize`), co giữ tỉ lệ, **pad bằng màu biên** (lặp điểm ảnh mép — không kéo méo). `Apply` cho ảnh gốc, mask (phần pad = *không sửa*), ảnh đánh dấu. `Restore`: nhận ảnh model trả **dù khác kích thước yêu cầu** → co về kích thước đã pad → cắt pad → co về đúng `w × h` gốc. Model không khai báo kích thước (Gemini) → không pad, chỉ `Restore` | Ảnh lẻ (1023×767, 3000×1000, 600×2400); pad chia đều, lệch tối đa 1 px và **mask + ảnh + kết quả lệch cùng nhau**; khứ hồi với provider "trả nguyên" ra đúng kích thước gốc |
| **`RegionMaskBuilder`** | Dựng mask ở kích thước làm việc: chữ nhật tô đặc; cọ = hợp các nét `add` trừ các nét `Erase`; `edit = hợp(vùng edit)` (không có vùng edit mà có chỉ dẫn chung → toàn ảnh) `− hợp(vùng keep)`. Ra ba thứ: **mask cứng** (gửi provider), **mask ghép** (nới 1% cạnh dài, tối thiểu 6 px, rồi làm mờ viền), **mask từng vùng** (cho `Sequential`). `Export(mask, convention)`: `AlphaZeroIsEdit` → PNG RGBA alpha 0 ở vùng sửa; `WhiteIsEdit` → PNG xám trắng ở vùng sửa | Toạ độ điểm ảnh của chữ nhật đúng tới 1 px; vùng keep đục lỗ vùng edit; chỉ dẫn chung + keep = toàn ảnh trừ vùng giữ; hai quy ước xuất đúng; mask ghép có dải chuyển 0→255 |
| **`RegionAnnotationRenderer`** | Vẽ lên bản sao ảnh gốc: vùng edit tô màu bảng 35% + viền 3 px; vùng keep viền đứt nét, không tô; huy hiệu tròn có số ở góc trên-trái vùng (tự dịch vào trong nếu sát mép). Độ dày và cỡ huy hiệu tỉ lệ theo cạnh dài | Màu điểm ảnh trong vùng #1 đúng màu bảng #1; vùng keep không bị tô; huy hiệu nằm trong ảnh |
| **`RegionCompositor`** | `gốc × (1 − m) + mới × m` theo mask ghép. `PreserveOutside = false` → trả ảnh model (đã `Restore`) | **Provider giả vẽ lại toàn bộ ảnh → 100% điểm ảnh ngoài mask ghép giống gốc từng byte**; bên trong mask cứng là ảnh mới |
| **`RegionPromptComposer`** (Application, thuần hàm) | Câu chỉ dẫn như thiết kế §5.2: vị trí bằng lời (lưới 3×3: "phía trên bên trái", "chính giữa", …) + phần trăm; vùng giữ; chỉ dẫn chung; câu "không vẽ số, khung, màu đánh dấu" **chỉ** ở chế độ ảnh đánh dấu; bản cho một vùng khi `Sequential`. Chế độ `Reference`: câu dẫn "Dùng ảnh tham chiếu …" + mô tả người dùng | 9 vị trí; phần trăm làm tròn; chế độ mask không có câu về dấu đánh dấu |

---

### 3C — Adapter sửa ảnh (1 ngày)

| Adapter | Cách gọi | Ghi chú |
|---|---|---|
| **`OpenAiImages.EditAsync`** | `POST {base}/images/edits`, `multipart/form-data`: `model`, `prompt`, `n=1`, `size`, `quality`, `output_format`; ảnh là `image` (một ảnh) hoặc `image[]` (nhiều ảnh: gốc → ảnh đánh dấu → tham chiếu); `mask` chỉ ở đường mask. `ExtraParamsJson` thêm thành trường form (bỏ trường `null` như bản tạo ảnh). Kết quả `b64_json` hoặc `url` qua `ImageDownloadGuard` | Dùng chung `MapError`/`FriendlyError`; 404/405 → `Unsupported` |
| **`Gemini`** (mới) | `POST {base}/models/{model}:generateContent`, header `x-goog-api-key`; `contents[0].parts` = chữ + `inline_data` (ảnh gốc JPEG chất lượng 92 để nhẹ, ảnh đánh dấu PNG, tham chiếu); `generationConfig.responseModalities = ["IMAGE"]`, tạo ảnh từ chữ thêm `imageConfig.aspectRatio`. Đọc `candidates[].content.parts[].inlineData` | Không có mask → luôn đường ảnh đánh dấu. `promptFeedback.blockReason`, `finishReason` = `SAFETY`/`IMAGE_SAFETY`/`PROHIBITED_CONTENT` → `ContentPolicy`; không có phần ảnh → `InvalidResponse` kèm lời model (đã che). Base URL mẫu `https://generativelanguage.googleapis.com/v1beta`. Làm luôn `GenerateAsync` để model Gemini dùng được ở tab Tạo mới |
| **`Fake.EditAsync`** | Vẽ lại **toàn bộ** ảnh (đảo màu + nhiễu nhẹ) và tô đậm vùng mask | Cố ý đổi cả phần ngoài vùng để test chứng minh bước ghép lại |

**"Chạy thử sửa ảnh"** ở trang Model (chỉ hiện khi model khai báo `MaskEdit` hoặc `InstructionEdit`): tự
sinh ảnh 512×512 (nền xám, hình vuông trắng giữa ảnh), mask/ảnh đánh dấu cho hình vuông, prompt "đổi hình
vuông thành màu đỏ" → gọi `EditAsync` → hiện ảnh trả về, ghi `LastEditTest*` và một dòng
`ImageProviderCall` với `Operation = test-edit`. Đây là cách phát hiện sớm gateway (ví dụ 9Router) không hỗ
trợ `/images/edits` hay không nhận `mask`.

Trang **ModelEdit**: gợi ý năng lực theo adapter (Gemini: tắt `MaskEdit`, bật `InstructionEdit` +
`ReferenceImages`); cảnh báo khi `MaskEdit` bật mà `MaskConvention = None`.

**Test:** multipart đúng trường (`image` vs `image[]`, `mask` chỉ khi có, tham số thêm, không lộ key trong
lỗi); Gemini đúng JSON (phần chữ, `inline_data` base64 + mime, header key), đọc ảnh, các kiểu bị chặn, 429,
không có ảnh; 404 → `Unsupported`.

---

### 3D — Tải ảnh lên, service, runner (1 ngày)

#### Tải ảnh lên

`IImageUploadService.UploadAsync(stream, fileName, userId)`: ≤ 15 MB (kiểm cả `Content-Length` và khi đọc),
`Image.Identify` ≤ 40 MP, giải mã được bằng ImageSharp (không tin phần mở rộng), chuẩn hoá (xoay, bỏ
metadata, ≤ 2048) → lưu `ai-images/uploads/{yyyy}/{MM}/{id}.png|jpg` → dòng `ImageUploads`. Ảnh có kênh
trong suốt giữ PNG, còn lại JPEG 92.

#### `ImageStudioService.CreateAsync` — thêm cho `Reference` / `RegionEdit`

1. **Nguồn thuộc site hiện tại**: media (chưa xoá, là ảnh), output (job cùng site, chưa bị dọn), upload
   (cùng site, chưa bị dọn). Sai → *"Không tìm thấy ảnh gốc"* (không nói ảnh có tồn tại ở site khác).
2. **Năng lực model**: `RegionEdit` cần `MaskEdit` hoặc `InstructionEdit`; `Reference` cần
   `ReferenceImages`, số ảnh ≤ `min(MaxReferenceImages, 4)`. Sai → nói rõ model nào làm được.
3. **Quyền sử dụng (Q5)**: có ảnh đầu vào `Origin = upload` (ảnh tải lên hoặc media tải lên) mà chưa tick →
   chặn. Tick → ghi `RightsConfirmed`, `RightsConfirmedAt`, `RightsIp` (IP lấy ở trang Api, truyền vào).
4. **Kiểm vùng** (`ImageRegionValidator`), lưu `RegionsJson` đã chuẩn hoá.
5. **Lượt và chi phí**: `QuotaUnits = biến thể × (Sequential ? số vùng edit : 1)`; hạn mức kiểm theo
   `QuotaUnits` (vẫn dưới khoá theo site); `EstimatedCostUsd = giá × QuotaUnits`.
6. `ParentJobId` (Sửa tiếp) phải là job cùng site.

`GetFormAsync` trả thêm: năng lực từng model, bảng màu, giới hạn (vùng, chỉ dẫn, dung lượng tải lên).

#### `ImageJobRunner` — hai chế độ mới

**`RegionEdit`, `Single`:**

1. `SourceImageLoader` → ảnh làm việc (≤ 2048), lưu bản chụp `ai-images/sources/{jobId}.png` →
   `SourceStorageKey`.
2. `RegionMaskBuilder` → kiểm lại diện tích thật từng vùng.
3. Model có `MaskEdit` → **đường mask** (ưu tiên, vì chính xác hơn); không thì **đường ảnh đánh dấu**.
4. `SizeFitter.Plan` theo `SupportedSizes`; `Apply` cho ảnh gốc + mask/ảnh đánh dấu. Lưu đúng file gửi đi
   vào `MaskStorageKey` / `AnnotatedStorageKey` (để truy vết và xem lại ở trang Chi tiết).
5. `RegionPromptComposer` → `FinalPrompt` (không nối phong cách thương hiệu vào lệnh sửa — tránh đổi tông
   cả ảnh).
6. Mỗi biến thể một lần `EditAsync` song song (thử lại đúng 1 lần với 429/502/503/504 như tạo ảnh).
7. `Restore` → `RegionCompositor` (nếu `PreserveOutside`) → `AiImageFinalizer` với
   `compositeWithTrainedAlgorithmicMedia` → lưu output.

**`RegionEdit`, `Sequential`:** lặp từng vùng edit theo thứ tự số: mask/ảnh đánh dấu **chỉ vùng đó** (vùng
keep vẫn trừ và vẫn vẽ), kết quả bước trước (đã ghép) làm ảnh gốc bước sau. Một biến thể. Lỗi ở bước *k* →
nếu đã xong ≥ 1 bước thì **lưu kết quả tới bước *k − 1***, job `Succeeded` kèm `Warning` *"Dừng ở vùng #k:
…"* (người dùng đã trả tiền cho các bước trước); lỗi ngay bước 1 → `Failed`.

**`Reference`:** nạp các ảnh tham chiếu (≤ 1536 cạnh dài để request nhẹ) → `EditAsync(Source = ảnh đầu,
References = phần còn lại, không mask)` với kích thước theo tỉ lệ khung như tạo ảnh → metadata
`trainedAlgorithmicMedia` (ảnh mới, không phải bản sửa).

Mọi lần gọi ghi `ImageProviderCall` với `Operation = edit` / `reference`.

#### `PromoteAsync`

Job có `SourceMediaId` → `Media` mới **cùng thư mục với ảnh gốc** (D7), tên `{tên-gốc}-ai-v{n}.{ext}`, alt
lấy của ảnh gốc nếu người dùng không sửa, `Origin = ai-edited`. Còn lại → thư mục "Ảnh AI" như cũ
(`ai-generated` cho `Generate`/`Reference`, `ai-edited` cho `RegionEdit`).

**Test:** nguồn khác site / đã dọn bị chặn; thiếu tick bị chặn, media do AI tạo không cần tick; năng lực
sai bị chặn; `QuotaUnits` và chi phí khi `Sequential`; đường mask lưu mask đúng quy ước, đường ảnh đánh dấu
khi model không có `MaskEdit`; `Sequential` gọi đúng N lần theo thứ tự, dừng giữa chừng giữ kết quả +
`Warning`; ghép lại giữ 100% ngoài vùng qua cả runner; metadata composite; promote vào đúng thư mục gốc;
tải lên quá cỡ / quá điểm ảnh / không phải ảnh / xoay EXIF.

---

### 3E — Modal: "Từ ảnh mẫu" và "Sửa theo vùng" (1,5 ngày)

#### Modal ba tab

`Tạo mới` · `Từ ảnh mẫu` · `Sửa theo vùng`. `imageStudio.open` nhận thêm
`source: { kind, id }` (mở thẳng tab Sửa theo vùng) và `mode: 'reference'`. Ở tab sửa, modal rộng ra
(`max-w-6xl`); trên điện thoại chuyển thành toàn màn hình.

- **Thẻ model** lọc theo tab: model thiếu năng lực bị làm mờ kèm lý do ("Model này không sửa ảnh được").
  Tab sửa mặc định chọn model có `MaskEdit`.
- **Tab Từ ảnh mẫu**: vùng kéo thả / "Chọn từ thư viện" (`mediaLibraryModal.open`) / dán ảnh (Ctrl+V);
  dải ảnh đã chọn (≤ giới hạn của model, xoá được, ảnh đầu là ảnh chính); ô mô tả; mẫu có
  `RequiresSourceImage` hiện ở đây; nút "Sửa theo vùng ảnh này" chuyển ảnh sang tab bên cạnh.
- **Ô tick quyền sử dụng** hiện khi có ảnh `upload`; tick một lần giữ nguyên cho tới khi đóng modal.

#### `wwwroot/js/admin/image-region-editor.js`

Lớp dùng chung cho modal và trang sửa toàn trang:

```js
const editor = new ImageRegionEditor(el, { imageUrl, width, height, palette, maxRegions: 8,
                                           onChange(regions) {} });
editor.getRegions(); editor.setRegions(regions); editor.renderAnnotatedPreview(); editor.destroy();
```

| Phần | Chi tiết |
|---|---|
| **Vẽ** | Ảnh hiển thị (≤ 2048 px) + một canvas phủ. Toạ độ lưu **chuẩn hoá theo ảnh gốc**, đổi qua ma trận xem (phóng to, kéo) → đổi cỡ cửa sổ hay xem trên điện thoại không lệch |
| **Công cụ** | Chữ nhật `R` (kéo), Cọ `B` (thanh chỉnh cỡ 0,5–10% bề ngang, con trỏ hình tròn đúng cỡ), Tẩy `E` (nét trừ trong vùng cọ đang chọn), Chọn `V` (di chuyển; chữ nhật có 8 tay nắm đổi cỡ), Hoàn tác/Làm lại `Ctrl+Z` / `Ctrl+Shift+Z` (50 bước), Phóng to (lăn chuột quanh con trỏ, `+`/`−`, "Vừa khung"), Kéo ảnh (giữ `Space` hoặc chuột giữa), `Delete` xoá vùng đang chọn, `1`–`8` chọn vùng, `Esc` bỏ chọn |
| **Cảm ứng** | Pointer Events: một ngón vẽ, hai ngón phóng to/kéo; `touch-action: none` trên canvas |
| **Đánh số** | Số = vị trí trong danh sách + 1, **màu theo số** (bảng từ server). Xoá một vùng → các vùng sau dồn số và đổi màu theo số mới; chỉ dẫn đi theo vùng. Đủ 8 vùng → công cụ vẽ tắt kèm lời nhắc |
| **Nét cọ** | Rút gọn điểm (Ramer–Douglas–Peucker, sai số 0,002) khi nhả chuột; tổng ≤ 4.000 điểm |
| **Bảng vùng** | Mỗi dòng: ô màu, `#N`, nút **Sửa / Giữ nguyên**, ô chỉ dẫn (đếm ký tự, tối đa 300), nút xoá. Rê chuột lên dòng → vùng sáng lên, và ngược lại. Cuối bảng: "Chỉ dẫn chung cho cả ảnh" |
| **Tuỳ chọn** | "Giữ nguyên tuyệt đối phần ngoài vùng" (mặc định bật); "Sửa lần lượt từng vùng (chính xác hơn, tốn N lượt)" — bật thì số biến thể về 1 và chi phí hiện `× N` |
| **Xem trước bản đánh dấu** | Vẽ ở client bằng cùng bảng màu, cùng độ đục 35%, viền, huy hiệu số — giống bản server gửi đi |
| **Kiểm trước khi gửi** | Cùng luật với `ImageRegionValidator`; lỗi hiện ngay trên dòng vùng; server vẫn kiểm lại |
| **Mẫu `KeepSubject`** | Hiện hướng dẫn "Khoanh sản phẩm — vùng này được giữ nguyên", công cụ chuyển sẵn sang Chữ nhật ở chế độ Giữ nguyên, chỉ dẫn chung điền từ mẫu |
| **Truy cập** | Nút công cụ có `aria-pressed` + phím tắt trong `title`; bảng vùng dùng được bằng bàn phím |

Kiểu dáng thêm vào `Styles/admin.css` (Tailwind v4), build lại `npm run css:admin`.

---

### 3F — Phiên bản, so sánh, trang sửa toàn trang, thư viện media (1 ngày)

| Việc | Chỗ |
|---|---|
| Thẻ kết quả thêm **"Sửa tiếp"** (mở tab sửa với nguồn = output này, **mang theo vùng cũ** nếu job trước là `RegionEdit`, gắn `ParentJobId`) và **"Tạo lại"** (gửi lại y nguyên đầu vào với khoá chống trùng mới) | `image-studio.js` |
| **Thanh trượt trước/sau** dùng chung (hai ảnh chồng, `input[type=range]` điều khiển `clip-path`, kéo được bằng chuột/cảm ứng/phím mũi tên) | `image-compare.js` nhỏ, modal, `Detail.cshtml` |
| Trang **Chi tiết**: dãy phiên bản v1 → v2 → v3 (đi ngược `ParentJobId` tối đa 20 bước + các job con), so sánh ảnh gốc (`SourceStorageKey`) với kết quả, xem mask và ảnh đánh dấu đã gửi, danh sách vùng + chỉ dẫn, `Warning` nếu có | `Detail.cshtml(.cs)` |
| **Trang sửa toàn trang** `/Admin/ImageStudio/Edit?mediaId=` (hoặc `?outputId=`), quyền `ImageStudio.Image.Create`: trình đánh dấu chiếm màn hình, bảng vùng bên phải, dải kết quả + so sánh bên dưới, "Dùng ảnh này" → `Media` mới cùng thư mục | `ImageStudio/Edit.cshtml(.cs)` |
| `Media/Edit`: nút **"Sửa bằng AI"** (chỉ ảnh, có quyền `Create`, site đã bật) | `Media/Edit.cshtml` |
| `Media/Index`: lọc **Nguồn: Tất cả / Tải lên / Ảnh AI**, huy hiệu "AI" góc thẻ ảnh. `IMediaService.SearchAsync` thêm tham số tuỳ chọn `origin` ở cuối (chỗ gọi cũ không phải sửa) | `Media/Index.cshtml(.cs)`, `MediaService.SearchAsync` |
| Trang **Xưởng ảnh AI**: lọc thêm theo chế độ (Tạo mới / Từ ảnh mẫu / Sửa vùng), thẻ job sửa hiện ảnh gốc nhỏ bên cạnh | `ImageStudio/Index.cshtml(.cs)` |

**Test:** chuỗi phiên bản đúng thứ tự và chặn vòng lặp; lọc media theo nguồn; trang Edit từ chối media
khác site, media không phải ảnh, người thiếu quyền.

---

### 3G — Chạy thật, sửa lỗi, tài liệu (0,5–1 ngày)

**Môi trường** như Đợt 1–2: SQL Server 2022 + NewsCMS Web + Chromium (Playwright). Server giả mở rộng:

- `POST /v1/images/edits` (multipart): ghi lại trường nhận được (có `mask` không, bao nhiêu `image`), trả
  ảnh **đổi màu toàn bộ** để thấy rõ bước ghép lại giữ nguyên phần ngoài vùng; prompt có "tu-choi" → 400
  kiểm duyệt.
- `POST /v1beta/models/{model}:generateContent`: trả `inlineData`.

**Kịch bản:** đúng 8 bước ở §0, thêm: Chạy thử sửa ảnh cho model mask và model Gemini; tạo ảnh từ 2 ảnh
tham chiếu tải lên (có tick, thiếu tick bị chặn); sửa ảnh mẫu `KeepSubject` "đổi nền, giữ sản phẩm";
`Sequential` 3 vùng với lỗi ở vùng #3 → nhận kết quả tới #2 kèm cảnh báo; tạo 1 lô demo ở nền trong khi
dùng trang khác.

**Đo thêm:** bộ nhớ khi 2 job sửa ảnh 2048 px chạy song song (mục tiêu < 400 MB tăng thêm); thời gian xử lý
ảnh phía server (không tính provider) < 1,5 giây/biến thể.

**Tài liệu:** kết quả vào file này (như Đợt 1–2); đánh dấu M3 ở kế hoạch tổng; mục "Để dùng thật": khai
báo năng lực model, bấm "Chạy thử sửa ảnh", model gợi ý cho từng đường.

## 4. Tổng hợp file

| Tầng | File mới | File sửa |
|---|---|---|
| Domain | `ImageUpload.cs` | `ImageJob.cs`, `ImageModel.cs` |
| Application | `ImageRegionDtos.cs`, `ImageRegionValidator.cs`, `ImageRegionPalette.cs`, `RegionPromptComposer.cs`, `IImageUploadService.cs` | `IImageProvider.cs`, `ImageStudioDtos.cs`, `IImageStudioServices.cs`, `ImageStudioRules.cs`, `MediaDtos.cs` |
| Infrastructure | `Imaging/{SourceImageLoader,SizeFitter,RegionMaskBuilder,RegionAnnotationRenderer,RegionCompositor,PixelFont}.cs`, `Providers/GeminiImageProvider.cs`, `ImageUploadService.cs`, `ImagePromptDemoBatch{Queue,State,Worker}.cs`, migration `AddImageRegionEdit` | `OpenAiImageProvider.cs`, `FakeImageProvider.cs`, `ImageProviderRegistry.cs`, `ImageJobRunner.cs`, `ImageStudioService.cs`, `ImageModelService.cs`, `ImageStudioConfiguration.cs`, `MediaService.cs`, `DependencyInjection.cs` |
| Web | `ImageStudio/Edit.cshtml(.cs)`, `js/admin/image-region-editor.js`, `js/admin/image-compare.js` | `ImageStudio/Api.cshtml.cs` (`Upload`, `Source`, `TestEdit`), `_ImageStudioModal.cshtml`, `image-studio.js`, `Detail.cshtml`, `Index.cshtml`, `Config/ModelEdit`, `Config/Models`, `Config/Templates`, `Media/Edit`, `Media/Index`, `Styles/admin.css` |
| Test | `ImageRegionValidatorTests`, `SizeFitterTests`, `RegionMaskBuilderTests`, `RegionAnnotationRendererTests`, `RegionCompositorTests`, `RegionPromptComposerTests`, `GeminiImageProviderTests`, `ImageUploadTests`, `ImageStudioEditServiceTests`, `ImageJobRunnerEditTests`, `DemoBatchTests` | `OpenAiImageProviderTests`, `ImageStudioTestHarness` |

## 5. Rủi ro riêng của đợt

| Rủi ro | Giảm thiểu |
|---|---|
| **9Router không hỗ trợ `/images/edits` hoặc không nhận `mask`** | "Chạy thử sửa ảnh" phát hiện ngay ở trang Model; lỗi `Unsupported` nói rõ phải đổi gì. Đường ảnh đánh dấu (nhiều `image[]` không mask) hoặc Gemini thay thế |
| **Model coi mask chỉ là gợi ý, vẽ lại cả ảnh** (gpt-image làm vậy) | Chính là lý do có `RegionCompositor`: phần ngoài vùng luôn là điểm ảnh gốc. Bên trong vùng lệch màu ở mép → chỉnh độ nới + làm mờ viền bằng ảnh thật ở 3G, **không kéo dài đợt để tinh chỉnh prompt** |
| **Lệch 1 px giữa mask, ảnh gốc và kết quả** khi pad/cắt | `SizeFitter` viết test làm tròn trước (ngày đầu của 3B); mọi ảnh đi qua cùng một `FitPlan` |
| **Request Gemini quá nặng** (base64 ba ảnh 2048 px) | Ảnh gốc gửi JPEG 92; tham chiếu ≤ 1536 cạnh dài; ảnh đánh dấu cùng cỡ ảnh gốc |
| **Bộ nhớ máy chủ** (mỗi ảnh 2048² RGBA ≈ 16 MB, nhiều bản trung gian) | `using` cho mọi `Image`; mask dùng `L8` (1 byte/điểm); `MaxConcurrency` giữ 2; đo ở 3G |
| **Canvas chậm trên điện thoại với ảnh lớn** | Hiển thị bản ≤ 2048; vẽ lại theo `requestAnimationFrame`; toạ độ chuẩn hoá nên độ phân giải hiển thị không ảnh hưởng kết quả |
| **Ảnh người thật / deepfake** | Tick quyền có lưu vết (Q5); lỗi kiểm duyệt của provider không thử lại; mọi job có `CreatedBy` |
| **File tải lên, mask, ảnh đánh dấu tích lại** cho tới Đợt 6 | Nằm dưới `ai-images/` (dễ dọn hàng loạt); worker dọn ở Đợt 6 xoá theo `CreatedAt` |
