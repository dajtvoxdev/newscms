---
phase: planning
status: planned
title: "Chuẩn API tạo ảnh và video — cổng vilao.ai, OpenAI / Google / Stability, và các chuẩn video"
description: Tra cứu 29/09/2026 các chuẩn API ảnh và video, cái nào còn sống, cái nào đã bị khai tử, và lộ trình đưa từng chuẩn vào ImageStudio (NewsCMS) và AdVideo
---

# Chuẩn API tạo ảnh và video

> Kế hoạch tổng ImageStudio: [../2026-09-29-feature-ai-image-studio.md](../2026-09-29-feature-ai-image-studio.md) ·
> Đợt 3: [dot-3-sua-theo-vung-2026-09-29.md](dot-3-sua-theo-vung-2026-09-29.md) ·
> Provider khai báo của AdVideo: [../ad-video-studio/ke-hoach-provider-khai-bao-2026-09-25.md](../ad-video-studio/ke-hoach-provider-khai-bao-2026-09-25.md)
>
> Tra ngày **29/09/2026**. Các API này đổi nhanh: trước khi viết adapter nào, đọc lại tài liệu gốc ở
> mục 7 và chạy một lần gọi thật.

## 0. Tóm tắt

- **Cổng vilao.ai** (cổng bạn định dùng) theo chuẩn OpenAI → adapter `OpenAiImages` đang có tạo được ảnh
  ngay; sửa ảnh làm ở Đợt 3. Cần thêm `SizeMode` vì vilao nhận `size: "auto"`.
- **Ba chuẩn ảnh lớn đều tích hợp được**: OpenAI Images (đã có), Google Gemini (adapter nhỏ), Stability AI
  (adapter mới, mạnh nhất về sửa ảnh chính xác).
- **Đã bị khai tử, không làm**: DALL·E 2/3 và `/images/variations` (OpenAI tắt 12/05/2026), Imagen 4
  (Google tắt 17/08/2026), Sora 2 + Videos API của OpenAI (tắt 24/09/2026). `gpt-image-1` sẽ tắt
  23/10/2026.
- **Video**: mọi API video đều bất đồng bộ (gửi → mã → hỏi trạng thái → tải). AdVideo **đã có** bộ máy
  khai báo provider bằng JSON, nên thêm một nguồn video mới phần lớn là viết một file JSON.
- **Mâu thuẫn cần chốt (Q12)**: ngày 25/09 bạn yêu cầu *"xoá API Gemini trực tiếp"* ở AdVideo (Veo đi qua
  cổng NOVA). Hôm nay bạn hỏi tích hợp Google cho ảnh. Kế hoạch dưới đây đề xuất **Google đi qua cổng
  trước**, adapter Gemini gọi thẳng để dự phòng — xem mục 3.2.

### Cần bạn chốt

| # | Câu hỏi | Đề xuất | Ảnh hưởng |
|---|---|---|---|
| Q5 | Tick "Tôi có quyền sử dụng ảnh này" bắt buộc với mọi ảnh tải lên? | Có | Đợt 3 |
| — | Đợt 3 làm bản đầy đủ (7,5–8 ngày) hay bản gọn (6 ngày)? | Tuỳ Q12: nếu Google qua cổng thì bản đầy đủ chỉ còn ~7 ngày | Đợt 3 |
| Q11 | Stability làm ở Đợt 3 (+1 ngày) hay Đợt 5 (đề xuất)? | Đợt 5, cùng tách nền và mở rộng khung | Đợt 3 / 5 |
| Q12 | Google: gọi thẳng bằng key AI Studio, hay chỉ qua cổng (vilao/NOVA có model `nano-banana`)? | Qua cổng trước, nhất quán với quyết định 25/09 ở AdVideo; adapter Gemini gọi thẳng làm dự phòng (0,5–1 ngày khi cần) | Đợt 3 (3C) |
| V | Có làm các việc video V1–V4 (mục 5) cho AdVideo không, và làm lúc nào? | V1 + V3 ngay khi có key; V4 giữ quyết định 25/09 | AdVideo |

