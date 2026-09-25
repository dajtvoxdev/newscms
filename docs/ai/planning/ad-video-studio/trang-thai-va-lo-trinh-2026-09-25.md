---
phase: planning
title: "AdVideo — Trạng thái thật ngày 25/09/2026 và lộ trình tiếp"
description: Kiểm kê code so với 7 sprint plan, năm lỗ hổng chặn đường, và năm đợt triển khai tiếp theo xếp theo thứ tự mở khoá
---

# Trạng thái thật và lộ trình tiếp

> Kế hoạch tổng: [../2026-09-15-feature-ad-video-studio.md](../2026-09-15-feature-ad-video-studio.md)

Tài liệu này **không** thay các file sprint. Nó trả lời hai câu: *đang ở đâu thật* và *làm gì tiếp*.
Nguồn sự thật là code, không phải checkbox — mọi checkbox trong repo này đều để trống kể cả việc đã
xong, nên đọc plan một mình sẽ ra kết luận sai ở cả hai chiều.

## 1. Đang ở đâu

| Sprint | Thực tế | Ghi chú |
|---|---|---|
| 0 — Spike provider | **~15%** | 7 script + 4 khung kết quả đã viết, chưa chạy lần nào. Chỉ cửa license VieNeu có kết luận thật (Apache-2.0 tới v2-Turbo). `FAL_KEY` **đã có** trong `spike/.env` từ 16/09 nhưng chưa dùng |
| 1 — Xương sống | **~80%** | Xương sống chạy thông thật: 243 test xanh, 12 test integration đi hết 1→4→5→6→8→9 với FFmpeg thật, ra MP4 h264 có nhãn AI trong hình lẫn metadata. Còn 5 lỗ ở mục 2 |
| 2 — Đạo diễn & kho prompt | **~10%** | T2.9 xong và đang chạy; T2.8 gần xong (4 provider video đã nối, thêm provider = `set-credential`, không phải viết code). Phần còn lại là số không, nhưng điểm nối đặt đúng chỗ |
| 3 — Daily & locale pack | **~5%** | Chưa có `FrameChain`, `DriftGuard`, `SceneEntry`. Một nửa mẹo chống trôi đã có ngoài ý muốn (ảnh gốc được gửi lại ở mọi shot) |
| 4 — Mặt tiền NewsCMS | **0%** | `grep 'AdVideo\|VideoStudio'` trong NewsCMS = 0 dòng. Nặng hơn: AdVideo thiếu **≥ 8 endpoint** mà UI cần — Sprint 4 không chỉ thiếu UI |
| 5 — Vận hành nhiều khách | **~15%** | Chỉ multi-tenant chạy thật (filter cưỡng chế, fail-closed, worker ghim tenant của job). 9 hạng mục còn lại là `Guid?` trong DTO trỏ vào bảng chưa có |
| 6 — Pilot | **0%** | Chặn kép: pháp chế chưa chốt nhãn AI, và không có `FormatUsageStat` nên chỉ số quan trọng nhất của pilot (tỉ lệ render lại) chưa đo được |

### Sprint 1 làm được thật những gì

Không phải "gần xong" — những thứ này chạy và có test giữ:

- Pipeline 6 bước qua Hangfire, `ffprobe` xác nhận đầu ra 1080×1920 h264, có audio, đúng thời lượng.
- **Nhãn AI không có nút tắt** (D9): `FfmpegCommandBuilder` khai `required AiLabelSpec Label`, burn-in
  + metadata, QC fail nếu thiếu.
- **D10 đọc lúc chạy thật** ở 10 chỗ trong pipeline — không phải class chết. CLI nạp key, sửa setting,
  tạo tenant, rotate key.
- **Hàng rào tiền** (D8): trần mỗi job **và** trần toàn hệ thống áp *trước* khi ghi DB và trước khi
  enqueue; `AccumulatedCostUsd` bắt đầu từ `ActualCostUsd` chứ không từ 0; Hangfire `Attempts = 0`.
- `Idempotency-Key` đúng ba nhánh (trả job cũ / 409 / 400) với unique index làm trọng tài.
- Bảo vệ key: DataProtection singleton + `SetApplicationName` cố định + key ring dùng chung; API key
  tenant chỉ lưu SHA-256, so sánh chống timing attack; `RawProviderError` không rò ra API.
