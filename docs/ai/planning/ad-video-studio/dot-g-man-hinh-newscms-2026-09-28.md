---
phase: planning
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
