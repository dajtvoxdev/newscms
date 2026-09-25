---
phase: planning
title: "AdVideo — Provider khai báo bằng JSON, nhiều nguồn video, TTS đổi được"
description: Nghiên cứu NOVA/OpenRouter/TTS tiếng Việt và thiết kế ProviderDescriptor v1 — thêm nhà cung cấp bằng cách dán JSON vào DB thay vì viết adapter C#
---

# Provider khai báo bằng JSON

> Trạng thái dự án: [trang-thai-va-lo-trinh-2026-09-25.md](./trang-thai-va-lo-trinh-2026-09-25.md)
> Yêu cầu gốc (25/09/2026): nhiều nguồn video thay vì chỉ fal · **xoá API Gemini trực tiếp** ·
> ElevenLabs quá đắt · **dán một mẫu JSON là apply được provider**.

## 1. Những gì tìm được, và nó đổi kế hoạch ra sao

### 1.1 NOVA là cổng video nhiều model — nhưng không có giọng

`GET https://novagateway.net/v1/catalog` (công khai, không cần key) trả 30 model. Phần media:

| Model | Loại | Giá NOVA | Ghi chú |
|---|---|---|---|
| `xai/grok-imagine-video` | video | 75–100đ/s ($0.05–0.07/s) | 480p / 720p |
| `xai/grok-imagine-video-1.5` | video | 120–360đ/s ($0.08–0.25/s) | thêm 1080p |
| `google/flow-veo` — **Veo 3** | video | **250đ / video 8 giây** | `billing_type: request`, 250 credits |
| `kling/kling-{1.6,2.1,2.6,3.0}` | video | **chưa có giá trong catalog** | xem cảnh báo bên dưới |
| `openai/gpt-image-2(.5)`, `xai/grok-imagine-image(-2.0)`, `google/nano-banana-{2,pro}` | ảnh | 80–120đ/ảnh | |

**Không có một model TTS/audio nào.** NOVA không thay được ElevenLabs — vế giọng phải giải riêng.

Ba điều quan trọng, theo thứ tự:

1. **Veo 3 đi qua NOVA được, giá 250đ/8 giây.** Đây là câu trả lời sạch cho "xoá API Gemini trực
   tiếp": không mất Veo, chỉ đổi đường vào — một Bearer key thay cho `x-goog-api-key`, và thoát luôn
   cái bẫy `gcsUri` (adapter Veo hiện gửi ảnh tham chiếu qua `gcsUri`, `VeoVideoProvider.cs:203` —
   presigned URL của MinIO không dùng được ở đó).
2. **Kling có trong catalog nhưng trang model ghi "Studio dùng Imagine (ảnh/video) và Kling (video)"**,
   và tài liệu `POST /v1/videos` chỉ liệt kê hai model grok. Giá Kling trong catalog là 0.
   → **Chưa được coi Kling-qua-NOVA là fact.** Phải thử một lời gọi thật với `model: "kling/kling-3.0"`
   trước khi đưa vào kế hoạch.
3. **NOVA có thuế so với gọi thẳng.** Grok 1.5 ở 1080p: NOVA $0.25/s so với xAI niêm yết $0.08/s —
   cao hơn ~3 lần. Giá trị thật của NOVA không phải là rẻ, mà là **trả bằng VNĐ không cần thẻ quốc
   tế**, cộng link kết quả công khai 24h không cần Bearer (tải về MinIO đơn giản hơn).
   Dùng NOVA ở 480p và cho Veo 3; lên độ phân giải cao thì gọi thẳng rẻ hơn.

