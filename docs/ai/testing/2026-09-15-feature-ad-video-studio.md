---
phase: testing
title: Testing Strategy
description: Chiến lược kiểm thử AdVideo — unit, integration với provider giả lập, smoke test có lịch gọi thật, và nghe/nhìn thủ công
---

# Xưởng video quảng cáo AI — Chiến lược kiểm thử

> Yêu cầu: [../requirements/2026-09-15-feature-ad-video-studio.md](../requirements/2026-09-15-feature-ad-video-studio.md)
> Thiết kế: [../design/2026-09-15-feature-ad-video-studio.md](../design/2026-09-15-feature-ad-video-studio.md)

## Test Coverage Goals

### Luật nền: test tự động không được tiêu tiền

**Mọi test chạy trong CI đều dùng provider giả lập.** Không có ngoại lệ. Một suite test gọi Veo thật là một suite test tốn vài đô mỗi lần push, và sẽ bị tắt sau hai tuần.

Ba tầng tách bạch:

| Tầng | Gọi ra ngoài? | Chạy khi nào | Ai kích hoạt |
|---|---|---|---|
| **Unit** | Không bao giờ | Mỗi lần build | CI + local |
| **Integration** | Không — `FakeVideoProvider`, `FakeTtsProvider`, MinIO trong container, FFmpeg thật | Mỗi lần push | CI |
| **Smoke test provider** | **Có, gọi thật** | 1 lần/ngày theo lịch + trước mỗi release | Hangfire recurring job, không phải CI |

### Mục tiêu độ phủ

- **Unit: 100% code mới/sửa** trong `AdVideo.Core` — đây là nơi chứa logic khoá timeline, làm tròn lưới, validate capability, tính chi phí. Toàn bộ là hàm thuần, không có lý do gì không phủ hết. Cách đọc con số này (entity EF và DTO không tính vào ngưỡng): xem *Ngưỡng đọc thế nào* ở mục Test Reporting & Coverage.
- **Integration: mọi đường đi tới hỏng hóc**, không chỉ đường happy. Provider timeout, provider trả 429, shot lỗi giữa chừng, webhook gửi trùng.
- **E2E: 4 hành trình chính** (xem mục End-to-End).
- Phần `AdVideo.Infrastructure` chạm HTTP/FFmpeg/S3: không đặt mục tiêu 100% — phủ qua integration test thay vì mock từng dòng.

### Ràng buộc từ tiêu chí nghiệm thu

Mỗi tiêu chí S1–S6 trong requirements phải có ít nhất một test tự động hoặc một mục kiểm tay có chữ ký. Không có tiêu chí nào chỉ dựa vào "nhìn thấy nó chạy được".

## Unit Tests

### `TimelineLocker` — khoá timeline (bước 5)

Đây là chỗ dễ sai nhất trong cả hệ. Phủ dày nhất ở đây.

- [ ] Audio 4,8 giây + lưới Veo (4/6/8) → shot 6 giây, dư 1,2 giây bù bằng hold-frame
- [ ] Audio 4,8 giây + lưới Kling (bước 1s, 3–15) → shot 5 giây, dư 0,2 giây
- [ ] Audio 0,5 giây → làm tròn lên **mức tối thiểu của provider**, không ra shot 0 giây
- [ ] Audio 16 giây trên Kling (max 15) → **tách thành hai shot**, không cắt cụt lời thoại
- [ ] Audio 9 giây trên Veo (max 8) → tách shot, điểm cắt rơi vào **khoảng lặng giữa câu**, không giữa từ
- [ ] Cùng một storyboard qua Veo và Kling cho ra **số shot khác nhau** — và cả hai đều hợp lệ
- [ ] Tổng thời lượng sau khoá ≥ tổng thời lượng audio (không bao giờ ngắn hơn)
- [ ] Lệch tích luỹ qua 10 shot vẫn **< 200 ms**
- [ ] Không bao giờ sinh chỉ thị "kéo giãn video" — chỉ hold-frame hoặc Ken Burns

