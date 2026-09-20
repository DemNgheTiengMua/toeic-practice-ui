# TOEIC UI Prototype Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dựng prototype HTML/CSS tĩnh gồm 30 màn hình và mọi state của hệ thống "Ôn và thi chứng chỉ TOEIC", để chốt luồng nghiệp vụ trước khi viết code thật.

**Architecture:** Trang HTML tĩnh rời, không build step, không framework. Mọi trang dùng chung `tokens.css` + `base.css` + `components.css`. State hiển thị đổi bằng query param (`?state=loading`) do `state-switch.js` xử lý — nó chỉ bật/tắt class trên `<body>`, không mô phỏng nghiệp vụ. `index.html` là mục lục dẫn tới từng màn và từng state.

**Tech Stack:** HTML5, CSS3 (custom properties, flexbox, grid), vanilla JS (ES2020, không dependency). Không .NET, không npm, không API.

**Spec:** `docs/superpowers/specs/2026-09-20-toeic-ui-design.md`

## Global Constraints

- Prototype tĩnh: không gọi API, không lưu dữ liệu, không đăng nhập thật, không thanh toán thật.
- Không thêm dependency ngoài. Không npm, không CDN, không jQuery (jQuery chỉ thuộc dự án đích, không thuộc prototype).
- Ưu tiên desktop, viewport tham chiếu 1440×900. Không phủ hết breakpoint mobile.
- Mọi màu, spacing, font phải lấy từ CSS custom property trong `tokens.css`. Không hardcode mã màu trong file trang.
- Component đáp án phải nhận **số option động** (Part 2 chỉ có A/B/C, các part khác A/B/C/D). Hardcode 4 option là sai.
- Text UI viết bằng **tiếng Việt**. Nội dung câu hỏi TOEIC giữ tiếng Anh.
- Mọi trang có tải dữ liệu phải phủ 4 state: `loading` (skeleton, không spinner toàn trang), `empty`, `error` (có nút thử lại), `success`.
- Đáp án đúng và giải thích **chỉ** xuất hiện ở màn Review và màn luyện tập. Màn thi không được chứa đáp án đúng trong HTML.
- Accessibility: mỗi input có `<label>`, ảnh có `alt`, nhóm đáp án dùng `role="radiogroup"`, trạng thái chọn qua `aria-checked`. Focus phải thấy được.
- Không dùng `<table>` để layout. Chỉ dùng cho dữ liệu dạng bảng thật (bảng quy đổi điểm, danh sách đơn hàng).
- Commit sau mỗi task, message tiếng Anh không dấu, prefix `feat:` / `chore:` / `docs:`.

**Verification thay cho unit test:** prototype tĩnh không có test runner. Mỗi task kết thúc bằng bước verify thủ công: mở trang bằng `python -m http.server` trong `prototype/`, xem từng state trong danh sách, xác nhận console không có lỗi. Đây là gate của task, không được bỏ.

---

## File Structure

Mọi đường dẫn tương đối từ `prototype/`.

| File | Trách nhiệm |
|---|---|
| `index.html` | Mục lục 30 màn + link tới từng state |
| `assets/css/tokens.css` | Custom property: màu, spacing, font, radius, shadow, z-index |
| `assets/css/base.css` | Reset, typography, layout shell (topbar, sidebar, container), utility |
| `assets/css/components.css` | Button, card, badge, input, option-list, timer, navigator, skeleton, alert, modal, table, stepper |
| `assets/js/state-switch.js` | Đọc `?state=`, gắn `data-state` lên `<body>`, render thanh chọn state |
| `assets/img/` | Ảnh Part 1, avatar, logo — dùng SVG placeholder tự vẽ |
| `auth/login.html` `auth/register.html` `auth/forgot-password.html` | 3 màn auth |
| `student/dashboard.html` | Dashboard học viên |
| `student/exam-list.html` | Danh sách đề + biến thể paywall |
| `student/exam-instructions.html` | Hướng dẫn trước thi |
| `student/exam-listening.html` | Màn thi Listening |
| `student/exam-reading.html` | Màn thi Reading |
| `student/question-navigator.html` | Panel điều hướng 200 câu (component, vẽ riêng để review) |
| `student/exam-confirm-submit.html` | Xác nhận submit |
| `student/exam-result.html` | Kết quả + quy đổi |
| `student/exam-review.html` | Review đáp án + giải thích |
| `student/exam-history.html` | Lịch sử các lần thi |
| `student/profile.html` | Hồ sơ cá nhân |
| `student/practice-select.html` | Chọn part để luyện |
| `student/practice-take.html` | Màn luyện tập |
| `student/practice-summary.html` | Tổng kết phiên luyện |
| `payment/pricing.html` | Bảng giá & chọn gói |
| `payment/checkout.html` | Checkout |
| `payment/gateway-mock.html` | Trang cổng thanh toán vẽ giả |
| `payment/payment-result.html` | Kết quả thanh toán (3 nhánh) |
| `payment/wallet.html` | Ví lượt thi + lịch sử giao dịch |
| `admin/dashboard.html` | Thống kê |
| `admin/exam-list.html` | Danh sách đề |
| `admin/exam-editor.html` | Wizard soạn đề |
| `admin/question-editor.html` | Soạn câu hỏi theo part |
| `admin/users.html` | Quản lý người dùng |
| `admin/score-conversion.html` | Bảng quy đổi điểm |
| `admin/packages.html` | Quản lý gói & giá |
| `admin/orders.html` | Quản lý đơn hàng |

CSS chia 3 file theo trách nhiệm chứ không theo trang: token (giá trị thô) → base (khung) → components (thành phần tái dùng). Trang không có CSS riêng; nếu một trang cần style đặc thù, style đó thuộc `components.css` dưới một class có tiền tố rõ ràng (VD `.exam-audio`).

---

### Task 1: Nền — design token, base layout, state switcher

Task này tạo hạ tầng mọi task sau dùng. Không có màn nghiệp vụ nào ở đây, nhưng nó phải xong trước mọi thứ khác.

**Files:**
- Create: `prototype/assets/css/tokens.css`
- Create: `prototype/assets/css/base.css`
- Create: `prototype/assets/js/state-switch.js`
- Create: `prototype/_sandbox.html` (trang thử nền, xoá ở Task 16)

**Interfaces:**
- Consumes: không
- Produces:
  - Token: `--color-bg`, `--color-surface`, `--color-border`, `--color-text`, `--color-text-muted`, `--color-primary`, `--color-primary-hover`, `--color-primary-text`, `--color-success`, `--color-warning`, `--color-danger`, `--color-info`, `--space-1`…`--space-8`, `--font-sans`, `--font-mono`, `--text-xs`…`--text-3xl`, `--radius-sm`, `--radius-md`, `--radius-lg`, `--shadow-sm`, `--shadow-md`, `--z-modal`
  - Layout class: `.app-shell`, `.app-topbar`, `.app-sidebar`, `.app-main`, `.container`, `.stack`, `.row`, `.grid-2`, `.grid-3`
  - JS: `state-switch.js` tự chạy khi load, đọc `?state=<name>`, set `document.body.dataset.state`, render thanh chọn state ở góc dưới phải từ `<body data-states="loading,empty,error,success">`. Không export hàm.
  - Convention state: CSS ẩn/hiện bằng `body[data-state="loading"] .only-loading { display: block }`. Class `.only-<state>` mặc định `display: none`.

- [ ] **Step 1: Viết `tokens.css`**

```css
:root {
  /* màu nền & bề mặt */
  --color-bg: #f6f7f9;
  --color-surface: #ffffff;
  --color-border: #e3e6ea;
  --color-text: #1b1f24;
  --color-text-muted: #6b7480;

  /* màu thương hiệu */
  --color-primary: #1f6feb;
  --color-primary-hover: #1a5fd0;
  --color-primary-text: #ffffff;

  /* màu trạng thái */
  --color-success: #1a7f4b;
  --color-warning: #b7791f;
  --color-danger: #c62828;
  --color-info: #0d6e8c;

  /* spacing, thang 4px */
  --space-1: 4px;  --space-2: 8px;  --space-3: 12px; --space-4: 16px;
  --space-5: 24px; --space-6: 32px; --space-7: 48px; --space-8: 64px;

  /* typography */
  --font-sans: "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
  --font-mono: "Cascadia Mono", Consolas, monospace;
  --text-xs: 12px;  --text-sm: 13px; --text-base: 15px;
  --text-lg: 18px;  --text-xl: 22px; --text-2xl: 28px; --text-3xl: 34px;

  /* hình khối */
  --radius-sm: 4px; --radius-md: 8px; --radius-lg: 14px;
  --shadow-sm: 0 1px 2px rgba(16, 24, 40, .06);
  --shadow-md: 0 4px 12px rgba(16, 24, 40, .1);
  --z-modal: 100;
}
```

- [ ] **Step 2: Viết `base.css`**

```css
*, *::before, *::after { box-sizing: border-box; }
html, body { margin: 0; padding: 0; }
body {
  font-family: var(--font-sans);
  font-size: var(--text-base);
  color: var(--color-text);
  background: var(--color-bg);
  line-height: 1.5;
}
h1, h2, h3 { margin: 0 0 var(--space-3); line-height: 1.25; }
h1 { font-size: var(--text-2xl); }
h2 { font-size: var(--text-xl); }
h3 { font-size: var(--text-lg); }
p { margin: 0 0 var(--space-3); }
a { color: var(--color-primary); }
:focus-visible { outline: 2px solid var(--color-primary); outline-offset: 2px; }

/* shell */
.app-shell { display: grid; grid-template-rows: auto 1fr; min-height: 100vh; }
.app-shell--with-sidebar .app-body {
  display: grid; grid-template-columns: 240px 1fr; min-height: 0;
}
.app-topbar {
  display: flex; align-items: center; justify-content: space-between;
  gap: var(--space-4);
  padding: var(--space-3) var(--space-5);
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);
}
.app-sidebar {
  padding: var(--space-4);
  background: var(--color-surface);
  border-right: 1px solid var(--color-border);
}
.app-main { padding: var(--space-5); min-width: 0; }
.container { width: 100%; max-width: 1180px; margin: 0 auto; }
.container--narrow { max-width: 460px; }

/* layout helper */
.stack > * + * { margin-top: var(--space-4); }
.row { display: flex; align-items: center; gap: var(--space-3); }
.row--between { justify-content: space-between; }
.grid-2 { display: grid; grid-template-columns: repeat(2, 1fr); gap: var(--space-4); }
.grid-3 { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--space-4); }
.text-muted { color: var(--color-text-muted); }
.text-sm { font-size: var(--text-sm); }

/* hiển thị theo state */
.only-loading, .only-empty, .only-error, .only-success,
.only-resumed, .only-expired, .only-submitting, .only-offline,
.only-pending, .only-failed, .only-paywall { display: none; }
body[data-state="loading"]    .only-loading,
body[data-state="empty"]      .only-empty,
body[data-state="error"]      .only-error,
body[data-state="success"]    .only-success,
body[data-state="resumed"]    .only-resumed,
body[data-state="expired"]    .only-expired,
body[data-state="submitting"] .only-submitting,
body[data-state="offline"]    .only-offline,
body[data-state="pending"]    .only-pending,
body[data-state="failed"]     .only-failed,
body[data-state="paywall"]    .only-paywall { display: revert; }
```

- [ ] **Step 3: Viết `state-switch.js`**

```js
/**
 * Đổi state hiển thị của trang prototype.
 * Đọc ?state=<name>, gắn lên body[data-state], render thanh chọn state.
 * Không mô phỏng nghiệp vụ — chỉ bật/tắt class .only-<state>.
 */
(function () {
  "use strict";

  var body = document.body;
  var states = (body.dataset.states || "success")
    .split(",")
    .map(function (s) { return s.trim(); })
    .filter(Boolean);

  var requested = new URLSearchParams(location.search).get("state");
  var current = states.indexOf(requested) !== -1 ? requested : states[0];
  body.dataset.state = current;

  if (states.length < 2) return;

  var bar = document.createElement("nav");
  bar.className = "state-switcher";
  bar.setAttribute("aria-label", "Chon state de xem");

  var label = document.createElement("span");
  label.className = "state-switcher__label";
  label.textContent = "State:";
  bar.appendChild(label);

  states.forEach(function (name) {
    var link = document.createElement("a");
    var url = new URL(location.href);
    url.searchParams.set("state", name);
    link.href = url.pathname + url.search;
    link.textContent = name;
    link.className = "state-switcher__item";
    if (name === current) {
      link.setAttribute("aria-current", "true");
    }
    bar.appendChild(link);
  });

  body.appendChild(bar);
})();
```

- [ ] **Step 4: Thêm style cho thanh chọn state vào cuối `base.css`**

```css
.state-switcher {
  position: fixed; right: var(--space-4); bottom: var(--space-4);
  display: flex; align-items: center; gap: var(--space-2);
  flex-wrap: wrap; max-width: 60vw;
  padding: var(--space-2) var(--space-3);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-md);
  font-size: var(--text-xs);
  z-index: var(--z-modal);
}
.state-switcher__label { color: var(--color-text-muted); }
.state-switcher__item {
  padding: 2px var(--space-2);
  border-radius: var(--radius-sm);
  text-decoration: none;
}
.state-switcher__item[aria-current="true"] {
  background: var(--color-primary);
  color: var(--color-primary-text);
}
```

- [ ] **Step 5: Viết `_sandbox.html` để thử nền**

```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Sandbox — thử nền</title>
  <link rel="stylesheet" href="assets/css/tokens.css">
  <link rel="stylesheet" href="assets/css/base.css">
</head>
<body data-states="loading,empty,error,success">
  <div class="app-shell">
    <header class="app-topbar">
      <strong>Sandbox</strong>
      <span class="text-muted text-sm">thử token + state switcher</span>
    </header>
    <main class="app-main">
      <div class="container stack">
        <h1>Thử nền</h1>
        <p class="only-loading">Đang tải…</p>
        <p class="only-empty">Chưa có dữ liệu.</p>
        <p class="only-error">Đã xảy ra lỗi.</p>
        <p class="only-success">Tải xong.</p>
        <div class="grid-3">
          <div style="background: var(--color-primary); color: var(--color-primary-text); padding: var(--space-4); border-radius: var(--radius-md);">primary</div>
          <div style="background: var(--color-success); color: var(--color-primary-text); padding: var(--space-4); border-radius: var(--radius-md);">success</div>
          <div style="background: var(--color-danger); color: var(--color-primary-text); padding: var(--space-4); border-radius: var(--radius-md);">danger</div>
        </div>
      </div>
    </main>
  </div>
  <script src="assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 6: Verify**

```bash
cd prototype && python -m http.server 8080
```

Mở `http://localhost:8080/_sandbox.html`. Kiểm:
- Thanh chọn state hiện ở góc dưới phải với 4 mục, `success` đang active.
- Bấm từng mục: đúng một dòng text đổi theo, URL có `?state=`.
- Vào thẳng `?state=error`: dòng lỗi hiện ngay khi load.
- Vào `?state=xxx` (không hợp lệ): rơi về `success`, không lỗi JS.
- Console không có lỗi.

- [ ] **Step 7: Commit**

```bash
git add prototype/assets prototype/_sandbox.html
git commit -m "feat: add design tokens, base layout and state switcher"
```

---

### Task 2: Component library

Mọi màn sau chỉ lắp component từ đây. Task này không tạo màn nghiệp vụ nào, nhưng nếu component thiếu thì các task sau sẽ tự đẻ style rời — đó là thứ cần tránh.

**Files:**
- Create: `prototype/assets/css/components.css`
- Modify: `prototype/_sandbox.html` (thêm khu trưng bày component)

**Interfaces:**
- Consumes: token từ Task 1 (`--color-*`, `--space-*`, `--text-*`, `--radius-*`, `--shadow-*`, `--z-modal`)
- Produces các class dưới đây. Task sau phải dùng đúng tên này:
  - Button: `.btn`, biến thể `.btn--primary` `.btn--ghost` `.btn--danger`, kích thước `.btn--lg` `.btn--sm`, trạng thái `[disabled]`
  - Card: `.card`, `.card__header`, `.card__body`, `.card__footer`
  - Badge: `.badge`, biến thể `.badge--success` `.badge--warning` `.badge--danger` `.badge--info` `.badge--muted`
  - Form: `.field`, `.field__label`, `.field__input`, `.field__hint`, `.field__error`, `.field--invalid`
  - Đáp án: `.option-list`, `.option`, `.option__marker`, `.option__text`, trạng thái `.option--selected` `.option--correct` `.option--wrong`
  - Timer: `.timer`, `.timer__value`, biến thể `.timer--warning` `.timer--critical`
  - Navigator: `.navigator`, `.navigator__grid`, `.navigator__cell`, trạng thái `.is-unanswered` `.is-answered` `.is-marked` `.is-current`
  - Skeleton: `.skeleton`, `.skeleton--text`, `.skeleton--title`, `.skeleton--block`
  - Alert: `.alert`, biến thể `.alert--info` `.alert--success` `.alert--warning` `.alert--danger`
  - Empty/Error state: `.state-block`, `.state-block__icon`, `.state-block__title`, `.state-block__desc`
  - Modal: `.modal-overlay`, `.modal`, `.modal__header`, `.modal__body`, `.modal__footer`
  - Table: `.table`, `.table--striped`
  - Stepper: `.stepper`, `.stepper__item`, trạng thái `.is-done` `.is-current`
  - Progress: `.progress`, `.progress__bar`
  - Audio: `.exam-audio`, `.exam-audio__status`

- [ ] **Step 1: Viết `components.css` — phần button, card, badge, form**

```css
/* ===== Button ===== */
.btn {
  display: inline-flex; align-items: center; justify-content: center;
  gap: var(--space-2);
  padding: var(--space-2) var(--space-4);
  font: inherit; font-size: var(--text-sm);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-surface);
  color: var(--color-text);
  cursor: pointer;
  text-decoration: none;
}
.btn:hover { background: var(--color-bg); }
.btn[disabled], .btn[aria-disabled="true"] {
  opacity: .55; cursor: not-allowed; pointer-events: none;
}
.btn--primary {
  background: var(--color-primary);
  border-color: var(--color-primary);
  color: var(--color-primary-text);
}
.btn--primary:hover { background: var(--color-primary-hover); }
.btn--ghost { background: transparent; border-color: transparent; }
.btn--ghost:hover { background: var(--color-bg); }
.btn--danger {
  background: var(--color-danger); border-color: var(--color-danger); color: var(--color-primary-text);
}
.btn--lg { padding: var(--space-3) var(--space-5); font-size: var(--text-base); }
.btn--sm { padding: var(--space-1) var(--space-3); font-size: var(--text-xs); }

/* ===== Card ===== */
.card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-sm);
}
.card__header {
  padding: var(--space-4) var(--space-5);
  border-bottom: 1px solid var(--color-border);
}
.card__body { padding: var(--space-5); }
.card__footer {
  padding: var(--space-4) var(--space-5);
  border-top: 1px solid var(--color-border);
  background: var(--color-bg);
  border-radius: 0 0 var(--radius-lg) var(--radius-lg);
}

/* ===== Badge ===== */
.badge {
  display: inline-block;
  padding: 2px var(--space-2);
  border-radius: 999px;
  font-size: var(--text-xs);
  background: var(--color-bg);
  border: 1px solid var(--color-border);
}
.badge--success { background: var(--color-success-soft); border-color: var(--color-success-soft-border); color: var(--color-success); }
.badge--warning { background: var(--color-warning-soft); border-color: var(--color-warning-soft-border); color: var(--color-warning); }
.badge--danger  { background: var(--color-danger-soft); border-color: var(--color-danger-soft-border); color: var(--color-danger); }
.badge--info    { background: var(--color-info-soft); border-color: var(--color-info-soft-border); color: var(--color-info); }
.badge--muted   { color: var(--color-text-muted); }

/* ===== Form ===== */
.field { display: block; }
.field__label {
  display: block; margin-bottom: var(--space-1);
  font-size: var(--text-sm); font-weight: 600;
}
.field__input {
  width: 100%;
  padding: var(--space-2) var(--space-3);
  font: inherit;
  color: var(--color-text);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}
.field__input:focus-visible { border-color: var(--color-primary); }
.field__hint { margin-top: var(--space-1); font-size: var(--text-xs); color: var(--color-text-muted); }
.field__error { margin-top: var(--space-1); font-size: var(--text-xs); color: var(--color-danger); }
.field--invalid .field__input { border-color: var(--color-danger); }
```

- [ ] **Step 2: Viết tiếp — option list, timer, navigator**

```css
/* ===== Đáp án ===== */
/* Số option động: Part 2 chỉ A/B/C, part khác A/B/C/D. Không giả định 4. */
.option-list { display: flex; flex-direction: column; gap: var(--space-2); }
.option {
  display: flex; align-items: flex-start; gap: var(--space-3);
  padding: var(--space-3) var(--space-4);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  cursor: pointer;
}
.option:hover { border-color: var(--color-primary); }
.option__marker {
  flex: 0 0 auto;
  width: 26px; height: 26px;
  display: grid; place-items: center;
  border: 1px solid var(--color-border);
  border-radius: 50%;
  font-size: var(--text-sm); font-weight: 600;
}
.option__text { flex: 1 1 auto; }
.option--selected {
  border-color: var(--color-primary);
  background: var(--color-primary-soft);
}
.option--selected .option__marker {
  background: var(--color-primary);
  border-color: var(--color-primary);
  color: var(--color-primary-text);
}
/* chỉ dùng ở màn review + luyện tập */
.option--correct { border-color: var(--color-success); background: var(--color-success-soft); }
.option--correct .option__marker { background: var(--color-success); border-color: var(--color-success); color: var(--color-primary-text); }
.option--wrong { border-color: var(--color-danger); background: var(--color-danger-soft); }
.option--wrong .option__marker { background: var(--color-danger); border-color: var(--color-danger); color: var(--color-primary-text); }

/* ===== Timer ===== */
.timer {
  display: inline-flex; align-items: center; gap: var(--space-2);
  padding: var(--space-2) var(--space-3);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-surface);
}
.timer__value { font-family: var(--font-mono); font-size: var(--text-lg); font-variant-numeric: tabular-nums; }
.timer--warning  { border-color: var(--color-warning); color: var(--color-warning); }
.timer--critical { border-color: var(--color-danger);  color: var(--color-danger); }

/* ===== Navigator 200 câu ===== */
.navigator { display: flex; flex-direction: column; gap: var(--space-3); }
.navigator__grid {
  display: grid;
  grid-template-columns: repeat(10, 1fr);
  gap: var(--space-1);
}
.navigator__cell {
  aspect-ratio: 1;
  display: grid; place-items: center;
  font-size: var(--text-xs);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface);
  cursor: pointer;
}
.navigator__cell.is-unanswered { color: var(--color-text-muted); }
.navigator__cell.is-answered {
  background: var(--color-primary); border-color: var(--color-primary); color: var(--color-primary-text);
}
.navigator__cell.is-marked {
  background: var(--color-warning-soft); border-color: var(--color-warning); color: var(--color-warning); font-weight: 700;
}
.navigator__cell.is-current { outline: 2px solid var(--color-text); outline-offset: 1px; }
```

- [ ] **Step 3: Viết tiếp — skeleton, alert, state block, modal, table, stepper, progress, audio**

```css
/* ===== Skeleton ===== */
@keyframes skeleton-pulse { 0%, 100% { opacity: 1 } 50% { opacity: .55 } }
.skeleton {
  background: var(--color-track);
  border-radius: var(--radius-sm);
  animation: skeleton-pulse 1.4s ease-in-out infinite;
}
.skeleton--text  { height: 12px; margin-bottom: var(--space-2); }
.skeleton--title { height: 22px; width: 42%; margin-bottom: var(--space-3); }
.skeleton--block { height: 120px; }
@media (prefers-reduced-motion: reduce) { .skeleton { animation: none } }

/* ===== Alert ===== */
.alert {
  padding: var(--space-3) var(--space-4);
  border: 1px solid var(--color-border);
  border-left-width: 4px;
  border-radius: var(--radius-md);
  background: var(--color-surface);
}
.alert--info    { border-left-color: var(--color-info); }
.alert--success { border-left-color: var(--color-success); }
.alert--warning { border-left-color: var(--color-warning); }
.alert--danger  { border-left-color: var(--color-danger); }

/* ===== Empty / Error ===== */
.state-block {
  padding: var(--space-7) var(--space-5);
  text-align: center;
  color: var(--color-text-muted);
}
.state-block__icon { font-size: var(--text-3xl); line-height: 1; margin-bottom: var(--space-3); }
.state-block__title { font-size: var(--text-lg); font-weight: 600; color: var(--color-text); margin-bottom: var(--space-2); }
.state-block__desc { margin-bottom: var(--space-4); }

/* ===== Modal ===== */
.modal-overlay {
  position: fixed; inset: 0;
  display: grid; place-items: center;
  padding: var(--space-5);
  background: var(--color-overlay);
  z-index: var(--z-modal);
}
.modal {
  width: 100%; max-width: 480px;
  background: var(--color-surface);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-md);
}
.modal__header { padding: var(--space-4) var(--space-5); border-bottom: 1px solid var(--color-border); }
.modal__body { padding: var(--space-5); }
.modal__footer {
  display: flex; justify-content: flex-end; gap: var(--space-2);
  padding: var(--space-4) var(--space-5);
  border-top: 1px solid var(--color-border);
}

/* ===== Table ===== */
.table { width: 100%; border-collapse: collapse; font-size: var(--text-sm); }
.table th, .table td {
  padding: var(--space-3);
  text-align: left;
  border-bottom: 1px solid var(--color-border);
}
.table th { font-weight: 600; background: var(--color-bg); }
.table--striped tbody tr:nth-child(even) { background: var(--color-bg); }

/* ===== Stepper ===== */
.stepper { display: flex; gap: var(--space-2); flex-wrap: wrap; }
.stepper__item {
  padding: var(--space-2) var(--space-3);
  font-size: var(--text-sm);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  color: var(--color-text-muted);
}
.stepper__item.is-done { border-color: var(--color-success); color: var(--color-success); }
.stepper__item.is-current { border-color: var(--color-primary); color: var(--color-primary); font-weight: 600; }

/* ===== Progress ===== */
.progress { height: 8px; background: var(--color-track); border-radius: 999px; overflow: hidden; }
.progress__bar { height: 100%; background: var(--color-primary); }

/* ===== Audio (màn thi Listening) ===== */
.exam-audio {
  display: flex; align-items: center; gap: var(--space-4);
  padding: var(--space-4);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}
.exam-audio__status { font-size: var(--text-sm); color: var(--color-text-muted); }
```

- [ ] **Step 4: Thêm khu trưng bày component vào `_sandbox.html`**

Thêm `<link rel="stylesheet" href="assets/css/components.css">` sau `base.css`, rồi thêm vào trong `.container.stack`:

