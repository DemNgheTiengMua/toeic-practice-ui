# Sổ tay kiểm thử — trạng thái từng luồng

> Tiêu chuẩn PASS: **chạy được VÀ nhìn thấy kết quả trên màn hình**. Chấm bằng mắt, không chấm code.
> Ngày bắt đầu: 2026-11-15. Người test: agent qua Playwright trên `localhost:5000`.

## Phiên 2026-11-15 (đêm trước demo 07:00)
**Trạng thái phát hiện lúc 19:20:**

| Mục | Giá trị | Đánh giá |
|---|---|---|
| `learner@demo.local` OOBE | xong (`isOobeComplete: true`) | ✅ vào được mọi cửa |
| Learning path | 22 steps, status `active` | ✅ |
| Practice | listening 4 section, reading 3 | ✅ |
| **Writing practice** | **0 exams** | ❌ LỖI-20 |
| **Speaking practice** | **0 exams** | ❌ LỖI-20 |
| Credit `learner@` | **7** (baseline 13) | ⚠️ đã tiêu khi test |
| Order đang mở | **1** | ⚠️ chặn mua credit |
| Tài khoản lạ | `vanhuydo44@gmail.com` | ⚠️ rác từ phiên khác |

### LỖI-20 🔴 `/writing` và `/speaking` trống — không có exam nào

Seed **không chứa** exam với `Purpose` là `writing_practice` hay `speaking_practice`. Chỉ có 3 exam:
2 `mock` (TOEIC, IELTS) và 1 `placement`.

**Ảnh hưởng:** hai cửa demo chính (`/writing`, `/speaking`) luôn hiện danh sách rỗng với mọi tài
khoản, dù OOBE đã xong. Không thể demo Writing/Speaking practice theo đường riêng.

**Nội dung đã có sẵn:** pool có **8 item speaking** + **14 item writing** với stem thật, chỉ chưa
được gắn vào exam nào. Đây là việc nối dây, không phải soạn nội dung.

**Kế hoạch sửa:** thêm 1 exam `speaking_practice` + 1 exam `writing_practice` cho IELTS vào seed,
gắn các item đã có trong pool.

### LỖI-21 🔴 `/onboarding` không bao giờ hiện bước chọn chứng chỉ

`OnboardingPage.derivedStep` trả **3** khi mọi điều kiện đã xong, mà step 3 là "Certificate & Goal" —
đã hoàn thành rồi. Không có bước nào cho phép **chọn chứng chỉ sau placement**.

Thêm nữa `OnboardingRouteView` **redirect sang `/dashboard`** ngay khi `isOobeComplete === true`, nên
người dùng không bao giờ mở lại được OOBE để xem/sửa lựa chọn của mình.

**Ảnh hưởng:** trải nghiệm OOBE gãy đúng chỗ quan trọng nhất — sau khi thi xong, người học không được
hỏi muốn học chứng chỉ nào.

### LỖI-22 🔴 Placement gắn một chứng chỉ, TargetBand và Path gắn chứng chỉ khác

Đo trên DB thật:

```
PlacementTests:  cert=2 (IELTS)  band=B1  W=NULL  S=38.89
TargetBands:     cert=1 (TOEIC)  target=B2
LearnerPaths:    cert=1 (TOEIC)  active
```

Placement tạo theo `activeCertId` **tại thời điểm bấm Start**, không theo chứng chỉ người dùng chọn
sau đó. Nên kết quả placement thuộc IELTS trong khi lộ trình học thuộc TOEIC.

**Ảnh hưởng:** đúng như mô tả — "kết quả chỉ hiện cho chứng chỉ đã chọn, không tạo path cho chứng
chỉ còn lại". Hai chứng chỉ không thể cùng tồn tại trên một lần đo.

### LỖI-23 🔴 `WritingPct` NULL dù scope `all_skills`

