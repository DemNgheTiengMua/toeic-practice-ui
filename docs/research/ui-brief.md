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

## 5. What the SRS must answer

### 5.1 The placement result screen — the hardest screen in the product

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

### 5.2 The dashboard

6. **What are the blocks?** The current three (next action, goal standing, skill cards) were a
   deliberate reduction from eleven. Does the new behaviour fit three, or does it need a fourth — and if
   so, what is removed to pay for it?
7. **How does the dashboard express "this week" and "distance to target" side by side** (see §2.2)
   without becoming two competing progress bars?
8. **What is the single next action**, and how is it chosen when there are four skill levels, a weekly
   plan and a gated free-drilling mode?
9. **How does the dashboard change for a certificate that examines two skills?** No empty cards, no
   "not applicable".

### 5.3 The learning path

10. **How do you show a plan with no end?** A sliding four-week window has no "9 of 20". What
    communicates "you are on track" instead?
11. **How is a step presented so its size is understood** — five items versus twenty — before the
    learner starts it?
12. **How does a repeated step feel?** A step that showed no improvement repeats once. It must not read
    as failure.
13. **What does a re-plan look like** when it happens, so the learner understands what changed?
14. **How are steps assigned to skills shown**, given the plan skews toward weak skills? Does the
    learner see "this week: 3 Reading, 1 Listening, 1 Writing"?
15. **What does the learning path show for a certificate with two skills?**

### 5.4 Practice and free drilling

16. **How do you present two entry points** — a step from the plan, and free drilling — so the learner
    knows which to use and why one may be locked?
17. **What does a locked free-drilling mode show**, and does it motivate or annoy?
18. **How does a practice session look different per skill?** Typed MCQ, a recorded speaking task, a
    typed writing task. Three different interaction models under one word.
19. **What does the learner see during and after AI grading** for a productive-skill session, including
    when grading fails?
20. **How much of a session is visible at once** — one item, a scrolling set, or a grid?

### 5.5 Cross-cutting

21. **How does the learner switch certificate**, and what happens to the screens when they do (plan,
    results, practice all change)?
22. **What shows the learner that a result is old** — a placement result reused for up to 90 days?
23. **What are the empty, loading, error and offline states** for each of the screens above? The product
    already handles offline in a sitting; the rest of the interface has no such story.
24. **What is the navigation model?** The current sidebar has five items. With placement, path,
    practice, exams, writing and speaking, five may not be enough — or the answer is that some are
    reached from the path rather than from the nav.

### 5.6 Interaction patterns worth defining once

25. **Progress display**: when is a bar the right thing, when a number, when a step list? The product
    shows band progress, weekly completion, session progress and step completion — four kinds of
    "progress" currently rendered four different ways.
26. **State language**: how does the interface say "you have not done this", "you are doing this",
    "this is done", "this failed to grade", "this is not available on your certificate"? Define the
    vocabulary once so it is consistent everywhere.
27. **The one-action rule**: the dashboard reduction from eleven blocks to three was driven by "one
    screen, one action". Does that rule generalise to every screen in the rebuild?

---

## 6. Deliverable format

An SRS with:

1. Scope and non-goals.
2. **Screen inventory** — every learner-facing screen after the rebuild, what it is for, and its states.
3. **Per-screen specification**: purpose, the question it answers for the learner, layout in words,
   which of the design system's rules carry the meaning, and its empty/loading/error/offline states.
4. **Interaction patterns** (§5.6) defined once and referenced by the screens.
5. **Content and copy rules** — how bands, levels, failures and "not measured" are worded. The product
   has been bitten by raw database keys reaching a learner's eyes (a band printed as `below_A1`), so
   state the rule that prevents it.
6. **What is deliberately not shown** — the interface says less than the system knows, on purpose.
7. **Open questions** with options and your recommendation.
8. **References** to real sources on assessment feedback, progress visualisation, and measurement
   communication — particularly on how to present a multi-dimensional result without misleading.

---

## 7. Known gaps, deliberately not solved here

- **Accessibility.** Out of scope for v1 by the owner's decision. Note in open questions what a future
  version must change — a deaf learner and a Listening section is the obvious case.
- **Mobile.** The product is desktop-first at 1440×900 by recorded choice. Do not design a mobile
  layout; say what would break.
- **Admin and authoring screens.** Separate concern, separate rebuild.
- **Pricing and checkout.** Out of scope entirely.

---

## 8. What to be careful about

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
