# #250 progress

Branch `integ/cefr-rebuild`, HEAD `799101a`. Build 0 errors. Schema harness `ALL CHECKS PASSED`.

## Closed

| # | What | Commit | Proof |
|---|---|---|---|
| 261 | A productive task on a pool paper can be sat | `52af0bc` | `PoolProductiveTaskTests` |
| 262 | Writing and speaking practice read the paper roster | `fb1dac9` | `PoolProductivePracticeTests` |
| 263 | The free drill draws from the pool | `d05eeec` + `17e7c55` | `PoolDrillCatalogTests` |
| 265 | Authoring has one door — **the Studio editor moved onto the pool** | `fc9b905` + `0c50406` | `StudioPoolEditorTests`, `PoolAuthoringRoutesTests` |
| 266 | The importer writes one model and refuses what the pool cannot express | `e1c26e7` | 116/116 import + admin |
| 267 | The seed is generated, pool-native, publishes every exam | `3f72ae5` | `DemoSeedTests`, `PaperMigrationCountsTests` |
| 268 | The seed can feed a placement stage-one draw | `d1f6ab1` | `SeedFeedsPlacementTests` |

## Partly done

**#264** — the saved bank was already pool-native. The category view's six readers move in **#271**, filed because it changes what a learner reads on three screens.

**#269** — the reads that were *decisions* now use the roster (`799101a`): certificate change, purpose change, audio preflight, admin item count. Three of those were wrong in ways no test covered — the worst being that a pool listening paper never required an audio check. What remains is dropping the tables, which needs 118 test call sites rewritten onto the pool door first. Detailed on the ticket.

## The four defects the pool migration was silently causing

Found by loading the seed, not by reading the generator:

1. **Audio slices dropped.** A group's `AudioStartMs`/`AudioEndMs` were never copied to its stimulus — all eight diagnostic slices arrived empty.
2. **Word floors dropped.** `MinWords`/`MaxWords`/`PrepSeconds`/`ResponseSeconds` were never copied to the item version — all four Writing Extension tasks lost their floor and the grader's check stopped applying.
3. **A word ceiling with no floor** needed a floor of 1, not a weakened CHECK.
4. **The drill guessed each drawn id's shape** by "does a pool item carry this id" — the two counters collide, so it wrote pool-shaped answers for content nobody drew.

Plus two more found by making the legacy write disappear in #266: `PaperPositionsAsync` read `Questions` to rank incoming questions, so every imported item was written and **none bound** (a silent zero); and every import request carried key `0`, which threw as soon as a batch had two questions in one group.

## What the next session should do

1. **#269 step 1** — rewrite the 118 test call sites onto `IAdminItemService` + `BindItemAsync`. Biggest files: `AdminExamServiceTests` (60), `ExamRebuildFullSystemE2ETests` (13), `AuthoringLevelTests` (10), `AdminExamPublishGateTests` (10).
2. Then #269 steps 2–5 (delete the methods, drop the tables, the absence test, the `DataDisposition` entries, the `grep`).
3. Then **#270** — whole suite twice, schema harness, browser journeys on a fresh crawl env.
4. **#271** — the category view's six readers.

## Process corrections recorded

- `AGENTS.md` §6 now states the run discipline: filtered tests during work, the affected set when a change fans out, the whole suite twice only at the end.
- graphify was skipped for most of the first half. When finally run it immediately caught a wrong claim in my own spec (the category view has six readers, not two). It is run per ticket now.

## A demo-owner note

The seed's demo learner has **no learning path and no placement**. Deliberate: a path is derived from a measurement, and the generated seed does not ship a measurement nobody took. Sitting a placement now generates one — which #268's stock makes possible for the first time.