- `TimelineLocker` có test đúng hai phía ngưỡng 200 ms.
- Làm **thêm** ngoài plan: `Tenant` + `ApiKeyHasher` + CLI, `CostEstimator`, `NativeSoundTranslator`,
  `QualityChecker`, trần chi tiêu theo ngày, `LocalDiskStorageService`, `CONFIGURATION.md` (289 dòng).

## 2. Năm lỗ hổng chặn đường

Xếp theo mức chặn, không theo kích cỡ.

### L1 — Không có một migration EF nào. Đây là thứ chặn mọi thứ khác.

Không có thư mục `Migrations/`, `grep 'MigrationBuilder|ModelSnapshot'` = 0 dòng. Trong khi đó
`Api/Program.cs:170`, `Cli/AdminCommands.cs:352` và `README.md:103` đều gọi/hướng dẫn `migrate` như
thể nó làm việc. **`dotnet run -- migrate` hiện tạo đúng một bảng `__EFMigrationsHistory` rồi in
"Xong."**

Hệ quả nối tiếp, đúng thứ tự: `docker compose up` **không thể** thành công dù Docker có chạy —
`docker-compose.yml:68` đặt `ASPNETCORE_ENVIRONMENT=Development` cho service api → `Program.cs:166-172`
migrate rồi seed ngay → `SystemSettingSeeder.cs:41-44` truy vấn bảng `SystemSettings` chưa tồn tại →
api chết lúc khởi động → worker `depends_on: api: service_healthy` cũng không bao giờ lên.

Vì sao không ai thấy: 12 test integration dùng **SQLite + `EnsureCreatedAsync`**
(`PipelineTestHost.cs:131,154`), vòng qua migration hoàn toàn. Build xanh không chứng minh schema
dựng được trên SQL Server, nên mọi lỗi chỉ có ở SQL Server (decimal precision, filtered unique index,
collation) chưa từng được phát hiện.

### L2 — Nửa lượng code sẽ chạy lần đầu vào đúng lúc tốn tiền

0 test cho: 4 adapter provider thật (1017 dòng), toàn bộ `AdVideo.Api` (endpoint 361 dòng, validator
245 dòng, `ApiKeyAuthHandler` 134 dòng, CLI 338 dòng), `ProviderFailureMapper`, `PollyPolicies`,
`ProviderJson`, `MinioStorageService`.

Hai chỗ đau nhất: `ProviderFailureMapper` (Luật 2) và `PollyPolicies.ShouldRetry` quyết định **có trả
tiền lần nữa hay không**, đều là hàm tĩnh, viết test được trong một buổi. Và `MinioStorageService` là
lớp lưu trữ của production trong khi 100% test chạy `LocalDisk` — lớp được test và lớp chạy thật là
hai lớp khác nhau.

Việc này **không cần API key**: khuôn stub `HttpMessageHandler` đã có sẵn trong
`tests/AdVideo.Tests/Infrastructure/StubHttpHandlers.cs`.

### L3 — Đường tiền được test bằng số 0, nên phép kiểm rỗng

Cả hai provider giả báo `ReportedCostUsd = 0m`. Chỗ cộng dồn duy nhất là `JobArtifacts.cs:148`
(`job.ActualCostUsd += costUsd`). Assert duy nhất là `job.ActualCostUsd.Should().Be(0m)`.
**Xoá hẳn dòng cộng dồn thì cả 12 test vẫn xanh.** Test trần chi phí duy nhất đặt trần = 0 nên nó nảy
trước khi phát sinh đồng nào — phép so `ActualCostUsd` với `MaxCostUsd` bằng giá trị khác 0 chưa bao
giờ chạy. Chính comment `JobArtifacts.cs:18` đã cảnh báo đúng lỗ này. Sửa rẻ: cho `FakeProviderOptions`
trả cost ≠ 0.

### L4 — Ba bẫy vận hành đã được cảnh báo trong plan mà chưa làm

- **`adv-work` không có lifecycle rule 7 ngày.** `IStorageService.cs:73` ghi "tạo bucket **kèm
  lifecycle rule**"; `MinioStorageService.cs:173-192` chỉ `PutBucketAsync`. Mỗi job đẻ 5–10 file
  trung gian không ai xem (R8).
