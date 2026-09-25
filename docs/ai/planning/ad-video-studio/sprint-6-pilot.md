---
phase: planning
sprint: 6
title: "Sprint 6 — Pilot (Tuần 9)"
description: 2–3 khách thật, ≥ 50 job, đo tỉ lệ render lại theo định dạng, số liệu để định giá
---

# Sprint 6 — Pilot

> Kế hoạch tổng: [../2026-09-15-feature-ad-video-studio.md](../2026-09-15-feature-ad-video-studio.md)
> Yêu cầu: [../../requirements/2026-09-15-feature-ad-video-studio.md](../../requirements/2026-09-15-feature-ad-video-studio.md)

## Mục tiêu

Đưa cho **2–3 khách hàng thật** dùng, thu đủ **50 job** trở lên, rồi trả lời câu hỏi mà không ai trả lời được bằng suy đoán:

- Định dạng nào khách thật sự dùng, định dạng nào không ai đụng tới?
- Khách render lại bao nhiêu lần trước khi hài lòng?
- Một video "khách chấp nhận được" **thật sự tốn bao nhiêu**, tính cả số lần render lại?
- Bán giá bao nhiêu thì có lãi?

Sprint này ít code nhất và quyết định nhiều nhất. Code chủ yếu là **đo đạc**, không phải thêm tính năng.

## Definition of Done

- [ ] **Pháp chế đã chốt hình thức nhãn AI** và nhãn hiện tại đúng theo đó ← **chặn cứng**
- [ ] 2–3 khách thật đã dùng, mỗi khách ít nhất 10 job
- [ ] Tổng ≥ **50 job** hoàn thành
- [ ] `FormatUsageStat` ghi nhận: định dạng nào dùng bao nhiêu, render lại mấy lần
- [ ] Biết **chi phí thật một video được chấp nhận**, tính cả render lại
- [ ] Tài liệu API công khai cho khách tự tích hợp
- [ ] Danh sách lỗi và chỗ vướng từ khách thật, đã phân loại P0–P3
- [ ] P0 và P1 đã sửa xong
- [ ] Bảng kiến nghị định giá, có số liệu chống lưng
- [ ] Kiểm tra lại toàn bộ danh sách "bẫy đã biết" trước khi mở rộng

## Điều kiện vào

| Cần có | Ghi chú |
|---|---|
| **Pháp chế chốt nhãn AI** | **Chặn cứng.** Không mở cho khách ngoài nếu chưa có |
| Multi-tenant chạy ổn | Sprint 5 |
| Hạn mức chi tiêu đã bật | Sprint 5 — bảo vệ khi khách dùng thật |
| 2–3 khách đồng ý pilot | Nên là khách đã có quan hệ, chấp nhận sản phẩm chưa hoàn chỉnh |
| Điều khoản dịch vụ đã ký | Bao gồm phần giọng nói, hình ảnh, trách nhiệm nội dung |
| Người trực hỗ trợ trong tuần pilot | Khách vướng phải có người trả lời trong ngày |

## Task Breakdown

### T6.1 — Chốt nhãn AI với pháp chế

- [ ] Pháp chế đọc Luật TTNT 2025 (hiệu lực 1/3/2026) và các văn bản hướng dẫn
- [ ] Chốt: nhãn ghi gì, đặt ở đâu, hiện bao lâu, metadata cần gì
- [ ] Cập nhật `AiLabelStamper` theo đúng kết luận
- [ ] Kiểm tra lại toàn bộ video đã làm có đúng chuẩn mới không
- [ ] Ghi lại kết luận thành tài liệu, dẫn chiếu điều khoản luật

**Files sửa:** `AdVideo/src/AdVideo.Core/Media/AiLabelStamper.cs`
**Files tạo:** `docs/legal/nhan-ai-ket-luan.md`

