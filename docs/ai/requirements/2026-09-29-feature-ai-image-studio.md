---
phase: requirements
title: Requirements & Problem Understanding
description: Xưởng ảnh AI — tạo và sửa ảnh bằng AI ngay trong NewsCMS, dùng cho bài viết và sản phẩm
---

# Xưởng ảnh AI (ImageStudio) — Yêu cầu

> Thiết kế: [../design/2026-09-29-feature-ai-image-studio.md](../design/2026-09-29-feature-ai-image-studio.md)
> Kế hoạch: [../planning/2026-09-29-feature-ai-image-studio.md](../planning/2026-09-29-feature-ai-image-studio.md)

## Problem Statement

**Vấn đề.** Người soạn bài và người đăng sản phẩm trên NewsCMS cần ảnh: ảnh đại diện, ảnh minh hoạ trong
bài, ảnh sản phẩm nền trắng, ảnh bối cảnh. Hiện họ phải ra ngoài (Canva, ChatGPT, Photoshop), tải về rồi
tải lên thư viện media. Muốn sửa một chi tiết nhỏ (đổi màu áo, xoá logo, thay nền) thì phải làm lại cả ảnh.

**Hiện trạng trong repo.**
- Đã có tool `image_generate` (`NewsCMS.Infrastructure/Ai/Tools/ImageGenerationTool.cs`), chỉ dùng cho bot
  Telegram của theme KeoBia. Nó gọi `/v1/images/generations` qua 9Router, chỉ tạo ảnh từ chữ, không có màn
  hình, không lưu vào thư viện media và không ghi chi phí.
- Trợ lý AI trong trình soạn thảo (`ai-assist.js`, `article_chat`) sinh được tiêu đề, tóm tắt và nội dung,
  **nhưng không có ảnh**.
- Kho mẫu prompt theo trend đã chạy cho video (Đợt I của AdVideo). Ảnh chưa có kho tương tự.

**Ai bị ảnh hưởng.**
- *Biên tập viên / người viết bài*: cần ảnh bìa và ảnh minh hoạ nhanh, đúng chủ đề.
- *Người đăng sản phẩm*: có ảnh chụp thật bằng điện thoại, cần biến thành ảnh bán hàng (nền sạch, bối cảnh
  đẹp) mà **sản phẩm vẫn giữ nguyên**.
- *Quản trị nền tảng (SuperAdmin)*: cần kiểm soát model nào được dùng, chi phí mỗi site và nội dung mẫu.

## Goals & Objectives

### Mục tiêu chính

1. **G1. Tạo ảnh từ mô tả.** Người dùng nhập mô tả tự do (tiếng Việt) hoặc chọn mẫu, chọn model, tỉ lệ
   khung và số biến thể, rồi nhận ảnh trong thư viện media.
2. **G2. Admin cấu hình sẵn model, người dùng chọn.** Giống cách chọn giọng đọc ở VideoStudio:
   SuperAdmin thêm, bật hoặc tắt vài model tạo ảnh (OpenAI-compatible, Gemini, fal.ai…) trên màn hình,
   đặt tên và mô tả dễ hiểu; người dùng chọn một trong các model đó khi tạo ảnh. Không cần deploy. Mỗi model khai báo năng lực (tạo từ chữ, dùng ảnh tham
   chiếu, sửa theo mask), khung hình hỗ trợ và giá ước tính.
3. **G3. Kho prompt mẫu, tự cập nhật theo trend.** Có mẫu mặc định theo mục đích (ảnh bìa bài, ảnh sản
   phẩm, banner…), mẫu quản trị tự viết và mẫu AI sinh định kỳ theo xu hướng, qua cùng một bộ luật kiểm
   duyệt. **Mỗi mẫu có ảnh demo** để người dùng thấy trước kết quả. Người dùng luôn viết được prompt
   riêng và có nút "Cải thiện prompt".
4. **G4. Sửa ảnh có sẵn.** Tải ảnh mẫu lên, hoặc chọn từ thư viện, để làm ảnh tham chiếu hay ảnh gốc
   cần sửa.
5. **G5. Đánh dấu vùng cần sửa theo số thứ tự.** Người dùng khoanh vùng trên ảnh (hình chữ nhật hoặc cọ
   vẽ). Mỗi vùng tự nhận số **#1, #2, #3…** và có một ô chỉ dẫn riêng ("#1 đổi áo sang màu đỏ", "#2 xoá
   logo"). Một vùng có thể đánh dấu **Giữ nguyên** để bảo vệ, ví dụ giữ sản phẩm khi đổi nền. **Phần ngoài
   vùng sửa được giữ nguyên từng điểm ảnh.**
6. **G6. Tích hợp vào bài viết.** Tạo ảnh đại diện, chèn ảnh AI vào nội dung, sửa ảnh đang có trong bài.
   Trợ lý AI soạn được **bài hoàn chỉnh có ảnh** (ảnh bìa và ảnh minh hoạ trong bài), lưu ở dạng nháp để
   người dùng duyệt.
