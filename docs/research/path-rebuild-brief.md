# Research brief: rebuild the Learning Path, four skills

**For an external deep-research agent with no repository access.** Self-contained.

**Scope:** the learning path only — how a path is planned, what its steps are, and how it closes.
Placement is a separate brief (`placement-rebuild-brief.md`); practice is a third. Pricing is out.

**This is a rebuild, not an extension.** The owner's instruction is that the path subsystem is deleted
and rebuilt. Nothing is preserved because it exists; it is preserved only if it earns its place.

---

## 1. What the product is

A multi-certificate English exam-practice application. A learner picks a **certificate**, sets a
**goal** (starting band, target band, exam date, hours per week), takes a **placement test**, then
follows a **learning path**: a plan of study steps leading to the target band by the exam date.

Five certificates. **They do not all have four skills:**

| Certificate | Skills |
|---|---|
| TOEIC | Listening, Reading, Speaking, Writing |
| IELTS Academic & General | Listening, Reading, Writing, Speaking |
| VSTEP (Vietnam national) | Listening, Reading, Writing, Speaking |
| TOEIC Multi-Stage Adaptive (CAT) | Listening, Reading |
| TOEIC Speaking & Writing | Speaking, Writing |

**Domain vocabulary:** Certificate, Skill, Section (a part of a paper), Exam, Practice session (a free
drill), Lesson (authored teaching material), Attempt, Step (one activity in a path), Band, CEFR level.

**Invariants carried from the existing product:** practice and placement are free; one certificate's
content never leaks into another; an AI never chooses a step, a question, or a score — it writes
narrative only.

---

## 2. What the path currently does, and why it is being replaced

The path is generated, not templated, and that part works. What does not hold up:

### 2.1 It reads one band for the whole learner

The generator takes a **single** starting band — the placement result, else the latest mock exam, else
the goal — and pitches the entire plan at it. A learner who reads B2 and speaks A2 gets one number.

The placement rebuild returns **one CEFR per skill plus an overall**. A single band cannot express that,
so the path must decide how to consume four.

### 2.2 It selects study material by section, not by level

A step says "practise section P5". It carries no level, so it cannot be matched to "this learner needs
B1 grammar". The pool being built for the placement rebuild labels every question with a CEFR level —
and no part of the path can consume that label, because steps point at sections.

### 2.3 Lessons carry no level

The lesson table has **no band or level column at all**. Thirteen lessons exist; one is attached to a
topic category. So a path cannot say "study this because you are B1 and weak in verb tenses" — only
"study this".

### 2.4 Session size is a constant

Ten questions per practice session, at most four previously seen. That is a good default and a poor
constant: a learner at A2 targeting B2 in three weeks cannot be given a more intense plan, because the
unit of work cannot vary.

### 2.5 The middle of a path is unmeasured

A step closes when the learner finishes the activity. Whether the activity moved them toward the target
is not recorded in any actionable way. A path that cannot report "you are on step 9 of 20 and no closer
to B2" is a checklist, not a plan.

### 2.6 What the current data looks like

To calibrate how much is being replaced: 1 learner path, 6 steps, 4 path templates, 16 modules, 34
module items, and 5 topic rows that **no code reads**. That is the entire population of this subsystem
in the demo database. Rebuilding it destroys very little.

---

## 3. The decision this brief cannot make

**Does a path follow the overall CEFR, or does it become per-skill?**

The placement rebuild returns four CEFR values and one overall (the weakest). The path must choose.

**Both branches are specified below.** The implementing agent should plan for the branch the placement
brief settles on, and the SRS must say which branch it assumed, and state the consequence for the other.

---

### Branch A — one path, driven by the overall CEFR

The path is a single sequence pitched at the overall level (the weakest skill), and it prioritises steps
in the weakest skills.

- **What it costs:** the schema barely moves. A path still carries one band.
- **What it gives:** one simple plan. "You are A2 overall, here is your route to B2."
- **What it loses:** a learner who is strong in three skills and weak in one is treated as uniformly
  weak. Their plan wastes time on material they do not need, which is the fastest way to lose them.
- **What it demands:** the step selection must still be able to *target* the weak skill, so steps need a
  skill and a level even if the path itself carries one band.

### Branch B — one path, with a per-skill strand

The path is a single timeline whose steps are assigned to skills, each stride advanced at its own
level. A learner's Reading strand may sit at B2 while their Speaking strand sits at A2, and the plan
paces them independently toward the same target date.

- **What it costs:** the path becomes at least two levels deep — a path carries per-skill current
  levels, and each step carries the skill and level it serves. Step ordering becomes a scheduling
  problem, not a list.