- **MinIO chưa cấu hình CORS** ở bất kỳ đâu, dù T1.6 dặn làm ngay ở Sprint 1.
- **Không có `appsettings.Production.json`.** Thiếu `FontFile` thì `FfmpegOptions.Validate()` chặn
  worker khởi động, còn thiếu `KeyRingPath` thì key ring rơi vào `~/.aspnet` của user service — đúng
  bài học đã mất một lần ở NewsCMS.

### L5 — Hai đường tiêu tiền / rò key ngoài ý muốn

- **`options.provider` cho khách tự ép provider, bỏ qua kiểm capability.**
  `CreateAdVideoRequestValidator.cs:232` nhận nguyên chuỗi, chỉ `Trim`; `AdVideoEndpoints.cs:124-136`
  nhánh ép chỉ gọi `FindVideoProviderAsync(forced)` còn `ProviderCapabilityValidator.Check` chỉ chạy
  ở nhánh `SelectVideoProviderAsync`. Một tenant ép được provider không đáp ứng tier/tỉ lệ khung/có
  người — và vì `FakeProviderOptions.Enabled` **mặc định `true` trong code**
  (`FakeProviderOptions.cs:31`) còn `ProviderNames.Fake = "fake"`, trên host cấu hình sai khách ép
  được luôn provider giả. Trái cả Luật 3 lẫn ghi chú của Sprint 4 ("form không có ô chọn provider").
- **`spike/.env` chứa `FAL_KEY` thật và KHÔNG được gitignore.** Root `.gitignore` có `*.key` và
  `secrets.json` nhưng không có dòng nào khớp `.env`, và `spike/` không có `.gitignore` riêng.
  `git status` đang để `?? spike/`. Repo này đã phải rewrite lịch sử một lần để gỡ secret.

## 3. Mười chỗ plan và code lệch nhau

Ghi lại để không lập kế hoạch sai:

