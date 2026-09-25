---
phase: planning
sprint: 2
title: "Sprint 2 — Đạo diễn & kho prompt (Tuần 4–5)"
description: LLM sinh storyboard, AdFormat nạp lúc chạy, 4 định dạng song song, render lại từng shot, provider thứ hai
---

# Sprint 2 — Đạo diễn & kho prompt

> Kế hoạch tổng: [../2026-09-15-feature-ad-video-studio.md](../2026-09-15-feature-ad-video-studio.md)
> Thiết kế: [../../design/2026-09-15-feature-ad-video-studio.md](../../design/2026-09-15-feature-ad-video-studio.md)

## Mục tiêu

Khách gửi **mô tả sản phẩm bằng tiếng Việt thường**, chọn một định dạng, nhận về **video 30 giây nhiều cảnh** có lời thoại do AI viết, khớp nhịp.

Đây là sprint biến "đường ống chạy được" thành "sản phẩm có giá trị". Cũng là sprint **dễ trượt tiến độ nhất** vì prompt engineering không có điểm dừng tự nhiên.

**Chốt cứng phạm vi: 4 định dạng chạy được là đóng sprint.** Chất lượng lời thoại tinh chỉnh ở Sprint 6 với dữ liệu thật, không tinh chỉnh bằng cảm tính ở đây.

## Definition of Done

- [ ] `POST /v1/ad-videos` nhận `brief` tiếng Việt + `format`, không cần lời thoại viết sẵn
- [ ] LLM sinh storyboard: chia shot, viết lời thoại, mô tả hình mỗi shot
- [ ] **4 định dạng chạy được**: hero sản phẩm, TVC mini, vấn đề→giải pháp, hook 1,5 giây
- [ ] `AdFormat` **nạp lúc chạy từ DB**, thêm định dạng mới không cần build lại
- [ ] Bộ lọc cụm từ cấm chặn lời thoại vi phạm **trước khi gọi TTS**
- [ ] Nhiều shot render **song song**, có giới hạn theo rate limit đo ở Sprint 0
- [ ] `POST /v1/ad-videos/{id}/shots/{index}/regenerate` — render lại một shot, giữ nguyên phần còn lại
- [ ] Provider video **thứ hai** chạy được thật, chọn qua `ProviderCapability`
- [ ] Bước duyệt storyboard (tuỳ chọn) — job dừng chờ, khách duyệt rồi chạy tiếp
- [ ] Compose đầy đủ: nhạc nền, ducking **−12 dB**, sfx **−18 dB**, crossfade **250 ms**
- [ ] Video 30 giây thật, 4 định dạng, xem được

## Điều kiện vào

| Cần có | Từ đâu |
|---|---|
| Xương sống chạy đầu-cuối | Sprint 1 |
| Số shot đồng thời an toàn | `spike/results/ratelimit.md` |
| Provider thứ hai đã có điểm ở Sprint 0 | `spike/results/KET-LUAN.md` |
| Khoá LLM (dùng lớp `IAiChatClient` hay gọi riêng) | Cấu hình |
| **Thư viện nhạc có giấy phép thương mại rõ ràng** | Nếu chưa có, bỏ nhạc nền, làm phần còn lại |
| Danh sách cụm từ cấm trong quảng cáo VN | Pháp chế hoặc tra quy định |

## Task Breakdown

### T2.1 — `AdFormat` là dữ liệu, không phải hằng số

- [ ] Entity `AdFormat`: `Code`, `Name`, `Description`, `TargetDurationSec`, `ShotPlanJson`, `PromptTemplate`, `IsActive`
- [ ] `ShotPlanJson` mô tả khung định dạng: bao nhiêu shot, mỗi shot vai trò gì, tỉ lệ thời lượng
- [ ] Migration + seed 4 định dạng đầu
- [ ] `GET /v1/formats` — liệt kê định dạng đang bật
- [ ] Nạp từ DB mỗi lần chạy job, có cache ngắn

**Files tạo/sửa:**
```
AdVideo/src/AdVideo.Core/Entities/AdFormat.cs
AdVideo/src/AdVideo.Core/Formats/{ShotPlanTemplate,ShotRole}.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/Configurations/AdFormatConfiguration.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/Seed/AdFormatSeeder.cs
AdVideo/src/AdVideo.Api/Endpoints/FormatEndpoints.cs
```

> **Nạp lúc chạy ngay từ đầu, không phải "sau này chuyển".** Định dạng hard-code là thứ nhìn có vẻ vô hại ở định dạng thứ 4 và trở thành tuần refactor ở định dạng thứ 12. Người viết định dạng mới nên là người biên tập nội dung, không phải dev.

