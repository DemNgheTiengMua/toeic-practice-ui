# Research brief — Practice subsystem (rebuild)

**Audience:** one external deep-research agent, no repository access. This file is self-contained.

**What you deliver:** a complete SRS that will become specs → tickets → build.

**Scope:** practice only. Placement and the learning path have their own briefs. Pricing is out.

**This is a delete-and-rebuild.** Existing code and schema are evidence about what went wrong, not a
constraint on the answer.

---

## 1. The product

A multi-certificate English exam-practice web application. A learner picks a **certificate**, sets a
**goal** (starting band, target band, exam date, hours per week), takes a **placement test**, follows a
**learning path**, and drills between path steps.

Five certificates. **They do not all have four skills:**

| Certificate | Skills |
|---|---|
| TOEIC | Listening, Reading, Speaking, Writing |
| IELTS Academic & General | Listening, Reading, Writing, Speaking |
| VSTEP (Vietnam national) | Listening, Reading, Writing, Speaking |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading |
| TOEIC Speaking & Writing | Speaking, Writing |

**Domain vocabulary to adopt:** Certificate, **Skill** (one modality), **Section** (a part of a paper,
e.g. TOEIC "Part 5"), **Topic** (a grammar or skill sub-area, e.g. "Verb tense"), **Exam**, **Practice
session** (a short free drill), **Lesson** (authored teaching material made of cards), **Attempt** (a
sitting of an exam), **Free response** (spoken or written answer scored by AI), **Raw score → Scaled
score → Band**, **CEFR level** (A1–C2).

**Invariants that must survive the rebuild:**

- Practice is **always free**. No credit ledger row, ever.
- A certificate's content, scoring and bands never leak into another.
- An AI never chooses a question, a score, or a schedule. It writes narrative only.
- Derived values (accuracy, mastery, progress) are computed from stored facts, never stored twice.

---

## 2. What practice does today

Two selectors, and they disagree.

### 2.1 Standalone practice — a mastery machine over topics

- The unit is the **Topic**, never the individual question. Rationale given: repeating one question
  teaches that question's answer, not the grammar behind it.
- Four states: **Untested → Learning → Reviewing → Mastered**, driven by a pure state machine:

| From | Event | To | Next review |
|---|---|---|---|
| Untested | first answer, right or wrong | Learning | +24 h |
| Learning | accuracy < 75%, or < 5 answers, or a wrong answer | Learning | +24 h |
| Learning | ≥ 75% over ≥ 5 answers, or streak ≥ 3 | Reviewing | +3 d |
| Reviewing | answer, still short of the Mastered proof | Reviewing | +7 d |
| Reviewing | ≥ 85% over ≥ 10 answers, across ≥ 2 distinct days | Mastered | +14 d |
| Mastered | answer, no lapse | Mastered | +14 d |
| Mastered | one wrong answer | Mastered | +24 h (not +14 d) |
| Mastered | second wrong in a row (**lapse**) | Reviewing | +24 h |

- **A wrong answer also starts a 24-hour cooldown on that exact question**, separate from the topic's
  next-review date. Two layers: the topic is not due, and this question is not to be re-served.
- A session is **10 questions**: at most 4 previously seen, the rest unseen. When the eligible stock is
  short, the session is shorter rather than padded with repeats.
- Learner sees a progress figure per topic and the cooldown moment.

### 2.2 Path practice — a section drill

A learning-path step names a **section** and serves questions from it in `DisplayOrder`. It does **not**
consult mastery, does **not** respect the 24-hour cooldown, and does **not** feed the topic mastery
machine. A learner can meet the question they just got wrong, in the same hour, via the path.

### 2.3 What is missing entirely

- **Speaking practice** exists as a separate recorded-and-AI-graded attempt, not as a drill. A spoken
  answer cannot be typed, so it was never part of the practice session model.
- **Writing practice** exists the same way: a written answer graded by AI, plus one narrow "typed drill"
  path that checks word count and required keywords without AI.
- **No intensity control.** Ten questions is a constant. A learner three weeks from their exam cannot
  drill harder.
- **No connection to level.** Questions carry `DifficultyLevel` 1–5 with no meaning (the placement
  rebuild replaces it with CEFR). Practice cannot offer "B1 items on your weakest topic".

---

## 3. Measured state (what the rebuild replaces)

Captured from the seeded demo database on 2026-11-03. These numbers describe the system **before** the
rebuild; the placement rebuild replaces the level model, so treat them as evidence, not as a target.

| Measure | Value |
|---|---|
| Questions in the bank | 116, across 5 certificates and 4 skills |
| Questions at difficulty 1 / 2 / 3 / 4 / 5 | 4 / 62 / 43 / 7 / **0** |
| Topic categories | 15 |
| Topic categories with **any** question | **3** (2, 1 and 1 question) |
| Sections declaring ≥ 30 questions | 8 |
| Sections with **zero** questions | 5 |
| Lessons | 13 |
| Lessons with a level column | **none exists** |
| Lessons attached to a topic | 1 of 13 |

**The consequence:** a mastery machine over topics is only as good as the topics. With three usable
topics, "your weakest topic" is nearly meaningless, and a session of ten questions from a section that
holds eleven runs out fast.

---

## 4. Questions the SRS must answer

### 4.1 What selects the next question?

Two selectors exist and disagree. Choose one model and state why the other loses:

- **Topic mastery + cooldown** (what standalone practice does) — spaces repetition by topic, but can
  drift far from what the exam will ask.
