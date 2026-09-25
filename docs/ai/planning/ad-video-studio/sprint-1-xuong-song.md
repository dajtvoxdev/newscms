---
phase: planning
sprint: 1
title: "Sprint 1 — Xương sống (Tuần 2–3)"
description: API + worker + DB + storage, một provider video, hai adapter TTS, một shot 8 giây, compose tối thiểu — curl ra MP4 có giọng đọc
---

# Sprint 1 — Xương sống

> Kế hoạch tổng: [../2026-09-15-feature-ad-video-studio.md](../2026-09-15-feature-ad-video-studio.md)
> Thiết kế: [../../design/2026-09-15-feature-ad-video-studio.md](../../design/2026-09-15-feature-ad-video-studio.md)
> Kiểm thử: [../../testing/2026-09-15-feature-ad-video-studio.md](../../testing/2026-09-15-feature-ad-video-studio.md)

> **15/09/2026 — Sprint này khởi động khi Sprint 0 chưa chạy** (chưa có API key và ngân sách spike). Phần lớn Sprint 1 không cần key: solution, entity, API, worker, FFmpeg, test — tất cả chạy được với `FakeVideoProvider`/`FakeTtsProvider`. Phần **cần key bị hoãn có đánh dấu** (🔑). Bù lại, mọi con số lẽ ra học được từ Sprint 0 (rate limit, model id, giới hạn provider) đều đặt trong **cấu hình DB** theo quyết định D10 — khi có số liệu thật thì sửa một dòng, không deploy lại.

## Mục tiêu

Một cú `curl` gửi ảnh sản phẩm + một câu lời thoại, trả về **MP4 8 giây có giọng đọc tiếng Việt và nhãn AI**.

Không có LLM đạo diễn. Không có nhiều shot. Không có định dạng. Người gọi tự viết lời thoại. Mục tiêu là **đường ống thông suốt từ đầu tới cuối**, dù mỗi đoạn đều ở dạng tối giản nhất.

Ba thứ **phải làm đúng ngay từ commit đầu**, vì sửa sau rất đắt:
- `IVideoProvider` / `ITtsProvider` — trừu tượng hoá provider
- `ProviderCall` — ghi nhận mọi lần gọi ra ngoài, có đô la
- Nhãn AI trong bước compose, **không phải tuỳ chọn**

## Definition of Done

**Đạt được ngay, không cần key:**

- [ ] `AdVideo.sln` build sạch, `dotnet test` xanh
- [ ] `POST /v1/ad-videos` nhận job, trả `202` + `job_id`
- [ ] `GET /v1/ad-videos/{id}` trả trạng thái theo đúng vòng đời
- [ ] Hangfire chạy job qua 9 bước (bỏ qua bước 2 đạo diễn, 3 duyệt storyboard, 7 lip-sync ở sprint này — chạy 1 → 4 → 5 → 6 → 8 → 9) với **provider giả**
- [ ] File cuối nằm trên MinIO, tải về bằng presigned URL
- [ ] Video ghép từ clip mẫu có giọng đọc tiếng Việt (file audio fixture), khớp độ dài shot
- [ ] **Nhãn AI hiện trên video và có trong metadata** — không có cờ tắt
- [ ] Key provider, cấu hình vận hành, prompt **đọc từ DB** (`ProviderCredential` / `SystemSetting` / `PromptTemplate`), key mã hoá tại chỗ
- [ ] `Idempotency-Key` bắt buộc; gửi trùng trả về job cũ, không tạo job mới
- [ ] Hai adapter TTS (ElevenLabs + VieNeu) **viết xong, unit test với HTTP giả**
- [ ] `AdVideo.Core` đạt **100% line + branch coverage**
- [ ] `adv-work` có lifecycle rule xoá sau 7 ngày

**Hoãn tới khi có API key (🔑):**

- [ ] 🔧 Chạy thật đầu-cuối với provider thật: `curl` → MP4 8 giây có giọng đọc thật
- [ ] 🔧 Mỗi lần gọi provider thật sinh `ProviderCall` có `cost_usd` thật, đối chiếu được hoá đơn
- [ ] 🔧 Xác nhận model id của fal còn hiệu lực; cập nhật `ProviderCredential` theo kết quả
- [ ] 🔧 Đo `MaxConcurrentShots` thật và cập nhật `SystemSetting` (hiện đặt mặc định bảo toàn = 1)

