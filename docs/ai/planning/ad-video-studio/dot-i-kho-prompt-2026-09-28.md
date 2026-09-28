---
phase: planning
status: done-2026-09-28
title: "AdVideo — Đợt I: kho mẫu prompt cho form tạo video, tự cập nhật theo xu hướng"
description: Người làm marketing chọn mẫu brief có sẵn thay vì tự viết; kho có mẫu mặc định và mẫu AI sinh theo trend định kỳ
---

# Đợt I — Kho mẫu prompt

> Trước đó: [dot-h-giong-doc-2026-09-28.md](dot-h-giong-doc-2026-09-28.md).

## 1. Bài toán

Form tạo video bắt người dùng tự viết *cảnh quay* và *lời thoại*. Người làm marketing của cửa hàng nhỏ
thường không biết bắt đầu từ đâu, và không theo kịp kiểu video đang được xem nhiều.

## 2. Thiết kế

**Kho nằm ở NewsCMS, không ở AdVideo.** Mẫu chỉ là thứ điền sẵn vào form. Việc sinh mẫu cần LLM và tìm
web, mà NewsCMS đã có sẵn: kết nối AI, skill AI lưu trong DB, công cụ 9Router/Firecrawl. AdVideo chưa có
LLM (đạo diễn LLM thuộc Sprint 2). `PromptTemplate` bên AdVideo là prompt *hệ thống* (negative prompt,
đạo diễn), không liên quan.

| Thành phần | Chỗ |
|---|---|
| Bảng `VideoPromptTemplates`: dùng chung mọi site, xoá mềm. Nguồn: mặc định / tự viết / trend. Trạng thái: đang hiện / chờ duyệt / ẩn. Mẫu trend có hạn dùng | NewsCMS |
| Bảng `VideoPromptLibrarySettings`: một dòng, sửa trên màn hình. Gồm bật/tắt, chu kỳ, số mẫu mỗi lần, hạn dùng, duyệt trước, trọng tâm | NewsCMS |
| Bảng `VideoPromptTrendRuns`: lịch sử chạy (thêm/loại/ẩn, có tìm web không, lý do) | NewsCMS |
| 12 mẫu mặc định theo ngành, seed kiểu "chỉ thêm, không ghi đè" | `VideoPromptLibrarySeeder` |
| Skill AI `videostudio_trend_templates`: prompt hệ thống chứa hợp đồng JSON, `UseTools = true`. Quản trị sửa được ở trang AI | seed |
| `VideoPromptTrendWorker`: 15 phút hỏi một lần "đến lịch chưa". Lịch tính từ lần chạy gần nhất, kể cả lần lỗi, để AI hỏng thì không bị gọi liên tục | `BackgroundService` |

**Mẫu AI sinh ra không được tin mặc định.** Mỗi mẫu phải qua `VideoPromptTemplateRules`, cùng bộ luật
áp cho mẫu quản trị tự viết:
- Cụm quảng cáo tuyệt đối hoặc cam kết: "tốt nhất", "số 1", "100%", "cam kết"… Không chặn "nhất" đứng
  một mình, vì "thống nhất", "nhất định" là tiếng Việt bình thường.
- Ngành không làm quảng cáo tự động: rượu bia, thuốc lá, cờ bạc…
- Khung hình và thời lượng phải nằm trong khoảng AdVideo nhận.

Mẫu trend còn phải qua thêm hai luật:
- Lời thoại phải có chỗ giữ `{san_pham}`.
- Tiêu đề không trùng mẫu đã có (so sánh sau khi bỏ dấu).

Link nguồn chỉ giữ http(s), tối đa 5 link. Lý do loại từng mẫu được ghi vào lần chạy.

**Chỗ giữ `{san_pham}`:** form thay bằng Tên sản phẩm ngay khi chọn mẫu, và server thay thêm lần nữa lúc
gửi. Nếu chưa nhập tên, server báo lỗi, không để giọng đọc đọc thành tiếng chữ "{san_pham}".

**Không bao giờ chạy chồng:** có khoá trong tiến trình, và kiểm tra trong DB xem có lần chạy dở nào dưới
30 phút không.

## 3. Màn hình

| Trang | Ai | Việc |
|---|---|---|
| VideoStudio → Tạo video | `VideoStudio.Video.Create` | Khối "Bắt đầu từ mẫu có sẵn": tìm theo chữ, lọc "Đang trend" hoặc theo ngành, bấm "Dùng mẫu này" để điền cảnh quay, lời thoại, khung hình, thời lượng, có người. Video tạo thành công thì cộng lượt dùng cho mẫu |
| Cấu hình AdVideo → Kho mẫu video | SuperAdmin | Cấu hình tự cập nhật, nút "Cập nhật ngay", lịch sử chạy, bảng mẫu (lọc theo nguồn và trạng thái, duyệt/ẩn/hiện/sửa/xoá), thêm và sửa mẫu |

## 4. Kiểm chứng (28/09)

- 15 test mới: luật nội dung, seed chạy lại không nhân bản và không ghi đè, form chỉ thấy mẫu đang hiện
  và chưa hết hạn, cập nhật trend (lưu mẫu đạt, loại mẫu sai kèm lý do, duyệt trước, ẩn mẫu hết hạn, AI
  lỗi hoặc trả rác, lịch). NewsCMS.Tests: 350/350.
- Chạy thật trên SQL Server 2022, dùng một server giả theo chuẩn OpenAI thay cho model:
  - Migration và seed có 12 mẫu.
  - Chromium tạo kết nối AI, bấm "Cập nhật ngay": thêm 2 mẫu, loại 1 mẫu vì "tốt nhất"/"100%".
  - Request gửi AI có danh sách tiêu đề đã có.
  - Form tạo video lọc được "Đang trend". "Dùng mẫu này" điền "Bánh mì Hùng" vào chỗ `{san_pham}`, đặt
    9:16 / 12 giây / có người.
  - Gửi form khi chưa nhập tên sản phẩm thì bị chặn với lời nhắc.
  - Không có lỗi JavaScript.

## 5. Để có xu hướng thật

1. Trang **Kết nối AI**: tạo kết nối mặc định (OpenAI hoặc tương thích).
2. Trang **Kỹ năng AI**: nạp key cho `tool_firecrawl_search` hoặc 9Router search rồi bật. Không có công
   cụ tìm web thì mẫu chỉ dựa trên hiểu biết của model, và lần chạy ghi rõ điều đó.
3. **Cấu hình AdVideo → Kho mẫu video**: bật tự cập nhật, chọn chu kỳ. Nên bật "duyệt trước" trong vài
   tuần đầu.
