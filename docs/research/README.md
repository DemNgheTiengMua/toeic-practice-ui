# Four research briefs, four sessions

Each brief is self-contained: an agent with no repository access can produce a full SRS from it. They
are deliberately separate so each session can go deep on one area without carrying the others.

## What to give each session

| Give this file | For | The SRS it should return |
|---|---|---|
| `placement-rebuild-brief.md` | Placement test as a system | Placement SRS: level model, two-stage adaptive, per-skill results, engine reuse, edge cases |
| `practice-rebuild-brief.md` | Practice subsystem as a system | Practice SRS: selection, mastery, spacing, per-skill drills, intensity |
| `path-rebuild-brief.md` | Learning path | Path SRS: step shape, level attachment, scheduling, adaptation, end states |
| `content-blueprint-brief.md` | The content itself | Content spec: level definitions, topic taxonomy, item formats, rubrics, volume tables, acceptance rules |

## Order matters

**Content first, or at least in parallel with placement.** The placement brief asks for item counts
"derived, not asserted" — and the derivation depends on the level definitions and item format that the
content brief produces. Give both to their sessions at the same time; if the placement session finishes
first, its item-count section will be provisional until the content spec lands.

**Placement before path.** The path brief specifies both branches (a path follows the overall CEFR, or
it carries a per-skill strand) precisely because this is not settled until placement answers it. The
path session should read the placement SRS if it exists.

**Practice can run independently.** It references both, but its questions stand on their own.

## What is deliberately not in any brief

- **Pricing and credit economics.** The owner's instruction: redesign later.
- **The demo content set.** Each brief asks how a demo set should differ; none specifies its contents.
- **The 51 stale documents under `docs/`.** Real maintenance, unrelated to the rebuild.

## What the briefs agree on, so the four SRS documents do not contradict each other

| Shared decision | Where it appears |
|---|---|
| One shared content pool for all certificates | All four |
| CEFR is the scoring axis (not each certificate's own scale) | Placement, path, content |
| A band is not a CEFR level | Placement, path, content |
| Practice and placement are free | Placement, practice |
| The AI grades and narrates; it never chooses content or scores | All four |
| Do not invent numbers; derive or mark provisional | All four |
| Speaking and Writing are the hard half | All four |
| One writer per fact (the two-writer defect) | Placement, practice, path |

If an SRS contradicts one of these, that is the first thing to challenge in it.

## Known defects to hand over as evidence

`placement-free-response-defect.md` — a placement carrying free responses cannot be completed, confirmed
by execution. It is the concrete example of why "one writer per fact" is in three of the briefs.

`path-brief-audit.md` — the evidence behind the path brief's two-branch structure.

`placement-practice-rebuild-brief.md` — the earlier combined brief. Superseded for placement; its
practice material and its content measurements are folded into the two documents above. Keep it only as
the record of what was measured before the rebuild.
