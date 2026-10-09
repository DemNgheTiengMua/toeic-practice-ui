# Multi-Certificate Companion — User Flow by Actor

This document answers "what steps does each user type go through", distinct
from the screen/state specs which answer "what states does each screen have".

Updated 2026-10-09 to the redesigned prototype.

Screen paths are relative to `prototype/`; `?state=<name>` selects a state
(catalog: `index.html`).

## 0. Actors

| Actor | Entry | Main work |
|---|---|---|
| **Guest** | `landing.html`, `auth/register.html`, `auth/login.html` | Create account, log in |
| **Learner** | `student/dashboard.html` | Pick certificate, placement, free practice, buy credits, mock exams, speaking practice, mentor bookings, learning path |
| **Admin** | `admin/dashboard.html` | Author exams, manage scales per exam, packages/orders, users, complaint resolution, stats |
| **Mentor** | *(out of prototype scope)* | Sell slots, meet learners on Google Meet, get payouts |

Two things a **Learner** must understand from the start, because they drive
the entire flow:

1. **Practice, weakness analysis, and placement are free, forever.** No
   paywall, no CTA, no ledger write.
2. **Credits buy four things only:** mock exam starts, AI grading (one charge
   per attempt, not per answer), mentor bookings, and premium path upgrades.

## 1. High-Level Map

```
Guest ──register/login──► Learner ──► Dashboard
                                         │
                                         │ pick certificate (persists in parallel)
                                         ▼
                              [FREE] Placement test
                                         ▼
                              result band + target band
                                         ▼
                              generated learning path (basic=free, premium=paid)
                                         │
        ┌────────────────────────────────┼──────────────────────────┐
        │                                │                          │
        ▼                                ▼                          ▼
  [FREE] practice by section    [PAID] mock exams        [PAID] mentor booking
  → practice-take                       │                 [PAID] speaking practice
  → practice-summary                    │  ┌── 0 credits ──► paywall
        │                               │  │      (no deduction)
        ▼                               ├──┘
  weakness-analysis (free)              ▼
        │                       exam-instructions → deduct 1 credit
        └── points to weak spots        ▼
                              exam-listening / exam-reading
                                         ▼
                              exam-confirm-submit
                                         ▼
                              ┌── MCQ-only ──► exam-result (+ band)
                              │
                              └── has speaking/writing ──► grading pending
                                                           ▼
                                  [PAID] AI grades each free response (one charge covers all)
                                                           ▼
                                         exam-result (+ band) + recommendations
                                                           ▼
                              exam-review · exam-history (per certificate)
```

The learning loop is **free at both ends**: practice generates data, weakness
analysis turns that into "you are weak at X" — and that is why a learner sees
the need to spend credits. This is the funnel, not a side feature.

## 2. Learner — Step by Step

### 2.1 Guest Creates Account

| Step | Screen | Notes |
|---|---|---|
| 1 | `auth/register.html` | Full name, email, password + confirm, terms acceptance; email must be verified (schema also stores phone) |
| 2 | `auth/login.html` | — |
| 3 | `auth/forgot-password.html` | Password reset flow (`sent`) |
| 4 | `student/profile.html` | Edit account details |

No identity gate — mock testing needs none.

### 2.2 New Learner: Pick Certificate → Placement

The placement test **selects the certificate first**, then tests, so the exam
matches the skills that certificate actually has. Everything learner-scoped is
keyed by `(user, certificate)` — placement results, target band, learning
path, history — and persists independently. Switching certificates never
destroys another certificate's data (only an explicit reset does).

| Step | Screen | What happens |
|---|---|---|
| 1 | `student/dashboard.html` | Pick a certificate (TOEIC, IELTS, VSTEP, ...) from the certificate picker |
| 2 | `student/placement-select.html` | Pick a placement test (listed apart from mocks) |
| 3 | `student/placement-take.html` | Take the placement test — **FREE**; `resumed` after a reload, `expired`, `offline` |
| 4 | `student/placement-result.html` | Band + per-section results |
| 5 | `student/target-band-set.html` | Pick a target band — free |
| 6 | `student/path-generated.html` | System generates a learning path from `PathTemplates` matching `fromBand → toBand`, "built from your placement on …" |
| 7 | `student/learning-path.html`, `student/path-detail.html` | Modules unlock in order: locked → available → in progress → done; progress from linked practice sessions |
| 8 | `student/path-upgrade.html` | Optional **premium** upgrade (`premium_path`, 5 credits in the prototype); `paywall` says nothing was deducted and the free path is unchanged |

