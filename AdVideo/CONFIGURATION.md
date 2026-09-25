# Cấu hình AdVideo

Tài liệu này liệt kê **mọi** thứ có thể chỉnh trong AdVideo, và quan trọng hơn là **chỉnh ở đâu**.

Có hai nơi, và ranh giới giữa chúng là quyết định **D10**:

| | File cấu hình / biến môi trường | Cơ sở dữ liệu |
|---|---|---|
| Chứa gì | Những thứ phải biết **trước khi** đọc được DB | Mọi thứ còn lại |
| Ví dụ | Chuỗi kết nối, endpoint MinIO, đường dẫn ffmpeg | API key, trần chi tiêu, tên provider, prompt |
| Đổi bằng cách nào | Sửa file → **khởi động lại** tiến trình | Lệnh CLI hoặc SQL → **có hiệu lực trong vòng vài giây**, không restart |
| Ai đổi | Người deploy | Người vận hành |

> **Vì sao chia như vậy.** Toàn bộ cấu hình vận hành nằm trong DB (D10) để đổi provider, đổi trần
> chi tiêu hay xoay API key không cần deploy lại — và để API với Worker, hai tiến trình riêng, không
> bao giờ đọc ra hai giá trị khác nhau. Thứ duy nhất còn ở file là **tầng khởi động**: muốn đọc DB
> đã phải có chuỗi kết nối, muốn đọc file đã phải có endpoint lưu trữ. Không thể cất chúng vào chính
> cái mà chúng dùng để mở.

---

## Phần 1 — Cấu hình file (appsettings / biến môi trường)

Toàn bộ nằm trong `appsettings.json` của `AdVideo.Api` và `AdVideo.Worker`. Cả hai host nạp cùng một
tầng hạ tầng qua `AddAdVideoInfrastructure`, nên **hai file phải khớp nhau** ở các khoá lưu trữ và DB.

### 1.1. Chuỗi kết nối

| Khoá | Bắt buộc | Ghi chú |
|---|---|---|
| `ConnectionStrings:AdVideoDb` | ✅ | Thiếu là ném `InvalidOperationException` ngay lúc khởi động |

AdVideo dùng **DB riêng** (`AdVideoDb`), không dùng chung với hệ thống khác trên cùng máy chủ. Nếu
thiếu khoá này, `AddAdVideoInfrastructure` từ chối khởi động thay vì chạy với DB mặc định nào đó.

```json
{
  "ConnectionStrings": {
    "AdVideoDb": "Server=localhost;Database=AdVideoDb;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

**Biến môi trường `ADVIDEO_DB`** — chỉ dùng cho `dotnet ef` lúc thiết kế (`AdVideoDbContextFactory`).
Lúc chạy thật không ai đọc nó. Đặt biến này khi tạo hoặc chạy migration từ dòng lệnh:

```bash
ADVIDEO_DB="Server=localhost;Database=AdVideoDb;Trusted_Connection=True;TrustServerCertificate=True" \
  dotnet ef migrations add InitialCreate -p src/AdVideo.Infrastructure -s src/AdVideo.Api
```

### 1.2. Lưu trữ — `AdVideo:Storage`

| Khoá | Mặc định | Ghi chú |
|---|---|---|
| `Provider` | `LocalDisk` | `LocalDisk` hoặc `Minio` |
| `ServiceUrl` | `http://localhost:9000` | Endpoint **ứng dụng** gọi tới |
| `PublicServiceUrl` | *(trống)* | Endpoint **trình duyệt người dùng** gọi tới |
| `AccessKey` | *(trống)* | Bắt buộc khi `Provider = Minio` |
| `SecretKey` | *(trống)* | Bắt buộc khi `Provider = Minio` |
| `Region` | `us-east-1` | MinIO không dùng, nhưng AWS SDK bắt buộc phải có |
| `LocalRoot` | `App_Data/advideo-storage` | Chỉ dùng khi `Provider = LocalDisk` |

> **`PublicServiceUrl` không phải là chuyện thẩm mỹ.** Chữ ký SigV4 ký cả header `Host`. Ký URL bằng
> `http://minio:9000` rồi thay host thành `https://cdn.example.vn` là chữ ký hỏng — MinIO trả
> 403 `SignatureDoesNotMatch`. Khi MinIO nằm sau nginx, presigned URL phải được **ký** bằng đúng host
> công khai, nghĩa là một client S3 thứ hai. Bỏ trống thì dùng luôn `ServiceUrl`.

`AccessKey`/`SecretKey` **không ghi vào `appsettings.json`**. Dùng biến môi trường (dấu `__` thay cho
`:`) hoặc user-secrets:

```bash
export AdVideo__Storage__AccessKey=...
export AdVideo__Storage__SecretKey=...
```

