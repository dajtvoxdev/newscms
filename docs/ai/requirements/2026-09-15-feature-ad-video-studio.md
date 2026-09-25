---
phase: requirements
title: Requirements & Problem Understanding
description: Xưởng video quảng cáo AI — service sinh video quảng cáo có voice-over tiếng Việt từ ảnh sản phẩm + brief
---

# Xưởng video quảng cáo AI — Yêu cầu

> Nguồn gốc: [docs/ke-hoach-service-video-quang-cao.html](../../ke-hoach-service-video-quang-cao.html) (Bản 9, 14/09/2026).
> Tài liệu này là bản chuyển thể sang định dạng AI DevKit; file HTML vẫn là nguồn sự thật cho số liệu giá và so sánh provider.

## Problem Statement

**Vấn đề cốt lõi.** Khách hàng của NewsCMS — doanh nghiệp SME Việt Nam bán hàng qua TikTok/Facebook/YouTube — cần video quảng cáo liên tục (theo chiến dịch, theo mùa, theo từng biến thể sản phẩm) nhưng mỗi TVC ngắn thuê ngoài tốn **3–8 triệu đồng** và mất vài ngày. Kết quả là họ đăng ảnh tĩnh, hoặc dùng video quay bằng điện thoại, hoặc không quảng cáo gì cả.

**Ai bị ảnh hưởng.**
- *Người làm marketing tại SME* — người trực tiếp chịu KPI nội dung, không biết dựng phim, không có ngân sách thuê agency cho từng biến thể.
- *Chủ doanh nghiệp nhỏ* — có ảnh sản phẩm chụp sẵn nhưng không biến được thành video.
- *Chúng ta (nhà vận hành NewsCMS)* — đang bán CMS, muốn có thêm một dịch vụ giá trị cao bán kèm cho tập khách sẵn có.

**Hiện trạng / workaround.** Tự ghép ảnh thành slideshow bằng CapCut, hoặc thuê freelancer từng video. Cả hai đều không scale theo số biến thể, và không ai làm được voice-over tiếng Việt chất lượng ổn định.

**Vì sao bây giờ.** Các model text/image-to-video đã đủ tốt để dựng shot quảng cáo sản phẩm với giá vốn ~166.000 đ cho video 30 giây — rẻ hơn 20–50 lần so với thuê ngoài. Biên lợi nhuận rất rộng; điểm chặn thật sự là **chất lượng đầu ra có hợp gu khách Việt không**, không phải chi phí API.

## Goals & Objectives

### Primary goals

1. **G1 — Từ brief ra video hoàn chỉnh.** Nhận ảnh sản phẩm (+ ảnh người mẫu, + ảnh bối cảnh) và một đoạn mô tả tiếng Việt, trả về file MP4 có voice-over tiếng Việt, phụ đề, logo, CTA — không cần người dựng can thiệp.
2. **G2 — Voice-over tiếng Việt đúng nhịp.** Lời thoại do TTS riêng sinh ra (không dùng thoại của model video), và **thời lượng shot bám theo độ dài audio thật**, không ngược lại.
3. **G3 — Không phụ thuộc một provider.** Bốn adapter video (Veo, Kling, Seedance, Vidu) đều chạy được trong production; một provider bị khai tử không làm chết sản phẩm.
4. **G4 — Kho prompt là tài sản, không phải code.** Định dạng quảng cáo và thư viện bối cảnh Việt Nam là dữ liệu có version, sửa được lúc chạy, không cần deploy.
5. **G5 — Biết chính xác mỗi video tốn bao nhiêu.** Mọi lần gọi ra ngoài được ghi sổ kèm chi phí thật bằng đô la, từ ngày đầu.
6. **G6 — Tuân thủ nghĩa vụ gắn nhãn AI.** Nhãn hiển thị + metadata nhúng là một bước bắt buộc trong pipeline, khách không tắt được.

### Secondary goals

