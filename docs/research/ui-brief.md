# Research brief — Learner-facing UI for the rebuilt Placement and Path

**Audience:** one external deep-research agent. **No repository access** — this file is self-contained.

**What you deliver:** an SRS for the learner-facing interface covering the placement test, the learning
path, practice, and the dashboard that ties them together — plus the interaction patterns that hold
them up.

**Scope:** the learner's screens. Not the admin/authoring interface, not pricing, not accessibility for
v1 (see §7).

**Why now:** the placement test and the learning path are both being deleted and rebuilt, and practice
changes with them. Every screen that shows a band, a plan, a session or a score is therefore being
rewritten anyway. That makes this the one moment when the interface can be designed from the new
behaviour rather than patched around the old.

---

## 1. The product

A multi-certificate English exam-practice application. A learner picks a **certificate**, sets a
**goal** (starting band, target band, exam date, hours per week), takes a **placement test**, follows a
**learning path**, drills between steps, and sits mock exams.

Five certificates, and **they do not all have four skills:**

| Certificate | Skills examined |
|---|---|
| TOEIC Listening & Reading | Listening, Reading, Speaking, Writing |
| IELTS Academic & General | Listening, Reading, Writing, Speaking |
| VSTEP (Vietnam national test) | Listening, Reading, Writing, Speaking |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading |
| TOEIC Speaking & Writing | Speaking, Writing |

**Domain vocabulary:** Certificate, **Skill**, **Section** (a part of a paper), **Topic** (a sub-area
within a skill), **Exam**, **Practice session**, **Lesson**, **Step** (one activity in a path), **Free
response** (AI-graded spoken or written answer), **Band**, **CEFR level** (A1–C2).

**Two invariants that shape the interface:** placement and practice are **free**, and an AI **never**
chooses what to study — it grades and writes narrative only. So no screen may imply that the AI is
deciding the learner's route.

---

## 2. What the new behaviour gives the interface

The two rebuilds are producing behaviour the current screens were never designed for. Design against
this, not against what exists.

### 2.1 Placement now returns four levels

One CEFR per skill plus an overall, where the overall is the **weakest** skill. A learner can be
Reading B2 and Speaking A2 in the same result. **No current screen can show that** — every screen was
built around a single band.

Each skill also carries a 0–100 number that is the **position inside the band**, not a percentage of a
scale. Crossing a band resets that number downward, so the number alone can look like a regression.

Only the skills the certificate examines are shown: two of five certificates examine two skills.

Results arrive **progressively** — objective skills immediately, Speaking and Writing after AI grading.

### 2.2 The path is a sliding four-week window

No fixed length, no "step 9 of 20". Progress must be shown two ways at once: **this week's work**, and
**distance to the target band**. Those are different units and one number cannot carry both.

