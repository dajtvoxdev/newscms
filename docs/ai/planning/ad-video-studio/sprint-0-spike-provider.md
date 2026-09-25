---
phase: planning
sprint: 0
title: "Sprint 0 — Spike provider (Tuần 1)"
description: Chạy thử 4 provider video và 2 hướng TTS trên ảnh sản phẩm thật, chấm điểm, đo rate limit — trước khi viết một dòng service nào
---

# Sprint 0 — Spike provider

> Kế hoạch tổng: [../2026-09-15-feature-ad-video-studio.md](../2026-09-15-feature-ad-video-studio.md)
> Thiết kế: [../../design/2026-09-15-feature-ad-video-studio.md](../../design/2026-09-15-feature-ad-video-studio.md)

## Mục tiêu

Trả lời **ba câu hỏi có thể giết dự án**, trong tuần đầu tiên, với chi phí dưới $100:

1. Có provider nào cho ra video mà người làm marketing Việt Nam thấy **dùng được** không?
2. Tự host VieNeu-TTS có khả thi không, hay phải trả tiền ElevenLabs cho mọi video?
3. Một tài khoản chạy được **bao nhiêu job song song** trước khi bị chặn?

Câu 3 nghe nhàm nhất nhưng ảnh hưởng tới kiến trúc nhiều nhất. Nếu một key chỉ chạy được 2 job đồng thời thì phần scale phải thiết kế lại **trước** khi code Sprint 1, không phải sau.

**Sprint này không viết code service.** Chỉ script rời, ảnh, video, và một bảng chấm điểm.

## Definition of Done

- [ ] 12 video mẫu (3 kịch bản × 4 provider) render xong, xem được, lưu cùng chỗ
- [ ] Bảng chấm điểm có đủ 5 người marketing chấm, có số trung bình mỗi provider
- [ ] Biết **giá thật** mỗi video của từng provider (đối chiếu hoá đơn, không phải giá niêm yết)
- [ ] Biết **rate limit thật** của từng key: bao nhiêu request đồng thời, bao nhiêu mỗi phút
- [ ] Có kết luận Vidu Q2 Pro giữ được bao nhiêu chủ thể trong một khung hình
- [ ] Nghe mù 10 mẫu giọng, có danh sách 8–12 giọng tuyển chọn cho tiếng Việt
- [ ] Có **kết luận dứt khoát** về VieNeu-TTS: dùng được ở tier nào, hay không dùng
- [ ] Ghi lại provider nào **từ chối ảnh có mặt người** và từ chối như thế nào
- [ ] Một trang tổng kết: chọn provider nào làm mặc định cho Sprint 1, vì sao

## Điều kiện vào

| Cần có trước khi bắt đầu | Ghi chú |
|---|---|
| Tài khoản Gemini API có bật thanh toán | Veo 3.1 Fast/Lite gọi trực tiếp |
| Tài khoản fal.ai có credit | Kling 3.0, Seedance 2.0, Vidu Q2 Pro — một key dùng chung |
| Tài khoản ElevenLabs (gói bất kỳ) | Đủ để nghe thử Voice Library |
| **Ảnh sản phẩm thật của một khách thật** | 3 sản phẩm, mỗi sản phẩm 2–3 góc. **Không dùng ảnh stock** — ảnh stock đã đẹp sẵn, không phản ánh chất lượng ảnh khách gửi |
| 5 người làm marketing sẵn sàng chấm điểm | Nửa ngày, cuối tuần |
| Máy có Python hoặc `curl` + `ffprobe` | Script spike chạy rời, không cần .NET |

## Tiến độ

_Cập nhật 15/09/2026._

> ## 🔶 TRẠNG THÁI: BLOCKED — chưa có API key và ngân sách spike
>
> **Quyết định 15/09/2026:** đổi thứ tự — **Sprint 1 (phần skeleton) chạy trước**, Sprint 0 chạy ngay khi có key và ngân sách. Lý do: phần lớn Sprint 1 không cần key (solution, entity, API, worker, Fake providers, FFmpeg, test), và quyết định **D10** (mọi cấu hình/key/prompt nằm trong DB) khiến các con số lẽ ra học từ Sprint 0 có thể điền sau mà không cần deploy lại.
>
> **Sprint 0 vẫn là điều kiện cứng cho:** các mục 🔑 của Sprint 1 (adapter chạy thật), Sprint 2 (không vào Sprint 2 khi chưa render được video thật), và **Sprint 6** (không mở pilot khi chưa biết provider nào dùng được và chi phí thật là bao nhiêu).
>
> Việc chuẩn bị không vô ích: toàn bộ script đã viết xong và smoke-test bằng `--dry-run`; cửa license của VieNeu (T0.8) đã tra xong — Apache-2.0, dùng thương mại được tới v2-Turbo. Có key + ảnh + người chấm thì phần còn lại chạy trong 1–2 ngày.