The placement test and the basic generated path are free — they never write a
`CreditTransactions` row. Premium adds video lessons, detailed explanations,
and AI-graded writing; the path structure is identical.

### 2.3 Free Practice by Section

Accessed from the sidebar: **Practice by Section** (the old "Luyện theo part").

| Step | Screen | What happens |
|---|---|---|
| 1 | `student/practice-select.html` | Pick a section (data-driven per certificate), question count, question source (all / previously wrong / unseen), feedback mode (after each question / at the end) |
| 2 | `student/practice-take.html` | Answer; `revealed-correct` / `revealed-wrong` show the explanation plus why the *chosen* option is wrong; save a question to the bank |
| 3 | `student/practice-summary.html` | Per-section accuracy, question-type labels, saved questions |

Unlike mock exams, the correct answer and explanation can show
**immediately**, not after submit. This is the selling point of practice mode.

**Practice is MCQ-only** — immediate grading structurally requires a correct
option, so it can only work for listening/reading. Practising speaking/writing
routes through the paid AI-grading path.

No step here asks for credits, and no ledger row is written. Free by
construction.

### 2.4 Weakness Analysis (Free)

`student/weakness-analysis.html` — aggregates data from **both** free practice
sessions **and** graded mock attempts, so it is useful even for a learner who
has never paid.

This screen answers three questions, in order:

1. **Per-section accuracy, sorted weakest-first** — not by section number,
   because a learner needs to see the weak spots first.
2. **What to practice next** — the section that is both weak and
   high-question-count.
3. **Which question types are often wrong** — not just which section, but
   which *question type* within it.

The question-type breakdown depends on `Questions.CategoryId` tagging — it is
only as good as the question bank's tagging.

### 2.5 Hitting the Paywall: Buy Credits

A learner clicks a mock exam in `student/exam-list.html` but has 0 credits:

```
student/exam-list.html (paywall)  →  payment/pricing.html
                          →  payment/checkout.html
                          →  payment/gateway-mock.html   (gateway simulation)
                          →  payment/payment-result.html (4 branches)
                          →  payment/wallet.html
```

Branches at `payment/payment-result.html`: `success` / `failed` / `pending`
(reconciling) / `expired`. `expired` is separate from `failed` because the
message differs: expired invites a new order, gateway error (with its code)
invites retry.

Order states: `pending` → `reconciling` → `paid` | `failed` | `expired`.
Constraint: **one open order per account** — `reconciling` counts too.
`payment/checkout.html` (`pending` state) blocks at the UI with "do not pay
again", and a filtered unique index blocks at the DB. Without this,
double-click becomes two orders, paid twice.

Credits are the **SUM of ledger rows**, not a stored column.
`payment/wallet.html` shows "purchased − used" from `vw_UserCreditSummary` and
lists orders, including `Reconciling` ones.

### 2.6 Mock Exams

Precondition: **credits > 0**.

| Step | Screen | What happens |
|---|---|---|
| 1 | `student/exam-list.html` | Pick a mock exam; `paywall` at 0 credits |
| 2 | `student/exam-instructions.html` | Structure + durations; Start **deducts 1 credit** (`mock_exam_start`) |
| 3 | `student/exam-listening.html` | Listening sections, audio **cannot scrub**, cannot return to prior sections |
| 4 | `student/exam-reading.html` | Reading sections, free navigation; flag for review |
| 5 | `student/question-navigator.html` | 200-cell grid, colored by per-question state, "only flagged" filter |
| 6 | `student/exam-confirm-submit.html` | Warn about unanswered and flagged questions; Listening stays locked |
| 7a | *(MCQ-only exam)* | `student/exam-result.html` — scores + band, submitted time, time taken |
| 7b | *(has speaking/writing)* | `student/grading-pending.html` — "AI is grading your responses"; `failed` offers a free retry |
| 8 | `student/exam-result.html` (`graded-with-recommendations`) | Scores + band + "to reach B2" recommendations for this attempt |
| 9 | `student/exam-review.html` | Your answer vs correct answer, explanation of why the chosen option is wrong, "only flagged", replay one listening item with its transcript |
| 10 | `student/exam-history.html` | History per certificate, each with its band |