Bốn bucket được tạo tự động nếu chưa có: `adv-uploads` (file khách tải lên), `adv-work` (shot trung
gian), `adv-final` (video giao khách), `adv-voice` (giọng đọc).

### 1.3. FFmpeg — `AdVideo:Ffmpeg`

| Khoá | Mặc định | Ghi chú |
|---|---|---|
| `FfmpegPath` | `ffmpeg` | Tên lệnh (tìm trong PATH) hoặc đường dẫn tuyệt đối |
| `FfprobePath` | `ffprobe` | |
| `TimeoutSeconds` | `300` | Một lần gọi ffmpeg/ffprobe |
| `Threads` | `0` | `0` = để FFmpeg tự quyết |
| `FontFile` | *(trống)* | **Đường dẫn file font có dấu tiếng Việt** |

> **`FontFile` phải trỏ tới một file có thật.** Thiếu font thì FFmpeg **vẫn chạy, vẫn thoát 0, vẫn
> xuất ra video** — chỉ có điều mọi chữ tiếng Việt hiện thành ô vuông. `FfmpegComposer` từ chối ngay
> khi font trống hoặc file không tồn tại, trước khi tốn một lần encode. Nhãn AI bắt buộc (D9) được vẽ
> bằng chính font này, nên không có font nghĩa là không có video hợp lệ.

`FfmpegPath`/`FfprobePath` viết theo kiểu Windows trên máy dev, nhưng **khi deploy Linux phải ghi đè**
trong `appsettings.Production.json`. Bỏ qua bước này thì việc nén/probe hỏng âm thầm.

```jsonc
// appsettings.Production.json trên VPS Ubuntu
{
  "AdVideo": {
    "Ffmpeg": {
      "FfmpegPath": "/usr/bin/ffmpeg",
      "FfprobePath": "/usr/bin/ffprobe",
      "FontFile": "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
    }
  }
}
```

> **Đường dẫn Windows trong JSON phải dùng gạch chéo XUÔI** (`C:/ffmpeg/bin/ffmpeg.exe`). Gõ gạch
> chéo ngược thì `\f` thành ký tự form-feed — đường dẫn sai mà không ai thấy — còn `\W` là escape
> không hợp lệ và cả file appsettings không đọc được. .NET trên Windows hiểu cả hai kiểu gạch chéo.

### 1.4. Provider giả — `AdVideo:FakeProviders`

Đây là thứ làm cho Sprint 1 chạy được toàn tuyến với **chi phí bằng 0**.

| Khoá | Mặc định | Ghi chú |
|---|---|---|
| `Enabled` | `true` | **Production phải đặt `false`** |
| `LatencyMs` | `0` | Giả lập độ trễ mỗi lần gọi |
| `FailShotIndexes` | `[]` | Shot có chỉ số trong danh sách sẽ fail |
| `FailureKind` | `Transient` | `Transient`, `ContentPolicy`, `ProviderUnavailable`, … |
| `FailTimesBeforeSuccess` | `0` | Fail mấy lần rồi mới thành công |
| `TtsWithoutTimings` | `false` | TTS trả kết quả không có mốc thời gian |
| `PingSucceeds` | `true` | Đặt `false` để thử nhánh provider chết |

> **Vì sao production phải tắt.** Nếu credential thật hỏng mà provider giả vẫn còn trong danh sách,
> nó sẽ **âm thầm nhận việc** và khách nhận về một video `testsrc2`. Thà job fail còn hơn.

Mọi mặc định đều là "chạy trơn tru": bật lỗi phải là hành động cố ý của test. Đường thành công thì
test nào cũng đi qua — thứ cần chứng minh là pipeline xử lý đúng khi một shot bị từ chối nội dung,
khi provider trả 429, khi lần thử thứ nhất fail và lần hai được.

### 1.5. Timeout HTTP của provider

`ProviderRegistry.HttpClientName` được cấu hình cứng trong code: **10 phút cho một request HTTP**.
Không có khoá config cho nó, và đây là chủ ý — mặc định 100 giây của `HttpClient` đủ cho lệnh gửi và
lệnh hỏi trạng thái, nhưng không đủ để **tải về** một clip qua đường truyền chậm, mà timeout ở bước
tải nghĩa là mất một shot **đã trả tiền**.

Ngân sách thời gian của cả lần render là chuyện khác, và nó nằm trong DB:
`VideoProviderTimeoutSeconds`.

### 1.6. Khoá mã hoá API key — `AdVideo:DataProtection`

| Khoá | Mặc định | Ghi chú |
|---|---|---|
| `KeyRingPath` | *(trống)* | Thư mục giữ key ring. Trống = chỗ mặc định của nền tảng |

