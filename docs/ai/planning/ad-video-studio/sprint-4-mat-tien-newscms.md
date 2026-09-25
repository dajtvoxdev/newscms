---
phase: planning
sprint: 4
title: "Sprint 4 — Mặt tiền trong NewsCMS (Tuần 7)"
description: Module Marketing/VideoStudio trong admin NewsCMS — khách tự làm video, không cần dev
---

# Sprint 4 — Mặt tiền trong NewsCMS

> Kế hoạch tổng: [../2026-09-15-feature-ad-video-studio.md](../2026-09-15-feature-ad-video-studio.md)
> Thiết kế: [../../design/2026-09-15-feature-ad-video-studio.md](../../design/2026-09-15-feature-ad-video-studio.md)

## Mục tiêu

Người làm marketing đăng nhập NewsCMS, tải ảnh sản phẩm, gõ mô tả bằng tiếng Việt thường, chọn định dạng, bấm nút, **xem từng cảnh hiện ra dần**, tải video về.

Không đụng tới `AdVideo/` ở sprint này ngoài việc thêm webhook nếu thiếu. Đây là sprint về phía NewsCMS.

## Definition of Done

- [ ] Module `Marketing/VideoStudio` trong Areas/Admin, vào được từ menu
- [ ] Permission `Marketing.AdVideo.View/.Create/.Approve/.Delete` khai trong `Permissions.cs` và seed qua `DbSeeder`
- [ ] Trang tạo video: tải ảnh, nhập brief, chọn định dạng, chọn tỉ lệ khung, chọn tier
- [ ] Trang theo dõi: tiến độ theo bước, **hiện thumbnail từng shot ngay khi xong**
- [ ] Trang duyệt storyboard: xem, sửa lời thoại, duyệt hoặc huỷ
- [ ] Danh sách video đã làm, xem lại, tải về
- [ ] `IAdVideoClient` trong `NewsCMS.Application`, cài đặt trong `NewsCMS.Infrastructure`
- [ ] Webhook từ AdVideo về NewsCMS, xác thực HMAC
- [ ] Người không có quyền không thấy menu và bị chặn ở endpoint
- [ ] **Một người marketing thật làm được một video mà không cần hỏi dev**

## Điều kiện vào

| Cần có | Từ đâu |
|---|---|
| API ổn định, không còn đổi hợp đồng | Sprint 2 + 3 |
| Endpoint duyệt storyboard | Sprint 2 |
| Webhook `job.shot_completed` | Sprint 2 |
| Định dạng daily chạy được | Sprint 3 |
| AdVideo chạy được ở môi trường NewsCMS gọi tới được | Hạ tầng |

## Task Breakdown

### T4.1 — Client gọi AdVideo từ NewsCMS

- [ ] `IAdVideoClient` trong `NewsCMS.Application/VideoStudio/`
- [ ] Phương thức: `CreateJobAsync`, `GetJobAsync`, `ListJobsAsync`, `ApproveStoryboardAsync`, `UpdateStoryboardAsync`, `RegenerateShotAsync`, `GetDownloadUrlAsync`
- [ ] Cài đặt `AdVideoClient` trong `NewsCMS.Infrastructure/VideoStudio/` dùng `HttpClient` + Polly
- [ ] Gắn `X-AdVideo-Key` và `Idempotency-Key` tự động
- [ ] Cấu hình `AdVideoOptions`: base URL, key, timeout
- [ ] Đăng ký DI theo đúng cách các module khác trong repo làm

**Files tạo:**
```
NewsCMS.Core/src/NewsCMS.Application/VideoStudio/IAdVideoClient.cs
NewsCMS.Core/src/NewsCMS.Application/VideoStudio/Models/*.cs
NewsCMS.Core/src/NewsCMS.Infrastructure/VideoStudio/AdVideoClient.cs
NewsCMS.Core/src/NewsCMS.Infrastructure/VideoStudio/AdVideoOptions.cs
```

> `Idempotency-Key` sinh ở phía NewsCMS và lưu vào session của form — người dùng bấm hai lần hoặc F5 giữa chừng thì không tạo hai job. Đây là loại lỗi tốn tiền thật.

### T4.2 — Permission

- [ ] Thêm lớp lồng `Marketing` vào `NewsCMS.Shared/Constants/Permissions.cs` theo đúng convention `Module.Subject.Action`
- [ ] 4 mã: `Marketing.AdVideo.View`, `.Create`, `.Approve`, `.Delete`
- [ ] Thêm vào `Permissions.All()` để `DbSeeder` seed
- [ ] Gán mặc định cho vai trò quản trị

