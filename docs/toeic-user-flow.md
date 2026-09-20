# Luồng nghiệp vụ TOEIC — kể theo tác nhân

Tài liệu này trả lời câu hỏi "người dùng đi qua những bước nào", khác với
`2026-09-20-toeic-ui-design.md` trả lời "mỗi màn hình có state gì". Mỗi bước
đều ghi rõ file prototype để tra ngược được.

## 0. Tác nhân

| Tác nhân | Vào ở đâu | Việc chính |
|---|---|---|
| **Khách** | `auth/register.html`, `auth/login.html` | Tạo tài khoản, đăng nhập |
| **Học viên** | `student/dashboard.html` | Luyện miễn phí, xác thực CCCD, mua lượt, thi thật |
| **Quản trị viên** | `admin/dashboard.html` | Duyệt KYC, soạn đề, quản lý gói/đơn, bảng quy đổi điểm |

Hai điều tác nhân **Học viên** phải hiểu ngay từ đầu, vì chúng quyết định
toàn bộ luồng:

1. **Luyện tập và phân tích điểm yếu miễn phí, mãi mãi.** Không có paywall,
   không CTA mua gói trong bất kỳ màn luyện tập nào.
2. **Xác thực CCCD là bắt buộc trước khi thi thật, nhưng bản thân nó miễn
   phí và không trừ lượt.** Lượt chỉ bị trừ khi bắt đầu một bài thi thật.

## 1. Bản đồ tổng quát

```
Khách ──đăng ký/đăng nhập──► Học viên ──► Dashboard
                                              │
        ┌─────────────────────────────────────┤
        │                                     │
        ▼                                     ▼
  [MIỄN PHÍ] luyện theo part            [TRẢ TIỀN] thi thật
  practice-select → practice-take              │
        → practice-summary                     │  ┌── chưa xác thực CCCD ──► chặn
        │                                      │  │      (không trừ lượt)
        ▼                                      ├──┼── 0 lượt ──► paywall
  weakness-analysis (miễn phí)                 │  │      (không trừ lượt)
        │                                      │  │
        └──── chỉ ra chỗ yếu ─────────────────►│  └── đủ điều kiện
                                               ▼
                              trừ 1 lượt → exam-instructions
                                               ▼
                              exam-listening → exam-reading
                                               ▼
                              exam-confirm-submit → exam-result (+ CEFR)
                                               ▼
                              exam-review · exam-history
```

Vòng lặp học tập là **miễn phí ở cả hai đầu**: luyện tập sinh ra dữ liệu,
phân tích điểm yếu biến dữ liệu đó thành "bạn yếu Part 7" — và đó chính là
lý do học viên thấy cần thi thật. Đây là phễu, không phải tính năng phụ.

## 2. Học viên — từng bước

### 2.1 Khách tạo tài khoản

| Bước | Màn | Ghi chú |
|---|---|---|
| 1 | `auth/register.html` | Email + mật khẩu. Không cần KYC để đăng ký. |
| 2 | `auth/login.html` | — |
| 3 | `auth/forgot-password.html` | Luồng quên mật khẩu |

KYC **không** nằm ở bước này. Bắt xác thực ngay lúc đăng ký là chặn người
dùng trước khi họ thấy sản phẩm có gì — mà luyện tập thì miễn phí.

### 2.2 Học viên mới: luyện tập miễn phí

Vào thẳng từ sidebar: **Luyện theo part**.

| Bước | Màn | Việc xảy ra |
|---|---|---|
| 1 | `practice-select.html` | Chọn part (Part 1–7) và số câu |
| 2 | `practice-take.html` | Làm từng câu, **feedback ngay sau mỗi câu** |
| 3 | `practice-summary.html` | Tổng kết phiên: độ chính xác từng part |

Ở bước 2, khác với thi thật: đáp án và giải thích hiện **ngay**, không đợi
nộp bài. Đây là điểm bán của chế độ luyện.

