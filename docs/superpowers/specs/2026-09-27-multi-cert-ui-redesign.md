# Multi-Certificate Companion — UI Prototype Redesign

Date: 2026-09-27
Status: Design (awaiting approval)
Supersedes: The pre-pivot TOEIC-only static prototype under `prototype/`

## Why This Redesign

The database pivoted (2026-09-24) from an 18-table TOEIC-only, KYC-gated system to a 41-table multi-certificate companion platform. The schema is done, tested, and documented. The UI is frozen at the pre-pivot state.

**The gap:** 34 HTML screens still implement TOEIC-only + KYC gating, while the database now supports multiple certificates (TOEIC/IELTS/VSTEP), placement tests, AI grading, mentor booking, and learning paths — with no identity verification.

**The fix:** Update the static HTML prototype to match the new schema, maintaining the "exam-room signage" design system and state-coverage discipline throughout.

## Scope

### Remove (Pass 1)
- **3 screens:** `student/kyc-submit.html`, `student/kyc-pending.html`, `admin/kyc-review.html`
- **1 catalog group:** "ID verification (gate before the real exam)"
- **Messaging:** Strip "verify your ID first" from paywall messages
- **Profile fields:** Remove CCCD/national-ID references

### Transform (Pass 2)
- **Dashboard** — Add certificate picker (first-time) or active cert display (returning user)
- **Profile** — Add "Active certificate" dropdown, remove KYC fields
- **Exam list** — Filter to active certificate, add certificate badge to cards
- **Exam history** — Show only active certificate's attempts
- **Practice select** — Change from "Part 1-7" hardcoded to data-driven sections per certificate
- **Exam instructions** — Paywall becomes "Buy credits to start" (no KYC gate)
- **Admin exam editor** — Add certificate picker, skills/sections load per certificate
- **Admin score conversion** — Split into two tables: raw→scaled (editable per exam), scaled→band (read-only per certificate)

### Add (Pass 3)

**Placement test flow (5 screens):**
1. `student/placement-select.html` — Pick placement exam for active certificate
2. `student/placement-take.html` — Take placement test (reuses exam-take pattern)
3. `student/placement-result.html` — Show band, prompt to set target band
4. `student/target-band-set.html` — Set target band for this certificate
5. `student/path-generated.html` — "Your learning path is ready"

**Learning path screens (3 screens):**
6. `student/learning-path.html` — View generated path, module list, progress
7. `student/path-upgrade.html` — Upgrade to premium path (credit cost)
8. `student/path-detail.html` — Single path module detail

**AI grading screen (1 screen):**
9. `student/grading-pending.html` — "AI is grading your responses, check back soon"

**Mentor booking flow (5 screens):**
10. `student/mentor-list.html` — Browse mentors by (certificate, skill), show ratings
11. `student/mentor-detail.html` — Mentor profile, available slots, reviews
12. `student/mentor-confirm.html` — Confirm booking (credit cost, paywall check)
13. `student/booking-detail.html` — Show Google Meet link, mentor info, status
14. `student/booking-review.html` — Leave rating and review after session

**Admin screens (2 screens):**
15. `admin/complaints.html` — View complaints, filter by status
16. `admin/complaint-detail.html` — Single complaint, resolution form, issue credit make-good

**Exam result enhancement:**
- Add "Recommendations toward your target band" section (only for AI-graded attempts)
- Show rubric scores (Fluency, Pronunciation, Lexical, Grammar)

### Export to Figma (Pass 4)
- Convert all 47 HTML screens to Figma frames via HTML-to-Figma plugin
- Organize by flow (Auth, Student, Admin, Payment, New flows)
- Extract design tokens to Figma variables
- Convert HTML components to Figma components
- Link states via Figma prototyping (replicate ?state= behavior)
- Create index/catalog page in Figma

## Design Constraints (Locked)

