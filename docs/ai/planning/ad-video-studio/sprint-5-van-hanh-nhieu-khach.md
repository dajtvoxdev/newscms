---
phase: planning
sprint: 5
title: "Sprint 5 — Vận hành nhiều khách (Tuần 8)"
description: Multi-tenant, giọng riêng có ghi nhận đồng ý, bộ nhận diện thương hiệu, hạn mức, QC đầy đủ, xuất nhiều tỉ lệ khung
---

# Sprint 5 — Vận hành nhiều khách

> Kế hoạch tổng: [../2026-09-15-feature-ad-video-studio.md](../2026-09-15-feature-ad-video-studio.md)
> Thiết kế: [../../design/2026-09-15-feature-ad-video-studio.md](../../design/2026-09-15-feature-ad-video-studio.md)

## Mục tiêu

Chuyển từ "một hệ thống chạy được" sang "một dịch vụ nhiều khách cùng dùng được mà không giẫm chân nhau".

**Sprint này không có tính tiền.** Credit hoãn lại làm cùng gói bán sau pilot. Nhưng phải **biết chi phí đô la thật** của mỗi job — đó là đầu vào để định giá ở Sprint 6.

Phần nặng nhất về pháp lý nằm ở đây: nhân bản giọng nói.

## Definition of Done

- [ ] Mọi truy vấn lọc theo `TenantId` **ở tầng repository**, không phải ở tầng endpoint
- [ ] Truy cập job của tenant khác trả **404**, không phải 403
- [ ] `VoiceProfile` — giọng riêng mỗi tenant, không dùng chéo được
- [ ] `ConsentRecord` sinh ra ở **mọi** lần tạo giọng, ghi tài khoản/IP/thời điểm/tên chủ giọng
- [ ] Có quy trình gỡ giọng khi bị khiếu nại
- [ ] `BrandKit` — logo, màu, font, câu định vị áp vào video
- [ ] `PronunciationDictionary` — sửa cách đọc tên riêng, tên thương hiệu
- [ ] `TenantQuota` — trần số job/ngày và trần chi phí đô la/ngày
- [ ] QC đầy đủ: quét chữ lọt lưới, kiểm nhãn AI, kiểm lệch tiếng-hình
- [ ] Xuất nhiều tỉ lệ khung từ một lần render
- [ ] Bảng đối chiếu: tổng `ProviderCall.CostUsd` khớp hoá đơn **trong ±5%**
- [ ] Lip-sync (bước 6) chạy được cho shot có người nói

## Điều kiện vào

| Cần có | Từ đâu |
|---|---|
| UI dùng được | Sprint 4 |
| Tài khoản ElevenLabs gói ≥ Creator | Cho Professional Voice Clone |
| Hoá đơn provider của một tháng thật | Để đối chiếu chi phí |
| Điều khoản dịch vụ có phần về giọng nói và hình ảnh | Pháp chế |
| Danh sách giọng tuyển chọn | `spike/results/voice-scorecard.md` (Sprint 0) |

## Task Breakdown

### T5.1 — Multi-tenant ở tầng repository

- [ ] `ITenantContext` lấy tenant từ API key
- [ ] Global query filter trong `AdVideoDbContext` theo `TenantId`
- [ ] Mọi entity có dữ liệu khách đều mang `TenantId`
- [ ] Không tìm thấy hoặc khác tenant → **404**
- [ ] Job chạy trong worker cũng phải mang đúng tenant context

**Files tạo/sửa:**
```
AdVideo/src/AdVideo.Core/Tenancy/ITenantContext.cs
AdVideo/src/AdVideo.Infrastructure/Tenancy/TenantContext.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/AdVideoDbContext.cs
AdVideo/tests/AdVideo.Tests/Tenancy/TenantScopeTests.cs
```

> Lọc ở **tầng repository**, không phải ở từng endpoint. Lọc ở endpoint nghĩa là mỗi endpoint mới là một cơ hội quên, và quên một lần là rò dữ liệu khách này sang khách khác.
>
> Trả **404 chứ không 403**: 403 xác nhận job đó có tồn tại, và số job là thông tin kinh doanh.

### T5.2 — `VoiceProfile` và ghi nhận đồng ý

