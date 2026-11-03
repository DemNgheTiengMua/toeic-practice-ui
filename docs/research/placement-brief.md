# Research brief — Rebuild the Placement Test

**Audience:** one external deep-research agent. **No repository access** — this file is self-contained.

**What you deliver:** a complete SRS that becomes specs → tickets → build.

**Scope:** the placement test as a whole — how the item pool is produced and stored, how a sitting runs,
how it is scored, and what result it returns. Pricing is out of scope.

**This is a delete-and-rebuild.** The existing placement is deleted, not extended. Its code and schema
are evidence of what went wrong, not a constraint on your answer.

---

## 1. The product

A multi-certificate English exam-practice application. A learner picks a **certificate**, sets a
**goal** (starting band, target band, exam date, hours per week), takes a **placement test** to
establish where they are, then follows a generated **learning path**.

Five certificates. **They do not all have four skills:**

| Certificate | Skills examined |
|---|---|
| TOEIC Listening & Reading | Listening, Reading, Speaking, Writing |
| IELTS Academic & General | Listening, Reading, Writing, Speaking |
| VSTEP (Vietnam national test) | Listening, Reading, Writing, Speaking |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading |
| TOEIC Speaking & Writing | Speaking, Writing |

**Domain vocabulary to adopt:** Certificate, **Skill** (one modality), **Section** (a part of a paper),
**Exam**, **Attempt** (a sitting), **Free response** (a spoken or written answer scored by AI), **Raw
score → Scaled score → Band**, **CEFR level** (A1–C2).

**Invariants that must survive:**

- Placement is **always free**. No credit ledger row, ever.
- One certificate's content, scoring or bands never leak into another.
- An AI never chooses an item, a score, or a level. It grades free responses and writes narrative.
- A band is looked up from a table; it is never hardcoded and never invented.

---

## 2. What exists now, and why it is being replaced

Four placement exams: a 24-question TOIC diagnostic, two 10-question exams (IELTS, VSTEP), and a
32-question "Diagnostic Benchmark 45M". Five defects make their result untrustworthy.

### 2.1 It cannot discriminate between adjacent bands

The recommended TOEIC placement (24 questions), by difficulty level:

| Level | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|
| Items | 4 | 11 | 9 | 0 | 0 |

A test with no difficult items cannot separate a B2 response from a C1 one. It reports a band anyway,
and that band becomes the learner's starting point for the path, the goal, and the feasibility verdict.

### 2.2 It covers two skills out of four

Every placement exam is Listening and Reading only. Speaking and Writing — two of four skills the
product claims to teach — have **no starting point at all**.

### 2.3 Nothing is randomized

Selection is fixed; two attempts are byte-identical. A learner who retakes sees the same paper.

### 2.4 Difficulty has no meaning

Items carry `DifficultyLevel` 1–5 with **no mapping to CEFR or to any certificate scale**, and there are
**zero items at level 5** across the whole bank of 116. "Hard" is a number nobody defined.

### 2.5 A confirmed defect: a placement with free responses cannot be completed

On submit, a free response moves to `pending_ai`, the attempt stays `submitted` with no band, and the
placement snapshot is then written as `done` with a null band — which a database constraint correctly
refuses. **Two writers disagree about one row.** Confirmed by execution, not by reading.

This matters here because the rebuild makes free responses the *normal* case: four skills means
Speaking and Writing in every placement.

---

## 3. Decisions already taken

Settled. Build on them; do not relitigate.

