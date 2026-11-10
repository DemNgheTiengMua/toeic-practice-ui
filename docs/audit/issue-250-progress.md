# #250 progress — overnight run

Branch `integ/cefr-rebuild`, HEAD `d1f6ab1`. Build 0 errors. Schema harness `ALL CHECKS PASSED`
after every commit.

## Closed

| # | What | Commit | Proof |
|---|---|---|---|
| 261 | A productive task on a pool paper can be sat | `52af0bc` | `PoolProductiveTaskTests` |
| 262 | Writing and speaking practice read the paper roster | `fb1dac9` | `PoolProductivePracticeTests` |
| 263 | The free drill draws from the pool | `d05eeec` + `17e7c55` | `PoolDrillCatalogTests` |
| 267 | The seed is generated, pool-native, publishes every exam | `3f72ae5` | `DemoSeedTests`, `PaperMigrationCountsTests` |
| 268 | The seed can feed a placement stage-one draw | `d1f6ab1` | `SeedFeedsPlacementTests` |

## Partly done

**#264** — the saved bank was already pool-native (four existing tests prove it). Two things remain,
and both are recorded on the ticket and in `#271`:

- `vw_UserCategoryAccuracy` has **six** readers, not the two the spec claimed. Moving them changes what
  a learner reads on the dashboard cards, the insight narrative and the weakness page. #250 puts
  learner-visible redesign out of scope, so it is filed as **#271**.
- `vw_UserSectionAccuracyAll` keeps its legacy branch behind a `shortcut:` comment naming its deletion
  condition (#269). It must stay until then: `PoolAndLegacyAccuracyAgreeTests` proves the two shapes
  agree by holding both in one database.

## Not started

**#265 (authoring has one door)** — the ticket's premise is **wrong**, and this is the important finding
of the night. It says "the legacy authoring API is exposed and called by no screen". True for
`CreateQuestionAsync` / `UpdateQuestionAsync` / `DeleteQuestionAsync` / `CreateGroupAsync`. But the
**Studio editor** (`/admin/exams/{id}/studio`) is a live admin screen and writes legacy through five
routes — autosave question, autosave group, reorder, create group, delete group — with its rail, cards
and up/down buttons all keyed by `QuestionId` / `GroupId`. So #265 is not "delete a dead API"; it is
"rebuild the Studio editor on the pool". That needs its own session, not a tail.

**#266 (importer writes one model)** — blocked on #265 by construction: the importer keeps its legacy
half only because a paper may still be assembled that way.

**#269 (drop the tables)** — blocked on #265 and #266, and on #271 for the category view.

**#270 (whole product green twice)** — the last gate; cannot start until #269.

## Four real defects found by running the seed, not by reading it

1. **Audio slices were being dropped.** A group's `AudioStartMs`/`AudioEndMs` were never copied to its
   stimulus, so all eight diagnostic audio slices arrived empty. A listening item with no slice plays the
   whole section track. Fixed in `docs/migrate-paper-to-pool.sql`; measured 8/8 after.
2. **Word floors were being dropped.** `MinWords`/`MaxWords`/`PrepSeconds`/`ResponseSeconds` were never
   copied to the item version, so all four Writing Extension tasks lost their floor and the grader's
   "at least N words" check silently stopped applying. Fixed; measured 4/4 after.
3. **A word ceiling with no floor** (Q110, `inline_completion`, `MaxWords = 1`) is refused by
   `CK_ItemVersions_WordRange`. It now carries a floor of 1 rather than losing its ceiling — losing it
   would let a one-word blank accept a paragraph. The CHECK was **not** weakened.
4. **A per-id shape guess in the drill.** `SavedQuestionServiceTests` caught it: item ids and question
   ids are two counters that **collide**, so on a shared database a legacy question id named an
   unrelated item and the drill wrote a pool-shaped answer for content nobody drew. The planner now
   returns the shape alongside the ids, from the one place that decided it (`17e7c55`).

## Two things I got wrong and corrected

- **I skipped graphify for most of the session.** `AGENTS.md` §2 makes it mandatory before the first
  file of a ticket. I ran it late, and it immediately caught a wrong claim in my own spec (the "two
  readers" of the category view are six). It is now run per ticket.
- **I ran a suite far wider than the change.** Recorded in `AGENTS.md` §6 as a rule with the exact
  commands, so the next session does not repeat it.

## What is left for a human

- **#265 is a UI rebuild**, not a deletion. Budget a session for it.
- The seed's demo learner now has **no learning path and no placement**. That is deliberate: a path is
  derived from a measurement, and the generated seed does not ship a measurement nobody took. A demo
  should sit a placement to generate one — which #268's stock now makes possible for the first time.