Không bước nào ở đây hỏi lượt thi, hỏi CCCD, hay hiện paywall.

### 2.3 Phân tích điểm yếu (miễn phí)

`weakness-analysis.html` — gộp dữ liệu từ **cả** phiên luyện **và** bài thi
đã chấm, nên nó vẫn hữu ích cho học viên chưa từng thi thật.

Màn này trả lời ba câu, theo thứ tự:

1. **Độ chính xác từng part, xếp yếu nhất trước** — không xếp theo số part,
   vì học viên cần thấy chỗ yếu trước tiên.
2. **Nên luyện gì trước** — chọn part vừa yếu vừa chiếm nhiều câu.
3. **Dạng câu hay sai** — sai ở *dạng câu hỏi* nào, không chỉ part nào.

Có ghi chú thẳng rằng số liệu dạng câu hỏi cần dữ liệu gắn nhãn ở tầng nội
dung, tức là phụ thuộc vào chất lượng ngân hàng câu hỏi.

### 2.4 Chạm paywall: mua lượt

Học viên bấm vào một đề full ở `exam-list.html`. Nếu ví 0 lượt:

```
exam-list.html (paywall)  →  payment/pricing.html
                          →  payment/checkout.html
                          →  payment/gateway-mock.html   (cổng vẽ giả)
                          →  payment/payment-result.html (4 nhánh)
                          →  payment/wallet.html
```

Bốn nhánh ở `payment-result.html`: `success` / `failed` / `pending` /
`expired`. `expired` tách khỏi `failed` vì thông điệp khác nhau: hết hạn thì
mời tạo đơn mới, cổng lỗi thì mời thử lại.

Ràng buộc: **mỗi tài khoản chỉ được có một đơn `pending`**. `checkout.html`
chặn ở tầng UI và DB ép bằng unique index có điều kiện. Không chặn thì bấm
hai lần thành hai đơn, trả tiền hai lần.

Lượt là **tổng của các giao dịch**, không phải một cột đếm — nên ví luôn
đối chiếu được với lịch sử giao dịch.

### 2.5 Xác thực CCCD

Vào từ sidebar **Xác thực CCCD** (gốc nhóm là `kyc-pending.html`), hoặc từ
thẻ KYC ở `profile.html`.

| Bước | Màn | Việc xảy ra |
|---|---|---|
| 1 | `kyc-submit.html` | Khai họ tên, ngày sinh, số CCCD; tải 3 ảnh; tích cam kết |
| 2 | `kyc-pending.html?state=pending` | Hồ sơ đang chờ duyệt |
| 3 | *(admin duyệt — xem §3.1)* | |
| 4 | `kyc-pending.html?state=approved` | Đã duyệt → mở cổng thi thật |
| 4' | `kyc-pending.html?state=rejected` | Bị từ chối → hiện **nguyên văn** lý do, có nút gửi lại |

Ba ảnh bắt buộc: mặt trước CCCD, mặt sau CCCD, ảnh chân dung. Form có sẵn
trạng thái thiếu ảnh chân dung để thấy việc chặn thiếu ảnh trông thế nào.

Cam kết phải tích: xác nhận là CCCD của chính mình, và hiểu rằng dùng giấy
tờ người khác sẽ bị khoá tài khoản. Một CCCD chỉ gắn được với một tài khoản.

Có nút **"Để sau"** — vì chưa xác thực thì vẫn luyện tập được, nên không
được nhốt người dùng vào form.

**Vì sao cần state `approved` riêng:** nếu không có, tín hiệu duy nhất học
viên nhận được là cổng chặn ở `exam-list.html` *tự biến mất* — một tín hiệu
im lặng, dễ bị bỏ qua hoặc hiểu nhầm thành lỗi. Khối `approved` nói rõ duyệt
là **mở cổng, không trừ lượt**.

### 2.6 Thi thật

Điều kiện vào: **đã xác thực CCCD** *và* **còn lượt**.