### `ProviderCapabilityValidator` — Luật 2

- [ ] Có `talent` + provider `veo` → `422` với mã lỗi đọc được và **gợi ý Kling**
- [ ] Có `talent` + provider `kling` → hợp lệ
- [ ] 5 ảnh tham chiếu + `veo` (tối đa 3) → `422`, nêu rõ thừa mấy ảnh
- [ ] 7 ảnh chia theo chủ thể + `vidu` → hợp lệ
- [ ] `aspect_ratio: "1:1"` + `veo` (chỉ 16:9, 9:16) → `422`
- [ ] Format `daily_soft_sell` + `vidu` → `422` (Vidu nối frame ở endpoint khác — cố ý vắng mặt trong `requires`)
- [ ] Format `daily_soft_sell` mà không upload ảnh người → `422` **ở tầng API**, không chạy đến bước 6
- [ ] `provider: null` + không có talent + tier standard → chọn Veo 3.1 Fast
- [ ] `provider: null` + có talent + tier standard → chọn Kling 3.0
- [ ] `provider: null` + brief có **≥2 chủ thể** phải giữ nhất quán → chọn **Vidu**, thắng lựa chọn theo tier
- [ ] `provider: null` + tier premium → Seedance cho cả hai trường hợp
- [ ] Bảng ánh xạ đọc từ cấu hình — đổi giá trị trong config, kết quả đổi theo, **không phải sửa code**

### `NativeSoundTranslator` — D3 + Luật 2

- [ ] Mặc định (không khai `native_sound`) → `off` trên **cả bốn** provider
- [ ] `off` + Vidu → phải **tắt tường minh** (Vidu bật sẵn — chỗ dễ quên nhất)
- [ ] `sfx_only` + Vidu → đặt chế độ `sfx`, **không** thêm negative prompt
- [ ] `sfx_only` + Veo/Kling/Seedance → bật audio + negative prompt chứa `"no speech, no dialogue, no voices"`
- [ ] `full` + Veo/Kling/Seedance → cảnh báo trong response rằng không tách sạch được thoại
- [ ] Mọi nhánh `sfx_only` đều **bật cờ yêu cầu VAD** ở bước 9

### `StoryboardValidator` + bộ lọc cụm từ cấm (R3)

- [ ] Lời thoại chứa "số 1 Việt Nam" → chặn **trước khi gọi TTS**
- [ ] "tốt nhất", "duy nhất", "hàng đầu" không kèm căn cứ → chặn
- [ ] "tốt nhất cho da khô theo thử nghiệm X" (có căn cứ) → cho qua
- [ ] Chặn ở bước 2, **không tốn một đồng TTS nào**
- [ ] Từ khoá cấm nạp từ cấu hình, không hard-code
- [ ] Storyboard thiếu `negative_prompt_ref` → tự gắn `global_no_text` (D4)
- [ ] Mọi prompt gửi provider đều chứa `"no text, no letters, no captions, no logo, no watermark"`

### `CostCalculator` + `CreditEstimator`

- [ ] 30 giây, 6 shot, tier standard → khớp bảng giá vốn trong design (±1%)
- [ ] `POST /v1/estimates` là **hàm tất định** — gọi 100 lần ra đúng một con số
- [ ] Format daily nhân hệ số 2,0× (credit) / 2,8× (hệ số retry giá vốn)
- [ ] Regenerate một shot → tính theo độ dài shot đó, không theo cả job
- [ ] Vượt `max_credits` → job dừng, không render tiếp
- [ ] `ProviderCall` ghi **mỗi lần gọi**, kể cả lần thất bại

### `IdempotencyGuard` + webhook

