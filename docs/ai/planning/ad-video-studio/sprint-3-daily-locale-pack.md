---
phase: planning
sprint: 3
title: "Sprint 3 — Định dạng daily & locale pack (Tuần 6)"
description: Nối frame tuần tự cho video 40 giây một mạch, chống trôi hình, thư viện bối cảnh Việt Nam
---

# Sprint 3 — Định dạng daily & locale pack

> Kế hoạch tổng: [../2026-09-15-feature-ad-video-studio.md](../2026-09-15-feature-ad-video-studio.md)
> Thiết kế: [../../design/2026-09-15-feature-ad-video-studio.md](../../design/2026-09-15-feature-ad-video-studio.md)

## Mục tiêu

Video **40 giây liền mạch** — không cắt cảnh, một nhân vật đi xuyên suốt, sản phẩm xuất hiện tự nhiên trong đời sống thường ngày Việt Nam.

Đây là định dạng **khác biệt nhất** của sản phẩm. Ai cũng làm được video quảng cáo nhiều cảnh; video daily 40 giây một mạch không trôi hình thì khó hơn nhiều.

Cũng là sprint **rủi ro kỹ thuật cao nhất**: nối frame là kỹ thuật chưa làm bao giờ, và trôi hình là vấn đề tích luỹ — không nhìn thấy ở đoạn 2, rõ mồn một ở đoạn 5.

## Definition of Done

- [ ] Định dạng `daily` sinh được video **40 giây không cắt cảnh**
- [ ] Nối frame tuần tự: khung cuối của đoạn trước làm khung đầu của đoạn sau
- [ ] Nhân vật, trang phục, ánh sáng **giữ được xuyên suốt** — người xem không thấy đổi
- [ ] Ảnh tham chiếu lặp lại ở mọi đoạn để chống trôi
- [ ] `SceneEntry` — thư viện bối cảnh Việt Nam, **nạp từ DB**, ≥ 30 mục
- [ ] Sản phẩm xuất hiện tự nhiên, không như bị dán vào
- [ ] Lời thoại trải đều cả 40 giây, không dồn cục
- [ ] 3 video daily mẫu, 5 người marketing chấm ≥ 3/5
- [ ] Nếu không đạt 40 giây: **giảm xuống 24 giây và ghi lý do**, không kéo dài sprint

## Điều kiện vào

| Cần có | Từ đâu |
|---|---|
| Pipeline nhiều shot chạy ổn | Sprint 2 |
| Provider **có hỗ trợ nối frame** (image-to-video từ khung cuối) | Xác nhận trong `ProviderCapability`; nếu không provider nào có thì đổi cách tiếp cận |
| Người Việt có mắt nhìn để biên tập thư viện bối cảnh | ~2 ngày |
| 5 người marketing chấm điểm | Cuối sprint |
| Ảnh sản phẩm chất lượng tốt | Nối frame khuếch đại mọi khiếm khuyết đầu vào |

## Task Breakdown

### T3.1 — Kiểm tra khả năng nối frame của provider

- [ ] Xác nhận provider nào nhận **khung cuối của video trước** làm ảnh đầu vào
- [ ] Đo thực tế: nối 5 đoạn liên tiếp, xem trôi màu/trôi nhân vật bắt đầu từ đoạn nào
- [ ] Ghi vào `ProviderCapability`: `SupportsFrameChaining`, `MaxRecommendedChainLength`
- [ ] Nếu **không provider nào nối được**: dừng lại, báo cáo, đổi sang phương án "nhiều cảnh có chuyển cảnh mềm"

**Files sửa:** `AdVideo/src/AdVideo.Core/Providers/ProviderCapability.cs`

> Làm task này **trong nửa ngày đầu sprint**. Nếu câu trả lời là không, cả sprint phải đổi hướng — biết sớm hơn tốt hơn nhiều.
>
> Số liệu đã lường trước: trôi hình thường lộ rõ **sau 4–5 lần nối**. 40 giây chia thành 5 đoạn 8 giây là đúng ngưỡng nguy hiểm.

### T3.2 — `FrameChainRenderer` — pipeline tuần tự riêng

- [ ] Lớp render **riêng biệt**, không dùng lại `RenderShotsStep` (vốn chạy song song)
- [ ] Render đoạn 1 → trích khung cuối → làm ảnh đầu vào đoạn 2 → lặp
- [ ] Trích khung bằng FFmpeg, giữ nguyên độ phân giải và không nén lại
- [ ] Một đoạn fail → retry đoạn đó; hết retry → fail cả job (không nối lệch)
- [ ] Tiến độ báo theo từng đoạn — người dùng chờ lâu hơn nhiều so với định dạng khác