---

## 1. Cổng vilao.ai (Q1 — đã chốt 29/09)

### 1.1 Hình dạng API (theo mẫu cURL bạn gửi)

```bash
# Tạo ảnh — JSON
curl https://api.vilao.ai/v1/images/generations \
  -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
  -d '{"model":"xai/grok-imagine-image","prompt":"…","n":1,"size":"auto"}'

# Sửa ảnh — multipart
curl https://api.vilao.ai/v1/images/edits \
  -H "Authorization: Bearer $KEY" \
  -F "model=xai/grok-imagine-image" -F "image=@./input.png" -F "mask=@./mask.png" \
  -F "prompt=Add a small hat to the panda" -F "size=auto"
```

Trùng chuẩn OpenAI Images → dùng adapter `OpenAiImages` (Đợt 1), không cần adapter mới. Từ máy chủ này
gọi tới `api.vilao.ai` được (không key → 401, đúng như mong đợi).

### 1.2 Cấu hình trên màn hình

| Chỗ | Giá trị |
|---|---|
| **Kết nối AI** | Base URL `https://api.vilao.ai/v1` (có `/v1`), API key của vilao |
| **Model tạo ảnh** | Adapter `OpenAiImages` · Model id `xai/grok-imagine-image` · Kích thước `auto` (khi có `SizeMode` ở Đợt 3: chọn `AspectRatio` nếu vilao chuyển tiếp `aspect_ratio`, không thì `Auto`) · Giá ước tính theo bảng giá vilao |
| **Tham số thêm** (chỉ khi vilao báo sai tham số) | `{"output_format": null, "quality": null}` — giá trị `null` là bỏ trường đó khỏi request |
| **Năng lực của Grok** | `TextToImage`, `ReferenceImages` (tối đa 5 theo tài liệu xAI), `InstructionEdit`. **Không** khai `MaskEdit` — tài liệu xAI không nhắc tới mask, nên vilao nhận `mask` nhưng Grok nhiều khả năng bỏ qua |
| Sau khi lưu | Bấm **"Chạy thử"** (tạo ảnh); từ Đợt 3 bấm thêm **"Chạy thử sửa ảnh"** |

### 1.3 Hai chỗ phải xử lý trong code (đã đưa vào Đợt 3)

1. **`size: "auto"` → model tự chọn khung.** Ảnh bìa 16:9 không chắc ra 16:9. Thêm
   `ImageModel.SizeMode`:
   - `Size` — như hiện nay, gửi `WxH` gần nhất trong danh sách kích thước của model;
   - `AspectRatio` — gửi `aspect_ratio: "16:9"` (kèm `size: "auto"`); Grok gốc có tham số này;
   - `Auto` — gửi `size: "auto"`, rồi máy chủ **cắt giữa về đúng tỉ lệ** người dùng chọn nếu lệch > 2%.
2. **Grok sửa ảnh bằng lời.** Sửa theo vùng với Grok đi **đường ảnh đánh dấu**: ảnh gốc + ảnh có vùng
   #1 #2 tô màu + chỉ dẫn có vị trí bằng lời. Bước ghép lại (D5) vẫn giữ nguyên 100% phần ngoài vùng.
   Nếu vilao chỉ nhận **một** `image` mỗi lần → gửi một ảnh gốc có **viền mảnh đánh số, không tô**, dặn
   model xoá viền (chất lượng trong vùng kém hơn; khi đó sửa vùng nên dùng model có mask).

### 1.4 Phải trả lời bằng một lần gọi thật

Mỗi câu tốn một hai ảnh; làm ngay khi có key.

