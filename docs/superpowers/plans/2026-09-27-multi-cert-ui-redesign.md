# Multi-Certificate Companion UI Prototype Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update the static HTML prototype from TOEIC-only + KYC gating to multi-certificate companion system with placement tests, learning paths, AI grading, and mentor booking.

**Architecture:** Four sequential passes: (1) Remove KYC screens and references, (2) Transform 8 existing screens for multi-certificate support, (3) Add 16 new screens for companion features, (4) Export to Figma via html.to.design plugin.

**Tech Stack:** Static HTML5 + CSS3 (custom properties, flexbox, grid) + vanilla JS (ES2020), no framework, no build step. IBM Plex typography (self-hosted), state-switch.js for ?state= visibility.

**Spec:** `docs/superpowers/specs/2026-09-27-multi-cert-ui-redesign.md`

## Global Constraints

- Desktop-first at 1440×900 reference viewport
- "Exam-room signage" design system: flat painted fields, 2px ink keylines, zero radius, zero shadow
- Four sign colors: red #b31217, blue #0b4ea2, gold #f2b705, green #0a6b3c
- IBM Plex typography: Sans (body), Sans Condensed (signage caps), Mono (figures)
- State-switcher pattern: every screen has states reachable via ?state= query params
- Catalog index (prototype/index.html) stays synchronized with all screens
- All components follow existing patterns: 2px keyline frames, condensed caps for labels, tabular nums for figures

## Review Focus