- [ ] Cùng `Idempotency-Key` gửi hai lần → một job, response thứ hai trả job cũ
- [ ] Thiếu `Idempotency-Key` → `400`
- [ ] Webhook ký HMAC-SHA256 đúng với secret của tenant
- [ ] Timestamp quá 5 phút → từ chối (chống replay)
- [ ] Webhook gửi trùng `job.completed` → bên nhận đối chiếu được bằng `job_id`, **không tính tiền hai lần**

### `TenantScopeFilter` — cách ly dữ liệu

- [ ] `VoiceProfile` có `TenantId` của A → **không xuất hiện** trong `GET /v1/voices` của B
- [ ] `VoiceProfile` có `TenantId` null → xuất hiện cho mọi tenant
- [ ] `AdFormat` riêng của tenant A → không hiện cho B; kho chung hiện cho cả hai
- [ ] Truy vấn job của tenant khác bằng `job_id` đoán được → `404`, không phải `403` (không rò rỉ sự tồn tại)
- [ ] Filter nằm ở **tầng repository**, không phải tầng UI — test gọi thẳng repository

### `AiLabelStamper` — D9

- [ ] Nhãn hiển thị được chèn ở **mọi** đường đi ra, kể cả đường lỗi có partial output
- [ ] Không có cờ nào tắt được nhãn — test cố tình truyền `disable_label: true` → vẫn có nhãn
- [ ] Metadata nhúng có mặt trong file MP4 xuất ra
- [ ] Hình thức nhãn đọc từ **cấu hình** (vì pháp chế chưa chốt) nhưng sự tồn tại thì không cấu hình được

## Integration Tests

Dùng `FakeVideoProvider` / `FakeTtsProvider` trả file có sẵn; MinIO chạy trong container; **FFmpeg là thật** (bước compose là chỗ hay hỏng nhất, mock nó là mất ý nghĩa test).

- [ ] **Pipeline đầy đủ với provider giả** — brief → MP4, qua cả 9 bước, không gọi ra ngoài
- [ ] **Thứ tự 4→5→6 không đảo** — test khẳng định TTS hoàn tất trước khi có lệnh render đầu tiên
- [ ] **Fan-out song song** — format `render_mode: parallel`, 6 shot, xác nhận chạy đồng thời
- [ ] **Nối frame tuần tự** — format `sequential_chained`, xác nhận đoạn 3 **chờ** đoạn 2 xong; frame cuối đoạn 2 là frame đầu đoạn 3
- [ ] **Ảnh tham chiếu truyền lại ở mỗi đoạn** của daily (chống trôi hình)
- [ ] **Shot lỗi → retry cùng provider, đổi seed** — xác nhận provider key không đổi
- [ ] **Provider chết hẳn → fail cả job**, không vá bằng model khác (Luật 1)
- [ ] **Provider timeout / 429** → Polly retry đúng số lần rồi mở circuit breaker
- [ ] **Regenerate shot 3** → chỉ shot 3 render lại, storyboard và các shot khác giữ nguyên, `ProviderCall` ghi thêm đúng một dòng
- [ ] **Reframe** → tái dùng storyboard + voice-over, **không gọi LLM, không gọi TTS**, chỉ render lại hình
- [ ] **Huỷ job giữa chừng** → không phát sinh lời gọi provider mới sau lệnh huỷ
- [ ] **Job dừng ở `awaiting_approval`** → không có lời gọi TTS nào cho tới khi duyệt
- [ ] **`PATCH` storyboard rồi duyệt** → lời thoại mới là lời được TTS đọc
- [ ] **Ingest từ chối sớm** — ảnh hỏng, ảnh sai định dạng, ảnh quá lớn → lỗi ở bước 1, không tốn gì
- [ ] **MinIO: upload → presigned URL tải về được**, hạn 15 phút
- [ ] **Presigned URL hết hạn** → 403, và thông báo lỗi nói rõ là hết hạn (không phải "không tìm thấy")
- [ ] **Lifecycle rule** — file trong `adv-work` có tag vòng đời đúng
- [ ] **Hai tenant chạy song song** → file không lẫn đường dẫn, job không lẫn trạng thái
- [ ] **API endpoint tests** — mọi endpoint ở bảng thiết kế: mã trạng thái, shape response, lỗi 4xx có thông điệp đọc được
- [ ] **`X-AdVideo-Key` sai / thiếu** → `401`
- [ ] **Chi phí tổng hợp** — sau một job đầy đủ, tổng `ProviderCall.CostUsd` khớp số ghi trên `AdVideoJob`