| # | Decision | Reason |
|---|---|---|
| D1 | **One CEFR per skill plus one overall.** The overall is the **weakest** skill, not an average. | One band hides "reads B2, speaks A2". An average claims everything is fine. |
| D2 | **CEFR is attached to each item** (A1–C2). `DifficultyLevel` 1–5 is replaced, not mapped. | A 1–5 ↔ CEFR table is another invented mapping; that class of table already caused a defect here. |
| D3 | **One shared pool for all certificates.** The certificate decides only which skills are measured and which target is displayed. | One placement design for every certificate; avoids multiplying content cost by five. |
| D4 | **Measure only the skills the chosen certificate has.** TOEIC_CAT is measured on two. Results for skills the certificate lacks are stored but not shown. | Measuring Speaking for a certificate that never examines it produces an unused number. Storing it means a later switch to IELTS already has the data. |
| D5 | **Results appear progressively.** Objective skills are scored and shown immediately; Speaking and Writing show "grading" and fill in when done. | A learner watching a spinner is worse than one watching a result assemble. If AI fails, the objective half still has value. |
| D6 | **Two tasks per productive skill**, chosen to fit the level estimated from the objective half. | One task is too thin to place a person; four makes the sitting unbearable. Choosing by level avoids asking an A2 learner for an academic argument. |
| D7 | **A failed task does not void the skill.** One task graded → result shown, marked as estimated from one task. Both failed → the skill is "not measured" and the overall skips it. | Treating an AI failure as A1 is a fabricated result. |
| D8 | **Retake allowed with a 24-hour cooldown**, unlimited total. | Random pools plus instant retakes invites "keep going until a high band appears". A cooldown still allows recovery from a genuine mishap. |
| D9 | **Two-stage adaptive (multistage)**, not per-item adaptive, not linear. Stage 1: fixed blueprint across all bands. Stage 2: items only around the estimated level. | Linear was rejected by the owner: it forces a long paper and leaves each band thin. Per-item CAT needs calibrated item parameters this product does not have. |
| D10 | **Stage 2 delivers at least 5 items per band** for objective skills, so one lucky guess cannot decide a band. | 4-option items have a 25% guessing floor; at 4 items one guess moves the result 25%. |
| D11 | **Full CEFR range A1–C2**, with density concentrated at A2–C1. | Cutting C2 breaks the path to a C1 target; cutting A1 abandons the beginner, who needs placement most. |
| D12 | **A skill shows CEFR plus a 0–100 number, and that number is the position inside the band** — not a percentage of the certificate scale, not raw percentage correct. Band and number are always shown together. | It answers "am I improving": two attempts both B2 reading 78% then 12% is progress. Crossing a band resets the number downward, so the number alone would look like a regression. |
| D13 | **The same 0–100 rule for all four skills.** Objective: raw → scaled through the certificate's scale → position in band. Productive: rubric → band → position in band. | Comparing four skills needs one unit. Two units means the result table cannot answer "which is weakest". |
| D14 | **Objective band rule: ≥70% correct at band L and at every lower band → CEFR = L**, with the threshold published to the learner. | 70% sits above the 25% guessing floor. Publishing it is more honest than "the system says B1". |
| D15 | **Non-monotonic results are flagged, not silently accepted.** Correct at B2 but wrong at A2 means guessing or a bad item: take the highest band of the first monotonic run, and record it for an administrator. | A high rate of these means the pool is broken, and nobody would find out otherwise. |
| D16 | **Scoring happens on the CEFR axis, never on the certificate's own scale.** The certificate is not involved in grading. | Certificate scales produce three different numbers for the same answers, and two certificates (TOEIC_CAT, TOEIC_SW) have no CEFR-named band at all — the design would break for them. |
| D17 | **Placement is "done" when enough skills are measured to compute an overall**; a skill that failed grading is marked not-measured rather than blocking completion. | A partial result beats no result. Requires the `done ⇒ band is not null` invariant to become `done ⇒ band exists or a reason is recorded`. |
| D18 | **Reusing an earlier result is allowed for 90 days**, per skill. The UI shows the date each result was measured. | Ability drifts; eight-month-old results say nothing about today. Re-measuring all four because the certificate changed is wasteful. |
| D19 | **No overall timer; a fixed 60-second recording limit per speaking task.** | Placement measures ability, not speed. The recording limit is the one exception: speech must be bounded and conditions equal. |
| D20 | **The pool is uploaded as JSON, one file per skill**, validated with per-line errors. UI editing comes later. | Bulk load is the only practical way to fill hundreds of items. JSON represents shared passages and multi-item groups; CSV cannot without duplication. |
| D21 | **Generated content passes automatic validation, then a 10% sample is reviewed by a human.** No pilot study. | A wrong answer key mis-places every learner who meets it, silently. Reviewing 500 items fully is not realistic; the risk of an unmeasured label error is accepted. |
| D22 | **The demo set is a smaller run of the same shape, executed end-to-end with the real AI.** | A fake-AI demo proves nothing about the thing most likely to break. |
| D23 | **The demo set is marked in the data, not implemented as separate code.** | Two code paths for demo and real drift apart. |

**M1 (storage).** Placement keeps using `ExamAttempts` for the sitting — it inherits the engine's state
machine, expiry, locking and draft sync — and `PlacementTests` as the snapshot that records the result.
It does **not** get a separate attempt table. The snapshot is **derived from the attempt by one writer**;
that is the direct lesson of §2.5.

---

## 4. Reuse the mock-exam sitting engine

