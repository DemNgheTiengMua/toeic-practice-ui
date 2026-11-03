# Research brief: rebuild the Placement Test, four skills

**For an external deep-research agent with no repository access.** Self-contained: the product, the
measured state, every decision already taken, and what the SRS must answer.

**Scope:** the placement test only. The learning path is a separate brief
(`path-brief-audit.md` records what it needs from this one). Pricing is out of scope.

---

## 1. What the product is

A multi-certificate English exam-practice web application. A learner picks a **certificate**, sets a
**goal** (current band, target band, exam date, hours per week), takes a **placement test** to establish
a starting band, then follows a generated **learning path**.

Five certificates, and **they do not all have four skills**:

| Certificate | Skills |
|---|---|
| TOEIC | Listening, Reading, Speaking, Writing |
| IELTS Academic & General | Listening, Reading, Writing, Speaking |
| VSTEP (Vietnam national) | Listening, Reading, Writing, Speaking |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading |
| TOEIC Speaking & Writing | Speaking, Writing |

Domain language the SRS should adopt: Certificate, Skill (one modality), Section (a part of a paper),
Exam (a concrete paper), Practice session (free drill), Attempt (a sitting), Free response (spoken or
written answer scored by AI), Raw score → Scaled score → Band.

Two invariants: placement and practice are **always free** (no credit ledger row), and one
certificate's content, scoring or bands can never leak into another.

---

## 2. The problem, measured

The current placement is a single 24-question TOEIC diagnostic plus two 10-question exams and a
32-question benchmark. It has four defects that make its result untrustworthy.

### 2.1 It cannot discriminate between adjacent bands

The recommended TOEIC placement (24 questions) has **4 items at difficulty 1, 11 at 2, 9 at 3, and
none above 3**:

| Difficulty | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|
| Items | 4 | 11 | 9 | 0 | 0 |

A test with no difficult items cannot distinguish a B2 response from a C1 one. It reports a band
anyway, and that band becomes the learner's starting point for everything downstream.

### 2.2 It covers two skills out of four

Every placement exam in the seed is Listening and Reading only. Speaking and Writing — two of the four
skills the product claims to teach — have **no starting point at all**. A learner's writing path is
planned without anyone ever having read a sentence they wrote.

### 2.3 Nothing is randomized

Question selection is fixed; two attempts are identical. A learner who retakes sees the same paper.

### 2.4 Difficulty has no meaning

Questions carry `DifficultyLevel` 1–5, with **no mapping to CEFR or to any certificate scale**, and
**zero items at level 5** across the whole bank. "Hard" is a number nobody defined.

### 2.5 A live defect, confirmed at runtime

A placement exam that contains free-response questions **cannot be completed**. On submit the free
response goes to `pending_ai`, the attempt stays `submitted` with no band, and the placement snapshot is
then written as `done` with a null band — which a database constraint correctly refuses. Two writers
disagree about one row.

This matters here because the rebuild makes free responses the *normal* case: four skills means
Speaking and Writing in every placement. Detail in `placement-free-response-defect.md`.

---

## 3. Decisions already taken

These are settled. The SRS builds on them; it should not relitigate them.