### Kiểm chất lượng đầu ra bằng `ffprobe` (tự động)

Chạy trên mọi MP4 do integration test sinh ra:

- [ ] Thời lượng file khớp thời lượng dự kiến (±100 ms)
- [ ] Không có khung đen ở đầu/cuối
- [ ] Audio không clip (peak < 0 dBFS)
- [ ] Loudness ≈ **−14 LUFS** (±1 LU)
- [ ] Có track phụ đề hoặc phụ đề burn-in khi `captions.enabled = true`
- [ ] File `.srt` sinh ra có timestamp khớp `CharTiming` từ TTS
- [ ] Metadata nhãn AI có mặt

### Tầng integration đã dựng (T1.12, 2026-09-21)

12 test trong `AdVideo.Tests/Integration/PipelineTests.cs`, chạy trọn pipeline Sprint 1
(bước 1 → 4 → 5 → 6 → 8 → 9) trong tiến trình test, khoảng 28 giây cho cả lớp.

```bash
# Tầng unit — thuần logic, ~50 ms
dotnet test AdVideo/tests/AdVideo.Core.Tests/AdVideo.Core.Tests.csproj

# Tầng integration — cần FFmpeg thật trên máy
dotnet test AdVideo/tests/AdVideo.Tests/AdVideo.Tests.csproj --logger "console;verbosity=normal"
```

Ba chỗ **lệch so với kế hoạch gốc**, đều là lệch có chủ ý:

| Kế hoạch | Thực tế | Lý do |
|---|---|---|
| MinIO chạy trong container | `LocalDiskStorageService` trong thư mục tạm | Bộ test phụ thuộc một container đang chạy là bộ test sẽ bị tắt đi. Đường đi qua `IStorageService` là một, nên chỗ hỏng thật vẫn được phủ. |
| DB thật | Sqlite trên file, dựng schema bằng `EnsureCreated` | Migration sinh cho SQL Server nên Sqlite không chạy được. Đổi lại: **tầng này KHÔNG kiểm migration** — việc đó thuộc một lần chạy thật trên SQL Server. Chọn Sqlite chứ không phải InMemory vì InMemory không áp unique index. |
| FFmpeg thật | **FFmpeg thật** | Giữ nguyên. Phần lớn lỗi bước compose là lỗi cú pháp filter graph. |

FFmpeg và font được dò theo thứ tự: biến môi trường → `PATH` → các thư mục quen thuộc của
hệ điều hành. Không dò ra thì test **ném lỗi chứ không tự bỏ qua** — một test tự skip là một
test không bao giờ chạy trên CI mà không ai biết. Hai đường thoát khi máy để ở chỗ khác:

```bash
export ADVIDEO_TEST_FFMPEG_DIR=/opt/ffmpeg/bin   # chứa cả ffmpeg và ffprobe
export ADVIDEO_TEST_FONTFILE=/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf
```

Đã phủ:

- [x] **Pipeline đầy đủ với provider giả** — brief → MP4, `ffprobe` đọc được: h264, 1080×1920,
      có audio, thời lượng khớp timeline đã khoá, loudness đo được, metadata nhãn AI có mặt
- [x] **D3 — tắt tiếng model** — clip thô không có audio track (kiểm cả cờ trong DB lẫn file
      thật), video thành phẩm thì có
