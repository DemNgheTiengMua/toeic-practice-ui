# Two briefs, two sessions

The product's placement test and learning path are being deleted and rebuilt. Each gets one brief,
self-contained, for one deep-research session that has **no repository access**.

## What to hand over

| File | Session | The SRS it should return |
|---|---|---|
| `placement-brief.md` | Placement test | How the item pool is produced and stored, how a sitting runs, how it is scored, what result it returns |
| `path-brief.md` | Learning path | How a plan is produced, what its steps contain, how practice content is selected and generated, how progress is measured, how the plan ends |

Each brief is self-contained: product description, measured state of what is being replaced, every
decision already taken, the questions the SRS must answer, the deliverable format, the traps, and the
data appendix.

## What is inside which

**Placement** covers: creating the item pool, uploading it, taking a placement, scoring it, the result.
It reuses the existing sitting engine rather than building a second one, and says exactly what that
engine already provides and what placement must add.

**Path** covers everything between "the learner knows where they are" and "the learner sits the exam":
generating a plan, a step's lifecycle, what study material a step points at, how practice items are
selected, free drilling outside the plan, mastery and spacing, per-skill practice (including Speaking
and Writing), re-planning, progress reporting, and the four ways a plan ends. It also carries the
content requirement — how many items at what levels — because the path is what decides what content is
needed.

## Order

**Placement first, or both in parallel.** The path brief asks what a path should do when four skills
have four levels, and states that this is the one thing it cannot settle alone. If the placement SRS
exists when the path session starts, hand it over too.

**Nothing else blocks.** They are independent documents; neither needs the other's output to begin.

## The decisions the two briefs share

Both carry these, so the two SRS documents cannot contradict each other:

| Shared decision | Why it matters |
|---|---|
| One shared content pool serving all certificates | Two of five certificates examine only two skills |
| CEFR is the scoring axis, not each certificate's own scale | Two certificates have no CEFR-named band at all |
| A band is not a CEFR level | TOEIC 785–940 is labelled "B2" internally and is not CEFR B2 |
| Practice and placement are free | No credit ledger row, ever |
| The AI grades and narrates; it never chooses content or scores | |
| Do not invent numbers; derive or mark provisional | The current system is what invented numbers produce |
| Speaking and Writing are the hard half | The current product barely has them |
| One writer per fact | Two writers disagreeing about one answer is a confirmed defect |

If an SRS contradicts one of these, that is the first thing to challenge in it.

## Evidence kept alongside

`placement-free-response-defect.md` — a placement carrying free responses cannot be completed, confirmed
by execution. It is the concrete case behind "one writer per fact", which appears in both briefs.

`data/` — the four measured tables (band tables, exam difficulty spreads, section declared-vs-actual
counts, topic question counts). Every number in the briefs comes from here.

## What is deliberately not in either brief

- **Pricing and credit economics.** To be redesigned separately.
- **Accessibility.** Out of scope for v1 by the owner's decision; both briefs note it as a known gap.
- **The demo content set's contents.** Both briefs say how a demo should differ from real content, not
  what it should hold.