| Bước | Màn | Việc xảy ra |
|---|---|---|
| 1 | `exam-list.html` | Chọn đề |
| 2 | `exam-instructions.html` | Xác nhận **trừ 1 lượt** |
| 3 | `exam-listening.html` | Part 1–4, audio **không tua**, khoá quay lại part trước |
| 4 | `exam-reading.html` | Part 5–7, di chuyển tự do |
| 5 | `question-navigator.html` | Panel 200 ô, tô màu theo state từng câu |
| 6 | `exam-confirm-submit.html` | Cảnh báo số câu chưa làm |
| 7 | `exam-result.html` | Điểm L / R / Total + **thanh CEFR** |
| 8 | `exam-review.html` | Đáp án + giải thích (mở khoá sau khi chấm) |
| 9 | `exam-history.html` | Lịch sử các lần thi, kèm bậc CEFR từng lần |

Listening và Reading **tách màn riêng** vì ràng buộc điều hướng ngược nhau.
Nhét chung một view sẽ thành một đống `if`.

Đề: 200 câu / 120 phút; Listening và Reading mỗi phần 5–495; tổng 10–990.
Part 2 chỉ có 3 lựa chọn — số lựa chọn phải lấy động, không hardcode 4.

### 2.7 Điểm và bậc CEFR

Điểm đi theo đường: **raw → scaled → bậc CEFR**.

- `raw → scaled` **khác nhau giữa các đề** (độ khó khác nhau), nên admin sửa
  được, và bảng này thuộc về từng đề.
- `scaled → CEFR` là **hằng số theo chuẩn**, suy ra từ tổng điểm. Admin
  **không sửa được** — `admin/score-conversion.html` hiển thị nó chỉ đọc,
  không có ô nhập.

Bậc CEFR hiện cho học viên bằng một **thanh** ở `dashboard.html`,
`exam-history.html` và `exam-result.html`: sáu khoảng (`<A1`, `A1`, `A2`,
`B1`, `B2`, `C1`) trên thang 10–990, có mốc 120/225/550/785/945/990 và
đánh dấu khoảng đang đứng. Thanh này để học viên thấy mình đang ở đâu và
còn cách bậc kế tiếp bao xa — một con số 785 không tự nói lên điều đó.

## 3. Quản trị viên — từng bước

### 3.1 Duyệt KYC — chặn hay mở cổng cho học viên

`admin/kyc-review.html`. Hàng đợi hiện học viên, tên trên CCCD, số CCCD đã
che, thời điểm gửi và **thời gian chờ** (để hồ sơ cũ nổi lên trước).

| Hành động | Kết quả phía học viên |
|---|---|
| Duyệt | Cổng thi thật mở. **Không trừ lượt nào.** |
| Từ chối | Hiện nguyên văn lý do ở `kyc-pending.html?state=rejected`, kèm nút gửi lại |

Từ chối **bắt buộc ghi lý do** — ép ở cả UI và DB (`CK_Kyc_RejectReason`).
Học viên cần biết chính xác chỗ nào sai thì mới sửa được; "hồ sơ không hợp
lệ" là vô dụng.

Hộp thoại duyệt đặt cạnh nhau ảnh CCCD và thông tin học viên khai, để admin
đối chiếu mà không phải mở hai chỗ.

Ảnh CCCD **không lưu trong DB** và số CCCD được **băm** — màn duyệt chỉ hiện
số đã che. Việc đối chiếu ảnh với khuôn mặt là OCR thật, nằm ngoài prototype.

### 3.2 Các luồng admin khác

| Luồng | Màn | Ràng buộc |
|---|---|---|
| Soạn đề | `exam-editor.html` (wizard), `question-editor.html` | Upload audio/ảnh; câu hỏi theo part |
| Bảng quy đổi điểm | `score-conversion.html` | `raw → scaled` sửa được; **bảng CEFR chỉ đọc** |
| Gói & giá | `packages.html` | Hộp thoại sửa gói |
| Đơn hàng | `orders.html` | Đối chiếu đơn với cổng |
| Học viên | `users.html` | Trạng thái KYC từng người |
| Thống kê | `admin/dashboard.html` | — |