- [x] **Job đã xong bị Hangfire giao lại** → không chạy lại, không một lời gọi provider nào
- [x] **Khách huỷ giữa chừng** → dừng ở ranh giới bước kế tiếp, đọc lại trạng thái từ DB
- [x] **Brief thiếu `voice.script`** → fail ở bước 1, trước mọi lời gọi
- [x] **Ảnh sản phẩm `file://`** → từ chối trước khi gửi request nào
- [x] **Ảnh sản phẩm 404** → fail ngay, không thử lại
- [x] **Chạm trần chi tiêu** → dừng trước khi thu giọng đọc
- [x] **TTS trả audio nhưng không có mốc thời gian** → không khoá timeline, không ghi shot nào
- [x] **Shot bị kiểm duyệt từ chối** → fail ngay, đúng một lời gọi video
- [x] **Lỗi tạm thời ở shot đầu** → thử lại rồi chạy tiếp; 2 lời gọi, `AttemptNumber` 1 và 2
- [x] **Sáu bước đăng ký đủ và không trùng số thứ tự** — thứ tự chạy lấy từ `IPipelineStep.Order`,
      không từ thứ tự đăng ký DI

Mọi HttpClient gọi ra provider thật đều gắn `NoNetworkHandler`: một lời gọi lọt ra ngoài làm
test đỏ kèm lý do, chứ không xanh kèm hoá đơn. `FakeProviderOptions` là thứ duy nhất trả video.

Chưa phủ ở tầng này (còn ở danh sách trên): fan-out song song, nối frame tuần tự, regenerate,
reframe, presigned URL hết hạn, hai tenant song song, endpoint API, circuit breaker.

## End-to-End Tests

Bốn hành trình, chạy qua UI thật trong NewsCMS (Playwright) với backend dùng provider giả:

- [ ] **Hành trình 1 — Video sản phẩm cơ bản.** Đăng nhập → VideoStudio → chọn ảnh từ Media → gõ brief → chọn tier Chuẩn → tạo job → thấy từng shot hiện dần → tải MP4 về.
- [ ] **Hành trình 2 — Có duyệt storyboard.** Tạo job với `require_storyboard_approval` → job dừng → sửa lời thoại một shot → duyệt → nhận video có đúng lời đã sửa.
- [ ] **Hành trình 3 — Không ưng một shot.** Video xong → bấm render lại shot 3 → nhận video mới chỉ khác ở shot 3.
- [ ] **Hành trình 4 — Xuất thêm tỉ lệ.** Video 9:16 xong → bấm xuất 16:9 → nhận video mới **cùng lời thoại**, khác khung hình.
- [ ] **Regression kề cạnh** — module Media, Builder, Posts vẫn chạy bình thường sau khi thêm permission mới và sửa `DbSeeder`.
- [ ] **Phân quyền** — tài khoản không có `Marketing.AdVideo.Create` không thấy nút tạo; gọi thẳng URL → `403`.

## Test Data

### Fixtures

| Fixture | Nội dung | Dùng cho |
|---|---|---|
| `fixtures/images/product-*.jpg` | 3 ảnh sản phẩm thật (đã xin phép), nền sạch | Ingest, render |
| `fixtures/images/talent-*.jpg` | 2 ảnh người, có `ConsentRecord` giả lập | Luật 2, R5 |
| `fixtures/images/broken.jpg` | File hỏng cố ý | Ingest từ chối sớm |
| `fixtures/audio/vo-4800ms.mp3` + `.timings.json` | Audio 4,8 giây + `CharTiming` thật | Khoá timeline (dùng đúng câu 4,8 giây trong D6) |
| `fixtures/video/shot-*.mp4` | 6 clip ngắn cho `FakeVideoProvider` trả về | Pipeline, compose |
| `fixtures/video/shot-with-speech.mp4` | Clip có tiếng người | Test VAD bắt được |
| `fixtures/formats/*.json` | 8 `AdFormat` của danh mục khởi điểm | Kho prompt |
| `fixtures/scenes/*.json` | 5 `SceneEntry` có `avoid` và `verified_on` | Locale pack |
| `fixtures/providers/*.json` | Response mẫu của cả 4 provider, gồm cả lỗi 429/500 | Adapter |

