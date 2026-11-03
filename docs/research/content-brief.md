# Research brief — Content blueprint and authoring standard

**Audience:** one external deep-research agent. **No repository access** — self-contained.

**What you deliver:** a content specification that an authoring pipeline can execute against, plus the
acceptance rules that decide whether a produced item is fit to enter the pool.

**This brief is the single owner of every content number in the rebuild.** Two sibling briefs —
placement and path — consume your volume tables. They must not invent their own. If you change a number
here, you change what both can promise.

**Scope:** the content. Not the application, not the scheduling, not the pricing.

---

## 1. The product and its certificates

A multi-certificate English exam-practice application. Placement measures where a learner is; the
learning path decides what they study; both are fed by one shared pool of content.

Five certificates. **They do not all have four skills:**

| Certificate | Skills examined | Character |
|---|---|---|
| TOEIC Listening & Reading | Listening, Reading, Speaking, Writing | Workplace and business English |
| IELTS Academic & General | Listening, Reading, Writing, Speaking | Academic and general; band 0–9 |
| VSTEP (Vietnam national test) | Listening, Reading, Writing, Speaking | CEFR-aligned national test |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading | Adaptive, two stages |
| TOEIC Speaking & Writing | Speaking, Writing | Productive skills only |

**One shared pool serves all certificates.** An item is not authored "for TOEIC" — it is authored at a
CEFR level for a skill, and any certificate whose skills include that skill may use it. Your
specification must make this work: an item must be usable without knowing which certificate the learner
chose.

**Domain vocabulary:** Certificate, **Skill** (one modality), **Section** (a part of a paper), **Topic** (a
sub-area within a skill), **Exam**, **Item** (a single question), **Task** (a free-response prompt),
**Free response** (spoken or written answer graded by AI), **Rubric criterion**, **Band descriptor**,
**CEFR level** (A1–C2).

---

## 2. What the content must achieve

### 2.1 Placement must discriminate between adjacent bands

Placement uses **two stages**: a fixed blueprint across all bands, then items only around the estimated
level. It reports one CEFR per skill plus an overall. **Hard limit: a placement sitting is 60 minutes
total**, roughly half objective.

To separate B2 from C1, the pool needs items at both, with enough density that a two-stage design can
concentrate on the boundary it finds.

**The failure to avoid:** the current placement has 24 items over three difficulty levels and none
above level 3. It cannot tell B2 from C1, yet it reports a band that becomes the learner's starting
point for everything downstream.

### 2.2 Practice must have a topic to be weak in

Practice tracks mastery **per topic**, not per item. A topic with two items cannot support a mastery
judgement, and a learner cannot be "weak in inference" if no inference items exist.

**The failure to avoid:** of 15 topic categories in the current system, **12 have no items at all**.

### 2.3 Both must be defensible

Every item carries a CEFR label. A wrong label mis-places or mis-teaches every learner who meets it,
silently. Your specification must define what makes a label defensible and how an item is checked before
it enters the pool.

---

## 3. What exists now (the thing being replaced)

Measured from the seeded demo database on 2026-11-03. Treat as the starting point being replaced, not a
target.

| Measure | Value |
|---|---|
| Items, all certificates, all skills | 116 |
| Distribution over difficulty 1–5 | 4 / 62 / 43 / 7 / **0** |
| Topic categories | 15 |
| Categories with any item | **3** (2, 1, 1) |
| Sections declaring ≥ 30 items | 8 |
| Sections with **zero** items | 5 |
| Lessons | 13 |
| Lessons carrying a level | **none — no such column exists** |
| Exam-level gap | the IELTS mock declares 40 items and holds 2 |
| Rubric criteria with a written band descriptor | **most have none** |

The pattern: content was produced to fill a demo, not to a specification. Your brief exists to make
that impossible to repeat.

The last row matters as much as the first. The AI has been asked to score free responses against
criteria whose score levels were never written down. That is not a scoring bug; it is a missing
specification.

---

## 4. Decisions already taken

Settled. Build on them.

| # | Decision | Reason |
|---|---|---|
| C1 | **CEFR is attached to each item.** A separate 1–5 difficulty scale is not used. | A 1–5 ↔ CEFR table is an invented mapping; that class of table already caused a defect here. |
| C2 | **Full CEFR range A1–C2**, with density concentrated at A2–C1. | Cutting C2 breaks the path to a C1 target; cutting A1 abandons the beginner. |
| C3 | **One pool, all certificates.** | One placement design for every certificate; avoids multiplying cost by five. |
| C4 | **Placement stage 2 needs at least 5 items per band** for objective skills. | 4-option items have a 25% guessing floor; at 4 items one guess moves the result 25%. |
| C5 | **Two tasks per productive skill** in a placement. | One is too thin to place a person; four makes the sitting unbearable. |
| C6 | **Content is generated by AI, validated automatically, then a 10% sample is human-reviewed.** No pilot study. | Reviewing every item is not realistic; the risk of an unmeasured label error is accepted. |
| C7 | **The pool is uploaded as JSON, one file per skill.** | Bulk load is the only practical way to fill hundreds of items. JSON represents shared passages and multi-item groups; CSV cannot without duplication. |
| C8 | **The demo set is a smaller run of the same shape, executed end-to-end with the real AI**, and marked in the data rather than implemented as separate code. | A fake-AI demo proves nothing about the thing most likely to break. Two code paths drift apart. |

---

## 5. What your specification must answer

### 5.1 The level model

