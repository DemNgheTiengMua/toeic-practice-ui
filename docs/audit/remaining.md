## Where this work stands

**Read `bash scripts/preflight.sh` first. It derives all of this from live sources, so it cannot go
stale. This file is the part a script cannot infer: which tickets are done and WHY, and what decision the
next session must not re-litigate.**

---

## The 12 tickets

| # | State | Evidence / note |
|---|---|---|
| 261–268, 271, 273 | **closed** | before this work |
| **264** | **closed** | saved bank + section view pool-only; harness green; 19/19 |
| **269** | **closed** | 5/5 objects dropped; `src/` refs = 0; harness 59→60/60 |
| **270** | **closed** | suite 1943 pass / 1 skip / 0 fail; harness 60/60; 54/57 journeys |
| **272** | **closed** | the 14 remaining refs are all absence-proofs, allowlisted with reasons |
| **274** | **closed** | `AttemptStageBundles` + `AttemptItemResponses`; attempt-first `LoadSittingQuestionsAsync` |
| **275** | **closed (content-blocked)** | code complete; seed holds 2 items/band, needs 5–6. `SeedFeedsStageTwoTests` is `Skip`ped — unskip is the whole acceptance |
| **277** | **closed** | all 4 placement manifests import, 20 items, errorCount 0 |
| **276** | see below | the agent finished the wiring; NOT yet verified or committed |

Open and expected to stay open until content arrives: **#278** (seed has no per-skill measurement →
2 journeys red), **#260 / #250 / #181** (parents/epics).

---

## Decisions already made — do not re-litigate

1. **Exam content is not this repository's job.** The owner said so directly. Listening/Speaking import
   succeeds with placeholder media (a Vietnamese song, an unrelated JPG, two cloned `[placeholder]`
   questions). That is disclosed on #277 and is not work to redo.
2. **The seed deliberately ships no measurement.** `scripts/dump-seed.ps1` excludes
   `LearnerSkillStates`, `AttemptSkillScores`, `PlacementTests`, `LearnerPaths` with the reason "shipping a
   placement would assert a measurement nobody took", and `DemoSeedTests` asserts the omission. A fixture
   that needs a measurement must write one itself (`LearnerDashboardTests.ArrangeSkillMeasurementAsync` is
   the pattern). Do not "fix" the seed.
3. **`AttemptStageBundles` gives a sitting its own paper; the shared roster is never mutated.** #274 was
   the gate for #260. `PlacementService.StartPlacementAsync` still sits the static roster unless #276's
   wiring landed.
4. **A placement paper is a slice, not a form.** The publish audit judges it only on the sections it binds
   (`AdminExamService.Audit.cs`, the `"placement"` arm). Do not widen that back to all 20 TOEIC sections.

---

## What #276 needs, exactly

The agent reported: interfaces over both draw services, DI registration, `StartPlacementTestAsync` calls
stage one, stage two entered at the routing boundary, and a dead-service guard. **Unverified.** Verify with:

```
bash scripts/preflight.sh
git status --short                      # 5 modified + 8 new files are #276's
dotnet build                            # must be 0 errors
dotnet test tests/Services.Tests --filter "FullyQualifiedName~Placement" --nologo -- xUnit.MaxParallelThreads=1
bash docs/schema-tests/run-all.sh       # must stay 60/60
```

Then commit and close #276 with the counts. **The one rule that matters:** a failed draw must fall back to
the static paper, not refuse. 4 of 5 routes cannot draw on this seed, so a refusal would turn "every learner
gets a placement" into "most get a 500".

---

## The refund / live-dashboard wave (branch `feat/refunds-realtime-dashboard`)

Spec: `docs/specs/2026-11-12-refunds-realtime-dashboard-swagger-docker.md`. Tickets #279-#287.
Nine tickets, three independent roots: the reporting-period fix (#279), the refund schema (#280), and the
OpenAPI document (#285). Everything else hangs off those.

**Two decisions already made, do not re-litigate:**

1. **No `UpdatedAt` and no `rowversion` for live change detection.** ADR-0013. The revision is counts and
   sums over already-indexed columns. The limit is deliberate: an edit changing neither a count nor a sum
   is invisible, which is fine because the dashboard shows only counts and sums. Do not "improve" this
   into a timestamp column without reading the ADR — `Orders` already carries 9 indexes, 8 constraints and
   an `AFTER UPDATE` trigger.
2. **Revenue is net and may be negative.** ADR-0014. A money refund lands on its **settlement** date, not
   its request date. Credit refunds never touch revenue. Reporting days are cut at a fixed UTC+7, never
   the container's timezone.

**Facts worth not re-deriving:** `POST /api/auth/sign-in` is the login route (there is no `/login`), it is
rate-limited to 10/min outside Development, and the solution has **no CORS policy** at all (ADR-0002). The
API is **239 endpoint registrations** across 22 mappers; `LessonEndpoints.cs` dual-mounts 6 handlers under
two prefixes, so registrations exceed distinct route shapes.

---

## Traps this session hit, so the next one does not

- **Do not slice a C# file by line index with PowerShell.** It was done six times and wrong six times —
  once deleting three pool methods along with the three legacy ones. Read the file, use the `edit` tool.
- **Do not run the full suite while another agent edits `src/`.** The verdict is meaningless. Clean
  `LocalDB` first (`sqllocaldb stop/start`, drop leftover `EE*` databases) and give each run its own
  `TEST_DB_NAME`.
- **A test that owns a cell must own its database.** Five classes failed only in the full run for this
  reason. The pattern is in `PoolGateOneTests` / `PathStepCompletionTests`.
- **A screenshot count is not evidence.** The first pass produced two 5,950-byte blank PNGs for routes that
  do not exist (`/admin/items` has no GET; `/admin/imports` is a modal). Look at the images.
- **`media/` is untracked and large.** Three audio files and a JPG, ~136 MB, none of it real exam content.

---

## The one thing that will otherwise be forgotten

`scripts/dump-seed.ps1:249` still emits
`DELETE FROM [SavedQuestions] WHERE [QuestionId] IS NOT NULL;` — a column #269 dropped. **The seed
generator is broken against the current schema.** Fix that line before anyone regenerates the seed, or the
dump fails with `Msg 207 Invalid column name 'QuestionId'`.
