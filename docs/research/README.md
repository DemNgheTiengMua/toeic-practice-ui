# Four briefs, four sessions — and how to send them

## The four files

| File | Session | The SRS it returns |
|---|---|---|
| `content-brief.md` | Content | Level model, topic taxonomy, item formats, rubrics, volume tables, acceptance rules |
| `placement-brief.md` | Placement | Item pool handling, the sitting, scoring, the result |
| `path-brief.md` | Learning path | The plan, its steps, practice selection, mastery, progress, end states |
| `ui-brief.md` | Learner interface | Every learner screen, its states, interaction patterns |

Each is self-contained: an agent with no repository access produces a full SRS from it.

## Why four and not three

Placement and path each need to know **how much content exists at each level**. If both compute it, they
produce two different answers for one pool — and the implementer is left holding two documents that
disagree.

So the content blueprint is a document of its own, and the other two **consume** it:

- `placement-brief` says: "Take the pool's size as given. What you decide is how many items a placement
  *uses*."
- `path-brief` says: "State what the path *requires* of the content. Name the gap if the pool cannot
  supply it. Do not invent a volume table."

That single change removes the only guaranteed contradiction in the set.

---

## How to send

There are two viable orders, and one that is wrong.

### Order A — content first, then the rest (safest)

1. **Send `content-brief.md` alone.** Wait for the SRS.
2. **Send it with `placement-brief.md` and `path-brief.md`**, one session each, adding: *"the content
   SRS is attached; take its volume tables as given rather than deriving your own."*
3. **Send `ui-brief.md` last**, with the placement and path SRS attached, adding: *"design against these
   behaviours."*

Costs one extra round trip. Buys the strongest guarantee: nobody invents numbers.

### Order B — all four at once (faster, slightly looser)

Send all four in parallel, and include this line in the placement and path prompts:

> A sibling session is producing the content specification. Do not compute how many items exist, or how
> many items per topic or band the pool should hold. State what your design *needs* from the pool and
> flag any gap. If you must assume a number to proceed, mark it clearly as an assumption.

You get four SRS documents in one pass. The risk: placement and path may still assume different pool
sizes, and you reconcile them yourself when they come back.

### Do not do this

**Do not send `placement-brief` and `path-brief` without either the content SRS or that line.** They
will each produce a volume table, and you will spend a day deciding which one the implementation follows.

### The UI session, either order

`ui-brief.md` is the most useful **after** the other SRS documents exist, because it designs against
behaviour rather than guesses. If it must run in parallel, tell it that §2 of its brief is the behaviour
contract — it is written to be sufficient on its own.

---

## What to put in each prompt

Beyond attaching the file, each session needs three things stated explicitly:

1. **"You have no access to the repository. The brief is complete; do not ask for code."** Otherwise a
   careful agent spends its first turn asking for files it cannot see.
2. **"Return an SRS in the format the brief specifies, not a summary."** Left alone, agents summarise.
3. **"Where you cannot justify a number, put it in open questions rather than choosing one."** This is
   the single most valuable instruction in the whole set — it is what the last content attempt lacked.

For placement and path, add the shared-pool line above.

For UI, add: **"The design system in §4 is a constraint. Do not propose rounded corners, shadows,
gradients, or a card grid; this product deliberately rejects them."** Design agents default to the
modern SaaS look unless told.

---

## What to expect back, and what to check first

Each SRS should contain a **screen inventory or domain model**, **numbered testable requirements**,
**business rules**, **open questions**, and **references**.

Check these five things before accepting any of them — each corresponds to a way the four documents can
still contradict each other:

| Check | Why |
|---|---|
| Does it use **CEFR** as the scoring axis, not a certificate's own band scale? | Two briefs say this explicitly; a third could quietly reintroduce band conversion |
| Does it treat **one shared pool** as given, rather than per-certificate content? | Two of five certificates examine only two skills |
| Does it **cite a specific source** for any reliability or spacing claim? | Unsourced numbers are what produced the current content model |
| Does it **flag a gap** rather than inventing a volume? | The content brief owns those numbers |
| Does it avoid **naming a certificate** in a rule that should be generic? | That exact mistake caused a defect in this product before |

If two SRS documents disagree, the briefs say which one owns the disputed area:

- **Item counts, levels, rubrics, formats** → content owns it.
- **How a placement uses items, and what it returns** → placement owns it.
- **How a path schedules and closes steps** → path owns it.
- **What is shown and how it is worded** → UI owns it.

Anything outside those four is an open question for you, not for the agents.

---

## What is deliberately in no brief

- **Pricing and credit economics.** Later.
- **Accessibility.** Out of scope for v1 by the owner's decision; each brief notes the gap.
- **Mobile.** Desktop-first at 1440×900 is a recorded trade-off.
- **Admin and authoring screens.** Separate rebuild.
- **The demo set's contents.** The briefs say how demo differs from real, not what it holds.

## Evidence kept beside the briefs

`placement-free-response-defect.md` — a placement carrying free responses cannot be completed, confirmed
by execution. It is the concrete case behind the "one writer per fact" rule that appears in three briefs.

`data/` — four measured tables. Every number in the briefs comes from there.
