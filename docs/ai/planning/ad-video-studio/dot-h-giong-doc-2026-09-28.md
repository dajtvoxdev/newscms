---
phase: planning
status: done-2026-09-28
title: "AdVideo — Đợt H: chọn giọng có sẵn và clone giọng riêng"
description: Khách chọn giọng đọc khi tạo video, hoặc clone giọng của chính họ từ ghi âm mẫu (có xác nhận của chủ giọng)
---

# Đợt H — Giọng đọc

> Trước đó: [dot-g-man-hinh-newscms-2026-09-28.md](dot-g-man-hinh-newscms-2026-09-28.md).

## 1. Vấn đề

- Pipeline gửi `VoiceId = "default"` cho engine TTS. ElevenLabs thật trả 404 cho chuỗi này, nên giọng
  đọc chưa bao giờ chạy được với key thật.
- Khách không chọn được giọng. Họ cũng không dùng được giọng của chủ cửa hàng hay người mẫu quen mặt.

## 2. Thiết kế

| Khái niệm | Ai tạo | Ai thấy |
|---|---|---|
| **Giọng có sẵn** (preset) | Quản trị nền tảng: thêm tay theo voice id, hoặc nhập từ thư viện giọng của engine | Mọi tenant |
| **Giọng clone** | Tenant: tải 1–5 file ghi âm (≤ 10 MB/file) kèm lời xác nhận của chủ giọng | Chỉ tenant đã tạo |

- Bảng `VoiceProfiles`: `TenantId` null nghĩa là giọng có sẵn. Khoá duy nhất `(Provider, ProviderVoiceId, TenantId)`
  không tính dòng đã xoá mềm.
- **Luật 3 vẫn giữ:** khách chọn *giọng*, không chọn engine. API cho khách không trả tên engine. Danh sách
  chỉ hiện giọng có engine đang bật.
- **Giọng mặc định:** giọng có sẵn đầu tiên (`sort_order` nhỏ nhất) của engine TTS được chọn theo tier.
  Engine chưa có giọng có sẵn nào thì bước 4 dừng với lời nhắc thêm giọng. Riêng engine giả vẫn dùng
  `"default"`.
- **Bằng chứng xác nhận:** lưu nguyên văn lời xác nhận, người xác nhận (tài khoản NewsCMS đang bấm) và
  thời điểm. Mẫu ghi âm lưu ở bucket `adv-voice` dưới `{tenant}/voices/{profile}/`.
- **Engine clone** lấy từ setting `VoiceCloneProvider`. Trần số giọng clone mỗi tenant lấy từ
  `MaxClonedVoicesPerTenant`, mặc định 5, vì mỗi giọng chiếm một slot trong gói engine.
- **Xoá giọng clone:** xoá bên engine (không chặn nếu lỗi), rồi xoá mềm. Job đã vào hàng đợi với giọng
  bị xoá thì dừng ở bước 4 với lý do rõ, không lặng lẽ đọc bằng giọng khác.
- **Thư viện engine:** khi nhập từ thư viện, bỏ ra các giọng clone của tenant. Chúng cũng nằm trong tài
  khoản engine nhưng không bao giờ được thành giọng dùng chung.

## 3. API

| Endpoint | Key | Việc |
|---|---|---|
| `GET /v1/voices` | tenant | Giọng clone của tenant trước, rồi giọng có sẵn. Có link nghe thử |
| `POST /v1/voices` (multipart) | tenant | Gửi `name`, `description`, `consent_statement` (≥ 10 ký tự), `consented_by`, `consent_confirmed=true`, `files` |
| `DELETE /v1/voices/{id}` | tenant | Chỉ xoá được giọng clone của chính mình |
| `POST /v1/ad-videos` | tenant | Thêm trường `voice.voice_profile_id`. Id lạ hoặc giọng clone của tenant khác → 400 |
| `GET/POST/PUT/DELETE /v1/admin/voices` | operator | Quản lý giọng có sẵn |
| `GET /v1/admin/voices/library?provider=` | operator | Thư viện giọng của engine (đã bỏ giọng clone), kèm cờ `already_added` |
| `GET /v1/admin/voices/cloned-stats` | operator | Số giọng clone theo tenant |

Engine hỗ trợ clone: ElevenLabs (Instant Voice Cloning, `POST /v1/voices/add`) và engine giả dùng cho test.

## 4. Màn hình NewsCMS

| Trang | Ai vào | Việc |
|---|---|---|
| VideoStudio → Tạo video | `VideoStudio.Video.Create` | Chọn giọng bằng nút radio, có nghe thử. Mặc định là "Mặc định" |
| VideoStudio → Giọng đọc | `VideoStudio.Voice.Manage` (permission mới) | Nghe giọng có sẵn. Clone giọng: tải file, ghi lời xác nhận, tích ô xác nhận. Xoá giọng clone |
| Cấu hình AdVideo → Giọng đọc | SuperAdmin | Bảng giọng có sẵn (sửa, bật/tắt, thứ tự, gỡ), thêm theo voice id, nhập từ thư viện engine, số giọng clone theo site |

## 5. Kiểm chứng

- AdVideo, test mới:
  - adapter ElevenLabs: multipart, key không lộ, đọc thư viện, xoá.
  - nhận dạng định dạng ghi âm.
  - API: danh sách, clone kèm bằng chứng, cách ly tenant, thiếu xác nhận, trần số giọng, xoá, tạo job có giọng, id sai dạng, quản trị, thư viện bỏ giọng clone.
  - pipeline: giọng clone chạy trọn; giọng của tenant khác bị chặn; giọng bị xoá sau khi tạo job.
- NewsCMS, test mới: client gửi `voice_profile_id`, clone multipart đủ trường, không có file thì không gọi mạng, hiện lỗi 422, xoá, quản trị bằng operator key.

**Kết quả (28/09):** AdVideo.Core.Tests 510, AdVideo.Tests 185 (3 skip — cần MinIO thật), NewsCMS.Tests 335 — tất cả xanh.
Chưa chạy thật với ElevenLabs (chưa có key); clone qua engine giả.