```html
<h2>Component</h2>

<div class="card">
  <div class="card__header"><strong>Button &amp; badge</strong></div>
  <div class="card__body stack">
    <div class="row">
      <button class="btn btn--primary">Primary</button>
      <button class="btn">Default</button>
      <button class="btn btn--ghost">Ghost</button>
      <button class="btn btn--danger">Danger</button>
      <button class="btn btn--primary" disabled>Disabled</button>
    </div>
    <div class="row">
      <span class="badge badge--success">Đã chấm</span>
      <span class="badge badge--warning">Đang xử lý</span>
      <span class="badge badge--danger">Thất bại</span>
      <span class="badge badge--info">Mới</span>
      <span class="badge badge--muted">Nháp</span>
    </div>
  </div>
</div>

<div class="card">
  <div class="card__header"><strong>Đáp án — 3 option (Part 2) và 4 option</strong></div>
  <div class="card__body grid-2">
    <div class="option-list" role="radiogroup" aria-label="Câu 8 (Part 2)">
      <label class="option option--selected">
        <span class="option__marker">A</span>
        <span class="option__text">At the front desk.</span>
      </label>
      <label class="option">
        <span class="option__marker">B</span>
        <span class="option__text">Around nine o'clock.</span>
      </label>
      <label class="option">
        <span class="option__marker">C</span>
        <span class="option__text">Yes, I already did.</span>
      </label>
    </div>
    <div class="option-list" role="radiogroup" aria-label="Câu 101 (Part 5)">
      <label class="option option--correct">
        <span class="option__marker">A</span>
        <span class="option__text">announced</span>
      </label>
      <label class="option option--wrong">
        <span class="option__marker">B</span>
        <span class="option__text">announcing</span>
      </label>
      <label class="option">
        <span class="option__marker">C</span>
        <span class="option__text">announcement</span>
      </label>
      <label class="option">
        <span class="option__marker">D</span>
        <span class="option__text">to announce</span>
      </label>
    </div>
  </div>
</div>

<div class="card">
  <div class="card__header"><strong>Timer, progress, navigator</strong></div>
  <div class="card__body stack">
    <div class="row">
      <span class="timer"><span class="timer__value">01:58:20</span></span>
      <span class="timer timer--warning"><span class="timer__value">00:09:44</span></span>
      <span class="timer timer--critical"><span class="timer__value">00:01:07</span></span>
    </div>
    <div class="progress"><div class="progress__bar" style="width: 64%"></div></div>
    <div class="navigator__grid" style="max-width: 320px">
      <span class="navigator__cell is-answered">1</span>
      <span class="navigator__cell is-answered">2</span>
      <span class="navigator__cell is-marked">3</span>
      <span class="navigator__cell is-current is-unanswered">4</span>
      <span class="navigator__cell is-unanswered">5</span>
      <span class="navigator__cell is-unanswered">6</span>
      <span class="navigator__cell is-answered">7</span>
      <span class="navigator__cell is-unanswered">8</span>
      <span class="navigator__cell is-marked">9</span>
      <span class="navigator__cell is-answered">10</span>
    </div>
  </div>
</div>

<div class="card">
  <div class="card__header"><strong>Alert, skeleton, state block, stepper</strong></div>
  <div class="card__body stack">
    <div class="alert alert--info">Bài thi sẽ tự nộp khi hết thời gian.</div>
    <div class="alert alert--warning">Bạn còn 12 câu chưa trả lời.</div>
    <div class="alert alert--danger">Không lưu được đáp án. Đang thử lại…</div>
    <div>
      <div class="skeleton skeleton--title"></div>
      <div class="skeleton skeleton--text"></div>
      <div class="skeleton skeleton--text" style="width: 78%"></div>
    </div>
    <div class="state-block">
      <div class="state-block__icon" aria-hidden="true">□</div>
      <div class="state-block__title">Chưa có bài thi nào</div>
      <p class="state-block__desc">Hoàn thành bài thi đầu tiên để xem tiến độ.</p>
      <button class="btn btn--primary">Bắt đầu thi</button>
    </div>
    <div class="stepper">
      <span class="stepper__item is-done">1. Thông tin đề</span>
      <span class="stepper__item is-current">2. Upload audio</span>
      <span class="stepper__item">3. Nhập câu hỏi</span>
      <span class="stepper__item">4. Xem lại</span>
    </div>
  </div>
</div>
```

- [ ] **Step 5: Verify**

Mở `http://localhost:8080/_sandbox.html`. Kiểm:
- Nhóm đáp án bên trái có **đúng 3** option A/B/C, bên phải có **4** option — không cái nào bị cắt hay thừa.
- `.option--correct` xanh, `.option--wrong` đỏ, `.option--selected` xanh dương.
- Timer 3 mức đổi màu; số không nhảy ngang (tabular-nums).
- Navigator: 4 trạng thái phân biệt được **không chỉ bằng màu** (marked in đậm, current có outline).
- Skeleton nhấp nháy; bật "reduce motion" trong OS thì dừng.
- Không có scrollbar ngang ở 1440px.
- Console không lỗi.

- [ ] **Step 6: Commit**

```bash
git add prototype/assets/css/components.css prototype/_sandbox.html
git commit -m "feat: add component library for prototype"
```

---

### Task 3: Auth — đăng nhập, đăng ký, quên mật khẩu

**Files:**
- Create: `prototype/auth/login.html`
- Create: `prototype/auth/register.html`
- Create: `prototype/auth/forgot-password.html`

**Interfaces:**
- Consumes: token + layout từ Task 1, component `.card` `.field` `.btn` `.alert` `.field--invalid` từ Task 2
- Produces: pattern trang auth mà mọi task sau tham chiếu — `.app-shell` không sidebar, `.container--narrow`, đường dẫn CSS lùi một cấp (`../assets/css/...`)

Ba trang auth không tải dữ liệu nên **không có** state `loading`/`empty`. State cần vẽ: `success` (form trống, bình thường), `error` (sai thông tin / lỗi server), `submitting` (nút khoá, đang gửi).

- [ ] **Step 1: Viết `auth/login.html`**

```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Đăng nhập — TOEIC Practice</title>
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="success,error,submitting">
  <div class="app-shell">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <a class="btn btn--ghost btn--sm" href="../index.html">Mục lục prototype</a>
    </header>
    <main class="app-main">
      <div class="container container--narrow stack">
        <h1>Đăng nhập</h1>

        <div class="alert alert--danger only-error" role="alert">
          Email hoặc mật khẩu không đúng.
        </div>

        <div class="card">
          <div class="card__body stack">
            <div class="field">
              <label class="field__label" for="login-email">Email</label>
              <input class="field__input" id="login-email" type="email"
                     autocomplete="email" placeholder="ban@example.com">
            </div>

            <div class="field">
              <label class="field__label" for="login-password">Mật khẩu</label>
              <input class="field__input" id="login-password" type="password"
                     autocomplete="current-password">
              <p class="field__hint">
                <a href="forgot-password.html">Quên mật khẩu?</a>
              </p>
            </div>

            <label class="row">
              <input type="checkbox" id="login-remember">
              <span class="text-sm">Ghi nhớ đăng nhập</span>
            </label>
          </div>
          <div class="card__footer stack">
            <button class="btn btn--primary btn--lg only-success" style="width: 100%">
              Đăng nhập
            </button>
            <button class="btn btn--primary btn--lg only-error" style="width: 100%">
              Đăng nhập
            </button>
            <button class="btn btn--primary btn--lg only-submitting" style="width: 100%" disabled>
              Đang đăng nhập…
            </button>
            <p class="text-sm text-muted" style="margin: 0; text-align: center">
              Chưa có tài khoản? <a href="register.html">Đăng ký</a>
            </p>
          </div>
        </div>
      </div>
    </main>
  </div>
  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 2: Viết `auth/register.html`**

Cùng khung với `login.html` (đổi `<title>`, `<h1>` thành "Đăng ký"). Khác ở phần field và state:

```html
        <div class="card">
          <div class="card__body stack">
            <div class="field">
              <label class="field__label" for="reg-name">Họ và tên</label>
              <input class="field__input" id="reg-name" type="text" autocomplete="name">
            </div>

            <div class="field">
              <label class="field__label" for="reg-email">Email</label>
              <input class="field__input" id="reg-email" type="email" autocomplete="email">
            </div>

            <!-- state error: minh hoạ field không hợp lệ -->
            <div class="field only-error field--invalid">
              <label class="field__label" for="reg-email-bad">Email</label>
              <input class="field__input" id="reg-email-bad" type="email" value="ban@example">
              <p class="field__error">Email này đã được dùng.</p>
            </div>

            <div class="field">
              <label class="field__label" for="reg-password">Mật khẩu</label>
              <input class="field__input" id="reg-password" type="password"
                     autocomplete="new-password">
              <p class="field__hint">Tối thiểu 8 ký tự, có chữ và số.</p>
            </div>

            <div class="field">
              <label class="field__label" for="reg-confirm">Nhập lại mật khẩu</label>
              <input class="field__input" id="reg-confirm" type="password"
                     autocomplete="new-password">
            </div>

            <label class="row">
              <input type="checkbox" id="reg-terms">
              <span class="text-sm">Tôi đồng ý với điều khoản sử dụng</span>
            </label>
          </div>
          <div class="card__footer stack">
            <button class="btn btn--primary btn--lg only-success" style="width: 100%">Tạo tài khoản</button>
            <button class="btn btn--primary btn--lg only-error" style="width: 100%">Tạo tài khoản</button>
            <button class="btn btn--primary btn--lg only-submitting" style="width: 100%" disabled>Đang tạo tài khoản…</button>
            <p class="text-sm text-muted" style="margin: 0; text-align: center">
              Đã có tài khoản? <a href="login.html">Đăng nhập</a>
            </p>
          </div>
        </div>
```

Lưu ý: hai block field email (một thường, một `.only-error`) là cách prototype tĩnh thể hiện trạng thái invalid mà không cần JS. Giữ `id` khác nhau để không trùng.

- [ ] **Step 3: Viết `auth/forgot-password.html`**

`data-states="success,submitting,sent"`. Thêm `.only-sent` vào danh sách class ẩn trong `base.css` nếu chưa có — cụ thể là thêm `.only-sent` vào nhóm `display: none` và thêm dòng `body[data-state="sent"] .only-sent` vào nhóm hiện.

```html
        <h1>Quên mật khẩu</h1>

        <div class="card only-success">
          <div class="card__body stack">
            <p class="text-muted">Nhập email, chúng tôi sẽ gửi liên kết đặt lại mật khẩu.</p>
            <div class="field">
              <label class="field__label" for="fp-email">Email</label>
              <input class="field__input" id="fp-email" type="email" autocomplete="email">
            </div>
          </div>
          <div class="card__footer">
            <button class="btn btn--primary btn--lg" style="width: 100%">Gửi liên kết</button>
          </div>
        </div>

        <div class="card only-submitting">
          <div class="card__body stack">
            <p class="text-muted">Nhập email, chúng tôi sẽ gửi liên kết đặt lại mật khẩu.</p>
            <div class="field">
              <label class="field__label" for="fp-email-2">Email</label>
              <input class="field__input" id="fp-email-2" type="email" value="ban@example.com" disabled>
            </div>
          </div>
          <div class="card__footer">
            <button class="btn btn--primary btn--lg" style="width: 100%" disabled>Đang gửi…</button>
          </div>
        </div>

        <div class="card only-sent">
          <div class="card__body">
            <div class="state-block">
              <div class="state-block__icon" aria-hidden="true">✉</div>
              <div class="state-block__title">Đã gửi liên kết</div>
              <p class="state-block__desc">
                Kiểm tra hộp thư <strong>ban@example.com</strong>. Liên kết hết hạn sau 15 phút.
              </p>
              <a class="btn" href="login.html">Về trang đăng nhập</a>
            </div>
          </div>
        </div>
```

- [ ] **Step 4: Verify**

Mở lần lượt 3 trang qua `http://localhost:8080/auth/...`. Kiểm:
- CSS load được (đường dẫn `../assets/` đúng) — trang có nền xám, card trắng bo góc.
- `login.html?state=error`: alert đỏ hiện, nút vẫn bấm được.
- `login.html?state=submitting`: nút đổi chữ "Đang đăng nhập…" và bị khoá.
- `register.html?state=error`: field email viền đỏ + thông báo "Email này đã được dùng".
- `forgot-password.html?state=sent`: hiện khối "Đã gửi liên kết", **không** còn form.
- Click vào chữ của mỗi label thì focus nhảy vào input tương ứng (label gắn đúng `for`).
- Tab qua toàn trang: viền focus thấy rõ ở mọi input và button.
- Console không lỗi.

- [ ] **Step 5: Commit**

```bash
git add prototype/auth prototype/assets/css/base.css
git commit -m "feat: add auth screens for prototype"
```

---

### Task 4: Student shell + Dashboard

**Files:**
- Create: `prototype/student/dashboard.html`
- Modify: `prototype/assets/css/components.css` (thêm `.nav-list`, `.stat`)

**Interfaces:**
- Consumes: Task 1 layout, Task 2 component
- Produces:
  - Markup shell học viên mà 10 trang student sau **copy nguyên**: `.app-shell.app-shell--with-sidebar` > `.app-topbar` + `.app-body` > `.app-sidebar` + `.app-main`
  - Sidebar dùng `.nav-list` / `.nav-list__item` / `.nav-list__item.is-active`
  - Ô số liệu dùng `.stat` / `.stat__label` / `.stat__value` / `.stat__hint`
  - Topbar luôn có chip ví lượt: `<span class="badge badge--info">Còn 3 lượt thi</span>`

State: `loading` (skeleton), `empty` (chưa thi lần nào), `error`, `success`.

- [ ] **Step 1: Thêm `.nav-list` và `.stat` vào `components.css`**

```css
/* ===== Sidebar nav ===== */
.nav-list { display: flex; flex-direction: column; gap: var(--space-1); }
.nav-list__item {
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-md);
  color: var(--color-text);
  text-decoration: none;
  font-size: var(--text-sm);
}
.nav-list__item:hover { background: var(--color-bg); }
.nav-list__item.is-active {
  background: var(--color-primary-soft);
  color: var(--color-primary);
  font-weight: 600;
}
.nav-list__group {
  margin-top: var(--space-4); margin-bottom: var(--space-1);
  font-size: var(--text-xs); text-transform: uppercase;
  letter-spacing: .04em; color: var(--color-text-muted);
}

/* ===== Ô số liệu ===== */
.stat {
  padding: var(--space-4);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
}
.stat__label { font-size: var(--text-sm); color: var(--color-text-muted); }
.stat__value { font-size: var(--text-3xl); font-weight: 700; line-height: 1.1; margin: var(--space-1) 0; }
.stat__hint { font-size: var(--text-xs); color: var(--color-text-muted); }
```

- [ ] **Step 2: Viết `student/dashboard.html`**

```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Trang chủ — TOEIC Practice</title>
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="loading,empty,error,success">
  <div class="app-shell app-shell--with-sidebar">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <div class="row">
        <span class="badge badge--info">Còn 3 lượt thi</span>
        <a class="btn btn--sm" href="../payment/pricing.html">Mua thêm lượt</a>
        <a class="btn btn--ghost btn--sm" href="profile.html">Nguyễn Văn A</a>
      </div>
    </header>
    <div class="app-body">
      <aside class="app-sidebar">
        <nav class="nav-list" aria-label="Điều hướng chính">
          <a class="nav-list__item is-active" href="dashboard.html">Trang chủ</a>
          <div class="nav-list__group">Thi thử</div>
          <a class="nav-list__item" href="exam-list.html">Danh sách đề</a>
          <a class="nav-list__item" href="exam-history.html">Lịch sử thi</a>
          <div class="nav-list__group">Luyện tập</div>
          <a class="nav-list__item" href="practice-select.html">Luyện theo part</a>
          <div class="nav-list__group">Tài khoản</div>
          <a class="nav-list__item" href="../payment/wallet.html">Ví lượt thi</a>
          <a class="nav-list__item" href="profile.html">Hồ sơ</a>
        </nav>
      </aside>
      <main class="app-main">
        <div class="container stack">

          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="grid-3">
              <div class="skeleton skeleton--block"></div>
              <div class="skeleton skeleton--block"></div>
              <div class="skeleton skeleton--block"></div>
            </div>
            <div class="skeleton skeleton--block" style="height: 200px"></div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Không tải được dữ liệu</div>
                <p class="state-block__desc">Kiểm tra kết nối rồi thử lại.</p>
                <button class="btn btn--primary">Thử lại</button>
              </div>
            </div></div>
          </div>

          <!-- ===== empty: chưa thi lần nào ===== -->
          <div class="only-empty stack">
            <h1>Xin chào, Nguyễn Văn A</h1>
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">□</div>
                <div class="state-block__title">Bạn chưa làm bài thi nào</div>
                <p class="state-block__desc">
                  Làm một đề đầy đủ để biết trình độ hiện tại, hoặc luyện riêng từng part trước.
                </p>
                <div class="row" style="justify-content: center">
                  <a class="btn btn--primary btn--lg" href="exam-list.html">Bắt đầu thi thử</a>
                  <a class="btn btn--lg" href="practice-select.html">Luyện theo part</a>
                </div>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <div class="row row--between">
              <h1 style="margin: 0">Xin chào, Nguyễn Văn A</h1>
              <a class="btn btn--primary" href="exam-list.html">Thi thử ngay</a>
            </div>

            <div class="grid-3">
              <div class="stat">
                <div class="stat__label">Điểm gần nhất</div>
                <div class="stat__value">720</div>
                <div class="stat__hint">L 385 · R 335 — 18/09/2026</div>
              </div>
              <div class="stat">
                <div class="stat__label">Điểm cao nhất</div>
                <div class="stat__value">745</div>
                <div class="stat__hint">Đạt ngày 02/09/2026</div>
              </div>
              <div class="stat">
                <div class="stat__label">Đã hoàn thành</div>
                <div class="stat__value">6</div>
                <div class="stat__hint">bài thi đầy đủ · 24 phiên luyện</div>
              </div>
            </div>

            <div class="card">
              <div class="card__header"><strong>Độ chính xác theo part</strong></div>
              <div class="card__body stack">
                <div>
                  <div class="row row--between text-sm">
                    <span>Part 1 — Ảnh</span><span class="text-muted">83%</span>
                  </div>
                  <div class="progress"><div class="progress__bar" style="width: 83%"></div></div>
                </div>
                <div>
                  <div class="row row--between text-sm">
                    <span>Part 2 — Hỏi đáp</span><span class="text-muted">76%</span>
                  </div>
                  <div class="progress"><div class="progress__bar" style="width: 76%"></div></div>
                </div>
                <div>
                  <div class="row row--between text-sm">
                    <span>Part 5 — Câu đơn</span><span class="text-muted">71%</span>
                  </div>
                  <div class="progress"><div class="progress__bar" style="width: 71%"></div></div>
                </div>
                <div>
                  <div class="row row--between text-sm">
                    <span>Part 7 — Đoạn văn</span><span class="text-muted">58%</span>
                  </div>
                  <div class="progress"><div class="progress__bar" style="width: 58%"></div></div>
                </div>
                <p class="text-sm text-muted" style="margin: 0">
                  Part 7 đang là điểm yếu. <a href="practice-select.html">Luyện Part 7</a>
                </p>
              </div>
            </div>

            <div class="card">
              <div class="card__header"><strong>Bài thi gần đây</strong></div>
              <div class="card__body">
                <table class="table table--striped">
                  <thead>
                    <tr><th>Đề</th><th>Ngày</th><th>Listening</th><th>Reading</th><th>Tổng</th><th></th></tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td>ETS 2024 — Test 3</td><td>18/09/2026</td>
                      <td>385</td><td>335</td><td><strong>720</strong></td>
                      <td><a href="exam-review.html">Xem lại</a></td>
                    </tr>
                    <tr>
                      <td>ETS 2024 — Test 2</td><td>10/09/2026</td>
                      <td>370</td><td>320</td><td><strong>690</strong></td>
                      <td><a href="exam-review.html">Xem lại</a></td>
                    </tr>
                    <tr>
                      <td>ETS 2024 — Test 1</td><td>02/09/2026</td>
                      <td>395</td><td>350</td><td><strong>745</strong></td>
                      <td><a href="exam-review.html">Xem lại</a></td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <div class="card__footer">
                <a class="btn btn--sm" href="exam-history.html">Xem tất cả</a>
              </div>
            </div>
          </div>

        </div>
      </main>
    </div>
  </div>
  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 3: Verify**

Mở `http://localhost:8080/student/dashboard.html`. Kiểm:
- Sidebar 240px bên trái, mục "Trang chủ" có nền xanh nhạt (`.is-active`).
- `?state=loading`: chỉ thấy skeleton, **không** thấy số 720.
- `?state=empty`: thấy khối "Bạn chưa làm bài thi nào", không có bảng số liệu.
- `?state=error`: chỉ thấy khối lỗi + nút thử lại.
- `?state=success`: 3 ô số liệu, 4 thanh progress, bảng 3 dòng.
- Thu cửa sổ về 1280px: không có scroll ngang.
- Console không lỗi.

- [ ] **Step 4: Commit**

```bash
git add prototype/student/dashboard.html prototype/assets/css/components.css
git commit -m "feat: add student shell and dashboard"
```

---

### Task 5: Danh sách đề + Paywall + Hướng dẫn trước thi

Hai trang này là cửa vào phòng thi. `paywall` là state riêng chứ không phải thông báo lỗi — hết lượt thì nút "Bắt đầu thi" đổi hình dạng.

**Files:**
- Create: `prototype/student/exam-list.html`
- Create: `prototype/student/exam-instructions.html`

**Interfaces:**
- Consumes: shell học viên từ Task 4 (copy nguyên `.app-topbar` + `.app-sidebar`, đổi `.is-active` sang mục tương ứng), component Task 2
- Produces: pattern thẻ đề thi `.exam-card` (dùng lại ở `admin/exam-list.html` Task 11) — không cần CSS mới, lắp bằng `.card` + `.grid-2`

State `exam-list.html`: `loading`, `empty`, `error`, `success`, `paywall`.
State `exam-instructions.html`: `success`, `paywall`, `submitting`.

- [ ] **Step 1: Viết `student/exam-list.html`**

Copy shell từ `dashboard.html`, đổi `<title>` thành "Danh sách đề thi", đổi `.is-active` sang mục `exam-list.html`. Phần `.container.stack` thay bằng:

```html
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="grid-2">
              <div class="skeleton skeleton--block" style="height: 150px"></div>
              <div class="skeleton skeleton--block" style="height: 150px"></div>
              <div class="skeleton skeleton--block" style="height: 150px"></div>
              <div class="skeleton skeleton--block" style="height: 150px"></div>
            </div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Không tải được danh sách đề</div>
                <p class="state-block__desc">Kiểm tra kết nối rồi thử lại.</p>
                <button class="btn btn--primary">Thử lại</button>
              </div>
            </div></div>
          </div>

          <!-- ===== empty ===== -->
          <div class="only-empty stack">
            <h1>Danh sách đề thi</h1>
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">□</div>
                <div class="state-block__title">Chưa có đề thi nào</div>
                <p class="state-block__desc">Đề mới sẽ xuất hiện ở đây khi được phát hành.</p>
                <a class="btn" href="practice-select.html">Luyện theo part</a>
              </div>
            </div></div>
          </div>

          <!-- ===== paywall: hết lượt ===== -->
          <div class="only-paywall stack">
            <h1>Danh sách đề thi</h1>
            <div class="alert alert--warning">
              <strong>Bạn đã hết lượt thi.</strong>
              Mua thêm lượt để làm đề đầy đủ. Luyện theo part vẫn miễn phí.
              <div class="row" style="margin-top: var(--space-3)">
                <a class="btn btn--primary btn--sm" href="../payment/pricing.html">Xem bảng giá</a>
                <a class="btn btn--sm" href="practice-select.html">Luyện miễn phí</a>
              </div>
            </div>
            <div class="grid-2">
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>ETS 2024 — Test 4</strong>
                    <span class="badge badge--muted">Cần 1 lượt</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    200 câu · 120 phút · 7 parts
                  </p>
                  <button class="btn btn--primary" disabled>Bắt đầu thi</button>
                </div>
              </div>
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>ETS 2024 — Test 5</strong>
                    <span class="badge badge--muted">Cần 1 lượt</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    200 câu · 120 phút · 7 parts
                  </p>
                  <button class="btn btn--primary" disabled>Bắt đầu thi</button>
                </div>
              </div>
            </div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <div class="row row--between">
              <h1 style="margin: 0">Danh sách đề thi</h1>
              <span class="badge badge--info">Còn 3 lượt thi</span>
            </div>

            <div class="card">
              <div class="card__body row" style="gap: var(--space-4); flex-wrap: wrap">
                <div class="field" style="flex: 1 1 220px">
                  <label class="field__label" for="filter-keyword">Tìm đề</label>
                  <input class="field__input" id="filter-keyword" type="search"
                         placeholder="Tên đề, bộ đề…">
                </div>
                <div class="field" style="flex: 0 0 180px">
                  <label class="field__label" for="filter-status">Trạng thái</label>
                  <select class="field__input" id="filter-status">
                    <option>Tất cả</option>
                    <option>Chưa làm</option>
                    <option>Đã làm</option>
                    <option>Đang làm dở</option>
                  </select>
                </div>
              </div>
            </div>

            <div class="grid-2">
              <!-- đề đang làm dở -->
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>ETS 2024 — Test 4</strong>
                    <span class="badge badge--warning">Đang làm dở</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    200 câu · 120 phút · còn 47 phút · đã làm 128/200
                  </p>
                  <div class="progress"><div class="progress__bar" style="width: 64%"></div></div>
                  <div class="row">
                    <a class="btn btn--primary" href="exam-reading.html?state=resumed">Tiếp tục</a>
                    <button class="btn btn--ghost btn--sm">Huỷ bài</button>
                  </div>
                </div>
              </div>

              <!-- đề chưa làm -->
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>ETS 2024 — Test 5</strong>
                    <span class="badge badge--muted">Chưa làm</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    200 câu · 120 phút · 7 parts
                  </p>
                  <a class="btn btn--primary" href="exam-instructions.html">Bắt đầu thi</a>
                </div>
              </div>

              <!-- đề đã làm -->
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>ETS 2024 — Test 3</strong>
                    <span class="badge badge--success">Đã làm · 720</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    Hoàn thành 18/09/2026 · L 385 · R 335
                  </p>
                  <div class="row">
                    <a class="btn" href="exam-review.html">Xem lại</a>
                    <a class="btn btn--primary" href="exam-instructions.html">Làm lại</a>
                  </div>
                </div>
              </div>

              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>ETS 2024 — Test 2</strong>
                    <span class="badge badge--success">Đã làm · 690</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    Hoàn thành 10/09/2026 · L 370 · R 320
                  </p>
                  <div class="row">
                    <a class="btn" href="exam-review.html">Xem lại</a>
                    <a class="btn btn--primary" href="exam-instructions.html">Làm lại</a>
                  </div>
                </div>
              </div>
            </div>
          </div>
```

- [ ] **Step 2: Viết `student/exam-instructions.html`**

Trang này **không có sidebar** — vào chế độ thi là bỏ hết điều hướng phụ để tránh bấm nhầm ra ngoài. Dùng `.app-shell` thường + `.container` hẹp.