> Đây là task **duy nhất chặn cứng** cả sprint. Nghĩa vụ gắn nhãn thuộc về bên triển khai, mức phạt tới 2 tỉ đồng với tổ chức, và **không chuyển sang khách bằng điều khoản được**. Nếu pháp chế chưa trả lời đến đầu tuần 9 thì pilot chuyển thành **pilot nội bộ** — vẫn thu được số liệu, chỉ là không có khách ngoài.

### T6.2 — `FormatUsageStat` — đo cái đáng đo

- [ ] Entity: `TenantId`, `FormatCode`, `JobCount`, `RegenerateCount`, `AcceptedCount`, `AvgCostUsd`, `AvgDurationSec`, `PeriodStart`
- [ ] Tự động cập nhật khi job hoàn thành hoặc bị render lại
- [ ] Đánh dấu "chấp nhận" khi khách tải video về — đây là tín hiệu hài lòng gần nhất có được
- [ ] Báo cáo: định dạng × số job × tỉ lệ render lại × chi phí trung bình

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Entities/FormatUsageStat.cs
AdVideo/src/AdVideo.Worker/Jobs/FormatStatAggregatorJob.cs
AdVideo/src/AdVideo.Api/Endpoints/StatEndpoints.cs
```

> **Tỉ lệ render lại là chỉ số quan trọng nhất của pilot.** Nó nói lên hai thứ cùng lúc: định dạng nào chưa đủ tốt, và chi phí thật cao hơn chi phí danh nghĩa bao nhiêu. Một định dạng rẻ mà khách render lại 4 lần thì đắt hơn định dạng đắt mà khách nhận ngay.
>
> Dùng **hành vi tải về** làm tín hiệu chấp nhận, không dùng nút "đánh giá" — khách không bấm nút đánh giá.

### T6.3 — Tài liệu API cho khách

- [ ] OpenAPI/Swagger sinh từ code, đúng với thực tế
- [ ] Hướng dẫn bắt đầu: lấy key, gọi job đầu tiên, nhận webhook
- [ ] Bảng mã lỗi có giải thích tiếng Việt
- [ ] Ví dụ `curl` chạy được copy-paste
- [ ] Nói rõ về `Idempotency-Key` và vì sao bắt buộc
- [ ] Nói rõ về hạn mức và điều gì xảy ra khi vượt

**Files tạo:**
```
docs/api/ad-video-api.md
AdVideo/src/AdVideo.Api/OpenApi/*.cs
```

### T6.4 — Đón khách pilot

- [ ] Mỗi khách: một tenant, một API key, hạn mức phù hợp
- [ ] Buổi hướng dẫn 30 phút, **quay màn hình lại** để xem họ vướng ở đâu
- [ ] Nạp sẵn `BrandKit` và `PronunciationDictionary` cho từng khách
- [ ] Kênh hỗ trợ trực tiếp trong tuần pilot
- [ ] Nói trước: đây là bản thử, có thể có lỗi, phản hồi được hoan nghênh

> Quay lại buổi hướng dẫn có giá trị hơn mọi bản khảo sát. Chỗ khách im lặng và rê chuột vòng vòng chính là chỗ giao diện có vấn đề — và họ sẽ không báo cáo chỗ đó vì tưởng là mình chưa hiểu.

### T6.5 — Theo dõi hằng ngày trong tuần pilot

- [ ] Xem job fail mỗi ngày, tìm nguyên nhân trong ngày
- [ ] Theo dõi chi tiêu đô la mỗi ngày theo khách
- [ ] Kiểm tra smoke test provider có báo động không
- [ ] Ghi mọi phản hồi của khách vào một chỗ, kể cả phàn nàn nhỏ
- [ ] Phân loại lỗi P0–P3 mỗi ngày

**Files tạo:** `docs/pilot/nhat-ky-pilot.md`

| Mức | Nghĩa | Thời hạn xử lý |
|---|---|---|
| P0 | Không dùng được, hoặc rò dữ liệu, hoặc thiếu nhãn AI | Trong ngày |
| P1 | Tính năng chính hỏng, có cách vòng tránh | Trong 2 ngày |
| P2 | Gây khó chịu, không chặn việc | Sau pilot |
| P3 | Đề xuất cải tiến | Ghi nhận, xếp hàng |

### T6.6 — Tinh chỉnh chất lượng bằng dữ liệu thật

- [ ] Xem những job bị render lại nhiều nhất, tìm điểm chung
- [ ] Sửa prompt đạo diễn cho định dạng có tỉ lệ render lại cao
- [ ] Bổ sung `SceneEntry` theo ngành mà khách pilot thuộc về
- [ ] Bổ sung `PronunciationDictionary` mặc định từ tên riêng khách hay dùng
- [ ] Bổ sung cụm từ cấm nếu thấy LLM vẫn lọt

> **Đây là lúc tinh chỉnh chất lượng, không phải Sprint 2.** Sprint 2 tinh chỉnh bằng cảm tính của đội phát triển; ở đây tinh chỉnh bằng việc khách thật render lại bao nhiêu lần. Khác nhau hoàn toàn.

### T6.7 — Rà lại danh sách bẫy đã biết

Chạy hết danh sách này trước khi tính chuyện mở rộng. Mỗi mục đều là thứ đã biết là dễ hỏng:

- [ ] **Vidu mặc định bật âm thanh** — kiểm tra video Vidu không có hai lớp tiếng
- [ ] **Veo giữ file khoảng 2 ngày** — kiểm tra mọi file đã nằm trên MinIO, không còn URL nhà cung cấp
- [ ] **Lệch giờ máy MinIO** — kiểm tra NTP còn chạy, presigned URL còn hoạt động
- [ ] **CORS của MinIO** — kiểm tra tải/ghi từ trình duyệt, xem F12 có lỗi im lặng không
- [ ] **Trôi hình sau 4–5 lần nối** — xem lại video daily gần nhất
- [ ] **Không trộn shot từ hai provider** trong một video — kiểm tra trong dữ liệu `Shot`
- [ ] **ElevenLabs: giọng Default ngừng hoạt động 31/12/2026** — kiểm tra không `VoiceProfile` nào đang dùng giọng mặc định

**Files tạo:** `docs/pilot/checklist-truoc-khi-mo-rong.md`

### T6.8 — Báo cáo tổng kết pilot

- [ ] Số liệu: bao nhiêu job, định dạng nào bao nhiêu, tỉ lệ render lại từng định dạng
- [ ] Chi phí: trung bình mỗi video được chấp nhận, theo định dạng
- [ ] Thời gian: trung bình và tệ nhất, theo định dạng
- [ ] Tỉ lệ fail và nguyên nhân chính
- [ ] Phản hồi khách: cái gì họ thích, cái gì làm họ bực
- [ ] **Kiến nghị định giá**: giá sàn theo chi phí, giá đề xuất, biên lợi nhuận
- [ ] Kiến nghị: định dạng nào giữ, định dạng nào bỏ, định dạng nào cần làm thêm

**Files tạo:** `docs/pilot/bao-cao-tong-ket.md`

> Bảng định giá phải dựa trên **chi phí một video được chấp nhận**, không phải chi phí một lần render. Nếu khách render lại trung bình 2,3 lần thì giá vốn thật là 2,3 lần giá niêm yết của provider — bỏ qua điều này là định giá lỗ.

### T6.9 — Quyết định về phần credit

- [ ] Dựa trên số liệu pilot, quyết định mô hình tính tiền: theo video, theo credit, hay theo gói tháng
- [ ] Nếu chọn credit: thiết kế công thức quy đổi từ chi phí đô la thật
- [ ] Ghi lại quyết định và lý do — **chưa code ở sprint này**

**Files tạo:** `docs/pilot/quyet-dinh-mo-hinh-tinh-tien.md`

> Phần credit **cố ý hoãn tới đây**. Thiết kế công thức tính tiền trước khi biết chi phí thật và hành vi thật của khách là cách chắc chắn nhất để phải làm lại. Bảng `Credit*` vẫn chưa tạo; `ProviderCall` đã có đủ dữ liệu để dựng công thức khi cần.

## Kiểm chứng

```sql
-- Đủ 50 job chưa
SELECT COUNT(*) FROM AdVideoJobs WHERE Status = 'completed' AND CreatedAt >= '<ngày bắt đầu pilot>';

-- Tỉ lệ render lại theo định dạng
SELECT FormatCode,
       SUM(JobCount) AS Jobs,
       SUM(RegenerateCount) AS Regens,
       CAST(SUM(RegenerateCount) AS float) / NULLIF(SUM(JobCount), 0) AS RegenPerJob,
       AVG(AvgCostUsd) AS AvgCost
FROM FormatUsageStats
GROUP BY FormatCode
ORDER BY Jobs DESC;

-- Chi phí thật một video được chấp nhận
SELECT f.FormatCode,
       SUM(p.CostUsd) / NULLIF(SUM(f.AcceptedCount), 0) AS CostPerAccepted
FROM FormatUsageStats f
JOIN ProviderCalls p ON ...
GROUP BY f.FormatCode;
```

**Kiểm tra trước khi tuyên bố xong:**
- [ ] Không còn P0, P1 nào mở
- [ ] Nhãn AI đúng theo kết luận pháp chế trên **mọi** video đã giao
- [ ] Không có khách nào thấy dữ liệu của khách khác
- [ ] Chi phí DB khớp hoá đơn trong ±5%
- [ ] Danh sách bẫy đã biết đã rà hết

## Rủi ro trong sprint

| Rủi ro | Dấu hiệu | Phản ứng |
|---|---|---|
| **Pháp chế chưa chốt nhãn AI** | Không phản hồi đến đầu tuần 9 | **Chuyển sang pilot nội bộ.** Vẫn thu số liệu, không mở cho khách ngoài |
| Khách không dùng đủ nhiều | Dưới 20 job sau 4 ngày | Chủ động đề nghị làm video cùng khách; số liệu mỏng thì kết luận không đáng tin |
| Khách chỉ dùng một định dạng | 90% job vào một định dạng | Đây cũng là kết quả có giá trị — nhưng hỏi vì sao họ không thử cái khác |
| Chi phí vượt dự tính nhiều | Hạn mức bị chạm liên tục | Không nâng hạn mức vội. Tìm hiểu vì sao render lại nhiều trước |
| Lỗi P0 xuất hiện với khách thật | Video giao thiếu nhãn, dữ liệu rò | Dừng pilot, sửa, thông báo khách. Đừng sửa lặng lẽ |
| Khách đòi tính năng mới giữa pilot | Yêu cầu ngoài phạm vi | Ghi vào danh sách, không làm trong tuần pilot. Pilot là để đo, không phải để mở rộng |
| Số liệu quá đẹp để tin | Tỉ lệ render lại gần bằng 0 | Kiểm tra cách đo — có thể khách đang chấp nhận cả video không ưng vì ngại phản hồi |

## Ra khỏi sprint

- `docs/pilot/bao-cao-tong-ket.md` — số liệu thật để quyết định kinh doanh
- Bảng kiến nghị định giá có chi phí chống lưng
- Quyết định mô hình tính tiền, chưa code
- Danh sách lỗi và đề xuất đã phân loại — đầu vào cho giai đoạn sau
- Tài liệu API công khai

## Sau Sprint 6

Những thứ **cố ý để lại** ngoài 9 tuần này, sắp theo thứ tự nên làm:

1. **Phần credit / tính tiền** — theo quyết định ở T6.9
2. **Adapter Runway** — đã thiết kế chỗ cắm, chưa viết code
3. **Thêm định dạng** — theo số liệu `FormatUsageStat`, không theo cảm tính
4. **Mở rộng thư viện bối cảnh** theo ngành của khách thật
5. **Tối ưu chi phí** — dùng tier rẻ cho những shot không quan trọng
6. **Tự động hoá hoàn toàn** — bỏ bước duyệt cho khách đã tin tưởng hệ thống