### Seed data

- 2 tenant (`tenant-a`, `tenant-b`) để test cách ly.
- 3 `VoiceProfile`: 1 chung, 1 của A, 1 của B.
- 1 `BrandKit` đầy đủ (logo, font tiếng Việt, màu, bumper).
- 1 `PronunciationDictionary` với tên thương hiệu cố tình khó đọc.

### Database test

`AdVideoDb` riêng cho test, tạo mới mỗi lần chạy từ migration (không snapshot). Nhất quán với `NewsCMS.Tests`.

> **Bẫy đã gặp trong repo này:** test đọc file source qua đường dẫn tương đối tính từ `bin` sẽ vỡ nếu `-p:ArtifactsPath` đổi độ sâu thư mục. Nếu viết test kiểu đó, tính đường dẫn từ một mốc ổn định thay vì đếm số cấp cha.

## Test Reporting & Coverage

```bash
# Chạy toàn bộ (không gọi ra ngoài, không tốn tiền)
dotnet test AdVideo/AdVideo.sln

# Kèm độ phủ
dotnet test AdVideo/AdVideo.sln --collect:"XPlat Code Coverage"

# Chỉ AdVideo.Core (nơi đặt mục tiêu 100%)
dotnet test AdVideo/tests/AdVideo.Core.Tests/AdVideo.Core.Tests.csproj
```

- Ngưỡng CI: **mọi lớp CÓ LOGIC trong `AdVideo.Core` phải đạt 100% line**. Dưới ngưỡng thì fail build.
- `AdVideo.Infrastructure` và `AdVideo.Api`: không đặt ngưỡng số, nhưng mọi endpoint phải có ít nhất một integration test.
- Khoảng trống độ phủ phải được ghi lý do trong bảng dưới đây khi phát sinh — không để trống im lặng.

### Ngưỡng đọc thế nào (chốt sau T1.12, 2026-09-21)

Con số tổng của coverlet cho `AdVideo.Core` là **78,2% line / 99,3% branch**, và con số đó
**không** phải thước đo dùng để gác cổng. Lý do: `AdVideo.Core` chứa cả entity EF và record DTO,
mà coverlet tính mỗi `{ get; init; }` là một dòng. Muốn đẩy tổng lên 100% thì phải viết test đọc
từng thuộc tính của `AdVideoJob`, `Shot`, `MediaAsset`… — loại test không bao giờ đỏ vì một lỗi
thật, nhưng vẫn phải sửa mỗi lần đổi schema. Đó là chi phí bảo trì đổi lấy một con số.

Thước đo thật: **21/21 lớp có logic đạt 100% line**, gồm `TimelineLocker` (187 dòng),
`ProviderCapabilityValidator` (109), `QualityChecker` (86), `AiLabelStamper` (57),
`NativeSoundTranslator` (39), `CostEstimator` (42), `IdempotencyGuard`, `ApiKeyHasher`,
`Buckets`, `AspectRatioExtensions`, `JobStatusExtensions`, `StepResult`, `QcReport`.

