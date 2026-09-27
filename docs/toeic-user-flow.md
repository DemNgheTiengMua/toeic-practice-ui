# Multi-Certificate Companion — User Flow by Actor

This document answers "what steps does each user type go through", distinct
from the screen/state specs which answer "what states does each screen have".

**IMPORTANT:** The static prototype under `prototype/` was built for the
pre-pivot TOEIC-only, KYC-gated product and has not been re-screened for the
multi-certificate companion. Screen-file citations below point at those
historical screens; they illustrate state coverage patterns, not current scope.

## 0. Actors

| Actor | Entry | Main work |
|---|---|---|
| **Guest** | `auth/register.html`, `auth/login.html` | Create account, log in |
| **Learner** | `student/dashboard.html` | Pick certificate, placement, free practice, buy credits, mock exams, AI grading, mentor bookings, learning path |
| **Admin** | `admin/dashboard.html` | Import/author exams, manage scales/bands per certificate, packages/orders, complaint resolution, stats |
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
                              generated learning path (basic=free)
                                         │
        ┌────────────────────────────────┼──────────────────────────┐
        │                                │                          │
        ▼                                ▼                          ▼
  [FREE] practice by section    [PAID] mock exams        [PAID] mentor booking
  → practice-take                       │                          │
  → practice-summary                    │  ┌── 0 credits ──► paywall
        │                               │  │      (no deduction)
        ▼                               ├──┘
  weakness-analysis (free)              ▼
        │                       deduct 1 credit → exam-instructions
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
| 1 | `auth/register.html` | Email + password. No identity gate at registration. |
| 2 | `auth/login.html` | — |
| 3 | `auth/forgot-password.html` | Password reset flow |

No KYC, no CCCD. Removed in the pivot — mock testing needs no identity gate.

### 2.2 New Learner: Pick Certificate → Placement

The placement test **selects the certificate first**, then tests, so the exam
matches the skills that certificate actually has. Everything learner-scoped is
keyed by `(user, certificate)` — placement results, target band, learning
path, history — and persists independently. Switching certificates never
destroys another certificate's data (only an explicit reset does).

| Step | Screen | What happens |
|---|---|---|
| 1 | `student/dashboard.html` | Pick a certificate (TOEIC, IELTS, VSTEP, ...) |
| 2 | *(new: placement-select)* | Pick a placement exam |
| 3 | *(new: placement-take)* | Take the placement test — **FREE** |
| 4 | *(new: placement-result)* | Get a band + set a target band |
| 5 | *(new: path-generated)* | System generates a learning path from `PathTemplates` matching `fromBand → toBand` |

The placement test and the basic generated path are free — they never write a
`CreditTransactions` row. A learner may later upgrade to a **premium path**
(one `premium_path` debit per path), but the structure is identical; only the
richness and companion closeness differ.

### 2.3 Free Practice by Section

Accessed from the sidebar: **Practice by Section** (the old "Luyện theo part").

| Step | Screen | What happens |
|---|---|---|
| 1 | `practice-select.html` | Pick a section (not "Part 1–7" — sections are data-driven per certificate) and question count |
| 2 | `practice-take.html` | Answer each question, **feedback appears immediately after each one** |
| 3 | `practice-summary.html` | Summary: per-section accuracy |

At step 2, unlike mock exams: the correct answer and explanation show
**immediately**, not after submit. This is the selling point of practice mode.

**Practice is MCQ-only** — immediate grading structurally requires a correct
option, so it can only work for listening/reading. Practising speaking/writing
routes through the paid AI-grading path.

No step here asks for credits, and no ledger row is written. Free by
construction.

### 2.4 Weakness Analysis (Free)

`weakness-analysis.html` — aggregates data from **both** free practice
sessions **and** graded mock attempts, so it is useful even for a learner who
has never paid.

This screen answers three questions, in order:

1. **Per-section accuracy, sorted weakest-first** — not by section number,
   because a learner needs to see the weak spots first.
2. **What to practice next** — the section that is both weak and
   high-question-count.
3. **Which question types are often wrong** — not just which section, but
   which *question type* within it.

The question-type breakdown depends on tagged content at the question level —
it is only as good as the question bank's tagging.

### 2.5 Hitting the Paywall: Buy Credits

A learner clicks a mock exam in `exam-list.html` but has 0 credits:

```
exam-list.html (paywall)  →  payment/pricing.html
                          →  payment/checkout.html
                          →  payment/gateway-mock.html   (gateway simulation)
                          →  payment/payment-result.html (4 branches)
                          →  payment/wallet.html
```

Four branches at `payment-result.html`: `success` / `failed` / `pending` /
`expired`. `expired` is separate from `failed` because the message differs:
expired invites a new order, gateway error invites retry.

Constraint: **one `pending` order per account**. `checkout.html` blocks at the
UI, and a filtered unique index blocks at the DB. Without this, double-click
becomes two orders, paid twice.

Credits are the **SUM of ledger rows**, not a stored column — so the wallet
always reconciles against transaction history.

### 2.6 Mock Exams (Replaces "Thi Thật")

Precondition: **credits > 0**. No identity gate — KYC was removed with the
pivot.

