# Six briefs, six sessions — prompts and attachments

Every brief is self-contained: an agent with **no repository access** produces a full SRS from it. Send
the **file contents**, never a path — the agent cannot read your filesystem.

| # | Brief | Session |
|---|---|---|
| 1 | `content-brief.md` | Content |
| 2 | `placement-brief.md` | Placement |
| 3 | `path-brief.md` | Learning path |
| 4 | `ui-brief.md` | Learner interface |
| 5 | `admin-brief.md` | Admin & authoring |
| 6 | `transition-brief.md` | Transition (surviving readers + sequence) |

## Two work products that are not briefs

- **`placement-pool-manifest-prompt.md`** — the prompt to hand an external agent so it returns a
  placement pool as an importable `.json`. Self-contained: it carries the manifest schema, the fourteen
  item types, the flag that makes an item drawable, and the volumes the draw needs.
- **`placement-question-count.md`** — how many questions a placement sitting asks, and which of those
  numbers are fixed and which are routed. Read this before quoting a total; the answer is a range, and
  the reason is the routing table.

---

## When the SRS documents come back

See **`WORKFLOW.md`** in this directory: save the six files, run the six checks before writing any spec,
resolve contradictions with the ownership table, then spec → ticket → build one wave at a time, ending with
one integration ticket that blocks on everything.

## Order

**1 → (2, 3, 5) → (4, 6)**

Content first, because it owns every volume number. Then placement, path and admin in parallel. Then the
UI and the transition, which both need real designs to work against.

If you must run everything at once, add the shared-pool line to prompts 2, 3 and 5 (given below), and
accept that you will reconcile three sets of assumptions yourself. Do not send 6 first — sequencing a
design that does not exist is fiction.

---

## Session 1 — Content

**Upload:** `content-brief.md`

```
You are producing a content specification for a multi-certificate English exam
practice product. You have no access to the repository — the attached brief is
complete and self-contained. Do not ask for code, files, or clarification about
the codebase.

Read the whole brief before writing. Then produce the specification in the
format its section 6 specifies — the full document, not a summary.

Three things matter most:

1. Section 5.2 is the section every other document depends on. Three sibling
   sessions (placement, path, admin) will take your volume tables as given and
   will not compute their own. Your tables must satisfy two different consumers
   with one answer: a 60-minute two-stage placement separating adjacent CEFR
   bands, and a practice system needing enough unseen stock per topic that a
   learner does not meet repeats. Show the arithmetic for both and show how they
   fit one pool.

2. Where you cannot justify a number, put it in open questions with the options
   and your recommendation instead of choosing one. The current system is what
   happens when plausible numbers are chosen: 116 items, zero at the hardest
   level, twelve of fifteen topics empty.

3. Section 5.5 asks for rubric band descriptors. Most existing criteria have only
   a name and a maximum score — no descriptor at any level. Writing those is not
   documentation, it is what makes AI grading possible at all.

Cite specific sources for any claim about CEFR levels, item writing or
reliability. The implementing agent will check them.

Return one document.
```

---

## Session 2 — Placement

**Upload:** `placement-brief.md` + the content SRS

**If the content SRS is not ready**, upload only `placement-brief.md` and replace the shared-pool
paragraph in the prompt with the alternative below.

```
You are producing an SRS for rebuilding the placement test of a multi-certificate
English exam practice product. You have no access to the repository — the
attached brief is complete and self-contained. Do not ask for code or files.

Read the whole brief before writing. Then produce the SRS in the format its
section 7 specifies — the full document, not a summary.

The brief carries 23 settled decisions. Build on them; do not relitigate them.
The ones that shape everything else: placement returns one CEFR per skill plus
an overall (the weakest skill, not an average); scoring happens on the CEFR axis
and never on a certificate's own band scale; the item pool is shared across all
certificates; and the existing sitting engine is reused rather than rebuilt.

The pool's size is not yours to decide — the attached content specification owns
how many items exist at each band per skill. Take its tables as given. What you
decide is how many of those items a placement uses and how the two stages split
them within a 60-minute limit. If that pool cannot support the paper you want,
name the gap: "this design needs 8 items per band and the pool specifies 5" is a
finding. Producing a second volume table is not.

Where you cannot justify a number, put it in open questions rather than choosing
one.

Section 4.2 lists eight requirements placement adds that the existing engine does
not have — resume after exit, resume after reload, offline, partial submission,
the two-stage transition, abandonment without a timer, AI grading in flight, and
whether one-way locks belong in a measurement instrument. Answer all eight with
behaviour, not implementation.

Cite specific sources for any claim about adaptive or multistage testing.

Return one document.
```

