# Research brief — Content blueprint and authoring standard

**Audience:** one external deep-research agent, no repository access. Self-contained.

**What you deliver:** a complete content specification that an authoring pipeline (human or AI) can
execute against, plus the acceptance rules that decide whether a produced item is fit to enter the pool.

**Scope:** the *content* — items, tasks, rubrics, levels, volume. Not the application, not the
scheduling, not the pricing. Two sibling briefs cover placement and practice as systems; this one
covers what goes inside them.

**Why this exists separately:** the last attempt at this content was produced without a specification.
It yielded 116 questions for five certificates and four skills, zero items at the hardest level, and
twelve of fifteen topics with no questions at all. The rebuild needs a spec that makes those numbers
impossible to produce again.

---

## 1. The product and its certificates

A multi-certificate English exam-practice application. This content will feed a **placement test** and
a **practice subsystem**, both being rebuilt. Five certificates:

| Certificate | Skills examined | Character |
|---|---|---|
| TOEIC Listening & Reading | Listening, Reading | Workplace and business English |
| IELTS Academic & General | Listening, Reading, Writing, Speaking | Academic and general; band 0–9 |
| VSTEP | Listening, Reading, Writing, Speaking | Vietnamese national test; CEFR-aligned |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading | Adaptive, two stages |
| TOEIC Speaking & Writing | Speaking, Writing | Productive skills only |

Note: **one shared pool serves all certificates.** A question is not authored "for TOEIC" — it is
authored at a CEFR level for a skill, and any certificate whose skills include that skill may use it.
Your specification must make this work: an item must be usable without knowing which certificate the
learner has chosen.

---

## 2. What the content must achieve

### 2.1 Placement must discriminate

Placement reports **one CEFR per skill plus an overall** (the overall is the weakest skill). To do that,
the pool must contain items at every level from A1 to C2, with enough density around A2–C1 — where
learners actually are — to separate adjacent bands.

**The failure to avoid:** the current placement has 24 items spread over three difficulty levels and no
items above level 3. It cannot tell B2 from C1, yet it reports a band that becomes the learner's
starting point for everything downstream.

Your spec must state **how many items at each level, per skill**, and derive it. The binding constraint
is that a placement sitting must not exceed **60 minutes total**, of which roughly half is the objective
skills.

### 2.2 Practice must have a topic to be weak in

Practice tracks mastery **per topic**, not per question. A topic with two questions cannot support a
mastery judgement, and a learner cannot be "weak in inference" if there are no inference questions.

Your spec must state **which topics exist, per skill**, and **how many items each needs** for the mastery
model to mean anything.

### 2.3 Both must be defensible

Every item carries a CEFR label. A wrong label mis-places every learner who meets that item, silently.
Your spec must define what makes a label defensible, and how a produced item is checked before it
enters the pool.

---

## 3. What exists now (the thing being replaced)

Measured from the demo database on 2026-11-03.

| Measure | Value |
|---|---|
| Questions, all certificates, all skills | 116 |
| Distribution over difficulty 1–5 | 4 / 62 / 43 / 7 / **0** |
| Topic categories | 15 |
| Categories with any question | **3** (2, 1, 1) |
| Sections declaring ≥ 30 questions | 8 |
| Sections with **zero** questions | 5 |
| Lessons | 13 |
| Lessons carrying a level | **none — no such column exists** |
| Exam-level gap | the IELTS mock declares 40 questions and holds 2 |

The pattern behind those numbers: content was produced to fill a demo, not to a specification. Your
brief exists to make that impossible to repeat.

---

## 4. Questions your specification must answer

### 4.1 The level model for content

- What exactly does "this item is B1" mean? Give criteria a human author and an AI author can both
  apply and a reviewer can check.
- Is level a single label per item, or does it depend on the skill? Is an A2 Listening item and an A2
  Reading item defined the same way?
- Where an item tests several things at once (a reading passage with a main-idea question and an
  inference question), does each question carry its own level, or the passage?
- How does CEFR relate to the certificates' own scales? State explicitly whether a crosswalk is needed
  and, if so, defend it — the product already calls TOEIC 785–940 "B2" internally, which is not CEFR B2.

### 4.2 Item volume, derived

- **Objective items** (Listening, Reading): how many per level per skill, to (a) support a 60-minute
  placement that separates adjacent bands, and (b) give practice enough unseen stock that a learner does
  not see repeats. Show the arithmetic for both.
