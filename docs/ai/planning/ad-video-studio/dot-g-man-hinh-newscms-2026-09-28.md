---
phase: planning
status: done-2026-09-28
title: "AdVideo — Đợt G: màn hình cấu hình AdVideo và VideoStudio trong NewsCMS admin"
description: Sprint 4 thu gọn — NewsCMS admin gọi đúng các API của Đợt F; key, font, ảnh đều nhập qua màn hình
---

# Đợt G — Màn hình trong NewsCMS

> Trước đó: [dot-f-cau-hinh-qua-app-2026-09-28.md](dot-f-cau-hinh-qua-app-2026-09-28.md) (API đã chốt, có test).

## 1. Hai vai, hai loại key

| Vai | Ai | Key AdVideo | Nằm ở đâu trong NewsCMS |
|---|---|---|---|
| Quản trị nền tảng | `SuperAdmin` (như trang Sites) | **Operator key** — một cho cả hệ thống | Bảng `AdVideoConnections`, mã hoá DataProtection (khuôn `AiConnection`) |
| Người làm marketing của một site | Có quyền `VideoStudio.*` | **Tenant key** — mỗi site một cái | Bảng `SiteAdVideoTenants` (site-scoped), mã hoá DataProtection |

**Tenant key không bao giờ hiện ra màn hình.** SuperAdmin bấm "Kết nối site với AdVideo" → NewsCMS gọi
`POST /v1/admin/tenants` bằng operator key → cất key trả về (đã mã hoá) vào đúng site. Cấp lại key =
`rotate-key`, cũng không ai phải chép dán.

## 2. Việc cần làm

| # | Việc |
|---|---|
| G1 | `NewsCMS.Application/VideoStudio`: DTO + `IAdVideoAdminClient`, `IAdVideoClient`, `IAdVideoConnectionService` |
| G2 | `NewsCMS.Infrastructure/VideoStudio`: client HTTP dùng chung (đọc `problem+json` thành lỗi tiếng Việt), service kết nối, entity + cấu hình EF + migration |
| G3 | Permission `VideoStudio.Video.View/.Create/.Cancel` (seed qua `Permissions.All()`); trang cấu hình dùng `Roles = "SuperAdmin"` như các trang nền tảng |
| G4 | Trang **Cấu hình AdVideo** (SuperAdmin): kết nối + kiểm tra, liên kết site ↔ tenant, setting, credential, descriptor, prompt, nhãn AI (chữ + font) |
| G5 | Trang **VideoStudio** (theo site): danh sách job, tạo video (ảnh tải lên hoặc chọn từ thư viện media → `POST /v1/uploads`), chi tiết + xem/tải video + huỷ |
| G6 | Test: client HTTP với handler giả (header, lỗi, không lộ key), service kết nối trên InMemory; một lần chạy thật NewsCMS ↔ AdVideo |

`Idempotency-Key` sinh lúc mở form (trường ẩn): bấm hai lần hay F5 sau khi gửi không tạo hai job.

## 3. Ngoài phạm vi

Duyệt storyboard, render lại một shot, webhook + HMAC — chờ AdVideo có endpoint (Sprint 2).

## 4. Kết quả (28/09)

Tất cả G1–G6 xong. Màn hình:

| Trang | Đường dẫn | Ai vào |
|---|---|---|
| Kết nối & site | `/Admin/AdVideo` | SuperAdmin |
| Provider & key · Tham số vận hành · Nhãn AI · Prompt | `/Admin/AdVideo/{Providers,Settings,Label,Prompts}` | SuperAdmin |
| Video quảng cáo AI (danh sách, tạo, chi tiết) | `/Admin/VideoStudio` | quyền `VideoStudio.Video.View/Create/Cancel` |

**Kiểm chứng:** 20 test mới (325 test NewsCMS xanh). Chạy thật trên SQL Server 2022 + MinIO, AdVideo
API + Worker và NewsCMS Web là ba tiến trình riêng; Chromium (Playwright) đăng nhập `admin` vào
`phuphucyaka.io.vn` rồi làm đúng các bước của người quản trị: lưu địa chỉ + operator key → "Kiểm tra
kết nối" báo database/storage/queue ok → kết nối site Phú Phúc (tenant tạo bên AdVideo, key cất mã hoá
bên NewsCMS) → tải font DejaVu → đổi chữ nhãn → nạp key kling (trang không chứa key gốc) → tạo video
với ảnh tải lên → trang chi tiết tự cập nhật → "Đã xong" sau ~30 giây, có nút tải. Worker ghi đúng
nhãn "Video tạo bởi AI — Phú Phúc" và font từ MinIO.

**Lỗi có sẵn phát hiện khi dựng DB NewsCMS mới:** entity `KeoBiaChangelog` có trong model và snapshot
nhưng **không migration nào tạo bảng `KeoBiaChangelogs`** — `dotnet run -- --seed` trên DB trống nổ
`Invalid object name 'KeoBiaChangelogs'`. Không sửa trong đợt này (ngoài phạm vi); DB production chắc
đã có bảng tạo tay. Lần chạy thử tạo bảng bằng script sinh từ model.

## 5. Tiếp theo

- **Đợt C** — gọi provider thật: nạp key ở trang *Provider & key*, đổi `DefaultVideoProvider` /
  `StandardTtsProvider` ở *Tham số vận hành*, tải font có bản quyền ở *Nhãn AI*. Không còn bước nào cần
  SSH vào máy chủ ngoài `create-operator-key` lần đầu.
- **Đợt D** — Sprint 2 (đạo diễn LLM, duyệt storyboard): khi AdVideo có endpoint duyệt, thêm trang duyệt
  vào VideoStudio.