**Files tạo:**
```
AdVideo/src/AdVideo.Worker/Steps/FrameChainRenderStep.cs
AdVideo/src/AdVideo.Core/Daily/{ChainPlan,ChainSegment}.cs
AdVideo/src/AdVideo.Infrastructure/Media/FrameExtractor.cs
```

> **Tuần tự, không song song.** Đoạn n cần khung cuối của đoạn n−1, nên không có cách nào chạy đồng thời. Hệ quả: video daily 40 giây mất khoảng gấp 4–5 lần thời gian video 30 giây nhiều cảnh. Ghi rõ điều này ở UI Sprint 4.

### T3.3 — Chống trôi hình

- [ ] **Ảnh tham chiếu lặp lại**: gửi kèm ảnh nhân vật/sản phẩm gốc ở **mọi** đoạn, không chỉ đoạn đầu
- [ ] Mô tả cố định về nhân vật (trang phục, tóc, tuổi) nhắc lại nguyên văn trong prompt mỗi đoạn
- [ ] Khoá phong cách: ánh sáng, tông màu, ống kính mô tả giống nhau từng chữ
- [ ] Seed cố định cho cả chuỗi nếu provider hỗ trợ
- [ ] Đo trôi: so màu trung bình khung đầu và khung cuối của cả video

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Daily/DriftGuard.cs
AdVideo/src/AdVideo.Core/Daily/CharacterLock.cs
AdVideo/tests/AdVideo.Tests/Daily/DriftGuardTests.cs
```

> Cách chống trôi hiệu quả nhất là **lặp lại**, không phải mô tả hay hơn. Gửi lại ảnh gốc ở mỗi đoạn kéo model về neo ban đầu; chỉ dựa vào khung cuối của đoạn trước là để sai số cộng dồn.

### T3.4 — `SceneEntry` — thư viện bối cảnh Việt Nam

- [ ] Entity `SceneEntry`: `Code`, `Name`, `Category`, `PromptFragment`, `TimeOfDay`, `Region`, `IsActive`
- [ ] ≥ 30 mục hạt giống, chia nhóm: nhà ở, quán ăn, đường phố, nơi làm việc, chợ/siêu thị
- [ ] Mỗi mục là **đoạn prompt đã kiểm chứng**, không phải mô tả chung chung
- [ ] `GET /v1/scenes` — liệt kê cho UI chọn
- [ ] LLM đạo diễn chọn scene phù hợp brief, hoặc khách chỉ định

**Files tạo:**
```
AdVideo/src/AdVideo.Core/Entities/SceneEntry.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/Configurations/SceneEntryConfiguration.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/Seed/SceneEntrySeeder.cs
AdVideo/src/AdVideo.Infrastructure/Persistence/Seed/Scenes/vietnam-scenes.json
AdVideo/src/AdVideo.Api/Endpoints/SceneEndpoints.cs
```

> Mỗi `PromptFragment` phải được **thử thật và xem kết quả** trước khi vào thư viện. Một mục chưa thử là một quả mìn — nó sẽ nổ ở job của khách chứ không nổ ở máy dev.
>
> Việc này cần **người Việt có mắt nhìn**, không phải dev. "Quán cà phê vỉa hè Việt Nam" mà model vẽ ra quán Thái hay quán Trung thì chỉ người sống ở đây mới thấy sai.

### T3.5 — Cài sản phẩm vào cảnh một cách tự nhiên

- [ ] Sản phẩm xuất hiện **trong hành động**, không phải đặt tĩnh giữa khung
- [ ] Vai trò shot cho daily: cảnh đời thường → gặp vấn đề → dùng sản phẩm → kết quả
- [ ] Prompt nhấn "sản phẩm trong tay nhân vật, dùng tự nhiên" thay vì "sản phẩm ở trung tâm khung hình"
- [ ] Kiểm tra sản phẩm còn nhận ra được ở mọi đoạn (không bị model vẽ lại thành thứ khác)

**Files sửa:** `AdVideo/src/AdVideo.Infrastructure/Director/Prompts/daily-format.txt`

> Chỗ hỏng thường gặp: model giữ đúng *"một chai nước"* nhưng đổi nhãn, đổi màu nắp, đổi hình dáng qua từng đoạn. Ảnh tham chiếu lặp lại (T3.3) cũng giải quyết việc này.

### T3.6 — Trải lời thoại đều 40 giây

- [ ] `TimelineLocker` mở rộng cho chuỗi dài: chia lời thoại theo đoạn, có khoảng lặng
- [ ] Không dồn hết lời vào 15 giây đầu rồi im 25 giây cuối
- [ ] Prompt đạo diễn yêu cầu lời thoại **thưa hơn** so với TVC mini — daily có nhịp chậm
- [ ] Sai lệch tích luỹ qua 5 đoạn vẫn **< 200 ms**

**Files sửa:** `AdVideo/src/AdVideo.Core/Timeline/TimelineLocker.cs`
**Files tạo:** `AdVideo/tests/AdVideo.Tests/Timeline/LongFormTimelineTests.cs`

> Sai lệch tích luỹ là lý do ngưỡng 200 ms tính trên **toàn video**, không phải trên từng shot. Mỗi đoạn lệch 50 ms nghe không ra gì; năm đoạn cộng lại thành 250 ms thì tiếng và hình rõ ràng không ăn nhau.

### T3.7 — Ba video mẫu và chấm điểm

- [ ] 3 sản phẩm khác ngành, mỗi cái một video daily 40 giây
- [ ] 5 người marketing chấm: *liền mạch không* / *nhân vật có đổi không* / *bối cảnh có ra Việt Nam không* / *sản phẩm có tự nhiên không* / *có dám gửi khách không*
- [ ] Ghi lại đoạn nào bắt đầu lộ trôi hình
- [ ] Kết luận: giữ 40 giây hay giảm xuống

**Files tạo:** `spike/results/daily-scorecard.md`

## Kiểm chứng

```powershell
dotnet test AdVideo/AdVideo.sln --collect:"XPlat Code Coverage"

