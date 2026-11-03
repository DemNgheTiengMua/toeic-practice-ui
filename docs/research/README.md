# Seven briefs, seven sessions — and how to send them

## The briefs

| # | File | Session | The SRS it returns |
|---|---|---|---|
| 1 | `content-brief.md` | Content | Level model, topic taxonomy, item formats, rubrics, volume tables, acceptance rules |
| 2 | `placement-brief.md` | Placement | The pool in use, the sitting, scoring, the result |
| 3 | `path-brief.md` | Learning path | The plan, its steps, practice selection, mastery, progress, end states |
| 4 | `ui-brief.md` | Learner interface | Every learner screen, its states, interaction patterns |
| 5 | `admin-brief.md` | Admin & authoring | Roles, upload and review, item authoring, rubrics, the publish gate |
| 6 | `survivors-brief.md` | What survives | Mock exam, insight, weakness, calibration, goal — and reconciling two band meanings |
| 7 | `rollout-brief.md` | Sequencing | Order of operations, data disposition, content readiness gates, verification |

Each is self-contained: an agent with no repository access produces a full SRS from it.

---

## Why seven

The first four cover the rebuild itself. The last three exist because of three gaps that would otherwise
be nobody's job:

- **Someone must author the content.** All four rebuild briefs assume items exist. Without an authoring
  brief, no session owns the tool that makes them exist. Brief 5.
- **Not everything is being rebuilt.** The mock exam, AI insight, weakness analysis, calibration and goal
  feasibility all read a band, and a band is about to mean two different things. Brief 6.
- **Nothing can ship all at once.** The unit of meaning changes underneath every screen. Brief 7.

---

## Why content owns the numbers

Placement and path each need to know **how much content exists at each level**. If both compute it, they
produce two different answers for one pool — and the implementer holds two documents that disagree.

So:

- `content-brief` **owns** every volume number.
- `placement-brief` says: take the pool's size as given; what you decide is how many items a placement
  *uses*.
- `path-brief` says: state what the path *requires* of the content and name a gap; do not invent a table.

That removes the only guaranteed contradiction in the set.

---

## Order

### Recommended — content first

1. **Send brief 1 alone.** Wait for the SRS.
2. **Send briefs 2, 3, 5** in parallel, each with the content SRS attached and the line
   *"take its volume tables as given"*.
3. **Send briefs 4 and 6** next, with the placement and path SRS attached.
4. **Send brief 7 last**, with all six SRS attached.

Costs one extra round trip. Buys the strongest guarantee: nobody invents numbers, and the sequencing plan
is written against real designs rather than guesses.

### Faster — all seven at once

Send everything in parallel, and include this line in briefs 2, 3 and 5:

> A sibling session is producing the content specification. Do not compute how many items exist, or how
> many per topic or band the pool should hold. State what your design needs and flag any gap. If you must
> assume a number, mark it clearly as an assumption.

You get seven SRS in one pass and reconcile the assumptions yourself.

### Do not do this

**Do not send placement or path without either the content SRS or that line.** They will each produce a
volume table.

**Do not send brief 7 before the others.** Sequencing a design that does not exist yet is fiction.

---

## What to put in every prompt

1. *"You have no access to the repository. The brief is complete; do not ask for code."*
2. *"Return an SRS in the format the brief specifies, not a summary."*
3. *"Where you cannot justify a number, put it in open questions rather than choosing one."*

That third line is the single most valuable instruction in the set. The current content — 116 items, zero
at the hardest level, twelve empty topics — is what happens without it.

Area-specific additions:

| Brief | Add |
|---|---|
| 2, 3 | The shared-pool line above |
| 4 | *"Section 4 is a constraint: no rounded corners, shadows, gradients, or card grids. This product deliberately rejects them."* |
| 5 | *"The publish gate and structural lock are not obstacles to remove; they exist because three bypasses shipped unreviewed content."* |
| 6 | *"A band is not a CEFR level. Do not reconcile them with a lookup table unless you defend it with sources."* |
| 7 | *"Content is the long pole. If your sequence assumes content will be ready, say so as an assumption."* |

Design agents default to the modern SaaS look and content agents default to plausible numbers unless
told otherwise.

---

## What to check when an SRS comes back

| Check | Why |
|---|---|
| Uses **CEFR** as the scoring axis, not a certificate's own band scale | Two certificates have no CEFR-named band at all |
| Treats **one shared pool** as given | Two of five certificates examine only two skills |
| **Cites a specific source** for any reliability, spacing or level claim | Unsourced numbers produced the current pool |
| **Flags a gap** rather than inventing a volume | The content brief owns those numbers |
| Avoids **naming a certificate** in a rule that should be generic | That mistake caused a defect here before |
| States **one writer** per derived fact | Two writers disagreeing is a confirmed defect |

## Who owns what when two SRS disagree

| Disputed area | Owner |
|---|---|
| Item counts, levels, rubrics, formats | content |
| How a placement uses items, and what it returns | placement |
| How a path schedules and closes steps | path |
| What is shown and how it is worded | ui |
| Roles, upload, review, the publish gate | admin |
| What a band means outside the rebuild | survivors |
| The order of operations and data disposition | rollout |

Anything outside those seven is your decision, not the agents'.

---

## Evidence kept beside the briefs

- `placement-free-response-defect.md` — a placement carrying free responses cannot be completed, confirmed
  by execution. The concrete case behind "one writer per fact", which appears in five briefs.
- `data/` — four measured tables. Every number in the briefs comes from there.

## Not in any brief

Pricing and credit economics. Accessibility (out of scope for v1 by owner's decision). Mobile
(desktop-first is a recorded trade-off). The demo set's contents (the briefs say how demo differs from
real, not what it holds).