- **What it gives:** the plan addresses what is actually weak without stalling the rest. This is the
  shape that matches the placement data the rebuild produces.
- **What it loses:** simplicity, and the ability to say "you are on step 9 of 20" — because four
  strands do not have a single position.
- **What it demands:** a rule for how a path finishes when one strand lags — does it extend beyond the
  target date, drop the lagging skill, or re-plan?

**Recommendation, if the placement brief leaves it open: Branch B.** A rebuild that reads four levels
from placement and then collapses them into one is discarding the measurement it just paid for. But
Branch B's cost is real and the choice is the owner's.

---

## 4. What a rebuilt path must answer

These are independent of the branch choice.

### 4.1 Where a step's content comes from

A step currently names a section or a lesson. Under a rebuild it must name **what to practise and at
what level**. Specify the shape: does a step point at a section, a lesson, a topic, a skill, or a level
band — or several of these?

Whatever you choose must work for a certificate with two skills and for one with four, and must not
name a certificate.

### 4.2 How a level is attached to study material

Questions will carry a CEFR label (placement rebuild). Lessons do not, today. Either lessons gain a
level, or a path learns to use a lesson without knowing its level, or lessons are replaced by something
that does have one. Pick one and justify it.

### 4.3 How much work per week

The goal stores hours per week; an existing feasibility calculation converts band distance into required
hours. A path must turn that into steps without either over-scheduling (the learner gives up) or
under-scheduling (the target date slips silently).

The existing rule was 3–5 steps per week at 25–45 minutes. That ceiling was derived from the ticket, not
from evidence. A rebuild should be able to defend its own numbers or say they are provisional.

### 4.4 What makes a step complete

Today a step closes when its activity finishes. Under a rebuild, should a step close on *completion* or
on *demonstrated improvement*? The difference is large: "practise P5" closes when you answer ten
questions, whether or not you got better.

If a step can only close on improvement, the path can no longer be finished in one sitting and the
learner can get stuck. If it closes on completion, the path cannot tell whether it is working.

State which, and what happens when a learner repeatedly fails to improve on a step.

### 4.5 Adaptation

A path planned for twelve weeks, with a learner who is progressing faster or slower than planned. Does
the path re-plan? How often? What stops it thrashing — re-planning after every session would make the
plan meaningless?

### 4.6 What happens when content is missing

A path wants a B2 Speaking step. The pool has no B2 Speaking items yet. Does the path refuse to
generate, substitute, or schedule it and fail later? The current system schedules it and fails later.

### 4.7 How a path ends

Four ways, and the rebuild must handle all: the target band is reached; the exam date arrives; the
learner abandons; the learner keeps failing to progress. What does each look like, and what does the
learner see?

---

## 5. Reuse

The path subsystem has two pieces worth reusing rather than rebuilding, and the SRS should say
explicitly whether it keeps them:

- **`PathStepCompletion`** — one writer that closes a step and unlocks the next, called from every place
  an activity finishes (practice session, mock exam, lesson). This exists because three separate writers
  drifted apart once. Whatever replaces the path needs the same single-writer property.
- **The feasibility calculation** — band distance in cumulative study hours, checked against the
  learner's declared hours and the exam date, returning feasible / stretch / unrealistic, and returning
  "cannot judge" when the certificate's bands carry no hours. This is already correct and should not be
  reimplemented.

Everything else in the subsystem — templates, modules, module items, topic rows — is candidate for
deletion.

---

## 6. Deliverable format

An SRS with: scope and non-goals; domain model including the step shape and the level attachment (a
schema sketch is welcome); numbered testable functional requirements; business rules for step
completion, adaptation, missing content and path end states; the scheduling arithmetic behind the
weekly work; open questions with a recommendation; and references to real sources for anything you claim
about learning schedules or spacing.

Say at the top **which branch (A or B) you assumed**, and what changes if it is the other.

---

## 7. What to be careful about

- **Do not design for one certificate.** One of five has two skills. A rule naming a certificate is a
  red flag — that exact mistake caused a defect in this product before.
- **Do not invent numbers.** If you cannot justify a steps-per-week figure, say it is provisional rather
  than deriving it from nothing. The current 3–5 was chosen by a ticket, not by evidence, and the
  rebuild should not inherit that quietly.
- **A band is not a CEFR level.** Inside this product, TOEIC 785–940 is labelled "B2" and is not CEFR B2.
  The placement rebuild scores on the CEFR axis; do not reintroduce cross-certificate conversion.
- **The path is not a curriculum.** It is a schedule over material that exists. If your design needs
  content that does not exist, that is a content requirement, not a path requirement — say so.
- **Ten questions a session is a default, not a law.** If intensity must vary, say how.
