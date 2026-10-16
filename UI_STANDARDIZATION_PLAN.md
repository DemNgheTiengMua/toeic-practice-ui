# UI Standardization Plan: "Bảng hiệu phòng thi" (Prototype Alignment)

Kế hoạch và tài liệu hướng dẫn chuẩn hóa toàn bộ giao diện Razor Views (`src/WebMVC/Views/`) theo chuẩn Pixel & Design System từ `prototype/` và [`DESIGN.md`](./DESIGN.md).

---

## 1. Mục tiêu & Nguyên tắc bất biến (Design System Invariants)

Mọi view trong hệ thống đều tuân thủ phong cách thiết kế **"Bảng hiệu phòng thi"**:

1. **Hình khối & Khung viền (Keylines):**
   - **Tuyệt đối không bo góc:** `border-radius: 0` trên mọi button, card, panel, input, modal.
   - **Không đổ bóng, không làm mờ:** `box-shadow: none`, không dùng gradients, không dùng backdrop-blur.
   - **Đường nét rõ ràng:** Khung viền 2px mực đen (`#14181d`), vách ngăn chia phần nội bộ 1px soft rule (`#ccd2d6`).
2. **Quy tắc đảo màu (Inversion Rule):**
   - Trạng thái được chọn (`is-active`, `selected`) luôn đảo ngược thành nền mực đen (`#14181d`) với chữ trắng.
   - Tuyệt đối không dùng lớp phủ mờ (tint / wash) cho trạng thái kích hoạt.
3. **Bảng màu biển hiệu (Signage Palette):**
   - **Sign Blue (`#0b4ea2`):** Hành động chính (Primary), nút bấm hành động, band CEFR đạt được.
   - **Sign Gold (`#f2b705`):** Cảnh báo, câu hỏi được đánh dấu cờ (flagged), highlight nội dung.
   - **Sign Red (`#b31217`):** Từ chối, nguy hiểm, điều kiện thất bại.
   - **Sign Green (`#0a6b3c`):** Thành công, câu trả lời đúng, điều kiện mở.
4. **Typography & Iconography:**
   - **IBM Plex Mono:** Hiển thị con số, timer đếm ngược, điểm số, bảng dữ liệu.
   - **IBM Plex Sans Condensed (In hoa):** Tiêu đề lớn, nhãn bảng biển hiệu, placards.
   - **IBM Plex Sans:** Nội dung văn bản thân bài (body text).
   - **Icons:** Tuyệt đối không dùng Emoji hoặc glyph thô. Sử dụng bộ SVG vẽ sẵn trong `wwwroot/js/icons.js` và luôn đi kèm nhãn chữ.
5. **Bảo toàn chức năng & Kiểm thử:**
   - Giữ nguyên các directive Razor `@model`, `@if`, `@foreach`.
   - Giữ nguyên form submit, `@Html.AntiForgeryToken()`, các thẻ `<input asp-for="...">`, các class/id cho jQuery Ajax.
   - Sau mỗi cụm hoàn thành, chạy `dotnet build` và `dotnet test tests/WebMVC.Tests` (276 tests) đảm bảo pass 100%.

---

## 2. Chiến lược: "Từ khung sườn ra ruột" (Outside-In)

Để tránh hiện tượng **"mỗi trang một kiểu"** ( ví dụ: trang này header/sidebar một kiểu, trang khác lại khác):

- **Cụm 0 (Layout chung) làm trước:** Đóng đinh Header, Topbar, Sidebar và Context Bar trong các Shared Layouts. Toàn bộ các trang con tự động kế thừa giao diện chuẩn.
- **Cụm 1 đến 6:** Chuẩn hóa phần ruột (`@RenderBody()`) theo từng luồng nghiệp vụ.
- **Cụm 7 (Reconciliation Audit):** Rà soát đối chiếu chéo toàn bộ hệ thống để bảo đảm tính chặt chẽ, không có bất kỳ trang nào bị lệch style.

---

## 3. Checklist chi tiết theo từng cụm (Clusters)

### [x] Cụm 0: Móng nhà & Khung Layout dùng chung (Bảo đảm tính nhất quán toàn cục)

- [x] Đồng bộ CSS từ `prototype/assets/css/` sang `src/WebMVC/wwwroot/css/`:
  - `tokens.css`: Hệ màu, spacing, typography variables.
  - `base.css`: CSS reset, font stacks, layout grid, app-topbar band.
  - `components.css`: Buttons, badges, placards, tables, alerts, inputs.
  - `landing.css`: Landing page stylesheet for hero, preview, certificates, steps, features, cta.
