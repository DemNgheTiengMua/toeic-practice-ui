# Sổ tay kiểm thử — trạng thái từng luồng

> Tiêu chuẩn PASS: **chạy được VÀ nhìn thấy kết quả trên màn hình**. Chấm bằng mắt, không chấm code.
> Ngày bắt đầu: 2026-11-15. Người test: agent qua Playwright trên `localhost:5000`.

Ký hiệu: ✅ PASS · ❌ FAIL · ⚠️ PASS nhưng có lỗi · ⬜ chưa test

---

## Tổng quan 9 nhóm

| Nhóm | Nội dung | Số luồng | ✅ | ⚠️ | ❌ | ⬜ |
|---|---|---|---|---|---|---|
| A | Khách vãng lai | 7 | 5 | 2 | 0 | 0 |
| H | Học / thương mại / hỗ trợ | 16 | 6 | 3 | 0 | 7 |
| B | Xác thực & tài khoản | 12 | **12** | 0 | 0 | 0 |
| C | Chứng chỉ & OOBE | 4 | 1 | 0 | 0 | 3 |
| D | Placement | 10 | 0 | 0 | 0 | 10 |
| E | Thi thử (mock exam) | 11 | 0 | 0 | 0 | 11 |
| F | Luyện tập | 6 | 0 | 0 | 0 | 6 |
| G | Writing / Speaking | 8 | 0 | 0 | 0 | 8 |
| H | Học / thương mại / hỗ trợ | 16 | 0 | 0 | 0 | 16 |
| I | Quản trị | 17 | 0 | 0 | 0 | 17 |
| | **Tổng** | **89** | **24** | **5** | **0** | **60** |

---

## Nhóm A — Khách vãng lai ✅ HOÀN TẤT

| # | Luồng | Kiểm chứng bằng mắt | Kết quả |
|---|---|---|---|
| A1 | Trang chủ | Hero "Know where you stand. Know what's next.", 2 chứng chỉ, sample path, 3 bước | ✅ PASS |
| A2 | Danh sách chứng chỉ | TOEIC (P1–P7 kèm số câu), IELTS (REC/PAS/W/S), band riêng từng cert | ✅ PASS |
| A3 | Chi tiết chứng chỉ | `/certificates/toeic` → mô tả, bảng code/section/questions, band | ✅ PASS |
| A3− | Slug không tồn tại | `/certificates/not-a-real-cert` → thông báo + "Back to certificates" | ✅ PASS |
| A4 | Bảng giá | Slider, "1 Credit = 20,000 VND", 3 preset, 4 FAQ | ⚠️ LỖI-04 |
| A5 | Điều khoản / Riêng tư | `/terms` 924 ký tự, `/privacy` 897 ký tự | ✅ PASS |
| A6 | Chặn route bảo vệ | `/dashboard`, `/wallet`, `/admin`, `/profile`, `/practice` → `/sign-in?returnUrl=…` đúng | ✅ PASS |

### LỖI-04 ⚠️ Trang `/pricing` xếp learning path vào "Always free"