The owner's instruction: **do not build a second sitting engine.** Adapt the existing one. It already
handles the hard parts of a sitting, and a parallel implementation would drift.

### 4.1 What already exists

- **States:** attempt `in_progress → submitted → graded`, or `abandoned`; free response `draft →
  pending_ai → graded`; placement `in_progress → done | abandoned`.
- **Expiry:** an attempt carries an expiry; reading the sitting auto-submits an expired attempt, and a
  sweep auto-submits expired attempts when the catalogue loads.
- **Phase and group locks:** audio-driven sections are one-way; group-locked parts confirm before
  leaving; a hand-edited URL cannot rewind past a closed boundary.
- **Preflight:** audio and device verification before a listening sitting.
- **Draft saving and sync:** answers persist as they change, not at the end.
- **One shared scoring function** used by the background grading worker and the results page, so the
  stored figure and the displayed figure cannot disagree.

### 4.2 What the SRS must add, because placement needs it and the mock exam does not

State the **behaviour**, not the implementation:

1. **Resume after exit.** The learner closes the tab mid-placement. What do they see on return? With no
   overall timer (D19), what does "still in progress" mean and which state is authoritative?
2. **Resume after reload**, including a reload during a speaking recording and during AI grading of an
   already-submitted response.
3. **Offline and reconnection.** The mock exam warns on connection loss. For placement: what happens to
   an answer typed but not yet synced, and to the answered count the learner sees?
4. **Partial submission.** May a learner submit with sections unanswered? Refused, allowed-and-scored-
   wrong, or allowed with a confirmation naming the unanswered count? The mock exam confirms; say
   whether placement copies that.
5. **The two-stage transition.** Stage 1 ends mid-sitting. What does the learner see — a pause, a
   message, nothing? Is there a point where they can be interrupted and resume into stage 2?
6. **Abandonment.** When is a placement `abandoned` rather than `done`? With no overall timer (D19),
   expiry means something different and must be defined.
7. **AI grading in flight.** §2.5 is exactly this failure. State the rule so it cannot recur: **the
   snapshot is derived from the attempt, never written beside it.**
8. **One-way versus free navigation.** Placement inherits the mock exam's one-way locks today. Is that
   right for a measurement instrument? A lock can prevent a learner correcting a mis-click, which
   changes the measurement. Argue it either way.

---

## 5. What the SRS must answer

1. **Item counts, with arithmetic.** How many items in stage 1 and stage 2, per skill, to separate
   adjacent bands at a stated error rate. **Hard limit: 60 minutes total.** Do not allocate stage 1 and
   stage 2 by feel — derive both from the same budget and show that they fit together.
2. **Stage 2 selection and stopping.** Given a stage-1 estimate, which items does stage 2 serve, and
   when does it stop?
3. **Task design for Speaking and Writing.** Two tasks per skill (D6), chosen to fit the estimated
   level. What task types, what prompts, what rubrics, and what reliability does one task have? State
   the confidence you claim for a two-task skill estimate.
4. **Content blueprint.** Items per band per skill, tasks per band per skill, and the total. Derive from
   the 60-minute limit and D10's five-item minimum rather than choosing numbers.
5. **Migration.** What happens to existing mastery records, existing placement results, existing paths
   and the seeded demo content when the level model changes from 1–5 to CEFR. The product has no
   migrations and is not in production, so "start clean" is available — say what you would preserve
   and why.
6. **Cross-brief consequence.** The learning path currently reads a *single* band. Placement will
   produce four. **State whether a path should follow the overall CEFR or become per-skill**, and what
   that changes for the path's schema. This is the one thing the path brief cannot settle alone.

---

## 6. Known gaps, deliberately not solved here

- **Accessibility.** A deaf learner taking a Listening section, a learner with dyslexia taking a Writing
  task. Out of scope for v1 by the owner's decision. Do not design for it; do note in open questions
  what a future version would need to change.
- **Pricing.** Out of scope entirely.
- **The demo set's contents.** This brief says how a demo differs from real content, not what it holds.

---

## 7. Deliverable format

An SRS with:

1. Scope and non-goals.
2. **Domain model** — entities, relationships, and the level model. A schema sketch is welcome; this is
   one of the few places a diagram earns its keep.
3. **Numbered, testable functional requirements**, grouped by: starting a sitting, serving items,
   answering, the two-stage transition, submitting, grading, reporting a result.
