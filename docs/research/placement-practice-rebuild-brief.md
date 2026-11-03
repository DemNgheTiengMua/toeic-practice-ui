# Research brief: rebuild Placement and Practice, all four skills

**For an external deep-research agent with no access to this repository.** Everything you need to know
about the current system is in this document; you cannot read the code, so treat the numbers and the
described behaviour as your only source of truth about the "as-is" state.

**Our ask:** a standards-based SRS (and the business rules behind it) for rebuilding the placement test
and the practice subsystem from scratch, covering **Listening, Reading, Speaking and Writing**. Pricing
and credit economics are explicitly out of scope — they will be redesigned later.

---

## 1. What the product is

A multi-certificate English exam-practice web application. A learner picks a **certificate** (TOEIC,
IELTS Academic/General, VSTEP — the Vietnamese national test, plus two TOEIC variants), sets a **goal**
(current band, target band, exam date, hours per week), takes a **placement test** to establish a
starting band, then follows a generated **learning path** of practice sessions, lessons and mock exams.

The domain language we use, which your SRS should adopt:

| Term | Meaning |
|---|---|
| Certificate | A named exam product (TOEIC, IELTS, VSTEP…) |
| Skill | One modality: Listening, Reading, Speaking, Writing |
| Section | A part of a certificate's exam structure (TOEIC "Part 5", IELTS "Reading Passage 1") |
| Exam | A concrete paper within a certificate, with sections and questions |
| Practice session | A short drill of questions, free, no credit cost |
| Attempt | A sitting of an exam (mock) or of a placement test |
| Raw score → Scaled score → Band | A question's marks, the certificate's scaled score, the CEFR-ish band |
| Free response | A spoken or written answer scored by AI against a rubric |

Two hard invariants: placement and practice are **always free** (no credit ledger row), and a
certificate's content, scoring and bands can never leak into another certificate.

---

## 2. The problem, measured

We audited the seeded content model before writing this brief. The numbers are real, from the demo
database.

### 2.1 Questions do not carry a meaningful level

116 questions exist across 17 exams. Difficulty is stored as an integer 1–5:

| Difficulty | Count |
|---|---|
| 1 | 4 |
| 2 | 62 |
| 3 | 43 |
| 4 | 7 |
| 5 | **0** |

So 90% of the bank is "easy or medium", and there is **no hardest-tier item at all**. There is also no
mapping anywhere from that 1–5 scale to CEFR (A1–C2) or to a certificate's own scale (TOEIC 10–990,
IELTS 0–9). Nothing in the schema says "this question is a B2 Reading item".

### 2.2 Topic categories are almost empty

There are 15 topic categories (Verb tense, Preposition, Inference, Main idea, …). Exactly **three** have
any questions:

| Category | Questions |
|---|---|
| Verb tense | 2 |
| Preposition | 1 |
| Part of speech | 1 |
| the other 12 | **0** |

The weakness-analysis feature reads accuracy per topic. With three usable topics it cannot say anything
useful, so the "weakness" a learning path is built from is mostly noise.

### 2.3 Sections ask for far more questions than exist

Each section declares an expected question count. Five sections have **zero** questions; eight declare
30 or more. Examples:

| Section | Declares | Has |
|---|---|---|
| TOEIC Part 7 (Reading Comprehension) | 54 | 11 |
| TOEIC Part 5 (Incomplete Sentences) | 30 | 17 |
| IELTS Reading (R4) | 40 | 6 |
| IELTS Listening (REC) | 40 | 6 |
| TOEIC CAT Part 1 / Part 2 (adaptive) | 6 / 25 | **0 / 0** |

A practice session is supposed to draw ten questions from a section. Several sections cannot supply ten.

### 2.4 Lessons carry no level

13 lessons exist. The lesson table has **no band or level column at all**. Only one of the 13 is
attached to a topic category. So a learning path cannot say "study this lesson because you are at B1 and
weak in verb tense" — it can only say "study this lesson".

### 2.5 Placement cannot discriminate

Placement exams:

| Exam | Declares | Has | Difficulty spread |
|---|---|---|---|
| TOEIC diagnostic | 24 | 24 | levels 1,2,3 only |
| IELTS diagnostic | 10 | 10 | (small) |
| VSTEP diagnostic | 10 | 10 | (small) |
| "Diagnostic Benchmark 45M" | 32 | 32 | includes 2 writing tasks |

The TOEIC placement — the one the product recommends — has **no item above difficulty 3**. It is
therefore structurally incapable of distinguishing B2 from C1. Its result feeds the goal, which feeds
the learning path, which feeds practice. One weak measurement propagates through everything.

Placement covers Listening and Reading only. Speaking and Writing are **not** assessed at all, so those
two skills have no starting point.

### 2.6 What the consequence is, in one sentence

A learner's starting band is measured by a test that cannot measure, their weakness profile is computed
over three topics, their learning path points at sections that lack questions, and no lesson matches
their level. Each layer inherits the previous layer's error.

---

## 3. What we already have that constrains the design

These exist and are considered settled. Your SRS should build on them, not relitigate them.

- **Bands per certificate.** Each certificate has its own band table with min/max scaled scores and, for
  CEFR-aligned certificates, cumulative study hours. Example (TOEIC): `below_A1` 10–119, `A1` 120–220,
  `A2` 225–545, `B1` 550–780, `B2` 785–940, `C1` 945–990, with cumulative hours 0/100/250/500/800/1150.
  A band is looked up from a scaled score; it is never hardcoded.
- **Score aggregation per certificate.** Three rules that work: `sum` (skills add to one total),
  `avg_half_step` (average rounded to the nearest half band, used by IELTS), `avg_whole` (average rounded
  to a whole number, used by VSTEP).
