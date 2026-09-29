---
phase: planning
status: done-2026-09-29
title: "ImageStudio — Đợt 1: nền móng — admin cấu hình model, người dùng tạo ảnh từ mô tả"
description: Model tạo ảnh là dữ liệu do SuperAdmin cấu hình; người dùng chọn model, tạo ảnh ở nền, đưa ảnh chọn được vào thư viện media có nhãn AI
---

# Đợt 1 — Nền móng

> Kế hoạch tổng: [../2026-09-29-feature-ai-image-studio.md](../2026-09-29-feature-ai-image-studio.md) ·
> Thiết kế: [../../design/2026-09-29-feature-ai-image-studio.md](../../design/2026-09-29-feature-ai-image-studio.md)

## 1. Đã làm

| Phần | Chỗ |
|---|---|
| Entity `ImageModel`, `ImageJob`, `ImageJobOutput`, `ImageProviderCall`, `ImageStudioSiteSettings`; thêm `Media.Origin` + `Media.AiJobId` | `NewsCMS.Domain/Entities/ImageStudio/`, `Content/Media.cs` |
| Cấu hình EF + migration `AddImageStudio` (unique `(SiteId, IdempotencyKey)`, `Status` là concurrency token) | `Persistence/Configurations/ImageStudioConfiguration.cs`, `Persistence/Migrations/…_AddImageStudio.cs` |
| Hợp đồng: DTO, `IImageStudioService`, `IImageModelService`, `IImageStudioSiteService`, `IImageProvider`, luật thuần hàm `ImageStudioRules` (chọn kích thước theo tỉ lệ, ghép prompt, luật URL) | `NewsCMS.Application/ImageStudio/` |
| Adapter `OpenAiImages` (OpenAI, 9Router, gateway tương thích) + `Fake` (chỉ bật khi `ImageStudio:EnableFakeProvider`) | `Infrastructure/ImageStudio/Providers/` |
| `ImageDownloadGuard`: tải ảnh kết quả dạng URL, chặn IP nội bộ **tại lúc mở socket**, không theo redirect, ≤ 20 MB, phải giải mã được | `Infrastructure/ImageStudio/Imaging/ImageDownloadGuard.cs` |
| `AiImageFinalizer`: xoá metadata của provider, ghi nhãn AI (XMP IPTC `DigitalSourceType` + EXIF `Software`), chặn bom giải nén | `Imaging/AiImageFinalizer.cs` |
| Hàng đợi `Channel` + `ImageJobWorker` (`MaxConcurrency`, nạp lại job `Queued` lúc khởi động, đánh lỗi job `Running` quá hạn — không tự chạy lại) + `ImageJobRunner` (mỗi biến thể một lần gọi song song, thử lại đúng 1 lần với 429/502/503/504) | `Infrastructure/ImageStudio/Jobs/` |
| `ImageStudioService`: kiểm quyền site, model, hạn mức (dưới khoá theo site), idempotency, chi phí ước tính; promote vào thư mục **Ảnh AI**; huỷ job đang chờ | `ImageStudioService.cs` |
| Quyền `ImageStudio.Image.View`, `ImageStudio.Image.Create` | `Permissions.cs` |
| Màn hình SuperAdmin: **Model tạo ảnh** (thêm/sửa/xoá mềm, "Chạy thử"), **Site & hạn mức** (bật/tắt, ảnh/tháng, ảnh/người/ngày, dùng tháng này) | `Areas/Admin/Pages/ImageStudio/Config/` |
| Màn hình người dùng: **Xưởng ảnh AI** (lịch sử, lọc, tìm, phân trang), **Chi tiết** (prompt thật, chi phí, "Lưu vào thư viện") | `Areas/Admin/Pages/ImageStudio/{Index,Detail}` |
| **Modal dùng chung** `window.imageStudio.open({...})` — chọn model dạng thẻ (như chọn giọng đọc của VideoStudio), tỉ lệ khung, số ảnh, chi phí ước tính, lượt còn lại; ảnh hiện ngay khi xong; "Dùng ảnh này" | `Shared/_ImageStudioModal.cshtml`, `wwwroot/js/admin/image-studio.js`, API JSON `ImageStudio/Api` |
| Sidebar: "Xưởng ảnh AI" (Marketing), "Cấu hình Xưởng ảnh" (Nền tảng) | `_Sidebar.cshtml` |