## Điều kiện vào

| Cần có | Từ đâu | Trạng thái 15/09 |
|---|---|---|
| Kết luận provider mặc định | `spike/results/KET-LUAN.md` (Sprint 0) | ⏸ **Chưa có** — viết cả adapter theo tài liệu, chọn mặc định qua `SystemSetting`, đổi sau không cần deploy |
| Số liệu rate limit | `spike/results/ratelimit.md` | ⏸ **Chưa có** — đặt `MaxConcurrentShots = 1` (bảo toàn nhất); có số liệu thì sửa một dòng DB |
| Kết luận VieNeu (cửa timing) | `spike/results/vieneu-verdict.md` | 🟡 Cửa license **đã tra xong** (Apache-2.0, dùng thương mại được — chỉ tới v2-Turbo); cửa timing chưa — adapter đặt `HasWordTimings = false` cho tới khi đo |
| **API key thật** (Gemini, fal, ElevenLabs) | Tài khoản có thanh toán | ⏸ **Chưa có** — mọi verification chạy bằng Fake providers; phần 🔑 trong DoD hoãn |
| MinIO đã dựng, có endpoint + key | Hạ tầng | Dev dùng MinIO trong docker-compose |
| SQL Server, quyền tạo database `AdVideoDb` | VPS đã có SQL Server 2022 Express | ✅ |
| FFmpeg trên máy dev và trong image worker | `ffmpeg -version` chạy được | ✅ (dev máy này đã có) |
| Font tiếng Việt có bản quyền thương mại | Cho overlay nhãn AI | Cần trước khi compose bản thật |

> **Quy tắc khi thiết kế mà thiếu số liệu Sprint 0:** không đoán rồi hard-code. Mọi con số chưa biết (giới hạn đồng thời, model id, bậc thời lượng, hệ số giá) đi vào `SystemSetting` / `ProviderCredential` / `ProviderCapability` trong DB với **giá trị bảo toàn**. Sprint 0 chạy sau sẽ điền số thật — đó chính là lý do D10 tồn tại.

## Task Breakdown

### T1.1 — Dựng solution `AdVideo/`

- [ ] `dotnet new sln -n AdVideo` trong `AdVideo/`
- [ ] 4 project + 1 test project, target `net8.0`
- [ ] `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`
- [ ] Chiều phụ thuộc: `Api` → `Core` + `Infrastructure`; `Worker` → `Core` + `Infrastructure`; `Infrastructure` → `Core`; **`Core` không tham chiếu ai**
- [ ] `.editorconfig` sao chép quy ước từ `NewsCMS.Core/src`

**Files tạo:**
```
AdVideo/AdVideo.sln
AdVideo/Directory.Build.props
AdVideo/.editorconfig
AdVideo/src/AdVideo.Api/AdVideo.Api.csproj
AdVideo/src/AdVideo.Worker/AdVideo.Worker.csproj
AdVideo/src/AdVideo.Core/AdVideo.Core.csproj
AdVideo/src/AdVideo.Infrastructure/AdVideo.Infrastructure.csproj
AdVideo/tests/AdVideo.Tests/AdVideo.Tests.csproj
```

> `AdVideo.Core` **không tham chiếu EF Core, không tham chiếu HttpClient**. Đây là lý do nó test được 100% mà không cần mock nặng. Nếu thấy mình muốn thêm package vào `Core`, gần như chắc chắn là logic đặt sai chỗ.

### T1.2 — Entity + DbContext + migration đầu tiên