```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Hướng dẫn trước khi thi — TOEIC Practice</title>
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="success,paywall,submitting">
  <div class="app-shell">
    <header class="app-topbar">
      <strong>ETS 2024 — Test 5</strong>
      <a class="btn btn--ghost btn--sm" href="exam-list.html">Thoát</a>
    </header>
    <main class="app-main">
      <div class="container" style="max-width: 760px">

        <!-- ===== paywall ===== -->
        <div class="only-paywall">
          <div class="card"><div class="card__body">
            <div class="state-block">
              <div class="state-block__icon" aria-hidden="true">🔒</div>
              <div class="state-block__title">Bạn đã hết lượt thi</div>
              <p class="state-block__desc">
                Mỗi bài thi đầy đủ dùng 1 lượt. Mua thêm để tiếp tục.
              </p>
              <div class="row" style="justify-content: center">
                <a class="btn btn--primary btn--lg" href="../payment/pricing.html">Xem bảng giá</a>
                <a class="btn btn--lg" href="practice-select.html">Luyện miễn phí</a>
              </div>
            </div>
          </div></div>
        </div>

        <!-- ===== success + submitting ===== -->
        <div class="only-success stack">
          <h1>Trước khi bắt đầu</h1>

          <div class="card">
            <div class="card__header"><strong>Cấu trúc bài thi</strong></div>
            <div class="card__body">
              <table class="table">
                <thead>
                  <tr><th>Phần</th><th>Part</th><th>Số câu</th><th>Thời gian</th></tr>
                </thead>
                <tbody>
                  <tr><td rowspan="4">Listening</td><td>Part 1 — Ảnh</td><td>6</td><td rowspan="4">45 phút</td></tr>
                  <tr><td>Part 2 — Hỏi đáp</td><td>25</td></tr>
                  <tr><td>Part 3 — Hội thoại</td><td>39</td></tr>
                  <tr><td>Part 4 — Bài nói</td><td>30</td></tr>
                  <tr><td rowspan="3">Reading</td><td>Part 5 — Câu đơn</td><td>30</td><td rowspan="3">75 phút</td></tr>
                  <tr><td>Part 6 — Điền đoạn</td><td>16</td></tr>
                  <tr><td>Part 7 — Đọc hiểu</td><td>54</td></tr>
                </tbody>
                <tfoot>
                  <tr><th>Tổng</th><th></th><th>200</th><th>120 phút</th></tr>
                </tfoot>
              </table>
            </div>
          </div>

          <div class="alert alert--warning">
            <strong>Phần Listening chỉ phát audio một lần.</strong>
            Không tua lại, không tạm dừng, và không quay lại Listening sau khi đã sang Reading.
          </div>

          <div class="card">
            <div class="card__header"><strong>Lưu ý</strong></div>
            <div class="card__body">
              <ul class="stack" style="margin: 0; padding-left: var(--space-5)">
                <li>Đáp án được lưu tự động sau mỗi lần chọn. Đóng tab vẫn tiếp tục được.</li>
                <li>Hết giờ, bài tự nộp và chấm trên những câu đã làm.</li>
                <li>Bài thi này dùng <strong>1 lượt</strong>. Bạn còn 3 lượt.</li>
                <li>Chuẩn bị tai nghe và kiểm tra âm lượng trước khi bắt đầu.</li>
              </ul>
            </div>
          </div>

          <div class="card">
            <div class="card__body row" style="gap: var(--space-4)">
              <button class="btn" type="button">Phát audio thử</button>
              <span class="text-sm text-muted">Nghe được đoạn mẫu nghĩa là thiết bị đã sẵn sàng.</span>
            </div>
          </div>

          <label class="row">
            <input type="checkbox" id="confirm-ready">
            <span class="text-sm">Tôi đã đọc hướng dẫn và sẵn sàng bắt đầu</span>
          </label>

          <div class="row">
            <a class="btn btn--primary btn--lg" href="exam-listening.html">Bắt đầu thi — dùng 1 lượt</a>
            <a class="btn btn--lg" href="exam-list.html">Để sau</a>
          </div>
        </div>

        <div class="only-submitting">
          <div class="card"><div class="card__body">
            <div class="state-block">
              <div class="state-block__icon" aria-hidden="true">⏳</div>
              <div class="state-block__title">Đang khởi tạo bài thi…</div>
              <p class="state-block__desc">Đang trừ 1 lượt và tải đề. Đừng tắt trang.</p>
            </div>
          </div></div>
        </div>

      </div>
    </main>
  </div>
  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 3: Verify**

Kiểm `exam-list.html`:
- `?state=paywall`: alert vàng + 2 thẻ đề có nút **disabled** (mờ, không bấm được), badge "Cần 1 lượt".
- `?state=success`: 4 thẻ, thẻ đầu có badge "Đang làm dở" + progress bar + nút "Tiếp tục".
- `?state=empty` / `?state=error` / `?state=loading`: đúng một khối hiện.

Kiểm `exam-instructions.html`:
- **Không có sidebar** — khác rõ so với dashboard.
- `?state=success`: bảng cấu trúc có `rowspan` gộp đúng (Listening gộp 4 dòng, Reading gộp 3), tổng 200 câu / 120 phút.
- `?state=paywall`: chỉ thấy khối khoá, không thấy bảng cấu trúc.
- `?state=submitting`: chỉ thấy "Đang khởi tạo bài thi…".
- Console không lỗi.

- [ ] **Step 4: Commit**

```bash
git add prototype/student/exam-list.html prototype/student/exam-instructions.html
git commit -m "feat: add exam list with paywall and pre-exam instructions"
```

---

### Task 6: Màn thi Listening

Màn quan trọng nhất của prototype. Ba ràng buộc phải thấy được trên UI: audio **không tua được**, **không quay lại part trước**, và **không có đáp án đúng trong HTML**.

**Files:**
- Create: `prototype/student/exam-listening.html`
- Modify: `prototype/assets/css/components.css` (thêm `.exam-layout`, `.exam-sidebar`, `.save-status`)

**Interfaces:**
- Consumes: component Task 2 (`.option-list` `.timer` `.navigator` `.exam-audio` `.alert`)
- Produces:
  - `.exam-layout` — grid 2 cột: nội dung câu hỏi + panel điều hướng bên phải (300px)
  - `.exam-sidebar` — cột phải sticky
  - `.save-status` / `.save-status--saving` / `.save-status--saved` / `.save-status--error` — chỉ báo lưu đáp án
  - Markup màn thi mà Task 7 (Reading) và Task 9 (Review) tham chiếu

State: `loading`, `success`, `resumed`, `expired`, `submitting`, `offline`.

- [ ] **Step 1: Thêm CSS màn thi vào `components.css`**

```css
/* ===== Layout màn thi ===== */
.exam-layout {
  display: grid;
  grid-template-columns: 1fr 300px;
  gap: var(--space-5);
  align-items: start;
}
.exam-sidebar { position: sticky; top: var(--space-4); }
.exam-topbar-group { display: flex; align-items: center; gap: var(--space-4); }

/* ===== Chỉ báo lưu đáp án ===== */
.save-status { font-size: var(--text-xs); color: var(--color-text-muted); }
.save-status--saving { color: var(--color-text-muted); }
.save-status--saved  { color: var(--color-success); }
.save-status--error  { color: var(--color-danger); font-weight: 600; }

/* ===== Khối câu hỏi ===== */
.question {
  padding: var(--space-5);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
}
.question__meta {
  display: flex; align-items: center; gap: var(--space-3);
  margin-bottom: var(--space-3);
  font-size: var(--text-sm); color: var(--color-text-muted);
}
.question__number {
  font-weight: 700; font-size: var(--text-lg); color: var(--color-text);
}
.question__stem { margin-bottom: var(--space-4); }
.question__image {
  display: block; width: 100%; max-width: 560px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  margin-bottom: var(--space-4);
}
.passage {
  padding: var(--space-4);
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  font-size: var(--text-sm);
  max-height: 460px; overflow-y: auto;
}
```

- [ ] **Step 2: Tạo ảnh placeholder cho Part 1**

Tạo `prototype/assets/img/part1-sample.svg`:

```html
<svg xmlns="http://www.w3.org/2000/svg" width="560" height="360" viewBox="0 0 560 360" role="img" aria-label="Anh minh hoa Part 1">
  <rect width="560" height="360" fill="#e9ecf0"/>
  <rect x="40" y="220" width="480" height="100" fill="#cfd5dc"/>
  <circle cx="160" cy="150" r="52" fill="#b9c1ca"/>
  <rect x="300" y="98" width="180" height="104" fill="#b9c1ca"/>
  <text x="280" y="348" font-family="sans-serif" font-size="14" fill="#6b7480" text-anchor="middle">Anh Part 1 (placeholder)</text>
</svg>
```

- [ ] **Step 3: Viết `student/exam-listening.html` — khung + topbar + các state chặn**

```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Listening — ETS 2024 Test 5</title>
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="loading,success,resumed,expired,submitting,offline">
  <div class="app-shell">
    <header class="app-topbar">
      <div class="exam-topbar-group">
        <strong>ETS 2024 — Test 5</strong>
        <span class="badge badge--info">Listening · Part 2</span>
      </div>
      <div class="exam-topbar-group">
        <span class="save-status save-status--saved only-success">Đã lưu</span>
        <span class="save-status save-status--error only-offline">Mất kết nối — đang thử lại…</span>
        <span class="timer only-success">
          <span class="text-sm text-muted">Còn lại</span>
          <span class="timer__value">00:38:12</span>
        </span>
        <span class="timer timer--warning only-resumed">
          <span class="text-sm">Còn lại</span>
          <span class="timer__value">00:09:44</span>
        </span>
        <span class="timer timer--critical only-expired">
          <span class="text-sm">Hết giờ</span>
          <span class="timer__value">00:00:00</span>
        </span>
      </div>
    </header>
    <main class="app-main">

      <!-- ===== loading ===== -->
      <div class="container only-loading">
        <div class="exam-layout">
          <div class="stack">
            <div class="skeleton skeleton--block" style="height: 90px"></div>
            <div class="skeleton skeleton--title"></div>
            <div class="skeleton skeleton--text"></div>
            <div class="skeleton skeleton--text" style="width: 70%"></div>
            <div class="skeleton skeleton--block" style="height: 200px"></div>
          </div>
          <div class="skeleton skeleton--block" style="height: 320px"></div>
        </div>
      </div>

      <!-- ===== expired: hết giờ, đang tự nộp ===== -->
      <div class="container only-expired" style="max-width: 560px">
        <div class="card"><div class="card__body">
          <div class="state-block">
            <div class="state-block__icon" aria-hidden="true">⏱</div>
            <div class="state-block__title">Đã hết thời gian</div>
            <p class="state-block__desc">
              Bài thi đang được nộp tự động và chấm trên những câu bạn đã làm.
              Không cần làm gì thêm.
            </p>
            <div class="progress" style="max-width: 280px; margin: 0 auto">
              <div class="progress__bar" style="width: 70%"></div>
            </div>
          </div>
        </div></div>
      </div>

      <!-- ===== submitting ===== -->
      <div class="container only-submitting" style="max-width: 560px">
        <div class="card"><div class="card__body">
          <div class="state-block">
            <div class="state-block__icon" aria-hidden="true">⏳</div>
            <div class="state-block__title">Đang nộp bài…</div>
            <p class="state-block__desc">Đừng tắt trang. Việc này mất vài giây.</p>
          </div>
        </div></div>
      </div>

      <!-- CHUNK-6B -->

    </main>
  </div>
  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 4: Thay `<!-- CHUNK-6B -->` bằng thân màn thi (state `success` / `resumed` / `offline`)**

Ba state này dùng chung thân màn, khác nhau ở banner phía trên. Viết một block `.exam-body` hiện ở cả ba, cộng hai banner riêng.

Cơ chế `.only-<state>` của Task 1 chỉ cho một state mỗi element, nên trước khi viết markup hãy thêm vào cuối `base.css` một cơ chế cho element hiện ở **nhiều** state:

```css
/* element hiện ở nhiều state: đặt .multi-state rồi liệt kê trong data-show */
.multi-state { display: none; }
body[data-state="success"]    .multi-state[data-show~="success"],
body[data-state="resumed"]    .multi-state[data-show~="resumed"],
body[data-state="offline"]    .multi-state[data-show~="offline"],
body[data-state="expired"]    .multi-state[data-show~="expired"],
body[data-state="submitting"] .multi-state[data-show~="submitting"] { display: revert; }
```

```html
      <!-- banner riêng cho resumed / offline -->
      <div class="container only-resumed" style="margin-bottom: var(--space-4)">
        <div class="alert alert--info">
          <strong>Đã khôi phục bài đang làm.</strong>
          Bạn quay lại đúng câu 8, thời gian còn lại được tính từ lúc bắt đầu.
        </div>
      </div>
      <div class="container only-offline" style="margin-bottom: var(--space-4)">
        <div class="alert alert--danger" role="alert">
          <strong>Mất kết nối.</strong>
          Đáp án đang được giữ tạm trên máy và sẽ tự gửi lại khi có mạng.
          Đừng đóng tab.
        </div>
      </div>

      <div class="container exam-body multi-state" data-show="success resumed offline">
        <div class="exam-layout">

          <!-- cột nội dung -->
          <div class="stack">
            <!-- audio: không tua, không pause -->
            <div class="exam-audio">
              <span aria-hidden="true">▶</span>
              <div style="flex: 1 1 auto">
                <div class="progress"><div class="progress__bar" style="width: 42%"></div></div>
                <p class="exam-audio__status" style="margin: var(--space-2) 0 0">
                  Đang phát Part 2 — 03:18 / 07:50 ·
                  <strong>không tua lại, không tạm dừng</strong>
                </p>
              </div>
              <div class="row">
                <label class="text-sm text-muted" for="volume">Âm lượng</label>
                <input id="volume" type="range" min="0" max="100" value="70">
              </div>
            </div>

            <!-- câu Part 2: chỉ 3 option, không có đề bài in -->
            <div class="question">
              <div class="question__meta">
                <span class="question__number">Câu 8</span>
                <span class="badge badge--muted">Part 2 — Hỏi đáp</span>
                <span>Nghe câu hỏi và chọn câu trả lời phù hợp nhất.</span>
              </div>
              <p class="question__stem text-muted">
                <em>Part 2 không in đề bài. Chỉ chọn A, B hoặc C sau khi nghe.</em>
              </p>
              <div class="option-list" role="radiogroup" aria-label="Đáp án câu 8">
                <label class="option option--selected">
                  <span class="option__marker">A</span>
                  <span class="option__text">Lựa chọn A</span>
                  <input type="radio" name="q8" checked class="only-loading">
                </label>
                <label class="option">
                  <span class="option__marker">B</span>
                  <span class="option__text">Lựa chọn B</span>
                </label>
                <label class="option">
                  <span class="option__marker">C</span>
                  <span class="option__text">Lựa chọn C</span>
                </label>
              </div>
            </div>

            <!-- câu Part 1 để minh hoạ dạng có ảnh + 4 option -->
            <div class="question">
              <div class="question__meta">
                <span class="question__number">Câu 3</span>
                <span class="badge badge--muted">Part 1 — Ảnh</span>
                <span>Chọn câu miêu tả ảnh đúng nhất.</span>
              </div>
              <img class="question__image" src="../assets/img/part1-sample.svg"
                   alt="Ảnh minh hoạ câu 3: người đứng cạnh máy in trong văn phòng">
              <div class="option-list" role="radiogroup" aria-label="Đáp án câu 3">
                <label class="option">
                  <span class="option__marker">A</span>
                  <span class="option__text">Lựa chọn A</span>
                </label>
                <label class="option">
                  <span class="option__marker">B</span>
                  <span class="option__text">Lựa chọn B</span>
                </label>
                <label class="option">
                  <span class="option__marker">C</span>
                  <span class="option__text">Lựa chọn C</span>
                </label>
                <label class="option">
                  <span class="option__marker">D</span>
                  <span class="option__text">Lựa chọn D</span>
                </label>
              </div>
            </div>

            <div class="row row--between">
              <button class="btn" disabled
                      title="Listening không cho quay lại part trước">
                ← Part trước
              </button>
              <div class="row">
                <button class="btn btn--ghost">Đánh dấu xem lại</button>
                <button class="btn btn--primary">Câu tiếp →</button>
              </div>
            </div>
            <p class="text-xs text-muted" style="margin: 0">
              Part đã qua sẽ bị khoá. Audio mỗi part chỉ phát một lần.
            </p>
          </div>

          <!-- cột điều hướng -->
          <aside class="exam-sidebar stack">
            <div class="card">
              <div class="card__header">
                <div class="row row--between">
                  <strong class="text-sm">Listening</strong>
                  <span class="text-xs text-muted">31/100 câu</span>
                </div>
              </div>
              <div class="card__body stack">
                <div class="progress"><div class="progress__bar" style="width: 31%"></div></div>
                <div class="stepper" style="flex-direction: column">
                  <span class="stepper__item is-done">Part 1 — 6 câu · đã khoá</span>
                  <span class="stepper__item is-current">Part 2 — 25 câu · đang làm</span>
                  <span class="stepper__item">Part 3 — 39 câu</span>
                  <span class="stepper__item">Part 4 — 30 câu</span>
                </div>
              </div>
            </div>

            <div class="card">
              <div class="card__header"><strong class="text-sm">Câu 1–31</strong></div>
              <div class="card__body stack">
                <div class="navigator__grid">
                  <span class="navigator__cell is-answered">1</span>
                  <span class="navigator__cell is-answered">2</span>
                  <span class="navigator__cell is-answered">3</span>
                  <span class="navigator__cell is-marked">4</span>
                  <span class="navigator__cell is-answered">5</span>
                  <span class="navigator__cell is-answered">6</span>
                  <span class="navigator__cell is-answered">7</span>
                  <span class="navigator__cell is-current is-answered">8</span>
                  <span class="navigator__cell is-unanswered">9</span>
                  <span class="navigator__cell is-unanswered">10</span>
                </div>
                <p class="text-xs text-muted" style="margin: 0">
                  Panel đầy đủ 200 câu xem ở
                  <a href="question-navigator.html">question-navigator.html</a>
                </p>
                <div class="text-xs stack" style="gap: var(--space-1)">
                  <div class="row"><span class="navigator__cell is-answered" style="width: 18px">1</span> đã trả lời</div>
                  <div class="row"><span class="navigator__cell is-marked" style="width: 18px">2</span> đánh dấu xem lại</div>
                  <div class="row"><span class="navigator__cell is-unanswered" style="width: 18px">3</span> chưa trả lời</div>
                </div>
              </div>
            </div>

            <a class="btn btn--danger" href="exam-confirm-submit.html">Nộp bài sớm</a>
          </aside>

        </div>
      </div>
```

- [ ] **Step 5: Verify**

Mở `http://localhost:8080/student/exam-listening.html`. Kiểm:
- `?state=success`: thấy audio bar, câu 8 (Part 2) có **đúng 3** option, câu 3 (Part 1) có **4** option và ảnh SVG hiện.
- Nút "← Part trước" **disabled**; hover thấy tooltip giải thích.
- Audio bar không có nút tua/pause, có dòng chữ "không tua lại, không tạm dừng".
- **Xem source (Ctrl+U) hoặc Ctrl+F trong DevTools: không có class `option--correct`, không có chữ "Đáp án đúng", không có "giải thích"** ở bất kỳ đâu trong trang.
- `?state=resumed`: banner xanh "Đã khôi phục bài đang làm" + timer vàng, thân màn vẫn hiện.
- `?state=offline`: banner đỏ + chỉ báo "Mất kết nối" ở topbar, thân màn vẫn hiện.
- `?state=expired`: **chỉ** thấy khối "Đã hết thời gian", không thấy câu hỏi.
- `?state=submitting`: chỉ thấy "Đang nộp bài…".
- Cột phải sticky: cuộn trang thì panel điều hướng dính lại.
- Console không lỗi.

- [ ] **Step 6: Commit**

```bash
git add prototype/student/exam-listening.html prototype/assets/img/part1-sample.svg \
        prototype/assets/css/components.css prototype/assets/css/base.css
git commit -m "feat: add listening exam screen with locked audio and multi-state body"
```

---

### Task 7: Màn thi Reading + Panel điều hướng 200 câu

Reading ngược với Listening: **không audio**, di chuyển tự do giữa mọi câu, có đoạn văn cần đọc song song với câu hỏi.

**Files:**
- Create: `prototype/student/exam-reading.html`
- Create: `prototype/student/question-navigator.html`

**Interfaces:**
- Consumes: `.exam-layout` `.exam-sidebar` `.save-status` `.question` `.passage` `.multi-state` từ Task 6
- Produces: panel 200 câu đầy đủ mà `exam-confirm-submit.html` (Task 8) tham chiếu

State `exam-reading.html`: `loading`, `success`, `resumed`, `expired`, `submitting`, `offline`.
State `question-navigator.html`: chỉ `success` (đây là trang trưng bày component).

- [ ] **Step 1: Viết `student/exam-reading.html`**

Copy nguyên khung + topbar + 3 state chặn (`loading`, `expired`, `submitting`) từ `exam-listening.html`. Đổi:
- `<title>` thành "Reading — ETS 2024 Test 5"
- badge topbar thành `<span class="badge badge--info">Reading · Part 7</span>`
- `timer__value` của state `success` thành `01:02:47`

Thân màn (`.exam-body`) thay bằng:

```html
      <div class="container exam-body multi-state" data-show="success resumed offline">
        <div class="exam-layout">

          <div class="stack">
            <!-- Part 7: đoạn văn + câu hỏi cạnh nhau -->
            <div class="question">
              <div class="question__meta">
                <span class="badge badge--muted">Part 7 — Đọc hiểu</span>
                <span>Đọc email sau và trả lời câu 153–155.</span>
              </div>

              <div class="passage">
                <p><strong>From:</strong> m.torres@brightpath.com<br>
                   <strong>To:</strong> all-staff@brightpath.com<br>
                   <strong>Date:</strong> October 4<br>
                   <strong>Subject:</strong> Office relocation schedule</p>
                <p>Dear colleagues,</p>
                <p>As announced last month, our headquarters will move to the
                   Fairview Business Centre on November 15. Packing materials will be
                   delivered to each floor on November 11. Please label every box with
                   your department code before leaving on November 14.</p>
                <p>The IT team will disconnect all workstations at 5 P.M. on November 14
                   and reconnect them by 8 A.M. on November 17. If you need access to
                   your computer during that period, please contact the help desk before
                   November 10 so that a temporary laptop can be arranged.</p>
                <p>Thank you for your cooperation,<br>Marisol Torres, Operations Manager</p>
              </div>
            </div>

            <div class="question">
              <div class="question__meta">
                <span class="question__number">Câu 153</span>
                <span class="badge badge--muted">Part 7</span>
              </div>
              <p class="question__stem">What is the purpose of the email?</p>
              <div class="option-list" role="radiogroup" aria-label="Đáp án câu 153">
                <label class="option option--selected">
                  <span class="option__marker">A</span>
                  <span class="option__text">To explain the steps of an office move</span>
                </label>
                <label class="option">
                  <span class="option__marker">B</span>
                  <span class="option__text">To announce a new operations manager</span>
                </label>
                <label class="option">
                  <span class="option__marker">C</span>
                  <span class="option__text">To request feedback on a floor plan</span>
                </label>
                <label class="option">
                  <span class="option__marker">D</span>
                  <span class="option__text">To confirm a delivery of new computers</span>
                </label>
              </div>
            </div>

            <div class="question">
              <div class="question__meta">
                <span class="question__number">Câu 154</span>
                <span class="badge badge--muted">Part 7</span>
                <span class="badge badge--warning">Đã đánh dấu</span>
              </div>
              <p class="question__stem">By when should employees label their boxes?</p>
              <div class="option-list" role="radiogroup" aria-label="Đáp án câu 154">
                <label class="option">
                  <span class="option__marker">A</span>
                  <span class="option__text">November 10</span>
                </label>
                <label class="option">
                  <span class="option__marker">B</span>
                  <span class="option__text">November 11</span>
                </label>
                <label class="option">
                  <span class="option__marker">C</span>
                  <span class="option__text">November 14</span>
                </label>
                <label class="option">
                  <span class="option__marker">D</span>
                  <span class="option__text">November 17</span>
                </label>
              </div>
            </div>

            <!-- Reading: điều hướng tự do cả hai chiều -->
            <div class="row row--between">
              <button class="btn">← Câu trước</button>
              <div class="row">
                <button class="btn btn--ghost">Bỏ đánh dấu</button>
                <button class="btn btn--primary">Câu tiếp →</button>
              </div>
            </div>
            <p class="text-xs text-muted" style="margin: 0">
              Phần Reading cho phép quay lại sửa đáp án bất kỳ câu nào trong 75 phút.
            </p>
          </div>

          <aside class="exam-sidebar stack">
            <div class="card">
              <div class="card__header">
                <div class="row row--between">
                  <strong class="text-sm">Reading</strong>
                  <span class="text-xs text-muted">53/100 câu</span>
                </div>
              </div>
              <div class="card__body stack">
                <div class="progress"><div class="progress__bar" style="width: 53%"></div></div>
                <div class="stepper" style="flex-direction: column">
                  <span class="stepper__item is-done">Part 5 — 30 câu · xong</span>
                  <span class="stepper__item is-done">Part 6 — 16 câu · xong</span>
                  <span class="stepper__item is-current">Part 7 — 54 câu · đang làm</span>
                </div>
                <p class="text-xs text-muted" style="margin: 0">
                  Listening đã khoá — không quay lại được.
                </p>
              </div>
            </div>

            <div class="card">
              <div class="card__header"><strong class="text-sm">Câu 101–110</strong></div>
              <div class="card__body stack">
                <div class="navigator__grid">
                  <span class="navigator__cell is-answered">101</span>
                  <span class="navigator__cell is-answered">102</span>
                  <span class="navigator__cell is-marked">103</span>
                  <span class="navigator__cell is-answered">104</span>
                  <span class="navigator__cell is-answered">105</span>
                  <span class="navigator__cell is-unanswered">106</span>
                  <span class="navigator__cell is-answered">107</span>
                  <span class="navigator__cell is-answered">108</span>
                  <span class="navigator__cell is-unanswered">109</span>
                  <span class="navigator__cell is-answered">110</span>
                </div>
                <a class="btn btn--sm" href="question-navigator.html">Xem toàn bộ 200 câu</a>
              </div>
            </div>

            <a class="btn btn--primary" href="exam-confirm-submit.html">Nộp bài</a>
          </aside>

        </div>
      </div>
```

- [ ] **Step 2: Viết `student/question-navigator.html`**

Trang trưng bày panel đầy đủ. Không cần sinh tay 200 ô — dùng JS tại chỗ để sinh, vì đây là trang review component, không phải màn nghiệp vụ.

