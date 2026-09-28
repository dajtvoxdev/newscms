---
phase: planning
status: done-2026-09-28
title: "AdVideo — Đợt F: cấu hình qua app, lưu vào DB và MinIO"
description: Chuyển toàn bộ cấu hình vận hành (key provider, setting, descriptor, prompt, font, nhãn AI, ảnh sản phẩm) sang API quản trị ghi vào DB + MinIO, để app (NewsCMS admin) cấu hình trực tiếp thay vì sửa appsettings hay gõ CLI trên máy chủ
---

# Đợt F — Cấu hình qua app

> Kế hoạch tổng: [../2026-09-15-feature-ad-video-studio.md](../2026-09-15-feature-ad-video-studio.md) ·
> Trạng thái trước đợt này: [trang-thai-va-lo-trinh-2026-09-25.md](trang-thai-va-lo-trinh-2026-09-25.md)

## 1. Vì sao là đợt này, và vì sao bây giờ

Quyết định ngày 28/09: **mọi thứ người vận hành cần đổi sẽ được cấu hình trên app, lưu vào DB và
MinIO — không nằm trong `appsettings.json`**. Key ElevenLabs, ảnh sản phẩm thật của khách, font có
bản quyền, chữ trong nhãn AI đều là *dữ liệu nhập qua app*, không phải việc phải chờ trước khi code.

Hệ quả cho lộ trình:

- Năm mục "chặn ngoài code" ở mục 5 của file trạng thái **không còn chặn việc viết code**. Chúng chỉ
  chặn việc *dùng thật* (chạy spike, pilot), và lúc đó người vận hành tự nhập qua app.
- A5 (`appsettings.Production.json`) **thu hẹp lại**: chỉ còn những thứ phải có *trước khi đọc được
  DB* — chuỗi kết nối, endpoint + key MinIO, `KeyRingPath`, đường dẫn ffmpeg. Font rời khỏi danh sách
  đó. Những thứ này đi bằng biến môi trường (như `docker-compose.yml` đang làm), không cần file.
- Hiện hôm nay mọi cấu hình đi qua **CLI chạy trên máy chủ** (`set-credential`, `set-setting`,
  `set-descriptor`…). App không có cửa nào để gọi. Đợt F mở cửa đó.

Đợt F cũng chính là phần lớn của đợt "API cho mặt tiền" mà mục 4.E.2 của file trạng thái bắt phải chèn
trước Sprint 4: upload ảnh, danh sách job, huỷ job.

## 2. Nguyên tắc

1. **Bootstrap đi env, vận hành đi DB/MinIO.** Cái gì cần để *mở được DB* thì không thể nằm trong DB.
   Còn lại thì không được nằm ngoài DB.
2. **Hai loại danh tính, không trộn.** Tenant key (`X-AdVideo-Key`) chỉ thấy dữ liệu của mình.
   Operator key (`X-AdVideo-Operator-Key`) quản trị toàn hệ thống và **không** mang tenant nào. Không
   có key nào mở được cả hai cửa.
3. **CLI vẫn là gốc cấp quyền.** Operator key đầu tiên chỉ sinh được bằng CLI trên máy chủ — lý do ở
   `AdminCommands.cs` (vòng luẩn quẩn "endpoint cấp quyền cần được bảo vệ bằng quyền") vẫn đúng.
   Sau đó app làm mọi việc còn lại.
4. **CLI và API dùng chung một đường logic.** Tách phần ruột của lệnh CLI thành service; CLI và
   endpoint chỉ là hai lớp vỏ. Không để hai đường ghi credential với hai bộ quy tắc.
5. **Secret chỉ đi vào, không đi ra.** API nhận key provider và trả về bản che (`****abcd`); không có
   endpoint nào đọc lại key gốc. Tenant key và operator key chỉ in ra đúng một lần lúc sinh.
6. **Nhãn AI vẫn không tắt được (D9).** App đổi được *chữ* và *font*; không đổi được việc có nhãn.
   Chữ rỗng thì bị từ chối ở API và QC vẫn fail cứng.

## 3. Việc cần làm

### F0 — Lưới an toàn trước khi mở cửa quản trị (trả nợ Đợt B)

Mở endpoint ghi vào credential mà không có test API là mở cửa tiêu tiền không có khoá.