| File / hàm dưới 100% | Lý do |
|---|---|
| `Entities/*` (AdVideoJob, Shot, MediaAsset, ProviderCall, ProviderCredential, Tenant, SystemSetting, PromptTemplate, AdVideoProject), `Common/BaseEntity` | Auto-property thuần, không có nhánh. EF Core đọc/ghi chúng; hành vi thật được phủ ở integration test của `AdVideo.Tests`, không phải bằng test gán-rồi-đọc. |
| `Providers/IVideoProvider.cs` — `VideoRequest`, `VideoResult` (trừ `CanRetry`); `ITtsProvider.cs` — `TtsRequest`, `TtsResult` (trừ `CanLockTimeline`) | DTO qua ranh giới adapter. Phần CÓ logic (`CanRetry`, `CanLockTimeline`) đã phủ 100%; phần còn lại là thuộc tính. |
| `Pipeline/IPipelineStep.cs` — thuộc tính của `PipelineContext` | Túi trạng thái mutable do worker ghi. `IsCostCeilingHit` — nhánh duy nhất — đã phủ đủ 4 ca. |
| `Configuration/Stores.cs` — getter của `ResolvedCredential` | Phần có logic (`MaskedKey`, `ToString`) đã phủ 100%, gồm cả ca key ngắn hơn 4 ký tự. |
| `ProviderCapabilityValidator.cs:171` — nhánh `cost = null` | **Không chạy được.** `BuildSuggestions` chỉ thêm gợi ý sau khi `Check` trả `IsSatisfied`, mà `IsSatisfied` luôn kèm `RecommendedShotDurationSeconds` khác null. Giữ lại làm lưới an toàn nếu `Check` đổi về sau. |
| `AiLabelStamper.cs:156` — nhánh của `Any(...)` trong `Validate` | Cả hai vế của `||` đều có test (khớp theo tên trường, khớp theo giá trị, và không khớp vế nào). Coverlet đếm thiếu một nhánh của máy trạng thái `Any`. |

## Manual Testing

### Nghe và nhìn — thứ không tự động hoá được

Đây là phần **quan trọng nhất** của sản phẩm này và không có test tự động nào thay thế được. Mỗi sprint có một buổi duyệt cảm quan.

- [ ] **Không lệch tiếng** — xem toàn bộ video, tai nghe, để ý các mốc cắt shot
- [ ] **Lời thoại nghe như người nói**, không như sáu người khác nhau đọc sáu câu rời (kiểm `previous_request_ids` có tác dụng)
- [ ] **Tên thương hiệu đọc đúng** — đây là thứ khách để ý đầu tiên
- [ ] **Không có chữ do model vẽ** trong khung hình
- [ ] **Bối cảnh trông Việt Nam thật** — không phải bếp Mỹ có thêm cái nón lá
- [ ] **Nhân vật trông người Việt**, không phải "người châu Á chung chung"
- [ ] **Với format daily:** sản phẩm không lộ ra là quảng cáo trong 10 giây đầu; marketer chấm "có lướt qua không?"
- [ ] **Chuyển cảnh không giật**, tiếng nền không nhảy tại chỗ cắt (crossfade 250 ms)
- [ ] **Nhãn AI đọc được** nhưng không che mất nội dung

### Nghe mù — giọng đọc (Sprint 0, lặp lại khi đổi TTS)

Cùng một kịch bản có tên thương hiệu tiếng Việt, ElevenLabs vs VieNeu-TTS, **5 người làm marketing chấm mà không biết đâu là bản nào**. Đây là cách duy nhất vì VieNeu không công bố điểm MOS.

### Chấm điểm provider (Sprint 0)

Cùng một prompt, cùng bộ ảnh sản phẩm **thật của một khách hàng thật**, chạy qua Veo / Kling / Seedance / Vidu, rồi để chính người làm marketing chấm. **Mọi bảng giá trên mạng đều vô nghĩa nếu output không đạt gu khách hàng Việt Nam.**

### UI trong NewsCMS

- [ ] Từng shot hiện dần trong lúc chờ (`job.shot_completed`) — không phải spinner câm
- [ ] Giá ước tính hiện **trên nút** trước khi bấm
- [ ] Chọn định dạng qua **video ví dụ thật**, không qua mô tả chữ
- [ ] Nghe thử giọng trước khi chọn
- [ ] Thông báo lỗi đọc được bằng tiếng Việt (ví dụ "Veo không nhận ảnh có mặt người — thử Kling")
- [ ] Accessibility: nhãn form, focus ring, tương phản chữ
- [ ] Chạy được trên màn hình hẹp