```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Panel điều hướng 200 câu — component</title>
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="success">
  <div class="app-shell">
    <header class="app-topbar">
      <strong>Panel điều hướng câu</strong>
      <a class="btn btn--ghost btn--sm" href="../index.html">Mục lục prototype</a>
    </header>
    <main class="app-main">
      <div class="container stack">
        <h1>Panel điều hướng 200 câu</h1>
        <p class="text-muted">
          Component dùng chung ở màn thi Listening, Reading và màn xác nhận nộp bài.
          Ba trạng thái câu phân biệt bằng cả màu và hình dạng, không chỉ màu.
        </p>

        <div class="card">
          <div class="card__header">
            <div class="row row--between">
              <strong>Toàn bài — 200 câu</strong>
              <span class="text-sm text-muted">
                Đã trả lời <strong>181</strong> · Đánh dấu <strong>7</strong> · Chưa làm <strong>19</strong>
              </span>
            </div>
          </div>
          <div class="card__body stack" id="navigator-root">
            <!-- JS chèn các part vào đây -->
          </div>
          <div class="card__footer">
            <div class="row text-xs">
              <span class="row"><span class="navigator__cell is-answered" style="width: 20px">1</span> đã trả lời</span>
              <span class="row"><span class="navigator__cell is-marked" style="width: 20px">2</span> đánh dấu xem lại</span>
              <span class="row"><span class="navigator__cell is-unanswered" style="width: 20px">3</span> chưa trả lời</span>
              <span class="row"><span class="navigator__cell is-current is-answered" style="width: 20px">4</span> câu hiện tại</span>
            </div>
          </div>
        </div>
      </div>
    </main>
  </div>

  <script>
    /* Sinh 200 ô theo cấu trúc TOEIC L&R để review component.
       Dữ liệu trạng thái là giả, cố định theo công thức để không nhảy mỗi lần load. */
    (function () {
      "use strict";
      var parts = [
        { name: "Part 1 — Ảnh",        from: 1,   to: 6   },
        { name: "Part 2 — Hỏi đáp",     from: 7,   to: 31  },
        { name: "Part 3 — Hội thoại",   from: 32,  to: 70  },
        { name: "Part 4 — Bài nói",     from: 71,  to: 100 },
        { name: "Part 5 — Câu đơn",     from: 101, to: 130 },
        { name: "Part 6 — Điền đoạn",   from: 131, to: 146 },
        { name: "Part 7 — Đọc hiểu",    from: 147, to: 200 }
      ];
      var CURRENT = 153;
      var root = document.getElementById("navigator-root");

      parts.forEach(function (part) {
        var wrap = document.createElement("div");

        var head = document.createElement("div");
        head.className = "row row--between text-sm";
        head.style.marginBottom = "var(--space-2)";
        head.innerHTML = "<span>" + part.name + "</span>" +
          "<span class='text-muted'>câu " + part.from + "–" + part.to + "</span>";
        wrap.appendChild(head);

        var grid = document.createElement("div");
        grid.className = "navigator__grid";

        for (var n = part.from; n <= part.to; n++) {
          var cell = document.createElement("a");
          cell.className = "navigator__cell";
          cell.href = "#";
          cell.textContent = String(n);
          cell.setAttribute("aria-label", "Câu " + n);

          if (n % 17 === 0)      { cell.classList.add("is-marked"); }
          else if (n % 11 === 0) { cell.classList.add("is-unanswered"); }
          else                   { cell.classList.add("is-answered"); }

          if (n === CURRENT) {
            cell.classList.add("is-current");
            cell.setAttribute("aria-current", "true");
          }
          grid.appendChild(cell);
        }
        wrap.appendChild(grid);
        root.appendChild(wrap);
      });
    })();
  </script>
  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 3: Verify**

Kiểm `exam-reading.html`:
- `?state=success`: **không có** audio bar ở đâu cả (khác hẳn Listening).
- Nút "← Câu trước" **bấm được** (không disabled) — ngược với Listening.
- Đoạn văn Part 7 trong `.passage` có scroll riêng khi dài, không đẩy dài cả trang.
- Câu 154 có badge "Đã đánh dấu".
- Xem source: không có `option--correct`, không có chữ "Đáp án đúng".
- `?state=resumed` / `?state=offline` / `?state=expired` / `?state=submitting`: hành xử như Listening.

Kiểm `question-navigator.html`:
- Đếm được **7 nhóm part**, số câu cuối là **200**.
- Part 2 nhóm từ 7 đến 31 (25 câu), Part 7 từ 147 đến 200 (54 câu).
- Ô 153 có outline đậm (`is-current`).
- Console không lỗi.

- [ ] **Step 4: Commit**

```bash
git add prototype/student/exam-reading.html prototype/student/question-navigator.html
git commit -m "feat: add reading exam screen and full question navigator"
```

---

### Task 8: Xác nhận nộp bài + Kết quả

**Files:**
- Create: `prototype/student/exam-confirm-submit.html`
- Create: `prototype/student/exam-result.html`

**Interfaces:**
- Consumes: `.modal-overlay` `.modal` `.navigator__grid` `.stat` `.table` từ Task 2 & 4
- Produces: cách trình bày điểm (`.score-hero`) mà `exam-review.html` và `exam-history.html` (Task 9) dùng lại

State `exam-confirm-submit.html`: `success` (còn câu chưa làm), `complete` (đã làm hết), `submitting`.
State `exam-result.html`: `loading`, `error`, `success`.

Cần thêm `.only-complete` vào `base.css`: thêm `.only-complete` vào nhóm `display: none` và thêm dòng `body[data-state="complete"] .only-complete` vào nhóm hiện.

- [ ] **Step 1: Thêm `.score-hero` vào `components.css`**

```css
/* ===== Khối điểm lớn ===== */
.score-hero {
  display: grid;
  grid-template-columns: 1.2fr 1fr 1fr;
  gap: var(--space-4);
  padding: var(--space-5);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
}
.score-hero__total { text-align: center; }
.score-hero__total-value {
  font-size: 56px; font-weight: 800; line-height: 1;
  color: var(--color-primary);
}
.score-hero__total-label { font-size: var(--text-sm); color: var(--color-text-muted); }
.score-hero__section { text-align: center; align-self: center; }
.score-hero__section-value { font-size: var(--text-3xl); font-weight: 700; line-height: 1.1; }
```

- [ ] **Step 2: Viết `student/exam-confirm-submit.html`**

Trang này là màn thi phủ một modal lên trên, để thấy rõ modal nằm trong ngữ cảnh nào.

```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Xác nhận nộp bài — ETS 2024 Test 5</title>
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="success,complete,submitting">
  <div class="app-shell">
    <header class="app-topbar">
      <div class="exam-topbar-group">
        <strong>ETS 2024 — Test 5</strong>
        <span class="badge badge--info">Reading · Part 7</span>
      </div>
      <span class="timer timer--warning">
        <span class="text-sm">Còn lại</span>
        <span class="timer__value">00:06:31</span>
      </span>
    </header>
    <main class="app-main">
      <div class="container">
        <p class="text-muted">Nền phía sau là màn thi Reading đang mở.</p>
        <div class="skeleton skeleton--title"></div>
        <div class="skeleton skeleton--text"></div>
        <div class="skeleton skeleton--text" style="width: 64%"></div>
      </div>
    </main>
  </div>

  <!-- ===== còn câu chưa làm ===== -->
  <div class="modal-overlay only-success" role="dialog" aria-modal="true"
       aria-labelledby="submit-title-1">
    <div class="modal">
      <div class="modal__header"><h2 id="submit-title-1" style="margin: 0">Nộp bài?</h2></div>
      <div class="modal__body stack">
        <div class="alert alert--warning">
          <strong>Bạn còn 19 câu chưa trả lời.</strong>
          Câu chưa trả lời được tính là sai.
        </div>
        <div class="grid-3">
          <div class="stat">
            <div class="stat__label">Đã trả lời</div>
            <div class="stat__value" style="font-size: var(--text-2xl)">181</div>
          </div>
          <div class="stat">
            <div class="stat__label">Đánh dấu</div>
            <div class="stat__value" style="font-size: var(--text-2xl)">7</div>
          </div>
          <div class="stat">
            <div class="stat__label">Chưa làm</div>
            <div class="stat__value" style="font-size: var(--text-2xl); color: var(--color-danger)">19</div>
          </div>
        </div>
        <p class="text-sm text-muted" style="margin: 0">
          Còn 6 phút 31 giây. Bạn vẫn có thể quay lại làm tiếp.
        </p>
      </div>
      <div class="modal__footer">
        <a class="btn" href="exam-reading.html">Quay lại làm tiếp</a>
        <a class="btn btn--danger" href="exam-result.html">Nộp bài luôn</a>
      </div>
    </div>
  </div>

  <!-- ===== đã làm hết ===== -->
  <div class="modal-overlay only-complete" role="dialog" aria-modal="true"
       aria-labelledby="submit-title-2">
    <div class="modal">
      <div class="modal__header"><h2 id="submit-title-2" style="margin: 0">Nộp bài?</h2></div>
      <div class="modal__body stack">
        <div class="alert alert--success">
          <strong>Bạn đã trả lời cả 200 câu.</strong>
          Sau khi nộp không sửa được đáp án.
        </div>
        <p class="text-sm text-muted" style="margin: 0">
          Còn 6 phút 31 giây. Muốn kiểm tra lại 7 câu đã đánh dấu trước khi nộp?
        </p>
      </div>
      <div class="modal__footer">
        <a class="btn" href="exam-reading.html">Xem 7 câu đã đánh dấu</a>
        <a class="btn btn--primary" href="exam-result.html">Nộp bài</a>
      </div>
    </div>
  </div>

  <!-- ===== submitting ===== -->
  <div class="modal-overlay only-submitting" role="dialog" aria-modal="true"
       aria-labelledby="submit-title-3">
    <div class="modal">
      <div class="modal__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">⏳</div>
          <div class="state-block__title" id="submit-title-3">Đang nộp bài…</div>
          <p class="state-block__desc">Đừng tắt trang.</p>
          <div class="progress" style="max-width: 240px; margin: 0 auto">
            <div class="progress__bar" style="width: 45%"></div>
          </div>
        </div>
      </div>
    </div>
  </div>

  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 3: Viết `student/exam-result.html`**

Dùng shell học viên có sidebar (copy từ `dashboard.html`), `.is-active` ở mục "Lịch sử thi". Thân trang:

```html
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="skeleton skeleton--block" style="height: 150px"></div>
            <div class="skeleton skeleton--block" style="height: 220px"></div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Không tải được kết quả</div>
                <p class="state-block__desc">
                  Bài của bạn đã được lưu. Thử tải lại để xem điểm.
                </p>
                <button class="btn btn--primary">Thử lại</button>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <div class="row row--between">
              <div>
                <h1 style="margin: 0 0 var(--space-1)">Kết quả — ETS 2024 Test 5</h1>
                <p class="text-sm text-muted" style="margin: 0">
                  Hoàn thành 20/09/2026 · 11:42 · thời gian làm 01:53:29
                </p>
              </div>
              <div class="row">
                <a class="btn" href="exam-history.html">Lịch sử thi</a>
                <a class="btn btn--primary" href="exam-review.html">Xem lại bài làm</a>
              </div>
            </div>

            <div class="score-hero">
              <div class="score-hero__total">
                <div class="score-hero__total-value">760</div>
                <div class="score-hero__total-label">Tổng điểm · thang 10–990</div>
                <p class="text-sm" style="margin: var(--space-2) 0 0">
                  <span class="badge badge--success">+40 so với lần trước</span>
                </p>
              </div>
              <div class="score-hero__section">
                <div class="score-hero__section-value">400</div>
                <div class="text-sm text-muted">Listening · 5–495</div>
                <p class="text-xs text-muted" style="margin: var(--space-1) 0 0">82/100 câu đúng</p>
              </div>
              <div class="score-hero__section">
                <div class="score-hero__section-value">360</div>
                <div class="text-sm text-muted">Reading · 5–495</div>
                <p class="text-xs text-muted" style="margin: var(--space-1) 0 0">74/100 câu đúng</p>
              </div>
            </div>

            <div class="alert alert--info">
              Điểm được quy đổi từ số câu đúng qua bảng convert của đề, không phải nhân tuyến tính.
              19 câu bỏ trống được tính là sai.
            </div>

            <div class="card">
              <div class="card__header"><strong>Chi tiết theo part</strong></div>
              <div class="card__body">
                <table class="table table--striped">
                  <thead>
                    <tr><th>Part</th><th>Đúng</th><th>Số câu</th><th>Tỷ lệ</th><th></th></tr>
                  </thead>
                  <tbody>
                    <tr><td>Part 1 — Ảnh</td><td>5</td><td>6</td><td>83%</td>
                        <td><a href="exam-review.html">Xem lại</a></td></tr>
                    <tr><td>Part 2 — Hỏi đáp</td><td>21</td><td>25</td><td>84%</td>
                        <td><a href="exam-review.html">Xem lại</a></td></tr>
                    <tr><td>Part 3 — Hội thoại</td><td>33</td><td>39</td><td>85%</td>
                        <td><a href="exam-review.html">Xem lại</a></td></tr>
                    <tr><td>Part 4 — Bài nói</td><td>23</td><td>30</td><td>77%</td>
                        <td><a href="exam-review.html">Xem lại</a></td></tr>
                    <tr><td>Part 5 — Câu đơn</td><td>24</td><td>30</td><td>80%</td>
                        <td><a href="exam-review.html">Xem lại</a></td></tr>
                    <tr><td>Part 6 — Điền đoạn</td><td>12</td><td>16</td><td>75%</td>
                        <td><a href="exam-review.html">Xem lại</a></td></tr>
                    <tr><td>Part 7 — Đọc hiểu</td><td>38</td><td>54</td><td>70%</td>
                        <td><a href="exam-review.html">Xem lại</a></td></tr>
                  </tbody>
                  <tfoot>
                    <tr><th>Tổng</th><th>156</th><th>200</th><th>78%</th><th></th></tr>
                  </tfoot>
                </table>
              </div>
            </div>

            <div class="grid-2">
              <div class="card">
                <div class="card__header"><strong>Điểm mạnh</strong></div>
                <div class="card__body">
                  <ul style="margin: 0; padding-left: var(--space-5)">
                    <li>Part 3 — Hội thoại: 85%</li>
                    <li>Part 2 — Hỏi đáp: 84%</li>
                  </ul>
                </div>
              </div>
              <div class="card">
                <div class="card__header"><strong>Cần cải thiện</strong></div>
                <div class="card__body stack">
                  <ul style="margin: 0; padding-left: var(--space-5)">
                    <li>Part 7 — Đọc hiểu: 70% · 16 câu sai</li>
                    <li>Part 6 — Điền đoạn: 75% · 4 câu sai</li>
                  </ul>
                  <a class="btn btn--sm" href="practice-select.html">Luyện Part 7 ngay</a>
                </div>
              </div>
            </div>
          </div>
```

- [ ] **Step 4: Verify**

Kiểm `exam-confirm-submit.html`:
- `?state=success`: modal có alert vàng "còn 19 câu chưa trả lời", số 19 màu đỏ, nút chính là "Nộp bài luôn" màu đỏ.
- `?state=complete`: alert xanh "đã trả lời cả 200 câu", nút chính xanh dương.
- `?state=submitting`: modal chỉ còn "Đang nộp bài…", không có nút nào bấm được.
- Nền phía sau modal mờ đi, thấy được topbar + timer.

Kiểm `exam-result.html`:
- `?state=success`: số 760 to, cỡ 56px; hai ô 400 / 360 nhỏ hơn.
- Bảng chi tiết: cộng cột "Đúng" ra **156**, cột "Số câu" ra **200**.
- Part 2 ghi 25 câu, Part 7 ghi 54 câu — khớp spec.
- `?state=loading` / `?state=error`: chỉ một khối hiện.
- Console không lỗi.

- [ ] **Step 5: Commit**

```bash
git add prototype/student/exam-confirm-submit.html prototype/student/exam-result.html \
        prototype/assets/css/components.css prototype/assets/css/base.css
git commit -m "feat: add submit confirmation modal and exam result screen"
```

---

### Task 9: Review bài làm + Lịch sử thi + Hồ sơ

`exam-review.html` là màn **duy nhất** (cùng với màn luyện tập ở Task 10) được phép chứa đáp án đúng và giải thích.

**Files:**
- Create: `prototype/student/exam-review.html`
- Create: `prototype/student/exam-history.html`
- Create: `prototype/student/profile.html`

**Interfaces:**
- Consumes: `.option--correct` `.option--wrong` `.question` `.passage` `.score-hero` `.table` từ Task 2, 6, 8
- Produces: pattern giải thích đáp án `.explain` mà `practice-take.html` (Task 10) dùng lại

State `exam-review.html`: `loading`, `error`, `success`.
State `exam-history.html`: `loading`, `empty`, `error`, `success`.
State `profile.html`: `success`, `submitting`, `error`.

- [ ] **Step 1: Thêm `.explain` vào `components.css`**

Trước khi viết `.explain`, thêm 1 token vào `tokens.css` (append vào `:root`, không sửa token cũ). Task 2 đã tạo bộ `-soft` / `-soft-border` cho success/warning/danger/info nhưng thiếu cặp border của primary:

```css
  /* viền nhạt primary — dùng cho .explain (Task 9) và .badge--primary (Task 11) */
  --color-primary-soft-border: #cfe0fb;
```

```css
/* ===== Giải thích đáp án (chỉ màn review + luyện tập) ===== */
.explain {
  margin-top: var(--space-4);
  padding: var(--space-4);
  background: var(--color-primary-soft);
  border: 1px solid var(--color-primary-soft-border);
  border-left: 4px solid var(--color-primary);
  border-radius: var(--radius-md);
  font-size: var(--text-sm);
}
.explain__title {
  font-weight: 700; margin-bottom: var(--space-2);
  display: flex; align-items: center; gap: var(--space-2);
}
.explain__transcript {
  margin-top: var(--space-3);
  padding-top: var(--space-3);
  border-top: 1px dashed var(--color-primary-soft-border);
  font-style: italic;
  color: var(--color-text-muted);
}
```

- [ ] **Step 2: Viết `student/exam-review.html`**

Shell học viên có sidebar, `.is-active` ở "Lịch sử thi". Thân trang:

```html
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="skeleton skeleton--block" style="height: 90px"></div>
            <div class="skeleton skeleton--block" style="height: 260px"></div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Không tải được bài làm</div>
                <p class="state-block__desc">Kiểm tra kết nối rồi thử lại.</p>
                <button class="btn btn--primary">Thử lại</button>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <div class="row row--between">
              <div>
                <h1 style="margin: 0 0 var(--space-1)">Xem lại — ETS 2024 Test 5</h1>
                <p class="text-sm text-muted" style="margin: 0">
                  760 điểm · L 400 · R 360 · làm ngày 20/09/2026
                </p>
              </div>
              <a class="btn" href="exam-result.html">Về trang kết quả</a>
            </div>

            <div class="card">
              <div class="card__body row" style="gap: var(--space-4); flex-wrap: wrap">
                <div class="field" style="flex: 0 0 200px">
                  <label class="field__label" for="review-part">Part</label>
                  <select class="field__input" id="review-part">
                    <option>Tất cả 200 câu</option>
                    <option>Part 1 — Ảnh</option>
                    <option>Part 2 — Hỏi đáp</option>
                    <option>Part 3 — Hội thoại</option>
                    <option>Part 4 — Bài nói</option>
                    <option>Part 5 — Câu đơn</option>
                    <option selected>Part 7 — Đọc hiểu</option>
                  </select>
                </div>
                <div class="field" style="flex: 0 0 200px">
                  <label class="field__label" for="review-filter">Lọc</label>
                  <select class="field__input" id="review-filter">
                    <option>Tất cả</option>
                    <option selected>Chỉ câu sai</option>
                    <option>Chỉ câu bỏ trống</option>
                    <option>Chỉ câu đã đánh dấu</option>
                  </select>
                </div>
                <div class="row" style="align-self: flex-end">
                  <span class="badge badge--success">156 đúng</span>
                  <span class="badge badge--danger">25 sai</span>
                  <span class="badge badge--muted">19 bỏ trống</span>
                </div>
              </div>
            </div>

            <!-- câu sai -->
            <div class="question">
              <div class="question__meta">
                <span class="question__number">Câu 154</span>
                <span class="badge badge--muted">Part 7</span>
                <span class="badge badge--danger">Bạn chọn sai</span>
              </div>
              <p class="question__stem">By when should employees label their boxes?</p>
              <div class="option-list" aria-label="Đáp án câu 154">
                <div class="option">
                  <span class="option__marker">A</span>
                  <span class="option__text">November 10</span>
                </div>
                <div class="option option--wrong">
                  <span class="option__marker">B</span>
                  <span class="option__text">November 11</span>
                  <span class="badge badge--danger">Bạn chọn</span>
                </div>
                <div class="option option--correct">
                  <span class="option__marker">C</span>
                  <span class="option__text">November 14</span>
                  <span class="badge badge--success">Đáp án đúng</span>
                </div>
                <div class="option">
                  <span class="option__marker">D</span>
                  <span class="option__text">November 17</span>
                </div>
              </div>
              <div class="explain">
                <div class="explain__title">
                  <span aria-hidden="true">💡</span> Giải thích
                </div>
                <p style="margin: 0">
                  Email viết "label every box with your department code
                  <strong>before leaving on November 14</strong>". Ngày 11/11 là ngày
                  <em>giao vật liệu đóng gói</em>, không phải hạn dán nhãn — đây là bẫy
                  thường gặp ở Part 7: chọn đúng ngày nhưng sai sự việc.
                </p>
              </div>
            </div>

            <!-- câu bỏ trống -->
            <div class="question">
              <div class="question__meta">
                <span class="question__number">Câu 155</span>
                <span class="badge badge--muted">Part 7</span>
                <span class="badge badge--warning">Bỏ trống</span>
              </div>
              <p class="question__stem">
                What should employees do if they need a computer on November 15?
              </p>
              <div class="option-list" aria-label="Đáp án câu 155">
                <div class="option">
                  <span class="option__marker">A</span>
                  <span class="option__text">Visit the Fairview Business Centre</span>
                </div>
                <div class="option option--correct">
                  <span class="option__marker">B</span>
                  <span class="option__text">Contact the help desk before November 10</span>
                  <span class="badge badge--success">Đáp án đúng</span>
                </div>
                <div class="option">
                  <span class="option__marker">C</span>
                  <span class="option__text">Label their boxes early</span>
                </div>
                <div class="option">
                  <span class="option__marker">D</span>
                  <span class="option__text">Email the operations manager</span>
                </div>
              </div>
              <div class="explain">
                <div class="explain__title">
                  <span aria-hidden="true">💡</span> Giải thích
                </div>
                <p style="margin: 0">
                  Đoạn 3: "please contact the help desk before November 10 so that a
                  temporary laptop can be arranged".
                </p>
              </div>
            </div>

            <!-- câu Listening: có transcript -->
            <div class="question">
              <div class="question__meta">
                <span class="question__number">Câu 8</span>
                <span class="badge badge--muted">Part 2</span>
                <span class="badge badge--danger">Bạn chọn sai</span>
              </div>
              <div class="row" style="margin-bottom: var(--space-3)">
                <button class="btn btn--sm">▶ Nghe lại câu này</button>
                <span class="text-xs text-muted">Sau khi thi, audio được phép nghe lại</span>
              </div>
              <div class="option-list" aria-label="Đáp án câu 8">
                <div class="option option--wrong">
                  <span class="option__marker">A</span>
                  <span class="option__text">At the front desk.</span>
                  <span class="badge badge--danger">Bạn chọn</span>
                </div>
                <div class="option option--correct">
                  <span class="option__marker">B</span>
                  <span class="option__text">Around nine o'clock.</span>
                  <span class="badge badge--success">Đáp án đúng</span>
                </div>
                <div class="option">
                  <span class="option__marker">C</span>
                  <span class="option__text">Yes, I already did.</span>
                </div>
              </div>
              <div class="explain">
                <div class="explain__title">
                  <span aria-hidden="true">💡</span> Giải thích
                </div>
                <p style="margin: 0">
                  Câu hỏi bắt đầu bằng <strong>When</strong> nên cần đáp án chỉ thời gian.
                  Đáp án A trả lời cho <em>Where</em> — bẫy sai loại câu hỏi.
                </p>
                <div class="explain__transcript">
                  <strong>Transcript:</strong> "When does the training session start?"
                </div>
              </div>
            </div>

            <div class="row" style="justify-content: center">
              <button class="btn">Xem thêm 22 câu sai</button>
            </div>
          </div>
```

- [ ] **Step 3: Viết `student/exam-history.html`**

Shell học viên, `.is-active` ở "Lịch sử thi". Thân trang:

```html
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="skeleton skeleton--block" style="height: 120px"></div>
            <div class="skeleton skeleton--block" style="height: 240px"></div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Không tải được lịch sử</div>
                <p class="state-block__desc">Kiểm tra kết nối rồi thử lại.</p>
                <button class="btn btn--primary">Thử lại</button>
              </div>
            </div></div>
          </div>

          <!-- ===== empty ===== -->
          <div class="only-empty stack">
            <h1>Lịch sử thi</h1>
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">□</div>
                <div class="state-block__title">Chưa có bài thi nào</div>
                <p class="state-block__desc">
                  Làm bài thi đầu tiên để bắt đầu theo dõi tiến độ.
                </p>
                <a class="btn btn--primary" href="exam-list.html">Bắt đầu thi</a>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <h1>Lịch sử thi</h1>

            <div class="grid-3">
              <div class="stat">
                <div class="stat__label">Bài đã hoàn thành</div>
                <div class="stat__value">7</div>
                <div class="stat__hint">từ 02/09/2026</div>
              </div>
              <div class="stat">
                <div class="stat__label">Điểm cao nhất</div>
                <div class="stat__value">760</div>
                <div class="stat__hint">ETS 2024 Test 5 · 20/09</div>
              </div>
              <div class="stat">
                <div class="stat__label">Trung bình</div>
                <div class="stat__value">716</div>
                <div class="stat__hint">L 382 · R 334</div>
              </div>
            </div>

            <div class="card">
              <div class="card__header">
                <div class="row row--between">
                  <strong>Tiến độ điểm</strong>
                  <span class="text-sm text-muted">7 lần thi gần nhất</span>
                </div>
              </div>
              <div class="card__body">
                <!-- biểu đồ cột đơn giản bằng div, không dùng thư viện -->
                <div class="row" style="align-items: flex-end; gap: var(--space-4); height: 180px">
                  <div style="flex: 1; text-align: center">
                    <div style="height: 132px; background: var(--color-primary); border-radius: var(--radius-sm) var(--radius-sm) 0 0"></div>
                    <div class="text-xs text-muted" style="margin-top: var(--space-1)">745<br>02/09</div>
                  </div>
                  <div style="flex: 1; text-align: center">
                    <div style="height: 122px; background: var(--color-primary); border-radius: var(--radius-sm) var(--radius-sm) 0 0"></div>
                    <div class="text-xs text-muted" style="margin-top: var(--space-1)">690<br>10/09</div>
                  </div>
                  <div style="flex: 1; text-align: center">
                    <div style="height: 128px; background: var(--color-primary); border-radius: var(--radius-sm) var(--radius-sm) 0 0"></div>
                    <div class="text-xs text-muted" style="margin-top: var(--space-1)">720<br>18/09</div>
                  </div>
                  <div style="flex: 1; text-align: center">
                    <div style="height: 140px; background: var(--color-success); border-radius: var(--radius-sm) var(--radius-sm) 0 0"></div>
                    <div class="text-xs text-muted" style="margin-top: var(--space-1)"><strong>760</strong><br>20/09</div>
                  </div>
                </div>
              </div>
            </div>

            <div class="card">
              <div class="card__header"><strong>Tất cả bài thi</strong></div>
              <div class="card__body">
                <table class="table table--striped">
                  <thead>
                    <tr>
                      <th>Đề</th><th>Ngày</th><th>Thời gian làm</th>
                      <th>Listening</th><th>Reading</th><th>Tổng</th>
                      <th>Trạng thái</th><th></th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td>ETS 2024 — Test 5</td><td>20/09/2026</td><td>01:53:29</td>
                      <td>400</td><td>360</td><td><strong>760</strong></td>
                      <td><span class="badge badge--success">Đã chấm</span></td>
                      <td><a href="exam-review.html">Xem lại</a></td>
                    </tr>
                    <tr>
                      <td>ETS 2024 — Test 3</td><td>18/09/2026</td><td>02:00:00</td>
                      <td>385</td><td>335</td><td><strong>720</strong></td>
                      <td><span class="badge badge--warning">Hết giờ</span></td>
                      <td><a href="exam-review.html">Xem lại</a></td>
                    </tr>
                    <tr>
                      <td>ETS 2024 — Test 2</td><td>10/09/2026</td><td>01:47:12</td>
                      <td>370</td><td>320</td><td><strong>690</strong></td>
                      <td><span class="badge badge--success">Đã chấm</span></td>
                      <td><a href="exam-review.html">Xem lại</a></td>
                    </tr>
                    <tr>
                      <td>ETS 2024 — Test 1</td><td>02/09/2026</td><td>01:58:40</td>
                      <td>395</td><td>350</td><td><strong>745</strong></td>
                      <td><span class="badge badge--success">Đã chấm</span></td>
                      <td><a href="exam-review.html">Xem lại</a></td>
                    </tr>
                    <tr>
                      <td>ETS 2024 — Test 4</td><td>28/08/2026</td><td>—</td>
                      <td>—</td><td>—</td><td>—</td>
                      <td><span class="badge badge--muted">Đã huỷ</span></td>
                      <td><span class="text-muted text-sm">Không có dữ liệu</span></td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>
```

- [ ] **Step 4: Viết `student/profile.html`**

Shell học viên, `.is-active` ở "Hồ sơ". Thân trang:

```html
          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Không lưu được thay đổi</div>
                <p class="state-block__desc">Thử lại sau ít phút.</p>
                <button class="btn btn--primary">Thử lại</button>
              </div>
            </div></div>
          </div>

          <!-- ===== success + submitting ===== -->
          <div class="multi-state stack" data-show="success submitting">
            <h1>Hồ sơ cá nhân</h1>

            <div class="grid-2">
              <div class="card">
                <div class="card__header"><strong>Thông tin cơ bản</strong></div>
                <div class="card__body stack">
                  <div class="field">
                    <label class="field__label" for="pf-name">Họ và tên</label>
                    <input class="field__input" id="pf-name" type="text" value="Nguyễn Văn A">
                  </div>
                  <div class="field">
                    <label class="field__label" for="pf-email">Email</label>
                    <input class="field__input" id="pf-email" type="email"
                           value="nguyenvana@example.com" disabled>
                    <p class="field__hint">Email dùng để đăng nhập, không đổi được.</p>
                  </div>
                  <div class="field">
                    <label class="field__label" for="pf-target">Điểm mục tiêu</label>
                    <input class="field__input" id="pf-target" type="number"
                           value="800" min="10" max="990" step="5">
                    <p class="field__hint">Dùng để hiển thị tiến độ ở trang chủ.</p>
                  </div>
                  <div class="field">
                    <label class="field__label" for="pf-deadline">Ngày dự định thi thật</label>
                    <input class="field__input" id="pf-deadline" type="date" value="2026-12-15">
                  </div>
                </div>
                <div class="card__footer">
                  <button class="btn btn--primary only-success">Lưu thay đổi</button>
                  <button class="btn btn--primary only-submitting" disabled>Đang lưu…</button>
                </div>
              </div>

              <div class="stack">
                <div class="card">
                  <div class="card__header"><strong>Ví lượt thi</strong></div>
                  <div class="card__body stack">
                    <div class="stat">
                      <div class="stat__label">Lượt thi còn lại</div>
                      <div class="stat__value">3</div>
                      <div class="stat__hint">Gói 10 lượt · mua 15/09/2026</div>
                    </div>
                    <a class="btn" href="../payment/wallet.html">Xem ví &amp; giao dịch</a>
                  </div>
                </div>

                <div class="card">
                  <div class="card__header"><strong>Đổi mật khẩu</strong></div>
                  <div class="card__body stack">
                    <div class="field">
                      <label class="field__label" for="pf-old">Mật khẩu hiện tại</label>
                      <input class="field__input" id="pf-old" type="password"
                             autocomplete="current-password">
                    </div>
                    <div class="field">
                      <label class="field__label" for="pf-new">Mật khẩu mới</label>
                      <input class="field__input" id="pf-new" type="password"
                             autocomplete="new-password">
                      <p class="field__hint">Tối thiểu 8 ký tự, có chữ và số.</p>
                    </div>
                  </div>
                  <div class="card__footer">
                    <button class="btn">Đổi mật khẩu</button>
                  </div>
                </div>
              </div>
            </div>
          </div>
```

- [ ] **Step 5: Verify**

Kiểm `exam-review.html`:
- `?state=success`: câu 154 có option B viền đỏ + badge "Bạn chọn", option C viền xanh + badge "Đáp án đúng".
- Câu 155 (bỏ trống): **chỉ** có option đúng được tô, không có option nào bị tô đỏ.
- Câu 8 có đúng 3 option (Part 2) và có khối transcript trong `.explain__transcript`.
- Mọi câu đều có khối `.explain` màu xanh nhạt.

Kiểm `exam-history.html`:
- `?state=success`: 3 ô số liệu, biểu đồ 4 cột (cột 760 màu xanh lá), bảng 5 dòng.
- Dòng cuối trạng thái "Đã huỷ" có các cột điểm là "—", không có link xem lại.
- `?state=empty`: chỉ thấy khối rỗng.

Kiểm `profile.html`:
- `?state=success`: input email **disabled** và có hint giải thích.
- `?state=submitting`: nút đổi thành "Đang lưu…" và bị khoá, form vẫn hiện.
- `?state=error`: chỉ thấy khối lỗi.
- Console không lỗi ở cả 3 trang.

- [ ] **Step 6: Commit**

```bash
git add prototype/student/exam-review.html prototype/student/exam-history.html \
        prototype/student/profile.html prototype/assets/css/components.css
git commit -m "feat: add exam review, history and profile screens"
```

---

### Task 10: Luyện tập — chọn part, làm bài, tổng kết

Luyện tập khác thi ở ba điểm phải thấy trên UI: **không giới hạn thời gian**, **feedback ngay sau mỗi câu**, và **miễn phí** (không trừ credit).

**Files:**
- Create: `prototype/student/practice-select.html`
- Create: `prototype/student/practice-take.html`
- Create: `prototype/student/practice-summary.html`

**Interfaces:**
- Consumes: shell học viên Task 4, `.option--correct` `.option--wrong` `.explain` `.explain__transcript` từ Task 2 & 9, `.question` `.passage` từ Task 6
- Produces: không có interface mới — task này chỉ lắp component đã có

State `practice-select.html`: `loading`, `error`, `success`.
State `practice-take.html`: `answering` (chưa chọn, chưa có feedback), `revealed-correct`, `revealed-wrong`, `finished`.
State `practice-summary.html`: `success`.

Cần thêm 4 state mới vào `base.css`: thêm `.only-answering`, `.only-revealed-correct`, `.only-revealed-wrong`, `.only-finished` vào nhóm `display: none`, và thêm các dòng tương ứng vào nhóm hiện:

```css
body[data-state="answering"]         .only-answering,
body[data-state="revealed-correct"]  .only-revealed-correct,
body[data-state="revealed-wrong"]    .only-revealed-wrong,
body[data-state="finished"]          .only-finished { display: revert; }
```

Và mở rộng `.multi-state` cho các state này:

```css
body[data-state="answering"]        .multi-state[data-show~="answering"],
body[data-state="revealed-correct"] .multi-state[data-show~="revealed-correct"],
body[data-state="revealed-wrong"]   .multi-state[data-show~="revealed-wrong"],
body[data-state="finished"]         .multi-state[data-show~="finished"],
body[data-state="complete"]         .multi-state[data-show~="complete"],
body[data-state="paywall"]          .multi-state[data-show~="paywall"],
body[data-state="pending"]          .multi-state[data-show~="pending"],
body[data-state="failed"]           .multi-state[data-show~="failed"],
body[data-state="loading"]          .multi-state[data-show~="loading"],
body[data-state="empty"]            .multi-state[data-show~="empty"],
body[data-state="error"]            .multi-state[data-show~="error"] { display: revert; }
```

- [ ] **Step 1: Viết `student/practice-select.html`**

Shell học viên, `.is-active` ở "Luyện theo part". Thân trang:

```html
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="grid-2">
              <div class="skeleton skeleton--block" style="height: 130px"></div>
              <div class="skeleton skeleton--block" style="height: 130px"></div>
              <div class="skeleton skeleton--block" style="height: 130px"></div>
              <div class="skeleton skeleton--block" style="height: 130px"></div>
            </div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Không tải được dữ liệu luyện tập</div>
                <p class="state-block__desc">Kiểm tra kết nối rồi thử lại.</p>
                <button class="btn btn--primary">Thử lại</button>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <div>
              <h1 style="margin: 0 0 var(--space-1)">Luyện theo part</h1>
              <p class="text-muted" style="margin: 0">
                Không giới hạn thời gian, xem đáp án và giải thích ngay sau mỗi câu.
                <strong>Miễn phí, không trừ lượt thi.</strong>
              </p>
            </div>

            <div class="card">
              <div class="card__body row" style="gap: var(--space-4); flex-wrap: wrap">
                <div class="field" style="flex: 0 0 200px">
                  <label class="field__label" for="pr-count">Số câu mỗi phiên</label>
                  <select class="field__input" id="pr-count">
                    <option>10 câu</option>
                    <option selected>20 câu</option>
                    <option>40 câu</option>
                    <option>Toàn bộ part</option>
                  </select>
                </div>
                <div class="field" style="flex: 0 0 220px">
                  <label class="field__label" for="pr-source">Nguồn câu hỏi</label>
                  <select class="field__input" id="pr-source">
                    <option selected>Tất cả đề</option>
                    <option>Chỉ câu tôi từng làm sai</option>
                    <option>Chỉ câu chưa từng làm</option>
                  </select>
                </div>
                <div class="field" style="flex: 0 0 200px">
                  <label class="field__label" for="pr-mode">Kiểu feedback</label>
                  <select class="field__input" id="pr-mode">
                    <option selected>Hiện đáp án ngay sau mỗi câu</option>
                    <option>Chỉ hiện ở cuối phiên</option>
                  </select>
                </div>
              </div>
            </div>

            <h2>Listening</h2>
            <div class="grid-2">
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>Part 1 — Ảnh</strong>
                    <span class="badge badge--success">83%</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    6 câu mỗi đề · đã luyện 48 câu · 1 240 câu trong kho
                  </p>
                  <div class="progress"><div class="progress__bar" style="width: 83%"></div></div>
                  <a class="btn btn--primary" href="practice-take.html">Luyện Part 1</a>
                </div>
              </div>
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>Part 2 — Hỏi đáp</strong>
                    <span class="badge badge--success">76%</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    25 câu mỗi đề · đã luyện 150 câu · 3 100 câu trong kho
                  </p>
                  <div class="progress"><div class="progress__bar" style="width: 76%"></div></div>
                  <a class="btn btn--primary" href="practice-take.html">Luyện Part 2</a>
                </div>
              </div>
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>Part 3 — Hội thoại</strong>
                    <span class="badge badge--success">85%</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    39 câu mỗi đề · đã luyện 78 câu · 4 800 câu trong kho
                  </p>
                  <div class="progress"><div class="progress__bar" style="width: 85%"></div></div>
                  <a class="btn btn--primary" href="practice-take.html">Luyện Part 3</a>
                </div>
              </div>
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>Part 4 — Bài nói</strong>
                    <span class="badge badge--warning">77%</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    30 câu mỗi đề · đã luyện 30 câu · 3 700 câu trong kho
                  </p>
                  <div class="progress"><div class="progress__bar" style="width: 77%"></div></div>
                  <a class="btn btn--primary" href="practice-take.html">Luyện Part 4</a>
                </div>
              </div>
            </div>

            <h2>Reading</h2>
            <div class="grid-2">
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>Part 5 — Câu đơn</strong>
                    <span class="badge badge--warning">71%</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    30 câu mỗi đề · đã luyện 210 câu · 3 700 câu trong kho
                  </p>
                  <div class="progress"><div class="progress__bar" style="width: 71%"></div></div>
                  <a class="btn btn--primary" href="practice-take.html">Luyện Part 5</a>
                </div>
              </div>
              <div class="card">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>Part 6 — Điền đoạn</strong>
                    <span class="badge badge--warning">75%</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    16 câu mỗi đề · đã luyện 32 câu · 1 980 câu trong kho
                  </p>
                  <div class="progress"><div class="progress__bar" style="width: 75%"></div></div>
                  <a class="btn btn--primary" href="practice-take.html">Luyện Part 6</a>
                </div>
              </div>
              <div class="card" style="border-color: var(--color-warning)">
                <div class="card__body stack">
                  <div class="row row--between">
                    <strong>Part 7 — Đọc hiểu</strong>
                    <span class="badge badge--danger">58%</span>
                  </div>
                  <p class="text-sm text-muted" style="margin: 0">
                    54 câu mỗi đề · đã luyện 54 câu · 6 700 câu trong kho
                  </p>
                  <div class="progress"><div class="progress__bar" style="width: 58%"></div></div>
                  <div class="alert alert--warning text-sm" style="padding: var(--space-2) var(--space-3)">
                    Part yếu nhất của bạn. Nên luyện phần này trước.
                  </div>
                  <a class="btn btn--primary" href="practice-take.html">Luyện Part 7</a>
                </div>
              </div>
            </div>
          </div>
```

- [ ] **Step 2: Viết `student/practice-take.html`**

Trang không sidebar (giống màn thi, để tập trung), nhưng topbar khác: **không có timer**, có nút thoát và tiến độ phiên.

```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Luyện Part 5 — TOEIC Practice</title>
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="answering,revealed-correct,revealed-wrong,finished">
  <div class="app-shell">
    <header class="app-topbar">
      <div class="exam-topbar-group">
        <strong>Luyện Part 5 — Câu đơn</strong>
        <span class="badge badge--muted">Không giới hạn thời gian</span>
      </div>
      <div class="exam-topbar-group">
        <span class="text-sm text-muted multi-state"
              data-show="answering revealed-correct revealed-wrong">Câu 7 / 20</span>
        <div class="progress multi-state" data-show="answering revealed-correct revealed-wrong"
             style="width: 160px">
          <div class="progress__bar" style="width: 35%"></div>
        </div>
        <a class="btn btn--ghost btn--sm" href="practice-select.html">Thoát</a>
      </div>
    </header>
    <main class="app-main">
      <div class="container" style="max-width: 820px">

        <!-- ===== đang trả lời: chưa có feedback ===== -->
        <div class="only-answering stack">
          <div class="question">
            <div class="question__meta">
              <span class="question__number">Câu 7</span>
              <span class="badge badge--muted">Part 5 — Câu đơn</span>
            </div>
            <p class="question__stem">
              The regional manager ------- the quarterly figures before the board meeting.
            </p>
            <div class="option-list" role="radiogroup" aria-label="Đáp án câu 7">
              <label class="option">
                <span class="option__marker">A</span>
                <span class="option__text">review</span>
              </label>
              <label class="option">
                <span class="option__marker">B</span>
                <span class="option__text">reviewing</span>
              </label>
              <label class="option">
                <span class="option__marker">C</span>
                <span class="option__text">will review</span>
              </label>
              <label class="option">
                <span class="option__marker">D</span>
                <span class="option__text">to review</span>
              </label>
            </div>
          </div>
          <div class="row row--between">
            <button class="btn btn--ghost">Bỏ qua câu này</button>
            <button class="btn btn--primary btn--lg">Kiểm tra đáp án</button>
          </div>
        </div>

        <!-- ===== trả lời đúng ===== -->
        <div class="only-revealed-correct stack">
          <div class="question">
            <div class="question__meta">
              <span class="question__number">Câu 7</span>
              <span class="badge badge--muted">Part 5 — Câu đơn</span>
              <span class="badge badge--success">Chính xác</span>
            </div>
            <p class="question__stem">
              The regional manager ------- the quarterly figures before the board meeting.
            </p>
            <div class="option-list" aria-label="Đáp án câu 7">
              <div class="option"><span class="option__marker">A</span>
                <span class="option__text">review</span></div>
              <div class="option"><span class="option__marker">B</span>
                <span class="option__text">reviewing</span></div>
              <div class="option option--correct">
                <span class="option__marker">C</span>
                <span class="option__text">will review</span>
                <span class="badge badge--success">Bạn chọn · đúng</span>
              </div>
              <div class="option"><span class="option__marker">D</span>
                <span class="option__text">to review</span></div>
            </div>
            <div class="explain">
              <div class="explain__title"><span aria-hidden="true">💡</span> Giải thích</div>
              <p style="margin: 0">
                Câu thiếu động từ chính, nên loại <em>reviewing</em> và <em>to review</em>.
                Mốc thời gian "before the board meeting" chỉ việc chưa xảy ra, nên chọn
                thì tương lai <strong>will review</strong>. Dạng <em>review</em> không chia
                đúng với chủ ngữ số ít.
              </p>
            </div>
          </div>
          <div class="row row--between">
            <button class="btn btn--ghost">Lưu câu này để xem lại</button>
            <button class="btn btn--primary btn--lg">Câu tiếp →</button>
          </div>
        </div>

        <!-- ===== trả lời sai ===== -->
        <div class="only-revealed-wrong stack">
          <div class="question">
            <div class="question__meta">
              <span class="question__number">Câu 7</span>
              <span class="badge badge--muted">Part 5 — Câu đơn</span>
              <span class="badge badge--danger">Chưa đúng</span>
            </div>
            <p class="question__stem">
              The regional manager ------- the quarterly figures before the board meeting.
            </p>
            <div class="option-list" aria-label="Đáp án câu 7">
              <div class="option"><span class="option__marker">A</span>
                <span class="option__text">review</span></div>
              <div class="option option--wrong">
                <span class="option__marker">B</span>
                <span class="option__text">reviewing</span>
                <span class="badge badge--danger">Bạn chọn</span>
              </div>
              <div class="option option--correct">
                <span class="option__marker">C</span>
                <span class="option__text">will review</span>
                <span class="badge badge--success">Đáp án đúng</span>
              </div>
              <div class="option"><span class="option__marker">D</span>
                <span class="option__text">to review</span></div>
            </div>
            <div class="explain">
              <div class="explain__title"><span aria-hidden="true">💡</span> Giải thích</div>
              <p style="margin: 0">
                <em>reviewing</em> là V-ing, không thể đứng một mình làm động từ chính —
                câu sẽ thiếu vị ngữ. Chỗ trống cần một động từ đã chia, và "before the
                board meeting" cho biết hành động chưa diễn ra, nên đáp án là
                <strong>will review</strong>.
              </p>
              <div class="explain__transcript">
                Dạng bẫy hay gặp ở Part 5: đưa V-ing vào vị trí động từ chính.
              </div>
            </div>
          </div>
          <div class="row row--between">
            <button class="btn btn--ghost">Lưu câu này để xem lại</button>
            <button class="btn btn--primary btn--lg">Câu tiếp →</button>
          </div>
        </div>

        <!-- ===== hết phiên ===== -->
        <div class="only-finished">
          <div class="card"><div class="card__body">
            <div class="state-block">
              <div class="state-block__icon" aria-hidden="true">✓</div>
              <div class="state-block__title">Xong 20 câu</div>
              <p class="state-block__desc">Xem lại kết quả phiên luyện này.</p>
              <a class="btn btn--primary btn--lg" href="practice-summary.html">Xem tổng kết</a>
            </div>
          </div></div>
        </div>

      </div>
    </main>
  </div>
  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 3: Viết `student/practice-summary.html`**

Shell học viên, `.is-active` ở "Luyện theo part". Thân trang (chỉ state `success`):

```html
          <div class="only-success stack">
            <div class="row row--between">
              <div>
                <h1 style="margin: 0 0 var(--space-1)">Tổng kết phiên luyện</h1>
                <p class="text-sm text-muted" style="margin: 0">
                  Part 5 — Câu đơn · 20 câu · 14 phút 32 giây · 20/09/2026
                </p>
              </div>
              <div class="row">
                <a class="btn" href="practice-select.html">Chọn part khác</a>
                <a class="btn btn--primary" href="practice-take.html">Luyện tiếp 20 câu</a>
              </div>
            </div>

            <div class="grid-3">
              <div class="stat">
                <div class="stat__label">Đúng</div>
                <div class="stat__value" style="color: var(--color-success)">15</div>
                <div class="stat__hint">trên 20 câu · 75%</div>
              </div>
              <div class="stat">
                <div class="stat__label">Sai</div>
                <div class="stat__value" style="color: var(--color-danger)">4</div>
                <div class="stat__hint">đã lưu vào kho câu sai</div>
              </div>
              <div class="stat">
                <div class="stat__label">Bỏ qua</div>
                <div class="stat__value">1</div>
                <div class="stat__hint">không tính vào tỷ lệ</div>
              </div>
            </div>

            <div class="alert alert--success">
              Độ chính xác Part 5 của bạn tăng từ <strong>71%</strong> lên
              <strong>72%</strong> sau phiên này.
            </div>

            <div class="card">
              <div class="card__header">
                <div class="row row--between">
                  <strong>Các câu cần xem lại</strong>
                  <span class="text-sm text-muted">4 câu sai · 1 câu bỏ qua</span>
                </div>
              </div>
              <div class="card__body">
                <table class="table table--striped">
                  <thead>
                    <tr><th>Câu</th><th>Dạng ngữ pháp</th><th>Bạn chọn</th><th>Đáp án</th><th></th></tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td>7</td><td>Thì của động từ</td>
                      <td><span class="badge badge--danger">B — reviewing</span></td>
                      <td><span class="badge badge--success">C — will review</span></td>
                      <td><a href="practice-take.html?state=revealed-wrong">Xem giải thích</a></td>
                    </tr>
                    <tr>
                      <td>11</td><td>Giới từ</td>
                      <td><span class="badge badge--danger">A — in</span></td>
                      <td><span class="badge badge--success">D — within</span></td>
                      <td><a href="practice-take.html?state=revealed-wrong">Xem giải thích</a></td>
                    </tr>
                    <tr>
                      <td>14</td><td>Từ loại</td>
                      <td><span class="badge badge--danger">C — competitively</span></td>
                      <td><span class="badge badge--success">B — competitive</span></td>
                      <td><a href="practice-take.html?state=revealed-wrong">Xem giải thích</a></td>
                    </tr>
                    <tr>
                      <td>18</td><td>Đại từ quan hệ</td>
                      <td><span class="badge badge--danger">A — which</span></td>
                      <td><span class="badge badge--success">C — whose</span></td>
                      <td><a href="practice-take.html?state=revealed-wrong">Xem giải thích</a></td>
                    </tr>
                    <tr>
                      <td>20</td><td>Liên từ</td>
                      <td><span class="badge badge--muted">Bỏ qua</span></td>
                      <td><span class="badge badge--success">B — although</span></td>
                      <td><a href="practice-take.html?state=revealed-wrong">Xem giải thích</a></td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>

            <div class="card">
              <div class="card__header"><strong>Lỗi theo dạng ngữ pháp</strong></div>
              <div class="card__body stack">
                <div>
                  <div class="row row--between text-sm">
                    <span>Thì của động từ</span><span class="text-muted">1 sai / 5 câu</span>
                  </div>
                  <div class="progress"><div class="progress__bar" style="width: 80%"></div></div>
                </div>
                <div>
                  <div class="row row--between text-sm">
                    <span>Từ loại</span><span class="text-muted">1 sai / 6 câu</span>
                  </div>
                  <div class="progress"><div class="progress__bar" style="width: 83%"></div></div>
                </div>
                <div>
                  <div class="row row--between text-sm">
                    <span>Giới từ</span><span class="text-muted">1 sai / 3 câu</span>
                  </div>
                  <div class="progress"><div class="progress__bar" style="width: 67%"></div></div>
                </div>
                <div>
                  <div class="row row--between text-sm">
                    <span>Đại từ quan hệ</span><span class="text-muted">1 sai / 2 câu</span>
                  </div>
                  <div class="progress"><div class="progress__bar" style="width: 50%"></div></div>
                </div>
              </div>
            </div>
          </div>
```

- [ ] **Step 4: Verify**

Kiểm `practice-select.html`:
- `?state=success`: 7 thẻ part (4 Listening + 3 Reading), thẻ Part 7 có viền vàng + alert "Part yếu nhất".
- Có dòng chữ "Miễn phí, không trừ lượt thi" ở đầu trang.

Kiểm `practice-take.html`:
- Topbar **không có timer**, có badge "Không giới hạn thời gian" và tiến độ "Câu 7 / 20".
- `?state=answering`: 4 option **không** option nào được tô màu đúng/sai, nút chính là "Kiểm tra đáp án", **không** có khối `.explain`.
- `?state=revealed-correct`: option C xanh với badge "Bạn chọn · đúng", có `.explain`, nút chính "Câu tiếp →".
- `?state=revealed-wrong`: option B đỏ + option C xanh cùng lúc, có `.explain`.
- `?state=finished`: chỉ thấy khối "Xong 20 câu", topbar không còn tiến độ.

Kiểm `practice-summary.html`:
- 3 ô số liệu 15 / 4 / 1, tổng 20.
- Bảng 5 dòng; dòng câu 20 cột "Bạn chọn" là badge xám "Bỏ qua".
- Console không lỗi ở cả 3 trang.

- [ ] **Step 5: Commit**

```bash
git add prototype/student/practice-select.html prototype/student/practice-take.html \
        prototype/student/practice-summary.html prototype/assets/css/base.css
git commit -m "feat: add practice mode screens with instant feedback"
```

---

### Task 11: Thanh toán — bảng giá, checkout, cổng giả

**Files:**
- Create: `prototype/payment/pricing.html`
- Create: `prototype/payment/checkout.html`
- Create: `prototype/payment/gateway-mock.html`

**Interfaces:**
- Consumes: shell học viên Task 4 (đường dẫn CSS vẫn `../assets/...` vì `payment/` cùng cấp với `student/`), `.card` `.stat` `.alert` `.table` `.stepper`
- Produces: `.price-card` / `.price-card--featured` / `.price-card__amount` dùng lại ở `admin/packages.html` (Task 13)

State `pricing.html`: `loading`, `error`, `success`.
State `checkout.html`: `success`, `submitting`, `error`.
State `gateway-mock.html`: `success` (trang giả, một state).

Lưu ý điều hướng: các trang trong `payment/` trỏ về student bằng `../student/...`, về mục lục bằng `../index.html`.

- [ ] **Step 1: Thêm `.price-card` vào `components.css`**

Hai token dùng ở đây đã có sẵn, KHÔNG định nghĩa lại: `--color-primary-soft` do Task 2 tạo, `--color-primary-soft-border` do Task 9 thêm vào `tokens.css`.

```css
/* ===== Badge nhấn (bổ sung cho nhóm badge ở Task 2) ===== */
.badge--primary {
  background: var(--color-primary-soft); border-color: var(--color-primary-soft-border); color: var(--color-primary);
  font-weight: 600;
}

/* ===== Thẻ gói giá ===== */
.price-card {
  display: flex; flex-direction: column;
  padding: var(--space-5);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
}
.price-card--featured {
  border-color: var(--color-primary);
  border-width: 2px;
  box-shadow: var(--shadow-md);
}
.price-card__name { font-weight: 700; font-size: var(--text-lg); }
.price-card__amount {
  font-size: var(--text-3xl); font-weight: 800; line-height: 1.1;
  margin: var(--space-3) 0 var(--space-1);
}
.price-card__unit { font-size: var(--text-sm); color: var(--color-text-muted); }
.price-card__list {
  flex: 1 1 auto;
  margin: var(--space-4) 0; padding-left: var(--space-5);
  font-size: var(--text-sm);
}
.price-card__list li + li { margin-top: var(--space-2); }
```

- [ ] **Step 2: Tạo `payment/pricing.html`**

Dùng shell học viên (copy topbar + sidebar từ `student/dashboard.html`, sửa `../assets/` giữ nguyên, mục sidebar active là "Nạp lượt thi"). `<body data-states="loading,error,success" data-state="success">`.

Nội dung `success` — 3 thẻ gói trong `.grid-3`:

```html
<div class="container only-success">
  <div class="stack">
    <div>
      <h1>Nạp lượt thi</h1>
      <p class="text-muted">Mỗi lượt dùng cho một đề thi full 200 câu. Luyện tập theo part luôn miễn phí.</p>
    </div>

    <div class="alert alert--info">Bạn đang còn <strong>3 lượt thi</strong>. Lượt thi không có ngày hết hạn.</div>

    <div class="grid-3">
      <div class="price-card">
        <div class="price-card__name">Gói Lẻ</div>
        <div class="price-card__amount">49.000 ₫</div>
        <div class="price-card__unit">1 lượt thi — 49.000 ₫/lượt</div>
        <ul class="price-card__list">
          <li>1 lượt thi full test</li>
          <li>Xem lại đáp án và giải thích</li>
          <li>Không hết hạn</li>
        </ul>
        <a class="btn" href="checkout.html?package=le">Chọn gói này</a>
      </div>

      <div class="price-card price-card--featured">
        <div class="row row--between">
          <div class="price-card__name">Gói Ôn Thi</div>
          <span class="badge badge--primary">Phổ biến</span>
        </div>
        <div class="price-card__amount">199.000 ₫</div>
        <div class="price-card__unit">5 lượt thi — 39.800 ₫/lượt</div>
        <ul class="price-card__list">
          <li>5 lượt thi full test</li>
          <li>Thống kê điểm mạnh yếu theo part</li>
          <li>Không hết hạn</li>
        </ul>
        <a class="btn btn--primary" href="checkout.html?package=onthi">Chọn gói này</a>
      </div>

      <div class="price-card">
        <div class="price-card__name">Gói Cấp Tốc</div>
        <div class="price-card__amount">349.000 ₫</div>
        <div class="price-card__unit">10 lượt thi — 34.900 ₫/lượt</div>
        <ul class="price-card__list">
          <li>10 lượt thi full test</li>
          <li>Thống kê điểm mạnh yếu theo part</li>
          <li>Không hết hạn</li>
        </ul>
        <a class="btn" href="checkout.html?package=captoc">Chọn gói này</a>
      </div>
    </div>
  </div>