Reload mid-exam returns to the last question (`resumed`).

Listening and Reading **are separate screens** because navigation constraints
are opposite. Jamming them into one view becomes a pile of `if` statements.

For speaking/writing: the attempt cannot reach `graded` on submit — it goes to
`submitted`, waits for the AI to grade each `FreeResponses` row (one
`AiGradings` row per free response, but only **one `ai_grading` charge per
attempt**, not per answer), and only reaches `graded` when all free responses
are done.

### 2.7 Scores and Bands

Scores follow the path: **raw → scaled → band**.

- `raw → scaled` **differs per exam** (difficulty varies), so admin edits it,
  and the table is per-exam (`ScoreScales`).
- `scaled → band` is **fixed standard per certificate**. TOEIC→CEFR, IELTS→0–9,
  VSTEP→bậc. Admin **cannot edit it** — `admin/score-conversion.html` shows it
  read-only, no input fields. Bands are snapshotted (`BandCode`) at grading so
  history survives catalog edits.

Bands are shown to the learner as a **bar** in `student/dashboard.html`,
`student/exam-history.html`, and `student/exam-result.html`: labeled ranges (e.g. six CEFR
bands `<A1`, `A1`, `A2`, `B1`, `B2`, `C1` over the 10–990 scale) with a
marker on the current band and visual distance to the next one — because a
bare number like 785 does not tell a learner where they stand.

Per-skill scores are rows (`AttemptSkillScores`) because skill counts vary by
certificate (TOEIC 2, IELTS 4). `TotalScore` is always the SUM of scaled skill
scores, trigger-enforced.

### 2.8 AI Grading and Recommendations

When an attempt has speaking/writing answers:

1. Submit goes to `submitted`, not `graded`.
2. The server writes one `AiGradings` row per `FreeResponses` row (per answer).
3. The server writes **one `ai_grading` debit** covering the entire attempt
   (not per answer — an 8-question Speaking+Writing attempt is one charge).
4. Each `AiGradings` row fans out to `AiGradingScores` — one row per rubric
   criterion. The rubric (`GradingCriteria`) is data-driven per
   `(certificate, skill)`, so IELTS Speaking gets
   Fluency/Lexical/Grammar/Pronunciation, and TOEIC has none.
5. When a grading reaches `done`, its free response flips to `graded`; only
   when **all** free responses are graded can the attempt reach `graded`.
6. The grading emits `Recommendations` — "what to fix to reach the target
   band" — which can spawn new `ai_task` steps in the learning path.

The learner sees `student/grading-pending.html` while this runs, then gets the
result with band, scores, and concrete recommendations. A `failed` grading is
retried **free** — the charge is already on the attempt, and a second
`ai_grading` row for the same attempt is impossible.

### 2.9 Speaking Practice

An exam of purpose `speaking_practice`; costs **1 credit** (`ai_grading`) for
the AI grading of all recordings.

| Step | Screen | What happens |
|---|---|---|
| 1 | `student/speaking-instructions.html` | Rules + cost; `mic-check` before any credit is spent; `paywall` at 0 credits points back to free practice |
| 2 | *(Start)* | **1 credit charged at start**; not returned if the learner leaves early, unrecorded questions are not graded |
| 3 | `student/exam-speaking.html` | Per-question `prepare` → `recording` → `recorded` timers; each recording saves at once; `save-failed` keeps it on the device and offers retry; `resumed`, `expired` |
| 4 | `student/grading-pending.html` | AI grading; `failed` → retry is free |
| 5 | `student/speaking-review.html` | Per-recording score against the section maximum (`SectionCriteria`), playback; **no band** — speaking alone scores one skill of a summed scale (trigger-enforced) |