**Files sửa:**
```
NewsCMS.Core/src/NewsCMS.Shared/Constants/Permissions.cs
NewsCMS.Core/src/NewsCMS.Infrastructure/Persistence/Seed/DbSeeder.cs
```

> Tách `.Create` và `.Approve` là có chủ đích: người viết brief và người duyệt storyboard thường là hai người khác nhau, và duyệt là lúc tiền bắt đầu tiêu.

### T4.3 — Trang tạo video

- [ ] Razor Page `Areas/Admin/Pages/VideoStudio/Create.cshtml`
- [ ] Tải ảnh sản phẩm — **dùng lại module media có sẵn của NewsCMS**, không viết uploader mới
- [ ] Ô nhập brief tiếng Việt, có gợi ý viết thế nào cho tốt
- [ ] Chọn định dạng — nạp từ `GET /v1/formats`, hiện mô tả và thời lượng
- [ ] Chọn tỉ lệ khung: dọc 9:16, vuông 1:1, ngang 16:9
- [ ] Chọn tier: Nháp (nhanh, rẻ) / Thành phẩm
- [ ] Bật/tắt duyệt storyboard — **mặc định bật**
- [ ] Ước tính thời gian chờ hiện ngay trên form, **daily ghi rõ là lâu hơn nhiều**

**Files tạo:**
```
NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/VideoStudio/Create.cshtml
NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/VideoStudio/Create.cshtml.cs
```

> Form **không có ô chọn provider**. Khách chọn *tier* và *có người xuất hiện hay không*; hệ thống tự chọn provider. Bắt người làm marketing chọn giữa Veo và Kling là bắt họ học thứ không liên quan đến việc của họ.

### T4.4 — Trang theo dõi tiến độ

- [ ] `Areas/Admin/Pages/VideoStudio/Detail.cshtml`
- [ ] Hiện bước hiện tại theo ngôn ngữ người dùng hiểu ("Đang viết kịch bản", "Đang thu giọng đọc", "Đang dựng cảnh 2/4")
- [ ] **Thumbnail từng shot hiện ngay khi shot đó xong** — không đợi cả video
- [ ] Tự làm mới bằng polling (đơn giản, đủ dùng) hoặc SSE nếu repo đã có sẵn hạ tầng
- [ ] Lỗi hiện lý do đọc được, không hiện mã lỗi provider
- [ ] Nút render lại một shot khi video đã xong

**Files tạo:**
```
NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/VideoStudio/Detail.cshtml
NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/VideoStudio/Detail.cshtml.cs
NewsCMS.Core/src/NewsCMS.Web/wwwroot/js/video-studio.js
```

> Hiện từng shot khi xong là cách giữ chân người dùng trong 8–15 phút chờ. Thanh tiến độ trơn không có gì để xem thì người ta đóng tab và tưởng hệ thống hỏng.

### T4.5 — Trang duyệt storyboard

- [ ] Hiện storyboard: từng shot có mô tả hình + lời thoại
- [ ] **Sửa được lời thoại** trước khi duyệt
- [ ] Nút "Duyệt và dựng video" / "Huỷ"
- [ ] Cần quyền `Marketing.AdVideo.Approve`
- [ ] Cảnh báo rõ: sau khi duyệt là bắt đầu tiêu tiền
- [ ] Hiện thời hạn chờ 24 giờ

**Files tạo:**
```
NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/VideoStudio/Storyboard.cshtml
NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/VideoStudio/Storyboard.cshtml.cs
```

### T4.6 — Danh sách video

- [ ] `Index.cshtml` — bảng video đã làm, lọc theo trạng thái và định dạng
- [ ] Xem trước ngay trong trang
- [ ] Tải về qua presigned URL
- [ ] Xoá (cần `.Delete`)
- [ ] Phân trang theo đúng cách các trang danh sách khác trong repo làm

**Files tạo:**
```
NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/VideoStudio/Index.cshtml
NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Pages/VideoStudio/Index.cshtml.cs
```

### T4.7 — Webhook AdVideo → NewsCMS

- [ ] Endpoint nhận webhook trong NewsCMS
- [ ] **Xác thực HMAC-SHA256** qua header `X-AdVideo-Signature` — từ chối nếu sai
- [ ] Xử lý sự kiện: `job.completed`, `job.failed`, `job.awaiting_approval`, `job.shot_completed`
- [ ] Gửi thông báo cho người tạo job khi xong hoặc lỗi
- [ ] Chịu được webhook trùng (AdVideo có thể gửi lại)

