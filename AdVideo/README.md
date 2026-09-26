# AdVideo — Xưởng video quảng cáo AI

Dịch vụ dựng video quảng cáo từ một brief tiếng Việt: nạp ảnh sản phẩm → thu giọng đọc → khoá
timeline → dựng từng shot bằng model sinh video → ghép, chuẩn âm, đóng nhãn AI → kiểm chất lượng.

**Solution và database riêng, không dùng chung với NewsCMS.** Trỏ chuỗi kết nối vào DB của hệ
thống khác là cách nhanh nhất để migration của AdVideo tạo bảng lạ trong DB của người ta.

```
AdVideo/
├── src/
│   ├── AdVideo.Core/            Logic thuần: khoá timeline, tính chi phí, QC, nhãn AI. Không I/O.
│   ├── AdVideo.Infrastructure/  EF Core, MinIO, FFmpeg, adapter provider, store cấu hình.
│   ├── AdVideo.Api/             HTTP + CLI quản trị. Đẩy job vào hàng đợi, KHÔNG dựng video.
│   └── AdVideo.Worker/          Chạy pipeline. Tiến trình DUY NHẤT cần FFmpeg.
├── tests/
│   ├── AdVideo.Core.Tests/      Unit, ~50 ms.
│   └── AdVideo.Tests/           Integration: pipeline thật + FFmpeg thật + provider giả.
├── samples/minimal-request.json
├── CONFIGURATION.md             Mọi khoá cấu hình, file lẫn DB.
└── docker-compose.yml
```

## Chạy bằng Docker (đường ngắn nhất)

Cần Docker và khoảng 4 GB RAM trống cho SQL Server.

```bash
cd AdVideo
docker compose up -d --build
```

Lần đầu mất vài phút: build hai image, SQL Server dựng database hệ thống, API chạy migration và
nạp seed. Xong khi cả bốn dịch vụ đều `healthy`:

```bash
docker compose ps
curl -s http://localhost:5080/healthz | jq
```

Cổng: API `5080`, worker `5081` (chỉ có `/healthz`), MinIO console `9001` (minioadmin/minioadmin),
SQL Server `11433`.

**Cả stack chạy bằng provider giả** (`AdVideo__FakeProviders__Enabled=true`): không cần API key,
không tốn một xu. Video ra là `testsrc2` — nhưng nó được ghép bằng **FFmpeg thật**, nên nhãn AI,
độ ồn LUFS và thời lượng đều là kiểm tra thật.

### Tạo tenant và lấy API key

Không có đường đăng ký qua HTTP, và đó là chủ ý: cấp key qua HTTP thì endpoint đó cần một cơ chế
xác thực khác, mà cơ chế đó lại cần một cách khởi tạo. Ai chạy được lệnh trên máy chủ thì đã có
quyền cao nhất rồi.

```bash
docker compose exec api dotnet AdVideo.Api.dll create-tenant --name "Dev"
```

Key được in **một lần duy nhất** — trong DB chỉ có bản băm. Mất thì `rotate-key`.

### Gọi thử

```bash
KEY=<key vừa in ra>

curl -X POST http://localhost:5080/v1/ad-videos \
  -H "X-AdVideo-Key: $KEY" \
  -H "Idempotency-Key: test-001" \
  -H "Content-Type: application/json" \
  -d @samples/minimal-request.json
```

→ `202` kèm `job_id`. Theo dõi:

```bash
curl -s http://localhost:5080/v1/ad-videos/<job_id> -H "X-AdVideo-Key: $KEY" | jq
```

`status` đi qua `queued` → `processing` → `completed`, và khi xong thì `download_url` là một
presigned URL của MinIO, tải thẳng được. Xem worker đang làm gì:

```bash
docker compose logs -f worker
```

> `assets.product_images` phải là URL **http/https mà container worker gọi tới được**. File
> `samples/minimal-request.json` dùng một ảnh trên Internet; nếu máy không ra mạng được thì upload
> một ảnh qua MinIO console rồi dùng link của nó. `file://` bị từ chối ngay ở bước 1 — đó là cách
> bắt máy chủ đọc hộ file của chính nó.

### Gửi lại cùng một `Idempotency-Key`

Cùng key + **cùng thân request** → trả về đúng job cũ, không tạo job mới: client thử lại sau
timeout là chuyện bình thường, và mỗi lần thử lại tạo thêm một video là mỗi lần tính tiền thêm
một lần. Cùng key + thân request **khác** → `409`, vì lúc đó không đoán được khách muốn job nào.

## Chạy không cần Docker (dev trên Windows)

Cần: .NET 8 SDK, SQL Server (Express là đủ), FFmpeg, và MinIO nếu muốn dùng kho S3 — hoặc đặt
`AdVideo:Storage:Provider = LocalDisk` để ghi thẳng ra đĩa.