- **G7** — Mặt tiền trong NewsCMS để khách tự làm, không cần dev.
- **G8** — Regenerate từng shot thay vì cả video (đòn bẩy giảm giá vốn mạnh nhất).
- **G9** — Xuất nhiều tỉ lệ khung hình từ cùng một storyboard + voice-over.
- **G10** — Vòng học: thống kê format nào hợp ngành hàng nào sau vài trăm video.

### Non-goals (ngoài phạm vi 9 tuần)

| Không làm | Lý do |
|---|---|
| Hệ thống tính tiền / credit hoàn chỉnh | Hoãn, làm cùng lúc với thiết kế gói bán (mục 09 file HTML). Khách pilot dùng theo hạn mức thoả thuận. |
| Duyệt nội dung theo ngành hàng bởi người của mình | Đã chốt: khách tự chịu trách nhiệm nội dung; khách trải rộng nhiều ngành nên không duyệt trước được. |
| Adapter Sora | Bị gỡ khỏi API ngày 24/09/2026, không có model thay thế. |
| Adapter Runway | Giữ ở dạng thiết kế dự phòng, không code trong 9 tuần đầu. |
| Editor timeline kéo-thả cho khách | Duyệt storyboard dạng form là đủ cho pilot. |
| Video > 60 giây | Sau 4–5 lần nối frame, màu và khuôn mặt bắt đầu trôi. Giới hạn thực tế 40–60 giây. |
| Mở đăng ký tự do cho khách ngoài | Chặn bởi việc pháp chế chốt hình thức nhãn AI (xem Open Items). |

## User Stories & Use Cases

### Người làm marketing tại SME (vai chính)

- Là **marketer**, tôi muốn **tải 2–3 ảnh sản phẩm và gõ một đoạn mô tả tiếng Việt**, để **nhận về video 30 giây đăng TikTok được ngay**.
- Là **marketer**, tôi muốn **chọn định dạng quảng cáo qua video ví dụ thật** thay vì đọc mô tả chữ, để **biết mình sắp nhận cái gì trước khi tốn tiền**.
- Là **marketer**, tôi muốn **xem và sửa storyboard trước khi render**, để **không phải chờ 10 phút rồi mới phát hiện lời thoại sai tên sản phẩm**.
- Là **marketer**, tôi muốn **render lại đúng shot số 3** mà tôi không ưng, để **không phải trả tiền cho cả video 30 giây**.
- Là **marketer**, tôi muốn **nghe thử giọng đọc trước khi chọn**, để **chọn bằng tai chứ không bằng mô tả "Nữ miền Bắc"**.
- Là **marketer**, tôi muốn **xuất bản 9:16 rồi xuất thêm bản 16:9** với cùng lời thoại, để **đăng được cả TikTok lẫn YouTube mà video nói giống nhau**.
- Là **marketer**, tôi muốn **thấy từng shot hiện dần trong lúc chờ**, để **biết hệ thống đang chạy chứ không treo**.

### Chủ thương hiệu

- Là **chủ thương hiệu**, tôi muốn **lưu logo, font, màu, bumper kết vào một brand kit**, để **mọi video của tôi trông cùng một nhà**.
- Là **chủ thương hiệu**, tôi muốn **dạy hệ thống đọc đúng tên thương hiệu của tôi**, để **không bị đọc sai dấu ở mọi video**.
- Là **chủ thương hiệu**, tôi muốn **tạo giọng riêng từ file ghi âm người phát ngôn của mình**, để **video có giọng nhận diện được**.

### Dev tích hợp (tenant NewsCMS khác)

- Là **dev**, tôi muốn **gọi `POST /v1/ad-videos` và nhận webhook khi xong**, để **nhúng tính năng vào site của tôi mà không phải poll**.
- Là **dev**, tôi muốn **gọi `POST /v1/estimates` trước**, để **hiện số credit chính xác lên nút bấm**.
- Là **dev**, tôi muốn **`GET /v1/providers` trả về giới hạn của từng model**, để **form của tôi tự dựng đúng ràng buộc thay vì hard-code**.

### Người vận hành (chúng ta)