**Toàn bộ công cụ và khung kết quả đã sẵn sàng. Chưa có số liệu nào, vì chưa có key API, chưa có ảnh sản phẩm thật và chưa có người chấm.**

| Task | Trạng thái | Còn thiếu |
|---|---|---|
| T0.1 — Dựng chỗ làm việc | ✅ **Xong** | — |
| T0.2 — Ba kịch bản thử | ✅ **Xong** | — |
| T0.3 — Script Veo | 🔧 Script xong, **chưa chạy** | `GEMINI_API_KEY` + ảnh thật |
| T0.4 — Script fal.ai | 🔧 Script xong, **chưa chạy** | `FAL_KEY` + ảnh thật |
| T0.5 — Vidu nhiều chủ thể | 🔧 Script xong, **chưa chạy** | key + ảnh thật + người chấm |
| T0.6 — Đo rate limit | 🔧 Script + khung xong, **chưa chạy** | key, **và sẽ tốn tiền** |
| T0.7 — Nghe mù giọng | 🔧 Script xong, **chưa chạy** | `ELEVENLABS_API_KEY`, điền `voices.json`, **5 người nghe** |
| T0.8 — Hai cửa VieNeu | 🟡 **Cửa 2 xong** (Apache-2.0, cho thương mại) · Cửa 1 **chưa** | VPS cài VieNeu để chạy `--gate-check` |
| T0.9 — Chấm điểm | 🔧 Script + khung xong, **chưa chạy** | 12 video thật + **5 người chấm** |

**Definition of Done: 0/9.** Không ô nào tick được cho tới khi có số liệu thật.

### Ba thứ chặn sprint, không phải chuyện code

1. **Tài khoản API có tiền** — Gemini, fal.ai, ElevenLabs
2. **Ảnh sản phẩm thật của một khách thật** — plan cấm ảnh stock, và đúng như vậy: ảnh stock sẽ cho kết quả lạc quan sai
3. **5 người làm marketing** — nửa ngày, cuối tuần

Có đủ ba thứ này thì phần còn lại chạy trong một đến hai ngày, vì script đã sẵn sàng.

### Đã phát hiện được một điều mà không cần chạy gì

Tra giấy phép VieNeu-TTS: **Apache-2.0 cho cả code lẫn model weights**, dùng thương mại được. Nhưng **chỉ tới v2-Turbo — bản v4 là proprietary, không mở**. Chi tiết và hai điều kiện kèm theo ở [`spike/results/vieneu-verdict.md`](../../../spike/results/vieneu-verdict.md).

Cùng lúc phát hiện **tài liệu nguồn nói sai**: nó ghi VieNeu có "23 preset vùng miền", thực tế **chỉ có 3 vùng Bắc/Trung/Nam**. Hệ quả: không thể hứa với khách tính năng "chọn giọng theo tỉnh thành". Biết ở tuần 1 rẻ hơn biết ở tuần 8.

## Task Breakdown

### T0.1 — Dựng chỗ làm việc cho spike

- [x] Tạo thư mục `spike/` ở gốc repo, thêm vào `.gitignore` phần video output (file nặng)
- [x] Tạo `spike/.env.example` liệt kê biến cần: `GEMINI_API_KEY`, `FAL_KEY`, `ELEVENLABS_API_KEY`
- [x] Tạo `spike/README.md` ghi cách chạy và cách đọc kết quả
- [ ] Chuẩn bị `spike/assets/` chứa ảnh sản phẩm thật (3 sản phẩm) — **khung thư mục đã có, ảnh thì chưa**

