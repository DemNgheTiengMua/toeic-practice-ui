# Hệ thống Ôn và thi chứng chỉ TOEIC — Spec màn hình & state

Ngày: 2026-09-20
Phạm vi tài liệu: **UI prototype tĩnh**. Không implement backend.

## 1. Mục tiêu

Dựng trước toàn bộ màn hình và các state của từng màn hình dưới dạng
prototype HTML/CSS tĩnh (kiểu Figma nhưng chạy được trong browser), để
chốt luồng nghiệp vụ trước khi viết code thật.

Deliverable: ~30 trang HTML tĩnh + CSS dùng chung + JS tối thiểu để
chuyển qua lại giữa các state. Không gọi API, không .NET, không DB.

## 2. Bối cảnh dự án đích

Prototype này sau đó sẽ được port sang kiến trúc:

- **Backend**: ASP.NET Core Web API, RESTful, JWT bearer auth. Ba module
  trong một project (`Identity`, `Content`, `Taking`), tách bằng
  folder + DI chứ không tách process.
- **Frontend**: ASP.NET Core MVC + Razor views, gọi API bằng jQuery Ajax.
- JWT giữ trong **HttpOnly cookie** phía MVC server, không để ở
  `localStorage` (token ở `localStorage` thì mọi lỗi XSS thành mất
  tài khoản).

Ba ràng buộc của hệ đích có ảnh hưởng tới cách vẽ UI:

1. **Timer không tin client.** Server trả `expiresAt` tuyệt đối; client
   chỉ đếm ngược để hiển thị. UI phải có state hết giờ do server chốt.
2. **Mỗi đáp án lưu ngay** (debounce ~400ms). UI cần chỉ báo
   "đang lưu / đã lưu / lỗi lưu".
3. **Đáp án đúng không gửi xuống trong lúc thi.** Chỉ màn Review mới
   hiển thị đáp án + giải thích.

## 3. Phạm vi nghiệp vụ

Trong phạm vi:

- TOEIC **Listening & Reading** only, trắc nghiệm 100%, auto-grade.
- Luyện tập theo từng Part, có feedback ngay từng câu.
- Xem lại các bài đã thi.
- Mua gói credit + thanh toán qua cổng redirect (VNPay/MoMo), mỗi lượt
  thi trừ 1 credit.
- Role: **Student** và **Admin**.

Ngoài phạm vi:

- TOEIC Speaking & Writing (cần chấm tay hoặc AI).
- Flashcard từ vựng — để dành phase sau.
- Đặt lịch ca thi có slot/sức chứa — đã chọn mô hình credit nên không có.
- Hoàn tiền (`refunded`).

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
đúng (raw score) qua bảng convert, không phải nhân tuyến tính.

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

### 5.2 Phiên thi

```
  NotStarted
      │ bấm "Bắt đầu thi" (trừ 1 credit)
      ▼
  Instructions ──────┐
      │ xác nhận      │ thoát
      ▼               ▼
  ListeningActive   Abandoned
      │ hết audio / hết 45'
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

Hai state dễ bỏ sót:

- **Resumed** — thi 120 phút thì reload tab là chuyện sẽ xảy ra. Mở lại
  phải về đúng câu đang làm với đúng thời gian còn lại.
- **Expired** — hết giờ là một nhánh bình thường, không phải lỗi. Bài
  vẫn được chấm trên những gì đã làm.

### 5.3 State từng câu hỏi

Độc lập với state phiên: `unanswered` / `answered` / `marked` (đánh dấu
xem lại). Ba state này là thứ panel điều hướng 200 câu tô màu.

## 6. Danh sách màn hình (~30)

### Auth (3)
1. Đăng nhập
2. Đăng ký
3. Quên mật khẩu

### Student — thi (11)
4. Dashboard — điểm gần nhất, tiến độ, ví lượt, nút thi nhanh
5. Danh sách đề thi — có biến thể `Paywall`
6. Hướng dẫn trước thi — xác nhận trừ 1 credit
7. **Màn thi Listening** — audio không tua, khoá không quay lại part trước
8. **Màn thi Reading** — di chuyển tự do
9. Panel điều hướng câu (200 ô, tô theo state câu) — *component dùng chung, vẽ như một màn riêng để review*
10. Xác nhận submit — cảnh báo số câu chưa làm
11. Kết quả — điểm L / R / Total + quy đổi
12. Review bài đã làm — đáp án + giải thích
13. Lịch sử các lần thi
14. Hồ sơ cá nhân

Màn thi Listening và Reading **tách riêng**: Listening có audio player
không cho tua và khoá điều hướng liên part, Reading thì tự do. Nhét
chung một view sẽ thành một đống `if`.

### Student — luyện tập (3)
15. Chọn part để luyện
16. Màn luyện tập — feedback ngay sau mỗi câu
17. Tổng kết phiên luyện

### Student — thanh toán (5)
18. Bảng giá & chọn gói
19. Checkout
20. Trang cổng thanh toán (vẽ giả, mô phỏng redirect)
21. Kết quả thanh toán — 3 nhánh: thành công / thất bại / đang xử lý
22. Ví lượt thi + lịch sử giao dịch

### Admin (8)
23. Dashboard thống kê
24. Danh sách đề
25. Soạn đề — wizard nhiều bước, upload audio/ảnh
26. Soạn câu hỏi theo part
27. Quản lý người dùng
28. Bảng quy đổi điểm
29. Quản lý gói & giá
30. Quản lý đơn hàng

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

Nhóm thanh toán thêm:

- `pending` — đang đối chiếu đơn, auto-refresh
- `failed` — có lý do + nút thử lại

## 8. Cấu trúc file prototype

```
prototype/
├── index.html              # mục lục toàn bộ màn hình + state
├── assets/
│   ├── css/
│   │   ├── tokens.css      # màu, spacing, typography
│   │   ├── base.css        # reset + layout
│   │   └── components.css  # button, card, option, timer, navigator
│   ├── js/
│   │   └── state-switch.js # đổi state để xem, không có logic thật
│   └── img/                # ảnh Part 1, placeholder
├── auth/                   # 3 trang
├── student/                # 14 trang (thi + luyện)
├── payment/                # 5 trang
└── admin/                  # 8 trang
```

`index.html` là mục lục — mở một chỗ thấy hết 30 màn và mọi state, để
review nhanh mà không phải dò thư mục.

`state-switch.js` chỉ làm một việc: đổi state hiển thị qua query param
(`?state=loading`) hoặc một thanh chọn state nổi ở góc. Không mô phỏng
nghiệp vụ.

## 9. Không làm trong prototype

- Gọi API thật, lưu dữ liệu, đăng nhập thật
- Chấm điểm thật, bảng quy đổi thật (dùng số mẫu)
- Thanh toán thật — trang cổng là màn vẽ giả
- Responsive đầy đủ mọi breakpoint — ưu tiên desktop, vì thi TOEIC
  trên điện thoại không phải luồng chính