**Replace the shared-pool paragraph with this if content is not ready:**

```
The pool's size is not yours to decide. A sibling session is producing a content
specification that owns how many items exist at each band per skill. Do not
compute the pool's size or invent a volume table. State what the paper your
design needs from the pool — items per stage, per band, per skill — and flag any
gap. If you must assume a number to proceed, mark it clearly as an assumption.
```

---

## Session 3 — Learning path

**Upload:** `path-brief.md` + the content SRS + (if available) the placement SRS

```
You are producing an SRS for rebuilding the learning path of a multi-certificate
English exam practice product. You have no access to the repository — the
attached brief is complete and self-contained. Do not ask for code or files.

Read the whole brief before writing. Then produce the SRS in the format its
section 7 specifies — the full document, not a summary.

The brief carries 18 settled decisions. Build on them; do not relitigate them.
Three shape everything else: the path is a sliding four-week window rather than a
fixed block (the current one stops dead after four weeks and nothing creates a
new one); progress is shown two ways at once — this week's work and distance to
target — because one number cannot carry both; and a step closes by being done,
with mastery deciding its size rather than whether it closes.

Two boundaries:

1. The pool's size is not yours. The attached content specification owns how many
   items exist per topic per level. State what the path requires of that content
   — the minimum per topic, per skill, per step type — and name a gap when the
   pool cannot supply it. Do not produce a volume table.

2. Placement's result shape is not yours. A sibling session rebuilds placement to
   return one CEFR per skill plus an overall. Section 3.1's P3 settles that a path
   is one timeline with steps assigned to skills, but how a step's level is chosen
   when four skills have four levels is yours to design.

Where you cannot justify a number, put it in open questions rather than choosing
one. The current system's ten-item session and 75% threshold have no evidence
behind them; do not inherit them quietly.

If you lean on spaced-repetition research, say where it transfers to grammar and
reading comprehension and where it does not.

Return one document.
```

---

## Session 4 — Learner interface

**Upload:** `ui-brief.md` + placement SRS + path SRS

```
You are producing an SRS for the learner-facing interface of a multi-certificate
English exam practice product. You have no access to the repository — the
attached brief is complete and self-contained. Do not ask for code or files.

Read the whole brief before writing. Then produce the SRS in the format its
section 7 specifies — the full document, not a summary.

Two things gate everything else:

1. Section 4 is a constraint, not a suggestion. This product has a deliberate
   visual language — the exam-room signboard: flat painted fields, 2px ink
   keylines, 1px soft interior rules, zero border radius, zero shadow, zero
   gradient, zero blur, active state inverts to a solid ink block, monospace for
   every number, drawn icons beside a word and never instead of it. Do not
   propose rounded corners, soft shadows, a gradient hero, a card grid, or chat
   bubbles. If a requirement genuinely cannot be met in this system, say so and
   argue it — that is a real finding, not a workaround.

2. Section 5 carries 23 decisions already taken. Build on them. The dashboard is
   three blocks and stays three; an unmeasured skill is never shown as a low
   band; an item's CEFR level is never shown while the learner is answering;
   token streaming applies to AI insight prose only, because grading output is
   structured JSON.

The hardest screen in the product is the placement result: four CEFR levels plus
an overall, where the overall is the weakest skill, each with a 0-100 number that
is the position inside its band. Make that the centre of your design. Note that
crossing a band resets that number downward, so it can read as a regression when
it is progress.

The attached placement and path SRS define the behaviour. Design against them
rather than guessing; if the brief's section 2 and an SRS disagree, follow the
SRS and say so.

Section 6 lists 18 questions still open. Answer them.

If you consult a design-pattern or design-system generator, take its structural
and accessibility reasoning and refuse its palette, corner radii and typography.
Section 7.1 explains why, with the concrete example that came up while writing
the brief.

Return one document.
```

---

## Session 5 — Admin & authoring

**Upload:** `admin-brief.md` + the content SRS