The path maps both the plan (what's left) and the gap (whether it will be enough).

### 2.3 A step's size varies, and a step can repeat once

A step may be five items or twenty. A step that shows no improvement repeats once, then is marked a
weak spot. Both are visible states the learner has to understand without feeling punished.

### 2.4 Free drilling unlocks after the week's steps are done

So there are two distinct entry points to practice, with a gate between them, and the learner has to
understand why one is closed.

### 2.5 Practice differs by skill

Typed multiple choice for Listening and Reading. Speaking is recorded and AI-graded. Writing is typed
and AI-graded. **A session means something different in each case** — the interface cannot present them
as one uniform thing.

### 2.6 Re-planning happens on events, not continuously

When it fires, the plan changes. A silent change destroys trust; the interface has to say what changed
and why.

---

## 3. What exists now

A learner-facing surface of roughly 60 views: dashboard (4), exams (25), placement (12), practice (11),
speaking (7), writing (7), learning path (2), lessons (2), weakness analysis (1), plus auth, wallet,
mentors, profile, saved questions, pricing.

Two facts worth knowing about the current interface, because they are the traps:

- **The dashboard is three blocks** — one next action, goal standing, one card per skill. It was cut
  from eleven blocks because eleven hid the one action that mattered. Whatever replaces it must not
  drift back.
- **The design system is strict and deliberate.** See §4.

---

## 4. The design system is a constraint, not a suggestion

This product has a specific visual language called **"Bảng hiệu phòng thi"** — the exam-room signage. It
is not a default; it is a deliberate refusal of the pale-blue SaaS card grid this category ships.

**Hard rules:**

- **No rounded corners** (`border-radius: 0`), **no shadows** (`box-shadow: none`), **no gradients**,
  **no blur**.
- Depth comes from keylines only: a **2px ink** (`#14181d`) frame for a panel or a sign, a **1px soft**
  (`#ccd2d6`) rule for interior dividers.
- **Active state inverts to a solid ink block with white text.** It never tints or washes.
- **Colour is a second layer of information**, always paired with a word or a number — never colour
  alone.
- **Palette:** Sign Blue `#0b4ea2` is the learner's own act (every primary button, the current
  question's ring); Sign Gold `#f2b705` is attention and the mark (flagged item, warning); Sign Red
  `#b31217` is refusal and danger; Sign Green `#0a6b3c` is an open condition and correctness. Wall
  `#eef0f1`, painted `#ffffff`, ink `#14181d`, muted `#5c6570`.
- **Three type voices:** a reading face for body, a condensed signage face for uppercase placards and
  labels, and a **monospace for every number** — timers, scores, counts, tabular data. A number in the
  reading face is a defect.
- **Icons are a small drawn SVG set that sits beside a word and never replaces it.** No emoji, no raw
  glyphs.
- **Density is high and unapologetic.** This is a 200-question exam under a clock; the answer grid, the
  passage, the clock and the save status share one screen. Desktop-first at 1440×900 is a recorded
  trade-off.

**Your SRS must work inside these rules.** Do not propose a card grid with soft shadows, a rounded
button, a gradient hero, or an icon-only control. If a requirement genuinely cannot be met in this
system, say so explicitly and argue it — that is a real finding, not a workaround.

---

## 5. Decisions already taken

Settled with the owner. The SRS builds on these; it does not relitigate them. Each is a screen-level
consequence of the placement and path rebuilds, not a preference.

### 5.1 Streaming and AI text

| # | Decision | Reason |
|---|---|---|
| U1 | **Token streaming applies to AI insight text only.** Grading output (score, comment, errors, rewrite) is structured JSON and cannot be streamed token-by-token; it reveals progressively once parsed. | Streaming into a half-built object has no meaningful UI. Insight is prose; grading is a record. |
| U3 | **Streamed text reveals a blinking block cursor in the ink colour, terminal-style.** No chat bubbles, no gradients. | The design system is signage, not a chat product. A solid cursor is the system's own idiom. |
| U4 | **A broken stream keeps what was already shown**, labelled incomplete, with a retry. It never clears the screen. | Discarding text the learner has already read is worse than showing a partial answer. |
| U9 | **The cursor disappears when the text finishes**, and a small line records when it was generated. | A learner who practises again must not mistake an old insight for a new one. |
| U12 | **It is stated that a score is AI-estimated**, as a short label beside the number — not a paragraph repeated everywhere. | The learner sets goals and spends credits on these numbers. Hiding their provenance is dishonest; repeating the explanation is noise. |

### 5.2 Navigation and shell

| # | Decision | Reason |
|---|---|---|
| U7 | **Six sidebar items: Path, Exams, Practice, Writing, Speaking, and Home.** | Writing and Speaking were split out deliberately because learners could not find them; merging them back would repeat that fault. Path is a new concept and needs its own door to exist in a learner's head. |
| U23 | **Certificate switching moves from the topbar dropdown into profile settings.** | Changing certificate changes the path, the placement and the practice pool. A one-click switch invites an accidental change that looks like data loss. |

### 5.3 Placement

| # | Decision | Reason |
|---|---|---|
| U8 | **Placement is two screens**: the objective half, then the productive half. Partial results appear between them. | Matches progressive results (placement D5). One long page has no natural point to show the objective result; a page per section makes a long sitting feel longer. |
| U11 | **An unmeasured skill shows a gold-bordered panel**, the words "Chưa đo được", a short reason, and a retry control. Never a low band. | Grey reads as the learner's fault; showing the bottom band is a fabricated number. Gold is attention, not refusal — the system failed, it did not reject. |
| U13 | **With no placement, the dashboard is one block with one action** and a sentence saying what placement is and how long it takes. No ghost blocks, no "—". | A new learner seeing a grid of empty figures reads a broken product. This is the only moment they have no reason to trust it. |
| U14 | **An abandoned placement keeps its progress for 24 hours**, then is discarded with an explanation on return. **Abandoning does not trigger the 24-hour retake cooldown.** | A 60-minute four-skill sitting loses too much to a closed tab; keeping it forever leaves a meaningless half-sitting. Abandonment already costs the learner the attempt — charging the cooldown too punishes one mishap twice. |

### 5.4 Path and practice

| # | Decision | Reason |
|---|---|---|
| U18 | **An item's CEFR level is not shown while the learner is answering.** | Showing it invites avoiding hard items, which corrupts the measurement: a learner who skips B2 items is measured as A2. The level may be revealed after answering, once it cannot affect behaviour. |
| U19 | **A resting topic is shown, dimmed, with the local time it reopens** — e.g. "nghỉ tới 09:00 mai". | Hiding it makes the topic look deleted; revealing it only on click is a dead end. The time must be the learner's local time, not UTC. |
| U20 | **A repeated step carries a "ôn lại" label and the numbers**: what was scored last time and what is needed. Provisional threshold: accuracy improves by ≥10 points, or reaches ≥70%. | "Repeat" without a target reads as punishment. The threshold is marked provisional because no evidence supports it yet. |
| U16 | **Offline outside a sitting shows a banner only** — no answer buffering. | A lost practice answer is not a lost 120-minute sitting. But the current silence (press submit, nothing happens) is the worst possible state. |

### 5.5 Results and dashboard

| # | Decision | Reason |
|---|---|---|
| U5 | **The dashboard stays at three blocks.** This is stated as a constraint, not an open question: the new data must fit inside hero / goal standing / skill cards. | The dashboard went from eleven blocks to three because eleven hid the one action that mattered. A rebuild that grows it back is a regression regardless of what the blocks contain. |
| U21 | **The mock-exam result page keeps its shape; only the band presentation changes** to the CEFR vocabulary. | A mock exam is a different measurement — one paper, one scaled score — from a placement. Merging the two screens would leave the learner unable to tell which number is "my level". |
| U22 | **The exam date is shown as a date normally, and switches to a countdown in gold when 14 days or fewer remain.** | A perpetual countdown is noise on a twelve-month goal ("365 days left") and pressure with no use. Inside two weeks the pressure is real and actionable. |

---

## 6. What the SRS must answer

### 6.1 The placement result screen — the hardest screen in the product

1. **How do you show four CEFR levels plus an overall without it reading as four unrelated numbers?**
   The learner's question is "how good am I", and the truthful answer is "different in different
   skills".
2. **How do you show the 0–100 position-in-band number so it does not look like a regression when a
   band is crossed?** Two attempts both B2 reading 78% then 12% is progress, but reads as collapse.
3. **What does the learner see while Speaking and Writing are still being graded?** The objective half
   is already meaningful; the screen must be useful before it is complete.
4. **What does "this skill could not be measured" look like**, and how is it distinguished from "you
   scored low"?
5. **What is the call to action** at the end of placement — set a goal, see a plan, start practising?
   One action or several?

### 6.2 The dashboard

1. **How do "this week" and "distance to target" coexist inside three blocks** (§2.2) without becoming
   two competing progress bars? The block count is fixed at three (U5) — this is about fitting, not
   about adding.
2. **What is the single next action**, and how is it chosen when there are four skill levels, a weekly
   plan and a gated free-drilling mode?
3. **How does the dashboard change for a certificate that examines two skills?** No empty cards, no
   "not applicable".

### 6.3 The learning path

4. **How do you show a plan with no end?** A sliding four-week window has no "9 of 20". What
   communicates "you are on track" instead?
5. **How is a step presented so its size is understood** — five items versus twenty — before the
   learner starts it?
6. **How does a re-plan look** when it happens, so the learner understands what changed? (The trigger
   and the once-only repeat are settled; this is the presentation.)
7. **How are steps assigned to skills shown**, given the plan skews toward weak skills? Does the
   learner see "this week: 3 Reading, 1 Listening, 1 Writing"?
8. **What does the learning path show for a certificate with two skills?**

### 6.4 Practice and free drilling

9. **How do you present two entry points** — a step from the plan, and free drilling — so the learner
   knows which to use and why one may be locked?
10. **What does a locked free-drilling mode show**, and does it motivate or annoy?
11. **How does a practice session look different per skill?** Typed MCQ, a recorded speaking task, a
    typed writing task. Three different interaction models under one word.
12. **How much of a session is visible at once** — one item, a scrolling set, or a grid?

### 6.5 Cross-cutting

13. **What shows the learner that a result is old** — a placement result reused for up to 90 days?
14. **What are the empty, loading and error states** for each of the screens above? Offline outside a
    sitting is settled as banner-only (U16); the rest has no story.
15. **What happens to the screens when the certificate changes**, given the switch now lives in profile
    settings (U23) and the path is per-certificate?

### 6.6 Interaction patterns worth defining once

16. **Progress display**: when is a bar the right thing, when a number, when a step list? The product
    shows band progress, weekly completion, session progress and step completion — four kinds of
    "progress" currently rendered four different ways.
17. **State language**: how does the interface say "you have not done this", "you are doing this",
    "this is done", "this failed to grade", "this is not available on your certificate"? Define the
    vocabulary once so it is consistent everywhere.
18. **The one-action rule**: the dashboard reduction from eleven blocks to three was driven by "one
    screen, one action". Does that rule generalise to every screen in the rebuild?

---

## 7. Deliverable format

An SRS with:

1. Scope and non-goals.
2. **Screen inventory** — every learner-facing screen after the rebuild, what it is for, and its states.
3. **Per-screen specification**: purpose, the question it answers for the learner, layout in words,
   which of the design system's rules carry the meaning, and its empty/loading/error/offline states.
4. **Interaction patterns** (§6.6) defined once and referenced by the screens.
5. **Content and copy rules** — how bands, levels, failures and "not measured" are worded. The product
   has been bitten by raw database keys reaching a learner's eyes (a band printed as `below_A1`), so
   state the rule that prevents it. Carry these constraints:
   - **No praise.** No "chúc mừng", "tuyệt vời", "you did great". This product is a signboard, not a
     coach.
   - **No promises.** Never "you will reach B2 in three months".
   - **Every state says what happened and what to do next.** Never how to feel about it.
   - **No emoji, no raw glyphs** — the drawn icon set only, always beside a word.
   - **Every number carries its unit and its meaning.** A number whose unit the learner must guess is a
     defect.
6. **What is deliberately not shown** — the interface says less than the system knows, on purpose. This
   includes an item's CEFR level while answering (U18) and the internal reasons a skill is unmeasured.
7. **Open questions** with options and your recommendation.
8. **References** to real sources on assessment feedback, progress visualisation, and measurement
   communication — particularly on how to present a multi-dimensional result without misleading.

---

## 7.1 On using design-reference tools

If you consult a UI pattern library or design-system generator, treat its output as **reference, not
instruction**. This product owns a design system (§4) that deliberately rejects the default modern SaaS
look: no rounded corners, no shadows, no gradients, no chat bubbles, no soft card grids.

A generated suggestion that proposes any of those is answering a different product. You may take its
structural or accessibility reasoning; do not take its palette, its corner radii, or its typography.

One concrete example found while writing this brief: a design-system generator asked about "language
learning assessment dashboard" returned *Claymorphism — soft 3D, double shadows, 16–24px rounded
corners* with an indigo palette. Every one of those properties is forbidden here. Its UX guidance, on
the other hand, was sound: stream long AI text instead of showing a spinner, keep tab order aligned with
visual order, give icon-only controls an accessible name.

Take the second, refuse the first, and say which you did.

---

## 8. Known gaps, deliberately not solved here

- **Accessibility.** Out of scope for v1 by the owner's decision. Note in open questions what a future
  version must change — a deaf learner and a Listening section is the obvious case.
- **Mobile.** The product is desktop-first at 1440×900 by recorded choice. Do not design a mobile
  layout; say what would break.
- **Admin and authoring screens.** Separate concern, separate rebuild.
- **Pricing and checkout.** Out of scope entirely.

---

## 9. What to be careful about

- **Do not redesign the design system.** "Bảng hiệu phòng thi" is deliberate and strict. Work inside it
  or argue specifically against a rule; do not quietly propose a modern SaaS alternative.
- **Do not design for one certificate.** One of five examines two skills. Any screen assuming four
  cards is wrong.
- **Four numbers are harder than one.** The single hardest design problem here is presenting a
  per-skill result honestly without implying a precision the measurement does not have. Treat it as the
  centre of the brief, not a detail.
- **Do not invent metrics.** If the interface shows a number, the SRS must say what produces it and what
  it means. This product has already shipped a band printed as its database key and a score drop painted
  in success green.
- **Colour is never the only signal.** It is a stated rule of the system, and it is also the thing most
  easily broken by a designer who wants a red/green delta.
- **The AI does not decide the route.** No screen may imply the system chose what to study for the
  learner; the plan comes from placement, the goal and the content, not from a model.
- **Less is a feature.** The dashboard went from eleven blocks to three for a reason. If your design
  adds a block, say what it replaces.