curl -X POST http://localhost:5080/v1/ad-videos `
  -H "X-AdVideo-Key: dev-key" -H "Idempotency-Key: daily-001" `
  -H "Content-Type: application/json" `
  -d '@AdVideo/samples/brief-daily.json'
```

**Kiểm tra thủ công — xem 3 video daily, mỗi cái xem hết:**
- [ ] Không thấy chỗ nối giữa các đoạn
- [ ] Nhân vật cùng một người từ đầu tới cuối — mặt, tóc, quần áo
- [ ] Tông màu không trôi từ ấm sang lạnh (hoặc ngược lại)
- [ ] Sản phẩm giữ nguyên nhãn, màu, hình dáng
- [ ] Bối cảnh ra đúng Việt Nam, không ra Thái/Trung/phương Tây
- [ ] Lời thoại trải đều, không dồn đầu

**Kiểm tra số:**
```
ffprobe → độ dài đúng 40s ±0,5s
Lệch tiếng-hình ở giây thứ 38 phải < 200 ms
```

## Rủi ro trong sprint

| Rủi ro | Dấu hiệu | Phản ứng |
|---|---|---|
| **Trôi hình vẫn lộ sau đoạn 3** | Nhân vật đổi mặt, màu trôi | **Giảm phạm vi xuống 24 giây (3 đoạn)** và đóng sprint. Không kéo dài — 24 giây liền mạch vẫn là sản phẩm bán được |
| Không provider nào nối frame được | T3.1 cho kết quả âm | Đổi hướng: "nhiều cảnh có chuyển cảnh mềm" thay vì một mạch. Báo ngay đầu sprint |
| Thời gian render quá lâu | 40 giây mất > 20 phút | Chấp nhận và ghi rõ ở UI; hoặc giảm số đoạn bằng cách dùng đoạn dài hơn nếu provider cho |
| Thư viện bối cảnh ra kết quả không Việt Nam | Người Việt xem thấy sai | Đây là lý do cần người Việt biên tập. Sửa `PromptFragment`, thử lại từng mục |
| Chi phí một video daily quá cao | 5 đoạn × giá mỗi đoạn | Ghi rõ vào bảng chi phí; daily là định dạng cao cấp, định giá riêng |
| Sản phẩm bị model vẽ lại | Nhãn sai ở đoạn 3 trở đi | Tăng trọng số ảnh tham chiếu; thử gửi ảnh sản phẩm cận cảnh riêng |

## Ra khỏi sprint

- Định dạng `daily` chạy được (40 giây, hoặc 24 giây có ghi lý do)
- `SceneEntry` ≥ 30 mục đã kiểm chứng — tài sản dùng lâu dài
- 3 video daily mẫu + bảng chấm điểm
- Số liệu thật về chi phí và thời gian một video daily

## Chuyển sang Sprint 4

Sprint 4 dựng UI trong NewsCMS. Hợp đồng API đã ổn định sau Sprint 2 và 3 — đây là lý do UI làm sau chứ không làm song song.