- [x] Chuẩn hóa [`src/WebMVC/Views/Shared/_LearnerLayout.cshtml`](./src/WebMVC/Views/Shared/_LearnerLayout.cshtml) theo `prototype/student/dashboard.html`:
  - `app-topbar`: Logo "extra efficient", Badge số dư credit, Button nạp credit, Thông tin tài khoản & Sign out.
  - `app-sidebar`: Cấu trúc `nav-list`, nhóm menu (`nav-list__group`), trạng thái `is-active`, SVG icons.
  - `context-bar`: Thanh trạng thái sticky (Certificate đang chọn, Target band, Progress %).
- [x] Chuẩn hóa [`src/WebMVC/Views/Shared/_AdminLayout.cshtml`](./src/WebMVC/Views/Shared/_AdminLayout.cshtml) theo `prototype/admin/dashboard.html`:
  - `app-topbar`: Admin area badge, email, Sign out.
  - `app-sidebar`: Overview, Content, Users & revenue, Public site.
- [x] Chuẩn hóa [`src/WebMVC/Views/Shared/_Layout.cshtml`](./src/WebMVC/Views/Shared/_Layout.cshtml) cho Public / Auth:
  - `app-shell`, `app-topbar` navy signage band, `app-main`.

---

### [x] Cụm 1: Public & Auth Flows

| Razor View (`src/WebMVC/Views/`) | Prototype File (`prototype/`) | Trạng thái |
| :------------------------------- | :---------------------------- | :--------: |
| `Home/Index.cshtml`              | `landing.html`                |    [x]     |
| `Certificates/Index.cshtml`      | `certificates.html`           |    [x]     |
| `Pricing/Index.cshtml`           | `payment/pricing.html`        |    [x]     |
| `Auth/SignIn.cshtml`             | `auth/login.html`             |    [x]     |
| `Auth/Register.cshtml`           | `auth/register.html`          |    [x]     |
| `Auth/ForgotPassword.cshtml`     | `auth/forgot-password.html`   |    [x]     |
| `Auth/ResetPassword.cshtml`      | `auth/reset-password`         |    [x]     |
| `Auth/VerifyEmail.cshtml`        | `auth/verify-email`           |    [x]     |
| `Auth/Denied.cshtml`             | `auth/denied`                 |    [x]     |

---

### [ ] Cụm 2: Learner Core & Quản lý tài khoản

| Razor View (`src/WebMVC/Views/`)           | Prototype File (`prototype/`)                          | Trạng thái |
| :----------------------------------------- | :----------------------------------------------------- | :--------: |
| `Dashboard/Index.cshtml`                   | `student/dashboard.html`                               |    [ ]     |
| `Profile/Index.cshtml`                     | `student/profile.html`                                 |    [ ]     |
| `Wallet/Index.cshtml`                      | `payment/wallet.html` & `student/target-band-set.html` |    [ ]     |
| `Checkout/Index.cshtml` & `Success.cshtml` | `payment/checkout.html` & `order-detail.html`          |    [ ]     |

---

### [ ] Cụm 3: Luyện tập & Phân tích điểm yếu (Practice & Diagnostics)

| Razor View (`src/WebMVC/Views/`)          | Prototype File (`prototype/`)    | Trạng thái |
| :---------------------------------------- | :------------------------------- | :--------: |
| `Practice/Index.cshtml` (Select section)  | `student/practice-select.html`   |    [ ]     |
| `Practice/Take.cshtml` (Làm bài)          | `student/practice-take.html`     |    [ ]     |
| `Practice/Summary.cshtml` (Kết quả phiên) | `student/practice-summary.html`  |    [ ]     |
| `WeaknessAnalysis/Index.cshtml`           | `student/weakness-analysis.html` |    [ ]     |
| `SavedQuestions/Index.cshtml`             | `student/saved-questions.html`   |    [ ]     |
| `Placement/Index.cshtml` (Select)         | `student/placement-select.html`  |    [ ]     |
| `Placement/Take.cshtml` (Sitting)         | `student/placement-take.html`    |    [ ]     |
| `Placement/Result.cshtml`                 | `student/placement-result.html`  |    [ ]     |

---

### [ ] Cụm 4: Phòng thi & Chấm thi (Exam Sittings & Reviews)