**Files đã tạo:**
```
spike/README.md              # cách chạy, cách đọc kết quả, danh sách file
spike/.env.example           # biến cần + ghi chú vì sao từng biến tồn tại
spike/common.py              # dùng chung: .env, HTTP, ffprobe, ghi kết quả
spike/assets/README.md       # vì sao cấm ảnh stock
spike/assets/{product-a,product-b,product-c}/.gitkeep
spike/output/.gitkeep        # gitignore nội dung, không gitignore thư mục
spike/results/{KET-LUAN,ratelimit,vieneu-verdict,daily-cost}.md
```

> `.gitignore` chặn **nội dung** chứ không chặn thư mục (`spike/output/*` kèm `!spike/output/.gitkeep`). Viết `spike/output/` thì git loại trừ cả thư mục và **không thể re-include `.gitkeep` bên trong** — repo clone về sẽ thiếu khung thư mục.
>
> `spike/results/` **vẫn được commit**: đó là lý do tồn tại của sprint này. Bốn file trong đó hiện là khung với ô `_điền_`, mỗi file có dòng cảnh báo ở đầu để không ai tưởng nhầm là đã có số liệu.

> Để `spike/` **ngoài** `AdVideo/`. Đây là code vứt đi, không phải nền móng. Trộn vào solution sẽ có người tưởng nó là thật.

### T0.2 — Ba kịch bản thử, dùng chung cho cả 4 provider

- [x] **Kịch bản A — Sản phẩm tĩnh, camera chuyển động**: chai/hộp/gói sản phẩm trên bàn, camera đẩy chậm. Đây là ca dễ nhất, provider nào cũng phải làm được.
- [x] **Kịch bản B — Có người cầm sản phẩm**: kiểm tra việc *"gương mặt có bị méo không"* và *"tay có 6 ngón không"*. Đây là ca hay hỏng nhất.
- [x] **Kịch bản C — Sản phẩm trong bối cảnh Việt Nam**: quán cà phê vỉa hè / chợ / căn hộ nhỏ. Kiểm tra model có hiểu bối cảnh Việt không hay ra bối cảnh Trung/Thái/phương Tây.
- [x] Viết prompt **giống hệt nhau** cho cả 4 provider, lưu vào `spike/prompts/{a,b,c}.txt`

> Đừng tinh chỉnh prompt riêng cho từng provider ở bước này. Mục tiêu là so sánh công bằng, không phải lấy kết quả đẹp nhất.

**Files đã tạo:** `spike/prompts/{a-static-product,b-person-holding,c-vietnam-scene,d-multi-subject}.txt` + `spike/prompts/README.md`

> Thêm kịch bản **D** ngoài plan: ca 3 chủ thể cho T0.5, để prompt của T0.5 cũng nằm chung chỗ và cùng quy tắc với ba ca kia.
>
> `prompts/README.md` ghi lại ba quyết định khi viết prompt, để Sprint 2 port sang LLM đạo diễn không phải đoán: **tiếng Anh** (model huấn luyện chủ yếu trên chú thích tiếng Anh; brief tiếng Việt sẽ do LLM dịch ở bước 2), **câu phủ định nằm trong prompt dương** (không phải provider nào cũng có trường `negative_prompt`), và **thời lượng đặt bằng tham số API chứ không viết vào prompt** (mỗi provider hiểu "8 seconds" một kiểu).

### T0.3 — Script gọi Veo 3.1 (Gemini API trực tiếp)

- [ ] Script gọi `veo-3.1-fast` với image-to-video, ảnh từ `spike/assets/`
- [ ] Ghi lại: thời gian từ lúc gửi tới lúc có file, kích thước file, độ phân giải, có audio gốc không
- [ ] **Thử riêng kịch bản B** để xem Veo có từ chối ảnh có mặt người không — ghi lại **nguyên văn thông báo lỗi**
- [ ] Thử thêm `veo-3.1-lite`, so sánh chất lượng/giá với Fast
- [ ] Ghi chú thời hạn lưu file trên server Google (**~2 ngày**) — xác nhận bằng cách thử tải lại link sau 48 giờ

**Files tạo:** `spike/run_veo.py` (hoặc `.sh`), `spike/output/veo/*`

> Bẫy đã biết: Veo giữ file khoảng 2 ngày rồi xoá. Sprint 1 phải tải về MinIO ngay, không lưu URL.

### T0.4 — Script gọi Kling 3.0, Seedance 2.0, Vidu Q2 Pro qua fal.ai