7. **G7. Tích hợp vào sản phẩm.** Tạo ảnh đại diện sản phẩm và ảnh gallery. Chế độ "từ ảnh chụp thật"
   đổi nền hoặc bối cảnh nhưng giữ nguyên sản phẩm.

### Mục tiêu phụ

- **G8.** Biết chi phí: mỗi lần gọi provider được ghi sổ, có hạn mức theo site và theo người dùng.
- **G9.** Gắn nhãn AI: metadata nhúng trong file và chú thích hiển thị. Theo cùng nghĩa vụ đã ghi ở rủi ro
  R4 của AdVideo.
- **G10.** Đưa tool Telegram `image_generate` về dùng chung lớp provider mới, để chỉ còn một chỗ cấu hình.

### Ngoài phạm vi (lần này)

- Xoá nền tự động bằng model tách nền riêng. Lần này dùng vùng "Giữ nguyên" do người dùng khoanh. Tách nền
  tự động để giai đoạn sau.
- Vẽ chữ tiếng Việt lên ảnh bằng model. Model vẽ dấu tiếng Việt sai gần như chắc chắn (xem R9 của
  AdVideo). Mẫu mặc định yêu cầu "không có chữ trong ảnh".
- C2PA / Content Credentials ký số.
- Tạo ảnh hàng loạt cho nhiều sản phẩm một lúc. Để giai đoạn sau, sau khi có số liệu chi phí thật.
- Tự động đăng bài. Bài có ảnh luôn lưu ở trạng thái **Nháp**.

## User Stories

| # | Là… | Tôi muốn… | Để… |
|---|---|---|---|
| U1 | Biên tập viên | bấm "Tạo bằng AI" ngay dưới ô Ảnh đại diện, AI gợi ý prompt từ tiêu đề và tóm tắt | có ảnh bìa đúng chủ đề trong một phút |
| U2 | Biên tập viên | chèn ảnh AI vào vị trí con trỏ trong TinyMCE, kèm alt và chú thích | bài có ảnh minh hoạ giữa các đoạn |
| U3 | Biên tập viên | bảo trợ lý AI "viết bài về X, kèm 1 ảnh bìa và 2 ảnh minh hoạ" | nhận bài hoàn chỉnh, duyệt rồi lưu nháp |
| U4 | Biên tập viên | chọn một ảnh trong bài, bấm "Sửa bằng AI", khoanh vùng #1 và ghi "xoá logo" | sửa đúng chỗ, không làm hỏng phần còn lại |
| U5 | Người đăng sản phẩm | tải ảnh chụp sản phẩm, khoanh sản phẩm là "Giữ nguyên", chọn mẫu "Nền trắng TMĐT" | có ảnh bán hàng mà nhãn và bao bì không bị méo |
| U6 | Người đăng sản phẩm | thêm 3 ảnh bối cảnh (lifestyle) vào gallery sản phẩm | trang sản phẩm có nhiều góc nhìn |
| U7 | Bất kỳ ai có quyền tạo | xem ảnh demo của từng mẫu, chọn mẫu "Đang trend", hoặc viết prompt riêng rồi bấm "Cải thiện prompt" | không phải tự nghĩ prompt từ đầu |
| U8 | Bất kỳ ai có quyền tạo | xem trước chi phí ước tính và số lượt còn lại trong tháng | không vượt hạn mức |
| U9 | Bất kỳ ai có quyền tạo | "Sửa tiếp từ ảnh này" sau mỗi lần sửa, xem lịch sử phiên bản | sửa dần từng bước, quay lại bản trước được |
| U10 | SuperAdmin | thêm model mới (dán base URL, key, model id, năng lực, giá) và bấm "Chạy thử" | đổi nhà cung cấp mà không cần deploy |
| U11 | SuperAdmin | bật tự cập nhật mẫu ảnh theo trend, duyệt mẫu trước khi hiện | kho luôn mới mà vẫn kiểm soát nội dung |
| U12 | SuperAdmin | đặt hạn mức ảnh/tháng cho từng site, xem chi phí theo site và model | không thủng ví |

## Success Criteria

- **Tạo từ chữ:** từ lúc bấm "Tạo" tới khi thấy ảnh ≤ 60 giây (P90) với model mặc định. Trang không treo
  trong lúc chờ.
- **Sửa theo vùng:** với chế độ "Giữ nguyên tuyệt đối phần ngoài vùng" (mặc định bật), **100% điểm ảnh
  ngoài mask (trừ viền mềm) giống hệt ảnh gốc**. Có test tự động kiểm điều này.
- **Bài hoàn chỉnh:** từ một yêu cầu trong khung chat ra bài nháp có ảnh bìa và ≥ 1 ảnh trong bài. Không
  còn ô ảnh chờ nào bị lưu vào DB.
- **Không phụ thuộc một provider:** tối thiểu 2 adapter chạy thật (OpenAI-compatible và một adapter khác).
  Đổi model mặc định trên màn hình là có hiệu lực ngay.
