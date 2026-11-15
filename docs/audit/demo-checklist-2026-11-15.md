# Checklist demo Speaking / Writing / payOS — 2026-11-15

Mọi bước dưới đây đã được chạy thật trên stack `docker compose` hôm nay. Ba lỗi chặn demo đã sửa
(`docs/audit/demo-blockers-2026-11-15.md`). Checklist này là thứ tự thao tác, không phải lý thuyết.

---

## A. Trước khi demo (làm tối nay, không phải sáng mai)

### A1. Dựng lại stack sạch

```sh
docker compose down -v          # -v là bắt buộc: xoá cả DB và audio
docker compose up -d --build
docker compose ps               # đợi api "healthy", web "running"
```

`down -v` xoá luôn audio đã upload — đúng ý, vì audio cũ thuộc attempt đã chết.

### A2. Kiểm tra 5 con số baseline

```sh
docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa \
  -P '<MSSQL_SA_PASSWORD trong .env.docker>' -C -d ExtraEfficiency -h -1 -W -Q "
SET NOCOUNT ON;
SELECT 'users='+CAST(COUNT(*) AS varchar) FROM Users;
SELECT 'exams='+CAST(COUNT(*) AS varchar) FROM Exams;
SELECT 'seq='+CAST(current_value AS varchar) FROM sys.sequences WHERE name='SEQ_PaymentNo';"
```

| Phải thấy | Giá trị |
|---|---|
| users | **4** |
| exams | **3** (1 TOEIC mock, 1 IELTS mock, 1 Universal Placement) |
| `SEQ_PaymentNo` | **3** trở lên |
| credits `learner@demo.local` | **13** |
| credits `learner.fresh@demo.local` | **0** |

Nếu `seq` = 1, seed chưa được sửa → **checkout sẽ 409 oan**.

### A3. Xác nhận AI thật còn sống

```sh
docker compose exec -T api printenv | findstr AI_PROVIDER
```

Phải là `AI_PROVIDER=real`. Nếu là `fake`, điểm sẽ là số cố định và trông giả trên màn hình.

> Cảnh báo đã kiểm chứng: key Claude hiện tại trả **401** với model `claude-sonnet-3.7` khi gọi
> trực tiếp từ máy, nhưng **chạy được từ trong container**. Test từ host, đừng kết luận key chết.

### A4. Chuẩn bị một file audio nói thật

Micro của máy demo sẽ thu âm, nhưng nên có sẵn `docs/audit/152-speaking-sample-1e.mp3` để dự phòng
nếu micro hội trường không vào được. Copy vào container:

```sh
docker compose cp docs/audit/152-speaking-sample-1e.mp3 api:/tmp/speech.mp3
```

### A5. Mở sẵn 3 tab

| Tab | URL | Account |
|---|---|---|
| 1 | http://localhost:5000/onboarding | `learner.fresh@demo.local` |
| 2 | http://localhost:5000/pricing | `learner.fresh@demo.local` |
| 3 | http://localhost:5000/writing | `learner@demo.local` |

---

## B. Chạy thử toàn bộ một lượt (bắt buộc, tối nay)

Đây là bẫy lớn nhất: **`/writing` và `/speaking` bị soft-lock cho tới khi xong OOBE**, và seed
**cố ý không** có placement. Không ai vào thẳng được. Phải đi hết luồng dưới đây ít nhất một lần.

### B1. Placement 4 kỹ năng (cửa duy nhất mở Speaking/Writing)

1. Đăng nhập `learner.fresh@demo.local` → `/onboarding`.
2. Chọn **"4 skills — Listening, Reading, Speaking & Writing"**. Nếu bấm không ăn → lỗi #1 đã tái phát.
3. **Start Universal Placement** → **Confirm ready & begin**.
4. Trả lời 5 câu Listening.
5. Bấm **Next** ở câu cuối → hộp thoại **"Advance to next locked section?"** → **Continue to next section**.
6. Trả lời 5 câu Reading → tiếp tục như bước 5.
7. **Writing**: gõ ≥ 150 từ vào ô. Đếm từ phải nhảy. Bấm **Save draft**.
8. **Speaking**: **Start recording** → nói 5–10 giây → **Stop recording** → đợi **"Spoken response saved"**.
9. **Review & submit** → **Submit answers now**.
10. Đợi 10–30 giây. Kết quả phải có **Writing** và **Speaking** khác 0.

| Phải thấy | Giá trị đo được hôm nay |
|---|---|
| `writingPct` | 61.11 |
| `speakingPct` | 36.11 |
| `overallPct` | 34.30 |

Nếu Writing hoặc Speaking = **0.00** trong khi đã trả lời → lỗi #5 tái phát.

11. Chọn target band → **Generate learning path** (tốn **5 credit**).

### B2. Speaking / Writing practice (giờ đã mở khoá)

1. Vào `/writing`. Phải thấy danh sách exam, **không** còn màn "Guided Setup Required".
2. `learner@demo.local` có **13 credit**; mỗi lần submit trừ **1** (`ai_grading`).
3. `/speaking` tương tự. Dùng micro thật, hoặc file dự phòng ở A4.

> Đã kiểm chứng: seed **không có exam `speaking_practice`/`writing_practice` nào**. Nếu ô danh sách
> trống, đó là thiếu nội dung chứ không phải lỗi — placement ở B1 vẫn là đường demo Speaking/Writing
> chắc chắn nhất.