</div>
```

State `loading`: 3 `.price-card` chứa `.skeleton--title` + 3 dòng `.skeleton--text`.
State `error`: `.state-block` với tiêu đề "Không tải được bảng giá", mô tả "Vui lòng thử lại sau ít phút." và `.btn` "Thử lại".

- [ ] **Step 3: Tạo `payment/checkout.html`**

`<body data-states="success,submitting,error" data-state="success">`. Có shell học viên. Layout `.grid-2` — cột trái là form, cột phải là thẻ tóm tắt đơn.

```html
<div class="container container--narrow multi-state" data-show="success submitting error">
  <div class="stack">
    <h1>Xác nhận đơn hàng</h1>

    <div class="stepper">
      <div class="stepper__item is-done">1. Chọn gói</div>
      <div class="stepper__item is-current">2. Xác nhận</div>
      <div class="stepper__item">3. Thanh toán</div>
      <div class="stepper__item">4. Hoàn tất</div>
    </div>

    <div class="alert alert--danger only-error">
      Không tạo được đơn hàng. Mã lỗi: ORDER_CREATE_FAILED. Vui lòng thử lại.
    </div>

    <div class="grid-2">
      <div class="card">
        <div class="card__header"><strong>Phương thức thanh toán</strong></div>
        <div class="card__body">
          <div class="option-list" role="radiogroup" aria-label="Phương thức thanh toán">
            <label class="option option--selected">
              <input type="radio" name="method" value="vnpay" checked>
              <span class="option__marker">1</span>
              <span class="option__text">VNPay — thẻ ATM, Internet Banking, QR</span>
            </label>
            <label class="option">
              <input type="radio" name="method" value="momo">
              <span class="option__marker">2</span>
              <span class="option__text">MoMo — ví điện tử</span>
            </label>
          </div>
          <p class="text-muted text-sm">Bạn sẽ được chuyển sang trang của cổng thanh toán để hoàn tất.</p>
        </div>
      </div>

      <div class="card">
        <div class="card__header"><strong>Đơn hàng</strong></div>
        <div class="card__body stack">
          <div class="row row--between"><span>Gói Ôn Thi</span><strong>199.000 ₫</strong></div>
          <div class="row row--between"><span class="text-muted">Số lượt thi</span><span>5 lượt</span></div>
          <div class="row row--between"><span class="text-muted">Mã đơn</span><span>DH-20260920-0148</span></div>
          <hr>
          <div class="row row--between"><strong>Tổng thanh toán</strong><strong>199.000 ₫</strong></div>
        </div>
        <div class="card__footer">
          <a class="btn btn--primary btn--lg only-success" href="gateway-mock.html">Thanh toán 199.000 ₫</a>
          <button class="btn btn--primary btn--lg only-submitting" disabled>Đang tạo đơn hàng…</button>
          <a class="btn btn--ghost" href="pricing.html">Chọn gói khác</a>
        </div>
      </div>
    </div>
  </div>
</div>
```

- [ ] **Step 4: Tạo `payment/gateway-mock.html`**

Trang giả lập cổng thanh toán. **Không có shell học viên** — đây là trang của bên thứ ba, chỉ có `base.css` + `components.css` + `tokens.css`. `<body data-states="success" data-state="success">`.

Mục đích duy nhất: cho người review bấm thử nhánh thành công / thất bại / treo.

```html
<div class="container container--narrow">
  <div class="stack">
    <div class="alert alert--warning">
      Trang mô phỏng cổng thanh toán. Đây <strong>không phải</strong> VNPay thật, không nhập thông tin thẻ thật.
    </div>

    <div class="card">
      <div class="card__header"><strong>VNPay (mô phỏng)</strong></div>
      <div class="card__body stack">
        <div class="row row--between"><span class="text-muted">Nhà bán</span><span>TOEIC Practice</span></div>
        <div class="row row--between"><span class="text-muted">Mã đơn</span><span>DH-20260920-0148</span></div>
        <div class="row row--between"><strong>Số tiền</strong><strong>199.000 ₫</strong></div>
        <hr>
        <div class="field">
          <label class="field__label" for="card-number">Số thẻ</label>
          <input class="field__input" id="card-number" value="9704 xxxx xxxx 1234" disabled>
          <span class="field__hint">Ô nhập bị khoá trong bản prototype.</span>
        </div>
      </div>
      <div class="card__footer stack">
        <p class="text-sm text-muted">Chọn kết quả muốn xem:</p>
        <div class="row">
          <a class="btn btn--primary" href="payment-result.html?state=success">Thanh toán thành công</a>
          <a class="btn btn--danger" href="payment-result.html?state=failed">Thanh toán thất bại</a>
          <a class="btn btn--ghost" href="payment-result.html?state=pending">Cổng phản hồi chậm</a>
        </div>
      </div>
    </div>
  </div>
</div>
```

- [ ] **Step 5: Verify trên browser**

Chạy `python -m http.server 8080` trong `prototype/`, mở lần lượt:
- `payment/pricing.html` với `?state=loading`, `?state=error`, `?state=success` — kiểm tra gói giữa có viền xanh đậm hơn hai gói bên.
- `payment/checkout.html` với `?state=success`, `?state=submitting`, `?state=error` — ở `submitting` nút phải `disabled` và không còn link "Thanh toán".
- `payment/gateway-mock.html` — bấm cả 3 nút, xác nhận URL đích có đúng `?state=`.
Không có lỗi console. Đây là gate của task, không được bỏ.

- [ ] **Step 6: Commit**

```bash
git add prototype/assets/css/components.css prototype/payment/pricing.html prototype/payment/checkout.html prototype/payment/gateway-mock.html
git commit -m "feat: add pricing, checkout and mock gateway screens"
```

### Task 12: Kết quả thanh toán và ví lượt thi

**Files:**
- Create: `prototype/payment/payment-result.html`
- Create: `prototype/payment/wallet.html`
- Modify: `prototype/assets/css/base.css` (thêm `.only-paid`)

**Interfaces:**
- Consumes: `.stepper` `.alert` `.state-block` `.table` `.stat` `.price-card` (Task 11), shell học viên (Task 4)
- Produces: không có class mới ngoài state `paid`

`payment-result.html` là màn khó nhất của nhóm thanh toán: cổng trả về **chậm và không chắc chắn**, nên `pending` là state bình thường chứ không phải lỗi.

State `payment-result.html`: `success` (đã cộng lượt), `failed`, `pending` (đang đối soát), `error` (không đọc được trạng thái đơn).
State `wallet.html`: `loading`, `empty`, `error`, `paid` (có giao dịch).

- [ ] **Step 1: Thêm `.only-paid` vào `base.css`**

Chèn vào đúng nhóm quy tắc state đã có:

```css
body[data-state="paid"] .only-paid { display: revert; }
```

Và thêm `paid` vào danh sách selector `.multi-state`:

```css
body[data-state="paid"] .multi-state[data-show~="paid"] { display: revert; }
```

- [ ] **Step 2: Tạo `payment/payment-result.html`**

Có shell học viên. `<body data-states="success,failed,pending,error" data-state="success">`.

```html
<div class="container container--narrow">
  <div class="stack">
    <div class="stepper">
      <div class="stepper__item is-done">1. Chọn gói</div>
      <div class="stepper__item is-done">2. Xác nhận</div>
      <div class="stepper__item is-done">3. Thanh toán</div>
      <div class="stepper__item is-current">4. Hoàn tất</div>
    </div>

    <!-- success -->
    <div class="card only-success">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">✓</div>
          <h1 class="state-block__title">Thanh toán thành công</h1>
          <p class="state-block__desc">Đã cộng <strong>5 lượt thi</strong> vào tài khoản. Bạn đang có <strong>8 lượt</strong>.</p>
        </div>
        <table class="table">
          <tbody>
            <tr><th scope="row">Mã đơn</th><td>DH-20260920-0148</td></tr>
            <tr><th scope="row">Gói</th><td>Gói Ôn Thi — 5 lượt</td></tr>
            <tr><th scope="row">Số tiền</th><td>199.000 ₫</td></tr>
            <tr><th scope="row">Phương thức</th><td>VNPay</td></tr>
            <tr><th scope="row">Thời gian</th><td>20/09/2026 14:32</td></tr>
          </tbody>
        </table>
      </div>
      <div class="card__footer row">
        <a class="btn btn--primary" href="../student/exam-list.html">Vào thi ngay</a>
        <a class="btn btn--ghost" href="wallet.html">Xem ví lượt thi</a>
      </div>
    </div>

    <!-- failed -->
    <div class="card only-failed">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">✕</div>
          <h1 class="state-block__title">Thanh toán thất bại</h1>
          <p class="state-block__desc">Cổng thanh toán báo giao dịch bị huỷ. Bạn <strong>chưa bị trừ tiền</strong> và chưa được cộng lượt.</p>
        </div>
        <div class="alert alert--warning">Mã lỗi từ cổng: 24 — Khách hàng huỷ giao dịch.</div>
      </div>
      <div class="card__footer row">
        <a class="btn btn--primary" href="checkout.html">Thử thanh toán lại</a>
        <a class="btn btn--ghost" href="pricing.html">Chọn gói khác</a>
      </div>
    </div>

    <!-- pending -->
    <div class="card only-pending">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">⏳</div>
          <h1 class="state-block__title">Đang đối soát giao dịch</h1>
          <p class="state-block__desc">Cổng thanh toán chưa trả kết quả cuối. Trang sẽ tự kiểm tra lại sau <strong>5 giây</strong>.</p>
        </div>
        <div class="alert alert--info">
          Nếu đã bị trừ tiền, lượt thi sẽ được cộng tự động trong vài phút. Không thanh toán lại để tránh trừ tiền hai lần.
        </div>
        <div class="progress"><div class="progress__bar" style="width:40%"></div></div>
      </div>
      <div class="card__footer row">
        <a class="btn" href="payment-result.html?state=pending">Kiểm tra lại ngay</a>
        <a class="btn btn--ghost" href="wallet.html">Xem ví lượt thi</a>
      </div>
    </div>

    <!-- error -->
    <div class="card only-error">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">!</div>
          <h1 class="state-block__title">Không đọc được trạng thái đơn hàng</h1>
          <p class="state-block__desc">Đơn DH-20260920-0148 vẫn tồn tại. Hãy mở ví lượt thi để xem trạng thái mới nhất.</p>
        </div>
      </div>
      <div class="card__footer row">
        <a class="btn btn--primary" href="wallet.html">Mở ví lượt thi</a>
      </div>
    </div>
  </div>
</div>
```

- [ ] **Step 3: Tạo `payment/wallet.html`**

Có shell học viên, sidebar active "Ví lượt thi". `<body data-states="loading,empty,error,paid" data-state="paid">`.

```html
<div class="container">
  <div class="stack">
    <div class="row row--between">
      <h1>Ví lượt thi</h1>
      <a class="btn btn--primary" href="pricing.html">Nạp thêm lượt</a>
    </div>

    <div class="grid-3 only-paid">
      <div class="stat">
        <div class="stat__label">Lượt thi còn lại</div>
        <div class="stat__value">3</div>
        <div class="stat__hint">Không có ngày hết hạn</div>
      </div>
      <div class="stat">
        <div class="stat__label">Đã dùng</div>
        <div class="stat__value">6</div>
        <div class="stat__hint">6 đề full test</div>
      </div>
      <div class="stat">
        <div class="stat__label">Tổng đã nạp</div>
        <div class="stat__value">548.000 ₫</div>
        <div class="stat__hint">3 đơn hàng</div>
      </div>
    </div>

    <!-- paid: có lịch sử giao dịch -->
    <div class="card only-paid">
      <div class="card__header"><strong>Lịch sử giao dịch</strong></div>
      <div class="card__body">
        <table class="table table--striped">
          <thead>
            <tr>
              <th scope="col">Mã đơn</th>
              <th scope="col">Gói</th>
              <th scope="col">Số tiền</th>
              <th scope="col">Phương thức</th>
              <th scope="col">Thời gian</th>
              <th scope="col">Trạng thái</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>DH-20260920-0148</td><td>Gói Ôn Thi (5 lượt)</td><td>199.000 ₫</td>
              <td>VNPay</td><td>20/09/2026 14:32</td>
              <td><span class="badge badge--success">Đã thanh toán</span></td>
            </tr>
            <tr>
              <td>DH-20260915-0092</td><td>Gói Lẻ (1 lượt)</td><td>49.000 ₫</td>
              <td>MoMo</td><td>15/09/2026 09:10</td>
              <td><span class="badge badge--warning">Đang đối soát</span></td>
            </tr>
            <tr>
              <td>DH-20260903-0031</td><td>Gói Cấp Tốc (10 lượt)</td><td>349.000 ₫</td>
              <td>VNPay</td><td>03/09/2026 20:44</td>
              <td><span class="badge badge--success">Đã thanh toán</span></td>
            </tr>
            <tr>
              <td>DH-20260901-0007</td><td>Gói Lẻ (1 lượt)</td><td>49.000 ₫</td>
              <td>VNPay</td><td>01/09/2026 11:02</td>
              <td><span class="badge badge--danger">Thất bại</span></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- empty -->
    <div class="card only-empty">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">₫</div>
          <h2 class="state-block__title">Chưa có giao dịch nào</h2>
          <p class="state-block__desc">Nạp lượt thi để làm đề full test. Luyện tập theo part vẫn miễn phí.</p>
          <a class="btn btn--primary" href="pricing.html">Xem bảng giá</a>
        </div>
      </div>
    </div>

    <!-- loading -->
    <div class="card only-loading">
      <div class="card__body stack">
        <div class="skeleton skeleton--title"></div>
        <div class="skeleton skeleton--text"></div>
        <div class="skeleton skeleton--text"></div>
        <div class="skeleton skeleton--block"></div>
      </div>
    </div>

    <!-- error -->
    <div class="card only-error">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">!</div>
          <h2 class="state-block__title">Không tải được ví lượt thi</h2>
          <p class="state-block__desc">Số lượt thi hiển thị có thể không chính xác. Thử tải lại trang.</p>
          <button class="btn">Tải lại</button>
        </div>
      </div>
    </div>
  </div>
</div>
```

Lưu ý: khối `.grid-3` dùng `.only-paid` chứ không phải `.multi-state` — ở state `empty` người dùng chưa từng nạp, nên ba ô thống kê không có gì để hiển thị và phải ẩn hoàn toàn.

- [ ] **Step 4: Verify trên browser**

Chạy `python -m http.server 8080` trong `prototype/`:
- `payment/payment-result.html` với `?state=success`, `?state=failed`, `?state=pending`, `?state=error` — mỗi state chỉ hiện **một** thẻ `.card`, không bị hiện hai thẻ cùng lúc.
- Ở `pending` xác nhận có câu cảnh báo "Không thanh toán lại để tránh trừ tiền hai lần".
- `payment/wallet.html` với `?state=loading`, `?state=empty`, `?state=error`, `?state=paid` — ở `empty` không thấy ô `.stat` nào.
- Đi hết luồng: `pricing.html` → `checkout.html` → `gateway-mock.html` → `payment-result.html?state=success` → `wallet.html`, mọi link đều mở được, không 404.
Không có lỗi console. Đây là gate của task, không được bỏ.

- [ ] **Step 5: Commit**

```bash
git add prototype/assets/css/base.css prototype/payment/payment-result.html prototype/payment/wallet.html
git commit -m "feat: add payment result and credit wallet screens"
```

### Task 13: Shell admin, dashboard và danh sách đề

**Files:**
- Create: `prototype/admin/dashboard.html`
- Create: `prototype/admin/exam-list.html`
- Modify: `prototype/assets/css/components.css` (thêm `.toolbar`, `.pagination`)

**Interfaces:**
- Consumes: `.app-shell--with-sidebar` `.nav-list` `.stat` `.table` `.badge` `.skeleton` `.state-block` (Task 1, 2, 4)
- Produces: **shell admin** — 6 trang admin còn lại copy nguyên topbar + sidebar này; `.toolbar` và `.pagination` dùng ở `admin/users.html` và `admin/orders.html` (Task 15)

Shell admin khác shell học viên ở hai điểm: topbar ghi "Khu vực quản trị" thay vì badge lượt thi, và sidebar có nhóm mục riêng.

State `dashboard.html`: `loading`, `error`, `success`.
State `exam-list.html`: `loading`, `empty`, `error`, `success`.

- [ ] **Step 1: Thêm `.toolbar` và `.pagination` vào `components.css`**

```css
/* ===== Thanh công cụ danh sách ===== */
.toolbar {
  display: flex; flex-wrap: wrap; gap: var(--space-3); align-items: flex-end;
  padding: var(--space-4);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}
.toolbar__grow { flex: 1 1 220px; }
.toolbar .field { margin: 0; }

/* ===== Phân trang ===== */
.pagination { display: flex; gap: var(--space-2); align-items: center; }
.pagination__info {
  margin-right: auto;
  font-size: var(--text-sm); color: var(--color-text-muted);
}
.pagination__page {
  display: inline-flex; align-items: center; justify-content: center;
  min-width: 34px; height: 34px; padding: 0 var(--space-2);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface);
  color: var(--color-text);
  font-size: var(--text-sm); text-decoration: none;
}
.pagination__page.is-current {
  background: var(--color-primary);
  border-color: var(--color-primary);
  color: var(--color-primary-text); font-weight: 600;
}
.pagination__page[aria-disabled="true"] {
  color: var(--color-text-muted); pointer-events: none; opacity: .6;
}
```

- [ ] **Step 2: Tạo shell admin trong `admin/dashboard.html`**

`<body data-states="loading,error,success" data-state="success">`. Shell này là bản gốc, 6 trang admin sau copy lại nguyên khối `.app-topbar` + `.app-sidebar`.

```html
<div class="app-shell app-shell--with-sidebar">
  <header class="app-topbar">
    <div class="row row--between">
      <strong>TOEIC Practice — Khu vực quản trị</strong>
      <div class="row">
        <span class="badge badge--warning">Admin</span>
        <span class="text-sm">admin@toeic.local</span>
        <a class="btn btn--sm btn--ghost" href="../auth/login.html">Đăng xuất</a>
      </div>
    </div>
  </header>

  <nav class="app-sidebar" aria-label="Điều hướng quản trị">
    <ul class="nav-list">
      <li class="nav-list__group">Tổng quan</li>
      <li><a class="nav-list__item is-active" href="dashboard.html">Bảng điều khiển</a></li>
      <li class="nav-list__group">Nội dung</li>
      <li><a class="nav-list__item" href="exam-list.html">Đề thi</a></li>
      <li><a class="nav-list__item" href="score-conversion.html">Bảng quy đổi điểm</a></li>
      <li class="nav-list__group">Người dùng &amp; doanh thu</li>
      <li><a class="nav-list__item" href="users.html">Học viên</a></li>
      <li><a class="nav-list__item" href="packages.html">Gói lượt thi</a></li>
      <li><a class="nav-list__item" href="orders.html">Đơn hàng</a></li>
      <li class="nav-list__group">Khác</li>
      <li><a class="nav-list__item" href="../index.html">Mục lục prototype</a></li>
    </ul>
  </nav>

  <main class="app-main">
    <!-- nội dung từng trang đặt ở đây -->
  </main>
</div>
```

Nội dung `main` của dashboard:

```html
<div class="container">
  <div class="stack">
    <h1>Bảng điều khiển</h1>

    <div class="grid-3 only-success">
      <div class="stat">
        <div class="stat__label">Học viên đang hoạt động</div>
        <div class="stat__value">1.284</div>
        <div class="stat__hint">+47 trong 7 ngày</div>
      </div>
      <div class="stat">
        <div class="stat__label">Lượt thi đã dùng (tháng này)</div>
        <div class="stat__value">3.912</div>
        <div class="stat__hint">Trung bình 130 lượt/ngày</div>
      </div>
      <div class="stat">
        <div class="stat__label">Doanh thu tháng này</div>
        <div class="stat__value">64.7 tr ₫</div>
        <div class="stat__hint">328 đơn đã thanh toán</div>
      </div>
    </div>

    <div class="grid-2 only-success">
      <div class="card">
        <div class="card__header"><strong>Đề thi được làm nhiều nhất</strong></div>
        <div class="card__body">
          <table class="table table--striped">
            <thead><tr><th scope="col">Đề</th><th scope="col">Lượt làm</th><th scope="col">Điểm TB</th></tr></thead>
            <tbody>
              <tr><td>ETS 2024 — Test 1</td><td>612</td><td>705</td></tr>
              <tr><td>ETS 2024 — Test 2</td><td>548</td><td>688</td></tr>
              <tr><td>ETS 2023 — Test 5</td><td>431</td><td>722</td></tr>
              <tr><td>Economy Vol.5 — Test 3</td><td>287</td><td>651</td></tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="card">
        <div class="card__header"><strong>Cần xử lý</strong></div>
        <div class="card__body stack">
          <div class="alert alert--warning">2 đề đang ở trạng thái nháp, thiếu file audio Part 3.</div>
          <div class="alert alert--info">5 đơn hàng đang chờ đối soát quá 30 phút.</div>
          <div class="alert alert--info">1 học viên báo lỗi câu 137 của ETS 2024 — Test 2.</div>
        </div>
        <div class="card__footer row">
          <a class="btn" href="exam-list.html">Mở danh sách đề</a>
          <a class="btn btn--ghost" href="orders.html">Mở đơn hàng</a>
        </div>
      </div>
    </div>

    <div class="card only-loading">
      <div class="card__body stack">
        <div class="skeleton skeleton--title"></div>
        <div class="skeleton skeleton--text"></div>
        <div class="skeleton skeleton--block"></div>
      </div>
    </div>

    <div class="card only-error">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">!</div>
          <h2 class="state-block__title">Không tải được số liệu</h2>
          <p class="state-block__desc">Các số liệu tổng quan tạm thời không khả dụng.</p>
          <button class="btn">Tải lại</button>
        </div>
      </div>
    </div>
  </div>
</div>
```

- [ ] **Step 3: Tạo `admin/exam-list.html`**

Copy shell admin từ Step 2, đổi mục active sang "Đề thi". `<body data-states="loading,empty,error,success" data-state="success">`.

```html
<div class="container">
  <div class="stack">
    <div class="row row--between">
      <h1>Đề thi</h1>
      <a class="btn btn--primary" href="exam-editor.html">Tạo đề mới</a>
    </div>

    <div class="toolbar only-success">
      <div class="field toolbar__grow">
        <label class="field__label" for="q">Tìm theo tên đề</label>
        <input class="field__input" id="q" type="search" placeholder="ETS 2024…">
      </div>
      <div class="field">
        <label class="field__label" for="f-status">Trạng thái</label>
        <select class="field__input" id="f-status">
          <option>Tất cả</option>
          <option>Đã phát hành</option>
          <option>Nháp</option>
          <option>Đã ẩn</option>
        </select>
      </div>
      <button class="btn">Lọc</button>
    </div>

    <div class="card only-success">
      <div class="card__body">
        <table class="table table--striped">
          <thead>
            <tr>
              <th scope="col">Tên đề</th>
              <th scope="col">Số câu</th>
              <th scope="col">Audio</th>
              <th scope="col">Lượt làm</th>
              <th scope="col">Cập nhật</th>
              <th scope="col">Trạng thái</th>
              <th scope="col">Thao tác</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>ETS 2024 — Test 1</td><td>200/200</td><td>Đủ 4 part</td><td>612</td><td>18/09/2026</td>
              <td><span class="badge badge--success">Đã phát hành</span></td>
              <td class="row">
                <a class="btn btn--sm" href="exam-editor.html">Sửa</a>
                <a class="btn btn--sm btn--ghost" href="question-editor.html">Câu hỏi</a>
              </td>
            </tr>
            <tr>
              <td>ETS 2024 — Test 3</td><td>186/200</td><td>Thiếu Part 3</td><td>0</td><td>20/09/2026</td>
              <td><span class="badge badge--warning">Nháp</span></td>
              <td class="row">
                <a class="btn btn--sm" href="exam-editor.html">Sửa</a>
                <a class="btn btn--sm btn--ghost" href="question-editor.html">Câu hỏi</a>
              </td>
            </tr>
            <tr>
              <td>Economy Vol.5 — Test 3</td><td>200/200</td><td>Đủ 4 part</td><td>287</td><td>02/09/2026</td>
              <td><span class="badge badge--success">Đã phát hành</span></td>
              <td class="row">
                <a class="btn btn--sm" href="exam-editor.html">Sửa</a>
                <a class="btn btn--sm btn--ghost" href="question-editor.html">Câu hỏi</a>
              </td>
            </tr>
            <tr>
              <td>Đề thử nội bộ 2025</td><td>200/200</td><td>Đủ 4 part</td><td>14</td><td>11/08/2026</td>
              <td><span class="badge">Đã ẩn</span></td>
              <td class="row">
                <a class="btn btn--sm" href="exam-editor.html">Sửa</a>
                <a class="btn btn--sm btn--ghost" href="question-editor.html">Câu hỏi</a>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="card__footer">
        <div class="pagination">
          <span class="pagination__info">Hiển thị 1–4 trong 12 đề</span>
          <a class="pagination__page" href="#" aria-disabled="true">Trước</a>
          <a class="pagination__page is-current" href="#" aria-current="page">1</a>
          <a class="pagination__page" href="#">2</a>
          <a class="pagination__page" href="#">3</a>
          <a class="pagination__page" href="#">Sau</a>
        </div>
      </div>
    </div>

    <div class="card only-empty">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">+</div>
          <h2 class="state-block__title">Chưa có đề thi nào</h2>
          <p class="state-block__desc">Tạo đề đầu tiên rồi nhập 200 câu theo từng part.</p>
          <a class="btn btn--primary" href="exam-editor.html">Tạo đề mới</a>
        </div>
      </div>
    </div>

    <div class="card only-loading">
      <div class="card__body stack">
        <div class="skeleton skeleton--title"></div>
        <div class="skeleton skeleton--text"></div>
        <div class="skeleton skeleton--text"></div>
        <div class="skeleton skeleton--block"></div>
      </div>
    </div>

    <div class="card only-error">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">!</div>
          <h2 class="state-block__title">Không tải được danh sách đề</h2>
          <p class="state-block__desc">Thử tải lại trang.</p>
          <button class="btn">Tải lại</button>
        </div>
      </div>
    </div>
  </div>