| # | Việc | Kiểm chứng |
|---|---|---|
| F0.1 | **B5**: provider giả báo cost ≠ 0 (cấu hình được) | Tổng `ProviderCall.CostUsd` = `ActualCostUsd`; một test trần chi phí với trần **khác 0** dừng job giữa chừng |
| F0.2 | **B3**: `ApiTestHost` dựng `Program` thật bằng `WebApplicationFactory` (SQLite, LocalDisk, Hangfire giả) | 401 thiếu/sai key, tenant tắt; 400 thiếu `Idempotency-Key`; 202; trả job cũ; 409; 422 |
| F0.3 | **B4**: cách ly tenant chiều phủ định qua HTTP | Tenant B `GET` job của tenant A → 404, không phải 200 |
| F0.4 | **B7**: workflow CI riêng cho AdVideo (có ffmpeg) | **Chờ quyết định**: `ci.yml` ghi rõ unit test bị bỏ khỏi CI theo yêu cầu ngày 07/09. Không tự bật lại; test chạy local trước commit |

### F1 — Operator key

| # | Việc |
|---|---|
| F1.1 | Entity `OperatorKey` (tên, prefix, SHA-256, `IsActive`, `LastUsedAt`, ghi chú) + migration |
| F1.2 | CLI `create-operator-key`, `revoke-operator-key`, `list-operator-keys` |
| F1.3 | `OperatorKeyAuthHandler` + hai policy `Tenant` / `Operator` gắn đúng scheme |
| F1.4 | Test: tenant key gọi `/v1/admin` → 401/403; operator key gọi `/v1/ad-videos` → 401/403 |

Key có tiền tố riêng `advop_` để một key lọt ra ngoài nhìn là biết nó quyền gì.

### F2 — API quản trị `/v1/admin/*`

| Nhóm | Endpoint | Ghi chú |
|---|---|---|
| Setting | `GET /settings` · `PUT /settings/{key}` | Kiểm kiểu + `MinValue`/`MaxValue`; không tạo khoá lạ |
| Credential | `GET /credentials` · `PUT /credentials/{provider}` · `POST /credentials/{provider}/deactivate` | Key che; logic chung với `set-credential` |
| Descriptor | `GET /descriptors` · `POST /descriptors` · `POST /descriptors/{provider}/versions/{v}/activate` · `POST /descriptors/{provider}/deactivate` · `POST /descriptors/preview` | Descriptor hỏng → 422 kèm đủ danh sách lỗi |
| Prompt | `GET /prompts` · `POST /prompts/{code}/versions` · `POST /prompts/{code}/versions/{v}/activate` | Kho prompt D7 đã có store, chỉ thiếu cửa |
| Tenant | `GET /tenants` · `POST /tenants` · `POST /tenants/{id}/rotate-key` · `POST /tenants/{id}/activate` · `/deactivate` | API key trả đúng một lần |
| Font nhãn | `POST /assets/label-font` (multipart) · `GET /assets/label-font` | Lưu MinIO bucket mới `adv-system`, khoá theo SHA-256; kiểm magic byte TTF/OTF |

### F3 — Worker đọc font và chữ nhãn từ DB/MinIO