- [ ] Entity `VoiceProfile`: `TenantId`, `Name`, `Provider`, `ExternalVoiceId`, `Kind` (library/IVC/PVC), `Status`, `ConsentRecordId`
- [ ] Tạo giọng từ Voice Library: chỉ chọn từ danh sách tuyển chọn Sprint 0
- [ ] Tạo giọng clone: **bắt buộc có `ConsentRecord` trước khi gọi provider**
- [ ] Entity `ConsentRecord`: tài khoản, IP, thời điểm, tên người chủ giọng, loại đồng ý, nội dung điều khoản đã ký
- [ ] Giọng của tenant này **không dùng được ở tenant khác** — kiểm tra ở tầng service, không chỉ ở UI
- [ ] Trạng thái `Suspended` để gỡ giọng khi có khiếu nại, không xoá dữ liệu kiểm chứng

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Entities/{VoiceProfile,ConsentRecord}.cs
AdVideo/src/AdVideo.Core/Voice/VoiceConsentGuard.cs
AdVideo/src/AdVideo.Infrastructure/Voice/VoiceProvisioningService.cs
AdVideo/src/AdVideo.Api/Endpoints/VoiceEndpoints.cs
AdVideo/tests/AdVideo.Tests/Voice/VoiceConsentGuardTests.cs
```

> Đây là chỗ rủi ro pháp lý cao nhất trong toàn dự án. Instant Voice Clone chỉ cần dưới 2 phút ghi âm và **không có bước xác minh chính chủ**. VieNeu tự host còn dễ hơn — 3 đến 8 giây, và không có lớp kiểm duyệt nào của nhà cung cấp đứng giữa.
>
> Giọng nói là dữ liệu sinh trắc học, xử lý nặng hơn hình ảnh. Khuyến khích khách dùng **Professional Voice Clone** vì ElevenLabs tự chạy bước xác minh giọng — đẩy một phần trách nhiệm sang nơi có công cụ để kiểm.

### T5.3 — `BrandKit`

- [ ] Entity `BrandKit`: `TenantId`, `LogoAssetId`, `PrimaryColor`, `SecondaryColor`, `FontFamily`, `Tagline`, `ToneOfVoice`
- [ ] Logo chèn ở bước compose, vị trí cấu hình được
- [ ] Màu thương hiệu dùng cho overlay chữ
- [ ] `ToneOfVoice` đưa vào prompt đạo diễn
- [ ] Một tenant nhiều BrandKit (khách có nhiều nhãn hàng)

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Entities/BrandKit.cs
AdVideo/src/AdVideo.Api/Endpoints/BrandKitEndpoints.cs
AdVideo/src/AdVideo.Infrastructure/Media/BrandOverlayComposer.cs
```

> Logo chèn bằng FFmpeg ở bước compose, **không đưa vào prompt cho model vẽ**. Model vẽ logo ra logo gần giống — và "gần giống" với nhận diện thương hiệu là tệ hơn không có.

### T5.4 — `PronunciationDictionary`

- [ ] Entity: `TenantId`, `Word`, `Pronunciation`, `Note`
- [ ] Áp trước khi gọi TTS: thay tên riêng bằng cách viết đọc đúng
- [ ] Mặc định sẵn cho các trường hợp hay gặp: đơn vị tiền, số điện thoại, tên nước ngoài
- [ ] UI trong NewsCMS để khách tự thêm

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Entities/PronunciationDictionary.cs
AdVideo/src/AdVideo.Core/Voice/PronunciationApplier.cs
AdVideo/tests/AdVideo.Tests/Voice/PronunciationApplierTests.cs
```

> Cùng họ vấn đề với việc model vẽ sai chữ tiếng Việt: máy đọc tên riêng sai là lỗi nhỏ nhưng làm hỏng cảm giác chuyên nghiệp của cả video. Sửa bằng từ điển rẻ hơn nhiều so với đổi model TTS.

### T5.5 — `TenantQuota` — trần chi tiêu

- [ ] Entity: `TenantId`, `MaxJobsPerDay`, `MaxCostUsdPerDay`, `MaxConcurrentJobs`
- [ ] Kiểm tra trước khi nhận job, vượt trần → `429` có thông báo rõ
- [ ] Cộng dồn chi phí trong ngày từ `ProviderCall`
- [ ] Cảnh báo khi chi tiêu toàn hệ thống trong ngày vượt ngưỡng
- [ ] Trần `max_credits` mỗi job (đã có từ hợp đồng API) được kiểm tra thật

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Entities/TenantQuota.cs
AdVideo/src/AdVideo.Core/Quota/QuotaGuard.cs
AdVideo/src/AdVideo.Worker/Jobs/DailySpendAlertJob.cs
AdVideo/tests/AdVideo.Tests/Quota/QuotaGuardTests.cs
```