| # | Decision | Reason |
|---|---|---|
| D1 | **Placement returns one CEFR per skill plus one overall CEFR.** The overall is the **weakest** skill, not an average. | A single band hides "reads B2, speaks A2", which is exactly what the path needs. An average claims everything is fine. |
| D2 | **CEFR is attached directly to each question** (A1–C2). `DifficultyLevel` 1–5 is replaced, not mapped. | A conversion table between 1–5 and CEFR is another invented mapping; that class of table already caused a defect. |
| D3 | **One shared pool for all certificates.** The certificate decides only which skills are measured and which target band is shown. | The owner asked for one placement design regardless of certificate. Shared pool avoids multiplying content cost by five. |
| D4 | **Placement measures only the skills the chosen certificate has.** TOEIC_CAT is measured on two, not four. Results for skills the certificate lacks are stored but not shown. | Measuring Speaking for a certificate that never examines it produces a number nobody uses. Storing it means a later switch to IELTS already has the data. |
| D5 | **Results appear progressively.** Numeric/objective skills are scored and shown immediately; Speaking and Writing show "grading" and fill in when done. | A learner watching a spinner for a minute is worse than one watching their result assemble. If AI fails, the objective half still has value. |
| D6 | **Two tasks per Speaking skill and two per Writing skill**, chosen to fit the level estimated from the objective half. | One task is too thin to place a person; four makes the sitting unbearable. Choosing by level means an A2 learner is not asked for an academic argument. |
| D7 | **A failed task does not void the skill.** One task graded → result shown, marked as estimated from one task. Both failed → the skill is "not measured", and the overall skips it. | Treating an AI failure as A1 is a fabricated result. "Not measured" is honest. |
| D8 | **Retake allowed with a 24-hour cooldown**, unlimited total. | Random pools plus instant retakes invites "keep going until a high band appears" — the exploit the practice cooldown already closed. A cooldown still allows recovery from a genuine mishap. |
| D9 | **Two-stage adaptive (multistage), not per-item adaptive, not linear.** Stage 1: fixed blueprint across bands. Stage 2: items only around the estimated level. | Linear was rejected by the owner: it forces a long paper and leaves each band thin. Per-item CAT needs calibrated item parameters we do not have. |
| D10 | **Stage 2 delivers at least 5 items per band** for objective skills, so a lucky guess cannot decide a band. | With 4-option items, guessing gives a 25% floor. At 4 items one guess moves the result 25%. |
| D11 | **Full CEFR range A1–C2.** AI generation removes the "too thin to cover the range" objection. | Cutting C2 breaks the path to a C1 target; cutting A1 abandons the beginner, who needs placement most. |
| D12 | **A skill is shown as CEFR plus a 0–100 number**, and that number is the **position inside the band**, not a percentage of the certificate scale and not raw percentage correct. | It answers "am I improving" — two attempts both B2 read 78% then 12% as progress. The UI must show band and number together, because crossing a band resets the number downward. |
| D13 | **The 0–100 rule is the same for all four skills.** Objective skills go raw → scaled through the certificate's own scale, then to a position in band; Speaking/Writing go rubric → band, then position in band. | Comparing four skills requires one unit. Two units means the result table cannot answer "which is my weakest". |
| D14 | **Band determination for objective skills: ≥70% correct at band L and at every lower band → CEFR = L**, with a published threshold and a monotonicity check. | 70% sits above the 25% guessing floor. Publishing the threshold is more honest than "the system says B1". |
| D15 | **Non-monotonic results are flagged, not silently accepted.** Correct at B2 but wrong at A2 means guessing or a bad item. Take the highest band of the first monotonic run, and record it for an administrator. | A high rate of these means the pool is broken, and nobody would find out otherwise. |
| D16 | **Scoring happens on the CEFR axis, never on the certificate's own scale.** The certificate is not involved in grading. | Scoring on certificate scales produces three different numbers for the same answers, and TOEIC_CAT has no CEFR-named band at all — the design would break for it. |
| D17 | **Placement is "done" when enough skills are measured to compute an overall**; skills that failed grading are marked not-measured rather than blocking completion. | A partial result beats no result. This requires the `done ⇒ band is not null` constraint to change to `done ⇒ band exists or a reason is recorded`. |
| D18 | **Reusing an earlier placement result is allowed for 90 days**, per skill; expired skills are re-measured. The UI must show the date each result was measured. | Ability drifts; results eight months old say nothing about today. Re-measuring all four because the learner switched certificate is wasteful. |
| D19 | **No overall timer; 60 seconds fixed recording limit per speaking task.** | Placement measures ability, not speed; a timer mixes the two. A fixed limit is the one exception, because speech must be bounded and everyone must have equal conditions. |
| D20 | **The pool can be uploaded as JSON, one file per skill**, validated with per-line errors. UI editing comes later. | Bulk load is the only practical way to fill hundreds of items. JSON represents shared passages and multi-question groups; CSV cannot without duplication. |
| D21 | **Generated content passes automatic validation, then a 10% sample is reviewed by a human.** | A wrong answer key in the placement pool mis-places every learner who meets that item, silently. A 100% human review of 500 items is not realistic. |
| D22 | **The demo set is a smaller run of the same shape, executed end-to-end with the real AI**, so a demo exercises the real pipeline. | A fake-AI demo proves nothing about the thing most likely to break. The cost is accepted. |
| D23 | **The demo set is marked in the data, not implemented as separate code.** | Two code paths for demo and real drift apart. |