- [ ] Một script chung, đổi model id — fal.ai dùng chung một dạng request
- [ ] Chạy cả 3 kịch bản cho cả 3 model
- [ ] **Kling**: kiểm tra riêng kịch bản B (chuyển động người) — đây là điểm mạnh được quảng cáo của Kling
- [ ] **Seedance**: ghi lại độ dài tối đa một lần gọi và bước nhảy thời lượng cho phép
- [ ] **Vidu**: xem mục T0.5

**Files tạo:** `spike/run_fal.py`, `spike/output/{kling,seedance,vidu}/*`

### T0.5 — Thử riêng khả năng nhiều chủ thể của Vidu

- [ ] Dựng một ca **3 chủ thể trong một khung**: người + sản phẩm + logo/biển hiệu
- [ ] Chạy 3 lần cùng prompt, xem có bao nhiêu lần giữ được cả 3 chủ thể nhận ra được
- [ ] So sánh cùng ca đó trên Veo và Kling để biết Vidu có thật sự hơn không
- [ ] **Kiểm tra mặc định âm thanh của Vidu** — có sinh audio kèm không, có bật sẵn không

**Vì sao tách riêng:** đây là lý do duy nhất để giữ Vidu trong danh sách. Nếu nó không hơn ở khoản này thì bỏ luôn cho gọn.

> Ghi vào kết quả: **Vidu mặc định bật âm thanh.** Nếu đúng, Sprint 1 phải tắt tường minh ở adapter, nếu không video ra sẽ có hai lớp tiếng chồng nhau.

### T0.6 — Đo rate limit thật của từng tài khoản

- [ ] Với mỗi provider: bắn **2, rồi 4, rồi 8** request đồng thời, ghi lại lúc nào bắt đầu bị từ chối
- [ ] Ghi lại **mã lỗi và header** khi bị giới hạn (`429`? có `Retry-After` không?)
- [ ] Đo số request tối đa trong 1 phút với cùng một key
- [ ] Ghi lại thời gian trung bình + tệ nhất của một lần render 8 giây

**Files tạo:** `spike/measure_ratelimit.py`, `spike/results/ratelimit.md`

> Đây là task dễ bị bỏ qua nhất và ảnh hưởng kiến trúc nhiều nhất. Một video 30 giây = 4 shot. Nếu key chỉ chịu 2 request đồng thời thì "render song song các shot" trong thiết kế phải thành hàng đợi có giới hạn, và thời gian job dài gấp đôi dự tính.

### T0.7 — Nghe mù giọng đọc tiếng Việt

- [ ] Lấy **10 giọng** từ ElevenLabs Voice Library có tiếng Việt (nam/nữ, các vùng miền)
- [ ] Đọc cùng **một đoạn lời quảng cáo có tên sản phẩm và số tiền** — ép model xử lý số và tên riêng
- [ ] Thêm 3 mẫu từ **VieNeu-TTS** vào cùng danh sách, **không gắn nhãn nguồn**
- [ ] Cho 5 người nghe, chấm 1–5, chọn "giọng nào bạn dám gửi cho khách"
- [ ] Ra danh sách **8–12 giọng tuyển chọn**, ghi rõ giọng nào hợp ngành nào

**Files tạo:** `spike/output/voices/*.mp3`, `spike/results/voice-scorecard.md`

> Mẫu phải có **số tiền và tên thương hiệu**. Giọng nào cũng đọc hay câu văn trơn; chỗ lộ chất lượng là "1.990.000đ" và "Vinamilk Optimum Gold".

### T0.8 — Hai cánh cửa của VieNeu-TTS

- [ ] **Cửa 1 — Có timing theo ký tự / theo từ không?** Không có thì không khoá được timeline, chỉ dùng được cho tier Nháp. Kiểm tra bằng cách đọc output API/thư viện xem có mốc thời gian không.
- [ ] **Cửa 2 — Giấy phép có cho dùng thương mại không?** Đọc `LICENSE` của repo **và** license của model weights — hai thứ này có thể khác nhau.
- [ ] Nếu qua cả hai cửa: đo **RTF** (real-time factor) trên CPU của VPS — 10 giây audio mất bao lâu
- [ ] Thử clone giọng từ mẫu 3–8 giây, nghe xem có dùng được không
- [ ] Ghi lại số preset vùng miền thật sự dùng được (tài liệu nói 23)