### Khác so với thiết kế ban đầu

- **Không thêm cờ phạm vi vào `AiConnection`.** Mọi chỗ chat chỉ dùng kết nối *mặc định*, nên kết nối chỉ dành cho ảnh không lọt vào chat. Model ảnh chọn kết nối trực tiếp.
- **Model không chia theo site** (chốt 29/09): mọi site thấy cùng danh sách model đang bật; SuperAdmin chỉ bật/tắt site và đặt hạn mức.
- **Mỗi lần gọi provider sinh đúng một ảnh**; nhiều biến thể thì gọi song song. Không phải gateway nào cũng nhận `n > 1`, và một biến thể lỗi không làm mất các biến thể khác.
- **API của modal nằm ở một trang riêng** (`/Admin/ImageStudio/Api?handler=…`) để trang bài viết và sản phẩm (Đợt 4, 5) gọi chung được.
- Site chưa có dòng cấu hình = **chưa bật** (tạo ảnh tốn tiền thật — bật chủ động như kết nối AdVideo).

## 2. Kiểm chứng (29/09)

**Test tự động — NewsCMS.Tests: 435/435** (350 cũ + 85 mới, `tests/NewsCMS.Tests/ImageStudio/`):

- Luật: chọn kích thước gần nhất theo tỉ lệ, đọc danh sách kích thước, luật URL (https / http localhost), ghép prompt, alt mặc định.
- Nhãn AI: ghi `DigitalSourceType` cho PNG, JPEG, WebP, **còn nguyên sau khi mã hoá lại** (bước tối ưu ảnh của thư viện media); xoá metadata provider gửi kèm; từ chối dữ liệu không phải ảnh.
- Che bí mật: key, `Bearer …`, `api_key=…`, chuỗi base64 dài; đọc thông báo lỗi các dạng JSON.
- Chặn SSRF: dải IP nội bộ (IPv4, IPv6, IPv4-mapped); từ chối http và IP nội bộ trước khi gửi request; host tin cậy được dùng http; không theo redirect; quá 20 MB; nội dung không phải ảnh.
- Adapter OpenAI: đường dẫn, header Bearer, body (`n = 1`, size, quality, output_format), tham số bổ sung không ghi đè được `model/prompt/n`, kết quả URL chỉ tin host của kết nối; ánh xạ lỗi 400 vi phạm nội dung / 401 / 429 / 502 / 400 — **không lộ key trong lỗi**; phản hồi hỏng; lỗi mạng.
- Service: site chưa bật, tạo job đúng size/chi phí/prompt thương hiệu, idempotency, validate, model tắt, trần biến thể, hạn mức tháng (tính cả ảnh đang chờ), hạn mức ngày theo người, form chỉ hiện model chạy được, job site khác không thấy, huỷ chỉ khi đang chờ, promote vào thư mục "Ảnh AI" có nhãn AI và bấm lại trả media cũ, không promote được ảnh site khác.
- Runner: đủ biến thể + một dòng sổ mỗi lần gọi, 429 thử lại một lần, lỗi nội dung không thử lại, thành công một phần, rác vẫn ghi chi phí nhưng không lưu, job không còn chờ thì bỏ qua, thiếu key báo lỗi dễ hiểu, Fake đúng hướng khung.
- Model service: validate kích thước/JSON, từ chối adapter chưa có code và kết nối http công cộng, một model mặc định, "Chạy thử" ghi sổ và lưu kết quả, xoá mềm.

