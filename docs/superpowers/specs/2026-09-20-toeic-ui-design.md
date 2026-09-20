# Hệ thống Ôn và thi chứng chỉ TOEIC — Spec màn hình & state

Ngày: 2026-09-20
Phạm vi tài liệu: **UI prototype tĩnh**. Không implement backend.
Tài liệu liên quan: `docs/database-schema.sql` — schema SQL Server suy ra
từ chính prototype này.

## 1. Mục tiêu

Dựng trước toàn bộ màn hình và các state của từng màn hình dưới dạng
prototype HTML/CSS tĩnh (kiểu Figma nhưng chạy được trong browser), để
chốt luồng nghiệp vụ trước khi viết code thật.

Deliverable: **34 trang HTML tĩnh** + `index.html` làm mục lục + CSS dùng
chung + JS tối thiểu để chuyển qua lại giữa các state. Không gọi API,
không .NET, không DB.

## 2. Bối cảnh dự án đích

Prototype này sau đó sẽ được port sang kiến trúc:

- **Backend**: ASP.NET Core Web API, RESTful, JWT bearer auth. Ba module
  trong một project (`Identity`, `Content`, `Taking`), tách bằng
  folder + DI chứ không tách process.
- **Frontend**: ASP.NET Core MVC + Razor views, gọi API bằng jQuery Ajax.
- JWT giữ trong **HttpOnly cookie** phía MVC server, không để ở
  `localStorage` (token ở `localStorage` thì mọi lỗi XSS thành mất
  tài khoản).
- **DB**: SQL Server, schema đã chốt ở `docs/database-schema.sql`.

Bốn ràng buộc của hệ đích có ảnh hưởng tới cách vẽ UI:

1. **Timer không tin client.** Server trả `expiresAt` tuyệt đối; client
   chỉ đếm ngược để hiển thị. UI phải có state hết giờ do server chốt.
2. **Mỗi đáp án lưu ngay** (debounce ~400ms). UI cần chỉ báo
   "đang lưu / đã lưu / lỗi lưu".
3. **Đáp án đúng không gửi xuống trong lúc thi.** Chỉ màn Review mới
   hiển thị đáp án + giải thích.
4. **Số dư credit là tổng của sổ cái**, không phải cột đếm. UI không
   được hiển thị số dư như một con số tự trị.

## 3. Phạm vi nghiệp vụ

Trong phạm vi:

- TOEIC **Listening & Reading** only, trắc nghiệm 100%, auto-grade.
- **Luyện tập theo từng Part miễn phí**, có feedback ngay từng câu.
- **Phân tích điểm yếu miễn phí** — tổng hợp từ chính các phiên luyện và
  các lần thi đã chấm.
- **Xác thực CCCD (KYC)** — điều kiện bắt buộc trước khi thi thật.
- Thi thật (đề full 200 câu) **tốn 1 credit** → paywall.
- Xem lại các bài đã thi.
- Mua gói credit + thanh toán qua cổng redirect (VNPay/MoMo).
- Quy đổi điểm raw → scaled → **bậc CEFR**.
- Role: **Student** và **Admin**.

Ngoài phạm vi:

- TOEIC Speaking & Writing (cần chấm tay hoặc AI).
- Flashcard từ vựng — để dành phase sau.
- Đặt lịch ca thi có slot/sức chứa — đã chọn mô hình credit nên không có.
- Hoàn tiền (`refunded`).

### 3.1 Phân chia free / trả tiền

Đây là thay đổi so với bản đầu, và nó đảo ngược mô hình cũ:

| Hoạt động | Giá | Lý do |
|---|---|---|
| Luyện theo Part | miễn phí | Không có phễu thì không ai mua |
| Phân tích điểm yếu | miễn phí | Chính là phễu: chỉ ra chỗ yếu để thấy cần thi thật |
| Thi thật (full 200 câu) | 1 credit | Thứ duy nhất tốn tiền |

Hệ quả lên UI: màn luyện tập **không được** có paywall hay CTA mua gói,
và mọi màn chặn thi thật phải kèm lối thoát "Luyện miễn phí".