API key của provider nằm trong DB ở dạng **đã mã hoá** bằng DataProtection (D10). Khoá để giải mã
chúng là key ring — mất key ring là mọi API key trong DB thành rác, phải nhập lại từng cái.

Hai điều bắt buộc, và cả hai đều đã được đặt sẵn trong `AddAdVideoInfrastructure`:

1. **Application name cố định (`"AdVideo"`).** Mặc định DataProtection lấy đường dẫn content root
   làm tên ứng dụng. API và Worker nằm ở hai thư mục khác nhau nên sẽ sinh ra hai khoá khác nhau,
   và Worker không giải mã nổi key mà API vừa ghi — thông báo lỗi lúc đó nói về payload hỏng, không
   nói gì về tên ứng dụng.
2. **`IApiKeyProtector` là singleton.** Đăng ký scoped thì mỗi request dựng một provider mới, và
   trên một số cấu hình key ring điều đó làm khoá bị sinh lại.

> **Production phải đặt `KeyRingPath` vào một thư mục được sao lưu**, và **không** đặt nó trong
> `bin/`. Chuyện này đã xảy ra ở NewsCMS trên chính máy chủ này: key ring nằm trong `bin/`, một lần
> dọn `bin/` là mất toàn bộ khoá, và API key AI trong DB không còn giải mã được.

```json
{
  "AdVideo": {
    "DataProtection": {
      "KeyRingPath": "/var/lib/advideo/keys"
    }
  }
}
```

### 1.7. Worker — `AdVideo:Worker`

| Khoá | Mặc định | Ghi chú |
|---|---|---|
| `MaxConcurrentJobs` | `2` | Số **job** chạy đồng thời trên một tiến trình worker |

Đây là `WorkerCount` của Hangfire. Mặc định của Hangfire là `ProcessorCount * 5` — hợp lý cho job gửi
email, tai hoạ cho job gọi FFmpeg: trên máy 8 nhân đó là 40 job cùng chạy, mỗi job một tiến trình
ffmpeg ăn hết CPU và vài trăm MB đĩa tạm. Máy không chết hẳn, chỉ chậm tới mức **mọi** job đều chạm
timeout.

Đừng nhầm với `MaxConcurrentShots` trong DB: khoá đó đếm **shot trong một job**, khoá này đếm **job
trong một tiến trình**. Số ffmpeg chạy cùng lúc tối đa là tích của hai số.

Khoá này nằm ở file chứ không ở DB vì nó là đặc tính của **máy** đang chạy — worker trên VPS 2 nhân
và worker trên máy 16 nhân phải đặt khác nhau, trong khi trần chi phí thì giống nhau ở mọi máy.

---

## Phần 2 — Cấu hình trong DB

Ba bảng, ba store, cùng một cơ chế cache: đọc được cache trong bộ nhớ, và mọi lần ghi phát tín hiệu
huỷ cache **cho cả API lẫn Worker**, nên sửa xong là có hiệu lực gần như tức thì, không cần restart.

### 2.1. `SystemSettings` — tham số vận hành

Seeder chỉ **thêm khoá còn thiếu, không bao giờ ghi đè**. Người vận hành chỉnh một con số lúc nửa đêm
để cứu sự cố, rồi lần deploy sau seeder trả nó về mặc định là kiểu hỏng tồi tệ nhất.

| Khoá | Mặc định | Khoảng hợp lệ | Tạm | Ý nghĩa |
|---|---|---|:--:|---|
| `MaxConcurrentShots` | `1` | 1–8 | ⚠️ | Số shot render đồng thời trong một job |
| `DefaultVideoProvider` | `fake` | | ⚠️ | Provider video mặc định |
| `DraftTtsProvider` | `fake` | | ⚠️ | Engine TTS tier Nháp |
| `StandardTtsProvider` | `fake` | | ⚠️ | Engine TTS tier Thành phẩm (bắt buộc có mốc thời gian theo từ) |
| `VideoProviderTimeoutSeconds` | `600` | 60–1800 | ⚠️ | Timeout một lần gọi, **tính cả thời gian poll** |
| `ShotMaxRetries` | `2` | 0–5 | ⚠️ | Mỗi lần retry là **một lần tính tiền** |
| `DailySystemCostLimitUsd` | `20` | 0–10000 | ⚠️ | Trần chi tiêu toàn hệ thống mỗi ngày |
| `MaxCostPerJobUsd` | `2` | 0–500 | ⚠️ | Trần một job, kiểm tra **trước** khi gọi provider (D8) |
| `DownloadUrlLifetimeMinutes` | `60` | 5–1440 | | Hạn presigned URL |
| `TargetLoudnessLufs` | `-14` | −31…−5 | | Chuẩn âm lượng đầu ra |
| `MaxLipSyncDriftMs` | `200` | 40–1000 | | Dung sai lệch tiếng-hình, vượt là QC đánh trượt |
| `GlobalNegativePromptCode` | `global-negative` | | | Code prompt negative toàn cục |
| `ProviderSmokeTestEnabled` | `false` | | | Smoke test hằng ngày — mỗi lần test là một lần tiêu tiền thật |