- **Section drill** (what path practice does) — mirrors exam structure, but re-serves the question just
  missed and teaches nothing about weakness.
- **A third thing** — if neither is right, say what is.

Whatever you choose must serve a learner whose goal is a specific band on a specific certificate, and
must not require the certificate to have four skills.

### 4.2 Is the Topic the right unit?

The current rationale is that repeating one question teaches the answer, not the grammar. That is
correct as far as it goes. But with 15 topics and 3 of them populated, the unit as designed cannot
work in practice.

- Is the Topic the right grain, or does the right grain depend on the skill?
- How many topics does a workable mastery model need, per skill, per certificate?
- Does a topic belong to a certificate or to the language? A TOEIC learner and an IELTS learner both
  need "verb tense" — is that one topic or two?

### 4.3 What does a practice session mean for each skill?

Typed multiple-choice works for Listening and Reading. It does not work for Speaking and Writing, which
are free responses graded by AI against a certificate rubric.

- Is a "session" for Speaking a set of recorded tasks? How many, at what length?
- Writing: one task, or several? Does a draft count as practice?
- **Does AI-graded practice feed the same mastery as typed drills?** If yes, a topic's mastery can be
  advanced by writing an essay — is that right? If no, a learner whose goal is Writing has a mastery
  model that never moves.

### 4.4 How does intensity vary?

Ten questions is a constant. A learner at A2 targeting B2 in three weeks and a learner casually
practising for next year get the same session.

- Should session size vary, and on what — distance to target, time to exam, recent accuracy?
- What caps it? A learner who drills 200 questions a day is not learning better; they are exhausting
  the pool.
- Is there a daily or weekly ceiling, and what does the learner see when they hit it?

### 4.5 How does practice relate to level?

The placement rebuild labels every question with a CEFR level.

- Should practice serve items **at** the learner's level, **just above** it, or **spanning** it?
- Should the level of practice items follow the weakest skill (as the overall band does) or each skill's
  own level?
- What happens when the pool has no items at the needed level for a skill?

### 4.6 How does practice relate to the learning path?

- Should a path step that says "practise P5" respect the topic cooldown, or is a path step an override?
- If a path step *must* be doable now (the plan says so), a cooldown that blocks it breaks the plan.
  Who wins?
- Should completing a path step be able to close on "practised" rather than "improved"?

### 4.7 What does mastery actually gate?

Today mastery affects only what is served next. It does not gate a path step, warn the learner, or
change the plan.

- Should "Mastered" mean anything to the learner beyond a label?
- Should a topic stay mastered forever, or decay if untouched for months?
- What does a learner see that makes mastery worth caring about?

### 4.8 How much repetition is healthy?

The current rule: 10 questions, at most 4 previously seen, rest unseen. No evidence is offered for
those numbers.

- Derive or justify a session composition. If you cannot, say it is provisional rather than dressing
  it as evidence.
- Is "previously seen" the right distinction, or should it be "previously seen and not yet mastered"?

---

## 5. Reuse

Two pieces are worth keeping and the SRS should say whether it keeps them:

- **The mastery state machine as a pure function.** Input an answer, output the next state and due
  date — no database, no clock of its own. It is currently correct and testable; whatever replaces it
  should keep that property, because a state machine entangled with persistence cannot be tested
  without a database.
- **The single-writer rule for recording an answer.** One place records the grade, updates mastery,
  and starts the cooldown. A previous defect came from two writers disagreeing about one answer (a
  re-submission overwrote the session score while mastery skipped the re-grade). Whatever replaces it
  must have exactly one writer per fact.

Everything else — the topic tables, the planner, the selector — is candidate for deletion.

---

## 6. Deliverable format

An SRS with:

1. **Scope and non-goals**, including what of the current system survives.
2. **Domain model** — entity names, relationships, and the level/topic model. A schema sketch is
   welcome; this is one of the few places a diagram earns its keep.
3. **Numbered, testable functional requirements**, grouped by: starting a session, selecting questions,
   recording an answer, mastery and spacing, per-skill practice (Listening/Reading vs Speaking/Writing),
   reporting to the learner.
4. **Business rules** that are not obvious from the requirements — cooldown, lapse, decay, eligibility,
   what happens when the pool is short, what happens when AI grading fails.
5. **Content requirements** — how many items, at what levels, per topic, per skill, for the mastery
   model to work. Derive, do not assert.
6. **Open questions** with options and your recommendation.
7. **References** to real sources on spaced repetition, interleaving, retrieval practice, and
   mastery-based selection. Cite specific work; the implementing agent will check.

---

## 7. What to be careful about

- **Do not design for one certificate.** One of five has two skills. A rule naming a certificate is a
  red flag — that mistake caused a defect here before.
- **Do not invent numbers.** If you cannot justify "10 questions" or "75% accuracy", say it is
  provisional. Those exact numbers are in the current system with no evidence behind them, and
  inheriting them quietly is how the rebuild repeats the old system with new names.
- **Spaced repetition research is about facts and vocabulary.** Grammar and reading comprehension may
  not behave the same way. If you lean on SRS literature, say where it does and does not transfer.
- **Speaking and Writing are the hard half.** Typed drills are a bank plus a selector. Productive skills
  are task design, rubric design, AI cost, and AI failure. Do not make them an afterthought — the
  current system barely has them, and the product claims four skills.
- **Practice is free.** Any design that needs a credit, a paid grading, or a limit that exists to save
  money contradicts an invariant.
- **The AI is not a teacher here.** It grades and it narrates. It does not choose what to practise.