### 3.2 Cổng KYC chặn trước, trừ credit sau

Xác thực CCCD là điều kiện **bắt buộc** trước khi thi thật. Thứ tự bắt
buộc trong luồng:

```
  kiểm tra KYC ──chưa xác thực──► chặn, KHÔNG trừ lượt
       │ đã xác thực
       ▼
  kiểm tra số dư ──0 lượt──► paywall, KHÔNG trừ lượt
       │ còn lượt
       ▼
  trừ 1 credit → vào phiên thi
```

Chặn KYC **sau** khi trừ credit là bug: học viên mất lượt rồi mới biết
thiếu giấy tờ. Vì vậy `exam-list.html` và `exam-instructions.html` đặt
khối `kyc-required` / `kyc-pending` / `rejected` **trước** khối `paywall`,
và mọi khối chặn đều ghi rõ "không trừ lượt".

Một CCCD chỉ được gắn với một tài khoản (chặn dùng chung giấy tờ), và
mỗi tài khoản chỉ có một hồ sơ `pending` tại một thời điểm. Hai ràng buộc
này do server ép; màn admin duyệt hiển thị sẵn kết quả kiểm tra để admin
không phải tự đối chiếu.

### 3.3 Luồng đầy đủ theo tác nhân

Tài liệu này trả lời "mỗi màn hình có state gì". Câu hỏi "người dùng đi qua
những bước nào, theo thứ tự nào, và bước nào chặn bước nào" nằm ở
[`docs/toeic-user-flow.md`](../../toeic-user-flow.md) — kể theo tác nhân
(Khách / Học viên / Admin), mỗi bước trỏ về file prototype tương ứng.

## 4. Cấu trúc đề thi TOEIC L&R

Dùng để mock dữ liệu cho đúng.