Cột **Tạm** là cờ `IsProvisional`. Nó có nghĩa rất cụ thể: **số này là phỏng đoán bảo toàn, chưa được
đo**. Sprint 0 (đo thật bằng key thật) bị bỏ qua vì chưa có API key và ngân sách, nên cờ này chính là
danh sách việc phải làm lại khi có key. `ISettingsStore.GetProvisionalAsync` đọc đúng danh sách đó.

Hai trần chi tiêu để thấp **có chủ đích**: chưa có ngân sách, nên cái giá của một vòng retry chạy loạn
phải bị chặn ở mức vài chục đô, không phải vài nghìn. Nâng chúng lên là một quyết định có ý thức.

Ba khoá provider mặc định `fake` cũng vậy — đổi sang provider thật là hành động có ý thức của người
vận hành **sau khi** đã nạp key.

### 2.2. `ProviderCredentials` — API key

Key được mã hoá bằng DataProtection trước khi ghi vào DB, và **không bao giờ đọc ngược ra được** qua
API hay log; chỉ `ProviderRegistry` giải mã lúc dựng provider.

Nạp key bằng CLI của `AdVideo.Api` (không sửa DB bằng tay):

```bash
dotnet run --project src/AdVideo.Api -- set-credential --provider veo --key "$GEMINI_API_KEY"
dotnet run --project src/AdVideo.Api -- set-credential --provider elevenlabs --key "$ELEVENLABS_API_KEY"
```

Mỗi credential còn mang theo endpoint và **capability** (giá mỗi đơn vị, độ dài clip cho phép, có
mốc thời gian theo từ hay không, voice hết hạn khi nào). Capability trong DB **ghi đè** capability
khai trong code, để sửa bảng giá khi nhà cung cấp đổi giá mà không phải deploy.

> ⚠️ **`IApiKeyProtector` phải là singleton.** Đăng ký scoped thì mỗi request dựng một DataProtection
> provider mới, và trên một số cấu hình key ring điều đó làm key bị **sinh lại** — nghĩa là mọi API
> key đã mã hoá trong DB thành rác không giải mã được. Đây là lỗi đã từng xảy ra ở dự án khác trên
> chính máy chủ này. Key ring cũng không được nằm trong `bin/`: `clean` là mất khoá.

### 2.3. `PromptTemplates` — prompt

Prompt gửi cho model **không nằm trong code**. Mỗi template có `Code` (định danh ổn định), nội dung,
và số phiên bản; sửa prompt là thêm phiên bản mới, không đè lên bản cũ — để truy được một video cũ
đã sinh ra từ prompt nào.

`GlobalNegativePromptCode` ở trên trỏ tới một template trong bảng này.

---

## Phần 3 — Xem và sửa

```bash
# Liệt kê toàn bộ setting, kèm cờ provisional
dotnet run --project src/AdVideo.Api -- list-settings

# Đổi một setting (kiểm tra khoảng hợp lệ trước khi ghi)
dotnet run --project src/AdVideo.Api -- set-setting --key MaxConcurrentShots --value 3
```

Sửa thẳng bằng SQL cũng được, nhưng phải tự chịu hai thứ CLI làm hộ: kiểm tra `MinValue`/`MaxValue`,
và phát tín hiệu huỷ cache (SQL thì phải đợi cache hết hạn).

---

## Phần 4 — Danh sách kiểm trước khi chạy production

- [ ] `AdVideo:FakeProviders:Enabled` = `false`
- [ ] `AdVideo:Ffmpeg:FontFile` trỏ tới file font **có thật** và **có dấu tiếng Việt**
- [ ] `FfmpegPath`/`FfprobePath` là đường dẫn Linux, không phải đường dẫn Windows của máy dev
- [ ] `AdVideo:Storage:Provider` = `Minio`, có `AccessKey`/`SecretKey` từ biến môi trường
- [ ] `PublicServiceUrl` đặt đúng host công khai nếu MinIO nằm sau nginx
- [ ] Đã nạp credential thật và đổi ba khoá `*Provider` khỏi `fake`
- [ ] Đã xem lại toàn bộ setting còn cờ provisional và điều chỉnh theo số đo thật
- [ ] Hai trần chi tiêu (`DailySystemCostLimitUsd`, `MaxCostPerJobUsd`) đặt đúng ngân sách thật
- [ ] Key ring DataProtection được cấu hình lưu ở nơi **không bị xoá khi deploy**