- Là **người vận hành**, tôi muốn **biết một video thật sự tốn bao nhiêu đô la và vì sao**, để **định giá gói bán bằng số liệu chứ không bằng cảm tính**.
- Là **người vận hành**, tôi muốn **được cảnh báo khi chi tiêu trong ngày vượt ngưỡng**, để **một vòng retry sai không thủng ví**.
- Là **người vận hành**, tôi muốn **smoke test hằng ngày cho từng provider**, để **phát hiện API đổi trước khi khách phát hiện**.

### Edge cases phải xử lý

| Tình huống | Hành vi mong muốn |
|---|---|
| Khách chọn Veo nhưng upload ảnh có mặt người | Trả `422` kèm lý do đọc được + gợi ý Kling. Không để provider từ chối rồi ném lỗi khó hiểu. |
| Khách chọn format `daily` nhưng không upload ảnh người | Từ chối ngay ở tầng API (khối `requires`), không chạy đến bước 6 mới hỏng. |
| Khách bấm nút tạo job hai lần | `Idempotency-Key` bắt buộc — một job, một lần tính tiền. |
| Provider chết giữa job | Fail cả job, mời khách render lại bằng provider khác. **Không** âm thầm vá một shot bằng model khác. |
| Shot lỗi lẻ | Retry cùng provider, đổi seed. Không đổi model. |
| Khách bật `sfx_only` trên provider không tách được thoại | Bật audio + negative prompt "no speech" + kiểm lại bằng VAD ở bước QC. |
| LLM viết lời thoại chứa "số 1 Việt Nam" | Bộ lọc cụm từ cấm chặn **trước** khi gọi TTS. |
| Khách huỷ job giữa chừng | Dừng phát sinh chi phí; chốt phần đã render, phần còn lại hoàn. |
| Ảnh khách gửi lên hỏng / sai định dạng | Từ chối ở bước 1 (ingest), không để hỏng ở bước 6. |
| Model lén vẽ chữ vào khung hình | QC bước 9 quét khung hình bắt chữ lọt lưới. |

## Success Criteria

### Tiêu chí nghiệm thu theo mốc

| # | Tiêu chí | Đo thế nào | Mốc |
|---|---|---|---|
| S1 | Gọi API bằng `curl` → nhận về MP4 8 giây có giọng đọc tiếng Việt | Chạy tay, xem file | Cuối Sprint 1 |
| S2 | Video 30 giây nhiều cảnh, lời thoại khớp hình, chọn được 1 trong 4 định dạng | 5 video mẫu, không cái nào lệch tiếng > 200 ms | Cuối Sprint 2 |
| S3 | Video daily 40 giây một mạch, sản phẩm cài tự nhiên | 3 video mẫu, marketer chấm "không lộ quảng cáo" ≥ 4/5 | Cuối Sprint 3 |
| S4 | Khách tự làm được video trong NewsCMS, không cần dev | 1 người chưa từng dùng hoàn thành 1 video < 15 phút | Cuối Sprint 4 |
| S5 | Nhiều tenant chạy song song, biết chi phí đô la thật từng video | Query `ProviderCall` ra số tiền/video khớp hoá đơn ±5% | Cuối Sprint 5 |
| S6 | 2–3 khách thật dùng, có số liệu tỉ lệ regenerate theo định dạng | Báo cáo `FormatUsageStat` ≥ 50 job thật | Cuối Sprint 6 |

### Ngưỡng chất lượng và hiệu năng

- **Lệch tiếng:** voice-over và hình lệch nhau **< 200 ms** ở mọi mốc cắt shot.
- **Chuẩn âm lượng:** bản mix cuối chuẩn hoá về **−14 LUFS**; không clip.
- **Thời gian chờ:** tier Nháp trả kết quả **< 2 phút**; video đa cảnh 30 giây **2–5 phút**; daily 40 giây **8–15 phút**.
- **Nhãn AI:** 100% video giao đi có nhãn hiển thị + metadata. QC fail nếu thiếu — không có ngoại lệ.
- **Không chữ do model vẽ:** 0 video giao đi có chữ do model sinh (mọi chữ đều do FFmpeg vẽ).
- **Tính đúng tiền:** mỗi job có ít nhất một dòng `ProviderCall` cho mỗi lần gọi ra ngoài; tổng khớp hoá đơn provider ±5%.
- **Idempotency:** gửi cùng `Idempotency-Key` hai lần → một job, một lần tính tiền.