### Smoke test sau deploy

- [ ] `GET /v1/providers` trả về đủ 4 provider, tất cả `healthy`
- [ ] Tạo một job tier Nháp thật → có MP4 trong < 2 phút
- [ ] Presigned URL tải về được từ ngoài mạng nội bộ
- [ ] Webhook đến được NewsCMS và chữ ký verify đúng

## Performance Testing

### Đo, không đoán

- [ ] **Rate limit thật của từng provider** — đo bằng cách tăng dần số request song song cho tới khi bị chặn. **Đây là việc của Sprint 0 và là con số quyết định kiến trúc scale.**
- [ ] Thời gian một job 30 giây, 6 shot, fan-out song song — mục tiêu **2–5 phút**
- [ ] Thời gian một job daily 40 giây, nối frame tuần tự — mục tiêu **8–15 phút**, và khách phải được báo trước con số này
- [ ] Tier Nháp — mục tiêu **< 2 phút**
- [ ] Thời gian FFmpeg compose riêng — mục tiêu **< 60 giây** cho video 30 giây
- [ ] **VieNeu-TTS RTF trên phần cứng thật** — công bố 0,37–0,62 trên CPU desktop; đo lại trên VPS
- [ ] Số job song song tối đa trước khi worker nghẽn CPU (FFmpeg + VieNeu tranh nhau — **chạy trên worker tách biệt**)

### Load & stress

- [ ] 20 job cùng lúc từ 3 tenant → không job nào fail vì tranh tài nguyên nội bộ
- [ ] Provider trả 429 hàng loạt → circuit breaker mở, job xếp hàng thay vì fail dồn
- [ ] Storage đầy / MinIO không phản hồi → job fail sạch sẽ với thông điệp đúng, không để file rác

## Bug Tracking

### Mức độ

| Mức | Nghĩa | Ví dụ | Xử lý |
|---|---|---|---|
| **P0** | Mất tiền hoặc vi phạm pháp lý | Vòng retry loạn tiêu tiền; video giao đi **thiếu nhãn AI**; giọng tenant A lộ sang B | Dừng deploy, sửa ngay |
| **P1** | Video giao đi không dùng được | Lệch tiếng > 200 ms; chữ do model vẽ; audio clip | Sửa trước khi đóng sprint |
| **P2** | Đúng nhưng khó chịu | Thời gian chờ vượt mục tiêu; thông báo lỗi khó hiểu | Xếp vào sprint sau |
| **P3** | Thẩm mỹ / tiện nghi | Bố cục UI, chữ nghĩa | Ghi nhận |

### Regression

Mỗi bug P0/P1 **phải kèm một test tự động** trước khi đóng. Bug về cảm quan (lệch tiếng, trôi hình) mà không viết được test tự động thì thêm một mục vào checklist duyệt cảm quan của sprint.

### Danh sách bẫy đã biết — kiểm mỗi lần release

1. **Vidu bật audio mặc định** trong khi D3 nói tắt → dễ quên nhất trong cả hệ.
2. **Video Veo chỉ sống 2 ngày trên server Google** → worker chậm tải về là mất file.
3. **Lệch giờ máy MinIO** làm presigned URL hết hạn sớm — triệu chứng giống hệt "sai access key".
4. **CORS MinIO thiếu origin NewsCMS** → trình duyệt chặn *không có lỗi rõ ràng*.
5. **Nối frame quá 4–5 lần** → màu và khuôn mặt trôi. Giới hạn 40–60 giây.
6. **Một shot Veo ghép cùng một shot Seedance** → nhìn ra ngay hai đoạn dán lại (Luật 1).
7. **ElevenLabs Default voices hết hạn 31/12/2026** → mọi `voice_id` nhóm đó sẽ chết.
