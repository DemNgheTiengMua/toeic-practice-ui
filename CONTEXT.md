# extra efficient

A multi-certificate English practice-and-mock-exam companion: learners diagnose themselves for free
and spend credits on mock exams, AI grading, and mentor sessions. Practice, placement, the weakness
report, and the learning path are free.

## Language

### Catalog

**Certificate**:
An English qualification the product prepares for (TOEIC, IELTS, VSTEP), defined entirely by data.
_Avoid_: Exam type, test

**Item lifecycle**:
The six states an item moves through, named by CNT-42: `draft`, `validated`, `active`,
`quarantined`, `retired`, `retired_defective`. `draft` means **not yet judged** by the four ingestion
gates; **`validated` means gates passed, awaiting audit** - the intermediate state, and the only one
promotion may come from; `active` is **in service**, the one state every pool selector reads.
Promotion is `validated -> active`, performed by `IngestionWorkbenchService.ClearForServiceAsync`
under `pool.content.review`, and recorded as an ADM-02 decision. `draft -> active` is not a named
transition and is not reachable: an item nobody judged cannot become servable.
_Avoid_: Published (for items), live (unqualified), approved (it names no state)

**Skill**:
One assessed ability within a certificate, with exactly one modality (listening, reading, speaking, writing).

**Section**:
A part of a skill with its own question count and format (e.g. TOEIC Part 2).
_Avoid_: Part (except in TOEIC-specific copy)

**Exam**:
An authored set of questions for one certificate, with a purpose: mock, placement, speaking practice, or writing practice.
_Avoid_: Test, paper

**Audio slicing**:
How a listening question group gets its audio. An exam section may have one **master track** uploaded in Exam Studio; when it does, every group in that section plays the master track, cut to the group's own start/end slice. A group plays its own audio file only when its section has no master track. A section with a master track and several groups needs a slice on every group, or each group would play the whole track.
_Avoid_: Clip (for a slice), part audio

### Learning activity

**Practice session**:
A free drill addressed either by a section (the legacy shape) or by a cell - one topic at one CEFR
level - which is the shape the content pool uses and the one a learning-path step points at. Graded
immediately for an objective item: a listening or reading answer is marked by its correct option, and
a writing answer against the question's own word floor and required keywords, never by AI, so a drill
never charges credits. A pool productive task has no key at all, so it is recorded ungraded rather
than scored. Speaking has no typed drill - a spoken answer cannot be typed - so a speaking section
links to its recorded, AI-graded speaking practice instead. Bounded to a FIFO retention window
(default: 20 sessions per certificate) so storage stays predictable while preserving enough
historical question data for weakness analysis and AI mentor insights.
_Avoid_: Practice (unqualified), quiz

**Attempt**:
One learner's sitting of an exam, with a server-owned deadline, of any purpose.
_Avoid_: Session, sitting

**Mock attempt**:
An attempt at a mock exam; starting it costs credits and it ends with a scaled score and a band.

**Placement attempt**:
An attempt at a placement exam whose result seeds a learning path (first sitting per certificate per learner is free; subsequent retakes charge credits by scope via `placement_retake`).

**Skill practice attempt**:
A paid speaking-practice or writing-practice attempt, AI-graded against a rubric, scored on that skill alone. The attempt claims no certificate total; the band its rubric lands in on that certificate's own bands is kept as an estimate on the grading.
_Avoid_: Speaking practice session, writing practice session

**Free response**:
A learner's spoken or written answer in an attempt, graded by AI rather than by a correct option.

### Scoring

**Raw score**:
The points a learner earns in one skill before conversion to the reporting scale.

**Scaled score**:
A skill's raw score converted onto its certificate's reporting scale. The certificate defines the default conversion; a single exam may carry its own conversion, which overrides that default for sittings of that exam.
_Avoid_: Converted score

**Band**:
A named level a learner reaches from their total scaled score (e.g. B2, TOEIC Blue 785–900). Bands are part of the certificate's editable definition and are what learning paths and recommendations target.
_Avoid_: Grade, level, tier, rank

### Money

**Credit**:
The in-app unit of payment, bought with real money and spent on paid features.

**Ledger**:
The append-only list of credit movements per learner; the **balance** is always its sum, never a stored number.
_Avoid_: Wallet balance column, credit counter

**Order**:
One purchase of a credit package through a payment gateway.
_Avoid_: Payment, transaction

**Gateway**:
The external service that takes the real money for an order (payOS today; a mock gateway for demos and tests).
_Avoid_: Payment provider, PSP

**Payment event**:
One message about an order received from or requested of its gateway (webhook, return redirect, status query), kept as evidence.
_Avoid_: Callback log, IPN

### AI

**Grading**:
AI scoring of one free response against its section's rubric criteria; paid, retryable for free.
_Avoid_: Evaluation, assessment

**Insight**:
An AI narrative about a learner's progress over their own history (after a session, an attempt, on a path, or on the dashboard), regenerated only when its inputs change.
_Avoid_: Analysis, AI feedback, summary

**Weakness analysis**:
Per-section and per-category accuracy computed by SQL views; the numbers an insight reads, never computed by AI.

### Guided learning

**Learning path**:
A structured sequence of modules for a certificate, progressing from diagnostic placement through topic mastery to full exam readiness.

**Path module**:
One themed step in a path (e.g., "Grammar Fundamentals"), containing multiple items.

**Path module item**:
One activity within a module: practice section, mock exam, AI task, mentor session, or lesson. Items unlock sequentially.

**Lesson**:
A grammar micro-learning unit in a module, comprising 6–10 swipeable cards (rules, pitfalls, examples, micro-checks) with in-card practice. Free; no credits charged.

**Lesson card**:
One swipeable screen in a lesson flow. Four types: rule (teach one grammar principle), pitfall (common mistake), example (correct usage), micro_check (1-question MCQ to verify understanding). Cards unlock in order; a micro-check must be answered correctly to advance.

**Lesson progress**:
Per-learner state for a lesson: how many cards viewed, which was last, when started/updated, how many completed. Drives "3 of 8" UI and resume-from-last behavior.