**Chạy thật** trên SQL Server 2022 (Docker) + NewsCMS Web (Development) + một server giả theo chuẩn OpenAI
(`/v1/images/generations`, lần lẻ trả `b64_json`, lần chẵn trả `url`). Chromium (Playwright) đăng nhập
`admin` vào `phuphucyaka.io.vn`:

1. Tạo kết nối AI trỏ vào server giả; tạo model "GPT Image — chất lượng cao" (mặc định) và "Nháp nhanh (giả lập)".
2. "Chạy thử" → "Model chạy được", có ảnh xem trước; cột "Chạy thử gần nhất" ghi "Chạy được".
3. Bật site Phú Phúc, hạn mức 6 ảnh/tháng.
4. Xưởng ảnh AI → "Tạo ảnh": modal hiện hai model dạng thẻ, ước tính "2 × $0.04 ≈ $0.08", "Site còn 6 lượt".
   Tạo 2 ảnh 16:9 → server giả nhận `size = 1536x1024`, `n = 1`, `quality = high`, đúng key. Hai ảnh hiện
   trong modal (một từ base64, một tải qua URL với host tin cậy), còn 4 lượt.
5. "Dùng ảnh này" → `Medias` có `anh-ai-…-1.png`, `Origin = ai-generated`, thư mục "Ảnh AI", alt lấy từ mô tả.
6. Prompt bị server giả từ chối → job "Lỗi" với lời báo tiếng Việt, chi phí 0, sổ ghi `content_policy`.
7. Dùng hết 6 lượt → lần tạo tiếp bị chặn trước khi gọi provider; mở lại modal thì nút "Tạo ảnh" bị khoá kèm
   lý do "Site đã dùng hết lượt tạo ảnh của tháng này".
8. Trang chi tiết: "Lưu vào thư viện" chuyển thành "Đã trong thư viện".
9. Không có lỗi JavaScript. (Lỗi console còn lại là Google Fonts bị chặn bởi proxy của môi trường thử và ảnh
   sản phẩm mẫu của dữ liệu seed không có file.)

## 3. Lỗi có sẵn gặp lại khi dựng DB mới (không sửa ở đợt này)

- `KeoBiaChangelogs` không có migration (đã ghi ở Đợt G của AdVideo) — lần chạy thử tạo bảng bằng script sinh từ model.
- `KeoBiaMatches.StarType` có trong model nhưng không có trong migration: worker KeoBia báo `Invalid column name 'StarType'` lúc khởi động trên DB mới. Không ảnh hưởng Xưởng ảnh.

## 4. Để chạy với nhà cung cấp thật

1. **Kết nối AI** → thêm kết nối: Base URL gồm `/v1` (OpenAI: `https://api.openai.com/v1`; 9Router: `http://localhost:20128/v1`), nhập API key. **Không** đặt làm mặc định nếu kết nối này chỉ dùng cho ảnh.
2. **Cấu hình Xưởng ảnh → Model tạo ảnh** → thêm model: loại "Chuẩn OpenAI", chọn kết nối, model id (ví dụ `gpt-image-1` hoặc `cx/gpt-5.5-image` qua 9Router), giá ước tính. Gateway không nhận `output_format` thì thêm tham số bổ sung `{"output_format": null}`. Bấm "Chạy thử".
3. **Site & hạn mức** → bật site, đặt hạn mức.
4. Cấp quyền `ImageStudio.Image.View` / `ImageStudio.Image.Create` cho vai trò cần dùng (SuperAdmin có sẵn).

## 5. Tiếp theo

- **Đợt 2** — kho prompt mẫu (mặc định + tự viết + trend) **có ảnh demo**, "Cải thiện prompt", "Gợi ý từ nội dung".
- **Đợt 3** — tải ảnh mẫu, sửa theo vùng đánh số, adapter Gemini / fal.