| # | Câu hỏi | Cách kiểm | Nếu "không" |
|---|---|---|---|
| L1 | vilao có những model ảnh nào (gpt-image-2, nano-banana, seedream…) và giá? | `GET /v1/models` | Nhập tay từ trang Models của vilao |
| L2 | Kết quả trả `b64_json` hay `url`? Nếu `url` thì host nào? | Một lần tạo ảnh | `url` từ CDN khác host vẫn tải được qua `ImageDownloadGuard` (chỉ cần https + IP công khai) |
| L3 | `aspect_ratio` có được chuyển tiếp tới Grok không? | Gửi `aspect_ratio: "16:9"`, đo ảnh trả về | Dùng `SizeMode = Auto` + cắt giữa |
| L4 | `/images/edits` có nhận **nhiều** `image` (`image[]`) không? | "Chạy thử sửa ảnh" đường ảnh đánh dấu | Dùng cách một ảnh có viền mảnh (mục 1.3) |
| L5 | `mask` có tác dụng với Grok không? Với gpt-image qua vilao thì sao? | Mask một góc, so phần ngoài mask | Khai model đó là `InstructionEdit` thay vì `MaskEdit` |
| L6 | Lỗi kiểm duyệt trả mã gì? | Prompt bị cấm | Thêm mã vào `MapError` |
| L7 | vilao có API **video** không (`/v1/videos`)? | `GET /v1/models` | Bỏ việc V1 ở mục 5 |

**Để mình chạy các câu trên trong môi trường cloud này:** thêm biến môi trường **`VILAO_API_KEY`** trong
cài đặt môi trường (menu môi trường trên thanh tiêu đề phiên → Edit). Phiên mới sẽ đọc được. **Không dán
key vào chat.** Không có key thì mọi thứ vẫn làm và test được bằng server giả; chỉ phần "chạy thật" chờ.

---

## 2. Ba chuẩn API ảnh

### 2.1 So sánh

| | **OpenAI Images** | **Google Gemini** | **Stability AI** |
|---|---|---|---|
| Tạo ảnh | `POST /v1/images/generations` (JSON) | `POST /v1beta/models/{model}:generateContent` (JSON), `responseModalities: ["IMAGE"]` | `POST /v2beta/stable-image/generate/{ultra\|core\|sd3}` (multipart) |
| Sửa ảnh | `POST /v1/images/edits` (multipart: `image`/`image[]`, `mask`) | Cùng `generateContent`: ảnh gửi dạng `inline_data` + chỉ dẫn | `/v2beta/stable-image/edit/…`: `inpaint`, `erase`, `outpaint`, `search-and-replace`, `search-and-recolor`, `remove-background`, `replace-background-and-relight` |
| Xác thực | `Authorization: Bearer` | `x-goog-api-key` (key Google AI Studio) | `Authorization: Bearer` |
| Khung hình | `size` (`WxH` cố định hoặc `auto`) | `imageConfig.aspectRatio` (1:1, 3:2, 2:3, 3:4, 4:3, 4:5, 5:4, 9:16, 16:9, 21:9) | `aspect_ratio`; ảnh sửa **giữ kích thước ảnh vào** |
| Mask | Có — PNG, **alpha = 0 là vùng sửa** (`AlphaZeroIsEdit`) | Không có — sửa bằng lời | Có — **trắng là vùng sửa** (`WhiteIsEdit`), hoặc lấy từ kênh alpha |
| Kết quả | `b64_json` hoặc `url` | `candidates[].content.parts[].inlineData` (base64) | Thẳng bytes (`Accept: image/*`) hoặc JSON base64 |
| Model tiêu biểu | `gpt-image-2`, `gpt-image-2.5-*`, và mọi model qua cổng (vilao, 9Router, NOVA…) | Gemini 3.1 Flash Image, Gemini 3 Pro Image ("nano-banana") | Stable Image Ultra / Core, SD 3.5 |
| Ai dùng chuẩn này | OpenAI **và hầu hết các cổng** | Chỉ Google | Chỉ Stability |

**Điểm mấu chốt:** các cổng (vilao, 9Router, NOVA, OpenRouter…) quy nhiều model về chuẩn OpenAI. Nên
**phần lớn trường hợp chỉ cần adapter OpenAI**. Adapter gốc (Gemini, Stability) chỉ cần khi muốn gọi
thẳng — rẻ hơn, hoặc có tính năng mà chuẩn OpenAI không có (xoá vật thể, tách nền, mở rộng khung của
Stability).