Cùng placement trên: Listening 40%, Reading 40%, Speaking 38.89%, **Writing NULL**. Bài Writing
không được chấm hoặc không được gửi. Công thức phần trăm đã sửa (#5) nhưng ở đây thiếu dữ liệu đầu vào.

### LỖI-24 🔴 Không có chỗ xem lại kết quả placement chi tiết

Placement result chỉ hiện bảng phần trăm tổng. **Không** xem được:

- từng câu trắc nghiệm đã chọn gì, đúng/sai
- transcript bài nói
- bài viết + rubric từng tiêu chí
- điểm từng tiêu chí kèm nhận xét

`/exams/review/{attemptId}` chỉ phục vụ mock exam; placement không có route review tương ứng.

### LỖI-25 🔴 Audio "tự complete" vì localStorage nhớ trạng thái

`ExamAudioPlayer` lưu `completed: true` vào localStorage theo khoá
`audioPath::segment::questionId`. Một lần phát xong — hoặc một lần reload giữa chừng — là lần sau
vào thấy "Played once" dù chưa nghe gì. Câu 4 "tự nhiên play được" vì khoá của nó chưa có trong
localStorage.

Thêm nữa stimulus đầu của placement không khai báo `AudioStartMs`/`AudioEndMs`, nên đoạn phát là
**toàn bộ file 175 giây** thay vì 30 giây như các câu sau.

### LỖI-26 🟡 Chọn chứng chỉ hiện trước khi làm gì

Header hiện `IELTS` + số credit ngay từ màn đầu, trước khi người dùng chọn gì. Về kỹ thuật không sai
nhưng trái với ý đồ OOBE: chứng chỉ lẽ ra được chọn **sau** placement, không phải có sẵn từ đầu.

### LỖI-28 🔴 Nút "Adjust goal" trên dashboard là vòng lặp chết

`DashboardPage` render link `Adjust goal` trỏ tới `/onboarding`. Nhưng `/onboarding` **redirect về
`/dashboard`** ngay khi `isOobeComplete === true` (LỖI-21). Bấm nút → nháy một cái → về đúng chỗ cũ.

Không có phản hồi, không thông báo. Người dùng tưởng nút hỏng.

**Cần:** một route riêng để sửa goal (`/goals` hoặc `/placement/target-band`), không dùng lại
`/onboarding` — vì onboarding đã hoàn thành thì không mở lại được.

### LỖI-29 🔴 Trang `/placement` trắng trơn khi `exams` rỗng

API trả `exams: []` nhưng `isRetake: true`. Trang chỉ render danh sách exam và nút retake **bên trong**
`exams.map` — mảng rỗng thì không render gì. Kết quả: chỉ còn 2 ô "B1" và "7 cr", phần còn lại trắng.

Không fallback, không thông báo "không có bài placement cho chứng chỉ này".

Nguyên nhân `exams` rỗng: exam placement thuộc IELTS (cert 2), learner đang ở TOEIC (cert 1) — lọc
theo chứng chỉ nên loại sạch (cùng gốc với LỖI-22).

**Cần:** trạng thái rỗng có lời giải thích + đường đi tiếp.

### LỖI-30 🟡 Dữ liệu goal mâu thuẫn trên dashboard

Ảnh chụp dashboard cho thấy:

```
Goal Standing · IELTS        ← tiêu đề ghi IELTS
OVERALL A1 · Speaking
Level against target:  B1 → None
Remaining guided hours: Cannot judge
Pacing: No weekly pace set · 0m / 0m this week
Verdict: Cannot judge
```

Nhưng DB thật:

```
learner@demo.local activeCert = 1 (TOEIC)
TargetBands: cert=1 current=A2 target=B2 date=NULL hours=NULL feas=NULL
```

Ba mâu thuẫn: tiêu đề ghi IELTS trong khi cert đang hoạt động là TOEIC; `B1` hiển thị trong khi DB ghi
`A2`; và `→ None` vì `target` đọc sai chỗ.

**Cần:** một nguồn dữ liệu duy nhất cho goal, đọc theo `activeCertId`.

---


`/admin/operations` tab Mentors có hai thứ: cấu hình **lịch rảnh** (weekly windows) và **payout
report** — mà payout chỉ liệt kê buổi **đã hoàn thành**.

Không có chỗ nào cho admin thấy:

- learner đã đặt lịch nào
- lịch hẹn sắp tới của một mentor
- booking đang chờ, đã huỷ

Backend cũng không có endpoint: `MentorEndpoints.Administration` chỉ có list/options/detail/create/
update, không có bookings. Chỉ mentor tự xem được booking của mình qua `/api/mentors/bookings`.

**Ảnh hưởng:** có người đặt lịch mà không ai biết, cho tới khi buổi học đã xong.

### LỖI-27 🔴 Admin không xem được lịch hẹn của mentor


Ký hiệu: ✅ PASS · ❌ FAIL · ⚠️ PASS nhưng có lỗi · ⬜ chưa test

---

## Danh sách lỗi — toàn bộ

### Đã sửa, có test đỏ→xanh (12)

| # | Lỗi | Nguyên nhân gốc | Trạng thái |
|---|---|---|---|
| 1 | Không chọn được placement 4 kỹ năng | `Radio` thiếu `Radio.Control`/`Radio.Content` → không sinh `<input>`, bấm vô hiệu, luôn rơi về 2 kỹ năng | ✅ |
| 2 | Mọi bản ghi âm bị từ chối upload | Validator so **nguyên header** (`audio/webm;codecs=opus`) với allow-list chứa `audio/webm` | ✅ |
| 3 | SpeechSuper từ chối mọi file nói | Provider chỉ nhận `wav/mp3/opus/ogg/amr`. Gửi đuôi `webm` → errId 41002. Cùng file gửi nhãn `opus` → chấm được | ✅ |
| 4 | Checkout thật bị chặn "Order already open" | Seed chèn `PaymentNo` 1,2 mà không đẩy `SEQ_PaymentNo`, order đầu tiên trùng khoá unique | ✅ |
| 5 | Placement báo Writing/Speaking = 0.00 | Phần trăm chia theo `MaxScaledScore - MinScaledScore`; IELTS để NULL cho WRIT/SPEA → range 0 | ✅ |
| 6 | Audio listening im lặng, "xong" sau 1–2s | Mọi file là placeholder 1.3s **im lặng** (`-91 dB`), trong khi stimulus khai báo đoạn tới 150s | ✅ #364 |
| 7 | Không nghe lại được bản ghi âm | Endpoint trả `Content-Disposition: attachment` → trình duyệt tải file thay vì phát | ✅ #365 |
| 8 | `/speaking/result/1` báo "Attempt 1 not found" | Trang dùng **PlacementId** (1) nhưng API cần **AttemptId** (1002) | ✅ #366 |
| 9 | Phải bấm đúng chữ mới chọn được đáp án | `Radio.Content` chỉ bọc `Label`, không bọc cả hàng có viền | ✅ #367 |
| 10 | Chuyển câu bị reload trang | URL sync mỗi lần đổi câu làm remount sitting shell | ✅ #368 |
| 11 | Catalog placement ghi sai chứng chỉ | Nhãn lấy từ learner's active certificate thay vì từ exam | ✅ #369 |
| 12 | `/auth/register` + `/sign-in` ghi learning path "Free" | Thực tế trừ 5 credit | ✅ #370 |

### Chưa sửa (0)

Không còn. Hai lỗi cuối cùng (#13 huỷ payOS không đóng order, #14 mất ngữ cảnh khi huỷ) đã sửa
cùng đợt: gateway báo `failed`/`cancelled`/`expired` giờ đóng order, và `cancelUrl` mang `orderCode`
để trang kết quả nói rõ order nào bị huỷ. Kiểm chứng bằng hành vi thật sau `down -v`: huỷ order →
tạo order mới **thành công ngay**, không còn 409 trong 15 phút.

### Sai thông tin trên màn hình (3 — đã sửa)

| # | Lỗi | Ở đâu | Mức độ |
|---|---|---|---|
| 15 | Learning path xếp vào "Always free" | `/pricing` — cùng lỗi #12 nhưng trang thứ ba bị bỏ sót | 🟡 copy |
| 16 | TOEIC quảng cáo có Speaking/Writing | `/` và `/certificates/toeic` ghi "Practice speaking and writing separately"; bảng ngay dưới chỉ có Listening + Reading | 🟡 copy |
| 17 | Order `failed` vẫn ghi "Credits granted: +5" | `/orders/DH-…` trang order thất bại | 🟡 copy |

### UI mock gateway (1 — đã sửa)

| # | Lỗi | Nguyên nhân gốc | Mức độ |
|---|---|---|---|
| 18 | Bấm Simulate failed/expired/Reconcile không phản hồi | `mockActionMutation.onSuccess` chỉ có nhánh `if (action === 'success')`; các action khác không set state, không thông báo. Server làm đúng (DB ghi `failed` mã 24), chỉ UI im lặng. Giờ mọi action báo kết quả và xoá order đã đóng | 🟡 mock only |

### Không phải lỗi (1)

| # | Hiện tượng | Giải thích |
|---|---|---|
| 19 | Dashboard soft-lock với tài khoản seed | Thiết kế: seed không chứa PlacementTests/TargetBands/LearnerPaths. Phải đi hết OOBE trước khi demo `/dashboard`, `/practice`, `/weakness`, `/speaking`, `/writing` |

**Tổng: 19 mục — 18 đã sửa, 0 chưa sửa, 1 không phải lỗi.**

Mọi lỗi đã sửa đều có test đỏ-trước-xanh-sau, trừ ba lỗi copy (#15, #16, #17) chỉ có assert render
hoặc seed check, và #19 là thiết kế.

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
| A4 | Bảng giá | Slider, "1 Credit = 20,000 VND", 3 preset, 4 FAQ | ⚠️ LỖI-15 |
| A5 | Điều khoản / Riêng tư | `/terms` 924 ký tự, `/privacy` 897 ký tự | ✅ PASS |
| A6 | Chặn route bảo vệ | `/dashboard`, `/wallet`, `/admin`, `/profile`, `/practice` → `/sign-in?returnUrl=…` đúng | ✅ PASS |

### LỖI-15 🟡 Trang `/pricing` xếp learning path vào "Always free"

Khối "Always free" ghi: *"Skill & section practice sessions, learning paths, and accuracy
diagnostics."* — nhưng learning path trừ **5 credit**. Cùng lỗi với LỖI-12, ở **trang thứ ba**
(`/auth/register` và `/sign-in` đã sửa ở #370, `/pricing` bị bỏ sót). Trớ trêu: cùng trang, khối
"Uses credits" liệt kê AI grading và placement retake nhưng **không có** learning path.

### LỖI-16 🟡 Trang chủ và trang chứng chỉ quảng cáo TOEIC có Speaking/Writing

`/` và `/certificates/toeic` ghi: *"Practice speaking and writing separately with AI-graded tasks."*
Nhưng TOEIC chỉ có 2 kỹ năng — bảng "Skills & sections" **ngay bên dưới** chỉ liệt kê Listening và
Reading. Tự mâu thuẫn trên cùng một trang.

---

## Nhóm H — Thương mại (mock payment)

| # | Luồng | Kiểm chứng bằng mắt | Kết quả |
|---|---|---|---|
| H6 | Ví credit + ledger | `/wallet` 0 credit, "No credit transactions recorded yet" | ✅ PASS |
| H7 | Checkout tạo order | Slider, preset 5/10/20, 100.000 ₫, "Balance after purchase: 5" | ✅ PASS |
| H8a | Mock success | `DH-…-0003` → `paid`; balance **0 → 5** | ⚠️ LỖI-18 |
| H8b | Mock failed | `DH-…-0004` → `failed`, error **24**, không cộng credit | ⚠️ LỖI-18 |
| H8c | Mock expired | `DH-…-0005` → `expired`, không cộng credit | ⚠️ LỖI-18 |
| H9 | Trang order `paid` | "Payment settled successfully", +5 credits, có thời điểm | ✅ PASS |
| H9 | Trang order `failed` | "Payment was not completed… Error code: 24" | ⚠️ LỖI-17 |
| H10 | `/payment/result?orderCode=…` | Đúng cho cả `paid` và `failed` | ✅ PASS |
| H10− | Thiếu mã order | "Missing order reference" + đường về ví | ✅ PASS |
| H8d | Mock `pending` → `reconciling` | Order `DH-…-0006` → `reconciling`, balance không đổi | ✅ PASS |
| H14 | Admin reconcile | `/admin/finance` hiện `reconcilingCount: 1`, nút **Reconcile** chỉ cho order đó, bấm ghi 4 `PaymentEvents` | ⚠️ LỖI-18 |

**Ledger xác nhận:** 3 order, chỉ **1 dòng** `purchase +5` (từ order `paid`). `failed` và `expired`
không ghi gì. Balance = SUM = 5. Đúng invariant "block before you charge".

### LỖI-18 🟡 Bấm trên mock gateway không có phản hồi trên màn hình

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

### LỖI-17 🟡 Order `failed` vẫn ghi "Credits granted: +5 credits"

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

### LỖI-13 🔴 Huỷ trên payOS không đóng order (chặn demo)

**Nhìn thấy:** huỷ thanh toán trên trang payOS → quay về `/checkout` → banner "Existing pending order
detected (DH-20261115-0053)" vẫn hiện. Bấm tạo order mới → **409 "Order already open"**. Không mua
được credit cho tới khi order tự hết hạn sau **15 phút**.

**Nguyên nhân đã xác minh:** `GetPaymentResultAsync` chỉ xử lý khi gateway trả `Paid`
(`SettleOrderAsync`). Với `failed` / `cancelled` / `expired`, code chỉ ghi một `PaymentEvents` row với
`WasApplied = false` rồi bỏ qua — order **không đổi status**. Bằng chứng từ DB: 5 event ghi
`reported=failed` nhưng order vẫn `pending`.

**Vì sao trước không thấy:** dùng mock gateway có nút "Cancel order" riêng gọi API huỷ → đóng order
ngay. Luồng huỷ **thật trên trang payOS** chưa từng được test.

**Ảnh hưởng demo:** nếu giảng viên bấm huỷ trên payOS để thử, hệ thống kẹt 15 phút.

### LỖI-14 🟠 Huỷ trên payOS về trang mất ngữ cảnh

`cancelUrl` = `/checkout?credits=N` — thiếu `orderCode`. Người dùng về form checkout trống, không biết
order nào vừa bị huỷ. Kết hợp với LỖI-13 thành trạng thái mâu thuẫn: banner báo "có order đang mở"
nhưng form cho phép tạo order mới (nút bị disable nên không bấm được, nhưng vẫn gây khó hiểu).

---

## Lỗi phát hiện được

### LỖI-12 ✅ Trang đăng ký ghi "Learning path — Free" (đã sửa ở #370)

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
| C4 | Dashboard người học | ⚠️ | Hiện được, nhưng **soft-lock** cho tới khi xong OOBE. Xem LỖI-19. |

### LỖI-19 ℹ️ Dashboard bị soft-lock với tài khoản demo (không phải lỗi)

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

Trước khi test tiếp: quyết định **LỖI-12** (sửa giá hay sửa chữ), và reset DB để có trạng thái sạch.