1. **What exactly does "this item is B1" mean?** Give criteria a human author and an AI author can both
   apply, and a reviewer can check.
2. **Is level one label per item, or does it depend on the skill?** Is an A2 Listening item defined the
   same way as an A2 Reading item?
3. **When an item tests several things** (a passage with a main-idea question and an inference
   question), does each question carry its own level, or the passage?
4. **How does CEFR relate to a certificate's own scale?** State explicitly whether a crosswalk is needed
   and, if so, defend it. The product currently calls TOEIC 785–940 "B2" internally, which is not CEFR
   B2.

### 5.2 Volume, derived — this is the section every other brief depends on

5. **Objective items: how many per level per skill**, to satisfy *both* consumers:
   - placement needs enough that a two-stage design over a 60-minute limit can separate adjacent bands
     (C4);
   - practice needs enough unseen stock that a learner does not see repeats within a session.
   Show the arithmetic for both, and reconcile them into one table — **not two tables that disagree**.
6. **Speaking tasks: how many per level**, and how many in one placement.
7. **Writing tasks: how many per level**, and how many in one placement.
8. **Lessons: how many, at which levels, covering which topics.**
9. **The total, and the minimum viable first release.** State which part can ship first without breaking
   either consumer.

### 5.3 Topic taxonomy

10. **What topics, per skill, make a mastery model meaningful?** Give the list.
11. **How many items does each topic need** before mastery can be judged?
12. **Are topics shared across certificates or per-certificate?** A TOEIC learner and an IELTS learner
    both need "verb tense" — one topic or two?
13. **How does a topic relate to a CEFR level?** Is "verb tense at B2" a topic, or is level a separate
    axis?

### 5.4 Item format per skill

14. **For each skill and question type, the full shape**: stem, stimulus (passage, audio, image),
    options, answer key, explanation, timing. Concrete enough that an authoring tool can validate a
    submitted item without asking a question.
15. **Listening with shared audio.** One audio may serve several items. Rules for audio length, items
    per audio, and whether an item may depend on a timestamp inside the audio.
16. **Reading with a shared passage.** One passage, several items. How many items per passage, and how
    does level attach when the items differ in difficulty?

### 5.5 Rubrics for Speaking and Writing

17. **The criteria per certificate and per skill, with band descriptors at every level.** The existing
    rubrics have criteria names and maximum scores but **most descriptors are empty** — the AI has been
    scoring against a scale nobody wrote down.
18. **How is a rubric designed so it discriminates between adjacent CEFR levels** rather than
    compressing everything into the middle?
19. **What is the reliability of one AI-scored task**, and how many tasks are needed for a defensible
    per-skill level? State the claim you are making and its basis.

### 5.6 Authoring and acceptance

20. **Who or what authors content**, and the pipeline end to end.
21. **What automatic checks must an item pass** — structural, linguistic, answer-key, level-consistency.
22. **What human review is required**, and on what sample.
23. **What makes an item rejected**, and what happens to items already in the pool that fail a later
    check.
24. **How is an item retired** without breaking a learner's history that references it.

### 5.7 Demo versus real

25. **How does a demo set differ from a real set** — smaller, same shape, or a fixed script?
26. **How is a demo item marked** so it can never be mistaken for real content, and can be removed
    cleanly?

---

## 6. Deliverable format

A content specification with:

1. **Scope and non-goals.**
2. **The level model** — definitions and criteria, operational enough to apply.
3. **Topic taxonomy** — the list, per skill, with the reasoning for the grain.
4. **Item format specification**, per skill and question type, including the two hard shapes.
5. **Rubric specification** — criteria, band descriptors, and the reliability claim for one task.
6. **Volume tables with arithmetic** — one reconciled table serving both placement and practice, plus
   lessons, plus the minimum viable first release.
7. **Authoring and acceptance rules** — pipeline, automatic checks, human review, rejection, retirement.
8. **Open questions** with options and your recommendation.
9. **References** — CEFR descriptors, item-writing standards, inter-rater reliability literature. Cite
   specific sources; the implementing agent will check them.

Prefer machine-readable appendices (JSON schema, or a table definition) for anything an authoring tool
must consume.

---

## 7. What to be careful about

- **This brief owns the numbers.** The placement brief and the path brief both need item counts. They
  will take yours. Do not leave a gap they would fill with their own arithmetic — that is exactly how
  two documents end up promising different pools.
- **Do not design content for one certificate.** One shared pool, and two of five certificates examine
  only two skills. If your item rules name a certificate, they are wrong.
- **Do not invent numbers.** Every volume figure comes with its arithmetic. The current pool is what
  happens when plausible numbers are chosen: 116 items, zero at the top level, twelve of fifteen topics
  empty.
- **A band is not a CEFR level.** TOEIC 785–940 is labelled "B2" internally. If your design needs
  cross-certificate comparison, defend it or avoid needing it.
- **Answer keys are the highest-risk artefact.** A wrong key mis-places every learner who meets that
  item and nobody finds out. Key verification is a first-class requirement, not a checkbox.
- **Shared audio and shared passages.** Design the format around multi-item stimuli, or you force
  passage duplication and inflate the pool.
- **The AI authors and the AI grades.** If the same model writes an item and later grades answers to
  it, say what that means for quality control. It is a real risk.
- **Rubric descriptors are the single biggest gap.** Most criteria have a name and a maximum score and
  nothing else. Writing those descriptors is not documentation — it is what makes grading possible at
  all.