> Đây là hàng rào chống thủng ví. Một vòng retry sai hoặc một khách bấm 50 lần có thể tiêu hàng trăm đô trong vài phút. Hạn mức đặt theo **đô la thật từ `ProviderCall`**, không theo số job — vì một job daily đắt gấp nhiều lần một job hero.

### T5.6 — QC đầy đủ (bước 9)

- [ ] **Quét chữ lọt lưới**: lấy mẫu khung hình, phát hiện chữ do model vẽ, cảnh báo hoặc fail
- [ ] Kiểm nhãn AI có trên hình **và** trong metadata — thiếu là fail cứng
- [ ] Kiểm lệch tiếng-hình: đo ở giây cuối, > 200 ms là fail
- [ ] Kiểm LUFS, độ dài, độ phân giải, tỉ lệ khung
- [ ] Kiểm khung hình đen hoặc đứng hình
- [ ] Báo cáo QC lưu cùng job, xem được trong UI

**Files tạo/sửa:**
```
AdVideo/src/AdVideo.Worker/Steps/QcStep.cs
AdVideo/src/AdVideo.Infrastructure/Qc/{TextDetector,DriftMeasurer,BlackFrameDetector}.cs
AdVideo/src/AdVideo.Core/Qc/QcReport.cs
```

> Quét chữ là lưới an toàn **thứ hai**. Lưới thứ nhất là negative prompt "no text" toàn cục từ Sprint 1. Cần cả hai vì negative prompt không phải lúc nào cũng được model tuân thủ.

### T5.7 — Lip-sync (bước 6)

- [ ] Chỉ chạy cho shot có người nói và khách bật
- [ ] Gọi provider lip-sync, ghi `ProviderCall`
- [ ] Fail lip-sync → **video vẫn ra**, ghi cảnh báo, không fail cả job
- [ ] Đo lệch sau lip-sync, vẫn phải **< 200 ms**

**Files tạo:** `AdVideo/src/AdVideo.Worker/Steps/LipSyncStep.cs`, `AdVideo/src/AdVideo.Infrastructure/Providers/LipSync/*.cs`

> Lip-sync là bước **tuỳ chọn và không chặn**. Nó làm video tốt hơn chứ không làm video dùng được; fail nó mà giết cả job là đánh đổi sai.

### T5.8 — Xuất nhiều tỉ lệ khung

- [ ] Từ một lần render, xuất ra dọc 9:16, vuông 1:1, ngang 16:9
- [ ] Cắt thông minh: giữ chủ thể chính trong khung, không cắt giữa mặt người
- [ ] Nhãn AI đặt lại vị trí cho từng tỉ lệ, luôn đọc được
- [ ] Mỗi bản xuất là một `MediaAsset` riêng

**Files tạo:** `AdVideo/src/AdVideo.Infrastructure/Media/AspectReframer.cs`

> Cắt lại rẻ hơn render lại rất nhiều. Nhưng render ở tỉ lệ gốc rộng nhất rồi cắt xuống, không phải ngược lại — phóng to từ khung hẹp thì mờ.

### T5.9 — Đối chiếu chi phí với hoá đơn

- [ ] Báo cáo: tổng `ProviderCall.CostUsd` theo provider theo tháng
- [ ] Đối chiếu với hoá đơn thật của từng nhà cung cấp
- [ ] Chênh lệch phải **trong ±5%**
- [ ] Lệch nhiều hơn → tìm ra nguyên nhân trước khi đóng sprint
- [ ] Báo cáo chi phí trung bình mỗi định dạng — đầu vào định giá

**Files tạo:** `AdVideo/src/AdVideo.Api/Endpoints/ReportEndpoints.cs`, `AdVideo/src/AdVideo.Core/Reporting/CostReport.cs`

> Nếu số trong DB lệch hoá đơn quá 5% thì mọi tính toán định giá ở Sprint 6 đều sai. Đây là task nhỏ nhưng chặn việc ra quyết định kinh doanh.

### T5.10 — Smoke test hằng ngày

