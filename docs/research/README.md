# Three briefs, three sessions

The placement test, the learning path and the learner interface are all being deleted and rebuilt.
Each gets one brief, self-contained, for one deep-research session that has **no repository access**.

## What to hand over

| File | Session | The SRS it should return |
|---|---|---|
| `placement-brief.md` | Placement test | The item pool, the sitting, the scoring, the result |
| `path-brief.md` | Learning path | The plan, its steps, practice content, mastery, progress, end states |
| `ui-brief.md` | Learner interface | Every learner screen, its states, and the interaction patterns behind them |

Each brief carries: the product and its certificates, the measured state of what is being replaced,
every decision already taken, the questions the SRS must answer, the deliverable format, the traps, and
(where relevant) the data appendix.

## Why the UI brief is separate

It could have been a section in each of the other two, but the hardest UI problem in this product is
**one screen that shows four skill levels at once**, and that screen belongs to neither placement nor
path — it is the junction. Splitting it across two briefs would have produced two half-designs and a
contradiction.

The UI brief also owns the things that must be consistent everywhere: how progress is shown, how states
are worded, how failures read.

## Order

**Placement and path can start in parallel.** The path brief asks what a path does when four skills have
four levels, and states that it cannot settle that alone; if the placement SRS exists when the path
session starts, hand it over too.

**Give the UI brief to its session last, or in parallel with both.** It is much more useful once the two
behaviour SRS documents exist, because it designs against behaviour rather than guesses. If it starts
first, tell it to treat §2 as the behaviour contract.

## The decisions all three share

| Shared decision | Why it matters |
|---|---|
| One shared content pool serving all certificates | Two of five examine only two skills |
| CEFR is the scoring axis, not each certificate's own scale | Two certificates have no CEFR-named band |
| A band is not a CEFR level | TOEIC 785–940 is labelled "B2" internally and is not CEFR B2 |
| Placement and practice are free | No credit ledger row, ever |
| The AI grades and narrates; it never chooses content or scores | No screen may imply otherwise |
| Do not invent numbers; derive or mark provisional | The current system is what invented numbers produce |
| Speaking and Writing are the hard half | The current product barely has them |
| One writer per fact | Two writers disagreeing about one answer is a confirmed defect |

If an SRS contradicts one of these, that is the first thing to challenge in it.

## The design system the UI brief is bound by

**"Bảng hiệu phòng thi"** — the exam-room signage. Flat painted fields, 2px ink keylines, 1px soft
interior rules, zero radius, zero shadow, zero gradient, zero blur. Active state inverts to a solid ink
block. Colour is always paired with a word or a number. Monospace for every number. Drawn SVG icons
beside a word, never instead of it. Desktop-first at 1440×900 by recorded choice.

This is deliberate and strict. Any UI SRS that proposes a soft-shadowed card grid is answering a
different product.

## Evidence kept alongside

`placement-free-response-defect.md` — a placement carrying free responses cannot be completed, confirmed
by execution. The concrete case behind "one writer per fact", which appears in all three briefs.

`data/` — the four measured tables. Every number in the briefs comes from here.

## What is deliberately not in any brief

- **Pricing and credit economics.** To be redesigned separately.
- **Accessibility.** Out of scope for v1 by the owner's decision; all three briefs note it as a gap.
- **Mobile.** Desktop-first is a recorded trade-off; no mobile layout is designed.
- **The demo content set's contents.** The briefs say how a demo differs from real content, not what it
  holds.
- **Admin and authoring screens.** Separate rebuild.