```powershell
# Migration (InitialCreate nằm ở src/AdVideo.Infrastructure/Persistence/Migrations)
dotnet run --project src/AdVideo.Api -- migrate
dotnet run --project src/AdVideo.Api -- seed

# Tenant + key
dotnet run --project src/AdVideo.Api -- create-tenant --name "Dev"

# Hai tiến trình, hai cửa sổ
dotnet run --project src/AdVideo.Api
dotnet run --project src/AdVideo.Worker
```

Đổi entity thì tạo migration mới (công cụ `dotnet-ef` ghim phiên bản trong `.config/dotnet-tools.json`):

```bash
dotnet tool restore
dotnet ef migrations add <TenMigration> --project src/AdVideo.Infrastructure \
    --startup-project src/AdVideo.Api --output-dir Persistence/Migrations
```

Quên bước này thì `MigrationTests` đỏ — test integration dùng SQLite + `EnsureCreated` nên tự chúng
không bắt được.

`appsettings.Development.json` đã trỏ sẵn FFmpeg vào `C:/ffmpeg/...` và font vào
`C:/Windows/Fonts/arial.ttf`. Đường dẫn trong JSON dùng **gạch chéo xuôi**: `"\f"` là ký tự
form-feed còn `"\W"` là escape không hợp lệ, nên gõ đường dẫn Windows kiểu `C:\ffmpeg` vào đây thì
hoặc sai âm thầm, hoặc cả file không đọc được.

## Cấu hình

Hai chỗ, và ranh giới giữa chúng là cố ý (D10):

| Nằm ở đâu | Cái gì | Vì sao |
|---|---|---|
| File / biến môi trường | Chuỗi kết nối, MinIO, đường dẫn FFmpeg, key ring DataProtection, số job song song | Đặc tính của **máy** đang chạy |
| DB | API key provider (đã mã hoá), trần chi phí, provider mặc định, ngưỡng QC, prompt | Đổi được lúc đang chạy, không cần deploy, và giống nhau ở mọi máy |

**Không có API key nào trong `appsettings.json`.** Nạp bằng CLI:

```bash
docker compose exec api dotnet AdVideo.Api.dll set-credential --provider veo --key <api key> --model veo-3
docker compose exec api dotnet AdVideo.Api.dll list-settings
docker compose exec api dotnet AdVideo.Api.dll set-setting --key DefaultVideoProvider --value veo
```

Chi tiết từng khoá: [CONFIGURATION.md](CONFIGURATION.md).

Hai thứ dễ sai nhất:

- **`AdVideo:DataProtection:KeyRingPath` phải là thư mục được sao lưu, và không được nằm trong
  `bin/`.** Key ring là khoá giải mã mọi API key provider trong DB. Mất nó là phải nhập lại từng
  cái. Chuyện này đã xảy ra ở NewsCMS trên chính máy chủ này. Trong compose, API và worker dùng
  **chung** một volume key ring — tách ra là worker không giải mã nổi key mà API vừa ghi.
- **`AdVideo:FakeProviders:Enabled` phải là `false` trên production.** Còn bật thì một credential
  thật bị hỏng sẽ được provider giả âm thầm nhận việc, và khách nhận về một video `testsrc2`. Thà
  job fail còn hơn.

## Test

```bash
# Unit — thuần logic, ~50 ms
dotnet test tests/AdVideo.Core.Tests/AdVideo.Core.Tests.csproj

# Integration — cần FFmpeg thật trên máy, ~30 giây
dotnet test tests/AdVideo.Tests/AdVideo.Tests.csproj
```

Tầng integration chạy trọn pipeline trong tiến trình test với Sqlite + kho file trên đĩa tạm +
provider giả + **FFmpeg thật**, rồi `ffprobe` file thành phẩm. Không cần container nào.

Mọi HttpClient trỏ ra provider thật bị chặn bằng một handler ném lỗi: **test tự động không được
tiêu tiền**. Tìm FFmpeg và font không ra thì test **ném lỗi chứ không tự bỏ qua** — một test tự
skip là một test không bao giờ chạy trên CI mà không ai biết. Đường thoát khi máy để ở chỗ khác:

```bash
export ADVIDEO_TEST_FFMPEG_DIR=/opt/ffmpeg/bin
export ADVIDEO_TEST_FONTFILE=/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf
```

## Những thứ chưa có ở Sprint 1

Pipeline chạy bước 1 → 4 → 5 → 6 → 8 → 9. Chưa có: LLM đạo diễn viết lời thoại và storyboard
(bước 2–3, nên `voice.script` **bắt buộc**), lip-sync (bước 7), phụ đề, nhạc nền, webhook, giao
diện trong NewsCMS, regenerate một shot, và fan-out song song.

Nhãn AI thì **có ngay từ Sprint 1 và không có nút tắt**: Luật TTNT 2025 đặt nghĩa vụ lên bên
triển khai, phạt tới 2 tỉ đồng, và không chuyển sang khách bằng điều khoản được.