| Step | Screen | What happens |
|---|---|---|
| 1 | `exam-list.html` | Pick a mock exam |
| 2 | `exam-instructions.html` | Confirm **deduct 1 credit** (`mock_exam_start`) |
| 3 | `exam-listening.html` | Listening sections, audio **cannot scrub**, cannot return to prior sections |
| 4 | `exam-reading.html` | Reading sections, free navigation |
| 5 | `question-navigator.html` | 200-cell grid, colored by per-question state |
| 6 | `exam-confirm-submit.html` | Warn about unanswered questions |
| 7a | *(MCQ-only exam)* | `exam-result.html` — scores + band immediate |
| 7b | *(has speaking/writing)* | *(new: grading-pending screen)* — "AI is grading your responses, check back soon" |
| 8 | *(after AI grading completes)* | `exam-result.html` — scores + band + recommendations toward target band |
| 9 | `exam-review.html` | Correct answers + explanations (unlocked after grading) |
| 10 | `exam-history.html` | History per certificate, each with its band |

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

Bands are shown to the learner as a **bar** in `dashboard.html`,
`exam-history.html`, and `exam-result.html`: labeled ranges (e.g. six CEFR
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

The learner sees a **pending-grading screen** while this runs, then gets the
result with band, scores, and concrete recommendations.

### 2.9 Mentor Bookings

*(new flow, not in the prototype)*

| Step | What happens |
|---|---|
| 1 | Browse mentors filtered by `(certificate, skill)` |
| 2 | Human mentor: pick an open slot; AI mentor: no slot needed |
| 3 | Confirm → deduct `mentor_booking` credit |
| 4 | Booking created, status `pending` → `confirmed` → `done` |
| 5 | System stores a Google Meet link; the app never hosts the session |
| 6 | After the session: learner can leave a review (1–5 rating + text) |
| 7 | If mentor no-show / issue: file a complaint → admin reviews → may issue `admin_grant` credit make-good |

One live booking per slot (filtered unique index); average mentor rating is a
**view**, never stored.

## 3. Admin — Step by Step

### 3.1 Exam Import and Authoring

`admin/exam-editor.html` (wizard), `admin/question-editor.html`. Upload audio
and images; questions grouped by section. **Batch import** is now a
first-class feature: `ImportBatches` + per-row `ImportLog` make ingest
queryable, not hidden in backend logic — an advisor requirement.

### 3.2 Score Conversion Tables

`admin/score-conversion.html`. Two tables, two owners:

| Table | Ownership | Editable? |
|---|---|---|
| `raw → scaled` | Admin, per exam | **Yes** — difficulty varies per exam |
| `scaled → band` | Fixed standard per certificate | **No** — read-only; otherwise two learners with the same score land in different bands |

### 3.3 Other Admin Flows

| Flow | Screen | Constraints |
|---|---|---|
| Packages & prices | `packages.html` | Edit package dialog |
| Orders | `orders.html` | Reconcile with gateway |
| Complaint resolution | *(new)* | May issue `admin_grant` credit make-good linked to the complaint |
| Users | `users.html` | Per-certificate history and path status |
| Stats | `admin/dashboard.html` | — |

The old **KYC review queue** (`admin/kyc-review.html`) is gone — removed with
the KYC pivot.

## 4. Ordering Constraint — The Part That's Easiest to Get Wrong

The old flow had: check KYC → check balance → deduct. KYC is gone; the
remaining order is **mandatory**:

```
  check balance ──0 credits──► paywall, NO deduction
       │ has credits
       ▼
  deduct 1 credit → start the exam/grading/booking/path-upgrade
```

**Deducting after hitting a precondition failure is a bug.** The learner loses
a credit and gets nothing, and the prototype has no refund flow (cash refunds
are out of scope; complaint resolution can issue a *credit* make-good, but
that is admin-driven).

Every blocking screen must say **"no credit was deducted"** and offer a free
way forward — in this case, the free practice loop.

## 5. Abnormal Branches — Non-Straight Paths

| Situation | Occurs at | Handling |
|---|---|---|
| Reload mid-exam | `exam-reading.html` | `resumed` — return to the right question, correct time remaining |
| Time runs out | `exam-reading.html`, `exam-confirm-submit.html` | `expired` — **normal branch**, auto-submit, still grades what was answered |
| Cancel mid-exam | `exam-confirm-submit.html` | `cancelled` — **credit already deducted**, dialog must say so |
| Network drops during answer save | Exam screen | `offline` — queue retry, do not lose the selected answer |
| Double-click submit | `exam-confirm-submit.html` | `submitting` — lock interaction |
| Gateway responds slowly | `payment-result.html` | `pending` — reconciling, auto-refresh |
| Order past reconciliation window (15 min) | `payment-result.html` | `expired` — invite new order, distinct from `failed` |
| AI grading in progress | Between submit and result | Pending-grading screen — "check back soon" |

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
| AI grading (entire attempt) | **1 charge** (`ai_grading`) | Second; one charge covers all free responses in the attempt |
| Mentor booking | **1 charge** (`mentor_booking`) | Third; human or AI |
| Premium path upgrade | **1 charge** (`premium_path`) | Fourth; richer modules, closer companion guidance |

Exactly **four** ledger reasons cost credits. Everything else is free.

## 7. Out of Scope

- Real/high-stakes exam sittings (removed in the pivot)
- KYC / CCCD identity verification (removed in the pivot)
- TOEIC Speaking & Writing (the *old* TOEIC-only prototype had none; the *new*
  schema supports them as data-driven skills — but no screens exist yet)
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

**The static prototype** under `prototype/` predates the pivot and has not
been re-screened. It demonstrates state-coverage discipline, not current scope.
The schema (`docs/database-schema.sql`) and this flow doc are the source of
truth.