**Files tạo:** `spike/results/vieneu-verdict.md`

**Kết luận phải dứt khoát**, một trong ba:
- ✅ Dùng cho cả Nháp và Thành phẩm
- ⚠️ Chỉ dùng cho tier Nháp (ElevenLabs gánh thành phẩm) ← khả năng cao nhất
- ❌ Không dùng

### T0.9 — Chấm điểm và chọn provider mặc định

- [ ] Ghép 12 video vào một trang xem được (HTML tĩnh là đủ), **giấu tên provider**
- [ ] 5 người chấm mỗi video theo 4 tiêu chí: *dùng được cho khách không* / *sản phẩm có bị biến dạng không* / *bối cảnh có ra Việt Nam không* / *chuyển động có tự nhiên không*
- [ ] Tổng hợp thành bảng: provider × kịch bản × điểm trung bình
- [ ] Đối chiếu hoá đơn thật với giá niêm yết, ghi chênh lệch
- [ ] Viết trang kết luận: **provider nào làm mặc định cho Sprint 1**, vì sao, và provider nào bị loại

**Files tạo:** `spike/results/scorecard.html`, `spike/results/KET-LUAN.md`

## Kiểm chứng

Sprint 0 không có test tự động. Kiểm chứng bằng cách trả lời được các câu sau, có số liệu kèm:

```
1. Provider nào điểm cao nhất ở kịch bản B (có người)?          → tên + điểm
2. Một video 8 giây tốn thật bao nhiêu đô ở mỗi provider?        → 4 con số từ hoá đơn
3. Một key chạy được bao nhiêu job song song?                    → 4 con số
4. Veo từ chối ảnh có mặt người ở mức nào?                       → nguyên văn lỗi
5. Vidu có mặc định bật âm thanh không?                          → có/không
6. VieNeu có timing ký tự không?                                 → có/không
7. VieNeu license có cho thương mại không?                       → trích dẫn license
8. Giọng nào đọc "1.990.000đ" nghe tự nhiên nhất?                → tên giọng + id
```

Nếu còn câu nào chưa trả lời được thì **chưa đóng sprint** — Sprint 1 sẽ code dựa trên phỏng đoán.

## Rủi ro trong sprint

| Rủi ro | Dấu hiệu | Phản ứng |
|---|---|---|
| **Không provider nào đạt 3/5** | Bảng chấm điểm thấp đều | **Dừng dự án lại để xem lại giả định**, không lao vào Sprint 1. Đây là kết quả hợp lệ của Sprint 0. |
| Chờ render lâu hơn dự tính | Một video 8 giây mất > 5 phút | Giảm còn 2 kịch bản × 4 provider; kịch bản C ít quan trọng nhất |
| Rate limit chặn ngay từ 2 request | Bị `429` liên tục | Ghi lại và **báo ngay cho phần thiết kế Sprint 1** — ảnh hưởng kiến trúc |
| Ảnh khách quá xấu, mọi provider đều ra tệ | Video nào cũng mờ/méo | Đây cũng là phát hiện có giá trị: cần thêm bước nâng chất ảnh đầu vào. Ghi vào kết luận. |
| fal.ai và gọi trực tiếp cho kết quả khác nhau | Kling qua fal khác Kling gọi thẳng | Ghi lại; Sprint 1 vẫn đi qua fal.ai cho gọn, nhưng biết là có chênh |
| VieNeu không cài được trên VPS | Thiếu dependency, quá nặng | Kết luận ❌, ghi rõ lý do; không đốt thêm thời gian |

## Ra khỏi sprint

- `spike/results/KET-LUAN.md` — chọn provider mặc định, có số liệu chống lưng
- `spike/results/ratelimit.md` — đầu vào bắt buộc cho thiết kế hàng đợi ở Sprint 1
- `spike/results/vieneu-verdict.md` — quyết định VieNeu vào tier nào
- `spike/results/voice-scorecard.md` — danh sách giọng tuyển chọn, dùng làm seed `VoiceProfile` ở Sprint 5
- 12 video mẫu — dùng để thuyết phục khách pilot ở Sprint 6

## Chuyển sang Sprint 1

Sprint 1 bắt đầu bằng việc **đọc `KET-LUAN.md`** và chọn provider mặc định từ đó, không chọn theo cảm tính hay theo tài liệu marketing của nhà cung cấp.