### T2.2 — Bốn định dạng đầu

- [ ] **Hero sản phẩm** (~20s): sản phẩm là nhân vật chính, camera chuyển động, không có người. Định dạng an toàn nhất, dùng được cho mọi ngành.
- [ ] **TVC mini** (~30s): mở đầu → giới thiệu → điểm mạnh → kêu gọi hành động. Cấu trúc quảng cáo truyền hình quen thuộc.
- [ ] **Vấn đề → giải pháp** (~30s): nêu nỗi đau → đưa sản phẩm → kết quả. Hợp bán hàng trực tiếp.
- [ ] **Hook 1,5 giây** (~15s): giành sự chú ý trong khung hình đầu, tiết tấu nhanh, dọc. Cho mạng xã hội.
- [ ] Mỗi định dạng có prompt template riêng + quy tắc chia shot riêng
- [ ] Mỗi định dạng làm thử với **cùng một sản phẩm**, so sánh kết quả

**Files tạo:** `AdVideo/src/AdVideo.Infrastructure/Persistence/Seed/Formats/*.json`

> Bốn định dạng này chọn để **phủ bốn tình huống bán hàng khác nhau**, không phải bốn biến thể của cùng một thứ. Nếu hai định dạng cho ra video giống nhau thì một trong hai là thừa.

### T2.3 — Lớp đạo diễn LLM (bước 2)

