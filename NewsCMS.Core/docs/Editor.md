# Editor: TinyMCE 7 Community

Phần soạn bài viết của NewsCMS dùng **TinyMCE 7 Community** (self-host từ jsDelivr CDN). Hoàn toàn miễn phí, không gọi service ngoài, có ngôn ngữ tiếng Việt sẵn.

## Tệp liên quan

| File | Vai trò |
|------|---------|
| `Areas/Admin/Shared/_TinyMce.cshtml` | Partial khởi tạo editor, dùng chung mọi nơi cần rich-text |
| `Areas/Admin/Shared/TinyMceOptions.cs` | Model truyền options vào partial |
| `Areas/Admin/Pages/Posts/Create.cshtml(.cs)` | Trang tạo bài, có editor + auto-slug + featured image |
| `Areas/Admin/Pages/Posts/Edit.cshtml(.cs)` | Trang sửa bài |
| `Areas/Admin/Pages/Media/Upload.cshtml.cs` | Endpoint `/admin/Media/Upload` — TinyMCE upload ảnh vào đây |
| `Infrastructure/Content/ContentSanitizer.cs` | Lọc HTML chống XSS trước khi lưu DB |
| `Infrastructure/Content/SlugHelper.cs` | Sinh slug tiếng Việt không dấu |
| `Infrastructure/Storage/LocalFileStorage.cs` | Lưu file vật lý vào `wwwroot/uploads/{folder}/{yyyy}/{MM}/` |

## Cách dùng editor trên trang khác

Bất kỳ trang admin nào muốn rich-text editor, chỉ cần:

```cshtml
<textarea asp-for="Input.Body" id="my-editor"></textarea>

@section Scripts {
    <partial name="_TinyMce" model='new TinyMceOptions { Selector = "#my-editor", Height = 500 }' />
}
```

## Luồng upload ảnh

1. User nhấn nút "Image" trong toolbar TinyMCE hoặc kéo-thả ảnh vào editor.
2. TinyMCE gọi `images_upload_handler` → POST `multipart/form-data` tới `/admin/Media/Upload`, kèm header `RequestVerificationToken`.
3. `Media/Upload.cshtml.cs`:
   - Kiểm tra extension trong whitelist (`Storage.AllowedExtensions` của appsettings).
   - Kiểm tra dung lượng (`Storage.MaxFileSizeMb`; video dùng riêng `Storage.MaxVideoSizeMb`).
   - Gọi `IFileStorage.SaveAsync()` → ghi vào `wwwroot/uploads/posts/yyyy/MM/{guid}.{ext}`.
   - Đọc width/height bằng ImageSharp.
   - Thêm bản ghi `Media` vào DB.
   - Trả JSON `{ "location": "/uploads/posts/2026/05/abc.jpg", "id": "...", ... }`.
4. TinyMCE chèn `<img src="/uploads/posts/2026/05/abc.jpg" />` vào nội dung bài.

## Upload file lớn — tus resumable (video 2GB, PDF nặng...)

File **> 32MB** tự động chuyển sang đường upload resumable chuẩn [tus.io](https://tus.io) tại `/admin/media/tus`:

- Client chia chunk 64MB (nằm dưới giới hạn body ~100MB của Cloudflare Tunnel), retry exponential khi rớt mạng, resume được sau khi refresh trang.
- Server validate extension + dung lượng **trước khi nhận byte nào** (`OnBeforeCreateAsync`) — sai loại file báo lỗi ngay, không tốn băng thông.
- Khi PATCH cuối hoàn tất: server stream file qua `IMediaService.CreateFromUploadAsync` (cùng logic whitelist/kind/subfolder/audit như đường thường), sinh poster video bằng ffmpeg (`Storage.FfmpegPath`, bỏ qua nếu không cài), rồi client poll `GET /Admin/Media?handler=TusStatus&tusId=...` để nhận JSON kết quả.
- File nhỏ ≤ 32MB giữ nguyên đường XHR cũ — TinyMCE paste ảnh, GrapesJS không đổi hành vi.

Cấu hình liên quan (mục `Storage` trong appsettings.json): `MaxFileSizeMb` (default ảnh/tài liệu), `MaxVideoSizeMb` (default 2048 = 2GB), `FfmpegPath`, `TusStoreRoot` (`App_Data/tus-uploads`). Worker `TusCleanupWorker` dọn file tus hết hạn mỗi giờ.

Ngoài ra mọi file media (`/uploads/**`) đều được gắn header `Cache-Control: public,max-age=31536000,immutable` (tên file GUID nên an toàn tuyệt đối) và hỗ trợ sẵn Range request — video seek, PDF byte-range load nhanh ở lượt xem sau.

## Sanitize HTML

Trước khi `SaveChanges`, nội dung bài đi qua `ContentSanitizer.Sanitize(html)`:

- Loại bỏ `<script>`, `on*` attributes, javascript: URLs.
- Chỉ cho phép `<iframe>` từ YouTube/Vimeo.
- Cho phép thẻ ngữ nghĩa cần thiết (`figure`, `figcaption`, `pre`, `code`, ...).

Bạn có thể chỉnh whitelist trong `ContentSanitizer.cs`.

## Sinh slug tiếng Việt

`SlugHelper.Generate("Tiêu đề có dấu Đặc biệt!")` → `"tieu-de-co-dau-dac-biet"`.

Trang Create/Edit có JS tự sinh slug khi gõ tiêu đề (chỉ khi user chưa chạm vào ô slug), và nút ↻ để re-generate thủ công.

## Tuỳ biến TinyMCE

Mở `_TinyMce.cshtml` để chỉnh:

- `plugins`, `toolbar`: thêm/bớt button.
- `content_style`: style trong editor cho khớp với theme landing.
- `codesample_languages`: bổ sung ngôn ngữ.
- `image_class_list`: nhóm class CSS chèn vào ảnh (responsive, alignment...).

Tham khảo: https://www.tiny.cloud/docs/tinymce/7/