| Section | Part | Số câu | Dạng | Ghi chú |
|---|---|---|---|---|
| Listening (45') | 1 | 6 | 4 options | Có ảnh |
| | 2 | 25 | **3 options (A/B/C)** | Không có ảnh, không đề bài in |
| | 3 | 39 | 4 options | 13 hội thoại × 3 câu |
| | 4 | 30 | 4 options | 10 bài nói × 3 câu |
| Reading (75') | 5 | 30 | 4 options | Câu đơn |
| | 6 | 16 | 4 options | 4 đoạn × 4 câu |
| | 7 | 54 | 4 options | Đoạn đơn + đoạn kép |
| **Tổng** | | **200** | | 120 phút, quy đổi 10–990 |

**Part 2 chỉ có 3 lựa chọn** — component đáp án phải nhận số option
động. Hardcode 4 option là sai ngay Part 2.

Điểm: Listening 5–495, Reading 5–495, Total 10–990. Quy đổi từ số câu
đúng (raw score) qua bảng convert, không phải nhân tuyến tính. Bảng
convert **khác nhau theo từng đề**, nên nó là dữ liệu gắn với đề
(`ScoreConversions`), không phải hằng số.

### 4.1 Bậc CEFR

Điểm tổng được quy ra bậc CEFR theo ngưỡng **cố định của chuẩn**, không
phải theo từng đề:

| Bậc | Khoảng điểm tổng |
|---|---|
| `<A1` | 10–119 |
| `A1` | 120–224 |
| `A2` | 225–549 |
| `B1` | 550–784 |
| `B2` | 785–944 |
| `C1` | 945–990 |

Hai quyết định đã chốt:

- **Admin không được sửa bậc CEFR.** Đây là ngưỡng chuẩn quốc tế; cho
  sửa nghĩa là mỗi đề có thể ra một bậc khác nhau cho cùng số điểm, và
  chứng chỉ mất giá trị đối chiếu. Admin chỉ sửa được bảng raw → scaled
  (phần thật sự khác nhau giữa các đề). Màn
  `admin/score-conversion.html` vì vậy hiển thị bảng CEFR ở dạng
  **chỉ đọc**, kèm ghi chú lý do.
- **Học viên phải thấy được cái thanh.** Điểm số trần (760) không tự nói
  lên điều gì; bậc CEFR mới là thứ so sánh được với yêu cầu tuyển dụng.
  Nên `exam-result.html`, `dashboard.html`, `exam-history.html` đều hiển
  thị **cả hai**: điểm số và thanh CEFR có bậc hiện tại được tô nổi.

Lưu ý khi đọc code: bậc đầu tiên là `<A1`, không phải `A1`. Thang điểm
10–990 không có điểm 0, nên 6 bậc chứ không phải 6 bậc bắt đầu từ A1.

## 5. State machine

### 5.1 Cổng thanh toán (đứng trước phiên thi)

```
  ChọnGói → Checkout → PaymentPending ⇄ PaymentFailed
                            │ thành công
                            ▼
                     CreditAvailable ──(0 lượt)──► Paywall
                            │ còn lượt
                            ▼
                       [phiên thi]
```

`Paywall`: hết lượt thì màn danh sách đề và nút "Bắt đầu thi" đổi hình
dạng (mờ + CTA mua gói), không phải hiện thông báo lỗi.

`PaymentPending`: cổng thanh toán trả về chậm là bình thường — người
dùng quay lại web trước khi đơn kịp xác nhận. Cần màn "đang đối chiếu
đơn" có auto-refresh.

Trạng thái đơn hàng: `pending` → `paid` | `failed` | `expired`.
`expired` là nhánh riêng của `failed`: đơn quá hạn đối chiếu (15 phút),
khác với cổng trả về lỗi. Gộp hai nhánh này làm một là sai thông điệp —
một cái nên mời thử lại, một cái nên mời tạo đơn mới.

`checkout.html` có khối chặn `pending`: **mỗi tài khoản chỉ được có một
đơn `pending`**. Không chặn thì học viên bấm hai lần thành hai đơn, trả
tiền hai lần. Ràng buộc này cũng được ép ở DB bằng unique index có điều
kiện.

### 5.2 Phiên thi

```
  NotStarted
      │ bấm "Bắt đầu thi"
      │ (KYC đã xác thực → trừ 1 credit)
      ▼
  Instructions ──────┐
      │ xác nhận      │ huỷ
      ▼               ▼
  ListeningActive   Cancelled
      │ hết audio / hết 45'   (mất lượt đã trừ)
      ▼
  ReadingActive ◄──── Resumed (mở lại tab)
      │                  ▲
      │ bấm submit        │ còn thời gian
      ▼                  │
  ConfirmSubmit ─────────┘ huỷ
      │ đồng ý        ┌──────────┐
      ▼               │ Expired  │ hết giờ → auto-submit
  Submitting ◄────────┴──────────┘
      │
      ▼
  Graded ──► Review (mở khoá đáp án + giải thích)
```

Ba state dễ bỏ sót:

- **Resumed** — thi 120 phút thì reload tab là chuyện sẽ xảy ra. Mở lại
  phải về đúng câu đang làm với đúng thời gian còn lại.
- **Expired** — hết giờ là một nhánh bình thường, không phải lỗi. Bài
  vẫn được chấm trên những gì đã làm. `expired` xuất hiện ở ba chỗ khác
  nhau: màn thi (auto-submit), hộp thoại xác nhận submit (hết giờ trong
  lúc đang mở hộp thoại), và kết quả thanh toán (đơn quá hạn).
- **Cancelled** — huỷ giữa chừng là mất lượt đã trừ. Phải có hộp thoại
  xác nhận nói thẳng điều đó, vì đây là hành động không hoàn tác được.

### 5.3 State từng câu hỏi

Độc lập với state phiên: `unanswered` / `answered` / `marked` (đánh dấu
xem lại). Ba state này là thứ panel điều hướng 200 câu tô màu.

## 6. Danh sách màn hình (34)

### Auth (3)
1. Đăng nhập
2. Đăng ký
3. Quên mật khẩu

### Student — thi (12)
4. Dashboard — điểm gần nhất + bậc CEFR, tiến độ, ví lượt, nút thi nhanh
5. Danh sách đề thi — có biến thể `Paywall`, `Cancelled`, và cổng KYC
6. Hướng dẫn trước thi — xác nhận trừ 1 credit, cũng bị chặn bởi KYC
7. **Màn thi Listening** — audio không tua, khoá không quay lại part trước
8. **Màn thi Reading** — di chuyển tự do
9. Panel điều hướng câu (200 ô, tô theo state câu) — *component dùng chung, vẽ như một màn riêng để review*
10. Xác nhận submit — cảnh báo số câu chưa làm
11. Kết quả — điểm L / R / Total + **thanh CEFR**
12. Review bài đã làm — đáp án + giải thích
13. Lịch sử các lần thi — kèm bậc CEFR từng lần
14. Hồ sơ cá nhân — có thẻ trạng thái KYC

Màn thi Listening và Reading **tách riêng**: Listening có audio player
không cho tua và khoá điều hướng liên part, Reading thì tự do. Nhét
chung một view sẽ thành một đống `if`.

### Student — luyện tập (3)
15. Chọn part để luyện
16. Màn luyện tập — feedback ngay sau mỗi câu
17. Tổng kết phiên luyện

### Student — xác thực CCCD (2)
18. Gửi hồ sơ KYC — khai họ tên/ngày sinh/số CCCD, tải 3 ảnh
    (mặt trước, mặt sau, chân dung), cam kết thông tin đúng
19. Trạng thái hồ sơ KYC — `pending` (kèm câu "luyện tập vẫn miễn phí"),
    `approved` (duyệt là **mở cổng**, không trừ lượt), và `rejected`
    (hiện nguyên văn lý do, nói rõ không mất lượt)

### Student — điểm yếu (1)
20. Phân tích điểm yếu — độ chính xác từng Part, xếp **yếu nhất trước**,
    gợi ý nên luyện gì, dạng câu hay sai, tiến độ 4 tuần

### Student — thanh toán (5)
21. Bảng giá & chọn gói
22. Checkout — có khối chặn khi đang có đơn `pending`
23. Trang cổng thanh toán (vẽ giả, mô phỏng redirect)
24. Kết quả thanh toán — 4 nhánh: thành công / thất bại / đang xử lý / hết hạn
25. Ví lượt thi + lịch sử giao dịch

### Admin (9)
26. Dashboard thống kê
27. Danh sách đề
28. Soạn đề — wizard nhiều bước, upload audio/ảnh
29. Soạn câu hỏi theo part
30. Quản lý người dùng
31. Bảng quy đổi điểm — sửa raw → scaled; **bảng CEFR chỉ đọc**
32. Quản lý gói & giá
33. Quản lý đơn hàng
34. Duyệt hồ sơ KYC — hàng đợi kèm thời gian chờ, hộp thoại đối chiếu
    ảnh, **bắt buộc ghi lý do khi từ chối**

## 7. State cần vẽ cho mỗi trang

Mọi trang có tải dữ liệu phải phủ 4 state cơ bản:

- `loading` — skeleton, không phải spinner toàn trang
- `empty` — chưa có đề / chưa thi lần nào / ví 0 lượt
- `error` — kèm nút thử lại
- `success` — trạng thái thường

Nhóm màn thi thêm:

- `resumed` — banner "tiếp tục bài đang làm"
- `expired` — hết giờ, đang auto-submit
- `submitting` — khoá tương tác, không cho bấm lại
- `offline` — mất mạng khi đang lưu đáp án, có hàng đợi retry
- `cancelled` — huỷ bài, mất lượt đã trừ (hộp thoại xác nhận)
- `complete` — đã nộp xong, không còn gì để làm

Nhóm thanh toán thêm:

- `pending` — đang đối chiếu đơn, auto-refresh
- `failed` — có lý do + nút thử lại
- `expired` — đơn quá hạn đối chiếu, mời tạo đơn mới

Nhóm KYC thêm:

- `kyc-required` — chưa gửi hồ sơ; chặn thi thật, nói rõ không trừ lượt
- `kyc-pending` — hồ sơ đang chờ duyệt
- `approved` — hồ sơ đã duyệt. Cần state riêng vì nếu không, tín hiệu duy
  nhất học viên nhận được là cổng chặn ở `exam-list.html` **tự biến mất** —
  một tín hiệu im lặng, dễ bị bỏ qua hoặc hiểu nhầm thành lỗi. Khối này nói
  rõ duyệt **không trừ lượt** (lượt chỉ trừ khi bắt đầu bài thi thật).
- `rejected` — hồ sơ bị từ chối, kèm lý do và nút gửi lại
- `adjust` — (admin) hộp thoại đang duyệt một hồ sơ

Nhóm luyện tập thêm:

- `answering` / `revealed-correct` / `revealed-wrong` / `finished`

Nhóm admin thêm:

- `step2` / `step3` — các bước của wizard soạn đề
- `edit` — hộp thoại sửa gói giá

`profile.html` dùng chung khối thẻ KYC cho cả bốn trạng thái
(`success` / `kyc-required` / `kyc-pending` / `rejected`), nên thẻ đó là
`.multi-state` chứ không phải `.only-<state>`.

Lưu ý: ở `profile.html`, hồ sơ **đã duyệt** nằm trong biến thể `success`
của thẻ KYC (`badge--success` "Đã xác thực") — **không** có state `approved`
riêng. State `approved` chỉ tồn tại ở `kyc-pending.html`, nơi nó là toàn bộ
nội dung trang. Hai chỗ này không mâu thuẫn: `profile.html` là trang hồ sơ
mà trạng thái KYC chỉ là một thẻ, còn `kyc-pending.html` là trang *của chính
hồ sơ KYC*, nên ở đó trạng thái mới cần đứng riêng thành một state. Người
review tìm "hồ sơ KYC đã duyệt" thì mở `profile.html?state=success`.

## 8. Cấu trúc file prototype

```
prototype/
├── index.html              # mục lục toàn bộ màn hình + state
├── assets/
│   ├── css/
│   │   ├── tokens.css      # màu, spacing, typography
│   │   ├── base.css        # reset + layout + đăng ký state
│   │   └── components.css  # button, card, option, timer, navigator,
│   │                       #   cefr, kyc-photo, modal, progress
│   ├── js/
│   │   └── state-switch.js # đổi state để xem, không có logic thật
│   └── img/                # ảnh Part 1, placeholder
├── auth/                   # 3 trang
├── student/                # 17 trang (thi + luyện + KYC + điểm yếu)
├── payment/                # 5 trang
└── admin/                  # 9 trang
```

`index.html` là mục lục — mở một chỗ thấy hết 34 màn và mọi state, để
review nhanh mà không phải dò thư mục.

`state-switch.js` chỉ làm một việc: đổi state hiển thị qua query param
(`?state=loading`) hoặc một thanh chọn state nổi ở góc. Không mô phỏng
nghiệp vụ. Trang chỉ có một state thì không hiện thanh chọn.

Cơ chế state: mỗi khối có class `.only-<state>`; `base.css` đăng ký cả
bốn nhóm luật cho từng state — ẩn (`.only-x.only-x`), hiện
(`body[data-state="x"] .only-x`), `.multi-state[data-show~="x"]`, và
`body[data-state="x"] .modal-overlay` (phải ép lại `display: grid` vì
`revert` trả về `block` của UA). **Thêm state mới phải sửa đủ cả bốn
nhóm**, nếu không khối modal sẽ hiện sai kiểu.

## 9. Không làm trong prototype

- Gọi API thật, lưu dữ liệu, đăng nhập thật
- Chấm điểm thật, bảng quy đổi thật (dùng số mẫu)
- Thanh toán thật — trang cổng là màn vẽ giả
- OCR/đối chiếu khuôn mặt thật ở luồng KYC — ảnh chỉ là placeholder
- Responsive đầy đủ mọi breakpoint — ưu tiên desktop, vì thi TOEIC
  trên điện thoại không phải luồng chính