1. **Visual design system stays intact** — flat painted fields, 2px ink keylines, zero radius, zero shadow, IBM Plex typography, four sign colors (red #b31217, blue #0b4ea2, gold #f2b705, green #0a6b3c)
2. **State-switcher pattern preserved** — every screen has states, all reachable via `?state=` query params, catalog index synchronized
3. **Static prototype** — HTML5 + CSS3 + vanilla JS (ES2020), no framework, no build step
4. **Desktop-first** — reference viewport 1440×900
5. **Tech stack** — tokens.css / base.css / components.css, state-switch.js

## Architecture

**Final screen count:** 34 original - 3 deleted + 16 new = **47 screens**

**Final state count:** ~26 original + ~60 new = **~86 states**

### File Structure
```
prototype/
├── index.html                        # Catalog (7 groups, 47 rows)
├── assets/
│   ├── css/
│   │   ├── tokens.css               # No changes
│   │   ├── base.css                 # No changes
│   │   └── components.css           # Add: certificate-picker, mentor-card,
│   │                                 #      path-module, rubric-scores
│   └── js/
│       └── state-switch.js          # No changes
├── auth/                            # 3 screens, no changes
├── student/
│   ├── [17 existing kept]           # 8 transformed, 9 unchanged
│   ├── [14 new]                     # Placement, path, mentor, grading
│   └── [deleted: kyc-submit, kyc-pending]
├── admin/
│   ├── [7 existing]                 # 2 transformed, 5 kept
│   ├── complaints.html              # NEW
│   └── complaint-detail.html        # NEW
│   └── [deleted: kyc-review.html]
└── payment/                         # 5 screens, no changes
```

## New Components

All follow the "exam-room signage" system: flat fields, 2px ink keylines, condensed caps, zero radius.

### 1. Certificate Picker
Used at: Dashboard (first-time users), Profile (dropdown)

**Dashboard variant (first-time):**
```html
<div class="certificate-picker">
  <h2>Pick a certificate to start</h2>
  <div class="certificate-picker__grid">
    <button class="certificate-card" data-cert="toeic">
      <span class="badge badge--info">TOEIC</span>
      <strong class="certificate-card__name">Test of English for International Communication</strong>
      <span class="certificate-card__skills">Listening · Reading</span>
    </button>
    <!-- IELTS, VSTEP cards -->
  </div>
</div>
```
- 3-column grid
- Each card: painted panel, 2px ink keyline, hover inverts to ink block with white text
- Badge uses existing `.badge.badge--info` (sign-blue)

**Profile dropdown variant:**
```html
<label class="field">
  <span class="field__label">Active certificate</span>
  <select class="field__input">
    <option value="toeic">TOEIC</option>
    <option value="ielts">IELTS</option>
    <option value="vstep">VSTEP</option>
  </select>
</label>
```

### 2. Active Certificate Display
Used at: Dashboard (returning users)

```html
<div class="active-certificate">
  <span class="text-muted">Currently working on</span>
  <strong class="active-certificate__name">TOEIC</strong>
  <button class="btn btn--plate btn--sm">Switch certificate</button>
</div>
```
- Opens certificate picker modal or navigates to profile

### 3. Certificate Badge
Used at: Exam cards, history items, mentor cards

```html
<span class="badge badge--info">TOEIC</span>
```
- Uses existing badge component, always sign-blue

### 4. Mentor Card
Used at: Mentor list

```html
<article class="mentor-card">
  <div class="mentor-card__header">
    <strong>Dr. Nguyen Van A</strong>
    <span class="badge badge--info">TOEIC</span>
  </div>
  <div class="mentor-card__rating">
    <span class="mentor-card__stars">★★★★☆</span>
    <span class="text-muted">4.5 · 23 reviews</span>
  </div>
  <p class="mentor-card__bio">15 years teaching TOEIC. Specialized in business English.</p>
  <div class="mentor-card__footer">
    <span class="mentor-card__price">50,000₫/session</span>
    <a href="mentor-detail.html" class="btn btn--primary btn--sm">View slots</a>
  </div>
</article>
```
- Painted panel, 2px ink keyline
- Rating uses ★ Unicode, not an icon set (world has no icons)
- Price in monospace (IBM Plex Mono, tabular nums)

### 5. Path Module Card
Used at: Learning path

```html
<article class="path-module">
  <div class="path-module__status">
    <span class="badge badge--success">Done</span>
    <!-- or: badge--warning (In progress), badge (Not started) -->
  </div>
  <div class="path-module__content">
    <strong>Module 1: Present Simple Tense</strong>
    <p class="text-muted">Practice · 20 questions · Est. 15 minutes</p>
  </div>
  <button class="btn btn--plate btn--sm">Start</button>
</article>
```
- Status badge changes per module state
- Uses existing badge + button components

### 6. Rubric Score Display
Used at: Exam result (AI-graded attempts only)

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
    <!-- Lexical, Grammar -->
  </dl>
</div>
```
- Scores in monospace (tabular nums)
- 2-column grid: criterion name | score

### 7. Recommendations List
Used at: Exam result (AI-graded attempts)

```html
<div class="alert alert--info">
  <strong>To reach B2 (your target):</strong>
  <ul class="recommendations">
    <li>Practice sentence stress in spoken responses</li>
    <li>Expand vocabulary for business contexts</li>
  </ul>
</div>
```
- Uses existing `.alert.alert--info` component
- List inside alert, no special styling

## State Coverage Plan

**Standard state sets per screen type:**
- **Data list screens:** success, loading, empty, error (4 states)
- **Form screens:** success, submitting, error (3 states)
- **Confirmation screens:** success, paywall, submitting, error (4 states)
- **Detail screens with status:** Each status is a state (e.g. booking-detail: pending, confirmed, done, cancelled = 4 states)

**New screens state count:**
- Placement flow (5 screens): 4+6+3+3+3 = 19 states
- Learning path (3 screens): 4+4+3 = 11 states
- Grading pending (1 screen): 3 states
- Mentor flow (5 screens): 4+3+4+4+3 = 18 states
- Admin complaints (2 screens): 4+4 = 8 states
- **Total new states: ~60**

**Transformed screens gain states:**
- Dashboard: +1 state (first-time = no active cert)
- Exam-result: +1 state (graded-with-recommendations)
- **Total added to existing: ~2**

**Grand total: ~26 original + ~60 new + ~2 added = ~88 states**

## Data Requirements (Placeholders)

### Certificates (3)
- **TOEIC:** Code "TOEIC", Name "Test of English for International Communication"
  - Skills: Listening (4 sections), Reading (3 sections)
- **IELTS:** Code "IELTS", Name "International English Language Testing System"
  - Skills: Listening (4 sections), Reading (3 sections), Writing (2 sections), Speaking (3 sections)
- **VSTEP:** Code "VSTEP", Name "Vietnamese Standardised Test of English Proficiency"
  - Skills: Similar to IELTS

### Section names (realistic per certificate)
**TOEIC Listening:**
- Section 1: Photographs
- Section 2: Question-Response
- Section 3: Short Conversations
- Section 4: Short Talks

**TOEIC Reading:**
- Section 5: Incomplete Sentences
- Section 6: Text Completion
- Section 7: Reading Comprehension

**IELTS Writing:**
- Task 1: Letter writing
- Task 2: Essay

### Mentors (6-8 sample)
- Mix of human and AI
- Vietnamese names (Dr. Nguyen Van A, Ms. Tran Thi B, etc.)
- Ratings: 3.5-5.0 stars
- Prices: 50,000₫ - 200,000₫/session
- AI mentors: "Instant booking" badge, no slot picker

### Learning Path Modules (5-7 per certificate)
- Module titles: grammar topics, skill practice, vocabulary building
- Types: practice, reading, video, AI task
- Progressive difficulty

### Placement Exams (1 per certificate)
- Shorter than mock exams: 30-40 questions vs 200
- Same UI as exam-take, labeled "Placement Test"

## Screen Flow Map

### New Learner (First Visit)
```
Login/Register
  ↓
Dashboard (no active cert)
  ↓
Certificate Picker → Pick TOEIC
  ↓
Placement-select
  ↓
Placement-take (30-40 questions)
  ↓
Placement-result (band = A2)
  ↓
Target-band-set (target = B2)
  ↓
Path-generated
  ↓
Learning-path (view modules)
  ↓
[Practice / Mock exams / Mentor booking — all TOEIC-scoped]
```

### Returning Learner
```
Dashboard (active cert = TOEIC)
  → Shows: TOEIC band bar, learning path progress, recent attempts
  → Can: Switch cert (opens picker), access practice/exams/mentors (TOEIC-filtered)
```

### Certificate Switching
```
Option A (Dashboard): Click "Switch certificate" → Modal picker → Pick IELTS → Dashboard reloads with IELTS data
Option B (Profile): Navigate to Profile → Change "Active certificate" dropdown → Save → Redirects to Dashboard
```

### Mock Exam with AI Grading
```
Exam-list (filtered to TOEIC) → Pick exam with Speaking/Writing
  ↓
Exam-instructions (deduct 1 credit)
  ↓
Exam-listening → Exam-reading → Exam-speaking → Exam-writing
  ↓
Exam-confirm-submit
  ↓
Grading-pending ("AI is grading, check back soon")
  ↓
[Wait or return later]
  ↓
Exam-result (with rubric scores + recommendations toward B2)
  ↓
Exam-review (explanations unlocked)
```

### Mentor Booking
```
Mentor-list (filtered to TOEIC, optional skill filter)
  ↓
Mentor-detail (view profile, slots, reviews)
  ↓
Mentor-confirm (paywall check, deduct 1 credit)
  ↓
Booking-detail (status: pending → confirmed)
  ↓
[Session happens outside app via Google Meet]
  ↓
Booking-detail (status: done)
  ↓
Booking-review (leave rating + text review)
```

### Admin Complaint Resolution
```
Admin/complaints (list, filter by status)
  ↓
Admin/complaint-detail (read complaint)
  ↓
Resolve form: issue credit make-good
  ↓
Complaint status → resolved
  ↓
Learner sees credit in wallet (ledger reason: admin_grant)
```

## Figma Export Plan (Pass 4)

### Tool
**HTML-to-Figma plugin** (e.g. "html.to.design" or equivalent)

### Process
1. **Export all 47 screens** — Run plugin on each HTML file, outputs Figma frames
2. **Organize structure** — Group frames by flow:
   - Auth (3 frames)
   - Student — Core (17 frames)
   - Student — Placement & Path (8 frames)
   - Student — Mentor (5 frames)
   - Student — Grading (1 frame)
   - Admin (9 frames)
   - Payment (5 frames)
3. **Extract design tokens to Figma variables:**
   - Colors: sign-red, sign-blue, sign-gold, sign-green, ink, wall, painted, etc.
   - Typography: headline, title, body, read, label, data (with font families, sizes, weights)
   - Spacing: space-1 through space-7
   - Borders: keyline (2px), interior (1px)
4. **Convert HTML components to Figma components:**
   - Primary/Plate/Danger buttons
   - Text field
   - Badge (default, success, warning, danger, info)
   - Alert (info, success, warning, danger)
   - Panel (header, body, footer)
   - Nav item
   - State block
   - CEFR band bar
   - Certificate picker
   - Mentor card
   - Path module card
   - Rubric score display
5. **Link states via prototyping:**
   - Replicate `?state=` behavior with Figma interactions
   - Each state variant becomes a separate frame
   - Hover states for buttons (invert to ink block)
6. **Create index/catalog page:**
   - Matches prototype/index.html structure
   - 7 groups, 47 rows
   - Clickable links to each screen's states

### Deliverable
- **One Figma file:** "Multi-Certificate Companion Prototype"
- **7 pages in Figma** (one per flow group)
- **Design system library** (colors, typography, components)
- **88 frames** (47 screens × average 1.9 states visible per screen)
- **Interactive prototype** — clickable state transitions
- **Index page** — navigation catalog

### Cleanup After Auto-Convert
- Fix any broken layouts (HTML → Figma can misinterpret flex/grid)
- Ensure all text uses proper Figma text layers (not rasterized)
- Verify component instances (not detached copies)
- Audit color usage (all use Figma variables, not hardcoded hex)
- Check typography (all use Figma text styles, not inline)

### Effort
**1-2 days:**
- Auto-convert: 2-3 hours
- Organize + cleanup: 4-6 hours
- Extract tokens/components: 3-4 hours
- Link states: 2-3 hours
- Index page: 1-2 hours

## Verification Checklist

### Pass 1 (Remove KYC)
- [ ] 3 KYC screens deleted: `student/kyc-submit.html`, `student/kyc-pending.html`, `admin/kyc-review.html`
- [ ] index.html catalog has no "ID verification" group
- [ ] exam-instructions.html paywall says "Buy credits" not "Verify ID"
- [ ] profile.html has no CCCD/national-ID fields
- [ ] All remaining catalog links work (no 404s)
- [ ] state-switch.js still works on all screens

### Pass 2 (Transform for Multi-Cert)
- [ ] Dashboard shows certificate picker (first-time) or active cert display (returning)
- [ ] Profile has "Active certificate" dropdown
- [ ] Exam-list filters to active cert, cards show cert badge
- [ ] Exam-history shows only active cert attempts
- [ ] Practice-select shows data-driven sections (not Part 1-7)
- [ ] Admin exam-editor has certificate picker
- [ ] Admin score-conversion shows two tables: editable raw→scaled, read-only scaled→band
- [ ] Can switch certificate at dashboard, content updates

### Pass 3 (Add New Features)
- [ ] All 16 new screens exist and render
- [ ] Placement flow connected: select → take → result → set target → path generated
- [ ] Learning path flow connected: list → detail, upgrade
- [ ] Mentor flow connected: list → detail → confirm → booking detail → review
- [ ] Grading-pending screen exists, referenced from exam flow
- [ ] Exam-result shows recommendations section for AI-graded attempts
- [ ] Admin complaints flow connected: list → detail
- [ ] index.html catalog has all new groups/rows (7 groups, 47 screens)
- [ ] All ~88 states reachable via ?state= params
- [ ] New components in components.css: certificate-picker, mentor-card, path-module, rubric-scores

### Pass 4 (Figma Export)
- [ ] All 47 screens exported to Figma as frames
- [ ] Frames organized in 7 pages by flow
- [ ] Design tokens extracted to Figma variables (colors, typography, spacing)
- [ ] HTML components converted to Figma components (16 components)
- [ ] States linked via Figma prototyping (88 state variants)
- [ ] Index/catalog page exists with navigation links
- [ ] No rasterized text (all proper text layers)
- [ ] No detached components (all instances)
- [ ] Colors use variables (no hardcoded hex)
- [ ] Typography uses text styles (no inline)

## Open Questions (Resolve Before Implementation)

1. **Certificate picker at dashboard:** Modal or navigate to profile?
   - **Recommendation:** Modal for quick switching, profile dropdown for account management

2. **Sections per certificate:** Use realistic labels or generic placeholders?
   - **Recommendation:** Use realistic labels based on real exam structure (see Data Requirements)

3. **AI mentor slots:** How to show "instant booking"?
   - **Recommendation:** Badge "Instant booking" on AI mentor cards, human mentors show calendar

4. **Learning path reset:** Need a reset button?
   - **Recommendation:** Add "Reset progress" danger button at learning-path footer (confirmation modal)

5. **Grading-pending UX:** Poll or static message?
   - **Recommendation:** Static "We'll email you when complete. Typical time: 5-10 minutes." (no polling in prototype)

6. **Figma plugin choice:** Which HTML-to-Figma tool?
   - **Recommendation:** "html.to.design" (supports modern CSS, maintains structure)

## Effort Estimate

**Pass 1 (Remove KYC):** 2-3 hours
**Pass 2 (Transform):** 1 day
**Pass 3 (Add 16 screens):** 2-3 days
**Pass 4 (Figma export):** 1-2 days

**Total: 4-6 days**

## Success Criteria

- [ ] 47 HTML screens with ~88 states, all reachable
- [ ] State-switcher discipline maintained (catalog synchronized)
- [ ] "Exam-room signage" design system preserved
- [ ] Full feature parity with 41-table schema
- [ ] Figma file with 88 frames, design system, interactive prototype
- [ ] Zero broken links, zero missing states
- [ ] All new components follow existing patterns (2px keylines, condensed caps, tabular nums for figures)

---

**Ready for implementation planning.**