### 2.2 Ánh xạ vào mô hình của ImageStudio

| | `Adapter` | `Capabilities` | `MaskConvention` | `SizeMode` |
|---|---|---|---|---|
| gpt-image qua OpenAI / vilao | `OpenAiImages` | `TextToImage`, `ReferenceImages`, `MaskEdit` (nếu L5 đạt) | `AlphaZeroIsEdit` | `Size` |
| Grok qua vilao | `OpenAiImages` | `TextToImage`, `ReferenceImages`, `InstructionEdit` | `None` | `AspectRatio` hoặc `Auto` |
| Gemini gọi thẳng | `Gemini` | `TextToImage`, `ReferenceImages`, `InstructionEdit` | `None` | `AspectRatio` |
| Stability | `Stability` (mới) | `TextToImage`, `MaskEdit`, + `ObjectErase`, `RemoveBackground`, `Outpaint` (mới, Đợt 5) | `WhiteIsEdit` | `AspectRatio` |

### 2.3 Đã bị khai tử — không làm

| Thứ | Tắt ngày | Thay bằng |
|---|---|---|
| `dall-e-2`, `dall-e-3`, và `/v1/images/variations` (chỉ DALL·E 2 dùng) | 12/05/2026 | `gpt-image-*` |
| `gpt-image-1` | **23/10/2026** | `gpt-image-2.5-*`. Model là dữ liệu (D2) → chỉ đổi trên màn hình Model |
| `gpt-image-1-mini`, `gpt-image-1.5`, `chatgpt-image-latest` | 01/12/2026 | `gpt-image-2.5-*` |
| Imagen 4 (`:predict`, bản standard / ultra / fast) | 17/08/2026 | Gemini 3.1 Flash Image |
| Imagen trên Vertex AI | — | Không làm: cần tài khoản dịch vụ Google Cloud (OAuth), nặng hơn nhiều so với key AI Studio |

---

## 3. Lộ trình adapter ảnh

| Việc | Đợt | Ngày | Trạng thái |
|---|---|---|---|
| OpenAI `/images/edits` (mask + nhiều ảnh) | 3 (3C) | trong kế hoạch Đợt 3 | Chắc chắn làm |
| `SizeMode` (`Size` / `AspectRatio` / `Auto` + cắt giữa) | 3 (3C) | 0,25 | Chắc chắn làm — cần cho vilao |
| Adapter **Gemini** gọi thẳng | 3 (3C) hoặc dự phòng | 0,5–1 | **Chờ Q12** |
| Adapter **Stability** | 5 (đề xuất) hoặc 3 | 1–1,5 | **Chờ Q11** |
| Adapter fal (queue) | — | 0,5 | Không làm trừ khi có key fal |

### 3.1 OpenAI edits + `SizeMode` (Đợt 3)