### B3. payOS (mock) — 3 nhánh

1. Đăng nhập `learner.fresh@demo.local`, vào `/checkout`.
2. Bấm **Proceed to payment** → hiện **Mock Gateway Simulator**.
3. **Simulate payment success** → về `/pricing`, số dư **0 → 5**.
4. Vào `/checkout` lần nữa → **Simulate failed** → order `failed`, mã lỗi **24**, **không** cộng credit.
5. Lặp lại → **Simulate expired** → order `expired`, không cộng credit.

Kiểm chứng ledger (đây là điều giảng viên sẽ hỏi):

```sh
docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '<pass>' \
  -C -d ExtraEfficiency -h -1 -W -Q "SET NOCOUNT ON;
SELECT Reason+' '+CAST(Delta AS varchar) FROM CreditTransactions c
JOIN Users u ON u.UserId=c.UserId WHERE u.Email='learner.fresh@demo.local';"
```

Phải thấy **đúng một** dòng `purchase 5`. Không có cột balance trong DB — số dư là `SUM(Delta)`.

---

## C. Câu trả lời khi bị hỏi (bản chất hệ thống)

| Câu hỏi | Trả lời | Chỗ trong code |
|---|---|---|
| Writing chấm thế nào? | Nộp bài → trừ 1 credit (`ai_grading`) → ghi hàng đợi `AiGradings` → worker nền 2s nhặt → Claude chấm theo rubric, trả điểm từng tiêu chí + `errorSpans` + `firstFix` + `modelRewrite`. Request không bị block. | `AiGradingProcessor.cs`, `ClaudeClient.cs` |
| Speaking chấm thế nào? | Ghi âm ở browser → upload → SpeechSuper chấm acoustic thang 0–100 (phát âm/fluency/ngữ pháp/từ vựng) → rubric của certificate nhân tỷ lệ lên. Transcript của SpeechSuper còn được Claude chấm lại phần ngôn ngữ. Hai nguồn gộp. | `SpeechSuperAiGrader.cs` |
| Vì sao webm gửi thành `opus`? | SpeechSuper chỉ nhận `wav/mp3/opus/ogg/amr`. Chrome ghi webm-container-of-Opus, provider nhận dưới tên `opus`. Cùng một file, nhãn `webm` → lỗi 41002; nhãn `opus` → chấm bình thường, điểm y hệt bản mp3. Không transcode. | `SpeechSuperAiGrader.cs` |
| Số dư credit lấy từ đâu? | Luôn là `SUM(Delta)` trên `CreditTransactions`. **Không có cột balance** để lệch. | `CreditLedger.cs` |
| Làm sao chắc không trừ tiền 2 lần? | "Block before you charge": kiểm tra đủ điều kiện **trước**, rồi mới ghi debit trong cùng transaction. Hết credit → chặn ở start, không tạo dòng nào. | `CreditLedger.cs` |
| payOS thật hay giả? | Demo chạy **mock**: `Payment__Gateway=mock`. Bốn nhánh bấm tay. Đổi sang `payos` cần webhook URL công khai — localhost không nhận được. | `docker-compose.yml` |
| Vì sao mỗi lúc một order? | Index `UX_Orders_OnePendingPerUser`. Order đang mở → 409. | `database-schema.sql` |

---

## D. Bẫy đã gặp — đừng lặp lại

| Bẫy | Cách tránh |
|---|---|
| **Placement có cooldown 24 giờ.** Retake bị 429 kèm giờ mở. | Chỉ demo **một** lượt placement. Muốn thử lại thì `docker compose down -v` rồi dựng lại. **Đừng** đốt lượt thử trên `learner@demo.local` trước giờ demo. |
| **Writing/Speaking soft-lock sau OOBE.** | Đi hết OOBE (placement → band → path) trước, đừng để tới lúc demo mới làm. |
| **Seed không có exam practice S/W.** | Đừng hứa "xem writing practice có sẵn". Đường chắc chắn: placement 4 kỹ năng. |
| **Chrome ghi `audio/webm;codecs=opus`.** | Đã sửa ở cả validator (`FileStore.cs`) và grader (`SpeechSuperAiGrader.cs`). Nếu upload đỏ, kiểm tra hai file này còn fix không. |
| **`dotnet run` trần trả 500 ở checkout.** | Luôn `docker compose up`, hoặc `Payment__Gateway=mock`. |
| **Micro hội trường.** | Chuẩn bị sẵn file audio ở A4. |
| **Full test suite 33 fail.** | Đó là **có từ trước** trên `main`, không phải do hôm nay. Đừng panic, đừng "sửa" lúc 23h. Chạy filter hẹp thay vì cả suite. |

---

## E. Sáng mai, 10 phút trước giờ

```
[ ] docker compose ps          → api healthy, web running
[ ] curl http://localhost:5001/healthz   → Healthy
[ ] Đăng nhập learner.fresh   → /onboarding mở được
[ ] Bấm thử 1 lần radio "4 skills" → có đổi (không bấm Start, không đốt lượt)
[ ] /checkout hiện đúng số dư
[ ] Audio dự phòng đã copy vào container (A4)
[ ] Tắt thông báo, tắt tab thừa
```

Lưu ý: bấm thử radio rồi rời trang thì không tạo attempt, không tốn credit, không dính cooldown.
Chỉ **Start Universal Placement** mới tính.