---

## 4. The engine: reuse the mock-exam sitting engine

The owner's instruction: **do not build a second sitting engine. Reuse the existing mock-exam engine
and adapt it.** It already handles the states and interruptions that make a sitting hard, and a parallel
implementation would drift.

### 4.1 What already exists

- **States:** attempt `in_progress → submitted → graded`, or `abandoned`; free response `draft →
  pending_ai → graded`; placement `in_progress → done | abandoned`.
- **Expiry:** an attempt carries `ExpiresAt`; reading the sitting auto-submits an expired attempt, and a
  sweep auto-submits expired attempts when the catalogue loads.
- **Phase and group locks:** audio-driven sections are one-way, group-locked parts confirm before
  leaving, and a hand-edited URL cannot rewind past a closed boundary.
- **Preflight:** audio and device verification before a listening sitting starts.
- **Draft saving and sync:** answers persist as they change rather than at the end.
- **Scoring and band placement** with one shared function used by the worker and the review page.

### 4.2 What the SRS must specify

The engine is reused, but placement adds requirements the mock exam does not have. For each, state the
behaviour, not the implementation:

1. **Resume after exit.** A learner closes the tab mid-placement and returns. What do they see? What is
   the time limit story when there is no overall timer (D19)? Which state is authoritative?
2. **Resume after reload**, including a reload during a speaking recording and during AI grading of an
   already-submitted response.
3. **Offline and reconnection.** The existing mock exam warns on connection loss. For placement, what
   happens to an answer typed but not yet synced, and to the answer count shown to the learner?
4. **Partial submission.** If a learner submits with sections unanswered, is that allowed (and scored
   as wrong), refused, or allowed with confirmation? The mock exam confirms and names the unanswered
   count — say whether placement copies that.
5. **Two-stage transition.** Stage 1 ends mid-sitting. Does the learner see a pause, a message, or
   nothing? Is there a point where they could be interrupted and resume into stage 2?
6. **Abandonment.** When is a placement `abandoned` rather than `done`? The current system abandons an
   expired attempt — for a test with no timer (D19), expiry means something else, and that must be
   defined.
7. **AI grading in flight.** The confirmed defect in §2.5 is exactly this: one writer assumes grading
   is synchronous. State the rule so it cannot recur — the snapshot is derived from the attempt, never
   written beside it.
8. **One-way versus free navigation.** Placement currently inherits the mock exam's one-way locks. Is
   that right for a test whose purpose is measurement? Argue it: a lock can prevent a learner from
   correcting a mis-click, which changes the measurement.

### 4.3 What must not be reused

- **The certificate-scale band lookup.** Placement scores on CEFR (D16).
- **The single-band result.** Placement has four (D1).
- **The synchronous-submission assumption.** See §2.5.

---

## 5. What the SRS must answer

1. **The level model.** Q-per-question CEFR labels: what makes a label defensible, and how is it
   verified without a large human review (D21)?
2. **Item counts, with arithmetic.** How many items in each stage, per skill, to separate adjacent
   bands at a stated error rate. Show the calculation. **Hard limit: 60 minutes total.**
3. **Stage 2 selection.** Given a stage-1 estimate, which items does stage 2 serve, and when does it
   stop?
4. **Speaking and Writing task design.** Two tasks per skill, chosen to fit the level (D6). What task
   types, what prompts, what rubrics, and what AI reliability does one task have? State the confidence
   you are claiming for a two-task skill estimate.
