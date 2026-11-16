# Luồng demo: xem lại kết quả thi placement

Tất cả bước bấm chuột, theo đúng thứ tự. Mở `http://localhost:5000`.

---

## 0. Chuẩn bị (1 lần, trước khi demo)

```powershell
docker compose up -d
```

Chờ đến khi `docker compose ps` thấy cả 3 service `Up`. Nếu Docker đã chết:

```powershell
. .\scripts\docker-keepalive.ps1; Assert-Docker; docker compose up -d
```

**Tài khoản:** `learner@demo.local` / `Demo1234!`

> Tài khoản này cần đã có ít nhất một placement đã chấm xong. Nếu DB vừa reset, chạy trước:
> `powershell -NoProfile -File scripts/walk-placement.ps1 -Email learner@demo.local`
> (script tự làm hết, ~1 phút; đợi thêm ~40 giây để AI chấm xong)

---

## 1. Đăng nhập

| Bước | Bấm gì | Thấy gì |
|---|---|---|
| 1.1 | Mở `http://localhost:5000` | Trang chủ hoặc `/dashboard` |
| 1.2 | Góc phải trên → **Sign out** (nếu đang đăng nhập ai khác) | Về trang đăng nhập |
| 1.3 | Ô **Email** → `learner@demo.local` | |
| 1.4 | Ô **Password** → `Demo1234!` | |
| 1.5 | Nút **Sign in** | Vào `/dashboard` |

**Kiểm tra đúng người:** góc phải trên phải ghi **Demo Learner**, không phải "Admin User".

---

## 2. Vào trang kết quả placement

Có **hai đường**, cả hai đều đúng. Chọn đường bạn thấy rõ hơn khi demo.

### Đường A — mở thẳng bằng địa chỉ (đường chắc chắn nhất)

Dashboard **không** có link tới kết quả placement. Sau khi nộp bài, UI điều hướng thẳng tới
`/placement/result/{attemptId}` — nhưng rời trang rồi thì không có nút quay lại từ Dashboard.

Vì vậy khi demo, đường chắc chắn nhất là gõ địa chỉ:

| Bước | Làm gì | Thấy gì |
|---|---|---|
| 2A.1 | Trên thanh địa chỉ gõ `localhost:5000/placement/result/1` | Trang kết quả |

> **Vì sao id là `1`:** sau một lần reset DB rồi chạy `walk-placement.ps1`, placement đầu tiên
> luôn có id `1`. Nếu bạn đã làm nhiều lần, kiểm tra id bằng:
> `docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Str0ng!Passw0rd' -C -d ExtraEfficiency -Q "SELECT PlacementId, Status FROM PlacementTests"`

### Đường B — làm mới một bài rồi xem ngay (nếu không có sẵn kết quả)

| Bước | Bấm gì | Thấy gì |
|---|---|---|
| 2B.1 | Sidebar trái → **Placement** | `/placement` — danh sách đề |
| 2B.2 | Bấm **Start free placement** | Vào phòng thi |
| 2B.3 | Làm bài (bấm đáp án từng câu) | |
| 2B.4 | Bấm **Submit** ở câu cuối | Tự chuyển sang `/placement/result/{id}` |
| 2B.5 | Đợi ~15 giây, F5 | Nút **Review every answer** bật lên |

---

## 3. Đọc trang kết quả

Trang `/placement/result/1` hiện:

| Khối | Nội dung |
|---|---|
| **Band** | `B1 — Intermediate (4.0–5.0)`, kèm danh sách band B1/B2/C1/C2 để so |
| **Điểm từng kỹ năng** | Listening / Reading / Writing / Speaking, mỗi cái một trạng thái |
| **Chứng chỉ khác** | Cùng bài làm quy đổi sang TOEIC (ví dụ `206 / A1`) — **không phải lỗi** |
| **Nút Review every answer** | Đây là nút quan trọng nhất — bấm tiếp bước 4 |

**Bốn trạng thái có thể thấy ở mỗi kỹ năng:**

| Trạng thái | Nghĩa | Nói gì khi demo |
|---|---|---|
| `measured` | Đã đo được | "Kỹ năng này có điểm" |
| `not_measured` | Không đo được (bỏ trống / grader từ chối) | "Có lý do, xem bên dưới" |
| `grading` | AI đang chấm | "Chờ vài giây" |
| *(ẩn)* | Không áp dụng | |

> **Nếu nút Review chưa bấm được:** nghĩa là còn bài đang chấm. Đợi ~10 giây, tải lại trang
> (F5). Nút sẽ bật khi mọi bài tự luận có kết luận.

---

## 4. Mở trang xem lại chi tiết ← **ĐÂY LÀ PHẦN CHÍNH**

| Bước | Bấm gì | Thấy gì |
|---|---|---|
| 4.1 | Nút **Review every answer** | Chuyển sang `/placement/review/1` |
| 4.2 | Nhìn thanh địa chỉ | `.../placement/review/1` — URL này **giữ được khi F5**, dùng để chứng minh |
| 4.3 | Cuộn xuống | 12 thẻ câu hỏi, đánh số 1 → 12 |

### 4a. Phần trắc nghiệm (câu 1–10)

Mỗi thẻ có:
- Chip kết quả: **Correct** (xanh) / **Incorrect** (đỏ) / **Left blank** (xám)
- Từng đáp án A/B/C/D, đáp án bạn chọn được đánh dấu
- Giải thích đáp án đúng