4. **Business rules** that are not obvious from the requirements: the monotonicity rule, the guessing
   floor, partial results, cooldown, reuse window, what happens when the pool lacks an item.
5. **Content blueprint** with the arithmetic behind it.
6. **Open questions**, each with options and your recommendation.
7. **References** to real standards — CEFR descriptors, adaptive/multistage testing, item calibration.
   Cite specific sources; the implementing agent will check them.

---

## 8. What to be careful about

- **Do not design for one certificate.** Four of five have four skills; two examine only two. A rule
  naming a certificate is a red flag — that exact mistake caused a defect here before.
- **Do not invent numbers.** Where you cannot justify an item count or a reliability figure, say so in
  open questions. The current placement is what happens when plausible numbers are chosen: 24 items over
  three difficulty levels that cannot measure what they claim.
- **A band is not a CEFR level.** TOIC 785–940 is labelled "B2" inside this product and is not CEFR B2.
  D16 resolves this by scoring on the CEFR axis; do not reintroduce cross-certificate conversion without
  arguing for it.
- **Speaking and Writing are the hard half.** Listening and Reading placement is a labelled item bank.
  Speaking and Writing placement is task design, rubric design, and an AI reliability claim you must
  defend. The current product has neither, so there is no prior art to lean on.
- **Do not propose a second sitting engine.** §4 says what to reuse and what to add.
- **The answer key is the highest-risk artefact.** A wrong key mis-places every learner who meets that
  item and nobody finds out. Treat key verification as a first-class requirement.

---

## 9. Appendix: current data

Captured from the seeded demo database on 2026-11-03. This is the starting point being replaced, not a
target.

### 9.1 Placement exams today

| Exam | Skills | Declared | Actual | Difficulty 1/2/3/4/5 |
|---|---|---|---|---|
| TOEIC Placement Test — Diagnostic | Listening, Reading | 24 | 24 | 4 / 11 / 9 / 0 / 0 |
| IELTS-DIAG-01 | Listening, Reading | 10 | 10 | 0 / 2 / 8 / 0 / 0 |
| VSTEP-DIAG-01 | Listening, Reading | 10 | 10 | (small) |
| Diagnostic Benchmark 45M | L, R + 2 Writing | 32 | 32 | includes 2 free responses |

### 9.2 Band tables (the scales the product already has)

| Certificate | Band code | Range | Cumulative study hours |
|---|---|---|---|
| TOEIC | below_A1 | 10–119 | 0 |
| TOEIC | A1 | 120–220 | 100 |
| TOEIC | A2 | 225–545 | 250 |
| TOEIC | B1 | 550–780 | 500 |
| TOEIC | B2 | 785–940 | 800 |
| TOEIC | C1 | 945–990 | 1150 |
| IELTS | B1 | 40–50 | 500 |
| IELTS | B2 | 55–65 | 800 |
| IELTS | C1 | 70–80 | 1150 |
| IELTS | C2 | 85–90 | 1550 |
| VSTEP | A2 | 40–45 | 250 |
| VSTEP | B1 | 50–60 | 500 |
| VSTEP | B2 | 65–75 | 800 |
| VSTEP | C1 | 80–100 | 1150 |
| TOEIC_CAT | CAT_B1 | 400–695 | NULL |
| TOEIC_CAT | CAT_B2 | 700–990 | NULL |
| TOEIC_SW | SW_B1 | 160–250 | NULL |
| TOEIC_SW | SW_B2 | 260–400 | NULL |

The last four rows are **not CEFR-named** — which is why D16 matters.

### 9.3 Grading rubrics in use

The AI returns a score per criterion, a comment per criterion, located errors, a priority fix and a
rewrite. Never a band, never a percentage.

| Certificate | Skill | Criteria | Scale |
|---|---|---|---|
| IELTS | Writing | Task Response, Coherence & Cohesion, Lexical Resource, Grammatical Range | 9.0 each |
| IELTS | Speaking | Fluency & Coherence, Lexical Resource, Grammatical Range, Pronunciation | 9.0 each |
| TOEIC | Writing | Grammar 5, Vocabulary 5, Organization 5, Relevance 5 | 5.0 each |
| TOEIC | Speaking | Pronunciation 3, Intonation 3, Grammar/Vocab/Cohesion 3, Relevance 3 | 3.0 each |
| VSTEP | Writing / Speaking | Task Fulfilment and others | 10.0 each |

**Most of these criteria have no band descriptors written.** The AI has been scoring against a scale
nobody wrote down. Fixing that is part of the content specification this rebuild needs.