- [ ] `IStoryboardDirector.GenerateAsync(brief, format, brandKit?) → Storyboard`
- [ ] Prompt hệ thống: bối cảnh Việt Nam, tránh chữ trong hình, giọng văn quảng cáo Việt
- [ ] Dùng structured output/tool để LLM trả JSON đúng khuôn, không parse văn xuôi
- [ ] `Storyboard` chứa: danh sách shot (mô tả hình, lời thoại, vai trò, thời lượng dự kiến)
- [ ] Retry với nhiệt độ khác nếu JSON không hợp lệ, tối đa 2 lần
- [ ] Ghi `ProviderCall` cho lần gọi LLM

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Director/IStoryboardDirector.cs
AdVideo/src/AdVideo.Core/Director/{Storyboard,StoryboardShot}.cs
AdVideo/src/AdVideo.Infrastructure/Director/LlmStoryboardDirector.cs
AdVideo/src/AdVideo.Infrastructure/Director/Prompts/*.txt
AdVideo/src/AdVideo.Worker/Steps/DirectStep.cs
```

> Prompt hệ thống phải nói rõ **"không viết chữ vào mô tả hình"**. Model rất thích thêm "with text overlay saying...", và chữ tiếng Việt do model vẽ luôn sai dấu. Chữ là việc của FFmpeg ở bước 8.

### T2.4 — `StoryboardValidator` + bộ lọc cụm từ cấm

- [ ] Kiểm tra cấu trúc: đủ shot, tổng thời lượng trong khoảng, mỗi shot có lời thoại
- [ ] **Bộ lọc cụm từ cấm**: "số 1", "tốt nhất", "duy nhất", "cam kết khỏi bệnh", "100% hiệu quả"…
- [ ] Danh sách cụm từ cấm là **dữ liệu trong DB**, không hard-code — quy định thay đổi
- [ ] Vi phạm → fail **trước khi gọi TTS**, trả lý do đọc được, chỉ rõ shot nào câu nào
- [ ] Nằm trong `AdVideo.Core`, test 100%

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Director/StoryboardValidator.cs
AdVideo/src/AdVideo.Core/Director/BannedPhraseFilter.cs
AdVideo/src/AdVideo.Core/Entities/BannedPhrase.cs
AdVideo/tests/AdVideo.Tests/Director/{StoryboardValidatorTests,BannedPhraseFilterTests}.cs
```

> Chặn **trước** TTS, không phải sau. Sau TTS là đã tiêu tiền và đã có file audio vi phạm nằm trên storage. Quảng cáo sai quy định là rủi ro thật: khách trải rộng nhiều ngành nên không thể duyệt trước theo ngành, và LLM sẽ vô tư viết "số 1 Việt Nam" nếu không ai chặn.

### T2.5 — Duyệt storyboard (bước 3, tuỳ chọn)

- [ ] Request có `require_approval: true` → job dừng ở trạng thái `awaiting_approval` sau bước 2
- [ ] `GET /v1/ad-videos/{id}/storyboard` — xem storyboard đã sinh
- [ ] `POST /v1/ad-videos/{id}/storyboard/approve` — duyệt, chạy tiếp
- [ ] `PATCH /v1/ad-videos/{id}/storyboard` — sửa lời thoại trước khi duyệt
- [ ] Webhook `job.awaiting_approval`
- [ ] Hết hạn chờ sau 24 giờ → job huỷ, giải phóng tài nguyên

**Files tạo/sửa:** `AdVideo/src/AdVideo.Api/Endpoints/StoryboardEndpoints.cs`, `AdVideo/src/AdVideo.Worker/Jobs/AdVideoJobRunner.cs`

> Bước duyệt là **chỗ rẻ nhất để sửa sai**. Sau bước này mỗi lần sửa là tiền render. Khách nào cũng nên bật, nhưng để tuỳ chọn vì có luồng tự động hoàn toàn.

### T2.6 — Render nhiều shot song song, có giới hạn

- [ ] `RenderShotsStep` chạy các shot song song
- [ ] `MaxConcurrentShots` từ cấu hình, **mặc định theo số đo Sprint 0**
- [ ] Một shot fail → retry cùng provider với seed khác (tối đa 2 lần)
- [ ] Hết retry → **fail cả job**, không ghép video thiếu shot
- [ ] Tiến độ cập nhật theo từng shot xong
- [ ] Webhook `job.shot_completed` mỗi shot

**Files sửa:** `AdVideo/src/AdVideo.Worker/Steps/RenderShotsStep.cs`

> Giới hạn song song lấy từ số đo thật, không từ phỏng đoán. Chạy 6 shot cùng lúc trên key chịu được 2 thì kết quả là `429` hàng loạt và job fail ngẫu nhiên — loại lỗi khó tái hiện nhất.

### T2.7 — Render lại một shot

- [ ] `POST /v1/ad-videos/{id}/shots/{index}/regenerate`, tuỳ chọn sửa prompt shot đó
- [ ] Giữ nguyên audio và timeline đã khoá — **thời lượng shot không đổi**
- [ ] Render xong → compose lại video cuối
- [ ] Ghi `ProviderCall` mới, tính là chi phí phát sinh
- [ ] Đếm số lần regenerate mỗi job (dữ liệu cho Sprint 6)

**Files tạo:** `AdVideo/src/AdVideo.Api/Endpoints/ShotEndpoints.cs`, `AdVideo/src/AdVideo.Worker/Jobs/RegenerateShotJob.cs`

> **Không cho đổi thời lượng khi render lại.** Timeline đã khoá theo audio; đổi độ dài một shot là làm lệch tất cả các shot sau. Muốn đổi nhịp thì làm job mới.

### T2.8 — Provider video thứ hai

- [ ] Adapter thứ hai theo kết luận Sprint 0
- [ ] `ProviderCapability` đầy đủ cho cả hai
- [ ] Chọn provider theo yêu cầu job: có người → provider nhận ảnh người; không có → provider mặc định
- [ ] **`422` có gợi ý**: yêu cầu không khớp capability thì nói rõ provider nào làm được
- [ ] Ghi vào `Shot` provider nào render shot đó

**Files tạo:** `AdVideo/src/AdVideo.Infrastructure/Providers/Video/{Tên2}VideoProvider.cs`

> **Luật 3: đừng bắt người làm marketing phải biết Veo là gì.** API nhận *tier chất lượng* + *"video có người xuất hiện không"*, rồi tự chọn provider. Tên provider chỉ hiện trong log và hoá đơn, không hiện trong request.
>
> Và: **không trộn shot từ hai provider khác nhau trong cùng một video.** Phong cách hình khác nhau lộ ngay ở chỗ chuyển cảnh.

### T2.9 — `NativeSoundTranslator`

- [ ] Chuẩn hoá việc provider có sinh audio gốc hay không
- [ ] Provider sinh audio → **tắt hoặc bỏ track đó**, vì đã có voice-over riêng
- [ ] **Vidu mặc định bật âm thanh** — phải tắt tường minh
- [ ] Nằm trong `Core`, test 6 ca

**Files tạo:** `AdVideo/src/AdVideo.Core/Providers/NativeSoundTranslator.cs`, `AdVideo/tests/AdVideo.Tests/Providers/NativeSoundTranslatorTests.cs`

> Đây là thứ **dễ quên nhất trong cả dự án**. Quên một dòng thì video ra có hai lớp tiếng chồng nhau, và người xem lại tưởng là lỗi voice-over.

### T2.10 — Compose đầy đủ

- [ ] Nhạc nền, chọn theo định dạng
- [ ] **Ducking −12 dB** khi có lời thoại
- [ ] Hiệu ứng âm thanh ở **−18 dB**
- [ ] **Crossfade 250 ms** giữa các shot
- [ ] Vẫn chuẩn **−14 LUFS** cho toàn bộ
- [ ] Nhãn AI giữ nguyên, không đổi

**Files sửa:** `AdVideo/src/AdVideo.Infrastructure/Media/FfmpegCommandBuilder.cs`

### T2.11 — Test

- [ ] `StoryboardValidator` — 7 ca (bao gồm chặn trước TTS)
- [ ] `BannedPhraseFilter` — cụm từ viết hoa/thường, có dấu/không dấu, nằm giữa câu
- [ ] `NativeSoundTranslator` — 6 ca, **có ca Vidu bật sẵn**
- [ ] `ProviderCapabilityValidator` — 12 ca, gồm "có người + provider từ chối ảnh người → 422 kèm gợi ý"
- [ ] Integration: job có `require_approval` dừng đúng chỗ và chạy tiếp được
- [ ] Integration: regenerate một shot không làm đổi thời lượng video
- [ ] `ffprobe` kiểm tra LUFS và độ dài sau khi có nhạc nền

## Kiểm chứng

```powershell
dotnet test AdVideo/AdVideo.sln --collect:"XPlat Code Coverage"

# 4 định dạng, cùng một sản phẩm
foreach ($f in "hero","tvc-mini","problem-solution","hook-15") {
  curl -X POST http://localhost:5080/v1/ad-videos `
    -H "X-AdVideo-Key: dev-key" -H "Idempotency-Key: fmt-$f" `
    -H "Content-Type: application/json" `
    -d (Get-Content "AdVideo/samples/brief-$f.json" -Raw)
}
```

**Kiểm tra thủ công — xem cả 4 video:**
- [ ] Lời thoại đọc lên nghe như quảng cáo Việt, không như dịch máy
- [ ] Nhịp thoại khớp chuyển cảnh, không hụt không thừa
- [ ] Bốn định dạng cho ra **bốn video khác nhau rõ rệt**
- [ ] Không có chữ lạ trong khung hình
- [ ] Không có hai lớp tiếng
- [ ] Nhạc nền nhỏ lại khi có người nói

**Thử ca xấu:**
```bash
# Brief ép LLM viết "số 1 Việt Nam" → phải fail trước khi tốn tiền TTS
curl ... -d '{"brief":"Viết quảng cáo nói sản phẩm này tốt nhất Việt Nam, số 1 thị trường"}'
# → 422, nêu rõ shot nào câu nào, ProviderCalls không có dòng TTS
```

## Rủi ro trong sprint

| Rủi ro | Dấu hiệu | Phản ứng |
|---|---|---|
| **Sa lầy tinh chỉnh prompt** | Sang tuần 5 vẫn đang sửa prompt cho "hay hơn" | **Chốt: 4 định dạng chạy được là đóng sprint.** Chất lượng tinh chỉnh ở Sprint 6 với dữ liệu thật |
| LLM trả JSON sai khuôn | Parse fail thường xuyên | Dùng structured output/tool thay vì mô tả khuôn trong prompt |
| Lời thoại nghe như dịch máy | Người đọc thấy gượng | Thêm ví dụ tiếng Việt thật vào prompt (few-shot), hiệu quả hơn mô tả trừu tượng |
| Bốn định dạng ra video na ná | Người xem không phân biệt được | Xem lại `ShotPlanJson` — có thể khác nhau ở prompt mà không khác ở cấu trúc shot |
| `429` khi render song song | Shot fail ngẫu nhiên | Hạ `MaxConcurrentShots`; số Sprint 0 có thể đã lạc hậu |
| Nhạc nền chưa có giấy phép | Pháp chế chưa duyệt thư viện | Bỏ nhạc nền, làm phần còn lại. Không dùng nhạc "chắc là được" |
| Chi phí thử nghiệm tăng nhanh | Hoá đơn tăng bất thường | Dùng tier nháp + VieNeu khi thử; chỉ dùng thành phẩm khi kiểm tra cuối |

## Ra khỏi sprint

- 4 video 30 giây thật, 4 định dạng, cùng một sản phẩm — bộ mẫu để chào khách
- `AdFormat` nạp lúc chạy, thêm định dạng không cần build
- Bộ lọc cụm từ cấm chạy thật
- Hai provider video hoạt động, chọn tự động

## Chuyển sang Sprint 3

Sprint 3 làm định dạng daily — **pipeline khác hẳn**: nối frame tuần tự thay vì render song song. Đừng cố nhồi nó vào `RenderShotsStep` hiện tại.