</div>
```

- [ ] **Step 4: Verify trên browser**

Chạy `python -m http.server 8080` trong `prototype/`:
- `admin/dashboard.html` với `?state=loading`, `?state=error`, `?state=success`.
- `admin/exam-list.html` với cả 4 state — ở `success` kiểm tra hàng "ETS 2024 — Test 3" hiện badge Nháp và cột Audio ghi "Thiếu Part 3"; ở `empty` không còn `.toolbar`.
- Bấm các mục sidebar admin: chỉ `dashboard.html` và `exam-list.html` tồn tại, các link khác sẽ 404 cho tới Task 14–15 — điều này là bình thường ở bước này.
Không có lỗi console. Đây là gate của task, không được bỏ.

- [ ] **Step 5: Commit**

```bash
git add prototype/assets/css/components.css prototype/admin/dashboard.html prototype/admin/exam-list.html
git commit -m "feat: add admin shell, dashboard and exam list screens"
```

### Task 14: Soạn đề — wizard tạo đề và trình soạn câu hỏi

**Files:**
- Create: `prototype/admin/exam-editor.html`
- Create: `prototype/admin/question-editor.html`
- Modify: `prototype/assets/css/components.css` (thêm `.upload`)
- Modify: `prototype/assets/css/base.css` (thêm state `step2`, `step3`)

**Interfaces:**
- Consumes: shell admin (Task 13), `.stepper` `.field` `.option-list` `.alert` `.table` `.badge` `.progress`
- Produces: `.upload` / `.upload__zone` / `.upload__file` — dùng lại ở `question-editor.html` cho ảnh Part 1

Đây là hai màn admin nặng nhất. `exam-editor.html` là wizard 3 bước; `question-editor.html` phải soạn được **cả câu 3 đáp án (Part 2) và câu 4 đáp án**, đúng Global Constraint về số lượng option động.

State `exam-editor.html`: `step1` dùng luôn `success` làm bước 1, `step2`, `step3`, `submitting`, `error`.
State `question-editor.html`: `success`, `submitting`, `error`.

- [ ] **Step 1: Thêm state mới vào `base.css`**

```css
body[data-state="step2"] .only-step2,
body[data-state="step3"] .only-step3 { display: revert; }
```

Và bổ sung vào danh sách `.multi-state`:

```css
body[data-state="step2"] .multi-state[data-show~="step2"],
body[data-state="step3"] .multi-state[data-show~="step3"] { display: revert; }
```

- [ ] **Step 2: Thêm `.upload` vào `components.css`**

```css
/* ===== Vùng tải file ===== */
.upload { display: flex; flex-direction: column; gap: var(--space-2); }
.upload__zone {
  display: flex; flex-direction: column; align-items: center; gap: var(--space-2);
  padding: var(--space-6);
  border: 2px dashed var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  color: var(--color-text-muted);
  font-size: var(--text-sm); text-align: center;
}
.upload__file {
  display: flex; align-items: center; justify-content: space-between; gap: var(--space-3);
  padding: var(--space-2) var(--space-3);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface);
  font-size: var(--text-sm);
}
.upload__file--missing {
  border-color: var(--color-danger);
  color: var(--color-danger);
}
```

- [ ] **Step 3: Tạo `admin/exam-editor.html` — bước 1 và khung wizard**

Copy shell admin, mục active "Đề thi". `<body data-states="success,step2,step3,submitting,error" data-state="success">`.

```html
<div class="container">
  <div class="stack">
    <h1>Tạo đề thi mới</h1>

    <div class="stepper only-success">
      <div class="stepper__item is-current">1. Thông tin đề</div>
      <div class="stepper__item">2. Tải audio Listening</div>
      <div class="stepper__item">3. Nhập câu hỏi</div>
    </div>

    <div class="alert alert--danger only-error">
      Không lưu được đề. Tên đề đã tồn tại trong hệ thống.
    </div>

    <!-- Bước 1 -->
    <div class="card only-success">
      <div class="card__header"><strong>Thông tin đề</strong></div>
      <div class="card__body stack">
        <div class="field">
          <label class="field__label" for="e-name">Tên đề</label>
          <input class="field__input" id="e-name" value="ETS 2024 — Test 4">
          <span class="field__hint">Tên hiển thị cho học viên trong danh sách đề.</span>
        </div>
        <div class="grid-2">
          <div class="field">
            <label class="field__label" for="e-source">Bộ đề / nguồn</label>
            <input class="field__input" id="e-source" value="ETS 2024">
          </div>
          <div class="field">
            <label class="field__label" for="e-status">Trạng thái</label>
            <select class="field__input" id="e-status">
              <option selected>Nháp</option>
              <option>Đã phát hành</option>
              <option>Đã ẩn</option>
            </select>
          </div>
        </div>
        <div class="field">
          <label class="field__label" for="e-note">Ghi chú nội bộ</label>
          <textarea class="field__input" id="e-note" rows="3">Đề dùng cho đợt ôn tháng 10.</textarea>
        </div>
        <div class="alert alert--info">
          Cấu trúc đề cố định: 200 câu, Listening 45 phút, Reading 75 phút. Không sửa được số câu từng part.
        </div>
      </div>
      <div class="card__footer row row--between">
        <a class="btn btn--ghost" href="exam-list.html">Huỷ</a>
        <a class="btn btn--primary" href="exam-editor.html?state=step2">Tiếp tục: tải audio</a>
      </div>
    </div>

- [ ] **Step 4: Thêm bước 2 (tải audio) vào `exam-editor.html`**

Chèn ngay sau khối `.card.only-success` của bước 1, vẫn trong cùng `.stack`. Ở bước này `.stepper` của bước 1 không đổi được bằng CSS thuần, nên **mỗi bước có `.stepper` riêng** — bọc `.stepper` của bước 1 bằng `.only-success` và thêm hai `.stepper` nữa cho `step2`, `step3`.

```html
<div class="stepper only-step2">
  <div class="stepper__item is-done">1. Thông tin đề</div>
  <div class="stepper__item is-current">2. Tải audio Listening</div>
  <div class="stepper__item">3. Nhập câu hỏi</div>
</div>

<div class="card only-step2">
  <div class="card__header"><strong>Audio cho 4 part Listening</strong></div>
  <div class="card__body stack">
    <div class="alert alert--warning">
      Học viên chỉ được nghe <strong>một lần</strong>, không tua lại. File phải đúng thứ tự câu, chèn sẵn khoảng lặng giữa các câu.
    </div>

    <div class="upload">
      <span class="field__label">Part 1 — 6 câu (hình ảnh)</span>
      <div class="upload__file">
        <span>part1.mp3 — 3:12 — 2.9 MB</span>
        <button class="btn btn--sm btn--ghost">Thay file</button>
      </div>
    </div>

    <div class="upload">
      <span class="field__label">Part 2 — 25 câu (hỏi đáp, 3 đáp án)</span>
      <div class="upload__file">
        <span>part2.mp3 — 9:48 — 8.7 MB</span>
        <button class="btn btn--sm btn--ghost">Thay file</button>
      </div>
    </div>

    <div class="upload">
      <span class="field__label">Part 3 — 39 câu (13 hội thoại)</span>
      <div class="upload__file upload__file--missing">
        <span>Chưa có file</span>
        <button class="btn btn--sm">Chọn file</button>
      </div>
      <div class="upload__zone">
        <strong>Kéo file .mp3 vào đây</strong>
        <span>Tối đa 60 MB. Chỉ nhận .mp3 hoặc .m4a.</span>
        <button class="btn btn--sm">Chọn từ máy</button>
      </div>
    </div>

    <div class="upload">
      <span class="field__label">Part 4 — 30 câu (10 bài nói)</span>
      <div class="upload__file">
        <span>part4.mp3 — 14:05 — 12.4 MB</span>
        <button class="btn btn--sm btn--ghost">Thay file</button>
      </div>
    </div>

    <div>
      <div class="row row--between text-sm">
        <span>Đã tải 3 / 4 file</span><span>75%</span>
      </div>
      <div class="progress"><div class="progress__bar" style="width:75%"></div></div>
    </div>
  </div>
  <div class="card__footer row row--between">
    <a class="btn btn--ghost" href="exam-editor.html?state=success">Quay lại</a>
    <a class="btn btn--primary" href="exam-editor.html?state=step3">Tiếp tục: nhập câu hỏi</a>
  </div>
</div>
```

- [ ] **Step 5: Thêm bước 3 và state `submitting` vào `exam-editor.html`**

Bước 3 không nhập câu trực tiếp — nó là bảng tiến độ 7 part, mỗi part mở sang `question-editor.html`.

```html
<div class="stepper only-step3">
  <div class="stepper__item is-done">1. Thông tin đề</div>
  <div class="stepper__item is-done">2. Tải audio Listening</div>
  <div class="stepper__item is-current">3. Nhập câu hỏi</div>
</div>

<div class="card only-step3">
  <div class="card__header"><strong>Tiến độ nhập câu hỏi</strong></div>
  <div class="card__body">
    <table class="table table--striped">
      <thead>
        <tr>
          <th scope="col">Part</th><th scope="col">Dạng</th><th scope="col">Số đáp án</th>
          <th scope="col">Tiến độ</th><th scope="col">Trạng thái</th><th scope="col">Thao tác</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td>Part 1</td><td>Mô tả hình ảnh</td><td>4</td><td>6 / 6</td>
          <td><span class="badge badge--success">Đủ</span></td>
          <td><a class="btn btn--sm" href="question-editor.html">Sửa</a></td>
        </tr>
        <tr>
          <td>Part 2</td><td>Hỏi đáp</td><td><strong>3</strong></td><td>25 / 25</td>
          <td><span class="badge badge--success">Đủ</span></td>
          <td><a class="btn btn--sm" href="question-editor.html">Sửa</a></td>
        </tr>
        <tr>
          <td>Part 3</td><td>Hội thoại (13 bài)</td><td>4</td><td>25 / 39</td>
          <td><span class="badge badge--warning">Thiếu 14 câu</span></td>
          <td><a class="btn btn--sm" href="question-editor.html">Nhập tiếp</a></td>
        </tr>
        <tr>
          <td>Part 4</td><td>Bài nói (10 bài)</td><td>4</td><td>30 / 30</td>
          <td><span class="badge badge--success">Đủ</span></td>
          <td><a class="btn btn--sm" href="question-editor.html">Sửa</a></td>
        </tr>
        <tr>
          <td>Part 5</td><td>Điền câu</td><td>4</td><td>30 / 30</td>
          <td><span class="badge badge--success">Đủ</span></td>
          <td><a class="btn btn--sm" href="question-editor.html">Sửa</a></td>
        </tr>
        <tr>
          <td>Part 6</td><td>Điền đoạn (4 đoạn)</td><td>4</td><td>16 / 16</td>
          <td><span class="badge badge--success">Đủ</span></td>
          <td><a class="btn btn--sm" href="question-editor.html">Sửa</a></td>
        </tr>
        <tr>
          <td>Part 7</td><td>Đọc hiểu</td><td>4</td><td>54 / 54</td>
          <td><span class="badge badge--success">Đủ</span></td>
          <td><a class="btn btn--sm" href="question-editor.html">Sửa</a></td>
        </tr>
      </tbody>
    </table>
    <div class="alert alert--warning">
      Còn thiếu 14 câu Part 3 và 1 file audio. Chưa thể phát hành đề.
    </div>
  </div>
  <div class="card__footer row row--between">
    <a class="btn btn--ghost" href="exam-editor.html?state=step2">Quay lại</a>
    <div class="row">
      <a class="btn" href="exam-list.html">Lưu nháp</a>
      <button class="btn btn--primary" disabled>Phát hành đề</button>
    </div>
  </div>
</div>

<div class="card only-submitting">
  <div class="card__body">
    <div class="state-block">
      <div class="state-block__icon" aria-hidden="true">⏳</div>
      <h2 class="state-block__title">Đang lưu đề…</h2>
      <p class="state-block__desc">Không đóng tab trong lúc tải audio lên.</p>
    </div>
    <div class="progress"><div class="progress__bar" style="width:60%"></div></div>
  </div>
</div>
```

- [ ] **Step 6: Tạo `admin/question-editor.html`**

Copy shell admin, mục active "Đề thi". `<body data-states="success,submitting,error" data-state="success">`.

Màn này chứng minh **số đáp án động**: đặt cạnh nhau một câu Part 2 (3 đáp án A/B/C) và một câu Part 1 (4 đáp án + ảnh).

```html
<div class="container">
  <div class="stack">
    <div class="row row--between">
      <div>
        <h1>Soạn câu hỏi</h1>
        <p class="text-muted">ETS 2024 — Test 4</p>
      </div>
      <a class="btn btn--ghost" href="exam-editor.html?state=step3">Về tiến độ đề</a>
    </div>

    <div class="toolbar">
      <div class="field">
        <label class="field__label" for="q-part">Part</label>
        <select class="field__input" id="q-part">
          <option>Part 1 — 6 câu</option>
          <option selected>Part 2 — 25 câu</option>
          <option>Part 3 — 39 câu</option>
          <option>Part 4 — 30 câu</option>
          <option>Part 5 — 30 câu</option>
          <option>Part 6 — 16 câu</option>
          <option>Part 7 — 54 câu</option>
        </select>
      </div>
      <div class="field">
        <label class="field__label" for="q-num">Câu số</label>
        <input class="field__input" id="q-num" type="number" value="12" min="7" max="31">
      </div>
      <button class="btn">Mở câu</button>
    </div>

    <div class="alert alert--danger only-error">
      Không lưu được câu 12. Chưa chọn đáp án đúng.
    </div>

    <!-- Câu Part 2: đúng 3 đáp án -->
    <div class="card only-success">
      <div class="card__header row row--between">
        <strong>Câu 12 — Part 2 (Hỏi đáp)</strong>
        <span class="badge badge--info">3 đáp án A/B/C</span>
      </div>
      <div class="card__body stack">
        <div class="alert alert--info">
          Part 2 chỉ có 3 đáp án. Không thêm được đáp án D.
        </div>
        <div class="field">
          <label class="field__label" for="q12-script">Lời thoại câu hỏi (audio script)</label>
          <textarea class="field__input" id="q12-script" rows="2">When will the shipment arrive at the warehouse?</textarea>
        </div>
        <fieldset>
          <legend class="field__label">Đáp án</legend>
          <div class="stack">
            <div class="row">
              <span class="option__marker">A</span>
              <input class="field__input toolbar__grow" value="Sometime next Tuesday." aria-label="Nội dung đáp án A">
              <label class="text-sm"><input type="radio" name="q12-correct" checked> Đúng</label>
            </div>
            <div class="row">
              <span class="option__marker">B</span>
              <input class="field__input toolbar__grow" value="At the loading dock." aria-label="Nội dung đáp án B">
              <label class="text-sm"><input type="radio" name="q12-correct"> Đúng</label>
            </div>
            <div class="row">
              <span class="option__marker">C</span>
              <input class="field__input toolbar__grow" value="Yes, I signed for it." aria-label="Nội dung đáp án C">
              <label class="text-sm"><input type="radio" name="q12-correct"> Đúng</label>
            </div>
          </div>
        </fieldset>
        <div class="field">
          <label class="field__label" for="q12-explain">Giải thích</label>
          <textarea class="field__input" id="q12-explain" rows="2">Câu hỏi "When" cần đáp án chỉ thời gian. B chỉ địa điểm, C là câu trả lời Yes/No không phù hợp với câu hỏi Wh-.</textarea>
        </div>
      </div>
      <div class="card__footer row row--between">
        <button class="btn btn--danger btn--ghost">Xoá câu</button>
        <div class="row">
          <button class="btn">Lưu và câu trước</button>
          <button class="btn btn--primary">Lưu và câu sau</button>
        </div>
      </div>
    </div>

    <!-- Câu Part 1: 4 đáp án + ảnh -->
    <div class="card only-success">
      <div class="card__header row row--between">
        <strong>Câu 3 — Part 1 (Mô tả hình ảnh)</strong>
        <span class="badge badge--info">4 đáp án A/B/C/D</span>
      </div>
      <div class="card__body stack">
        <div class="upload">
          <span class="field__label">Ảnh của câu</span>
          <div class="upload__file">
            <span>part1-q3.jpg — 840×560 — 156 KB</span>
            <button class="btn btn--sm btn--ghost">Thay ảnh</button>
          </div>
          <img class="question__image" src="../assets/img/part1-sample.svg" alt="Ảnh minh hoạ câu 3 Part 1" width="320">
        </div>
        <div class="field">
          <label class="field__label" for="q3-alt">Văn bản thay thế cho ảnh (alt)</label>
          <input class="field__input" id="q3-alt" value="Hai người đang xem tài liệu trong phòng họp">
          <span class="field__hint">Bắt buộc, dùng cho học viên khiếm thị.</span>
        </div>
        <fieldset>
          <legend class="field__label">Đáp án</legend>
          <div class="stack">
            <div class="row">
              <span class="option__marker">A</span>
              <input class="field__input toolbar__grow" value="They are reviewing a document." aria-label="Nội dung đáp án A">
              <label class="text-sm"><input type="radio" name="q3-correct" checked> Đúng</label>
            </div>
            <div class="row">
              <span class="option__marker">B</span>
              <input class="field__input toolbar__grow" value="A man is closing a window." aria-label="Nội dung đáp án B">
              <label class="text-sm"><input type="radio" name="q3-correct"> Đúng</label>
            </div>
            <div class="row">
              <span class="option__marker">C</span>
              <input class="field__input toolbar__grow" value="They are boarding a train." aria-label="Nội dung đáp án C">
              <label class="text-sm"><input type="radio" name="q3-correct"> Đúng</label>
            </div>
            <div class="row">
              <span class="option__marker">D</span>
              <input class="field__input toolbar__grow" value="A woman is watering plants." aria-label="Nội dung đáp án D">
              <label class="text-sm"><input type="radio" name="q3-correct"> Đúng</label>
            </div>
          </div>
        </fieldset>
      </div>
      <div class="card__footer row row--between">
        <button class="btn btn--danger btn--ghost">Xoá câu</button>
        <div class="row">
          <button class="btn">Lưu và câu trước</button>
          <button class="btn btn--primary">Lưu và câu sau</button>
        </div>
      </div>
    </div>

    <div class="card only-submitting">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">⏳</div>
          <h2 class="state-block__title">Đang lưu câu hỏi…</h2>
        </div>
      </div>
    </div>
  </div>
</div>
```

- [ ] **Step 7: Verify trên browser**

Chạy `python -m http.server 8080` trong `prototype/`:
- `admin/exam-editor.html` với `?state=success`, `?state=step2`, `?state=step3`, `?state=submitting`, `?state=error` — mỗi state chỉ hiện **một** `.stepper` và **một** `.card` chính; ở `step2` file Part 3 phải có viền đỏ; ở `step3` nút "Phát hành đề" phải `disabled`.
- Bấm "Tiếp tục" / "Quay lại" đi hết 3 bước bằng link.
- `admin/question-editor.html` với cả 3 state — đếm kỹ: câu Part 2 có **đúng 3** ô đáp án, câu Part 1 có **đúng 4**, và ảnh Part 1 hiện được.
Không có lỗi console. Đây là gate của task, không được bỏ.

- [ ] **Step 8: Commit**

```bash
git add prototype/assets/css/base.css prototype/assets/css/components.css prototype/admin/exam-editor.html prototype/admin/question-editor.html
git commit -m "feat: add exam wizard and question editor screens"
```

### Task 15: Quản trị học viên và bảng quy đổi điểm

**Files:**
- Create: `prototype/admin/users.html`
- Create: `prototype/admin/score-conversion.html`

**Interfaces:**
- Consumes: shell admin (Task 13), `.toolbar` `.pagination` `.table` `.badge` `.modal-overlay` `.modal` `.alert` `.skeleton` `.state-block`
- Produces: không có class mới

State `users.html`: `loading`, `empty`, `error`, `success`, `adjust` (mở modal cộng/trừ lượt thi).
State `score-conversion.html`: `loading`, `error`, `success`, `submitting`.

`score-conversion.html` tồn tại vì điểm TOEIC **không quy đổi tuyến tính** — số câu đúng đổi sang thang 5–495 theo bảng tra, và admin phải sửa được bảng đó.

- [ ] **Step 1: Thêm state `adjust` vào `base.css`**

```css
body[data-state="adjust"] .only-adjust { display: revert; }
body[data-state="adjust"] .multi-state[data-show~="adjust"] { display: revert; }
```

- [ ] **Step 2: Tạo `admin/users.html`**

Copy shell admin, mục active "Học viên". `<body data-states="loading,empty,error,success,adjust" data-state="success">`.

```html
<div class="container">
  <div class="stack">
    <h1>Học viên</h1>

    <div class="toolbar multi-state" data-show="success adjust">
      <div class="field toolbar__grow">
        <label class="field__label" for="u-q">Tìm theo email hoặc tên</label>
        <input class="field__input" id="u-q" type="search" placeholder="an.nguyen@…">
      </div>
      <div class="field">
        <label class="field__label" for="u-status">Trạng thái</label>
        <select class="field__input" id="u-status">
          <option>Tất cả</option>
          <option>Đang hoạt động</option>
          <option>Đã khoá</option>
        </select>
      </div>
      <button class="btn">Lọc</button>
    </div>

    <div class="card multi-state" data-show="success adjust">
      <div class="card__body">
        <table class="table table--striped">
          <thead>
            <tr>
              <th scope="col">Học viên</th><th scope="col">Email</th>
              <th scope="col">Lượt còn</th><th scope="col">Đề đã làm</th>
              <th scope="col">Điểm cao nhất</th><th scope="col">Đăng nhập gần nhất</th>
              <th scope="col">Trạng thái</th><th scope="col">Thao tác</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Nguyễn Văn An</td><td>an.nguyen@gmail.com</td><td>3</td><td>6</td><td>745</td>
              <td>20/09/2026 13:10</td>
              <td><span class="badge badge--success">Hoạt động</span></td>
              <td class="row">
                <a class="btn btn--sm" href="users.html?state=adjust">Sửa lượt</a>
                <button class="btn btn--sm btn--ghost">Khoá</button>
              </td>
            </tr>
            <tr>
              <td>Trần Thị Bình</td><td>binh.tran@gmail.com</td><td>0</td><td>12</td><td>820</td>
              <td>19/09/2026 21:44</td>
              <td><span class="badge badge--success">Hoạt động</span></td>
              <td class="row">
                <a class="btn btn--sm" href="users.html?state=adjust">Sửa lượt</a>
                <button class="btn btn--sm btn--ghost">Khoá</button>
              </td>
            </tr>
            <tr>
              <td>Lê Minh Cường</td><td>cuong.le@gmail.com</td><td>8</td><td>1</td><td>460</td>
              <td>05/09/2026 08:02</td>
              <td><span class="badge badge--danger">Đã khoá</span></td>
              <td class="row">
                <a class="btn btn--sm" href="users.html?state=adjust">Sửa lượt</a>
                <button class="btn btn--sm btn--ghost">Mở khoá</button>
              </td>
            </tr>
            <tr>
              <td>Phạm Thu Hà</td><td>ha.pham@gmail.com</td><td>1</td><td>0</td><td>—</td>
              <td>Chưa đăng nhập</td>
              <td><span class="badge badge--warning">Chưa kích hoạt</span></td>
              <td class="row">
                <a class="btn btn--sm" href="users.html?state=adjust">Sửa lượt</a>
                <button class="btn btn--sm btn--ghost">Gửi lại email</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="card__footer">
        <div class="pagination">
          <span class="pagination__info">Hiển thị 1–4 trong 1.284 học viên</span>
          <a class="pagination__page" href="#" aria-disabled="true">Trước</a>
          <a class="pagination__page is-current" href="#" aria-current="page">1</a>
          <a class="pagination__page" href="#">2</a>
          <a class="pagination__page" href="#">3</a>
          <a class="pagination__page" href="#">Sau</a>
        </div>
      </div>
    </div>

    <div class="card only-empty">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">?</div>
          <h2 class="state-block__title">Không tìm thấy học viên nào</h2>
          <p class="state-block__desc">Thử bỏ bộ lọc hoặc nhập từ khoá khác.</p>
        </div>
      </div>
    </div>

    <div class="card only-loading">
      <div class="card__body stack">
        <div class="skeleton skeleton--title"></div>
        <div class="skeleton skeleton--text"></div>
        <div class="skeleton skeleton--block"></div>
      </div>
    </div>

    <div class="card only-error">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">!</div>
          <h2 class="state-block__title">Không tải được danh sách học viên</h2>
          <button class="btn">Tải lại</button>
        </div>
      </div>
    </div>
  </div>
</div>

<!-- Modal cộng/trừ lượt thi -->
<div class="modal-overlay only-adjust" role="dialog" aria-modal="true" aria-labelledby="adj-title">
  <div class="modal">
    <div class="modal__header">
      <h2 id="adj-title">Điều chỉnh lượt thi</h2>
      <p class="text-muted">Nguyễn Văn An — an.nguyen@gmail.com — đang có <strong>3 lượt</strong>.</p>
    </div>
    <div class="modal__body stack">
      <div class="field">
        <label class="field__label" for="adj-amount">Số lượt cộng thêm</label>
        <input class="field__input" id="adj-amount" type="number" value="1">
        <span class="field__hint">Nhập số âm để trừ lượt. Sau điều chỉnh: 4 lượt.</span>
      </div>
      <div class="field">
        <label class="field__label" for="adj-reason">Lý do (bắt buộc)</label>
        <input class="field__input" id="adj-reason" value="Đền bù lỗi audio Part 3 ngày 18/09">
      </div>
      <div class="alert alert--warning">Mọi điều chỉnh đều được ghi log kèm tên admin thực hiện.</div>
    </div>
    <div class="modal__footer row row--between">
      <a class="btn btn--ghost" href="users.html?state=success">Huỷ</a>
      <a class="btn btn--primary" href="users.html?state=success">Xác nhận điều chỉnh</a>
    </div>
  </div>
</div>
```

- [ ] **Step 3: Tạo `admin/score-conversion.html`**

Copy shell admin, mục active "Bảng quy đổi điểm". `<body data-states="loading,error,success,submitting" data-state="success">`.

Hai bảng cạnh nhau: Listening (0–100 câu đúng) và Reading (0–100 câu đúng), mỗi bảng đổi ra thang 5–495. Bảng trong prototype chỉ cần **10 dòng mẫu mỗi bên**, có ghi chú rõ là bản rút gọn.

```html
<div class="container">
  <div class="stack">
    <div class="row row--between">
      <div>
        <h1>Bảng quy đổi điểm</h1>
        <p class="text-muted">Số câu đúng đổi sang thang điểm 5–495 cho từng kỹ năng.</p>
      </div>
      <button class="btn btn--primary only-success">Lưu bảng</button>
    </div>

    <div class="alert alert--warning only-success">
      Điểm TOEIC <strong>không tuyến tính</strong> — không tự tính bằng công thức. Sửa sai bảng này làm lệch điểm của mọi bài thi sau đó.
    </div>

    <div class="grid-2 only-success">
      <div class="card">
        <div class="card__header"><strong>Listening</strong></div>
        <div class="card__body">
          <table class="table table--striped">
            <thead><tr><th scope="col">Số câu đúng</th><th scope="col">Điểm</th></tr></thead>
            <tbody>
              <tr><td>100</td><td><input class="field__input" value="495" aria-label="Điểm Listening cho 100 câu đúng"></td></tr>
              <tr><td>95</td><td><input class="field__input" value="475" aria-label="Điểm Listening cho 95 câu đúng"></td></tr>
              <tr><td>90</td><td><input class="field__input" value="450" aria-label="Điểm Listening cho 90 câu đúng"></td></tr>
              <tr><td>80</td><td><input class="field__input" value="400" aria-label="Điểm Listening cho 80 câu đúng"></td></tr>
              <tr><td>70</td><td><input class="field__input" value="340" aria-label="Điểm Listening cho 70 câu đúng"></td></tr>
              <tr><td>60</td><td><input class="field__input" value="285" aria-label="Điểm Listening cho 60 câu đúng"></td></tr>
              <tr><td>50</td><td><input class="field__input" value="230" aria-label="Điểm Listening cho 50 câu đúng"></td></tr>
              <tr><td>40</td><td><input class="field__input" value="175" aria-label="Điểm Listening cho 40 câu đúng"></td></tr>
              <tr><td>20</td><td><input class="field__input" value="75" aria-label="Điểm Listening cho 20 câu đúng"></td></tr>
              <tr><td>0</td><td><input class="field__input" value="5" aria-label="Điểm Listening cho 0 câu đúng"></td></tr>
            </tbody>
          </table>
          <p class="text-sm text-muted">Bản rút gọn 10 mốc. Bảng thật có đủ 101 dòng từ 0 đến 100.</p>
        </div>
      </div>

      <div class="card">
        <div class="card__header"><strong>Reading</strong></div>
        <div class="card__body">
          <table class="table table--striped">
            <thead><tr><th scope="col">Số câu đúng</th><th scope="col">Điểm</th></tr></thead>
            <tbody>
              <tr><td>100</td><td><input class="field__input" value="495" aria-label="Điểm Reading cho 100 câu đúng"></td></tr>
              <tr><td>95</td><td><input class="field__input" value="460" aria-label="Điểm Reading cho 95 câu đúng"></td></tr>
              <tr><td>90</td><td><input class="field__input" value="440" aria-label="Điểm Reading cho 90 câu đúng"></td></tr>
              <tr><td>80</td><td><input class="field__input" value="385" aria-label="Điểm Reading cho 80 câu đúng"></td></tr>
              <tr><td>70</td><td><input class="field__input" value="325" aria-label="Điểm Reading cho 70 câu đúng"></td></tr>
              <tr><td>60</td><td><input class="field__input" value="270" aria-label="Điểm Reading cho 60 câu đúng"></td></tr>
              <tr><td>50</td><td><input class="field__input" value="215" aria-label="Điểm Reading cho 50 câu đúng"></td></tr>
              <tr><td>40</td><td><input class="field__input" value="160" aria-label="Điểm Reading cho 40 câu đúng"></td></tr>
              <tr><td>20</td><td><input class="field__input" value="60" aria-label="Điểm Reading cho 20 câu đúng"></td></tr>
              <tr><td>0</td><td><input class="field__input" value="5" aria-label="Điểm Reading cho 0 câu đúng"></td></tr>
            </tbody>
          </table>
          <p class="text-sm text-muted">Bản rút gọn 10 mốc. Bảng thật có đủ 101 dòng từ 0 đến 100.</p>
        </div>
      </div>
    </div>

    <div class="card only-submitting">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">⏳</div>
          <h2 class="state-block__title">Đang lưu bảng quy đổi…</h2>
        </div>
      </div>
    </div>

    <div class="card only-loading">
      <div class="card__body stack">
        <div class="skeleton skeleton--title"></div>
        <div class="skeleton skeleton--block"></div>
      </div>
    </div>

    <div class="card only-error">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">!</div>
          <h2 class="state-block__title">Không tải được bảng quy đổi</h2>
          <p class="state-block__desc">Không sửa bảng khi chưa tải được dữ liệu hiện tại.</p>
          <button class="btn">Tải lại</button>
        </div>
      </div>
    </div>
  </div>
</div>
```