### 2.10 Mentor Bookings

| Step | Screen | What happens |
|---|---|---|
| 1 | `student/mentor-list.html` | Browse human and AI mentors per certificate; price in credits |
| 2 | `student/mentor-detail.html` | Headline, expertise, rating (view), reviews; human: pick an open slot; AI: no slot |
| 3 | `student/mentor-confirm.html` | Name a focus area; confirm → deduct `mentor_booking` (mentor's `PriceCredits`); `paywall`; `error` (slot taken, nothing deducted) |
| 4 | `student/booking-detail.html` | `pending` → `confirmed` (Meet link; the app never hosts) → `done`; cancel free up to **24 h** before → `cancelled`, refunded as `booking_cancel` (equal to the charge, once); later cancellations are not credited back |
| 5 | `student/booking-review.html` | After `done`: 1–5 rating + text |

One live booking per slot (filtered unique index); average mentor rating is a
**view**, never stored.

### 2.11 Complaints

A learner complains about exactly one subject they own: a **booking**
(no-show / quality), an **exam attempt** (grading issue / technical error), or
an **order**. The type must match the subject. Admin resolves it (3.3); a
resolution may issue an `admin_grant` credit make-good, never cash. No
learner-side complaint form exists in the prototype; complaints appear on the
admin side only.

## 3. Admin — Step by Step

### 3.1 Exam Import and Authoring

| Step | Screen | What happens |
|---|---|---|
| 1 | `admin/exam-list.html` | Exams per certificate: draft / published / hidden, question count, missing parts |
| 2 | `admin/exam-editor.html` | 3-step wizard (`success` → `step2` → `step3`): certificate, name (unique per certificate), source, internal note; fixed section structure; one audio file per listening section |
| 3 | `admin/question-editor.html` | Questions per group: photo + required alt text, transcript, explanation (schema also holds question-type labels and per-option rationale; no inputs for them yet) |

**Batch import** is a first-class schema feature: `ImportBatches` + per-row
`ImportLog` make ingest queryable — an advisor requirement. No import screen
exists in the prototype.

### 3.2 Score Conversion Tables

`admin/score-conversion.html`. Two tables, two owners:

| Table | Ownership | Editable? |
|---|---|---|
| `raw → scaled` | Admin, per exam | **Yes** — difficulty varies per exam |
| `scaled → band` | Fixed standard per certificate | **No** — read-only; otherwise two learners with the same score land in different bands |

### 3.3 Other Admin Flows

| Flow | Screen | Constraints |
|---|---|---|
| Packages & prices | `admin/packages.html` | `edit` dialog; one featured package; feature bullets; hidden packages |
| Orders | `admin/orders.html` | Order code, gateway, txn ref, error code; "Reconcile again" on `reconciling` |
| Complaints | `admin/complaints.html` | Booking / exam / order complaints, newest first |
| Complaint resolution | `admin/complaint-detail.html` | Internal notes, resolution notes, resolver recorded; may issue `admin_grant` make-good, no cash refunds |
| Users | `admin/users.html` | Lock / unlock, resend activation email, `adjust` credits with a required reason (admin named in the ledger) |
| Stats | `admin/dashboard.html` | Totals and computed "needs attention" items |

## 4. Ordering Constraint — The Part That's Easiest to Get Wrong

The order is **mandatory**:

```
  check preconditions (balance, mic check, slot free)
       │ fails ──► paywall / error, NO deduction
       ▼ ok
  deduct → start the exam / speaking practice / booking / path-upgrade
```

**Deducting after hitting a precondition failure is a bug.** The learner loses
a credit and gets nothing. Credits come back only as `booking_cancel` (booking
cancelled ≥ 24 h ahead) or an admin-driven `admin_grant`; cash refunds are out
of scope.

Every blocking screen must say **"no credit was deducted"** and offer a free
way forward — in this case, the free practice loop.

## 5. Abnormal Branches — Non-Straight Paths

| Situation | Occurs at | Handling |
|---|---|---|
| Reload mid-exam | `student/exam-listening.html`, `student/exam-reading.html`, `student/exam-speaking.html`, `student/placement-take.html` | `resumed` — return to the last question, correct time remaining |
| Time runs out | Same screens + `student/exam-confirm-submit.html` | `expired` — **normal branch**, auto-submit, still grades what was answered |
| Cancel mid-exam | `student/exam-list.html` | `cancelled` — **credit already deducted, not refunded**, dialog says so |
| Network drops during answer save | `student/exam-listening.html`, `student/exam-reading.html`, `student/placement-take.html` | `offline` — queue retry, do not lose the selected answer |
| Recording upload fails | `student/exam-speaking.html` | `save-failed` — recording kept on the device, retry upload |
| Double-click submit | `student/exam-confirm-submit.html` | `submitting` — lock interaction |
| Gateway responds slowly | `payment/payment-result.html` | `pending` — reconciling, auto-refresh; no new order meanwhile (`payment/checkout.html` `pending`) |
| Order past reconciliation window (15 min) | `payment/payment-result.html` | `expired` — invite new order, distinct from `failed` |
| AI grading in progress / fails | `student/grading-pending.html`, `student/speaking-review.html` | Pending screen; `failed` → retry, no new charge |
| Mentor slot taken at confirm | `student/mentor-confirm.html` | `error` — nothing deducted, pick another slot |

Three branches most often forgotten: `resumed`, `expired`, and `cancelled` —
all three **will** happen in a long exam, not rare errors.

## 6. Free / Paid Table — The Final Tally

| Activity | Cost | Why |
|---|---|---|
| Register, log in | free | Gate before seeing the product kills the funnel |
| Pick a certificate, switch certificates | free | Learner-scoped data persists in parallel; switching is non-destructive |
| Placement test | free | No funnel without it; the path it generates is free too |
| Practice by section | free | Generates the data weakness analysis needs; no paywall here |
| Weakness analysis | free | The funnel itself: shows the need to spend credits |
| Basic learning path | free | Generated from the placement; only the *premium* upgrade costs |
| Mock exam start | **1 credit** (`mock_exam_start`) | First of four credit-costing actions |
| AI grading (entire attempt, incl. speaking practice) | **1 credit** (`ai_grading`) | Second; one charge covers all free responses; retry after failure is free |
| Mentor booking | mentor's `PriceCredits` (`mentor_booking`) | Third; human or AI; refunded as `booking_cancel` if cancelled ≥ 24 h ahead |
| Premium path upgrade | **1 charge** (`premium_path`, 5 credits in the prototype) | Fourth; video lessons, detailed explanations, AI-graded writing |

Exactly **four** ledger reasons cost credits. Everything else is free.

## 7. Out of Scope

- Real/high-stakes exam sittings (removed in the pivot)
- KYC / CCCD identity verification (removed in the pivot)
- Writing screens (the schema supports writing; only speaking has screens)
- Re-recording a speaking answer (one response per question, by decision)
- Learner-side complaint form and admin import screen (schema only)
- Flashcards
- Scheduled exam slots with capacity
- Cash refunds (credit make-goods exist; money-back does not)
- OCR / face matching for identity (was deferred, then removed with KYC)
- Calling real APIs, real payments, real AI grading providers
- The concrete import file format (schema tables exist; format is undecided)

## 8. What Changed in the Pivot (2026-09-24)

**Removed:**
- The entire "thi thật" (real exam) concept and its identity gate
- All KYC / CCCD verification (`KycVerifications` table, all KYC screens, the
  admin review queue)
- The TOEIC-only constraint

**Added:**
- Multiple certificates, data-driven (TOEIC, IELTS, VSTEP, ...)
- Skills beyond listening/reading: speaking, writing
- Free placement test → generated learning path
- AI grading of speaking/writing against a per-certificate rubric
- Mentor bookings (human via Meet, or AI) with reviews, complaints, payouts
- Batch import as a first-class, queryable feature
- Certificate isolation enforced by composite foreign keys in the schema
- Four distinct credit-spend reasons (was one: "start a real exam")

**The static prototype** under `prototype/` was re-screened for the pivot and
audited against the schema (`docs/schema-ui-audit.md`). The schema
(`docs/database-schema.sql`) is the data source of truth.