## 4. Ràng buộc thứ tự — phần dễ làm sai nhất

Thứ tự kiểm tra khi học viên bấm vào một đề full là **bắt buộc**, không phải
tuỳ chọn:

```
  kiểm tra KYC ──chưa xác thực──► chặn, KHÔNG trừ lượt
       │ đã xác thực
       ▼
  kiểm tra số dư ──0 lượt──► paywall, KHÔNG trừ lượt
       │ còn lượt
       ▼
  trừ 1 credit → vào phiên thi
```

**Chặn KYC sau khi trừ lượt là bug.** Học viên mất lượt rồi mới biết thiếu
giấy tờ, và không có gì để hoàn lại (prototype không có luồng hoàn tiền).

Vì vậy `exam-list.html` và `exam-instructions.html` đặt khối
`kyc-required` / `kyc-pending` / `rejected` **trước** khối `paywall`, và mọi
khối chặn đều ghi rõ **"không trừ lượt"**.

Hệ quả lên UI: màn luyện tập **không được** có paywall hay CTA mua gói, và
mọi màn chặn thi thật phải kèm lối thoát **"Luyện miễn phí"** — nếu không,
người chưa muốn trả tiền bị dồn vào ngõ cụt.

## 5. Nhánh bất thường — luồng không đi thẳng

| Tình huống | Xảy ra ở | Xử lý |
|---|---|---|
| Reload tab giữa lúc thi | `exam-reading.html` | `resumed` — về đúng câu đang làm, đúng thời gian còn lại |
| Hết giờ | `exam-reading.html`, `exam-confirm-submit.html` | `expired` — nhánh **bình thường**, tự nộp, vẫn chấm phần đã làm |
| Huỷ giữa chừng | `exam-confirm-submit.html` | `cancelled` — **mất lượt đã trừ**, hộp thoại phải nói thẳng |
| Mất mạng khi lưu đáp án | Màn thi | `offline` — hàng đợi retry, không mất đáp án đã chọn |
| Bấm gửi hai lần | `exam-confirm-submit.html` | `submitting` — khoá tương tác |
| Cổng thanh toán trả về chậm | `payment-result.html` | `pending` — đang đối chiếu, auto-refresh |
| Đơn quá hạn đối chiếu (15') | `payment-result.html` | `expired` — mời tạo đơn mới, khác `failed` |
| KYC bị từ chối | `kyc-pending.html` | Hiện lý do + nút gửi lại. **Không mất lượt, không mất tiền.** |

Ba nhánh dễ bỏ sót nhất là `resumed`, `expired` và `cancelled` — cả ba đều
là tình huống *sẽ* xảy ra với bài thi dài 120 phút, không phải lỗi hiếm.

## 6. Bảng free / paid — bản chốt

| Hoạt động | Giá | Vì sao |
|---|---|---|
| Luyện theo Part | miễn phí | Không có phễu thì không ai mua |
| Phân tích điểm yếu | miễn phí | Chính là phễu: chỉ ra chỗ yếu để thấy cần thi thật |
| Xác thực CCCD | miễn phí | Là điều kiện, không phải hàng hoá |
| Thi thật (full 200 câu) | **1 lượt** | Thứ duy nhất tốn tiền |

Đúng **một** thứ tốn tiền. Mọi thứ khác miễn phí — kể cả việc xác thực.

## 7. Ngoài phạm vi prototype

- TOEIC Speaking & Writing (cần chấm tay hoặc AI)
- Flashcard từ vựng
- Đặt lịch ca thi theo slot
- Hoàn tiền
- OCR/đối chiếu khuôn mặt thật ở luồng KYC
- Gọi API thật, thanh toán thật, chấm điểm thật