1. **Bước 2, 3, 7 không có class nào**, trái comment `IPipelineStep.cs:114-118` ("giữ step đã viết
   nhưng tắt bằng cờ thì Sprint 2 chỉ việc bật lên"). Sprint 2/5 phải viết mới, không phải bật cờ.
2. **`MaxConcurrentShots` là setting vô tác dụng**: `RenderShotsStep.cs:143-153` log cảnh báo rồi
   `foreach` tuần tự. T2.6 là "viết phần song song", không phải "chỉnh con số".
3. **Ngưỡng lệch 200 ms là mã chết**: nó chỉ tích giá trị khác 0 khi `Layout = ContinuousAudio`, mặc
   định là `PadPerShot` và `LockTimelineStep` không truyền `Layout`. Gạch "sai lệch qua 5 đoạn < 200 ms"
   của Sprint 3 hiện không được bảo đảm bởi dòng code nào đang chạy.
4. **T2.8 gần xong**: `ProviderRegistry.cs:306-317` đã nối 4 provider video, manifest đầy đủ. Thêm
   provider thứ hai = `set-credential` + đổi setting.
5. **Nội dung nhãn AI là hằng số trong code**: `AiLabelStamper.cs:42-44` nói đọc từ
   `PromptTemplate`/`SystemSetting`, nhưng `ComposeStep.cs:147` gọi `Build(ratio, duration)` không
   truyền override.
6. **`brief.format` và `assets.scene_reference` bị bỏ im lặng**: DTO nhận, validator/`JobBrief` không
   đọc. Client gửi hôm nay không nhận lỗi và cũng không có tác dụng gì.
7. **4 endpoint mà tài liệu thiết kế xếp vào Sprint 1 chưa có**: `POST {id}/cancel`, `GET {id}/assets`,
   `GET /v1/providers`, `GET /v1/voices`. Riêng **cancel đã có đủ logic** ở
   `AdVideoJobRunner.cs:243-253` và **có test integration** — chỉ thiếu cửa để khách bấm.
8. **QC yếu hơn vẻ ngoài**: `QcStep.cs:94` tắt cứng `detectBlackFrames`; `resolution` và `loudness`
   đặt `IsBlocking: false`; phép kiểm quét chữ có sẵn và là fail cứng nhưng **không có bộ đo** đổ dữ
   liệu vào.
9. **"Giọng đọc tiếng Việt từ fixture" thực tế là nốt sine 220 Hz** (`FakeTtsProvider.cs:107`), và
   thư mục `tests/AdVideo.Tests/Fixtures` mà csproj khai **không tồn tại**.
10. **Không có CI nào biết AdVideo tồn tại** (`grep 'AdVideo'` trong `.github/workflows/` = 0), và
    không có ngưỡng coverage nào được ép, dù `Directory.Build.props` có comment nói ngược lại. Nên
    "CI fail nếu Core dưới 100%" hiện là câu chữ, và coverage 78.19% không làm gì đỏ.

Thêm: thiếu hẳn **circuit breaker** (`PollyPolicies` chỉ có retry), **webhook + HMAC**, **phụ đề .srt**,
**thumbnail** — bốn thứ tài liệu testing/design đòi mà code chưa có chỗ cắm.

## 4. Lộ trình: năm đợt

Thứ tự này không theo số sprint mà theo **cái gì mở khoá cái gì**.

### Đợt A — Làm cho nó chạy được thật · 2–3 ngày · không cần key

Mở khoá mọi thứ phía sau. Không có A thì không deploy được, không seed được, không chạy Sprint 0 được
(kết luận spike phải ghi vào DB).

| # | Việc | Kiểm chứng |
|---|---|---|
| A1 | `dotnet ef migrations add InitialCreate` + áp lên SQL Server thật | `dotnet ef database update` xong, `SELECT` được 9 bảng; kiểm decimal precision + filtered unique index của `IdempotencyKey` |
| A2 | `docker compose up -d --build` chạy trọn stack | 4 service healthy, `create-tenant` → curl POST → GET tới `completed` → tải `download_url` về xem được |
| A3 | Hai kiểm tra thủ công của Sprint 1 | Mở MP4: nhãn AI đọc được ở **cả khung dọc và ngang**; `ffprobe` thấy metadata AI |
| A4 | Thêm `.env` + `spike/output/*` vào `.gitignore`, rồi **commit `AdVideo/`** | `git check-ignore spike/.env` trả về hit; `git log` có commit đầu của AdVideo |
| A5 | `appsettings.Production.json`: `FfmpegPath`, `FfprobePath`, `FontFile`, `KeyRingPath` | Khớp `CONFIGURATION.md:279-289` |
| A6 | Lifecycle `adv-work` 7 ngày + `adv-final` 90 ngày + CORS cho MinIO | `mc ilm ls` thấy rule; upload từ browser không bị CORS |
| A7 | Bịt L5: `FakeProviderOptions.Enabled` mặc định `false` + throw nếu Production mà bật; `options.provider` chạy qua allowlist + `ProviderCapabilityValidator`, không cho ép `"fake"` | Test: ép provider không đáp ứng tỉ lệ khung → 422; ép `"fake"` ở Production → từ chối |

A4 làm **trước** mọi commit khác. Hiện `AdVideo/` chưa được commit lần nào, và `spike/` nằm cùng cây —
một `git add .` là đưa key fal vào lịch sử.

### Đợt B — Trả nợ test cho nửa code chưa từng chạy · 4–5 ngày · không cần key

Làm trước Đợt C. Lý do: C là lần đầu tiêu tiền thật, và lúc đó mỗi vòng debug là một hoá đơn. Test
adapter bằng HTTP giả trước thì lúc có key chỉ còn sai những thứ mà tài liệu provider nói sai.

| # | Việc | Ghi chú |
|---|---|---|
| B1 | 4 adapter với `HttpMessageHandler` giả: hình dạng request, parse response, map 429/422/5xx → `FailureKind` | Khuôn stub đã có; đây là L2 |
| B2 | `ProviderFailureMapper` + `PollyPolicies.ShouldRetry` + `ProviderJson` | Hàm tĩnh, một buổi |
| B3 | Endpoint API bằng `WebApplicationFactory` | `public partial class Program` và `Mvc.Testing` **đã có sẵn**, chưa ai dùng. Phủ 202/400/409/422/503 + `ApiKeyAuthHandler` + 28 nhánh validator |
| B4 | Test cách ly tenant **chiều phủ định** (tenant B không thấy dữ liệu tenant A) + vòng mã hoá/giải mã credential + `Invalidate()` cache | Filter có chạy cưỡng chế trong 12 test hiện tại, nhưng chiều "không thấy được" chưa có phép kiểm nào |
| B5 | Sửa L3: fake provider báo cost ≠ 0, assert tổng `ProviderCall.CostUsd` khớp `ActualCostUsd`, và một test trần chi phí với trần khác 0 | Không sửa thì D8 vẫn là phép kiểm rỗng |
| B6 | `TestBriefs` serialize DTO thật thay vì gõ tay JSON snake_case | Hiện hợp đồng API↔Worker bị **nhân bản**, không được kiểm: đổi tên một property thì 12 test vẫn xanh mà production fail ở bước 1 |
| B7 | CI workflow cho AdVideo + ngưỡng coverage ép thật | Kèm: hạ mốc "100% line" xuống **"100% trên code có logic"** và ghi lý do — phần thiếu hiện là property accessor của entity/DTO, ép 100% bằng test gọi getter là test giả |
| B8 | `MinioStorageService` có test (Testcontainers hoặc MinIO cục bộ) | Nếu hoãn thì phải ghi rõ: lớp storage production chưa từng được kiểm |

### Đợt C — Chạy Sprint 0 phần fal · nửa ngày làm + chờ render · ~$20–40

`FAL_KEY` đã có. Đây là lần đầu code gọi provider thật, và nó trả nợ đúng ba mục 🔑 của Sprint 1.

| # | Việc | Ghi chú |
|---|---|---|
| C1 | `run_fal.py` 3 kịch bản × Kling/Seedance/Vidu + `run_vidu_subjects.py` | Cần **ảnh sản phẩm thật** — không thay bằng ảnh stock |
| C2 | `measure_ratelimit.py` tắt dry-run → điền `ratelimit.md` | Đây là số liệu mà Sprint 1 đang phải đoán (`MaxConcurrentShots = 1`) |
| C3 | Điền `daily-cost.md` bằng hoá đơn thật, đối chiếu `ProviderCall.CostUsd` | Đúng mục 🔑 "cost_usd đối chiếu được hoá đơn" |
| C4 | Cập nhật `SystemSetting` + `ProviderCredential` theo kết quả (D10: sửa DB, không sửa code) | `DefaultVideoProvider` đang là `"fake"` |
| C5 | Xác nhận model id của fal còn hiệu lực; ghi nguyên văn lỗi khi provider từ chối ảnh | `AcceptsHumanFaces=false` của Veo hiện là suy từ tài liệu |
| C6 | 5 người marketing chấm điểm → `scorecard.html` + `KET-LUAN.md` | Chặn bởi người, không bởi code |

**Còn chặn sau C:** Veo (chưa có `GEMINI_API_KEY`) và **toàn bộ TTS thật** (chưa có ElevenLabs; VieNeu
chưa self-host nên Cửa 1 — có timing ký tự hay không — vẫn chưa trả lời được). Hai đường đi:
mua ElevenLabs gói thấp, hoặc dựng VieNeu trên VPS để đóng cửa 1. Không có đường thứ ba: TTS là bước 4,
không bỏ qua được.

### Đợt D — Sprint 2 phần không cần key · ~70% khối lượng Sprint 2

Cổng cứng của plan ("không vào Sprint 2 khi các mục 🔑 chưa xong") nên đọc lại thành: **không ĐÓNG
Sprint 2 bằng provider giả — nhưng mở được ngay.** Nó chặn đúng bốn thứ: chấm chất lượng 4 định dạng,
so sánh định dạng, đo drift nối frame, và 3 video daily + 5 người chấm. Phần còn lại là code + dữ liệu
thuần, kiểm chứng được bằng `FakeProviders` (provider giả sinh clip **thật** bằng ffmpeg nên `ffprobe`
vẫn đo được LUFS, độ dài, nhãn AI).

| # | Việc | Ghi chú |
|---|---|---|
| D1 | `AdFormat` là dữ liệu nạp lúc chạy (entity + store + seeder + endpoint) | Bắt khuôn `DbSettingsStore`/`DbPromptStore` + `StoreCacheSignal` đã có. Plan đã cảnh báo: biến từ hằng số sang dữ liệu sau là việc đau đớn. **Đừng đặt tên `ShotPlan`** — tên đã bị timeline chiếm |
| D2 | `BannedPhraseFilter` + `StoryboardValidator` | Thuần Core, test 100% được. Áp **ngay** lên `voice.script` trước TTS — chặn R3 ("số 1 Việt Nam") trước cả khi có LLM |
| D3 | T2.6 render song song thật (semaphore theo `MaxConcurrentShots` + `DbContext` riêng mỗi shot) | Hiện setting đã đọc mà không có tác dụng |
| D4 | T2.10 crossfade 250 ms + ducking −12 dB + sfx −18 dB trong `FfmpegCommandBuilder`, **và bật lại `detectBlackFrames`** | `QcStep.cs:93` cố ý tắt "vì chưa có crossfade"; thêm crossfade mà quên bật là mất phép kiểm |
| D5 | Sửa hợp đồng pipeline để có trạng thái **dừng chờ người**: `StepResult` hiện chỉ có Ok/Fail/Retry; thêm cột `StoryboardJson`; endpoint approve + **cancel** + **regenerate** | Cancel đã có logic + test, chỉ thiếu endpoint. `RegenerateCount` đã có cột, chưa ai ghi |
| D6 | Lớp đạo diễn LLM (bước 2): prompt template trong DB, DTO, structured output, test bằng LLM giả | Chỉ cần **key LLM** — khác loại và rẻ hơn key video rất nhiều |
| D7 | 4 file `samples/brief-*.json` mà mục Kiểm chứng của Sprint 2 đòi | Hiện `samples/` chỉ có `minimal-request.json` |

### Đợt E — Sprint 3 → 6, với bốn điều chỉnh bắt buộc

1. **Sprint 3 phải sửa `Layout` trước** khi nói "trải lời thoại đều 40 giây": ngưỡng 200 ms hiện là mã
   chết với `PadPerShot`. Truyền `PresetSegments` là đúng nhưng **không đủ**.
2. **Chèn một đợt "API cho mặt tiền" trước Sprint 4.** Sprint 4 tự nhận "không đụng tới AdVideo ngoài
   webhook" — sai nặng: thiếu ≥ 8 endpoint (formats, list, shots, approve, regenerate, delete, upload,
   webhook), **chưa có đường upload ảnh** (brief chỉ nhận URL http), và **chưa sinh thumbnail** ở đâu
   dù bước 9 tự nhận có. Không chèn thì Sprint 4 vừa làm UI vừa làm API và trượt chắc.