```
You are producing an SRS for the staff-facing interface of a multi-certificate
English exam practice product: the tools that put content into the pool, review
it, and keep it correct. You have no access to the repository — the attached
brief is complete and self-contained. Do not ask for code or files.

Read the whole brief before writing. Then produce the SRS in the format its
section 5 specifies — the full document, not a summary.

Three constraints frame everything:

1. The publish gate and the structural lock are not obstacles to remove. They
   exist because three bypasses shipped unreviewed content, and because a
   structural edit can rewrite a score that was already recorded. Making the
   workflow smoother is the goal; making the gate optional is not.

2. This is a warehouse, not an exam editor. Items are shared across five
   certificates, and two of those examine only two skills. Any screen assuming
   "this item belongs to this exam" is wrong.

3. The item specification — level definitions, formats, rubric criteria and band
   descriptors — is owned by the attached content specification. Take it as
   given. Your job is the tool that applies and enforces it, not the standard
   itself.

The finding that makes this brief necessary: the current pool is 116 items with
zero at the hardest level, twelve of fifteen topic categories empty, and five
sections with no items at all. An authoring tool that enforced a specification
would have made most of that impossible. None existed.

Section 4.4 asks about rubric authoring. Most existing criteria have a name and a
maximum score and no band descriptor at any level, which means the AI has been
grading against a scale nobody wrote down. That screen does not exist today and
is the most valuable one in this brief.

Return one document.
```

---

## Session 6 — Transition

**Upload:** `transition-brief.md` + **all five SRS above**

```
You are producing an SRS for the transition of a multi-certificate English exam
practice product: the subsystems that are not being rebuilt but read a band whose
meaning is about to change, and the order in which the whole rebuild ships. You
have no access to the repository — the attached brief is complete and
self-contained, and the five attached SRS documents define what is being built.

Read the whole brief before writing. Then produce the SRS in the format its
section 5 specifies — the full document, not a summary.

The spine of this brief: a band is not a CEFR level. Today a band is a row in a
certificate's own table (TOEIC 10-990, IELTS 0-9, VSTEP). Placement is about to
produce CEFR. Two of five certificates have no CEFR-named band at all. Do not
reconcile the two with a lookup table unless you defend the table with sources —
this product has been wrong about that mapping before.

Three things make this hard and none of them can be skipped:

1. The surviving readers. Mock exam, exam review, AI insight, weakness analysis,
   goal feasibility, calibration, mentor booking, saved questions. Every one
   reads a band. Section 2.2 lists them and how.

2. The content does not exist yet. A four-skill placement cannot launch against
   116 items with zero at the hardest level. Your sequence must state how much
   content is needed before which step, or say explicitly that it assumes
   content will be ready.

3. There are no migrations. The schema is a single file applied to a fresh
   database. That makes "start clean" cheap and "preserve history" expensive.
   Say which you would choose for each table and why, rather than defaulting.

Section 4.7 asks you to find the other two-writer defects. One is confirmed: a
placement carrying free responses cannot be completed because two writers
disagree about one row. Look for the others in the readers described in 2.2 and
state the rule that prevents the class.

Do not sequence by what is easiest to build. Sequence by what keeps the product
consistent — the hard constraint is that a learner must never see two different
answers to "what is my level" on the same day.

Return one document.
```

---

## What to check when an SRS comes back

| Check | Why |
|---|---|
| Uses **CEFR** as the scoring axis, not a certificate's own band scale | Two certificates have no CEFR-named band |
| Treats **one shared pool** as given | Two of five certificates examine only two skills |
| **Cites a specific source** for any reliability, spacing or level claim | Unsourced numbers produced the current pool |
| **Flags a gap** rather than inventing a volume | Content owns those numbers |
| Avoids **naming a certificate** in a rule that should be generic | That mistake caused a defect here before |
| States **one writer** per derived fact | Two writers disagreeing is a confirmed defect |

## Who owns a disputed area

| Area | Owner |
|---|---|
| Item counts, levels, rubrics, formats | content |
| How placement uses items and what it returns | placement |
| How a path schedules and closes steps | path |
| What is shown and how it is worded | ui |
| Roles, upload, review, the publish gate | admin |
| What a band means outside the rebuild, and the order of operations | transition |

Anything outside those six is your decision, not the agents'.

## Evidence beside the briefs

- `placement-free-response-defect.md` — the confirmed two-writer defect, with the test that proves it.
- `data/` — four measured tables. Every number in the briefs comes from there.

## Not in any brief

Pricing and credit economics. Accessibility (out of scope for v1 by the owner's decision). Mobile
(desktop-first is a recorded trade-off). The demo set's contents.