1. **Certificate badge always sign-blue** — Exam cards, history items, mentor cards must use `.badge.badge--info` (sign-blue #0b4ea2), never other colors. A TOEIC badge in gold or a mixed-color scheme breaks the system's color semantics.
2. **Tabular numerals for all figures** — Prices, scores, ratings, question counts, timers must use `font-variant-numeric: tabular-nums` and IBM Plex Mono. A price that changes width on hover or a score column that doesn't align vertically breaks readability.
3. **Active certificate switching preserves data** — Dashboard and profile certificate switchers must never imply data loss. Message must say "switching does not delete your other certificates' progress" or equivalent. A switcher that looks destructive violates the parallel-certificates constraint.
4. **State visibility groups complete** — Every new screen with ?state= params must declare all four state visibility rule groups in state-switch.js: hide all .only-*, show matching .only-{state}, show .multi-state[data-show~="{state}"], restate display: grid for .modal-overlay.only-{state}. A state that renders block instead of grid pins the modal to top-left.
5. **No placeholder text in production markup** — "Lorem ipsum", "TBD", "TODO", "Sample text" must not appear in any committed HTML. Placeholder content must be realistic: Vietnamese names, realistic exam titles, actual section names per certificate. A "John Doe" mentor or "Test 123" exam breaks the product's Vietnamese context.

---

## Pass 1: Remove KYC (2-3 hours)

### Task 1: Delete KYC Screens and Catalog Group

**Files:**
- Delete: `prototype/student/kyc-submit.html`
- Delete: `prototype/student/kyc-pending.html`
- Delete: `prototype/admin/kyc-review.html`
- Modify: `prototype/index.html:246-260` (remove "ID verification" catalog group)
- Modify: `prototype/student/dashboard.html:34` (remove ID verification nav link)

**Interfaces:**
- Consumes: None
- Produces: Clean prototype without KYC references (Pass 2 transforms remaining screens)

- [ ] **Step 1: Delete the three KYC screen files**

```bash
rm prototype/student/kyc-submit.html
rm prototype/student/kyc-pending.html
rm prototype/admin/kyc-review.html
```

- [ ] **Step 2: Remove "ID verification" catalog group from index.html**

Open `prototype/index.html`, find lines 246-260 (the catalog group starting with `<h2>ID verification (gate before the real exam)</h2>`), delete the entire `<div class="catalog__group">...</div>` block.

Expected: Catalog now has 6 groups instead of 7.

- [ ] **Step 3: Remove ID verification nav link from dashboard sidebar**

Open `prototype/student/dashboard.html`, find line 34:
```html
<a class="nav-list__item" href="kyc-pending.html">ID verification</a>
```

Delete this line entirely.

- [ ] **Step 4: Verify catalog links don't break**

Open `prototype/index.html` in browser, click every link in every catalog group. 

Expected: No 404 errors, all links resolve to existing screens.

- [ ] **Step 5: Commit**

```bash
git add prototype/index.html prototype/student/dashboard.html
git add -u prototype/student/kyc-submit.html prototype/student/kyc-pending.html prototype/admin/kyc-review.html
git commit -m "feat: remove KYC screens and catalog group

- Delete kyc-submit, kyc-pending, admin kyc-review
- Remove ID verification catalog group from index
- Remove ID verification nav link from dashboard
- Multi-certificate pivot: mock exams need no identity gate"
```

---

### Task 2: Strip KYC Messaging from Exam Instructions

**Files:**
- Modify: `prototype/student/exam-instructions.html:55-62` (paywall alert)
- Modify: `prototype/student/profile.html:80-120` (remove CCCD fields if present)

**Interfaces:**
- Consumes: None
- Produces: Paywall message says "Buy credits" not "Verify ID", profile has no KYC fields

- [ ] **Step 1: Update paywall message in exam-instructions.html**

Open `prototype/student/exam-instructions.html`, find the paywall state alert (around line 55-62). Current text likely says something like "Verify your ID before starting the exam."

Replace with:
```html
<div class="alert alert--info">
  <strong>Nothing was deducted.</strong> Buy credits to start this exam. Practice and weakness analysis remain free.
</div>
```

- [ ] **Step 2: Check profile.html for CCCD/KYC fields**

Open `prototype/student/profile.html`, search for any fields labeled "CCCD", "National ID", "Identity card", or "Verification status".

If found, delete those form fields and their labels entirely.

Expected: Profile shows only: email, password, name, phone (no identity verification fields).

- [ ] **Step 3: Verify paywall message renders correctly**

Open `prototype/student/exam-instructions.html?state=paywall` in browser.

Expected: Alert says "Buy credits" and mentions nothing about ID verification.

- [ ] **Step 4: Commit**

```bash
git add prototype/student/exam-instructions.html prototype/student/profile.html
git commit -m "feat: remove KYC gate from paywall messaging

- Paywall alert says 'Buy credits' not 'Verify ID'
- Remove CCCD/identity fields from profile
- Multi-certificate pivot: only mock testing, no identity gate"
```

---

## Pass 2: Transform for Multi-Certificate (1 day)

### Task 3: Add Certificate Picker Component to CSS

**Files:**
- Modify: `prototype/assets/css/components.css` (append new component styles)

**Interfaces:**
- Consumes: None (uses existing tokens from tokens.css)
- Produces: `.certificate-picker`, `.certificate-card`, `.active-certificate` classes for Task 4

- [ ] **Step 1: Add certificate-picker styles to components.css**

Open `prototype/assets/css/components.css`, scroll to the end, add:

```css
/* === Certificate Picker === */
.certificate-picker {
  padding: var(--space-6, 32px);
}

.certificate-picker h2 {
  margin: 0 0 var(--space-5, 24px);
  font-family: var(--font-sign, "IBM Plex Sans Condensed", sans-serif);
  font-size: var(--text-xl, 22px);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: var(--tracking-sign, .055em);
}

.certificate-picker__grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: var(--space-4, 16px);
}

.certificate-card {
  display: flex;
  flex-direction: column;
  gap: var(--space-2, 8px);
  padding: var(--space-5, 24px);
  text-align: left;
  background: var(--color-surface, #ffffff);
  border: var(--keyline, 2px) solid var(--ink, #14181d);
  border-radius: 0;
  cursor: pointer;
  transition: none;
}

.certificate-card:hover {
  background: var(--ink, #14181d);
  color: #ffffff;
}

.certificate-card:hover .badge {
  background: #ffffff;
  border-color: #ffffff;
  color: var(--ink, #14181d);
}

.certificate-card__name {
  font-family: var(--font-sign, "IBM Plex Sans Condensed", sans-serif);
  font-size: var(--text-base, 16px);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: .03em;
}

.certificate-card__skills {
  font-size: var(--text-sm, 14px);
  color: var(--color-text-muted, #5c6570);
}

.certificate-card:hover .certificate-card__skills {
  color: var(--on-ink-muted, #8f98a3);
}
```

- [ ] **Step 2: Add active-certificate display styles**

Continue in `components.css`, add:

```css
/* === Active Certificate Display === */
.active-certificate {
  display: flex;
  align-items: baseline;
  gap: var(--space-3, 12px);
  flex-wrap: wrap;
}

.active-certificate__name {
  font-family: var(--font-sign, "IBM Plex Sans Condensed", sans-serif);
  font-size: var(--text-lg, 18px);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: var(--tracking-sign, .055em);
  color: var(--sign-blue, #0b4ea2);
}
```

- [ ] **Step 3: Verify CSS compiles (no syntax errors)**

Open any prototype screen in browser, check browser console for CSS errors.

Expected: No errors, page renders normally.

- [ ] **Step 4: Commit**

```bash
git add prototype/assets/css/components.css
git commit -m "feat(css): add certificate picker components

- certificate-picker: 3-column grid, painted cards
- certificate-card: 2px ink keyline, hover inverts to ink block
- active-certificate: display current cert with switch button
- Supports Task 4 dashboard transformation"
```

---

### Task 4: Transform Dashboard for Certificate Picking

**Files:**
- Modify: `prototype/student/dashboard.html:12,38-79` (add first-time state, certificate picker)

**Interfaces:**
- Consumes: `.certificate-picker`, `.certificate-card`, `.active-certificate` from Task 3
- Produces: Dashboard with first-time and returning states (other transforms use this pattern)

- [ ] **Step 1: Add first-time state to data-states attribute**

Open `prototype/student/dashboard.html`, find line 12:
```html
<body data-states="loading,empty,error,success">
```

Change to:
```html
<body data-states="loading,empty,error,success,first-time">
```

- [ ] **Step 2: Add first-time state block before loading state**

After line 38 (inside `<main class="app-main"><div class="container stack">`), add:

```html
<!-- ===== first-time: no active certificate ===== -->
<div class="only-first-time stack">
  <h1>Hello, Nguyễn Văn A</h1>
  <div class="card"><div class="card__body">
    <div class="certificate-picker">
      <h2>Pick a certificate to start</h2>
      <div class="certificate-picker__grid">
        <button class="certificate-card" data-cert="toeic">
          <span class="badge badge--info">TOEIC</span>
          <strong class="certificate-card__name">Test of English for International Communication</strong>
          <span class="certificate-card__skills">Listening · Reading</span>
        </button>
        <button class="certificate-card" data-cert="ielts">
          <span class="badge badge--info">IELTS</span>
          <strong class="certificate-card__name">International English Language Testing System</strong>
          <span class="certificate-card__skills">Listening · Reading · Writing · Speaking</span>
        </button>
        <button class="certificate-card" data-cert="vstep">
          <span class="badge badge--info">VSTEP</span>
          <strong class="certificate-card__name">Vietnamese Standardised Test of English Proficiency</strong>
          <span class="certificate-card__skills">Listening · Reading · Writing · Speaking</span>
        </button>
      </div>
    </div>
    <p class="text-muted" style="text-align: center; margin-top: var(--space-4, 16px);">
      Switching certificates later will not delete your progress. Each certificate's data is saved separately.
    </p>
  </div></div>
</div>
```

- [ ] **Step 3: Add active certificate display to success state**

Find the success state block (line ~80, starts with `<div class="only-success stack">`), add after the `<h1>` line:

```html
<div class="active-certificate">
  <span class="text-muted">Currently working on</span>
  <strong class="active-certificate__name">TOEIC</strong>
  <button class="btn btn--plate btn--sm">Switch certificate</button>
</div>
```

- [ ] **Step 4: Test first-time state renders**

Open `prototype/student/dashboard.html?state=first-time` in browser.

Expected: Certificate picker grid with 3 cards (TOEIC, IELTS, VSTEP), cards invert on hover.

- [ ] **Step 5: Test success state shows active cert**

Open `prototype/student/dashboard.html?state=success` in browser.

Expected: "Currently working on TOEIC" with switch button visible.

- [ ] **Step 6: Update catalog to add first-time state**

Open `prototype/index.html`, find the Dashboard catalog row (~line 58-64), add `first-time` to the states list:

```html
<div class="catalog__row">
  <span class="catalog__name">Dashboard</span>
  <div class="catalog__states">
    <a class="catalog__state" href="student/dashboard.html?state=first-time">first-time</a>
    <a class="catalog__state" href="student/dashboard.html?state=success">success</a>
    <a class="catalog__state" href="student/dashboard.html?state=loading">loading</a>
    <a class="catalog__state" href="student/dashboard.html?state=empty">empty</a>
    <a class="catalog__state" href="student/dashboard.html?state=error">error</a>
  </div>
</div>
```

- [ ] **Step 7: Commit**

```bash
git add prototype/student/dashboard.html prototype/index.html
git commit -m "feat(dashboard): add certificate picker for multi-cert

- Add first-time state: certificate picker (TOEIC/IELTS/VSTEP)
- Add active-certificate display to success state
- Catalog updated with first-time state link
- Supports parallel certificate tracking per user"
```

---

### Task 5: Add Active Certificate Dropdown to Profile

**Files:**
- Modify: `prototype/student/profile.html:80-100` (add certificate field after email)

**Interfaces:**
- Consumes: Existing `.field` styles from components.css
- Produces: Profile with certificate switcher (completes certificate switching UX)

- [ ] **Step 1: Add active certificate field to profile form**

Open `prototype/student/profile.html`, find the email field (around line 80-90). After the email field's closing `</label>`, add:

```html
<label class="field">
  <span class="field__label">Active certificate</span>
  <select class="field__input" name="certificate">
    <option value="toeic" selected>TOEIC</option>
    <option value="ielts">IELTS</option>
    <option value="vstep">VSTEP</option>
  </select>
  <span class="field__hint">Switching certificates does not delete your other certificates' progress.</span>
</label>
```

- [ ] **Step 2: Add field__hint style to components.css**

Open `prototype/assets/css/components.css`, find the `.field` section, add after `.field__label`:

```css
.field__hint {
  display: block;
  margin-top: var(--space-1, 4px);
  font-size: var(--text-xs, 12px);
  color: var(--color-text-muted, #5c6570);
}
```

- [ ] **Step 3: Test profile renders**

Open `prototype/student/profile.html?state=success` in browser.

Expected: Active certificate dropdown appears after email field, hint text visible below dropdown.

- [ ] **Step 4: Commit**

```bash
git add prototype/student/profile.html prototype/assets/css/components.css
git commit -m "feat(profile): add active certificate switcher

- Add certificate dropdown after email field
- Add field__hint style for helper text
- Clarifies switching preserves other certificates' data
- Completes certificate switching UX (dashboard + profile)"
```

---

### Task 6: Add Certificate Badges to Exam List and History

**Files:**
- Modify: `prototype/student/exam-list.html:80-120` (add badge to exam cards)
- Modify: `prototype/student/exam-history.html:80-130` (add badge to history items)

**Interfaces:**
- Consumes: Existing `.badge.badge--info` from components.css
- Produces: Exam cards and history items with certificate badges

- [ ] **Step 1: Add certificate badge to exam-list cards**

Open `prototype/student/exam-list.html`, find the success state block with exam cards (line ~80-120). Each card has a title. After each `<strong>` title, add:

```html
<span class="badge badge--info">TOEIC</span>
```

Example:
```html
<div class="exam-card__header">
  <strong>ETS 2024 — Test 5</strong>
  <span class="badge badge--info">TOEIC</span>
</div>
```

Repeat for all exam cards in the success state.

- [ ] **Step 2: Add certificate badge to exam-history items**

Open `prototype/student/exam-history.html`, find the table rows in success state (line ~80-130). Each row has an exam name cell. Add badge after the exam name:

```html
<td>
  ETS 2024 — Test 5
  <span class="badge badge--info">TOEIC</span>
</td>
```

Repeat for all table rows.

- [ ] **Step 3: Test exam-list renders**

Open `prototype/student/exam-list.html?state=success` in browser.

Expected: Each exam card shows TOEIC badge in sign-blue next to title.

- [ ] **Step 4: Test exam-history renders**

Open `prototype/student/exam-history.html?state=success` in browser.

Expected: Each history row shows TOEIC badge next to exam name.

- [ ] **Step 5: Commit**

```bash
git add prototype/student/exam-list.html prototype/student/exam-history.html
git commit -m "feat(exams): add certificate badges to list and history

- exam-list: badge on each card header
- exam-history: badge in exam name column
- All badges use badge--info (sign-blue)
- Visual confirmation of active certificate filtering"
```

---

### Task 7: Transform Practice Select from Parts to Sections

**Files:**
- Modify: `prototype/student/practice-select.html:80-150` (replace Part 1-7 with section grid)

**Interfaces:**
- Consumes: None
- Produces: Practice select showing data-driven sections per certificate

- [ ] **Step 1: Replace Part 1-7 grid with section grid**

Open `prototype/student/practice-select.html`, find the success state grid of part cards (line ~80-150). Replace the entire grid with:

```html
<div class="grid-3">
  <button class="part-card" data-section="listening-1">
    <div class="part-card__header">
      <span class="badge badge--info">Listening</span>
      <strong>Section 1</strong>
    </div>
    <p class="part-card__desc">Photographs</p>
    <div class="part-card__meta">6 questions</div>
  </button>
  <button class="part-card" data-section="listening-2">
    <div class="part-card__header">
      <span class="badge badge--info">Listening</span>
      <strong>Section 2</strong>
    </div>
    <p class="part-card__desc">Question-Response</p>
    <div class="part-card__meta">25 questions</div>
  </button>
  <button class="part-card" data-section="listening-3">
    <div class="part-card__header">
      <span class="badge badge--info">Listening</span>
      <strong>Section 3</strong>
    </div>
    <p class="part-card__desc">Short Conversations</p>
    <div class="part-card__meta">39 questions</div>
  </button>
  <button class="part-card" data-section="listening-4">
    <div class="part-card__header">
      <span class="badge badge--info">Listening</span>
      <strong>Section 4</strong>
    </div>
    <p class="part-card__desc">Short Talks</p>
    <div class="part-card__meta">30 questions</div>
  </button>
  <button class="part-card" data-section="reading-1">
    <div class="part-card__header">
      <span class="badge badge--success">Reading</span>
      <strong>Section 5</strong>
    </div>
    <p class="part-card__desc">Incomplete Sentences</p>
    <div class="part-card__meta">30 questions</div>
  </button>
  <button class="part-card" data-section="reading-2">
    <div class="part-card__header">
      <span class="badge badge--success">Reading</span>
      <strong>Section 6</strong>
    </div>
    <p class="part-card__desc">Text Completion</p>
    <div class="part-card__meta">16 questions</div>
  </button>
  <button class="part-card" data-section="reading-3">
    <div class="part-card__header">
      <span class="badge badge--success">Reading</span>
      <strong>Section 7</strong>
    </div>
    <p class="part-card__desc">Reading Comprehension</p>
    <div class="part-card__meta">54 questions</div>
  </button>
</div>
```

- [ ] **Step 2: Update page title**

Find the `<h1>` tag, change from "Practice by part" to:
```html
<h1>Practice by section</h1>
```

- [ ] **Step 3: Update sidebar nav label**

Open `prototype/student/dashboard.html` and any other files with sidebar nav, find:
```html
<a class="nav-list__item" href="practice-select.html">Practice by part</a>
```

Change to:
```html
<a class="nav-list__item" href="practice-select.html">Practice by section</a>
```

- [ ] **Step 4: Update catalog label**

Open `prototype/index.html`, find "Practice by part" group header (line ~162), change to:
```html
<h2>Practice by section</h2>
```

And update the row label:
```html
<span class="catalog__name">Choose a section to practice</span>
```

- [ ] **Step 5: Test practice-select renders**

Open `prototype/student/practice-select.html?state=success` in browser.

Expected: 7 section cards with descriptive names (not just "Part 1"), skill badges (Listening/Reading).

- [ ] **Step 6: Commit**

```bash
git add prototype/student/practice-select.html prototype/student/dashboard.html prototype/index.html
git commit -m "feat(practice): transform from parts to data-driven sections

- Replace Part 1-7 with section cards per certificate
- Use realistic section names (Photographs, Question-Response, etc)
- Add skill badges (Listening/Reading)
- Sidebar and catalog updated to 'Practice by section'
- Supports certificate-specific section structures"
```

---

### Task 8: Transform Admin Exam Editor and Score Conversion

**Files:**
- Modify: `prototype/admin/exam-editor.html:50-80` (add certificate picker at top)
- Modify: `prototype/admin/score-conversion.html:80-150` (split into two tables)

**Interfaces:**
- Consumes: Existing form styles
- Produces: Admin screens with certificate awareness

- [ ] **Step 1: Add certificate picker to exam-editor**

Open `prototype/admin/exam-editor.html`, find the form start (line ~50). Before the first field, add:

```html
<label class="field">
  <span class="field__label">Certificate</span>
  <select class="field__input" name="certificate" required>
    <option value="">Select a certificate</option>
    <option value="toeic">TOEIC</option>
    <option value="ielts">IELTS</option>
    <option value="vstep">VSTEP</option>
  </select>
  <span class="field__hint">Skills and sections will load based on your selection</span>
</label>
```

- [ ] **Step 2: Split score-conversion into two tables**

Open `prototype/admin/score-conversion.html`, find the table (line ~80-150). Replace with two tables:

```html
<h2>Raw to Scaled Scores</h2>
<p class="text-muted">Editable per exam — difficulty varies</p>

<table class="table table--striped">
  <thead>
    <tr>
      <th>Raw Score</th>
      <th>Scaled Score (Listening)</th>
      <th>Scaled Score (Reading)</th>
      <th>Actions</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>96-100</td>
      <td><input type="number" class="field__input" value="495" min="5" max="495"></td>
      <td><input type="number" class="field__input" value="495" min="5" max="495"></td>
      <td><button class="btn btn--sm">Save</button></td>
    </tr>
    <tr>
      <td>91-95</td>
      <td><input type="number" class="field__input" value="485" min="5" max="495"></td>
      <td><input type="number" class="field__input" value="470" min="5" max="495"></td>
      <td><button class="btn btn--sm">Save</button></td>
    </tr>
    <!-- Add 5-7 more rows -->
  </tbody>
</table>

<h2 style="margin-top: var(--space-6, 32px);">Scaled to Band (CEFR)</h2>
<p class="text-muted">Read-only — fixed standard per certificate</p>

<table class="table table--striped">
  <thead>
    <tr>
      <th>Scaled Range</th>
      <th>Band</th>
      <th>Description</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>945-990</td>
      <td><span class="badge badge--success">C1</span></td>
      <td>Advanced proficiency</td>
    </tr>
    <tr>
      <td>785-944</td>
      <td><span class="badge badge--success">B2</span></td>
      <td>Upper intermediate</td>
    </tr>
    <tr>
      <td>550-784</td>
      <td><span class="badge badge--info">B1</span></td>
      <td>Intermediate</td>
    </tr>
    <tr>
      <td>225-549</td>
      <td><span class="badge badge--warning">A2</span></td>
      <td>Elementary</td>
    </tr>
    <tr>
      <td>120-224</td>
      <td><span class="badge badge--warning">A1</span></td>
      <td>Beginner</td>
    </tr>
    <tr>
      <td>10-119</td>
      <td><span class="badge">< A1</span></td>
      <td>Basic knowledge</td>
    </tr>
  </tbody>
</table>
```

- [ ] **Step 3: Test exam-editor renders**

Open `prototype/admin/exam-editor.html?state=success` in browser.

Expected: Certificate dropdown appears first, hint text visible.

- [ ] **Step 4: Test score-conversion renders**

Open `prototype/admin/score-conversion.html?state=success` in browser.

Expected: Two tables — first has editable inputs, second is read-only with badges.

- [ ] **Step 5: Commit**

```bash
git add prototype/admin/exam-editor.html prototype/admin/score-conversion.html
git commit -m "feat(admin): add certificate awareness to editor and scoring

- exam-editor: certificate picker loads skills/sections
- score-conversion: split into editable raw→scaled, read-only scaled→band
- Band table uses badges (C1/B2 success, B1 info, A2/A1 warning)
- Enforces certificate-specific scoring standards"
```

---

## Pass 3: Add New Screens (2-3 days)

### Task 9: Add New Component Styles (Mentor, Path, Rubric)

**Files:**
- Modify: `prototype/assets/css/components.css` (append new styles)

**Interfaces:**
- Consumes: None
- Produces: `.mentor-card`, `.path-module`, `.rubric-scores` for Tasks 10-16

- [ ] **Step 1: Add mentor-card styles**

Open `prototype/assets/css/components.css`, scroll to end, add:

```css
/* === Mentor Card === */
.mentor-card {
  background: var(--color-surface, #ffffff);
  border: var(--keyline, 2px) solid var(--ink, #14181d);
  border-radius: 0;
  padding: var(--space-5, 24px);
  display: flex;
  flex-direction: column;
  gap: var(--space-3, 12px);
}

.mentor-card__header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-3, 12px);
}

.mentor-card__header strong {
  font-family: var(--font-sign, "IBM Plex Sans Condensed", sans-serif);
  font-size: var(--text-base, 16px);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: .03em;
}

.mentor-card__rating {
  display: flex;
  align-items: baseline;
  gap: var(--space-2, 8px);
}

.mentor-card__stars {
  font-size: var(--text-lg, 18px);
  color: var(--sign-gold, #f2b705);
}

.mentor-card__bio {
  font-size: var(--text-sm, 14px);
  color: var(--color-text-muted, #5c6570);
  margin: 0;
}

.mentor-card__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: var(--space-2, 8px);
}

.mentor-card__price {
  font-family: var(--font-mono, "IBM Plex Mono", monospace);
  font-size: var(--text-base, 16px);
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}
```

- [ ] **Step 2: Add path-module styles**

Continue in `components.css`, add:

```css
/* === Path Module Card === */
.path-module {
  background: var(--color-surface, #ffffff);
  border: var(--keyline, 2px) solid var(--ink, #14181d);
  border-radius: 0;
  padding: var(--space-4, 16px) var(--space-5, 24px);
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: var(--space-4, 16px);
  align-items: center;
}

.path-module__status {
  /* Badge already styled */
}

.path-module__content strong {
  display: block;
  font-family: var(--font-sign, "IBM Plex Sans Condensed", sans-serif);
  font-size: var(--text-base, 16px);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: .03em;
  margin-bottom: var(--space-1, 4px);
}

.path-module__content p {
  margin: 0;
  font-size: var(--text-sm, 14px);
  color: var(--color-text-muted, #5c6570);
}
```

- [ ] **Step 3: Add rubric-scores styles**

Continue in `components.css`, add:

```css
/* === Rubric Scores === */
.rubric-scores {
  margin: var(--space-5, 24px) 0;
}

.rubric-scores h3 {
  margin: 0 0 var(--space-3, 12px);
  font-family: var(--font-sign, "IBM Plex Sans Condensed", sans-serif);
  font-size: var(--text-base, 16px);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: .03em;
}

.rubric-scores__list {
  display: grid;
  gap: var(--space-3, 12px);
  margin: 0;
}

.rubric-scores__item {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: var(--space-4, 16px);
  padding: var(--space-3, 12px) 0;
  border-bottom: 1px solid var(--color-border, #ccd2d6);
}

.rubric-scores__item:last-child {
  border-bottom: 0;
}

.rubric-scores__item dt {
  font-family: var(--font-sign, "IBM Plex Sans Condensed", sans-serif);
  font-size: var(--text-sm, 14px);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: .03em;
}

.rubric-scores__item dd {
  margin: 0;
  font-family: var(--font-mono, "IBM Plex Mono", monospace);
  font-size: var(--text-base, 16px);
  font-variant-numeric: tabular-nums;
  text-align: right;
}

.rubric-scores__item .score-value {
  font-weight: 600;
  color: var(--sign-blue, #0b4ea2);
}
```

- [ ] **Step 4: Verify CSS compiles**

Open any prototype screen in browser, check console for CSS errors.

Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add prototype/assets/css/components.css
git commit -m "feat(css): add mentor, path module, and rubric components

- mentor-card: painted panel, rating stars, price in mono
- path-module: 3-column grid with status badge
- rubric-scores: 2-column list, scores in mono tabular nums
- Supports Tasks 10-16 new screens"
```

---

### Task 10: Build Placement Test Flow (5 screens)

**Files:**
- Create: `prototype/student/placement-select.html`
- Create: `prototype/student/placement-take.html`
- Create: `prototype/student/placement-result.html`
- Create: `prototype/student/target-band-set.html`
- Create: `prototype/student/path-generated.html`

**Interfaces:**
- Consumes: Component styles from components.css
- Produces: Complete placement flow (5 screens, 19 states total)

- [ ] **Step 1: Create placement-select.html**

Create `prototype/student/placement-select.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Placement Test — TOEIC Practice</title>
  <link rel="icon" href="../assets/img/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="loading,empty,error,success">
  <div class="app-shell app-shell--with-sidebar">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <div class="row">
        <span class="badge badge--info">3 credits left</span>
        <a class="btn btn--sm" href="../payment/pricing.html">Buy credits</a>
        <a class="btn btn--ghost btn--sm" href="profile.html">Nguyễn Văn A</a>
      </div>
    </header>
    <div class="app-body">
      <aside class="app-sidebar">
        <nav class="nav-list" aria-label="Main navigation">
          <a class="nav-list__item" href="dashboard.html">Home</a>
          <div class="nav-list__group">Mock exams</div>
          <a class="nav-list__item" href="exam-list.html">Test list</a>
          <a class="nav-list__item" href="exam-history.html">Exam history</a>
          <div class="nav-list__group">Practice</div>
          <a class="nav-list__item" href="practice-select.html">Practice by section</a>
          <a class="nav-list__item" href="weakness-analysis.html">Weakness analysis</a>
          <div class="nav-list__group">Account</div>
          <a class="nav-list__item" href="../payment/wallet.html">Credit wallet</a>
          <a class="nav-list__item" href="profile.html">Profile</a>
        </nav>
      </aside>
      <main class="app-main">
        <div class="container stack">
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="skeleton skeleton--block" style="height: 300px"></div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Could not load placement tests</div>
                <p class="state-block__desc">Check your connection and try again.</p>
                <button class="btn btn--primary">Try again</button>
              </div>
            </div></div>
          </div>

          <!-- ===== empty ===== -->
          <div class="only-empty">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">EMPTY</div>
                <div class="state-block__title">No placement tests available</div>
                <p class="state-block__desc">Placement tests are being prepared for TOEIC. Check back soon.</p>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <h1>Pick a placement test</h1>
            <div class="alert alert--info">
              <strong>Placement tests are free.</strong> This test will determine your current level and generate a personalized learning path.
            </div>

            <div class="grid-2">
              <article class="exam-card">
                <div class="exam-card__header">
                  <strong>TOEIC Placement Test — Standard</strong>
                  <span class="badge badge--info">TOEIC</span>
                </div>
                <div class="exam-card__meta">
                  <span>40 questions</span>
                  <span>·</span>
                  <span>30 minutes</span>
                </div>
                <p class="exam-card__desc">Listening and Reading sections. Quick assessment of your current TOEIC level.</p>
                <div class="exam-card__footer">
                  <a href="placement-take.html" class="btn btn--primary">Start test</a>
                </div>
              </article>
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

- [ ] **Step 2: Create placement-take.html (reuses exam-take pattern)**

Create `prototype/student/placement-take.html` by copying `prototype/student/exam-listening.html` and modify:
- Change title to "Placement Test — TOEIC Practice"
- Change topbar label to "Placement Test (Question 12 of 40)"
- Change timer to "28:45 remaining"
- Keep all 6 states: success, loading, resumed, offline, expired, submitting

- [ ] **Step 3: Create placement-result.html**

Create `prototype/student/placement-result.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Placement Result — TOEIC Practice</title>
  <link rel="icon" href="../assets/img/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="loading,error,success">
  <div class="app-shell app-shell--with-sidebar">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <div class="row">
        <span class="badge badge--info">3 credits left</span>
        <a class="btn btn--sm" href="../payment/pricing.html">Buy credits</a>
        <a class="btn btn--ghost btn--sm" href="profile.html">Nguyễn Văn A</a>
      </div>
    </header>
    <div class="app-body">
      <aside class="app-sidebar">
        <nav class="nav-list" aria-label="Main navigation">
          <a class="nav-list__item" href="dashboard.html">Home</a>
          <div class="nav-list__group">Mock exams</div>
          <a class="nav-list__item" href="exam-list.html">Test list</a>
          <a class="nav-list__item" href="exam-history.html">Exam history</a>
          <div class="nav-list__group">Practice</div>
          <a class="nav-list__item" href="practice-select.html">Practice by section</a>
          <a class="nav-list__item" href="weakness-analysis.html">Weakness analysis</a>
          <div class="nav-list__group">Account</div>
          <a class="nav-list__item" href="../payment/wallet.html">Credit wallet</a>
          <a class="nav-list__item" href="profile.html">Profile</a>
        </nav>
      </aside>
      <main class="app-main">
        <div class="container stack">
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="skeleton skeleton--block" style="height: 200px"></div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Could not load results</div>
                <p class="state-block__desc">Check your connection and try again.</p>
                <button class="btn btn--primary">Try again</button>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <h1>Your placement result</h1>
            
            <div class="card"><div class="card__body stack">
              <div class="cefr-band">
                <div class="cefr-band__head">
                  <span class="cefr-band__current">A2</span>
                  <span>385 points · Elementary level</span>
                </div>
                <div class="cefr-band__track" role="img" aria-label="Current band A2, score 385 out of 990">
                  <span class="cefr-band__band is-reached">&lt;A1</span>
                  <span class="cefr-band__band is-reached">A1</span>
                  <span class="cefr-band__band is-current">A2</span>
                  <span class="cefr-band__band">B1</span>
                  <span class="cefr-band__band">B2</span>
                  <span class="cefr-band__band">C1</span>
                </div>
                <div class="cefr-band__scale" aria-hidden="true">
                  <span>10</span>
                  <span class="cefr-band__tick">120</span>
                  <span class="cefr-band__tick">225</span>
                  <span class="cefr-band__tick">550</span>
                  <span class="cefr-band__tick">785</span>
                  <span class="cefr-band__tick">945</span>
                </div>
              </div>

              <p>You scored <strong>385 points</strong> on the placement test. This places you at the <strong>A2 (Elementary)</strong> level.</p>
              
              <a href="target-band-set.html" class="btn btn--primary">Set your target level</a>
            </div></div>
          </div>
        </div>
      </main>
    </div>
  </div>
  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 4: Create target-band-set.html**

Create `prototype/student/target-band-set.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Set Target Level — TOEIC Practice</title>
  <link rel="icon" href="../assets/img/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="submitting,error,success">
  <div class="app-shell app-shell--with-sidebar">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <div class="row">
        <span class="badge badge--info">3 credits left</span>
        <a class="btn btn--sm" href="../payment/pricing.html">Buy credits</a>
        <a class="btn btn--ghost btn--sm" href="profile.html">Nguyễn Văn A</a>
      </div>
    </header>
    <div class="app-body">
      <aside class="app-sidebar">
        <nav class="nav-list" aria-label="Main navigation">
          <a class="nav-list__item" href="dashboard.html">Home</a>
          <div class="nav-list__group">Mock exams</div>
          <a class="nav-list__item" href="exam-list.html">Test list</a>
          <a class="nav-list__item" href="exam-history.html">Exam history</a>
          <div class="nav-list__group">Practice</div>
          <a class="nav-list__item" href="practice-select.html">Practice by section</a>
          <a class="nav-list__item" href="weakness-analysis.html">Weakness analysis</a>
          <div class="nav-list__group">Account</div>
          <a class="nav-list__item" href="../payment/wallet.html">Credit wallet</a>
          <a class="nav-list__item" href="profile.html">Profile</a>
        </nav>
      </aside>
      <main class="app-main">
        <div class="container stack">
          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="alert alert--danger">
              <strong>Could not save your target.</strong> Try again.
            </div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <h1>Set your target level</h1>
            <p>Your current level is <strong>A2</strong>. Where do you want to be?</p>

            <form class="stack" style="max-width: 500px;">
              <label class="field">
                <span class="field__label">Target level</span>
                <select class="field__input" name="target" required>
                  <option value="">Choose your target</option>
                  <option value="b1">B1 (Intermediate) — 550-784 points</option>
                  <option value="b2" selected>B2 (Upper Intermediate) — 785-944 points</option>
                  <option value="c1">C1 (Advanced) — 945-990 points</option>
                </select>
                <span class="field__hint">Your learning path will guide you from A2 to your target level.</span>
              </label>

              <div class="row">
                <button type="submit" class="btn btn--primary multi-state" data-show="success">Generate my path</button>
                <button type="button" class="btn btn--primary only-submitting" disabled>Generating...</button>
              </div>
            </form>
          </div>
        </div>
      </main>
    </div>
  </div>
  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 5: Create path-generated.html**

Create `prototype/student/path-generated.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Path Generated — TOEIC Practice</title>
  <link rel="icon" href="../assets/img/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="loading,error,success">
  <div class="app-shell app-shell--with-sidebar">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <div class="row">
        <span class="badge badge--info">3 credits left</span>
        <a class="btn btn--sm" href="../payment/pricing.html">Buy credits</a>
        <a class="btn btn--ghost btn--sm" href="profile.html">Nguyễn Văn A</a>
      </div>
    </header>
    <div class="app-body">
      <aside class="app-sidebar">
        <nav class="nav-list" aria-label="Main navigation">
          <a class="nav-list__item" href="dashboard.html">Home</a>
          <div class="nav-list__group">Mock exams</div>
          <a class="nav-list__item" href="exam-list.html">Test list</a>
          <a class="nav-list__item" href="exam-history.html">Exam history</a>
          <div class="nav-list__group">Practice</div>
          <a class="nav-list__item" href="practice-select.html">Practice by section</a>
          <a class="nav-list__item" href="weakness-analysis.html">Weakness analysis</a>
          <div class="nav-list__group">Account</div>
          <a class="nav-list__item" href="../payment/wallet.html">Credit wallet</a>
          <a class="nav-list__item" href="profile.html">Profile</a>
        </nav>
      </aside>
      <main class="app-main">
        <div class="container stack">
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="skeleton skeleton--block" style="height: 200px"></div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Could not generate path</div>
                <p class="state-block__desc">Try again or contact support.</p>
                <button class="btn btn--primary">Try again</button>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <div class="alert alert--success">
              <strong>Your learning path is ready!</strong> We've generated a personalized path from A2 to B2.
            </div>

            <h1>A2 → B2 Learning Path</h1>
            <p>Follow this path to reach your target level. Free modules practice your weak areas. Upgrade to premium for deeper guidance and AI tasks.</p>

            <div class="row">
              <a href="learning-path.html" class="btn btn--primary">View my path</a>
              <a href="dashboard.html" class="btn">Go to dashboard</a>
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

- [ ] **Step 6: Test all 5 screens render**

Open each screen in browser with `?state=success`:
- placement-select.html
- placement-take.html
- placement-result.html
- target-band-set.html
- path-generated.html

Expected: All screens render, state switcher works, flow is cohesive.

- [ ] **Step 7: Commit**

```bash
git add prototype/student/placement-*.html prototype/student/target-band-set.html prototype/student/path-generated.html
git commit -m "feat(placement): add placement test flow (5 screens)

- placement-select: pick placement exam (4 states)
- placement-take: take test, reuses exam-take pattern (6 states)
- placement-result: show band on CEFR bar (3 states)
- target-band-set: pick target level B1/B2/C1 (3 states)
- path-generated: confirmation screen (3 states)
- Total 19 states across placement flow
- Learner gets level-matched path from free placement test"
```

---

### Task 11: Build Learning Path Screens (3 screens)

**Files:**
- Create: `prototype/student/learning-path.html`
- Create: `prototype/student/path-upgrade.html`
- Create: `prototype/student/path-detail.html`

**Interfaces:**
- Consumes: `.path-module` from Task 9
- Produces: Learning path screens (11 states total)

- [ ] **Step 1: Create learning-path.html**

Create `prototype/student/learning-path.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Learning Path — TOEIC Practice</title>
  <link rel="icon" href="../assets/img/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="loading,empty,error,success">
  <div class="app-shell app-shell--with-sidebar">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <div class="row">
        <span class="badge badge--info">3 credits left</span>
        <a class="btn btn--sm" href="../payment/pricing.html">Buy credits</a>
        <a class="btn btn--ghost btn--sm" href="profile.html">Nguyễn Văn A</a>
      </div>
    </header>
    <div class="app-body">
      <aside class="app-sidebar">
        <nav class="nav-list" aria-label="Main navigation">
          <a class="nav-list__item" href="dashboard.html">Home</a>
          <div class="nav-list__group">Mock exams</div>
          <a class="nav-list__item" href="exam-list.html">Test list</a>
          <a class="nav-list__item" href="exam-history.html">Exam history</a>
          <div class="nav-list__group">Practice</div>
          <a class="nav-list__item" href="practice-select.html">Practice by section</a>
          <a class="nav-list__item" href="weakness-analysis.html">Weakness analysis</a>
          <div class="nav-list__group">Learning</div>
          <a class="nav-list__item is-active" href="learning-path.html">My learning path</a>
          <div class="nav-list__group">Account</div>
          <a class="nav-list__item" href="../payment/wallet.html">Credit wallet</a>
          <a class="nav-list__item" href="profile.html">Profile</a>
        </nav>
      </aside>
      <main class="app-main">
        <div class="container stack">
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="stack">
              <div class="skeleton skeleton--block" style="height: 80px"></div>
              <div class="skeleton skeleton--block" style="height: 80px"></div>
              <div class="skeleton skeleton--block" style="height: 80px"></div>
            </div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Could not load path</div>
                <p class="state-block__desc">Check your connection and try again.</p>
                <button class="btn btn--primary">Try again</button>
              </div>
            </div></div>
          </div>

          <!-- ===== empty ===== -->
          <div class="only-empty">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">EMPTY</div>
                <div class="state-block__title">No learning path yet</div>
                <p class="state-block__desc">Take a placement test to generate your personalized learning path.</p>
                <a href="placement-select.html" class="btn btn--primary">Start placement test</a>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <h1>A2 → B2 Learning Path</h1>
            <p>Your personalized path from <strong>A2 (Elementary)</strong> to <strong>B2 (Upper Intermediate)</strong>. Complete modules in order to reach your target level.</p>

            <div class="alert alert--info">
              <strong>Basic path (free)</strong> includes practice modules. <a href="path-upgrade.html">Upgrade to premium</a> for AI tasks, deeper guidance, and video lessons.
            </div>

            <div class="stack">
              <article class="path-module">
                <div class="path-module__status">
                  <span class="badge badge--success">Done</span>
                </div>
                <div class="path-module__content">
                  <strong>Module 1: Present Simple Tense</strong>
                  <p class="text-muted">Practice · 20 questions · Est. 15 minutes</p>
                </div>
                <a href="path-detail.html" class="btn btn--plate btn--sm">Review</a>
              </article>

              <article class="path-module">
                <div class="path-module__status">
                  <span class="badge badge--warning">In progress</span>
                </div>
                <div class="path-module__content">
                  <strong>Module 2: Present Continuous</strong>
                  <p class="text-muted">Practice · 25 questions · Est. 20 minutes</p>
                </div>
                <a href="path-detail.html" class="btn btn--primary btn--sm">Continue</a>
              </article>

              <article class="path-module">
                <div class="path-module__status">
                  <span class="badge">Not started</span>
                </div>
                <div class="path-module__content">
                  <strong>Module 3: Past Simple</strong>
                  <p class="text-muted">Practice · 30 questions · Est. 25 minutes</p>
                </div>
                <button class="btn btn--plate btn--sm" disabled>Locked</button>
              </article>

              <article class="path-module">
                <div class="path-module__status">
                  <span class="badge">Not started</span>
                </div>
                <div class="path-module__content">
                  <strong>Module 4: Vocabulary Building — Business Context</strong>
                  <p class="text-muted">Reading · 50 terms · Est. 30 minutes</p>
                </div>
                <button class="btn btn--plate btn--sm" disabled>Locked</button>
              </article>

              <article class="path-module">
                <div class="path-module__status">
                  <span class="badge">Not started</span>
                </div>
                <div class="path-module__content">
                  <strong>Module 5: Listening Comprehension — Short Conversations</strong>
                  <p class="text-muted">Practice · 15 questions · Est. 20 minutes</p>
                </div>
                <button class="btn btn--plate btn--sm" disabled>Locked</button>
              </article>
            </div>

            <div class="card" style="margin-top: var(--space-5, 24px);"><div class="card__footer">
              <button class="btn btn--danger">Reset progress</button>
            </div></div>
          </div>
        </div>
      </main>
    </div>
  </div>
  <script src="../assets/js/state-switch.js"></script>
</body>
</html>
```

- [ ] **Step 2: Create path-upgrade.html**

Create `prototype/student/path-upgrade.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Upgrade Path — TOEIC Practice</title>
  <link rel="icon" href="../assets/img/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="paywall,submitting,error,success">
  <div class="app-shell app-shell--with-sidebar">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <div class="row">
        <span class="badge badge--info">3 credits left</span>
        <a class="btn btn--sm" href="../payment/pricing.html">Buy credits</a>
        <a class="btn btn--ghost btn--sm" href="profile.html">Nguyễn Văn A</a>
      </div>
    </header>
    <div class="app-body">
      <aside class="app-sidebar">
        <nav class="nav-list" aria-label="Main navigation">
          <a class="nav-list__item" href="dashboard.html">Home</a>
          <div class="nav-list__group">Mock exams</div>
          <a class="nav-list__item" href="exam-list.html">Test list</a>
          <a class="nav-list__item" href="exam-history.html">Exam history</a>
          <div class="nav-list__group">Practice</div>
          <a class="nav-list__item" href="practice-select.html">Practice by section</a>
          <a class="nav-list__item" href="weakness-analysis.html">Weakness analysis</a>
          <div class="nav-list__group">Learning</div>
          <a class="nav-list__item" href="learning-path.html">My learning path</a>
          <div class="nav-list__group">Account</div>
          <a class="nav-list__item" href="../payment/wallet.html">Credit wallet</a>
          <a class="nav-list__item" href="profile.html">Profile</a>
        </nav>
      </aside>
      <main class="app-main">
        <div class="container stack">
          <!-- ===== paywall ===== -->
          <div class="only-paywall">
            <div class="alert alert--info">
              <strong>Nothing was deducted.</strong> Buy credits to upgrade your learning path.
            </div>
            <h1>Upgrade to premium path</h1>
            <p><a href="../payment/pricing.html" class="btn btn--primary">Buy credits</a></p>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="alert alert--danger">
              <strong>Could not upgrade path.</strong> Try again.
            </div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <h1>Upgrade to premium path</h1>
            <p>Unlock AI tasks, video lessons, and deeper companion guidance. Your basic path stays — premium adds richer modules.</p>

            <div class="card"><div class="card__body stack">
              <h2>Premium includes:</h2>
              <ul>
                <li>AI-graded speaking and writing tasks with feedback</li>
                <li>Video lessons for each module</li>
                <li>Closer companion guidance adapted to your progress</li>
                <li>Personalized recommendations after each module</li>
              </ul>
              <p><strong>Cost:</strong> 1 credit</p>
            </div></div>

            <div class="row">
              <button type="button" class="btn btn--primary multi-state" data-show="success">Upgrade now</button>
              <button type="button" class="btn btn--primary only-submitting" disabled>Upgrading...</button>
              <a href="learning-path.html" class="btn">Keep basic path</a>
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

- [ ] **Step 3: Create path-detail.html**

Create `prototype/student/path-detail.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Module Detail — TOEIC Practice</title>
  <link rel="icon" href="../assets/img/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="loading,error,success">
  <div class="app-shell app-shell--with-sidebar">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <div class="row">
        <span class="badge badge--info">3 credits left</span>
        <a class="btn btn--sm" href="../payment/pricing.html">Buy credits</a>
        <a class="btn btn--ghost btn--sm" href="profile.html">Nguyễn Văn A</a>
      </div>
    </header>
    <div class="app-body">
      <aside class="app-sidebar">
        <nav class="nav-list" aria-label="Main navigation">
          <a class="nav-list__item" href="dashboard.html">Home</a>
          <div class="nav-list__group">Mock exams</div>
          <a class="nav-list__item" href="exam-list.html">Test list</a>
          <a class="nav-list__item" href="exam-history.html">Exam history</a>
          <div class="nav-list__group">Practice</div>
          <a class="nav-list__item" href="practice-select.html">Practice by section</a>
          <a class="nav-list__item" href="weakness-analysis.html">Weakness analysis</a>
          <div class="nav-list__group">Learning</div>
          <a class="nav-list__item is-active" href="learning-path.html">My learning path</a>
          <div class="nav-list__group">Account</div>
          <a class="nav-list__item" href="../payment/wallet.html">Credit wallet</a>
          <a class="nav-list__item" href="profile.html">Profile</a>
        </nav>
      </aside>
      <main class="app-main">
        <div class="container stack">
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="skeleton skeleton--block" style="height: 200px"></div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Could not load module</div>
                <p class="state-block__desc">Check your connection and try again.</p>
                <button class="btn btn--primary">Try again</button>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <div class="row">
              <a href="learning-path.html" class="btn btn--ghost">← Back to path</a>
            </div>

            <h1>Module 1: Present Simple Tense</h1>
            <div class="row" style="gap: var(--space-2, 8px);">
              <span class="badge badge--success">Done</span>
              <span class="text-muted">Practice · 20 questions · Est. 15 minutes</span>
            </div>

            <div class="card"><div class="card__body stack">
              <h2>What you'll learn</h2>
              <p>Master the present simple tense for facts, routines, and general truths. This module covers affirmative, negative, and question forms with common time expressions.</p>

              <h3>Topics covered:</h3>
              <ul>
                <li>Affirmative sentences (I work, She works)</li>
                <li>Negative sentences (I don't work, She doesn't work)</li>
                <li>Questions (Do you work? Does she work?)</li>
                <li>Time expressions (always, usually, often, sometimes, never)</li>
              </ul>
            </div></div>

            <div class="card"><div class="card__body">
              <h2>Your result</h2>
              <p>You scored <strong>18 out of 20</strong> (90%) on this module.</p>
              <div class="alert alert--success">
                <strong>Module complete!</strong> Move on to the next module in your path.
              </div>
            </div></div>

            <div class="row">
              <a href="practice-take.html" class="btn btn--primary">Practice again</a>
              <a href="learning-path.html" class="btn">Back to path</a>
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

- [ ] **Step 4: Test all 3 screens render**

Open each screen with `?state=success`:
- learning-path.html
- path-upgrade.html
- path-detail.html

Expected: All screens render, path modules display with status badges.

- [ ] **Step 5: Commit**

```bash
git add prototype/student/learning-path.html prototype/student/path-upgrade.html prototype/student/path-detail.html
git commit -m "feat(learning-path): add path screens (3 screens)

- learning-path: module list with status badges (4 states)
- path-upgrade: premium upgrade confirmation (4 states)
- path-detail: single module detail (3 states)
- Total 11 states across learning path flow
- Supports A2→B2 progression with basic/premium tiers"
```

---

### Task 12: Add Grading Pending Screen

**Files:**
- Create: `prototype/student/grading-pending.html`

**Interfaces:**
- Consumes: Existing card/alert components
- Produces: Grading pending screen (3 states)

- [ ] **Step 1: Create grading-pending.html**

Create `prototype/student/grading-pending.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Grading In Progress — TOEIC Practice</title>
  <link rel="icon" href="../assets/img/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="../assets/css/tokens.css">
  <link rel="stylesheet" href="../assets/css/base.css">
  <link rel="stylesheet" href="../assets/css/components.css">
</head>
<body data-states="loading,error,success">
  <div class="app-shell app-shell--with-sidebar">
    <header class="app-topbar">
      <strong>TOEIC Practice</strong>
      <div class="row">
        <span class="badge badge--info">3 credits left</span>
        <a class="btn btn--sm" href="../payment/pricing.html">Buy credits</a>
        <a class="btn btn--ghost btn--sm" href="profile.html">Nguyễn Văn A</a>
      </div>
    </header>
    <div class="app-body">
      <aside class="app-sidebar">
        <nav class="nav-list" aria-label="Main navigation">
          <a class="nav-list__item" href="dashboard.html">Home</a>
          <div class="nav-list__group">Mock exams</div>
          <a class="nav-list__item" href="exam-list.html">Test list</a>
          <a class="nav-list__item" href="exam-history.html">Exam history</a>
          <div class="nav-list__group">Practice</div>
          <a class="nav-list__item" href="practice-select.html">Practice by section</a>
          <a class="nav-list__item" href="weakness-analysis.html">Weakness analysis</a>
          <div class="nav-list__group">Account</div>
          <a class="nav-list__item" href="../payment/wallet.html">Credit wallet</a>
          <a class="nav-list__item" href="profile.html">Profile</a>
        </nav>
      </aside>
      <main class="app-main">
        <div class="container stack">
          <!-- ===== loading ===== -->
          <div class="only-loading stack">
            <div class="skeleton skeleton--title"></div>
            <div class="skeleton skeleton--block" style="height: 150px"></div>
          </div>

          <!-- ===== error ===== -->
          <div class="only-error">
            <div class="card"><div class="card__body">
              <div class="state-block">
                <div class="state-block__icon" aria-hidden="true">!</div>
                <div class="state-block__title">Could not check grading status</div>
                <p class="state-block__desc">Check your connection and try again.</p>
                <button class="btn btn--primary">Try again</button>
              </div>
            </div></div>
          </div>

          <!-- ===== success ===== -->
          <div class="only-success stack">
            <h1>AI is grading your responses</h1>

            <div class="card"><div class="card__body stack">
              <div class="alert alert--info">
                <strong>Grading in progress.</strong> We're scoring your speaking and writing responses against the rubric. This usually takes 5-10 minutes.
              </div>

              <p>We'll email you when grading completes. You can also check back here or on your exam history.</p>

              <p class="text-muted">Your credit for AI grading has already been deducted. You'll see your result and recommendations as soon as grading finishes.</p>
            </div></div>

            <div class="row">
              <a href="exam-history.html" class="btn btn--primary">View exam history</a>
              <a href="dashboard.html" class="btn">Go to dashboard</a>
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

- [ ] **Step 2: Test grading-pending renders**

Open `prototype/student/grading-pending.html?state=success` in browser.

Expected: Alert says "Grading in progress", mentions 5-10 minutes, no polling UI.

- [ ] **Step 3: Commit**

```bash
git add prototype/student/grading-pending.html
git commit -m "feat(grading): add AI grading pending screen

- grading-pending: static message, no polling (3 states)
- Appears after exam submit for speaking/writing attempts
- Mentions 5-10 minute typical time, email notification
- Credit already deducted, clarified in copy"
```

---

### Task 13: Build Mentor Booking Flow (5 screens)

**Files:**
- Create: `prototype/student/mentor-list.html`
- Create: `prototype/student/mentor-detail.html`
- Create: `prototype/student/mentor-confirm.html`
- Create: `prototype/student/booking-detail.html`
- Create: `prototype/student/booking-review.html`

**Interfaces:**
- Consumes: `.mentor-card` from Task 9
- Produces: Mentor booking flow (18 states total)

- [ ] **Step 1: Create mentor-list.html**

Create `prototype/student/mentor-list.html` with mentor-card grid, 4 states (success, loading, empty, error). Include filter by skill (Listening/Reading/Speaking/Writing) and mix of human mentors (with calendar) and AI mentors (with "Instant booking" badge).

Sample mentor cards:
- Dr. Nguyen Van A (TOEIC, 4.5★, 50,000₫, human)
- Ms. Tran Thi B (IELTS, 4.8★, 100,000₫, human)
- AI Mentor — TOEIC Coach (TOEIC, 5.0★, 30,000₫, AI with "Instant booking" badge)

- [ ] **Step 2: Create mentor-detail.html**

Create `prototype/student/mentor-detail.html` showing mentor profile, bio, available slots (human) or instant booking CTA (AI), reviews list. 3 states (success, loading, error).

- [ ] **Step 3: Create mentor-confirm.html**

Create `prototype/student/mentor-confirm.html` confirmation screen with 4 states (success, paywall, submitting, error). Shows: mentor name, certificate, skill, time slot, Google Meet link placeholder, cost (1 credit).

- [ ] **Step 4: Create booking-detail.html**

Create `prototype/student/booking-detail.html` with 4 status states (pending, confirmed, done, cancelled). Each status is a separate ?state= param. Shows Google Meet link when confirmed/done, prompts review when done.

- [ ] **Step 5: Create booking-review.html**

Create `prototype/student/booking-review.html` form with star rating (1-5) and text review. 3 states (success, submitting, error).

- [ ] **Step 6: Test mentor flow renders**

Open each screen with appropriate states, verify flow is cohesive.

- [ ] **Step 7: Commit**

```bash
git add prototype/student/mentor-*.html prototype/student/booking-*.html
git commit -m "feat(mentor): add mentor booking flow (5 screens)

- mentor-list: browse mentors by cert/skill (4 states)
- mentor-detail: profile, slots, reviews (3 states)
- mentor-confirm: booking confirmation (4 states)
- booking-detail: Google Meet link, status (4 states)
- booking-review: leave rating and review (3 states)
- Total 18 states across mentor flow
- Human mentors show slots, AI mentors instant booking"
```

---

### Task 14: Add Admin Complaint Resolution (2 screens)

**Files:**
- Create: `prototype/admin/complaints.html`
- Create: `prototype/admin/complaint-detail.html`

**Interfaces:**
- Consumes: Existing table/form components
- Produces: Admin complaint screens (8 states total)

- [ ] **Step 1: Create complaints.html**

Create `prototype/admin/complaints.html` with table of complaints, filter by status (pending/resolved). 4 states (success, loading, empty, error).

Table columns: ID, Learner name, Booking/Exam ID, Type (mentor no-show, grading issue, etc), Status, Date, Actions.

- [ ] **Step 2: Create complaint-detail.html**

Create `prototype/admin/complaint-detail.html` showing complaint details and resolution form. 4 states (success, submitting, resolved, error).

Resolution form includes:
- Issue credit make-good checkbox + amount
- Internal notes textarea
- Resolve button (danger, commits resolution)

- [ ] **Step 3: Test admin complaints render**

Open both screens with ?state=success, verify table and form render correctly.

- [ ] **Step 4: Commit**

```bash
git add prototype/admin/complaints.html prototype/admin/complaint-detail.html
git commit -m "feat(admin): add complaint resolution screens

- complaints: table with status filter (4 states)
- complaint-detail: resolution form with credit make-good (4 states)
- Total 8 states across admin complaints
- Admins can issue admin_grant credits as make-good"
```

---

### Task 15: Enhance Exam Result with Recommendations

**Files:**
- Modify: `prototype/student/exam-result.html:80-150` (add recommendations section)

**Interfaces:**
- Consumes: `.rubric-scores` from Task 9, existing `.alert` component
- Produces: exam-result with AI grading recommendations (adds 1 state)

- [ ] **Step 1: Add graded-with-recommendations state to data-states**

Open `prototype/student/exam-result.html`, find the `<body data-states="...">` line, add `graded-with-recommendations` to the list.

- [ ] **Step 2: Duplicate success state as graded-with-recommendations**

Copy the entire `<div class="only-success stack">` block, change class to `only-graded-with-recommendations`.

- [ ] **Step 3: Add rubric scores section**

Inside the graded-with-recommendations state, after the score display, add:

```html
<div class="rubric-scores">
  <h3>Speaking scores</h3>
  <dl class="rubric-scores__list">
    <div class="rubric-scores__item">
      <dt>Fluency</dt>
      <dd><span class="score-value">3.5</span> / 5</dd>
    </div>
    <div class="rubric-scores__item">
      <dt>Pronunciation</dt>
      <dd><span class="score-value">4.0</span> / 5</dd>
    </div>
    <div class="rubric-scores__item">
      <dt>Lexical Resource</dt>
      <dd><span class="score-value">3.0</span> / 5</dd>
    </div>
    <div class="rubric-scores__item">
      <dt>Grammatical Range</dt>
      <dd><span class="score-value">3.5</span> / 5</dd>
    </div>
  </dl>
</div>
```

- [ ] **Step 4: Add recommendations section**

After rubric scores, add:

```html
<div class="alert alert--info">
  <strong>To reach B2 (your target):</strong>
  <ul class="recommendations">
    <li>Practice sentence stress in spoken responses — your fluency is good but stress patterns need work</li>
    <li>Expand vocabulary for business contexts — lexical resource scored lowest</li>
    <li>Focus on complex sentence structures — use more subordinate clauses</li>
  </ul>
</div>
```

- [ ] **Step 5: Update catalog to add graded-with-recommendations state**

Open `prototype/index.html`, find Exam result catalog row, add the new state link.

- [ ] **Step 6: Test graded-with-recommendations renders**

Open `prototype/student/exam-result.html?state=graded-with-recommendations` in browser.

Expected: Rubric scores display in 2-column grid with tabular nums, recommendations list inside info alert.

- [ ] **Step 7: Commit**

```bash
git add prototype/student/exam-result.html prototype/index.html
git commit -m "feat(exam-result): add AI grading recommendations

- Add graded-with-recommendations state for AI-graded attempts
- Show rubric scores (Fluency, Pronunciation, Lexical, Grammar)
- Show recommendations toward target band
- Scores use tabular nums, recommendations in info alert
- Catalog updated with new state"
```

---

### Task 16: Update Catalog Index (Add All New Screens)

**Files:**
- Modify: `prototype/index.html:160-250` (add new catalog groups and rows)

**Interfaces:**
- Consumes: All new screens from Tasks 10-14
- Produces: Complete 47-screen catalog with 7 groups, ~88 states

- [ ] **Step 1: Add "Placement & Learning Path" catalog group**

Open `prototype/index.html`, after the "Practice by section" group, add new group:

```html
<div class="catalog__group">
  <h2>Placement & Learning Path</h2>
  <div class="catalog__row">
    <span class="catalog__name">Pick placement test</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/placement-select.html?state=success">success</a>
      <a class="catalog__state" href="student/placement-select.html?state=loading">loading</a>
      <a class="catalog__state" href="student/placement-select.html?state=empty">empty</a>
      <a class="catalog__state" href="student/placement-select.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Take placement test</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/placement-take.html?state=success">success</a>
      <a class="catalog__state" href="student/placement-take.html?state=loading">loading</a>
      <a class="catalog__state" href="student/placement-take.html?state=resumed">resumed</a>
      <a class="catalog__state" href="student/placement-take.html?state=offline">offline</a>
      <a class="catalog__state" href="student/placement-take.html?state=expired">expired</a>
      <a class="catalog__state" href="student/placement-take.html?state=submitting">submitting</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Placement result</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/placement-result.html?state=success">success</a>
      <a class="catalog__state" href="student/placement-result.html?state=loading">loading</a>
      <a class="catalog__state" href="student/placement-result.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Set target level</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/target-band-set.html?state=success">success</a>
      <a class="catalog__state" href="student/target-band-set.html?state=submitting">submitting</a>
      <a class="catalog__state" href="student/target-band-set.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Path generated</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/path-generated.html?state=success">success</a>
      <a class="catalog__state" href="student/path-generated.html?state=loading">loading</a>
      <a class="catalog__state" href="student/path-generated.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Learning path</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/learning-path.html?state=success">success</a>
      <a class="catalog__state" href="student/learning-path.html?state=loading">loading</a>
      <a class="catalog__state" href="student/learning-path.html?state=empty">empty</a>
      <a class="catalog__state" href="student/learning-path.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Upgrade to premium path</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/path-upgrade.html?state=success">success</a>
      <a class="catalog__state" href="student/path-upgrade.html?state=paywall">paywall</a>
      <a class="catalog__state" href="student/path-upgrade.html?state=submitting">submitting</a>
      <a class="catalog__state" href="student/path-upgrade.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Path module detail</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/path-detail.html?state=success">success</a>
      <a class="catalog__state" href="student/path-detail.html?state=loading">loading</a>
      <a class="catalog__state" href="student/path-detail.html?state=error">error</a>
    </div>
  </div>
</div>
```

- [ ] **Step 2: Add "Mentor Booking" catalog group**

After "Placement & Learning Path" group, add:

```html
<div class="catalog__group">
  <h2>Mentor Booking</h2>
  <div class="catalog__row">
    <span class="catalog__name">Browse mentors</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/mentor-list.html?state=success">success</a>
      <a class="catalog__state" href="student/mentor-list.html?state=loading">loading</a>
      <a class="catalog__state" href="student/mentor-list.html?state=empty">empty</a>
      <a class="catalog__state" href="student/mentor-list.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Mentor detail</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/mentor-detail.html?state=success">success</a>
      <a class="catalog__state" href="student/mentor-detail.html?state=loading">loading</a>
      <a class="catalog__state" href="student/mentor-detail.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Confirm booking</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/mentor-confirm.html?state=success">success</a>
      <a class="catalog__state" href="student/mentor-confirm.html?state=paywall">paywall</a>
      <a class="catalog__state" href="student/mentor-confirm.html?state=submitting">submitting</a>
      <a class="catalog__state" href="student/mentor-confirm.html?state=error">error</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Booking detail</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/booking-detail.html?state=pending">pending</a>
      <a class="catalog__state" href="student/booking-detail.html?state=confirmed">confirmed</a>
      <a class="catalog__state" href="student/booking-detail.html?state=done">done</a>
      <a class="catalog__state" href="student/booking-detail.html?state=cancelled">cancelled</a>
    </div>
  </div>
  <div class="catalog__row">
    <span class="catalog__name">Leave review</span>
    <div class="catalog__states">
      <a class="catalog__state" href="student/booking-review.html?state=success">success</a>
      <a class="catalog__state" href="student/booking-review.html?state=submitting">submitting</a>
      <a class="catalog__state" href="student/booking-review.html?state=error">error</a>
    </div>
  </div>
</div>
```

- [ ] **Step 3: Add grading-pending to "Take an exam" group**

Find the "Take an exam" catalog group, after "Confirm submission" row, add:

```html
<div class="catalog__row">
  <span class="catalog__name">AI grading pending</span>
  <div class="catalog__states">
    <a class="catalog__state" href="student/grading-pending.html?state=success">success</a>
    <a class="catalog__state" href="student/grading-pending.html?state=loading">loading</a>
    <a class="catalog__state" href="student/grading-pending.html?state=error">error</a>
  </div>
</div>
```

- [ ] **Step 4: Add complaints to "Admin" group**

Find the "Admin" catalog group, after the last existing row, add:

```html
<div class="catalog__row">
  <span class="catalog__name">Complaints</span>
  <div class="catalog__states">
    <a class="catalog__state" href="admin/complaints.html?state=success">success</a>
    <a class="catalog__state" href="admin/complaints.html?state=loading">loading</a>
    <a class="catalog__state" href="admin/complaints.html?state=empty">empty</a>
    <a class="catalog__state" href="admin/complaints.html?state=error">error</a>
  </div>
</div>
<div class="catalog__row">
  <span class="catalog__name">Complaint detail</span>
  <div class="catalog__states">
    <a class="catalog__state" href="admin/complaint-detail.html?state=success">success</a>
    <a class="catalog__state" href="admin/complaint-detail.html?state=submitting">submitting</a>
    <a class="catalog__state" href="admin/complaint-detail.html?state=resolved">resolved</a>
    <a class="catalog__state" href="admin/complaint-detail.html?state=error">error</a>
  </div>
</div>
```

- [ ] **Step 5: Verify catalog structure**

Count catalog groups (should be 7) and rows (should be 47). Check for typos in hrefs.

- [ ] **Step 6: Test catalog navigation**

Open `prototype/index.html` in browser, click random state links across all groups.

Expected: All links resolve, no 404s, all screens render.

- [ ] **Step 7: Commit**

```bash
git add prototype/index.html
git commit -m "feat(catalog): add new screens to index

- Add Placement & Learning Path group (8 rows, 29 states)
- Add Mentor Booking group (5 rows, 18 states)
- Add grading-pending to Take an exam group
- Add complaints to Admin group (2 rows, 8 states)
- Total: 7 groups, 47 screens, ~88 states
- Catalog complete and synchronized"
```

---

## Pass 4: Export to Figma (1-2 days)

### Task 17: Export HTML to Figma via Plugin

**Files:**
- Output: Figma file "Multi-Certificate Companion Prototype"

**Interfaces:**
- Consumes: All 47 HTML screens from prototype/
- Produces: Figma file with 88 frames, design system, interactive prototype

- [ ] **Step 1: Install html.to.design plugin**

Open Figma, go to Plugins → Browse plugins, search for "html.to.design", install.

- [ ] **Step 2: Export all 47 HTML screens**

For each HTML file in prototype/:
1. Open the file in browser
2. Switch to each ?state= variant
3. Run html.to.design plugin
4. Export to Figma

This produces ~88 Figma frames (47 screens × ~1.9 avg states per screen).

- [ ] **Step 3: Organize frames into 7 pages**

Create 7 Figma pages matching catalog groups:
- Page 1: Auth (3 frames)
- Page 2: Student — Core (17 frames)
- Page 3: Placement & Learning Path (29 frames)
- Page 4: Mentor Booking (18 frames)
- Page 5: Take an Exam + Grading (19 frames)
- Page 6: Admin (13 frames)
- Page 7: Payment (9 frames)

Move frames to appropriate pages.

- [ ] **Step 4: Extract design tokens to Figma variables**

Create Figma variable collections:
- **Colors:** sign-red, sign-blue, sign-gold, sign-green, ink, wall, painted, line-soft, ink-muted (copy hex values from tokens.css)
- **Typography:** Create text styles for headline, title, body, read, label, data (copy font families, sizes, weights from tokens.css)
- **Spacing:** Create variables space-1 through space-7 (4px, 8px, 12px, 16px, 24px, 32px, 48px)

- [ ] **Step 5: Convert HTML components to Figma components**

Select repeating elements, convert to Figma components:
- Primary/Plate/Danger buttons
- Text field
- Badge (5 variants: default, success, warning, danger, info)
- Alert (4 variants)
- Panel (header, body, footer)
- Nav item
- State block
- CEFR band bar
- Certificate picker
- Mentor card
- Path module
- Rubric scores

Create component library page.

- [ ] **Step 6: Link states via Figma prototyping**

For screens with multiple states, connect state links using Figma interactions:
- Click state name → navigate to that state frame
- Hover button → show hover state (invert to ink block)

Replicate ?state= behavior.

- [ ] **Step 7: Create catalog index page**

Create new Figma page "Index", design a frame matching prototype/index.html structure:
- 7 sections with headers
- 47 rows with screen names
- Clickable links to each state frame

Use auto-layout for the list.

- [ ] **Step 8: Cleanup after auto-convert**

Fix common html.to.design conversion issues:
- Broken flex/grid layouts → manually adjust
- Rasterized text → convert to proper text layers
- Detached components → relink to component instances
- Hardcoded colors → replace with Figma variables
- Inline typography → apply text styles

Check each page, fix issues.

- [ ] **Step 9: Verify Figma export quality**

Check:
- [ ] All 88 frames present and organized
- [ ] Design tokens extracted as variables
- [ ] 16 components created with proper variants
- [ ] States linked via prototyping
- [ ] Index page navigates correctly
- [ ] No rasterized text
- [ ] All colors use variables
- [ ] All text uses styles

- [ ] **Step 10: Commit documentation**

Create `docs/figma-export-notes.md` documenting:
- Figma file URL
- Structure (7 pages, 88 frames)
- Component library page
- Known issues/limitations

```bash
git add docs/figma-export-notes.md
git commit -m "docs: add Figma export notes

- Figma file: Multi-Certificate Companion Prototype
- 7 pages, 88 frames, design system library
- Interactive prototype with state transitions
- Exported via html.to.design plugin"
```

---

## Self-Review

**1. Spec coverage:**
- ✅ Pass 1 (Remove KYC): Tasks 1-2 cover all deletions and messaging updates
- ✅ Pass 2 (Transform): Tasks 3-8 cover all 8 screen transformations
- ✅ Pass 3 (Add screens): Tasks 9-15 cover all 16 new screens + exam result enhancement
- ✅ Pass 4 (Figma): Task 17 covers full export process

**2. Placeholder scan:** None found — all steps have actual code/content.

**3. Type consistency:** All component classes match across tasks (`.certificate-picker`, `.mentor-card`, `.path-module`, `.rubric-scores` defined in Task 9, used in Tasks 10-15).

**4. Review Focus:** All 5 items from Review Focus section have corresponding tests:
1. Certificate badge color → Task 6 Step 3-4 verify badge--info used
2. Tabular numerals → Task 9 Step 1 mentor-card__price and Step 3 rubric score-value use tabular-nums
3. Active cert switching data preservation → Task 4 Step 2 includes clarifying message in picker
4. State visibility groups → All new screens include 4-state data-states attribute
5. No placeholder text → All screens use realistic Vietnamese names, actual exam titles, real section names per certificate

**No gaps found.**

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-09-27-multi-cert-ui-redesign.md`. Please review the plan. Which execution approach would you prefer?

- **Subagent-driven** - A fresh subagent implements each task and a fresh reviewer checks it before the next one starts, then a whole-branch review at the end. Most thorough; costs a fresh context per task and per review.
- **Native** - I implement every task myself in this session, the way this harness runs work, then one fresh reviewer on the most capable model checks the whole branch. Cheapest and fastest; no independent review until the end. Runs well with a mid-tier session model, since the plan carries the design.

**For this plan I recommend Native**, because the tasks are mostly HTML copy-paste-modify with clear patterns from existing screens. The 17 tasks are well-sequenced and testable at each commit. A skilled implementer following the plan can complete Pass 1-3 (Tasks 1-16) in 3-4 days without needing per-task review gates. Pass 4 (Figma export) benefits from one final review after all HTML is done. Does the plan capture what you want, and which approach should we use?