- **Chi phí:** mọi lần gọi provider có một dòng `ImageProviderCall`. Tạo vượt hạn mức bị chặn **trước**
  khi gọi provider.
- **An toàn:** key không bao giờ hiện ra màn hình hay log. Ảnh tải lên được giải mã lại bằng ImageSharp
  (không tin phần mở rộng file). URL ảnh do provider trả về được tải qua lớp chặn SSRF.
- **Test:** NewsCMS.Tests xanh toàn bộ. Có e2e Playwright cho trình đánh dấu vùng.

## Constraints & Assumptions

- **Kiến trúc NewsCMS:** Domain → Application → Infrastructure → Web. Razor Page không query EF. Quyền
  dạng `Module.Subject.Action`. Trang cấp nền tảng dùng `Roles = "SuperAdmin"` như các trang AdVideo và
  Sites hiện có.
- **Làm ở NewsCMS, không ở AdVideo.** Ảnh là việc ngắn (5–120 giây), kết quả phải vào thư viện media theo
  site, và mọi thứ cần dùng (kết nối AI, skill, tool tìm web, kho prompt, storage) đã có ở NewsCMS. Lý do
  chi tiết ở tài liệu thiết kế, mục D1.
- **Storage:** dùng `IFileStorage` hiện có (local `wwwroot/uploads`). Không thêm MinIO cho ảnh.
- **Hàng đợi:** NewsCMS không có Hangfire. Dùng `Channel` + `BackgroundService` như `VideoCompressionQueue`,
  nhưng **job lưu trong DB** để không mất khi restart.
- **Model id, giá, giới hạn kích thước là dữ liệu trong DB** (theo D10 của AdVideo). Các con số trong tài
  liệu là ví dụ; admin nhập giá trị thật ở màn hình. Không có đợt spike riêng (chốt 29/09).
- Giả định có ít nhất một key thật (9Router đang chạy cho Telegram, hoặc OpenAI/Gemini/fal) để chạy thật.

## Questions & Open Items

| # | Câu hỏi | Ảnh hưởng |
|---|---|---|
| Q1 | Ngoài 9Router, có định mua key OpenAI, Gemini hoặc fal.ai không? *(29/09: không chặn — admin tự thêm model ở màn hình)* | Thứ tự viết adapter ở Đợt 3 |
| Q2 | Hạn mức mặc định cho mỗi site: bao nhiêu ảnh/tháng, bao nhiêu ảnh/người/ngày? *(29/09: **theo gói khách mua** — NewsCMS chưa có khái niệm gói; cần chốt Q10)* | Đợt 6 |
| Q3 | Nhãn AI hiển thị: chú thích dưới ảnh trong bài là đủ, hay cần watermark trên ảnh? *(29/09: **chú thích là đủ**, không watermark; metadata AI trong file vẫn luôn ghi)* | Đợt 4, 6 |
| Q4 | Kho mẫu: chỉ dùng chung toàn hệ thống, hay cho mỗi site có mẫu riêng? *(29/09: **dùng chung toàn hệ thống** — không làm mẫu riêng theo site)* | Đợt 2 |
| Q5 | Ảnh có người thật do khách tải lên để sửa: bắt buộc tick xác nhận quyền sử dụng? (đề xuất: có) | Đợt 3 |
| Q6 | Vai trò nào được dùng Xưởng ảnh mặc định? *(29/09: **SuperAdmin dùng toàn bộ; vai trò khác do quản trị cấp** ở trang Vai trò — nhóm "ImageStudio" đã có sẵn, không seed cấp sẵn)* | Triển khai |
| Q7 | Ảnh tạo ra mà không chọn đưa vào thư viện: tự dọn sau 14 ngày — giữ con số này? *(29/09: **giữ 14 ngày**, vẫn đổi được qua `ImageStudio:OutputRetentionDays`)* | Đợt 6 |
| Q8 | "Bài hoàn chỉnh có ảnh": mặc định bao nhiêu ảnh? *(29/09: **AI quyết theo nội dung bài, không đặt giới hạn cứng** — chỉ hạn mức của gói giới hạn; người dùng thấy chi phí và bỏ bớt ảnh đề xuất được trước khi tạo)* | Đợt 4 |
| Q9 | Lỗi có sẵn ngoài Xưởng ảnh: `KeoBiaChangelogs` và cột `KeoBiaMatches.StarType` không có migration — DB mới dựng bị lỗi. Có muốn sửa (thêm migration) trong một việc riêng không? | Việc riêng |
| Q10 | Gói dịch vụ (theo Q2): làm luôn trong NewsCMS một danh mục **gói** do SuperAdmin tạo (tên, số ảnh/tháng, ảnh/người/ngày, sau này thêm hạn mức video) và gán gói cho từng site — không tích hợp thanh toán — hay chờ hệ thống bán gói/thanh toán riêng? Hạn mức tính theo **site** (khách = site) hay theo **tài khoản**? | Đợt 6 (có thể kéo lên sớm) |