**Files tạo:**
```
NewsCMS.Core/src/NewsCMS.Web/Areas/Admin/Controllers/AdVideoWebhookController.cs
NewsCMS.Core/src/NewsCMS.Infrastructure/VideoStudio/WebhookSignatureValidator.cs
```

> Xác thực chữ ký **bắt buộc**, không phải "sau này thêm". Endpoint webhook không có xác thực là cửa cho người ngoài đánh dấu job hoàn thành với link tải do họ chọn.

### T4.8 — Menu và điều hướng

- [ ] Thêm mục "Xưởng video" vào menu admin, dưới nhóm Marketing
- [ ] Ẩn menu nếu không có `Marketing.AdVideo.View`
- [ ] Theo đúng cách các module khác đăng ký menu trong repo

**Files sửa:** file cấu hình menu admin hiện có

### T4.9 — Test

- [ ] Unit: `AdVideoClient` với `HttpClient` giả — kiểm tra header, idempotency, xử lý lỗi
- [ ] Unit: `WebhookSignatureValidator` — chữ ký đúng, sai, thiếu, quá hạn
- [ ] Integration: tạo job từ UI → AdVideo nhận đúng payload
- [ ] Kiểm tra quyền: người thiếu `.Create` bị chặn; người thiếu `.Approve` không duyệt được
- [ ] Kiểm tra UI thủ công trên trình duyệt thật, cả màn hình nhỏ

### T4.10 — Người thật dùng thử

- [ ] Một người làm marketing **chưa từng nghe về dự án** tự làm một video
- [ ] Không được hỏi dev trong suốt quá trình
- [ ] Ghi lại mọi chỗ họ khựng lại
- [ ] Sửa những chỗ khựng rõ ràng nhất trong ngày

> Đây là **nghiệm thu thật sự của sprint này**. Mọi checkbox khác chỉ chứng minh code chạy; cái này chứng minh sản phẩm dùng được.

## Kiểm chứng

```powershell
dotnet build NewsCMS.Core/NewsCMS.sln
dotnet test NewsCMS.Core/NewsCMS.sln

# Migration cho permission mới (nếu có)
cd NewsCMS.Core/src/NewsCMS.Web
dotnet ef database update
```

**Kiểm tra thủ công:**
- [ ] Đăng nhập admin → thấy menu "Xưởng video"
- [ ] Tạo job với ảnh thật → job chạy, thumbnail hiện dần
- [ ] Storyboard duyệt được, sửa lời thoại được
- [ ] Video xong → xem trước được, tải về được
- [ ] Đăng nhập bằng tài khoản không có quyền → không thấy menu, gõ URL trực tiếp bị chặn
- [ ] Bấm nút tạo hai lần liên tiếp → chỉ một job được tạo

**Kiểm tra bảo mật:**
```bash
# Webhook chữ ký sai phải bị từ chối
curl -X POST https://.../admin/advideo/webhook -H "X-AdVideo-Signature: sai" -d '{"event":"job.completed"}'
# → 401
```

## Rủi ro trong sprint

| Rủi ro | Dấu hiệu | Phản ứng |
|---|---|---|
| Polling làm nặng server | Nhiều job chạy cùng lúc, request dồn | Giãn nhịp polling khi job còn lâu mới xong; chỉ poll nhanh ở bước cuối |
| Người dùng không hiểu brief viết thế nào | Brief một dòng, kết quả tệ | Thêm ví dụ brief tốt ngay trên form, không giấu trong tài liệu |
| Người dùng bỏ đi giữa chừng vì chờ lâu | Bỏ tab trước khi xong | Thumbnail từng shot (T4.4) + thông báo khi xong |
| AdVideo không gọi tới được từ NewsCMS | Timeout, lỗi mạng | Kiểm tra mạng nội bộ trước; tránh đi vòng qua internet nếu cùng máy chủ |
| Upload ảnh lớn bị chặn | Lỗi 413 | Repo có tiền lệ giới hạn Kestrel theo từng request — kiểm tra cấu hình cho route này |
| Làm đẹp UI quá đà | Tuần 7 dành cho CSS | Dùng đúng thành phần giao diện admin có sẵn. Sprint này về việc dùng được, không về đẹp |

## Ra khỏi sprint

- Module `Marketing/VideoStudio` dùng được trong NewsCMS
- Một video do người không phải dev tự làm từ đầu tới cuối
- Danh sách chỗ khựng của người dùng thật — đầu vào cho Sprint 6

## Chuyển sang Sprint 5

Sprint 5 làm phần vận hành: nhiều khách, giọng riêng, bộ nhận diện, hạn mức, QC đầy đủ. Đây là những thứ chỉ cần khi có nhiều hơn một khách.