5. **Content blueprint.** Items per band per skill, tasks per band per skill, and the total. Given the
   60-minute limit and D10's 5-item minimum, derive it rather than choosing it.
6. **Migration.** What happens to existing mastery records, existing placement results, existing paths,
   and the seeded demo content when the level model changes from 1–5 to CEFR. The product is not in
   production and has no migrations, so "start clean" is available — say what you would preserve.
7. **The path question.** The learning path currently reads one band (`LearningPathService.Generation`
   takes a single `ResultBandCode`). Placement will produce four. **Does a path follow the overall
   CEFR, or does it become per-skill?** This is explicitly open; whichever answer you give changes the
   path schema, so state the consequence.

---

## 6. Deliverable format

An SRS with: scope and non-goals; domain model including the level model (a schema sketch is welcome
here); numbered testable functional requirements grouped by placement / level management / content
authoring / reporting; business rules that are not obvious from the requirements; the content
blueprint with its arithmetic; open questions with options and your recommendation; and references to
real standards (CEFR alignment, adaptive testing, item calibration) that an implementing agent can
check.

---

## 7. What to be careful about

- **Do not design for one certificate.** Four of five have four skills; one has two of each pair. A rule
  naming a certificate is a red flag — that exact mistake produced a defect previously.
- **Do not invent numbers.** Where you cannot justify an item count or a reliability figure, say so in
  open questions. The current placement is what happens when plausible numbers are chosen: 24 items
  over three difficulty levels that cannot measure what they claim.
- **A band is not a CEFR level.** TOEIC 785–940 is called "B2" inside the product and is not CEFR B2.
  D16 resolves this by scoring on the CEFR axis; do not reintroduce cross-certificate conversion
  without arguing for it.
- **Speaking and Writing are the hard half.** Listening and Reading placement is an item bank with
  labels. Speaking and Writing placement is task design, rubric design, and AI reliability. The current
  system has neither, so there is no prior art here to lean on.
- **Do not propose a second sitting engine.** §4 explains what to reuse and the specific requirements
  it adds.

---

## 8. Appendix: current data

Captured from the seeded demo database on 2026-11-03. Treat as the starting point being replaced, not
as a target.

### 8.1 Placement exams today

| Exam | Skill coverage | Declared | Actual | Difficulty spread 1/2/3/4/5 |
|---|---|---|---|---|
| TOEIC Placement Test — Diagnostic | Listening, Reading | 24 | 24 | 4 / 11 / 9 / 0 / 0 |
| IELTS-DIAG-01 | Listening, Reading | 10 | 10 | 0 / 2 / 8 / 0 / 0 |
| VSTEP-DIAG-01 | Listening, Reading | 10 | 10 | (small) |
| Diagnostic Benchmark 45M | L, R + 2 Writing | 32 | 32 | includes 2 free responses |

### 8.2 Band tables (the scales the product already has)

| Certificate | Band | Range | Cumulative hours |
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

Note the last two rows: they are **not CEFR-named**, which is why D16 matters.

### 8.3 Grading rubrics in use (what the AI returns)

Each certificate has its own criteria and its own scale. The AI returns a score per criterion, a comment
per criterion, located errors, a priority fix and a rewrite — never a band, never a percentage.

| Certificate | Skill | Criteria | Scale |
|---|---|---|---|
| IELTS | Writing | Task Response, Coherence & Cohesion, Lexical Resource, Grammatical Range | 9.0 each |
| IELTS | Speaking | Fluency & Coherence, Lexical Resource, Grammatical Range, Pronunciation | 9.0 each |
| TOEIC | Writing | Grammar 5, Vocabulary 5, Organization 5, Relevance 5 | 5.0 each |
| TOEIC | Speaking | Pronunciation 3, Intonation 3, Grammar/Vocab/Cohesion 3, Relevance 3 | 3.0 each |
| VSTEP | Writing / Speaking | Task Fulfilment, and others | 10.0 each |

The rubric total is what produces a band today. Under D13 the same rubric total will produce a position
inside a CEFR band instead.