- [ ] Sao chép quy ước `BaseEntity` / `AuditableEntity` / `ISoftDelete` từ `NewsCMS.Domain/Common/BaseEntity.cs` sang `AdVideo.Core/Common/`
- [ ] 8 entity của Sprint 1: `AdVideoProject`, `AdVideoJob`, `Shot`, `MediaAsset`, `ProviderCall` + **`ProviderCredential`, `SystemSetting`, `PromptTemplate`** (D10)
- [ ] Mỗi entity một `IEntityTypeConfiguration<T>` riêng — theo đúng cách NewsCMS làm
- [ ] `EncryptedApiKey` của `ProviderCredential` mã hoá qua `ValueConverter` + `IDataProtector` — trong DB chỉ có ciphertext
- [ ] `AdVideoDbContext` trong `AdVideo.Infrastructure/Persistence/`
- [ ] Migration đầu: `dotnet ef migrations add InitialCreate`
- [ ] Seed `SystemSetting` với giá trị bảo toàn: `MaxConcurrentShots=1`, `DefaultProvider`, timeout, trần chi tiêu
- [ ] Seed `PromptTemplate`: negative prompt toàn cục ("no text, no letters, no watermark…")
- [ ] Index: `AdVideoJob(TenantId, Status)`, `AdVideoJob(IdempotencyKey)` **unique**, `ProviderCall(JobId)`, `Shot(JobId, Index)`, `ProviderCredential(Provider, Scope)`

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Common/BaseEntity.cs
AdVideo/src/AdVideo.Core/Entities/{AdVideoProject,AdVideoJob,Shot,MediaAsset,ProviderCall}.cs
AdVideo/src/AdVideo.Core/Entities/{ProviderCredential,SystemSetting,PromptTemplate}.cs
AdVideo/src/AdVideo.Core/Enums/{JobStatus,ShotStatus,AssetKind,ProviderKind}.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/AdVideoDbContext.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/Configurations/*.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/EncryptedStringConverter.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/Seed/SystemSettingSeeder.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/Migrations/*
```

> `ProviderCall` vào từ sprint này, **không phải Sprint 5**. Không có nó thì tới lúc đối chiếu hoá đơn sẽ không biết tiền đi đâu — và dữ liệu quá khứ thì không dựng lại được.
>
> Đồng thời: **đừng thêm cột billing tạm** vào `AdVideoJob` kiểu `EstimatedCost` để "sau này chuyển sang credit". Hoặc ghi chi phí thật vào `ProviderCall`, hoặc không ghi gì.

### T1.3 — Trừu tượng hoá provider

- [ ] `IVideoProvider`: `GenerateAsync(VideoRequest, CancellationToken) → VideoResult`
- [ ] `ITtsProvider`: `SynthesizeAsync(TtsRequest, CancellationToken) → TtsResult` (có mốc thời gian theo từ)
- [ ] `ProviderCapability` — manifest mô tả provider làm được gì: có nhận ảnh người không, sinh audio gốc không, các mức thời lượng cho phép, tỉ lệ khung hình hỗ trợ, có nối frame không
- [ ] `IProviderRegistry` — tra provider theo tên, trả về capability
- [ ] `ProviderCapabilityValidator` trong `Core` — so yêu cầu với capability, **trả về lý do đọc được**, không phải mã lỗi

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Providers/IVideoProvider.cs
AdVideo/src/AdVideo.Core/Providers/ITtsProvider.cs
AdVideo/src/AdVideo.Core/Providers/ProviderCapability.cs
AdVideo/src/AdVideo.Core/Providers/IProviderRegistry.cs
AdVideo/src/AdVideo.Core/Providers/ProviderCapabilityValidator.cs
```

> **Luật 2 của dự án: lớp API phải chuẩn hoá, không phải chuyển tiếp.** Người gọi yêu cầu video 10 giây mà provider chỉ chạy bậc 4/6/8 giây thì lỗi trả về phải nói *"provider này chỉ hỗ trợ 4, 6 hoặc 8 giây"* — không phải ném nguyên lỗi JSON của nhà cung cấp ra ngoài.

### T1.3b — Cấu hình, key và prompt đọc từ DB (D10)

- [ ] `ISettingsStore` trong `Core`: `GetAsync<T>(key, fallback)`, `SetAsync` — đọc `SystemSetting` có **cache + invalidation** (đổi setting không cần restart)
- [ ] `ICredentialStore`: lấy `ProviderCredential` theo `(provider, scope)`; trả về key **đã giải mã trong bộ nhớ**, không bao giờ log
- [ ] `IPromptStore`: lấy `PromptTemplate` active theo code, có version
- [ ] Mã hoá: ASP.NET Core Data Protection; master key qua env (`ADVIDEO_DP_KEY_PATH` hoặc key ring mặc định), **connection string của `AdVideoDb` cũng ở env** — hai thứ này không thể bootstrap từ chính DB
- [ ] Adapter (T1.4, T1.5) lấy model id / endpoint / key từ `ICredentialStore`, **không đọc `appsettings.json`**
- [ ] Unit test: mã hoá → giải mã round-trip; key sai → exception rõ ràng; cache invalidation khi đổi setting
- [ ] Script/endpoint admin tối thiểu để nạp key lần đầu (seed CLI `dotnet run -- set-credential veo <key>` là đủ — UI quản lý để Sprint 4/5)

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Configuration/{ISettingsStore,ICredentialStore,IPromptStore}.cs
AdVideo/src/AdVideo.Infrastructure/Configuration/{DbSettingsStore,DbCredentialStore,DbPromptStore}.cs
AdVideo/src/AdVideo.Infrastructure/Configuration/ProtectionSetup.cs
```

> Vì sao đặt đây mà không đợi Sprint 5: **Sprint 1 đang chạy trước Sprint 0.** Mọi con số chưa được kiểm chứng (model id của fal, giới hạn đồng thời, bậc thời lượng) mà hard-code vào config file thì mỗi lần Sprint 0 sửa lại là một lần deploy. Nằm trong DB thì sửa là xong. Chi phí: một lớp cache + mã hoá, làm một lần.
>
> Ranh giới cần nhớ: `spike/.env` (file khoá của script spike) là chuyện của `spike/` — code vứt đi, giữ nguyên. D10 áp cho **service thật** trong `AdVideo/`.

### T1.4 — Adapter provider video (một cái; mặc định chọn qua `SystemSetting`, chốt lại sau Sprint 0 🔑)

- [ ] Adapter cho provider mặc định tạm thời (đề xuất: **Kling qua fal.ai** — một key cho nhiều model, dễ đổi sang Veo/Seedance/Vidu bằng cách sửa `ProviderCredential`, không viết lại adapter). **Chốt chính thức khi Sprint 0 chạy** 🔑
- [ ] Polly: retry cho lỗi mạng tạm thời, **không retry lỗi nội dung bị từ chối**
- [ ] **Tải file về MinIO ngay khi render xong** — không lưu URL của nhà cung cấp
- [ ] Ghi `ProviderCall`: provider, model, request id, thời gian, đô la, thành công/thất bại
- [ ] Nếu provider là Vidu: **tắt tường minh âm thanh gốc**
- [ ] Negative prompt toàn cục **"no text, no letters, no watermark"** lấy từ `PromptTemplate`, không hard-code trong adapter
- [ ] 🔑 Khi có key: chạy thật một video 8 giây, xác nhận model id còn hiệu lực và chi phí khớp `ProviderCall`

**Files tạo:**
```
AdVideo/src/AdVideo.Infrastructure/Providers/Video/{Tên}VideoProvider.cs
AdVideo/src/AdVideo.Infrastructure/Providers/Video/ProviderRegistry.cs
AdVideo/src/AdVideo.Infrastructure/Http/ResiliencePolicies.cs
```

> **Luật 1: provider chết thì job chết.** Không tự đổi sang provider khác giữa chừng — hai model khác nhau cho ra hai phong cách hình khác nhau, ghép vào một video sẽ lộ rõ. Retry cùng provider với seed khác; hết retry thì fail và mời khách render lại.

### T1.5 — Hai adapter TTS

- [ ] `ElevenLabsTtsProvider` — dùng endpoint **`/with-timestamps`**, lấy mốc thời gian theo ký tự
- [ ] `VieNeuTtsProvider` — cửa license đã đạt (Apache-2.0 tới v2-Turbo); **cửa timing chưa đo** nên đặt `Capability.HasWordTimings = false` cho tới khi chạy `--gate-check` (T0.8) 🔑
- [ ] Chọn provider theo tier: Nháp → VieNeu (nếu dùng được), Thành phẩm → ElevenLabs
- [ ] Ghi `ProviderCall` cho cả TTS, không chỉ video
- [ ] Cả hai trả về cùng một `TtsResult` — audio + mốc thời gian + độ dài thật

**Files tạo:**
```
AdVideo/src/AdVideo.Infrastructure/Providers/Tts/ElevenLabsTtsProvider.cs
AdVideo/src/AdVideo.Infrastructure/Providers/Tts/VieNeuTtsProvider.cs
AdVideo/src/AdVideo.Infrastructure/Providers/Tts/TtsProviderSelector.cs
```

> Hai adapter ngay từ sprint đầu **không phải để dự phòng** — mà để ép giao diện `ITtsProvider` đủ tổng quát. Một adapter duy nhất thì giao diện sẽ vô tình bám sát hình dạng của nhà cung cấp đó, và cái thứ hai sẽ đòi refactor.
>
> Lưu ý về thời hạn: giọng Default của ElevenLabs **ngừng hoạt động 31/12/2026**. Chỉ dùng voice id từ Voice Library hoặc giọng tự clone, đừng dùng giọng mặc định cho dù nó tiện.

### T1.6 — Storage MinIO

- [ ] `IStorageService`: upload, presigned URL tải về, xoá
- [ ] AWS SDK for .NET với **`ForcePathStyle = true`** (bắt buộc với MinIO)
- [ ] 4 bucket: `adv-uploads`, `adv-work`, `adv-final`, `adv-voice`
- [ ] Lifecycle: `adv-work` xoá sau **7 ngày**
- [ ] Presigned URL hạn 1 giờ cho tải về
- [ ] Đường dẫn đối tượng: `{tenant}/{jobId}/{kind}/{filename}`

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Storage/IStorageService.cs
AdVideo/src/AdVideo.Infrastructure/Storage/MinioStorageService.cs
AdVideo/src/AdVideo.Infrastructure/Storage/StorageOptions.cs
```

> Hai bẫy MinIO đã biết trước:
> - **Lệch giờ máy chủ** làm presigned URL fail với lỗi nói về chữ ký, không nói về đồng hồ. Bật NTP trên máy MinIO.
> - **Thiếu cấu hình CORS** làm trình duyệt tải/ghi file thất bại *im lặng* — F12 mới thấy. Cấu hình CORS ngay ở sprint này, đừng đợi tới Sprint 4 khi có UI.

### T1.7 — API: nhận job

- [ ] `POST /v1/ad-videos` — Minimal API, validate, tạo job, đẩy vào Hangfire, trả `202`
- [ ] **`Idempotency-Key` bắt buộc** — thiếu thì `400`, trùng thì trả job cũ
- [ ] Xác thực bằng header `X-AdVideo-Key`
- [ ] `GET /v1/ad-videos/{id}` — trạng thái + tiến độ + link tải khi xong
- [ ] `GET /healthz` — kiểm tra DB, MinIO, Hangfire
- [ ] Model request theo đúng hợp đồng JSON trong tài liệu thiết kế (sprint này chỉ dùng phần tối thiểu)
- [ ] Trường hợp lỗi trả `ProblemDetails` có lý do đọc được

**Files tạo:**
```
AdVideo/src/AdVideo.Api/Program.cs
AdVideo/src/AdVideo.Api/Endpoints/AdVideoEndpoints.cs
AdVideo/src/AdVideo.Api/Contracts/{CreateAdVideoRequest,AdVideoJobResponse}.cs
AdVideo/src/AdVideo.Api/Auth/ApiKeyAuthHandler.cs
AdVideo/src/AdVideo.Api/appsettings.json
```

> Theo D10, **khoá API provider nằm trong DB (`ProviderCredential`), mã hoá tại chỗ** — không trong `appsettings.json`, không trong env. Env chỉ còn connection string + master key Data Protection. (Repo này có tiền lệ để secret thật trong `appsettings.json` của NewsCMS — đó là lựa chọn có chủ đích cho hệ thống nội bộ; khoá của bên thứ ba tính tiền theo lần gọi thì khác hẳn, và DB mã hoá còn mở đường cho BYO-key theo tenant ở Sprint 5.)

### T1.8 — Worker: khung pipeline 9 bước

- [ ] Hangfire với storage SQL Server (`AdVideoDb`, schema riêng)
- [ ] `AdVideoJobRunner` chạy tuần tự các bước, cập nhật trạng thái sau mỗi bước
- [ ] Sprint 1 chạy các bước: **1** (nạp) → **4** (TTS) → **5** (khoá timeline) → **6** (render shot) → **8** (compose) → **9** (QC)
- [ ] Bước **2** (LLM đạo diễn), **3** (duyệt storyboard), **7** (lip-sync) **bỏ qua** ở sprint này
- [ ] Trạng thái job theo đúng sơ đồ vòng đời trong tài liệu thiết kế
- [ ] Ghi log có `job_id` trong mọi dòng

**Files tạo:**
```
AdVideo/src/AdVideo.Worker/Program.cs
AdVideo/src/AdVideo.Worker/Jobs/AdVideoJobRunner.cs
AdVideo/src/AdVideo.Worker/Steps/{IngestStep,TtsStep,LockTimelineStep,RenderShotsStep,ComposeStep,QcStep}.cs
```

> Thứ tự **4 → 5 → 6 không đảo được**: có audio trước, rồi mới khoá timeline theo audio, rồi mới render hình theo timeline. Làm ngược lại thì hình dài 8 giây mà tiếng dài 9,3 giây, và không có cách sửa nào không xấu.

### T1.9 — `TimelineLocker` (phần logic quan trọng nhất của sprint)

- [ ] Nhận độ dài audio thật + bậc thời lượng provider cho phép → ra kế hoạch shot
- [ ] Làm tròn lên bậc gần nhất (audio 4,8s trên lưới 4/6/8 → shot 6s)
- [ ] Cắt lời thoại dài thành nhiều shot, **cắt ở chỗ im lặng**, không cắt giữa từ
- [ ] Tích luỹ sai lệch qua nhiều shot phải **< 200 ms**
- [ ] Nằm hoàn toàn trong `AdVideo.Core`, không gọi ra ngoài → test được 100%

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Timeline/TimelineLocker.cs
AdVideo/src/AdVideo.Core/Timeline/{TimelinePlan,ShotPlan,WordTiming}.cs
AdVideo/tests/AdVideo.Tests/Timeline/TimelineLockerTests.cs
```

> Đây là chỗ lệch tiếng-hình sinh ra. Mọi thứ khác hỏng thì nhìn thấy ngay; cái này hỏng thì video "cảm giác sai sai" mà không ai chỉ ra được vì sao.

### T1.10 — Compose bằng FFmpeg

- [ ] Ghép shot + voice-over thành MP4
- [ ] **Nhãn AI**: chữ chồng lên video, font tiếng Việt, đọc được ở cả khung dọc và ngang
- [ ] **Metadata**: ghi thông tin sinh bằng AI vào metadata file
- [ ] Chuẩn âm lượng **−14 LUFS**
- [ ] Sprint 1 làm bản tối thiểu: chưa cần nhạc nền, chưa cần ducking, chưa cần crossfade
- [ ] Output: H.264, MP4, khung hình theo yêu cầu

**Files tạo:**
```
AdVideo/src/AdVideo.Infrastructure/Media/FfmpegComposer.cs
AdVideo/src/AdVideo.Infrastructure/Media/FfmpegCommandBuilder.cs
AdVideo/src/AdVideo.Core/Media/AiLabelStamper.cs
```

> Nhãn AI **không có cờ tắt**, kể cả cho quản trị viên. Nghĩa vụ pháp lý (Luật TTNT 2025, hiệu lực 1/3/2026, phạt tới 2 tỉ đồng) thuộc về bên vận hành nền tảng và không chuyển sang khách bằng điều khoản được. Một cờ tắt là một cách để ai đó vô tình vi phạm.

### T1.11 — QC tự động, bản tối thiểu

- [ ] Chạy `ffprobe` kiểm tra: có video stream, có audio stream, đúng thời lượng (±0,5s), đúng độ phân giải
- [ ] Kiểm tra LUFS trong khoảng cho phép
- [ ] **Kiểm tra nhãn AI có trong metadata** — thiếu thì fail job, không cảnh báo suông
- [ ] Fail QC → job `failed`, có lý do đọc được

**Files tạo:** `AdVideo/src/AdVideo.Worker/Steps/QcStep.cs`, `AdVideo/src/AdVideo.Infrastructure/Media/FfprobeInspector.cs`

### T1.12 — Test

- [ ] Unit: `TimelineLocker` (9 ca theo tài liệu kiểm thử), `ProviderCapabilityValidator`, `AiLabelStamper`, `IdempotencyGuard`
- [ ] `FakeVideoProvider` + `FakeTtsProvider` trả file mẫu cố định
- [ ] Integration: job chạy hết pipeline với provider giả + MinIO container + **FFmpeg thật**
- [ ] `ffprobe` kiểm tra output của test integration
- [ ] CI fail nếu `AdVideo.Core` dưới 100% line/branch

**Files tạo:**
```
AdVideo/tests/AdVideo.Tests/Fakes/{FakeVideoProvider,FakeTtsProvider}.cs
AdVideo/tests/AdVideo.Tests/Integration/PipelineTests.cs
AdVideo/tests/AdVideo.Tests/Fixtures/*
```

> FFmpeg **thật** trong test integration, không giả lập. Phần lớn lỗi ở bước compose là lỗi cú pháp filter graph — giả lập FFmpeg thì test xanh mà production đỏ.
>
> Bẫy đã gặp trong repo này: test đọc file nguồn qua đường dẫn tương đối từ `bin` sẽ vỡ khi build với `-p:ArtifactsPath` vì độ sâu thư mục khác. Nếu viết test kiểu đó, tính đường dẫn từ biến môi trường hoặc đánh dấu bỏ qua khi không tìm thấy.

### T1.13 — Đóng gói & chạy được

- [ ] `Dockerfile` cho Api và Worker, **image worker có FFmpeg**
- [ ] `docker-compose.yml` cho môi trường dev: api, worker, sqlserver, minio
- [ ] `AdVideo/README.md`: cách chạy, biến môi trường, cách gọi thử
- [ ] Script `curl` mẫu trong README

**Files tạo:** `AdVideo/Dockerfile.Api`, `AdVideo/Dockerfile.Worker`, `AdVideo/docker-compose.yml`, `AdVideo/README.md`

## Kiểm chứng

```powershell
# Build và test
dotnet build AdVideo/AdVideo.sln
dotnet test AdVideo/AdVideo.sln --collect:"XPlat Code Coverage"

# Coverage của Core phải 100%
dotnet test AdVideo/tests/AdVideo.Tests --filter "FullyQualifiedName~Core" --collect:"XPlat Code Coverage"

# Migration áp được
dotnet ef database update --project AdVideo/src/AdVideo.Infrastructure --startup-project AdVideo/src/AdVideo.Api

# Chạy thử đầu-cuối VỚI FAKE PROVIDER (không cần key)
docker compose -f AdVideo/docker-compose.yml up -d
curl -X POST http://localhost:5080/v1/ad-videos `
  -H "X-AdVideo-Key: dev-key" `
  -H "Idempotency-Key: test-001" `
  -H "Content-Type: application/json" `
  -d '@AdVideo/samples/minimal-request.json'
# → 202 + job_id, đợi, rồi GET trạng thái tới khi completed

# 🔑 HOÃN TỚI KHI CÓ KEY: bật provider thật (đổi SystemSetting + nạp ProviderCredential)
#    rồi chạy lại đúng lệnh curl trên — kỳ vọng MP4 do provider thật render
```

**Kiểm tra thủ công bắt buộc — mở file MP4 ra xem** (bản fake: clip fixture + audio fixture ghép bằng FFmpeg **thật**, nên nhãn AI, LUFS, thời lượng đều là kiểm tra thật):
- [ ] Tiếng và hình khớp, không lệch rõ rệt
- [ ] **Nhãn AI đọc được** ở cả khung dọc và ngang
- [ ] `ffprobe` cho thấy metadata AI có mặt
- [ ] 🔑 Khi có key: giọng đọc **thật** nghe được, đúng tiếng Việt, không méo; không có chữ lạ do model tự vẽ; không có hai lớp tiếng

**Kiểm tra dữ liệu:**
```sql
-- Mọi setting đọc từ DB, không từ appsettings
SELECT [Key], [Value] FROM SystemSettings;
-- Key provider chỉ có ciphertext
SELECT Provider, LEFT(EncryptedApiKey, 20) FROM ProviderCredentials;

-- 🔑 Khi chạy provider thật:
SELECT Provider, Model, CostUsd, DurationMs, Success FROM ProviderCalls WHERE JobId = '<id>';
-- Phải có đủ dòng: 1 TTS + n video, mỗi dòng có CostUsd
```

## Rủi ro trong sprint

| Rủi ro | Dấu hiệu | Phản ứng |
|---|---|---|
| **FFmpeg ngốn thời gian hơn dự tính** | Hết tuần 2 vẫn chưa compose ra file xem được | Bỏ nhạc nền, bỏ ducking, bỏ crossfade — chỉ giữ ghép + nhãn + LUFS. Phần còn lại sang Sprint 2 |
| Presigned URL của MinIO fail | Lỗi nói về chữ ký | Kiểm tra NTP trước tiên, không debug code SDK |
| Font tiếng Việt vỡ dấu khi burn-in | Dấu thanh thành ô vuông | Thử font khác; kiểm tra `fontconfig` có trong image worker |
| Provider từ chối ảnh đầu vào | `422` từ nhà cung cấp | Đã lường: `ProviderCapabilityValidator` bắt trước khi gọi. Bổ sung ca mới vào validator |
| Rate limit thấp hơn giả định | Bị `429` khi chạy 2 shot | Số liệu này có từ Sprint 0 — hạ `MaxConcurrentShots` trong cấu hình |
| Hangfire + SQL Server Express chậm | Job chờ lâu ở hàng đợi | Giảm `WorkerCount`; SQL Express giới hạn 1 GB RAM buffer |
| Sa vào việc tối ưu sớm | Bắt đầu tinh chỉnh prompt để video đẹp hơn | **Sprint này không quan tâm video đẹp.** Đẹp là việc của Sprint 2 |
| **Thiết kế trên phỏng đoán vì thiếu số liệu Sprint 0** | Rate limit, model id, bậc thời lượng đều là giá trị bảo toàn chưa kiểm chứng | Đã lường bằng D10: mọi con số nằm trong `SystemSetting`/`ProviderCredential` — Sprint 0 chạy sau thì sửa DB, không sửa code. **Không** hard-code "tạm" vào config file |
| **Adapter viết theo tài liệu, chưa từng gọi thật** | Khi có key, request fail vì tài liệu lạc hậu hoặc model id đổi | Giữ nguyên Fake provider làm fallback; 🔑 verification là việc đầu tiên khi có key, trước khi Sprint 2 bắt đầu. Đây cũng là lý do chạy Sprint 0 càng sớm càng tốt dù Sprint 1 đã đi trước |

## Ra khỏi sprint

**Không cần key:**
- `AdVideo/` — solution build được, test xanh, Core 100% coverage
- Một MP4 8 giây ghép bằng FFmpeg thật từ fixture (clip + audio mẫu), **có nhãn AI** — chứng minh bước compose đúng
- Cấu hình/key/prompt đọc từ DB; đổi setting không cần restart
- `docker-compose` chạy được toàn bộ trên máy dev với Fake providers

**🔑 Khi có key (chạy Sprint 0 song song hoặc ngay sau):**
- MP4 8 giây do provider **thật** render, giọng đọc tiếng Việt thật
- `ProviderCalls` có dữ liệu chi phí thật, đối chiếu hoá đơn
- `KET-LUAN.md` của Sprint 0 → cập nhật `SystemSetting` (`DefaultProvider`, `MaxConcurrentShots`) và `ProviderCredential` theo kết quả

## Chuyển sang Sprint 2

Sprint 2 thêm LLM đạo diễn ở **bước 2** và kho định dạng. Xương sống không đổi hình dạng — chỉ có bước 2 chuyển từ "người gọi tự viết lời thoại" thành "LLM sinh storyboard".

**Điều kiện cứng trước khi vào Sprint 2: các mục 🔑 phải xong** — Sprint 2 render video thật nhiều shot, không thể làm bằng Fake provider mãi. Nếu tới lúc đó vẫn chưa có key thì dừng lại chờ, không đi tiếp trên nền giả lập.
