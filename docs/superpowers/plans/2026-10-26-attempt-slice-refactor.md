# Mock and Placement Attempt Refactoring (#126)

**Goal:** Implement issue #126 of parent #123 without changing sitting behavior.
**Architecture:** Preserve the six layers, public signatures and root namespaces. Use cohesive partial implementations, focused type declarations and display-ready MVC models. Keep current branch main and preserve the uncommitted Milestone 1 baseline.
**Spec:** GitHub DemNgheTiengMua/toeic-practice-ui issues #123 and #126; docs/adr/0010-exam-shell-and-client-side-question-swapping.md.

## Constraints

No schema or trigger changes, code-first migrations, DbSet wrappers, dependencies or visual redesign. Browser requests continue through MVC with antiforgery protection. Embedded question JSON, persistent audio, save queues, deadlines and phase transitions retain their behavior. No arbitrary file-size target.

## Work and verification

- [x] Split attempt and placement domain models, service interfaces/exceptions, and AttemptEngine into cohesive responsibilities. Check public signatures against the pre-task snapshot; run existing SQL-backed attempt/mock/placement/preflight/state-machine tests.
- [x] Split attempt/placement DTOs, client interfaces and CompanionApiClient partials. Separate endpoint registration from DTO mappings without route/status changes. Build all consumers.
- [x] Extract mock sitting CSS/JS and focused markup partials. Prepare all initial shell data on its view model; preserve DOM IDs and initialization order. Adapt existing shell contracts to inspect composed sources without weakening assertions.
- [x] Extract placement sitting and preflight assets/partials, with view-model preparation outside Razor. Add targeted asset/configuration contracts.
- [x] Separate exam/placement display models and POST inputs. Move date formatting, result statistics, history chart data and review grouping/state preparation out of Razor. Add a scoped presentation convention guard and controller assertions for prepared data.
- [x] Run solution build, MVC suite, SQL-backed service tests, JavaScript syntax checks and browser smoke/zero-reload checks where available. Review the task delta against the saved dirty-worktree baseline before reporting.

Baseline: dotnet build --no-restore succeeds with 0 warnings/errors; MVC suite 502/502. Pre-task source snapshot lives outside the repository at %TEMP%/extraefficiency-126-baseline. No commits or pushes without checkpoint preference from user; no issue closure until verified.

## Verification record — 2026-10-26

Issue #126 is implemented in the working tree on `main`. The preexisting Milestone 1 changes remain intact; no commits, pushes, PRs or issue updates were made. No database schema or DataAccess configuration change was required for this slice after Milestone 1.

| Check | Result |
| --- | --- |
| `dotnet build --no-restore -v quiet` | Passed, 0 warnings and 0 errors; rerun after stopping the crawl apps |
| `dotnet test tests/WebMVC.Tests --no-build --no-restore -v minimal` | 529/529 passed; final output in `logs/issue126-mvc-tests.log` |
| Complete Services.Tests suite against real LocalDB/schema/triggers | 642/642 passed; `logs/issue126-services-tests.log` |
| Targeted attempt/mock/placement/preflight/state-machine service suites | 97/97 passed before and after refactoring |
| Browser audio/sitting regression selection | 6/6 passed; `logs/issue126-browser-tests.log` |
| Browser placement catalog/target/path and studio-start selection | 2/2 passed; `logs/issue126-placement-e2e.log` |
| `node --check` on task JavaScript files | 21 files passed |
| `git diff --check` | Passed; only line-ending notices |

Additional manual checks covered instructions/preflight, same-document question navigation with a live timer, completed-review filtering, and placement answer persistence/navigation after explicitly completing the linked attempt's preflight. The completed-review filter asset is separate from the active-sitting review-modal asset, with a regression guard. JSON data islands remain embedded for zero-reload navigation; executable scripts and style blocks are external. Physical microphone/audio hardware was not tested.

Public API and implementation comparisons against the pre-task snapshot found unchanged engine members, DTO/client declarations and route behavior. Standards and specification reviews found no remaining introduced findings. Task source/test changes relative to the initial dirty-worktree snapshot are recorded in `logs/issue126-changes.json`; comparing solely against Git HEAD would also include Milestone 1.

### Placement-start defect — resolved after the refactor

Before the follow-up fix, a fresh placement attempt whose exam required preflight redirected directly to its placement sitting. `AttemptEngine` rejects the unverified attempt with `preflight_required`; the placement sitting endpoint does not handle this code and the request returns HTTP 500. MVC redirects to the placement catalog with an error.

The complete failure chain was compared against the pre-task snapshot and is unchanged:

- `src/WebMVC/Controllers/PlacementController.cs:42`: Start redirects directly to Take.
- `src/Services/PlacementService.cs:160`: placement creates its linked attempt through the engine.
- `src/Services/Attempts/AttemptEngine.Lifecycle.cs:29`: audio/microphone requirements determine initial preflight verification.
- `src/Services/Attempts/AttemptEngine.Sitting.cs:52` and `AttemptEngine.Preflight.cs:111`: sitting retrieval requires verified preflight.
- `src/Services/PlacementService.cs:222` and `:21`: the engine exception is translated while retaining its code.
- `src/WebAPI/Endpoints/PlacementEndpoints.Sitting.cs:24`: only not-found/not-active cases are handled.

The user requested this defect be fixed before continuing. Placement sitting now translates `preflight_required` to HTTP 400 and MVC resolves the owned linked attempt, then redirects to the existing preflight screen with a typed placement destination. Verification and retry retain that destination, so the learner returns to placement rather than the mock sitting. The normal verified sitting path makes no extra preflight lookup. No schema, grading, credit, or authentication behavior was relaxed.

Regression tests first failed for missing audio/microphone handoffs. Final MVC suite: 534/534; SQL-backed service suite: 644/644, including placement preflight, ownership, and zero ledger writes. The solution test run skipped 37 browser tests while apps were stopped; the new focused browser regression was run separately against `scripts/crawl-env.sh fresh` in headed mode and passed. It covers fresh start, direct resume, verification, return to placement, and the free ledger rule. Manual visible-browser checks also saved an answer and navigated to question 2. Verification outputs: `logs/placement-preflight-tests.log` and `logs/placement-preflight-browser.log`.

### Knowledge graph and environment

The graph was refreshed after the refactor: 12,337 nodes and 37,055 edges, with no dangling or missing endpoints. SQL extraction remains limited by the existing missing `tree_sitter_sql` parser; three self-loops are reported. Diagnostics are in `logs/issue126-graph-diagnostics.json`.

The disposable `ExtraEfficiencyCrawl` environment uses fake AI/mock payment. After the placement fix, its API and MVC are left running so the user can inspect the visible browser. The everyday `ExtraEfficiency` database was not modified by browser verification.

## Publishing verification — 2026-10-26

The user approved publishing two scoped commits on `main`: the #124/#125 prerequisites, followed by #126 and the placement preflight fix. The prerequisite commit was checked in a temporary copy containing only staged files: solution build passed with zero warnings/errors and 454 MVC tests passed.

The final staged-only tree excludes unrelated local test additions, the home-page copy edit, scratch documents and generated graph changes. Its solution build passed with zero warnings/errors, 487 MVC tests passed, and the headed placement start/resume/preflight regression passed. All 21 extracted JavaScript files pass syntax checks, all 27 EF trigger declarations remain, and the staged diff passes whitespace checks. All 627 SQL-backed service tests passed; publication output is recorded in `logs/publish-services-tests.log`.

Earlier suite totals in this record include unrelated local test additions that remain outside the published commits. The published acceptance checks use the staged-only tree to avoid depending on those files.