- [ ] Hangfire recurring job gọi **thật** từng provider mỗi ngày, video ngắn nhất có thể
- [ ] **Không chạy trong CI** — CI không nên phụ thuộc mạng ngoài và không nên tiêu tiền
- [ ] Provider fail → cảnh báo ngay
- [ ] Ghi lại thời gian phản hồi để theo dõi xu hướng

**Files tạo:** `AdVideo/src/AdVideo.Worker/Jobs/ProviderSmokeTestJob.cs`

> Đây là cách phát hiện provider đổi API hoặc ngừng dịch vụ **trước khi khách phát hiện**. Sora bị gỡ khỏi API trong chưa đầy một năm kể từ lúc ra mắt — chuyện này có thật và sẽ lặp lại.

### T5.11 — Test

- [ ] `TenantScopeFilter` — 5 ca, gồm truy cập chéo tenant trả 404
- [ ] `VoiceConsentGuard` — tạo giọng không có consent bị chặn
- [ ] `QuotaGuard` — vượt trần job, vượt trần đô la, job đang chạy đồng thời
- [ ] `PronunciationApplier` — tên riêng, số tiền, chữ viết tắt
- [ ] Integration: hai tenant chạy job cùng lúc, không thấy dữ liệu của nhau
- [ ] Integration: QC fail vì thiếu nhãn AI → job fail

## Kiểm chứng

```powershell
dotnet test AdVideo/AdVideo.sln --collect:"XPlat Code Coverage"

# Truy cập chéo tenant
curl http://localhost:5080/v1/ad-videos/<job-cua-tenant-A> -H "X-AdVideo-Key: key-tenant-B"
# → 404

# Vượt hạn mức
# (chạy job liên tiếp tới khi vượt MaxCostUsdPerDay) → 429 có thông báo rõ

# Đối chiếu chi phí
curl http://localhost:5080/v1/reports/cost?month=2026-09 -H "X-AdVideo-Key: admin-key"
```

```sql
-- Tổng chi phí theo provider, so với hoá đơn
SELECT Provider, SUM(CostUsd) AS Total, COUNT(*) AS Calls
FROM ProviderCalls
WHERE CreatedAt >= '2026-09-01'
GROUP BY Provider;
```

**Kiểm tra thủ công:**
- [ ] Tạo giọng clone mà không tick đồng ý → bị chặn
- [ ] `ConsentRecord` có đủ IP, thời điểm, tên chủ giọng
- [ ] Video có logo và màu thương hiệu đúng
- [ ] Tên thương hiệu đọc đúng sau khi thêm vào từ điển
- [ ] Ba bản xuất tỉ lệ khác nhau đều giữ chủ thể trong khung, nhãn AI đọc được

## Rủi ro trong sprint

| Rủi ro | Dấu hiệu | Phản ứng |
|---|---|---|
| **Rò dữ liệu giữa tenant** | Thấy job của khách khác | Dừng mọi việc khác. Đây là lỗi nghiêm trọng nhất có thể có |
| Chi phí DB lệch hoá đơn nhiều | Chênh > 5% | Kiểm tra có bỏ sót loại lời gọi nào không (lip-sync? retry? LLM?) |
| Cắt lại khung làm mất chủ thể | Mặt người bị cắt nửa | Cắt theo chủ thể chính chứ không cắt giữa; chấp nhận chừa viền nếu cần |
| Quét chữ báo động giả nhiều | Video tốt bị fail | Chuyển từ fail sang cảnh báo, để người xem quyết định |
| PVC cần thời gian xử lý lâu | Giọng chưa sẵn sàng khi khách cần | Cho dùng giọng thư viện trong lúc chờ |
| Lip-sync làm lệch tiếng-hình nặng hơn | Đo được > 200 ms sau lip-sync | Tắt lip-sync mặc định, để khách bật thủ công |

## Ra khỏi sprint

- Nhiều khách dùng chung được, cách ly dữ liệu đã kiểm chứng
- Biết **chi phí đô la thật** mỗi định dạng — đầu vào định giá
- Hồ sơ đồng ý về giọng nói đầy đủ, chịu được rà soát
- Smoke test hằng ngày phát hiện provider hỏng

## Chuyển sang Sprint 6

Sprint 6 mở cho khách thật. **Chặn cứng: pháp chế phải chốt hình thức nhãn AI trước khi mở cho khách ngoài.**