### Ngưỡng kinh doanh (đo ở Sprint 6, để định giá)

- Giá vốn thật trung bình cho video 30 giây tier Chuẩn: mục tiêu **≤ 200.000 đ** (dự toán 166.000 đ, hệ số retry 2,0×).
- Tỉ lệ regenerate thật theo từng định dạng — con số này thay thế hệ số 2,0× phỏng đoán.
- Tỉ lệ khách tải video về (proxy cho "video dùng được"): mục tiêu **≥ 60%** số job hoàn thành.

## Constraints & Assumptions

### Ràng buộc kỹ thuật

- **Model tối đa 15 giây một lần gọi.** Video dài hơn bắt buộc là nhiều shot ghép lại; "một mạch liên tục" phải giả lập bằng nối frame.
- **Không model nào viết đúng chữ tiếng Việt.** Mọi chữ trên màn hình do FFmpeg vẽ bằng font thật (D4).
- **Thoại do model sinh không dùng được** — sai dấu, sai trọng âm. Lời thoại luôn từ TTS riêng (D3).
- **Thứ tự 4→5→6 không đảo được**: sinh giọng → khoá thời lượng shot → render (D6).
- **Veo từ chối ảnh có mặt người** — ràng buộc chính sách, không phải kỹ thuật.
- **Video Veo chỉ nằm trên server Google 2 ngày** — worker phải tải về storage của mình ngay.
- **Vidu có hai endpoint tách biệt**: reference-to-video (đa chủ thể) và start-end-to-video (nối frame). Chọn cái này là mất cái kia.
- **Throughput bị chặn bởi rate limit của provider**, không phải CPU. Con số này phải đo thực nghiệm ở Sprint 0.
- **FFmpeg là binary ngoài** — phải có trong image của worker.
- **NewsCMS hiện không dùng Hangfire** — AdVideo.Worker tự mang theo, không thêm dependency vào CMS.

### Ràng buộc nghiệp vụ & pháp lý

- **Luật Trí tuệ nhân tạo 2025 hiệu lực từ 1/3/2026** — nghĩa vụ gắn nhãn AI thuộc về **bên triển khai** (chính dịch vụ này), không chuyển sang khách bằng điều khoản. Phạt tới 2 tỉ đồng với tổ chức.
- **Quảng cáo tại Việt Nam bị điều chỉnh chặt** — tuyên bố "số 1", "tốt nhất" cần căn cứ; dược/TPCN/mỹ phẩm có yêu cầu riêng.
- **Quyền hình ảnh cá nhân và giọng nói** — giọng nói là dữ liệu sinh trắc học, rủi ro nặng hơn hình ảnh. Mọi `talent` và mọi lần clone giọng phải có `ConsentRecord`.
- **Nhạc nền phải có giấy phép thương mại rõ ràng** — tránh model nhạc không công bố nguồn dữ liệu huấn luyện.

### Ràng buộc nguồn lực

- **1–2 dev, 9 tuần.** Không có QA riêng, không có designer riêng.
- **Mỗi lần chạy thử tốn tiền thật.** Test tự động không được gọi provider thật; chỉ smoke test có lịch mới gọi thật.

### Giả định (đã được chấp nhận, ghi rõ để sau này truy lại)