- **Feasibility is computed, not guessed.** Given a band pair and its cumulative hours, plus the
  learner's declared hours per week and their exam date, the system returns feasible / stretch /
  unrealistic. Where a certificate's bands carry no hours, it returns "cannot judge" rather than
  inventing a number.
- **The AI never decides content.** A model may write narrative text (an insight, a comment), never
  choose a step, a question or a score.
- **Mastery is tracked per topic, not per question.** Four states (untested → learning → reviewing →
  mastered), with a 24-hour cooldown after a wrong answer so a learner cannot re-click a question until
  it turns green.
- **Free-response answers are graded by AI against a per-certificate rubric** with an explicit
  band-descriptor per criterion, and a criterion the model omits makes the grading **Fail** rather than
  being filled in with a guess.
- **One attempt = one total score** (plus per-skill scores). The result page, band lookup and learning
  path all assume this.

---

## 4. What we need from you

### 4.1 The level model (the core question)

We must be able to say, for any question, lesson, section and exam: **what level is this, and on what
scale?** Design this concretely. At minimum answer:

- Is level one number, or a pair (difficulty × skill)?
- How does a TOEIC item's level (10–990 scale) relate to a CEFR label, and to an IELTS band? These
  certificates are not interchangeable — a mapping table is a claim that must be defensible, or the
  design must avoid needing one.
- Where does the level live: on the question, on the section, on the exam, or derived?
- What is the minimum evidence to certify a question's level (expert review, calibration on real
  responses, both)? Cite standard practice.

### 4.2 Placement that can actually discriminate

- **How many items, and at what levels,** to separate adjacent bands (e.g. B1 vs B2) with a stated
  confidence? Give the arithmetic, not a vibe. Our current test fails here with 24 items spread over
  three difficulty levels.
- **Adaptive or fixed?** If adaptive, give the item-selection rule and the stopping rule. If fixed, give
  the blueprint (how many items per skill per level).
- **How to assess Speaking and Writing at placement** without a human rater, given AI grading exists.
  What does the rubric look like, how many tasks, and what is the reliability of a single AI-scored
  task?
- **What the output is**: one overall band, or one per skill? Our current model is one band per
  attempt — say whether that is right, and if not, what the schema and the UI must carry instead.
- How does placement interact with the existing rule that a learner may later **diverge** from their
  placement band (we detect this and offer a correction; we never silently rewrite their path)?

### 4.3 Practice that teaches rather than tests

- **What selects the next question?** We have two selectors today and they disagree: standalone practice
  uses topic mastery + cooldown; path practice picks any question in a section. Which is right, or is
  neither?
- **How much repetition is healthy** before a learner is wasting time? Our current rule caps a session
  at 10 questions with at most 4 previously seen.
- **How should a practice session differ per skill?** Typed MCQ works for Listening/Reading. Speaking and
  Writing practice is a recorded/AI-graded free response — what does a "practice session" mean there?
- **What is the relationship between a lesson and practice?** Today a lesson is cards with a micro-check;
  practice is questions. Should a lesson's micro-check feed the same mastery as practice?

### 4.4 The content blueprint

Given all of the above, tell us **how much content we need**, per certificate and per skill: how many
questions at each level, how many lessons, how many placement items. This is the number that decides
whether the rebuild is feasible, so be concrete. Our current bank is 116 questions for five certificates
and four skills, which is roughly two orders of magnitude short.

### 4.5 Migration

We are deleting and rebuilding rather than patching. Say what that means for: existing learner mastery
records, existing placement results, existing learning paths, and the seeded demo content. We have no
migrations (schema is a single SQL file), and the product is not in production, so "start clean" is
available — but say what you would preserve and why.

---

## 5. Deliverable format

An SRS in these parts. Write in English; be specific enough that an engineer can implement without
asking you questions.

1. **Scope and non-goals.** Including what of the current system is kept.
2. **Domain model.** Entities, their relationships, and the level model from §4.1. A schema sketch is
   welcome — this is the one place a diagram or DDL earns its keep.
3. **Functional requirements**, numbered, one requirement per line, testable. Group by: placement,
   practice, level management, content authoring, reporting.
4. **Business rules.** Every rule that is not obvious from the requirements: minimum evidence, cooldown,
   adaptive stopping, band derivation, divergence detection, what happens when content is missing.
5. **Content blueprint.** The tables from §4.4, with the arithmetic that produced them.
6. **Open questions** you could not settle, with the options and your recommendation.
7. **References.** Standard practice you relied on (CEFR alignment, item response theory, adaptive
   testing, spaced repetition). Cite real sources, not generalities — the implementing agent will check
   them.

---

## 6. What to be careful about

- **Do not design for a single certificate.** The system is explicitly multi-certificate, and one of the
  existing bugs came from a label that only made sense for TOEIC. Any rule that names a certificate is a
  red flag.
- **Do not invent numbers.** Where you cannot justify an item count or a reliability figure, say so in
  open questions rather than picking a plausible-sounding one. Our current content model is exactly what
  happens when plausible-sounding numbers are picked: 24 placement items over three difficulty levels
  that cannot measure what they claim.
- **A band is not a CEFR level.** TOEIC 785–940 is called "B2" inside the product, but TOEIC's own
  mapping to CEFR is a published claim with caveats. If your design needs cross-certificate comparison,
  justify it explicitly.
- **Speaking and Writing are the hard half.** Listening and Reading practice is a question bank with
  levels. Speaking and Writing practice is task design, rubric design, and AI reliability. Do not let
  them be an afterthought — the current system barely has them, and the owner wants all four skills.