- **Speaking tasks**: how many per level, and how many tasks per sitting.
- **Writing tasks**: how many per level, and how many tasks per sitting.
- **Lessons**: how many, at which levels, covering which topics.
- What is the *total* content requirement, and which part is the minimum viable first release?

### 4.3 Topic taxonomy

- What topics, per skill, make a mastery model meaningful? Give the list.
- How many items does each topic need before mastery can be judged?
- Are topics shared across certificates or per-certificate? A TOEIC learner and an IELTS learner both
  need "verb tense" — one topic or two?
- How does a topic relate to a CEFR level? Is "verb tense at B2" a topic, or is level a separate axis?

### 4.4 Item format per skill

For each skill and each question type, specify the full shape: stem, stimulus (passage, audio,
image), options, answer key, explanation, timing. Be concrete enough that an authoring tool can
validate a submitted item without asking a question.

Two shapes are harder than the rest and your spec must handle them explicitly:

- **Listening with audio.** One audio may serve several questions. What are the rules for audio length,
  number of questions per audio, and whether a question may depend on a timestamp inside the audio?
- **Reading with a shared passage.** One passage, several questions. How many questions per passage,
  and how does level attach when questions differ in difficulty?

### 4.5 Rubric specification for Speaking and Writing

The AI returns a score per criterion plus a comment, located errors, a priority fix and a rewrite — not
a band and not a percentage.

- Define the criteria per certificate and per skill, with the band descriptors at each level. The
  existing rubrics have criteria names and maximum scores but **most descriptors are empty**, which
  means the AI has been asked to score against a scale nobody wrote down.
- What is the reliability of one AI-scored task, and how many tasks are needed for a defensible
  per-skill level? State the claim you are making and its basis.
- How is a rubric designed so it discriminates between adjacent CEFR levels rather than compressing
  everything into the middle?

### 4.6 Authoring and acceptance

- Who or what authors content, and what does the pipeline look like end to end?
- What automatic checks must an item pass (structural, linguistic, answer-key, level-consistency)?
- What human review is required, and on what sample size?
- What makes an item **rejected**, and what happens to items already in the pool that fail a later check?
- How is an item retired without breaking a learner's history that references it?

### 4.7 Demo versus real content

The product needs a small demo set that exercises the real pipeline end-to-end with the real AI, and a
full production set.

- How does a demo set differ from a real set — smaller, same shape, or a fixed script?
- How is a demo item marked so it can never be mistaken for real content, and can be removed cleanly?

---

## 5. Deliverable format

A content specification with:

1. **Scope and non-goals.**
2. **The level model** — definitions and criteria, operational enough to apply.
3. **Topic taxonomy** — the list, per skill, with the reasoning for the grain.
4. **Item format specification**, per skill and question type, including the two hard shapes (shared
   audio, shared passage).
5. **Rubric specification** — criteria, band descriptors, and the reliability claim for a single task.
6. **Volume tables with arithmetic** — items per level per skill, tasks per level, lessons, totals, and
   the minimum viable first release.
7. **Authoring and acceptance rules** — the pipeline, the automatic checks, the human review, rejection
   and retirement.
8. **Open questions** with options and your recommendation.
9. **References** — CEFR descriptors, item-writing standards, inter-rater reliability literature. Cite
   specific sources; the implementing agent will check them.

Prefer machine-readable appendices (JSON or a table schema) for anything an authoring tool must consume.

---

## 6. What to be careful about

- **Do not design content for one certificate.** One shared pool serves all five, and two of them
  examine only two skills. If your specification names a certificate anywhere in the item rules, it is
  wrong.
- **Do not invent numbers.** Every volume figure must come with its arithmetic. The current pool is what
  happens when plausible numbers are chosen: 24 placement items over three levels, zero at the top.
- **A band is not a CEFR level.** The product labels TOEIC 785–940 as "B2". That is an internal label.
  If your design needs cross-certificate comparison, defend it or avoid needing it.
- **Answer keys are the highest-risk content in the system.** A wrong key mis-places every learner who
  meets that item, and nobody finds out. Treat key verification as a first-class requirement, not a
  checkbox.
- **Listening audio and reading passages are shared across questions.** Design the format around that,
  or you will be forced to duplicate passages and inflate the pool.
- **The AI authors and the AI grades.** If the same model writes an item and later grades answers to it,
  say what that means for quality control. It is a real risk, not a theoretical one.
- **Speaking and Writing are the hard half.** Multiple choice is a bank with a key. Productive skills
  are task design, rubric design, and a reliability claim you have to defend.