| # | Giả định | Nếu sai thì sao |
|---|---|---|
| A1 | Code AdVideo nằm trong repo `hailuunguoc` tại folder `AdVideo/`, solution riêng | Chỉ là chuyện đổi đường dẫn; kiến trúc không đổi |
| A2 | SQL Server, **database riêng** `AdVideoDb`, không dùng chung với NewsCMS | Nếu phải chuyển Postgres: JSON column mapping đổi, phần còn lại giữ nguyên |
| A3 | MinIO đã được dựng sẵn và ổn định; service chỉ cấu hình endpoint + access key | Phải thêm việc dựng storage vào Sprint 1 |
| A4 | Giá provider trong file HTML là ước tính từ trang so sánh công khai, **chưa xác minh** | Sprint 0 đo lại bằng hoá đơn thật; dự toán giá vốn có thể lệch |
| A5 | Hệ số retry 2,0× (và 2,8× cho daily) là con số mượn từ pipeline tương tự | Sprint 6 thay bằng số đo thật |
| A6 | ElevenLabs là nền tảng giọng cho bản thành phẩm; VieNeu-TTS cho bản nháp | Nếu VieNeu qua được cửa timing + license thì nó lên bản thành phẩm |
| A7 | Không tạo git worktree cho việc viết tài liệu này; worktree sẽ tạo khi bắt đầu Sprint 1 | — |

## Questions & Open Items

### Chặn việc mở cho khách ngoài (không chặn Sprint 1)

- [ ] **Pháp chế đọc Luật Trí tuệ nhân tạo 2025 và chốt hình thức nhãn AI.**
  Cần chốt: nhãn hiển thị trông thế nào, đặt ở đâu trên khung hình, có tồn tại suốt video hay chỉ ở đầu/cuối; metadata theo chuẩn nào (C2PA là khuyến khích, không rõ có bắt buộc); có ngoại lệ nào cho quảng cáo không.
  *Không chặn Sprint 1* — code pipeline được và để hình thức nhãn là cấu hình. **Chặn Sprint 6 (pilot với khách thật) và mọi việc mở ra ngoài.**
  Ghi chú: các nguồn tra được là bài phân tích, không phải văn bản luật gốc.

### Để sau pilot

- [ ] **Gói bán gồm những gì ngoài credit?** Quyền dùng tier Cao cấp, số brand kit, số giọng clone, thời gian lưu video, số job chạy song song — đây là **quyền sử dụng**, khác bản chất với số dư tiêu dần. Đừng nhồi cả hai vào một con số.
- [ ] **Gói ElevenLabs nào.** Không chặn kỹ thuật; Professional Voice Clone cần Creator trở lên.

### Cần nghiên cứu trong Sprint 0

- [ ] **Rate limit song song thật của từng tài khoản provider** — hỏi thẳng nhà cung cấp hoặc đo thực nghiệm. Đây là con số quyết định kiến trúc scale.
- [ ] **VieNeu-TTS có xuất được timing từng ký tự không?** Cửa cứng: không có thì chỉ dùng được cho tier Nháp.
- [ ] **License của *weights* VieNeu-TTS** (không phải của code — code là Apache 2.0). Nguồn của > 10.000 giờ dữ liệu huấn luyện.
- [ ] **Chất lượng VieNeu so với ElevenLabs** — nghe mù, 5 người marketing chấm, kịch bản có tên thương hiệu tiếng Việt.
- [ ] **Output có hợp gu khách Việt không** — chạy ảnh sản phẩm thật của một khách thật qua cả 4 provider. Mọi bảng giá là vô nghĩa nếu output không đạt.

### Đã chốt (không hỏi lại)

| Câu hỏi | Quyết định |
|---|---|
| Storage đặt ở đâu | **MinIO**, dựng riêng; service chỉ cấu hình và đẩy lên |
| Ngành hàng của khách | **Rất đa dạng** — không duyệt trước theo ngành được |
| Ai duyệt video | **Khách tự chịu trách nhiệm**; duyệt storyboard là khách tự kiểm chất lượng, không phải mình kiểm tuân thủ |
| Nhiều tỉ lệ khung hình | Khách chọn, mỗi lần xuất là một job riêng, mặc định 9:16 |
| Vị trí code | Folder `AdVideo/` trong repo này, solution riêng |
| Database | SQL Server, database riêng `AdVideoDb` |