| Razor View (`src/WebMVC/Views/`)               | Prototype File (`prototype/`)                               | Trạng thái |
| :--------------------------------------------- | :---------------------------------------------------------- | :--------: |
| `Exams/Index.cshtml` (Exam list)               | `student/exam-list.html`                                    |    [ ]     |
| `Exams/Instructions.cshtml`                    | `student/exam-instructions.html`                            |    [ ]     |
| `Exams/Take.cshtml` (Listening & Reading)      | `student/exam-listening.html` & `exam-reading.html`         |    [ ]     |
| `Exams/ConfirmSubmit.cshtml`                   | `student/exam-confirm-submit.html`                          |    [ ]     |
| `Exams/Result.cshtml`                          | `student/exam-result.html`                                  |    [ ]     |
| `Exams/Review.cshtml`                          | `student/exam-review.html`                                  |    [ ]     |
| `Exams/History.cshtml`                         | `student/exam-history.html`                                 |    [ ]     |
| `Speaking/Instructions.cshtml` & `Take.cshtml` | `student/speaking-instructions.html` & `exam-speaking.html` |    [ ]     |
| `Speaking/Review.cshtml`                       | `student/speaking-review.html`                              |    [ ]     |
| `Writing/Instructions.cshtml` & `Take.cshtml`  | `student/writing-instructions.html` & `exam-writing.html`   |    [ ]     |
| `Writing/Review.cshtml`                        | `student/writing-review.html`                               |    [ ]     |

---

### [ ] Cụm 5: Lộ trình học, Mentor & Hỗ trợ

| Razor View (`src/WebMVC/Views/`)                   | Prototype File (`prototype/`)                          | Trạng thái |
| :------------------------------------------------- | :----------------------------------------------------- | :--------: |
| `LearningPath/Index.cshtml`                        | `student/learning-path.html` & `path-detail.html`      |    [ ]     |
| `LearningPath/Generated.cshtml` & `Upgrade.cshtml` | `student/path-generated.html` & `path-upgrade.html`    |    [ ]     |
| `Mentors/Index.cshtml` (Danh sách)                 | `student/mentor-list.html`                             |    [ ]     |
| `Mentors/Detail.cshtml` & `Booking.cshtml`         | `student/mentor-detail.html` & `booking-detail.html`   |    [ ]     |
| `Support/Index.cshtml` & `Report.cshtml`           | `student/support-history.html` & `support-report.html` |    [ ]     |

---

### [ ] Cụm 6: Phân hệ Quản trị (Admin Area)

| Razor View (`src/WebMVC/Views/`)         | Prototype File (`prototype/`)                    | Trạng thái |
| :--------------------------------------- | :----------------------------------------------- | :--------: |
| `Admin/Index.cshtml` (Dashboard metrics) | `admin/dashboard.html`                           |    [ ]     |
| `Admin/Exams/` & `Admin/Imports/`        | `admin/exam-list.html`, `exam-editor.html`       |    [ ]     |
| `Admin/Users/`                           | `admin/users.html`                               |    [ ]     |
| `Admin/Orders/` & `Admin/Packages/`      | `admin/orders.html`, `admin/packages.html`       |    [ ]     |
| `Admin/Complaints/`                      | `admin/complaints.html`, `complaint-detail.html` |    [ ]     |
| `Admin/ScoreConversion/`                 | `admin/score-conversion.html`                    |    [ ]     |

---

### [ ] Cụm 7: Cross-Page Reconciliation & Consistency Audit (Kiểm tra đối chiếu chéo)

- [ ] **Header / Topbar Audit:** Kiểm tra số dư credit, tên người dùng, nút sign-out hiển thị đồng nhất ở tất cả các trang.
- [ ] **Sidebar Nav Audit:** Kiểm tra logic `is-active` đánh dấu chính xác mục menu hiện tại ở từng URL, không bị nhầm lẫn giữa các trang con (ví dụ: `/exams/history` khác `/exams`).
- [ ] **Context Bar Audit:** Thanh sticky ngữ cảnh chứng chỉ (Certificate/Target/Progress) cố định chuẩn, không bị giật layout khi cuộn trang.
- [ ] **Buttons & Placards Audit:** Toàn bộ button tuân thủ nghiêm ngặt bảng màu biển hiệu và Inversion rule; placard có viền 2px mực đen sắc nét.
- [ ] **Empty States & Error Alerts:** Đồng bộ style thông báo lỗi/thành công, trạng thái danh sách rỗng (Empty state placard).
- [ ] **Kiểm thử hồi quy:** Chạy `dotnet test` toàn diện (Services.Tests + WebMVC.Tests) đạt 100% pass.

---