Đã mô tả trong [kế hoạch Đợt 3, bước 3C](dot-3-sua-theo-vung-2026-09-29.md#3c--adapter-sửa-ảnh-1-ngày).
Test: mỗi `SizeMode` gửi đúng trường; `Auto` cắt giữa ra đúng tỉ lệ; `image` khi một ảnh, `image[]` khi
nhiều ảnh; `mask` chỉ ở đường mask.

### 3.2 Google (chờ Q12)

**Bối cảnh:** 25/09, ở AdVideo, bạn yêu cầu *"xoá API Gemini trực tiếp"*. Veo đã được gỡ khỏi AdVideo, và
ai cần Veo thì đi qua cổng NOVA (`google/flow-veo`). NOVA cũng có model ảnh `google/nano-banana-{2,pro}`.

| Cách | Làm gì | Được | Mất |
|---|---|---|---|
| **A — Qua cổng (đề xuất)** | Không viết code: khai model `nano-banana` qua vilao/NOVA bằng adapter `OpenAiImages` (nếu cổng có — L1) | Một loại key, trả VNĐ, nhất quán với AdVideo; Đợt 3 bớt ~0,5–1 ngày | Phụ thuộc cổng có hỗ trợ sửa ảnh nhiều ảnh cho nano-banana không (L4) |
| **B — Gọi thẳng** | Adapter `Gemini` như bước 3C (tạo + sửa bằng lời + ảnh tham chiếu) | Không qua trung gian, đủ tính năng, không phụ thuộc cổng | Thêm key Google (cần thẻ quốc tế), đi ngược quyết định 25/09 |

Đề xuất: **A trước**. Chỉ làm B khi cổng không đáp ứng L1/L4, hoặc bạn đổi quyết định 25/09.

Nếu làm B, spec đã có trong 3C: `POST {base}/models/{model}:generateContent`, header `x-goog-api-key`,
`parts` = chữ + `inline_data` (ảnh gốc JPEG 92, ảnh đánh dấu PNG, tham chiếu); đọc `inlineData`;
`promptFeedback.blockReason` hoặc `finishReason` = `SAFETY` / `IMAGE_SAFETY` / `PROHIBITED_CONTENT` →
`ContentPolicy`; không có phần ảnh → `InvalidResponse`. Base URL
`https://generativelanguage.googleapis.com/v1beta`.

### 3.3 Stability (chờ Q11 — đề xuất Đợt 5)

**Vì sao đáng làm:** Stability là chuẩn duy nhất có **mask thật** cho sửa ảnh cộng các thao tác chuyên
biệt mà sản phẩm cần: tách nền (ảnh sản phẩm nền trắng), mở rộng khung (ảnh vuông 1:1 → ảnh bìa 16:9 mà
không cắt mất sản phẩm), xoá vật thể (xoá logo/người thừa mà không cần viết chỉ dẫn).

**Vì sao đề xuất Đợt 5:** giá trị lớn nhất (tách nền, mở rộng khung) phục vụ **ảnh sản phẩm**. Đợt 3 đã
đủ hai đường mask và ảnh đánh dấu qua vilao.

| # | Việc | Ngày |
|---|---|---|
| S1 | `ImageProviderAdapter.Stability`; `GenerateAsync` → `/generate/{ultra\|core\|sd3}` multipart (`prompt`, `negative_prompt`, `aspect_ratio`, `output_format`, `seed`), `Accept: image/*` nhận thẳng bytes. Endpoint chọn theo model id (`ultra`, `core`, `sd3.5-large`…) | 0,25 |
| S2 | `EditAsync` đường mask → `/edit/inpaint` (`image`, `mask` trắng là vùng sửa, `prompt`, `grow_mask`); ảnh trả về cùng kích thước ảnh vào nên `SizeFitter` đi nhánh không pad | 0,25 |
| S3 | Năng lực mới **`ObjectErase`** → `/edit/erase`: trình đánh dấu vùng có thêm lựa chọn **"Xoá"** cho vùng (không cần chỉ dẫn). Model không có năng lực này thì "Xoá" thành chỉ dẫn "xoá vật thể, lấp nền tự nhiên" | 0,25 |
| S4 | Năng lực mới **`RemoveBackground`** → `/edit/remove-background` (PNG trong suốt), dùng cho mẫu "Nền trắng TMĐT" ở Đợt 5 thay vì bắt người dùng khoanh sản phẩm | 0,25 |
| S5 | Năng lực mới **`Outpaint`** → `/edit/outpaint` (`left`/`right`/`up`/`down` tính từ khung đích): "Mở rộng thành ảnh bìa 16:9" từ ảnh sản phẩm vuông | 0,25 |
| S6 | Lỗi: 400 → `BadRequest`, 403 kiểm duyệt → `ContentPolicy`, 413 ảnh quá lớn, 422 → `BadRequest` kèm lời provider, 429 → `RateLimited`; test theo khuôn `OpenAiImageProviderTests`; chạy thật một lần mỗi endpoint | 0,25 |

Làm ở Đợt 3 thì chỉ S1 + S2 + S6 (~1 ngày); S3–S5 vẫn ở Đợt 5.
`replace-background-and-relight` chạy bất đồng bộ (phải poll) — không làm vòng này.

Giá Stability tính theo credit; nhập giá mỗi ảnh ở màn hình Model như mọi model khác (không có số giá nào
trong code).

---

## 4. Các chuẩn API video

Mọi API video đều **bất đồng bộ**: gửi yêu cầu → nhận mã → hỏi trạng thái (hoặc nhận webhook) → tải video
từ URL.

| Chuẩn | Gửi | Hỏi trạng thái / kết quả | Còn sống? | Ghi chú |
|---|---|---|---|---|
| **OpenAI Videos API** (Sora 2) | `POST /v1/videos` | `GET /v1/videos/{id}`, `GET /v1/videos/{id}/content` | **Không** — OpenAI tắt Videos API và mọi model `sora-2*` ngày **24/09/2026**, không có model thay thế | **Kiểu `/videos` vẫn sống ở các cổng**: NOVA, OpenRouter dùng đúng bộ ba này |
| **Google Veo 3.1** (Gemini API) | `POST /v1beta/models/{veo}:predictLongRunning` | `GET /v1beta/{operation name}` tới khi `done: true`; tải `video.uri` kèm key | Có (`veo-3.1-generate-preview`, `-fast-`, `-lite-`) | 16:9 và 9:16; có âm thanh; ảnh đầu/cuối, ảnh tham chiếu |
| **xAI Grok Imagine Video** | `POST /v1/videos/generations` | Hỏi theo `request_id` → URL video | Có (`grok-imagine-video-1.5`) | Tối đa 15 giây; ảnh → video, sửa video, kéo dài video |
| **Cổng kiểu OpenAI** (NOVA, OpenRouter, có thể cả vilao) | `POST {base}/videos` | `GET {base}/videos/{id}` | Có | Một key gọi nhiều model: Grok, Veo, Seedance, Wan, Kling (Kling qua NOVA chưa xác nhận) |
| **Hàng đợi tổng hợp: fal.ai** | `POST queue.fal.run/{model}` | `status_url` → `response_url` | Có | Kling, Veo, Wan, Hailuo, Seedance, LTX… |
| **Replicate** | `POST /v1/predictions` | `GET` theo `urls.get` hoặc webhook | Có | Nhiều model mã nguồn mở |
| **API riêng từng hãng** | Kling, Runway, MiniMax Hailuo, ByteDance Seedance, Alibaba Wan, Luma | Mỗi hãng một kiểu | Có | Mỗi hãng một cách xác thực — chỉ đáng làm khi cần tính năng riêng hoặc giá tốt hơn hẳn |

### 4.1 AdVideo đã có gì

- **Bộ máy khai báo provider bằng JSON** (`advideo.provider/v1`: `transport`, `submit`, `poll`, `result`,
  `errors`, `cost`) và `DeclarativeVideoProvider` đã có trong code. Kiểu hỏi trạng thái hỗ trợ:
  `PathTemplate` (NOVA, OpenRouter, xAI), `UrlFromSubmit` (fal), `ProbeUrl`. Thân request: **chỉ JSON**
  (chưa có multipart).
- Mẫu có sẵn: `samples/providers/fal-kling.json`, `samples/providers/nova-grok-video-15.json`
  (`xai/grok-imagine-video-1.5` qua NOVA).
- **Veo gọi thẳng đã bị gỡ** theo yêu cầu 25/09; Veo đi qua NOVA (`google/flow-veo`) hoặc OpenRouter
  (`veo-3.1-lite`).
- Việc OpenAI tắt Sora **không ảnh hưởng** AdVideo: AdVideo chưa bao giờ gọi thẳng OpenAI; kiểu
  `/videos` vẫn dùng qua cổng.

---

## 5. Lộ trình video (AdVideo)

Đi sau các bước P3/P4 của [kế hoạch provider khai báo](../ad-video-studio/ke-hoach-provider-khai-bao-2026-09-25.md)
(bộ máy đã có trong code). Không đụng tới NewsCMS ImageStudio.

| # | Việc | Code? | Ngày | Chờ |
|---|---|---|---|---|
| V1 | **vilao video**: nếu vilao có `/v1/videos` (L7) → descriptor `vilao-grok-video.json` sao từ `nova-grok-video-15.json`, chỉ đổi `baseUrl` và bảng giá; so giá với NOVA | Không | 0,25 + một lần gọi thật | Key vilao |
| V2 | **OpenRouter Video API** | Không | — | Đã có trong P4.4 của kế hoạch provider khai báo |
| V3 | **xAI gọi thẳng** cho 720p/1080p: descriptor `xai-grok-video.json` (`POST /v1/videos/generations`, poll theo `request_id`). Theo số 25/09, Grok 1.5 ở 1080p qua NOVA đắt gấp ~3 lần gọi thẳng. Tên các trạng thái lấy từ một lần gọi thật | Không | 0,25 + một lần gọi thật | Key xAI (cần thẻ quốc tế) |
| V4 | **Veo**: giữ quyết định 25/09 — qua NOVA `google/flow-veo` hoặc OpenRouter. Chỉ khi bạn đổi quyết định (Q12) mới làm Veo gọi thẳng; khi đó bộ máy cần thêm hai thứ: đọc trạng thái kiểu boolean (`done: true`) và tải video có kèm key | Có (nhỏ) | 0,5 | Q12 |
| V5 | **Sora / OpenAI Videos API** | — | — | Không làm — đã bị tắt |
| V6 | API cần thân **multipart** (một số hãng nhận ảnh đầu vào bằng file) | Có | 0,5 | Chỉ làm khi có hãng cụ thể cần |

---

## 6. Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| **Model bị khai tử nhanh** (gpt-image-1 tắt 23/10/2026, Imagen 4 và Sora đã tắt) | Model là dữ liệu (D2); nút "Chạy thử" + `LastTestOk` hiện trên trang Model; thêm model mới là sửa màn hình, không deploy |
| **Phụ thuộc một cổng** (vilao / NOVA dùng account pool upstream) | Luôn giữ ≥ 2 đường cho mỗi năng lực: cổng + một nhà cung cấp gọi thẳng (Stability hoặc Gemini cho ảnh; xAI hoặc fal cho video) |
| **Cổng nói "tương thích OpenAI" nhưng khác ở chi tiết** (`size: auto`, một `image`, bỏ qua `mask`) | Câu hỏi L1–L7 trả lời bằng lần gọi thật trước khi mở cho người dùng; "Chạy thử sửa ảnh" phát hiện ngay |
| **Giá trên cổng đổi hoặc sai** | `ImageProviderCall` là nguồn sự thật chi phí; giá nhập ở màn hình, không nằm trong code |

## 7. Nguồn (đọc 29/09/2026)

- xAI: [Image Editing](https://docs.x.ai/developers/model-capabilities/images/editing) · [Imagine API](https://docs.x.ai/developers/model-capabilities/imagine)
- OpenAI: [Deprecations](https://developers.openai.com/api/docs/deprecations) · [Sora discontinuation](https://help.openai.com/en/articles/20001152-what-to-know-about-the-sora-discontinuation)
- Google: [Imagen (đã khai tử)](https://ai.google.dev/gemini-api/docs/models/imagen) · [Tạo ảnh bằng Gemini](https://ai.google.dev/gemini-api/docs/image-generation) · [Veo 3.1](https://ai.google.dev/gemini-api/docs/veo)
- Stability: [Stable Image](https://platform.stability.ai/docs/getting-started/stable-image) · [LiteLLM — Stability](https://docs.litellm.ai/docs/providers/stability)
- Nội bộ: [ke-hoach-provider-khai-bao-2026-09-25.md](../ad-video-studio/ke-hoach-provider-khai-bao-2026-09-25.md) (NOVA catalog, OpenRouter Video API, quyết định xoá Gemini trực tiếp)