| # | Việc |
|---|---|
| F3.1 | Setting `AiLabelFontObjectKey` → `LabelFontResolver` tải font từ `adv-system` về cache đĩa (tên file = SHA nên không cần làm mới cache); không có thì lùi về `AdVideo:Ffmpeg:FontFile`; không có cả hai thì bước 8 fail với lời chỉ đường tới API |
| F3.2 | Setting `AiLabelOverlayText` → `AiLabelStamper.Build(overlayText)` (sửa lệch #5 của file trạng thái). Seed "Nội dung tạo bằng AI", đánh dấu **tạm** chờ pháp chế |

### F4 — Ảnh sản phẩm qua MinIO + hai endpoint app cần

| # | Việc |
|---|---|
| F4.1 | `POST /v1/uploads` (multipart): kiểm magic byte JPEG/PNG/WebP, ≤ 15 MB, lưu `adv-uploads`, ghi `MediaAsset`; trùng SHA-256 trong cùng tenant thì trả asset cũ |
| F4.2 | Brief nhận `assets.product_image_ids`. API kiểm id thuộc tenant; bước 1 dùng thẳng object đã có, không tải lại |
| F4.3 | `GET /v1/ad-videos` (phân trang, lọc trạng thái) |
| F4.4 | `POST /v1/ad-videos/{id}/cancel` — logic huỷ đã có và có test ở worker, chỉ thiếu cửa |

### F5 — MinIO (A6) + test lớp storage thật (B8)

| # | Việc |
|---|---|
| F5.1 | `EnsureBucketAsync` gắn lifecycle: `adv-work` xoá sau 7 ngày. Quy tắc là hàm thuần trong Core |
| F5.2 | CORS: MinIO không có CORS theo bucket qua S3 API — đặt `MINIO_API_CORS_ALLOW_ORIGIN` trong compose + ghi `CONFIGURATION.md` |
| F5.3 | Test `MinioStorageService` với MinIO thật — bật bằng biến môi trường `ADVIDEO_TEST_MINIO_URL`, không có thì bỏ qua (không bắt cả bộ test phụ thuộc một container) |

## 4. Ngoài phạm vi

- **Trang admin NewsCMS** gọi các API trên — là Đợt G ngay sau (Sprint 4 thu gọn: màn hình cấu hình
  AdVideo + VideoStudio). Đợt F chốt hợp đồng API trước để G chỉ còn là UI.
- Webhook + HMAC, duyệt storyboard, regenerate, `GET /v1/formats` — đi cùng Sprint 2 (Đợt D).
- Hạn mức theo tenant (`TenantQuota`) — Sprint 5.

## 5. Kết quả (28/09)

| Việc | Trạng thái |
|---|---|
| F0.1–F0.3 | ✅ |
| F0.4 CI | ⏸ chờ quyết định |
| F1, F2, F3, F4 | ✅ |
| F5.1 lifecycle | ✅ test trên MinIO thật |
| F5.2 CORS | ✅ biến môi trường trong compose + tài liệu |
| F5.3 test MinIO thật | ✅ — bắt hai lỗi production (xem dưới) |

**Lỗi thật tìm ra trong đợt này** (đều đã sửa, có test giữ):

1. `MinioStorageService` **không dựng được**: gán `RegionEndpoint = null` sau `ServiceURL` làm SDK xoá
   `ServiceURL`. Mọi host `Provider = Minio` chết khi resolve storage.
2. Mọi upload lên MinIO **nổ sau khi đã gửi**: SDK tự đóng stream (`AutoCloseStream`), code đọc lại
   độ dài sau đó.
3. 401/403 trả `application/json` chứ không phải `application/problem+json` như comment nói.
4. `set-credential` đổi key thì **reset priority về 0**.
5. `"0,5"` trong setting số được đọc thành **5** (dấu phẩy = phân cách nghìn) — trần chi phí lệch 10×.
6. `CONFIGURATION.md` nói `appsettings.Production.json` "đã có sẵn" — file bị gitignore, chưa từng vào repo.

**Kiểm chứng ngoài test:** SQL Server 2022 + MinIO trong Docker, API và Worker chạy như hai tiến trình
thật. Qua HTTP: operator đổi chữ nhãn → tải font DejaVu lên → tạo tenant → tenant tải ảnh → tạo job
bằng `product_image_ids` → job `completed` sau ~13 giây → tải `download_url` → `ffprobe`: h264
1080×1920 + AAC + metadata AI; khung hình trích ra có nhãn "Video tạo bởi AI — CHU Kafe" đúng dấu.
Idempotency trả 200, huỷ job trong hàng đợi thì worker không chạy nó, huỷ job đã xong trả 409.

## 6. Đợt G — ngay sau

Màn hình trong NewsCMS admin gọi các API trên (Sprint 4 thu gọn):

- `IAdVideoAdminClient` (operator key) + `IAdVideoClient` (tenant key) trong `NewsCMS.Application`,
  cài đặt trong `NewsCMS.Infrastructure`; key lưu trong setting của NewsCMS, không trong appsettings.
- Trang **Cấu hình AdVideo** (quyền riêng, chỉ quản trị hệ thống): setting có cờ "tạm", credential
  (nhập key, chỉ thấy bản che), descriptor (dán + chạy khô + bật), prompt, font nhãn.
- Trang **VideoStudio**: tải ảnh (dùng lại module media của NewsCMS rồi đẩy sang `POST /v1/uploads`),
  brief, danh sách job, chi tiết + huỷ.

## 7. Xong khi

- Người vận hành có operator key làm được **mọi** việc mà CLI làm hôm nay, qua HTTP, trừ việc sinh
  operator key đầu tiên và `migrate`.
- Một job chạy trọn với ảnh upload qua `POST /v1/uploads`, font lấy từ MinIO, chữ nhãn lấy từ DB, mà
  `AdVideo:Ffmpeg:FontFile` để trống.
- Toàn bộ test xanh khi chạy local.