Khối "Always free" ghi: *"Skill & section practice sessions, learning paths, and accuracy
diagnostics."* — nhưng learning path trừ **5 credit**. Cùng lỗi với LỖI-01, ở **trang thứ ba**
(`/auth/register` và `/sign-in` đã sửa ở #370, `/pricing` bị bỏ sót). Trớ trêu: cùng trang, khối
"Uses credits" liệt kê AI grading và placement retake nhưng **không có** learning path.

### LỖI-05 ⚠️ Trang chủ và trang chứng chỉ quảng cáo TOEIC có Speaking/Writing

`/` và `/certificates/toeic` ghi: *"Practice speaking and writing separately with AI-graded tasks."*
Nhưng TOEIC chỉ có 2 kỹ năng — bảng "Skills & sections" **ngay bên dưới** chỉ liệt kê Listening và
Reading. Tự mâu thuẫn trên cùng một trang.

---

## Nhóm H — Thương mại (mock payment)

| # | Luồng | Kiểm chứng bằng mắt | Kết quả |
|---|---|---|---|
| H6 | Ví credit + ledger | `/wallet` 0 credit, "No credit transactions recorded yet" | ✅ PASS |
| H7 | Checkout tạo order | Slider, preset 5/10/20, 100.000 ₫, "Balance after purchase: 5" | ✅ PASS |
| H8a | Mock success | `DH-…-0003` → `paid`; balance **0 → 5** | ⚠️ LỖI-06 |
| H8b | Mock failed | `DH-…-0004` → `failed`, error **24**, không cộng credit | ⚠️ LỖI-06 |
| H8c | Mock expired | `DH-…-0005` → `expired`, không cộng credit | ⚠️ LỖI-06 |
| H9 | Trang order `paid` | "Payment settled successfully", +5 credits, có thời điểm | ✅ PASS |
| H9 | Trang order `failed` | "Payment was not completed… Error code: 24" | ⚠️ LỖI-07 |
| H10 | `/payment/result?orderCode=…` | Đúng cho cả `paid` và `failed` | ✅ PASS |
| H10− | Thiếu mã order | "Missing order reference" + đường về ví | ✅ PASS |
| H8d | Mock `pending` → `reconciling` | Order `DH-…-0006` → `reconciling`, balance không đổi | ✅ PASS |
| H14 | Admin reconcile | `/admin/finance` hiện `reconcilingCount: 1`, nút **Reconcile** chỉ cho order đó, bấm ghi 4 `PaymentEvents` | ⚠️ LỖI-06 |

**Ledger xác nhận:** 3 order, chỉ **1 dòng** `purchase +5` (từ order `paid`). `failed` và `expired`
không ghi gì. Balance = SUM = 5. Đúng invariant "block before you charge".

### LỖI-06 ⚠️ Bấm trên mock gateway không có phản hồi trên màn hình

Ở `/checkout`, bấm **Simulate failed** hoặc **Simulate expired** → **không có gì thay đổi**. Panel
vẫn còn, không thông báo, không đổi trạng thái (so sánh trước/sau: `changed: false`).

Server làm đúng (DB ghi `failed` mã 24, `expired`) — chỉ **UI im lặng**. Người thao tác sẽ bấm lại
nhiều lần, và mỗi lần bấm lại là **một order mới**. Đây là lỗi UX nghiêm trọng trong luồng demo
payment.

Chỉ nhánh `success` có phản hồi (thông báo rồi tự chuyển trang sau 1.5s).

**Nguyên nhân đã xác minh trong code:** `mockActionMutation.onSuccess` chỉ rẽ nhánh khi
`action === 'success'`. Với `failed` và `expired` không có nhánh nào, nên không set state, không
invalidate, không thông báo. Nút **Reconcile** ở `/admin/finance` cũng vậy: server ghi 4
`PaymentEvents` nhưng màn hình không đổi.

**Chưa có nút "Slow gateway (reconcile)" trên checkout.** Backend hỗ trợ `action: "pending"` (đã gọi
trực tiếp qua API: order → `reconciling`), nhưng type trong UI chỉ khai báo
`'success' | 'failed' | 'expired'`. Nhánh reconcile chỉ chạm được từ `/admin/finance`.

### LỖI-07 ⚠️ Order `failed` vẫn ghi "Credits granted: +5 credits"

`/orders/DH-20261115-0004` (order thất bại):

```
Failed
Total amount: 100.000 ₫
Credits granted: +5 credits        ← SAI
Current credit balance: 5 credits
Payment was not completed
```

Dòng "Credits granted" hiện +5 cho một order không cấp credit nào.

---

## Nhóm B — Xác thực & tài khoản ✅ HOÀN TẤT

| # | Luồng | Kiểm chứng bằng mắt | Kết quả |
|---|---|---|---|
| B1 | Đăng ký tài khoản | Nhập họ tên/email/mật khẩu → tick điều khoản → nút `Create account` → trang **"Check your email"** kèm địa chỉ vừa đăng ký | ✅ PASS |
| B2 | Email kích hoạt | File `.eml` thật trong `mail-drop/`, có link `…/auth/verify-email?token=…` | ✅ PASS |
| B3 | Xác thực email | Bấm link → nút `Verify my email` → **"Email verified — Your account is fully active"** | ✅ PASS |
| B4 | Gửi lại kích hoạt | Trang check-email có ô email + nút `Send activation link` | ✅ PASS |
| B5 | Đăng nhập | Vào `/dashboard`, header hiện tên + `0 credits` | ✅ PASS |
| B5− | Đăng nhập khi **chưa** xác thực | Bị chặn, chuyển `/auth/check-email`, thông báo "Signing in needs a verified address" | ✅ PASS |
| B6 | Đăng xuất | Về trang chủ, mất tên người dùng | ✅ PASS |
| B7 | Quên mật khẩu | `Send link` → **"Link sent"** → mail thật → đặt mật khẩu mới → **"Password updated"** → đăng nhập bằng mật khẩu mới thành công | ✅ PASS |
| B8 | Hồ sơ | `/profile` hiện 4 khối: thông tin cá nhân, chứng chỉ đang học, đổi mật khẩu, top-up | ✅ PASS |
| B9 | Đổi mật khẩu | Đổi xong **tự đăng xuất** (đúng thiết kế "terminates all other sessions") | ✅ PASS |
| B10a | Route không tồn tại | **"Page not found"** + nút Return home | ✅ PASS |
| B10b | Learner vào `/admin` | Chuyển `/access-denied`, hiện **"Access restricted"** | ✅ PASS |

**Không có lỗi nào trong nhóm B.**

---

## Lỗi phát hiện được

### LỖI-01 ⚠️ Trang đăng ký ghi "Learning path — Free", thực tế trừ 5 credit

**Nhìn thấy ở đâu:** `/auth/register`, bảng "What you get on day one" góc phải dưới.

```
Learning path                    Free
```

**Thực tế:** tạo learning path trừ **5 credit** (`POST /api/learner/learning-path/generate`,
reason `learning_path` trong ledger).

**Vì sao quan trọng:** đây là màn hình đầu tiên người mới nhìn thấy. Nói "miễn phí" rồi thu 5
credit là sai lệch thông tin, và người xem sẽ thấy ngay khi bấm tạo path.

**Cách sửa (chọn 1):**
- Đổi dòng thành `Learning path` → `5 credits`
- Hoặc bỏ hẳn dòng đó khỏi bảng

**Chưa sửa.** Cần quyết định: đổi giá, hay đổi chữ?

---

## Nhóm C — Chứng chỉ & OOBE

| # | Luồng | Trạng thái | Ghi chú |
|---|---|---|---|
| C1 | Chuyển chứng chỉ | ✅ PASS | `/profile` → bấm nút `TOEIC — …` → header đổi ngay sang TOEIC. Không phải dropdown, là 2 nút bấm. |
| C2 | Onboarding bước 1 (chọn 2/4 kỹ năng) | ⬜ | Đã sửa lỗi radio hôm nay (không bấm được). Cần test lại bằng mắt. |
| C3 | Nút "Explore for now" | ⬜ | |
| C4 | Dashboard người học | ⚠️ | Hiện được, nhưng **soft-lock** cho tới khi xong OOBE. Xem LỖI-02. |

### LỖI-02 ⚠️ Dashboard bị soft-lock với tài khoản demo

**Nhìn thấy:** `/dashboard` của `learner@demo.local` hiện:

```
Guided Setup Required
Complete your guided setup to unlock this area
```

**Nguyên nhân:** seed **cố ý** không có PlacementTests/TargetBands/LearnerPaths
(`docs/audit/remaining.md`: "shipping a placement would assert a measurement nobody took").
Nên `isOobeComplete = false` với mọi tài khoản seed.

**Ảnh hưởng demo:** `/dashboard`, `/practice`, `/weakness`, `/learning-path`, `/speaking`,
`/writing` đều bị chặn cho tới khi đi hết OOBE.

**Không phải bug** — là thiết kế. Nhưng **phải chuẩn bị trước khi demo**.

---

## Trạng thái dữ liệu sau khi test (QUAN TRỌNG)

Tôi đã thay đổi dữ liệu thật trong lúc test. Ghi lại để khôi phục:

| Việc đã làm | Trạng thái hiện tại | Cần làm |
|---|---|---|
| Đổi mật khẩu `learner@demo.local` → `Demo5678!` → `Demo9999!` | **đã trả về `Demo1234!`** (xác nhận đăng nhập 200) | không cần |
| C1 đổi `learner@demo.local` sang **TOEIC** | **đã đổi lại IELTS**, xác nhận `cert=2` | không cần |
| Tạo tài khoản test `demo.b1794743761217@demo.local` | **còn trong DB** — xoá bằng `DELETE` bị FK chặn; 0 credit, không ảnh hưởng demo | bỏ qua, hoặc `down -v` |
| `learner@demo.local` | 13 credit, chứng chỉ IELTS | sẵn sàng demo S/W |

**Trạng thái hiện tại đã sạch để demo** (trừ 1 tài khoản test thừa, vô hại).

Nếu muốn sạch tuyệt đối:
```sh
docker compose down -v
docker compose up -d --build
```
Sau đó `learner@demo.local` về mặc định: **13 credit**, chứng chỉ **TOEIC** (phải switch IELTS
trước khi demo S/W).

---

## Hạ tầng — vấn đề đã gặp

**Docker Desktop tự tắt 3 lần** trong phiên test này, giữa lúc đang chạy. Nguyên nhân: một agent
khác hoặc tiến trình khác tắt nó.

Bật lại:
```powershell
Start-Process "C:\Users\Nguoi Viet Chim Tay\AppData\Local\Programs\DockerDesktop\Docker Desktop.exe"
```
Đợi ~50 giây rồi `docker compose ps`.

**Ảnh hưởng:** mỗi lần tắt giữa chừng là một luồng test bị đứt, phải làm lại từ đầu.

---

## Tiếp theo

Thứ tự đề xuất:

1. **Nhóm D — Placement** (10 luồng). Đây là cửa duy nhất mở Speaking/Writing, và là đường demo
   chính. Có bẫy cooldown 24h.
2. **Nhóm G — Writing/Speaking** (8 luồng). Đã sửa 3 lỗi, cần xác nhận lại bằng mắt.
3. **Nhóm I — Admin** (17 luồng, 15 chưa từng chạy). Rủi ro cao nhất.
4. **Nhóm A, E, F, H** — phần còn lại.

Trước khi test tiếp: quyết định **LỖI-01** (sửa giá hay sửa chữ), và reset DB để có trạng thái sạch.