Nguồn upstream của NOVA là account pool ("Nguồn upstream tổng hợp từ nhiều nguồn chất lượng 2api,
account pool" — nguyên văn trên trang model). Với một dịch vụ đem bán cho khách và có nghĩa vụ pháp lý
về nhãn AI, đây đúng là **R1 trong sổ rủi ro của chính dự án** ("provider biến mất giữa chừng"). Kết
luận thực dụng: dùng được, rẻ, nhưng **không để nó là đường duy nhất** — đó cũng chính là lý do phải
làm provider khai báo.

### 1.2 OpenRouter có Video API cùng hình dạng — nên làm trước NOVA

`POST /api/v1/videos` → poll `GET /api/v1/videos/{id}` → `/content`: **trùng khít bộ ba của NOVA**.
Một mẫu JSON chạy được hai cổng, chỉ khác `baseUrl`. Thêm ba thứ NOVA không có:

- `GET /api/v1/videos/models` trả lưới thời lượng, tỉ lệ khung, độ phân giải **và giá** từng model →
  `CapabilityJson` **tự sinh** thay vì gõ tay. Đây là thuốc chữa đúng căn bệnh mà
  `ProviderCapabilityCatalog` đang mắc: số liệu chết dần trong code.
- `generate_audio: false` chuẩn hoá xuyên model → phục vụ **D3** bằng một trường, thay cho ba dòng
  đoán mò `enable_audio`/`generate_audio`/`bgm` ở `FalQueueVideoProvider.cs:213-215`.
- `callback_url` → bỏ được vòng poll.

Giá một shot 8 giây 9:16 (số của agent nghiên cứu, đọc 25/09/2026 — **cần xác nhận lại trước khi
chốt**): seedance-2.0-mini **$0.27**, seedance-2.0-fast $0.32, wan-3.0 $0.34, veo-3.1-lite $0.40,
grok-imagine $0.40. So với bảng giá **đang nằm trong code**: Kling $1.36, Seedance $2.40.

### 1.3 Bảng giá trong code sai nặng — sửa nó là việc rẻ nhất trong cả kế hoạch

| Chỗ | Đang khai | Thực tế | Sai |
|---|---|---|---|
| `ProviderCapabilityCatalog.cs:91` seedance | $0.30/s | $0.034–0.10/s | **3–9×** |
| `ProviderCapabilityCatalog.cs:70` kling | $0.17/s | ~$0.11/s | ~1,5× |
| `ProviderCapabilityCatalog.cs:143` elevenlabs | $0.30/1000 ký tự | $0.05 (Flash) – $0.10 (v2/v3) | **3–6×** |

D8 nói "ước tính chi phí **trước** khi gọi". Ước cao gấp 8 lần vẫn là ước sai — chỉ sai về phía an toàn,
và nó làm trần chi tiêu từ chối oan những job hợp lệ.

### 1.4 ElevenLabs đang được cấu hình bằng một model không đọc được tiếng Việt

`ProviderCapabilityCatalog.cs:133` đặt `ModelId = "eleven_multilingual_v2"`, dòng 138 khai
`SupportedLanguages = ["vi","en"]`. **`eleven_multilingual_v2` không có tiếng Việt** — tiếng Việt chỉ
có từ Flash/Turbo v2.5 trở đi. Manifest đang hứa một năng lực model không có, và lỗi này sẽ lộ ra đúng
lúc gọi ElevenLabs thật lần đầu.

Đổi `eleven_multilingual_v2` → **`eleven_flash_v2_5`** vừa sửa lỗi vừa giảm giá một nửa ($0.10 → $0.05
mỗi 1000 ký tự). Không dùng `eleven_turbo_v2_5`: trang models của ElevenLabs đã xếp Turbo vào mục bị
Flash thay thế.

### 1.5 Vế "voice giá chát": mốc thời gian mới là thứ quyết định, không phải giá

Bước 5 `LockTimeline` cần mốc theo từ/ký tự. `TtsStep.cs:138-144` fail cứng nếu thiếu, và có test khoá
hành vi đó. Khảo sát:

| Nguồn | Giá /1000 ký tự | Mốc thời gian | Dán JSON được? |
|---|---|---|---|
| ElevenLabs Flash v2.5 | $0.05 | **có**, ký tự, `/with-timestamps` | được |
| **Azure Batch Synthesis** | **~$0.016** | **có**, từ — `wordBoundaryEnabled: true` → file `word.json` | **không** (kết quả là zip, cần adapter C#) |
| MiniMax speech-02-turbo | ~$0.04 | **mốc theo CÂU** (≤50 ký tự) dù tham số có `word` | được, nhưng mốc không đủ mịn |
| FPT.AI v5 | **333đ ≈ $0.013** | **không** | **không** — body `text/plain`, tham số nằm ở HTTP header |
| VBee / Viettel | ~$0.01 (chưa xác nhận) | chưa xác nhận | chưa rõ |
| VieNeu tự host | 0đ | **không** | được |
| Google Cloud TTS | $16/1 triệu | chỉ `<mark>` tự chèn, không chắc chắn | auth OAuth2 → không |
| OpenAI TTS · Amazon Polly | — | không có mốc · **không có tiếng Việt** | loại |

Hai điều phải nói thẳng:

**(a) Một lập luận hấp dẫn nhưng sai:** "mốc chỉ dùng để tìm khoảng lặng nên sai số ±50ms vô hại, cứ
dùng TTS rẻ rồi tự gióng (forced alignment)". Mốc còn chảy vào hai chỗ nữa trong
`TimelineLocker.cs`: nó quyết định **bậc thời lượng shot** (`Math.Ceiling(span - 0.05)` ép về lưới của
provider, dòng 240-241) — tức **quyết định tiền**, vì shot tính tiền theo giây — và quyết định ngưỡng
**từ chối**. Ngân sách dung sai của hệ thống là `DurationToleranceSeconds = 0.05` (dòng 33), đã tiêu
hết vào nhiễu làm tròn. Một sai số ±50–100ms đủ lật một đoạn thoại 6,02 giây từ bậc 6 lên bậc 8 trên
lưới `[4,6,8]` — trả thêm tiền mà không ai biết vì sao.

**(b) Nhưng tiền TTS không phải chỗ đáng cắt.** Với 170 ký tự/video × 1000 video/tháng: ElevenLabs
Flash ≈ $8,50/tháng, FPT ≈ $2,15/tháng. Cùng 1000 video đó, riêng phần **video** đã $270–2.000/tháng.
Tiết kiệm ở TTS là dưới 2% hoá đơn.
→ **Lý do đáng làm việc này là gỡ khoá độc quyền và sửa lỗi cấu hình, không phải tiết kiệm.** Đòn bẩy
chi phí thật nằm ở bước 6 RenderShots: đổi Seedance-qua-fal ($2,40/shot theo giá đang khai) sang
seedance-2.0-mini qua OpenRouter ($0,27/shot) là **giảm ~9 lần**, và đó chính là thứ provider khai báo
mở ra.

### 1.6 Hôm nay dán JSON vào DB **không** chạy được provider mới

`ProviderRegistry.CreateVideoProvider` (`:306-317`) và `CreateTtsProvider` (`:335-344`) là hai câu
`switch` cứng trên tên provider, nhánh mặc định ghi log "chưa viết code" rồi trả `null` — provider bị
bỏ khỏi danh sách trong im lặng. Hai cửa khác cũng đóng: `ProviderCapabilityCatalog.CategoryOf`
(`:177-184`) trả `null` cho tên lạ nên `set-credential` **từ chối** provider mới
(`AdminCommands.cs:159-166`); và `DbCredentialStore.LoadCapabilityRowsAsync` (`:231-236`) chỉ `SELECT`
hai cột nên cột descriptor mới sẽ không bao giờ tới được registry.

Tin tốt: **một nửa đã khai báo rồi.** `ProviderCredential.CapabilityJson` đã ở trong DB và
`ProviderCapabilityCatalog` chỉ là mẫu điền lần đầu cho CLI, không phải nguồn sự thật lúc chạy. Nên
"provider này hỗ trợ 9:16, lưới 4/6/8, giá $0.12/giây" **đã** đổi được bằng JSON. Thứ còn hard-code là
**giao thức dây**: URL, header, hình dạng body, tên field kết quả, luật poll, bảng giá, map lỗi.

### 1.7 Hai lỗ bảo mật có sẵn — descriptor sẽ nhân bản chúng nếu không bịt trước

- **SSRF + xuất khẩu credential.** `FalQueueVideoProvider` gắn `Authorization: Key <apiKey>` vào
  `_http.DefaultRequestHeaders` (`:52-53`) rồi **GET thẳng** `status_url` và `response_url` lấy từ
  *thân phản hồi của provider* (`:108`, `:227`), không có allowlist host nào. Một phản hồi bị sửa là
  key đi tới host của kẻ tấn công. Descriptor làm URL trở thành **dữ liệu người vận hành gõ vào**, nên
  lỗ này phải bịt trước dòng descriptor đầu tiên.
- **Rò key vào DB.** Toàn bộ thân phản hồi provider được ghi nguyên văn: `RawError =
  payload.RootElement.ToString()` → `ProviderCall.RawError` (4000 ký tự) và `job.RawProviderError`.
  Nhiều API dội lại request đã gửi trong thông báo lỗi.

## 2. Thiết kế: `ProviderDescriptor` v1

### 2.1 Biên giới — câu kiểm định

> **Descriptor chỉ phục vụ provider có hình dạng: submit → (poll) → đọc JSON; xác thực bằng header
> tĩnh; kết quả là URL hoặc base64 nằm trong cùng một cây JSON. Mọi thứ khác viết C#.**

Có câu này thì Azure Batch Synthesis (kết quả là zip), Kling gọi thẳng (ký HMAC mỗi 30 phút), Google
Cloud TTS (OAuth2), Cartesia (mốc chỉ trên WebSocket) **rơi ra ngoài một cách rõ ràng** thay vì kéo
schema phình ra thành một ngôn ngữ lập trình trong cột DB.

### 2.2 Mười một khối

Lưu ở **bảng mới `ProviderDescriptors`** theo khuôn `PromptTemplate` (`Code` + `Version` + `IsActive`)
— khuôn versioning/rollback duy nhất đã có trong repo.

```jsonc
{
  "schema": "advideo.provider/v1",
  "name": "nova-grok-video-15",     // = ProviderCredential.Provider. MỘT descriptor = MỘT tên
  "kind": "video",                   // video | tts
  "transport": {
    "baseUrl": "https://novagateway.net/v1",   // bắt buộc https
    "auth": { "in": "header", "name": "Authorization", "scheme": "Bearer",
              "valueRef": "{{secret.api_key}}" },   // THAM CHIẾU, không bao giờ là key thật
    "headers": { "Accept": "application/json" },
    "requestTimeoutSeconds": 120
  },
  "defaults":    { "resolution": "480p" },           // thứ VideoRequest không có
  "constraints": { "maxInputChars": 5000, "maxReferenceImages": 1 },
  "valueMaps":   { "aspect": { "Portrait9x16": "9:16", "Square1x1": "1:1",
                               "Landscape16x9": "16:9" } },
  "capability":  { /* nguyên văn VideoProviderCapability → ghi vào CapabilityJson */ },
  "submit": {
    "method": "POST", "path": "/videos",
    "body": { "kind": "json", "template": {
      "model": "{{model_id}}",
      "prompt": "{{prompt}}",
      "seconds": "{{seconds|string}}",              // NOVA cần chuỗi; fal cần số
      "extra_body": { "resolution": "{{resolution}}",
                      "aspect_ratio": "{{aspect_ratio|map:aspect}}" },
      "image": { "@when": "{{image_url}}", "url": "{{image_url}}" }
    } },
    "successStatus": [200, 201, 202],
    "failWhen": [ { "path": "error", "notEquals": 0 } ]   // HTTP 200 mà thân báo lỗi
  },
  "poll": {
    "mode": "pathTemplate",          // none | pathTemplate | urlFromSubmit | probeUrl
    "path": "/videos/{{provider_request_id}}",
    "intervalSeconds": 5, "maxWaitSeconds": 660,   // BẮT BUỘC có trần
    "statusPath": "status",
    "statusMap": { "queued": "pending", "processing": "pending",
                   "completed": "succeeded", "failed": "failed" }
  },
  "result": {
    "requestIdPath": "id",
    "videoUrlPath": "url || video_url || share_url",
    // kind=tts: audioBase64Path, alignment.{format,charactersPath,startsPath,endsPath}
  },
  "errors": {
    "byBodyCode": { "path": "error.code", "table": { "content_policy": "ContentRejected" } },
    "byStatus":   { "402": "ProviderUnavailable", "429": "RateLimited",
                    "401": "ProviderUnavailable", "5xx": "ProviderUnavailable" },
    "default": "Unknown",
    "deactivateCredentialOn": ["402"]
  },
  "cost": { "unit": "perSecond",
            "rateBy": { "variable": "resolution",
                        "table": { "480p": 0.08, "720p": 0.14, "1080p": 0.25 } },
            "extras": [ { "unit": "perReferenceImage", "rateUsd": 0.01 } ] }
}
```

**Quy tắc thay biến** (đủ để diễn đạt cả NOVA lẫn fal):

- Thay trên **cây JSON đã parse**, không nối chuỗi rồi parse lại → một prompt chứa `"` hay `}` không
  phá được cấu trúc body. Đây là chống template injection bằng **cấu trúc**, không bằng bộ lọc.
- Chuỗi chỉ gồm đúng một `{{var}}` giữ **kiểu tự nhiên**: `"{{seconds}}"` → `6` (số). Ép kiểu bằng
  hậu tố: `{{seconds|string}}` → `"6"`; còn `|int |bool |not |urlencode |map:`.
- `null`/rỗng thì **cặp khoá-giá trị biến mất**, không gửi `null`.
- `@when` cho nhánh tuỳ chọn, `@each` cho mảng (`reference_images`).
- Không gian tên biến **đóng và có kiểm**: biến lạ → từ chối lưu, không im lặng bỏ qua.

**Descriptor cố tình KHÔNG làm** (viết C#): ghép mốc ký tự thành từ · đo độ dài audio và dò track
tiếng bằng ffprobe · chia văn bản dài rồi nối audio và dịch mốc · OAuth2/HMAC/SigV4 · upload
multipart · webhook + HMAC · SSE · **luật retry và circuit breaker** (luật về tiền, không phải đặc
điểm provider) · **chọn provider (Luật 3)** — descriptor không bao giờ được tự khai "luôn chọn tôi".

### 2.3 Nối vào code: sửa một chỗ

```
CreateVideoProvider(credential):
  1. descriptor = DescriptorStore.GetActive(credential.Provider)     ← MỚI
  2. có     → new DeclarativeVideoProvider(...)
  3. không  → switch cũ (adapter viết tay còn lại)
  4. không  → LogUnknown (giữ nguyên)
```

Giữ nguyên không sửa dòng nào: provider vẫn dựng **mỗi lần gọi** nên đổi descriptor có hiệu lực không
cần restart; `SelectVideoProviderAsync` (Luật 3) không biết descriptor tồn tại;
`ProviderCapabilityValidator` không đổi vì loader ghi khối `capability` thẳng vào `CapabilityJson`.

### 2.4 Bảo mật — ba luật bắt buộc

1. **Allowlist host nằm ở `SystemSetting`, KHÔNG nằm trong descriptor.** Nếu host hợp lệ khai trong
   chính file JSON người vận hành dán vào thì người dán tự cấp quyền gọi ra bất cứ đâu. Allowlist áp
   cho **cả ba** nguồn URL: `baseUrl`, URL provider trả về, URL tải file.
2. **Một `HttpClient` riêng, không mang header auth, để tải URL provider trả về.** Bịt lỗ 1.7.
   Header đặt **trên từng request**, không mutate `DefaultRequestHeaders` — tên header đến từ
   descriptor nên client dùng chung sẽ rò header provider này sang provider kia.
3. **`{{secret.*}}` chỉ hợp lệ trong `transport.auth` và `transport.headers`**; xuất hiện chỗ khác →
   từ chối lưu. Validator quét descriptor theo mẫu key quen thuộc (`sk-`, `xi-`, hex ≥32, JWT) và từ
   chối nếu trông như đang chứa key thật. Trước khi ghi `RawError`, thay mọi chuỗi trùng key bằng
   `MaskedKey`.

Cộng: descriptor mới luôn vào ở trạng thái **tắt** (`IsActive=false`), chỉ bật bằng một lệnh riêng sau
khi chạy khô xanh. Ghi `DescriptorSha256` vào mỗi dòng `ProviderCall` để một clip hỏng tra ngược được
ra đúng bản descriptor đã sinh ra nó.

## 3. Triển khai

Phụ thuộc cứng: **việc này đi sau Đợt A** của lộ trình (migration + compose chạy được). Bảng
`ProviderDescriptors` phải nằm trong `InitialCreate`, không phải một migration thứ hai — dự án hiện
chưa có migration nào.

### P0 — Sửa cái sai, trước khi xây (nửa ngày, không cần key)

| # | Việc | Vì sao trước |
|---|---|---|
| P0.1 | `eleven_multilingual_v2` → `eleven_flash_v2_5` | model đang cấu hình **không đọc được tiếng Việt** |
| P0.2 | Sửa bảng giá: elevenlabs 0.30 → 0.05; seedance 0.30 → 0.10; kling 0.17 → 0.11 | D8 đang gác bằng số sai 3–9 lần |
| P0.3 | 402 → `ProviderUnavailable` (đang rơi xuống `Unknown`, mà `Unknown` **retry được**) | hết tiền thì hệ thống đang thử lại đủ số lần rồi mới bỏ |
| P0.4 | Map `error.code` theo **mã**, không theo marker chuỗi | `content_policy` (gạch dưới) không khớp marker `"content policy"` (dấu cách) → job bị kiểm duyệt báo sai lý do |
| P0.5 | `ReportedCostUsd` chỉ điền khi provider **thật sự báo giá** | fal và ElevenLabs đang điền từ đơn giá manifest → `CostIsReported = true` cho mọi lời gọi, mất khả năng đối soát hoá đơn |

### P1 — Phần thuần hàm, có test, chưa đụng adapter nào (2–3 ngày)

`ProviderDescriptor` records · `DescriptorValidator` · `JsonTemplateRenderer` · `JsonPathReader` ·
`DescriptorCostCalculator`. Tất cả nằm trong `AdVideo.Core`, thuần hàm, test 100% không cần mock —
cùng khuôn `CostEstimator`/`NativeSoundTranslator`. Đây vừa là nền của tính năng vừa là một phần của
món nợ test ở Đợt B.

Chú ý: `ProviderJson` hiện **chỉ trả về chuỗi** (`FindString` lọc cứng `ValueKind == String`), không
đọc được số hay mảng — mà mọi nguồn mốc thời gian đều là **mảng số**. `JsonPathReader` phải viết mới,
không uỷ quyền được. Chốt một mô hình JSON duy nhất cho lớp này (`JsonNode`) trước khi viết dòng đầu.

### P2 — Lớp chặn SSRF + redact key (1 ngày)

`SsrfGuardingHandler` + allowlist trong `SystemSetting` + client tải file không mang auth + quét key
khỏi log và `RawError`. **Trước** khi descriptor đầu tiên chạy thật.

### P3 — Entity, store, CLI, engine (2–3 ngày)

`ProviderDescriptorRow` + `DbDescriptorStore` (khuôn `DbPromptStore` + `StoreCacheSignal`) · CLI
`set-descriptor` / `list-descriptors` / `activate-descriptor` / `test-descriptor` ·
`DescriptorHttpExecutor` + `DeclarativeVideoProvider` / `DeclarativeTtsProvider` · mở ba cửa đóng ở
mục 1.6.

### P4 — Chuyển từng provider, có lưới an toàn (2–3 ngày)

Thứ tự có chủ đích:

1. **fal → descriptor**, kèm **golden test**: với một bộ `VideoRequest` cố định, body do
   `JsonTemplateRenderer` dựng phải **byte-for-byte** bằng body của `FalQueueVideoProvider` hôm nay.
   Đây là bằng chứng engine đủ sức, trước khi bỏ adapter cũ.
2. **ElevenLabs → descriptor** y hệt cách đó, giữ `BuildWordTimings` thành hàm dùng chung.
3. **Xoá Veo**: `VeoVideoProvider.cs` (325 dòng), hai nhánh trong `ProviderRegistry.cs:308-309`, hằng
   `ProviderNames.Veo`, manifest, `CategoryOf`, `DefaultEndpoint`, 6 chỗ trong
   `ProviderCapabilityValidatorTests`, `spike/run_veo.py` + nhánh veo trong `measure_ratelimit.py`,
   `GEMINI_API_KEY`/`VEO_MODEL*` trong `spike/.env*`, và các ví dụ trong `README.md:130-132` +
   `CONFIGURATION.md:241`. Veo **không phải mặc định ở bất kỳ đâu** trong code, nên việc xoá sạch hơn
   vẻ ngoài. Ai cần Veo thì đi qua NOVA `google/flow-veo`.
4. **Thêm OpenRouter bằng descriptor** — không viết dòng C# nào.
5. **Thêm NOVA** bằng cùng mẫu, khác `baseUrl`. **Đây là phép kiểm chứng của cả yêu cầu**: nếu bước
   này cần sửa code thì thiết kế chưa đạt.

### P5 — TTS rẻ (1–2 ngày, sau khi P4 xong)

Hai đường, chọn tường minh chứ đừng trôi:

- **Đường rẻ và an toàn nhất hiện nay: giữ ElevenLabs nhưng đổi sang `eleven_flash_v2_5`** (P0.1) —
  $0.05/1000 ký tự, mốc ký tự gốc, code đã có và đã có test. Xong trong một dòng.
- **Đường rẻ hơn nữa, tốn công hơn: Azure Batch Synthesis** (~$0.016/1000 ký tự, **có mốc theo từ**
  qua `wordBoundaryEnabled`, có 2 giọng vi-VN thật). Nhưng kết quả là **zip**, submit/poll khác hình
  dạng → **không dán JSON được, phải viết adapter C# riêng**. Đánh giá bằng một thử nghiệm 15 phút
  trên free tier 500K ký tự/tháng trước khi cam kết.
- FPT.AI ($0.013) rẻ nhất nhưng **hai rào**: không có mốc, và **không nhận JSON body** (thân
  `text/plain`, tham số nằm ở HTTP header, tên header là `api_key` có gạch dưới). Nếu muốn dùng, phải
  (a) thêm `submit.headersFromRequest` vào schema, và (b) giải bài toán mốc bằng forced alignment —
  một sidecar Python + PyTorch trên VPS không GPU, ~3–10 giây CPU mỗi clip. **Đề xuất: không làm ở
  vòng này.** Tiết kiệm thêm ~$6/tháng không đáng đổi lấy một runtime mới trên đường chạy chính.

## 4. Phải trả lời bằng một lần gọi thật, trước khi chốt

Mỗi câu dưới đây tốn vài nghìn đồng và chặn một nhánh của kế hoạch:

1. **`POST /v1/videos` có nhận `model: "kling/kling-3.0"` và `"google/flow-veo"` không?** Tài liệu chỉ
   viết về hai model grok; catalog thì có cả Kling lẫn Veo 3. Nếu có → NOVA thay được fal-Kling và
   đóng luôn vế "xoá Gemini". Nếu không → Kling chỉ dùng được trong Studio, kế hoạch NOVA thu về hai
   model grok + Veo 3.
2. **Veo 3 qua NOVA có nhận 9:16 và ảnh tham chiếu không, và 250đ/8s có đúng là giá cuối không?**
3. **Hạn mức gọi song song của NOVA** — trang "Rate limits" mà tài liệu lỗi trỏ tới đang **404**, nên
   `MaxConcurrentShots` chưa có cơ sở nào.
4. **Giá OpenRouter và lưới thời lượng từng model** — lấy bằng `GET /api/v1/videos/models`, đồng thời
   kiểm luôn ý tưởng tự sinh `CapabilityJson`.
5. **Azure vi-VN + `wordBoundaryEnabled`** có ra `word.json` dùng được không (15 phút, 0đ).

Và một việc không tốn gì: **`spike/.env` đang giữ `FAL_KEY` thật mà `.gitignore` không có dòng nào
khớp `.env`** — thêm NOVA key vào đó là thêm một key nữa vào cùng chỗ hở.