3. **Sprint 5: `TenantQuota` lên sớm.** Hiện chỉ có trần **toàn hệ thống** 20 USD/ngày — với pilot
   nhiều khách, một khách tiêu hết là mọi khách nhận 503. Và `Shot` **không có `TenantId`** (cố ý, ghi
   ở `AdVideoDbContext.cs:106`) nên phải bù bằng test cross-tenant chứ không bằng filter.
4. **Sprint 6 chặn pháp chế.** Việc cần đặt lịch ngay, không phải việc code: cho pháp chế đọc Luật
   TTNT 2025 và chốt hình thức nhãn. Kèm `FormatUsageStat` ở D-hoặc-E, nếu không pilot không đo được
   tỉ lệ render lại — chỉ số quan trọng nhất của Sprint 6.

Thêm vào backlog, đều là "tài liệu đòi mà code không có chỗ cắm": circuit breaker, webhook + HMAC
(3 mục test đã viết sẵn trong plan), phụ đề `.srt`, bộ đo chữ lọt lưới cho QC, phát hiện **đứng hình**
(Sprint 5 DoD đòi "khung đen **hoặc đứng hình**", phần sau chưa có chỗ cắm).

## 5. Đường tới găng

```
A (migration + compose chạy)
├── B (test nửa code chưa chạy) ──┐
├── C (Sprint 0 fal, cần tiền) ───┼── D (Sprint 2 không cần key)
└── A5/A6 (production config)     │
                                  └── E (Sprint 3→6)
Chặn ngoài code, nên khởi động song song NGAY:
  · key Gemini + ElevenLabs (hoặc VPS cho VieNeu) — chặn TTS thật, chặn đóng Sprint 2
  · ảnh sản phẩm thật của một khách thật — chặn C
  · 5 người marketing chấm điểm — chặn C6
  · pháp chế chốt nhãn AI — chặn Sprint 6
  · font tiếng Việt có bản quyền thương mại — hiện đang dùng arial.ttf / DejaVu
```

**A rồi B là phần không ai chặn được ngoài mình.** Bốn dòng cuối là việc của người khác — gửi đi ngay
hôm nay thì chúng chạy song song với A và B thay vì nối tiếp sau.