### 4b. Phần bài viết — **chỉ cho giáo viên xem AI chấm writing**

| Bước | Bấm gì | Thấy gì |
|---|---|---|
| 4b.1 | Cuộn tới **Question 11** (hoặc thẻ có nhãn `T1` / `T2`) | |
| 4b.2 | Nhìn chip góc trên bên phải thẻ | **Graded** / **Not graded** / **Awaiting grading** |
| 4b.3 | Nhìn khối **Your response** | Nguyên văn bài viết của học viên |
| 4b.4 | Nếu **Graded** → nhìn dòng **AI graded score** | Ví dụ `13.7 pts` |
| 4b.5 | Nếu **Graded** → nhìn **Rubric breakdown** | Từng tiêu chí + điểm (vd `3.3/9`) + lời bình |
| 4b.6 | Nếu **Not graded** → đọc dòng chữ vàng | Lý do nguyên văn của grader |

**Câu để nói khi demo:**

> "Đây là bài viết của học viên. Nếu AI chấm được, nó cho điểm tổng và điểm từng tiêu chí
> rubric kèm lời bình: Task Response, Coherence, Lexical Resource, Grammar.
> Nếu AI **từ chối** chấm — ví dụ bài không trả lời đề — trang không im lặng bỏ qua.
> Nó ghi **Not graded** và in đúng lý do của grader."

### 4c. Phần bài nói — **transcript + điểm + nghe lại**

| Bước | Bấm gì | Thấy gì |
|---|---|---|
| 4c.1 | Cuộn tới **Question 12** (hoặc thẻ có nhãn `INT`) | |
| 4c.2 | Nhìn chip | **Graded** nếu đã chấm |
| 4c.3 | Nhìn khối **Your response** | Transcript lời nói được chuyển thành chữ |
| 4c.4 | Bấm **nút Play** của trình phát audio | Nghe lại được giọng học viên |
| 4c.5 | Nhìn **Rubric breakdown** | 4 tiêu chí: Fluency and Coherence, Lexical Resource, Grammatical Range, Pronunciation — mỗi cái có điểm và nhận xét |

**Câu để nói khi demo:**

> "Bài nói được lưu thành audio, chuyển thành transcript, và chấm theo đúng 4 tiêu chí
> IELTS Speaking. Học viên vừa đọc lại được mình đã nói gì, vừa nghe lại được giọng mình,
> vừa thấy điểm từng tiêu chí."

---

## 5. Chứng minh URL bền (nếu giáo viên hỏi)

| Bước | Bấm gì | Thấy gì |
|---|---|---|
| 5.1 | Đang ở `/placement/review/1`, bấm **F5** | Trang tải lại |
| 5.2 | Nhìn lại nội dung | Y nguyên, không mất gì |

**Câu để nói:** "Địa chỉ này là địa chỉ thật, không phải state tạm. Bookmark được, gửi cho
người khác được, tải lại được."

---

## 6. Ba trạng thái của một bài tự luận (bảng để trả lời)

| Chip | Nghĩa | Khi nào |
|---|---|---|
| **Graded** | Đã chấm, có điểm | Grader trả kết quả bình thường |
| **Not graded** | Grader từ chối, có lý do | Bài không trả lời đề, hoặc lỗi model |
| **Awaiting grading** | Đang trong hàng đợi | Vừa nộp, worker chưa chạy |

> **Điểm mấu chốt khi bảo vệ:** "Một cột điểm NULL không phải là không có dữ liệu — nó là một
> sự kiện cần giải thích. Nên trang này phân biệt ba trạng thái, chứ không hiện một dấu gạch."

---

## 7. Nếu có gì trục trặc

| Triệu chứng | Xử lý |
|---|---|
| Trang trắng / 404 | Kiểm tra đúng id: `/placement/result/1` |
| Nút Review bấm không được | Đợi ~10 giây, F5. Còn bài đang chấm |
| Review hiện 400 `placement_not_finished` | Y như trên — chờ chấm xong |
| Không thấy placement nào | Chạy `walk-placement.ps1` (mục 0) |
| Đăng nhập nhầm Admin | Sign out, đăng nhập lại `learner@demo.local` |
| Docker chết | `. .\scripts\docker-keepalive.ps1; Assert-Docker; docker compose up -d` |
| Web hiện bản cũ | `docker compose up -d --build web` rồi Ctrl+F5 |

---

## 8. Thứ tự demo đề xuất (5 phút)

```
1. Đăng nhập learner@demo.local                        (30 giây)
2. Dashboard → thẻ Goal Standing                       (30 giây)  ← chứng minh band nhất quán
3. /placement/result/1 → band B1 + 4 kỹ năng           (60 giây)  ← điểm tổng
4. Bấm "Review every answer"                           (20 giây)
5. Cuộn câu 11 (writing) → rubric hoặc lý do từ chối   (90 giây)  ← AI chấm writing
6. Cuộn câu 12 (speaking) → transcript + audio + 4 tiêu chí (90 giây)  ← phần ấn tượng nhất
7. F5 để chứng minh URL bền                            (20 giây)
```

**Điểm nhấn cuối:** phần speaking (bước 6) là thứ gây ấn tượng nhất — học viên nghe lại được
giọng mình, đọc transcript, và thấy điểm từng tiêu chí. Để dành nó cho cuối.