- [ ] **Step 4: Verify trên browser**

Chạy `python -m http.server 8080` trong `prototype/`:
- `admin/users.html` với `?state=loading`, `?state=empty`, `?state=error`, `?state=success`, `?state=adjust` — ở `adjust` modal phải nằm trên bảng và bảng vẫn thấy mờ phía sau; bấm "Huỷ" quay về `success`.
- `admin/score-conversion.html` với cả 4 state — ở `success` đếm đủ 2 bảng × 10 dòng, mỗi ô input có `aria-label`.
Không có lỗi console. Đây là gate của task, không được bỏ.

- [ ] **Step 5: Commit**

```bash
git add prototype/assets/css/base.css prototype/admin/users.html prototype/admin/score-conversion.html
git commit -m "feat: add admin users and score conversion screens"
```

### Task 16: Quản trị gói lượt thi và đơn hàng

**Files:**
- Create: `prototype/admin/packages.html`
- Create: `prototype/admin/orders.html`

**Interfaces:**
- Consumes: shell admin (Task 13), `.toolbar` `.pagination` `.table` `.badge` `.price-card` (Task 11), `.modal-overlay` `.modal`
- Produces: không có class mới

State `packages.html`: `loading`, `error`, `success`, `edit` (modal sửa gói).
State `orders.html`: `loading`, `empty`, `error`, `success`.

- [ ] **Step 1: Thêm state `edit` vào `base.css`**

```css
body[data-state="edit"] .only-edit { display: revert; }
body[data-state="edit"] .multi-state[data-show~="edit"] { display: revert; }
```

- [ ] **Step 2: Tạo `admin/packages.html`**

Copy shell admin, mục active "Gói lượt thi". `<body data-states="loading,error,success,edit" data-state="success">`.

```html
<div class="container">
  <div class="stack">
    <div class="row row--between">
      <h1>Gói lượt thi</h1>
      <a class="btn btn--primary multi-state" data-show="success" href="packages.html?state=edit">Tạo gói mới</a>
    </div>

    <div class="alert alert--info multi-state" data-show="success edit">
      Sửa giá chỉ ảnh hưởng đơn hàng mới. Đơn đã thanh toán giữ nguyên giá cũ.
    </div>

    <div class="grid-3 multi-state" data-show="success edit">
      <div class="price-card">
        <div class="row row--between">
          <div class="price-card__name">Gói Lẻ</div>
          <span class="badge badge--success">Đang bán</span>
        </div>
        <div class="price-card__amount">49.000 ₫</div>
        <div class="price-card__unit">1 lượt — 49.000 ₫/lượt</div>
        <ul class="price-card__list">
          <li>Đã bán: 412 đơn</li>
          <li>Doanh thu: 20,2 tr ₫</li>
        </ul>
        <a class="btn" href="packages.html?state=edit">Sửa gói</a>
      </div>

      <div class="price-card price-card--featured">
        <div class="row row--between">
          <div class="price-card__name">Gói Ôn Thi</div>
          <span class="badge badge--primary">Nổi bật</span>
        </div>
        <div class="price-card__amount">199.000 ₫</div>
        <div class="price-card__unit">5 lượt — 39.800 ₫/lượt</div>
        <ul class="price-card__list">
          <li>Đã bán: 286 đơn</li>
          <li>Doanh thu: 56,9 tr ₫</li>
        </ul>
        <a class="btn" href="packages.html?state=edit">Sửa gói</a>
      </div>

      <div class="price-card">
        <div class="row row--between">
          <div class="price-card__name">Gói Cấp Tốc</div>
          <span class="badge">Đã ẩn</span>
        </div>
        <div class="price-card__amount">349.000 ₫</div>
        <div class="price-card__unit">10 lượt — 34.900 ₫/lượt</div>
        <ul class="price-card__list">
          <li>Đã bán: 94 đơn</li>
          <li>Doanh thu: 32,8 tr ₫</li>
        </ul>
        <a class="btn" href="packages.html?state=edit">Sửa gói</a>
      </div>
    </div>

    <div class="card only-loading">
      <div class="card__body stack">
        <div class="skeleton skeleton--title"></div>
        <div class="skeleton skeleton--block"></div>
      </div>
    </div>

    <div class="card only-error">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">!</div>
          <h2 class="state-block__title">Không tải được danh sách gói</h2>
          <button class="btn">Tải lại</button>
        </div>
      </div>
    </div>
  </div>
</div>

<div class="modal-overlay only-edit" role="dialog" aria-modal="true" aria-labelledby="pkg-title">
  <div class="modal">
    <div class="modal__header">
      <h2 id="pkg-title">Sửa gói — Gói Ôn Thi</h2>
    </div>
    <div class="modal__body stack">
      <div class="field">
        <label class="field__label" for="pkg-name">Tên gói</label>
        <input class="field__input" id="pkg-name" value="Gói Ôn Thi">
      </div>
      <div class="grid-2">
        <div class="field">
          <label class="field__label" for="pkg-credits">Số lượt thi</label>
          <input class="field__input" id="pkg-credits" type="number" value="5" min="1">
        </div>
        <div class="field">
          <label class="field__label" for="pkg-price">Giá (₫)</label>
          <input class="field__input" id="pkg-price" type="number" value="199000" step="1000">
          <span class="field__hint">Đơn giá: 39.800 ₫/lượt</span>
        </div>
      </div>
      <div class="field">
        <label class="field__label" for="pkg-status">Trạng thái</label>
        <select class="field__input" id="pkg-status">
          <option selected>Đang bán</option>
          <option>Đã ẩn</option>
        </select>
      </div>
      <label class="text-sm"><input type="checkbox" checked> Đánh dấu là gói nổi bật trên bảng giá</label>
    </div>
    <div class="modal__footer row row--between">
      <a class="btn btn--ghost" href="packages.html?state=success">Huỷ</a>
      <a class="btn btn--primary" href="packages.html?state=success">Lưu gói</a>
    </div>
  </div>
</div>
```

- [ ] **Step 3: Tạo `admin/orders.html`**

Copy shell admin, mục active "Đơn hàng". `<body data-states="loading,empty,error,success" data-state="success">`.

```html
<div class="container">
  <div class="stack">
    <h1>Đơn hàng</h1>

    <div class="grid-3 only-success">
      <div class="stat">
        <div class="stat__label">Đã thanh toán (tháng này)</div>
        <div class="stat__value">328</div>
        <div class="stat__hint">64,7 tr ₫</div>
      </div>
      <div class="stat">
        <div class="stat__label">Đang đối soát</div>
        <div class="stat__value">5</div>
        <div class="stat__hint">2 đơn quá 30 phút</div>
      </div>
      <div class="stat">
        <div class="stat__label">Thất bại / hết hạn</div>
        <div class="stat__value">41</div>
        <div class="stat__hint">Chủ yếu do khách huỷ</div>
      </div>
    </div>

    <div class="toolbar only-success">
      <div class="field toolbar__grow">
        <label class="field__label" for="o-q">Tìm theo mã đơn hoặc email</label>
        <input class="field__input" id="o-q" type="search" placeholder="DH-20260920-…">
      </div>
      <div class="field">
        <label class="field__label" for="o-status">Trạng thái</label>
        <select class="field__input" id="o-status">
          <option>Tất cả</option>
          <option>Đã thanh toán</option>
          <option>Đang đối soát</option>
          <option>Thất bại</option>
          <option>Hết hạn</option>
        </select>
      </div>
      <div class="field">
        <label class="field__label" for="o-method">Cổng</label>
        <select class="field__input" id="o-method">
          <option>Tất cả</option>
          <option>VNPay</option>
          <option>MoMo</option>
        </select>
      </div>
      <button class="btn">Lọc</button>
    </div>

    <div class="card only-success">
      <div class="card__body">
        <table class="table table--striped">
          <thead>
            <tr>
              <th scope="col">Mã đơn</th><th scope="col">Học viên</th><th scope="col">Gói</th>
              <th scope="col">Số tiền</th><th scope="col">Cổng</th><th scope="col">Mã giao dịch</th>
              <th scope="col">Thời gian</th><th scope="col">Trạng thái</th><th scope="col">Thao tác</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>DH-20260920-0148</td><td>an.nguyen@gmail.com</td><td>Gói Ôn Thi</td>
              <td>199.000 ₫</td><td>VNPay</td><td>14320988</td><td>20/09/2026 14:32</td>
              <td><span class="badge badge--success">Đã thanh toán</span></td>
              <td><button class="btn btn--sm btn--ghost">Chi tiết</button></td>
            </tr>
            <tr>
              <td>DH-20260920-0147</td><td>binh.tran@gmail.com</td><td>Gói Lẻ</td>
              <td>49.000 ₫</td><td>MoMo</td><td>—</td><td>20/09/2026 14:05</td>
              <td><span class="badge badge--warning">Đang đối soát</span></td>
              <td class="row">
                <button class="btn btn--sm">Đối soát lại</button>
                <button class="btn btn--sm btn--ghost">Chi tiết</button>
              </td>
            </tr>
            <tr>
              <td>DH-20260920-0141</td><td>cuong.le@gmail.com</td><td>Gói Cấp Tốc</td>
              <td>349.000 ₫</td><td>VNPay</td><td>14319022</td><td>20/09/2026 11:18</td>
              <td><span class="badge badge--danger">Thất bại</span></td>
              <td><button class="btn btn--sm btn--ghost">Chi tiết</button></td>
            </tr>
            <tr>
              <td>DH-20260919-0122</td><td>ha.pham@gmail.com</td><td>Gói Lẻ</td>
              <td>49.000 ₫</td><td>VNPay</td><td>—</td><td>19/09/2026 22:51</td>
              <td><span class="badge">Hết hạn</span></td>
              <td><button class="btn btn--sm btn--ghost">Chi tiết</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="card__footer">
        <div class="pagination">
          <span class="pagination__info">Hiển thị 1–4 trong 374 đơn</span>
          <a class="pagination__page" href="#" aria-disabled="true">Trước</a>
          <a class="pagination__page is-current" href="#" aria-current="page">1</a>
          <a class="pagination__page" href="#">2</a>
          <a class="pagination__page" href="#">3</a>
          <a class="pagination__page" href="#">Sau</a>
        </div>
      </div>
    </div>

    <div class="card only-empty">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">?</div>
          <h2 class="state-block__title">Không có đơn hàng nào khớp bộ lọc</h2>
          <p class="state-block__desc">Thử mở rộng khoảng thời gian hoặc bỏ lọc trạng thái.</p>
        </div>
      </div>
    </div>

    <div class="card only-loading">
      <div class="card__body stack">
        <div class="skeleton skeleton--title"></div>
        <div class="skeleton skeleton--text"></div>
        <div class="skeleton skeleton--block"></div>
      </div>
    </div>

    <div class="card only-error">
      <div class="card__body">
        <div class="state-block">
          <div class="state-block__icon" aria-hidden="true">!</div>
          <h2 class="state-block__title">Không tải được danh sách đơn hàng</h2>
          <button class="btn">Tải lại</button>
        </div>
      </div>
    </div>
  </div>
</div>
```

- [ ] **Step 4: Verify trên browser**

Chạy `python -m http.server 8080` trong `prototype/`:
- `admin/packages.html` với `?state=loading`, `?state=error`, `?state=success`, `?state=edit` — ở `edit` modal nằm trên 3 thẻ gói; gói "Gói Cấp Tốc" hiện badge "Đã ẩn" không màu.
- `admin/orders.html` với cả 4 state — ở `success` hàng đang đối soát có thêm nút "Đối soát lại" mà các hàng khác không có; hai cột "Mã giao dịch" của đơn chưa thành công hiện "—".
- Bấm hết 7 mục sidebar admin, tất cả đều mở được, không còn 404.
Không có lỗi console. Đây là gate của task, không được bỏ.

- [ ] **Step 5: Commit**

```bash
git add prototype/assets/css/base.css prototype/admin/packages.html prototype/admin/orders.html
git commit -m "feat: add admin packages and orders screens"
```

### Task 17: Mục lục prototype và dọn dẹp

**Files:**
- Create: `prototype/index.html`
- Delete: `prototype/_sandbox.html`
- Modify: `prototype/assets/css/components.css` (thêm `.catalog`)

**Interfaces:**
- Consumes: `.card` `.badge` `.table` (Task 2)
- Produces: không có gì cho task sau — đây là task cuối

`index.html` là cửa vào duy nhất khi review. Mỗi màn hình một dòng, kèm link tới **từng state** chứ không chỉ link tới trang, để người review không phải tự gõ `?state=`.

- [ ] **Step 1: Thêm `.catalog` vào `components.css`**

```css
/* ===== Mục lục prototype ===== */
.catalog__group { margin-top: var(--space-6); }
.catalog__row {
  display: grid; grid-template-columns: 240px 1fr; gap: var(--space-3);
  align-items: baseline;
  padding: var(--space-3) 0;
  border-bottom: 1px solid var(--color-border);
}
.catalog__name { font-weight: 600; }
.catalog__states { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.catalog__state {
  padding: 2px var(--space-2);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface);
  color: var(--color-primary);
  font-family: var(--font-mono); font-size: var(--text-xs);
  text-decoration: none;
}
.catalog__state:hover { background: var(--color-bg); }
```

- [ ] **Step 2: Tạo `prototype/index.html` — khung và nhóm Auth**

Trang này **không dùng** `state-switch.js` và không có shell. `<body>` trơn, chỉ `.container`.

```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Prototype UI — Ôn và thi TOEIC</title>
  <link rel="stylesheet" href="assets/css/tokens.css">
  <link rel="stylesheet" href="assets/css/base.css">
  <link rel="stylesheet" href="assets/css/components.css">
</head>
<body>
<div class="container">
  <div class="stack">
    <div>
      <h1>Prototype UI — Ôn và thi chứng chỉ TOEIC</h1>
      <p class="text-muted">
        Bản vẽ giao diện tĩnh, không có backend. Mỗi màn hình có nhiều state,
        bấm trực tiếp vào tên state để xem. Thiết kế cho desktop 1440×900.
      </p>
    </div>

    <div class="alert alert--info">
      Số liệu, tên đề và nội dung câu hỏi trong prototype đều là dữ liệu mẫu.
      Trang "Cổng thanh toán" là bản mô phỏng, không kết nối VNPay/MoMo thật.
    </div>

    <div class="catalog">
      <div class="catalog__group">
        <h2>Xác thực</h2>
        <div class="catalog__row">
          <span class="catalog__name">Đăng nhập</span>
          <div class="catalog__states">
            <a class="catalog__state" href="auth/login.html?state=success">success</a>
            <a class="catalog__state" href="auth/login.html?state=submitting">submitting</a>
            <a class="catalog__state" href="auth/login.html?state=error">error</a>
          </div>
        </div>
        <div class="catalog__row">
          <span class="catalog__name">Đăng ký</span>
          <div class="catalog__states">
            <a class="catalog__state" href="auth/register.html?state=success">success</a>
            <a class="catalog__state" href="auth/register.html?state=submitting">submitting</a>
            <a class="catalog__state" href="auth/register.html?state=error">error</a>
          </div>
        </div>
        <div class="catalog__row">
          <span class="catalog__name">Quên mật khẩu</span>
          <div class="catalog__states">
            <a class="catalog__state" href="auth/forgot-password.html?state=success">success</a>
            <a class="catalog__state" href="auth/forgot-password.html?state=sent">sent</a>
            <a class="catalog__state" href="auth/forgot-password.html?state=error">error</a>
          </div>
        </div>
      </div>

      <!-- các nhóm tiếp theo chèn ở đây -->
    </div>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 3: Thêm nhóm Học viên và Thi vào `index.html`**

Thay dòng `<!-- các nhóm tiếp theo chèn ở đây -->` bằng:

```html
<div class="catalog__group">
  <h2>Học viên</h2>
  <div class="catalog__row">
    <span class="catalog__name">Bảng điều khiển</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/dashboard.html?state=success">success</a>
      <a class="catalog__state" href="student/dashboard.html?state=loading">loading</a>
      <a class="catalog__state" href="student/dashboard.html?state=empty">empty</a>
      <a class="catalog__state" href="student/dashboard.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Danh sách đề thi</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/exam-list.html?state=success">success</a>
      <a class="catalog__state" href="student/exam-list.html?state=paywall">paywall</a>
      <a class="catalog__state" href="student/exam-list.html?state=loading">loading</a>
      <a class="catalog__state" href="student/exam-list.html?state=empty">empty</a>
      <a class="catalog__state" href="student/exam-list.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Lịch sử thi</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/exam-history.html?state=success">success</a>
      <a class="catalog__state" href="student/exam-history.html?state=loading">loading</a>
      <a class="catalog__state" href="student/exam-history.html?state=empty">empty</a>
      <a class="catalog__state" href="student/exam-history.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Thông tin cá nhân</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/profile.html?state=success">success</a>
      <a class="catalog__state" href="student/profile.html?state=submitting">submitting</a>
      <a class="catalog__state" href="student/profile.html?state=error">error</a>
    </div>
  </div>
</div>

<div class="catalog__group">
  <h2>Làm bài thi</h2>
  <div class="catalog__row">
    <span class="catalog__name">Hướng dẫn trước khi thi</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/exam-instructions.html?state=success">success</a>
      <a class="catalog__state" href="student/exam-instructions.html?state=paywall">paywall</a>
      <a class="catalog__state" href="student/exam-instructions.html?state=submitting">submitting</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Thi Listening</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/exam-listening.html?state=success">success</a>
      <a class="catalog__state" href="student/exam-listening.html?state=resumed">resumed</a>
      <a class="catalog__state" href="student/exam-listening.html?state=offline">offline</a>
      <a class="catalog__state" href="student/exam-listening.html?state=expired">expired</a>
      <a class="catalog__state" href="student/exam-listening.html?state=submitting">submitting</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Thi Reading</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/exam-reading.html?state=success">success</a>
      <a class="catalog__state" href="student/exam-reading.html?state=resumed">resumed</a>
      <a class="catalog__state" href="student/exam-reading.html?state=offline">offline</a>
      <a class="catalog__state" href="student/exam-reading.html?state=expired">expired</a>
      <a class="catalog__state" href="student/exam-reading.html?state=submitting">submitting</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Panel điều hướng 200 câu <span class="badge badge--info">component</span></span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/question-navigator.html">xem</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Xác nhận nộp bài</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/exam-confirm-submit.html?state=success">success</a>
      <a class="catalog__state" href="student/exam-confirm-submit.html?state=complete">complete</a>
      <a class="catalog__state" href="student/exam-confirm-submit.html?state=submitting">submitting</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Kết quả thi</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/exam-result.html?state=success">success</a>
      <a class="catalog__state" href="student/exam-result.html?state=loading">loading</a>
      <a class="catalog__state" href="student/exam-result.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Xem lại đáp án</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/exam-review.html?state=success">success</a>
      <a class="catalog__state" href="student/exam-review.html?state=loading">loading</a>
      <a class="catalog__state" href="student/exam-review.html?state=error">error</a>
    </div>
  </div>
</div>
```

- [ ] **Step 4: Thêm nhóm Luyện tập, Thanh toán và Quản trị vào `index.html`**

Chèn tiếp sau nhóm "Làm bài thi":

```html
<div class="catalog__group">
  <h2>Luyện tập theo part</h2>
  <div class="catalog__row">
    <span class="catalog__name">Chọn part luyện tập</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/practice-select.html?state=success">success</a>
      <a class="catalog__state" href="student/practice-select.html?state=loading">loading</a>
      <a class="catalog__state" href="student/practice-select.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Làm bài luyện tập</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/practice-take.html?state=answering">answering</a>
      <a class="catalog__state" href="student/practice-take.html?state=revealed-correct">revealed-correct</a>
      <a class="catalog__state" href="student/practice-take.html?state=revealed-wrong">revealed-wrong</a>
      <a class="catalog__state" href="student/practice-take.html?state=finished">finished</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Tổng kết luyện tập</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/practice-summary.html?state=success">success</a>
      <a class="catalog__state" href="student/practice-summary.html?state=loading">loading</a>
      <a class="catalog__state" href="student/practice-summary.html?state=error">error</a>
    </div>
  </div>
</div>

<div class="catalog__group">
  <h2>Nạp lượt thi &amp; thanh toán</h2>
  <div class="catalog__row">
    <span class="catalog__name">Bảng giá</span>
    <div class="catalog__states">
      <a class="catalog__state" href="payment/pricing.html?state=success">success</a>
      <a class="catalog__state" href="payment/pricing.html?state=loading">loading</a>
      <a class="catalog__state" href="payment/pricing.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Xác nhận đơn hàng</span>
    <div class="catalog__states">
      <a class="catalog__state" href="payment/checkout.html?state=success">success</a>
      <a class="catalog__state" href="payment/checkout.html?state=submitting">submitting</a>
      <a class="catalog__state" href="payment/checkout.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Cổng thanh toán <span class="badge badge--warning">mô phỏng</span></span>
    <div class="catalog__states">
      <a class="catalog__state" href="payment/gateway-mock.html">xem</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Kết quả thanh toán</span>
    <div class="catalog__states">
      <a class="catalog__state" href="payment/payment-result.html?state=success">success</a>
      <a class="catalog__state" href="payment/payment-result.html?state=failed">failed</a>
      <a class="catalog__state" href="payment/payment-result.html?state=pending">pending</a>
      <a class="catalog__state" href="payment/payment-result.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Ví lượt thi</span>
    <div class="catalog__states">
      <a class="catalog__state" href="payment/wallet.html?state=paid">paid</a>
      <a class="catalog__state" href="payment/wallet.html?state=loading">loading</a>
      <a class="catalog__state" href="payment/wallet.html?state=empty">empty</a>
      <a class="catalog__state" href="payment/wallet.html?state=error">error</a>
    </div>
  </div>
</div>

<div class="catalog__group">
  <h2>Quản trị</h2>
  <div class="catalog__row">
    <span class="catalog__name">Bảng điều khiển</span>
    <div class="catalog__states">
      <a class="catalog__state" href="admin/dashboard.html?state=success">success</a>
      <a class="catalog__state" href="admin/dashboard.html?state=loading">loading</a>
      <a class="catalog__state" href="admin/dashboard.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Danh sách đề thi</span>
    <div class="catalog__states">
      <a class="catalog__state" href="admin/exam-list.html?state=success">success</a>
      <a class="catalog__state" href="admin/exam-list.html?state=loading">loading</a>
      <a class="catalog__state" href="admin/exam-list.html?state=empty">empty</a>
      <a class="catalog__state" href="admin/exam-list.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Wizard tạo đề</span>
    <div class="catalog__states">
      <a class="catalog__state" href="admin/exam-editor.html?state=success">step 1</a>
      <a class="catalog__state" href="admin/exam-editor.html?state=step2">step 2</a>
      <a class="catalog__state" href="admin/exam-editor.html?state=step3">step 3</a>
      <a class="catalog__state" href="admin/exam-editor.html?state=submitting">submitting</a>
      <a class="catalog__state" href="admin/exam-editor.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Soạn câu hỏi</span>
    <div class="catalog__states">
      <a class="catalog__state" href="admin/question-editor.html?state=success">success</a>
      <a class="catalog__state" href="admin/question-editor.html?state=submitting">submitting</a>
      <a class="catalog__state" href="admin/question-editor.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Học viên</span>
    <div class="catalog__states">
      <a class="catalog__state" href="admin/users.html?state=success">success</a>
      <a class="catalog__state" href="admin/users.html?state=adjust">adjust</a>
      <a class="catalog__state" href="admin/users.html?state=loading">loading</a>
      <a class="catalog__state" href="admin/users.html?state=empty">empty</a>
      <a class="catalog__state" href="admin/users.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Bảng quy đổi điểm</span>
    <div class="catalog__states">
      <a class="catalog__state" href="admin/score-conversion.html?state=success">success</a>
      <a class="catalog__state" href="admin/score-conversion.html?state=submitting">submitting</a>
      <a class="catalog__state" href="admin/score-conversion.html?state=loading">loading</a>
      <a class="catalog__state" href="admin/score-conversion.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Gói lượt thi</span>
    <div class="catalog__states">
      <a class="catalog__state" href="admin/packages.html?state=success">success</a>
      <a class="catalog__state" href="admin/packages.html?state=edit">edit</a>
      <a class="catalog__state" href="admin/packages.html?state=loading">loading</a>
      <a class="catalog__state" href="admin/packages.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Đơn hàng</span>
    <div class="catalog__states">
      <a class="catalog__state" href="admin/orders.html?state=success">success</a>
      <a class="catalog__state" href="admin/orders.html?state=loading">loading</a>
      <a class="catalog__state" href="admin/orders.html?state=empty">empty</a>
      <a class="catalog__state" href="admin/orders.html?state=error">error</a>
    </div>
  </div>
</div>
```

- [ ] **Step 5: Xoá `_sandbox.html`**

```bash
rm prototype/_sandbox.html
```

File này chỉ dùng để dựng và soi component ở Task 1–2. Giữ lại trong bản giao sẽ gây nhầm là một màn hình thật.

- [ ] **Step 6: Verify toàn bộ prototype trên browser**

Chạy `python -m http.server 8080` trong `prototype/`, mở `http://localhost:8080/`:
- Bấm **hết** link state trong mục lục. Không link nào 404, không trang nào trắng.
- Đếm lại: mục lục phải có 30 màn hình chia 6 nhóm (Xác thực 3, Học viên 4, Làm bài thi 7, Luyện tập 3, Thanh toán 5, Quản trị 8 — tổng 30).
- Mở `student/exam-listening.html` và `student/exam-reading.html`, xem source: không có `option--correct`, không có chữ "Đáp án đúng", không có chữ "giải thích".
- Xác nhận `prototype/_sandbox.html` trả 404.
- Không có lỗi console ở bất kỳ trang nào.
Đây là gate của task, không được bỏ.

- [ ] **Step 7: Commit**

```bash
git add prototype/assets/css/components.css prototype/index.html
git rm prototype/_sandbox.html
git commit -m "feat: add prototype catalog index and remove sandbox"
```

---

## Sau khi xong 17 task

Prototype đứng độc lập trong `prototype/`, mở bằng bất kỳ static server nào. Đây là đầu vào cho giai đoạn sau: dựng ASP.NET Core Web API + MVC client, lúc đó markup ở đây được chuyển thành Razor view và dữ liệu mẫu được thay bằng dữ liệu thật qua jQuery Ajax.
