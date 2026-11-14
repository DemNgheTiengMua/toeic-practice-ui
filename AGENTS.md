# AGENTS.md

Guidance and instructions for AI coding assistants (Antigravity/AGY, Claude Code, and autonomous agents) working in this repository.

---

## 0. Start here — one command, every session

**A compacted session loses its working memory but keeps the repository, so anything phrased as
"remember to…" eventually fails. The defence is not memory, it is derivation.** Run this before reading a
file, running a test, or trusting anything you think you know about the current state:

```sh
bash scripts/preflight.sh
```

It reads the live state every time — branch, HEAD, dirty files, the **open tickets**, the last twelve
commits, and `docs/audit/remaining.md` — so it cannot go stale and cannot be forgotten. `remaining.md`
carries the part a script cannot infer: which tickets are done and *why*, the decisions already made and
not to be re-litigated, and the traps the previous session hit.

If `preflight.sh` and your memory disagree, the script is right.

## 1. Core Architecture & Layering

The backend is a .NET 10 solution (`ExtraEfficiency.slnx`) structured in strict layers:
`BusinessObjects` (domain models) ← `DataAccess` (EF Core, database-first) ← `Services` (use cases) ← `Contracts` (wire DTOs & companion API client) — consumed by:
- `WebAPI` (port 5001, minimal API, dual `Authorization: Bearer` + `HttpOnly` cookie JWT auth per `ADR-0015`)
- `src/web` (port 5000, React 19 + HeroUI v3 `@heroui/react` + `@heroui/styles` + Tailwind CSS v4 SPA, replacing the legacy `src/WebMVC` Razor project slice by slice)

### Binding Architectural Invariants
1. **Database-First EF Core (`docs/adr/0001-database-first-ef-core.md`)**:
   - `docs/database-schema.sql` is the single source of truth for the database schema.
   - Any schema changes must be edited in `docs/database-schema.sql` first, validated with `docs/schema-tests/run-all.sh`, and re-scaffolded.
   - `run-all.sh` runs **every** test on its own fresh database and reports one verdict per file; it does not stop at the first failure, and exits non-zero if any check failed. Read the summary at the end for the failing set.
   - A fixture `INSERT ... SELECT` must match a row. The harness injects `ExpectFixtureRows` after every line-leading such statement in every domain file (`docs/schema-tests/fixture-guard.awk`), so a fixture matching zero rows aborts naming the file and line instead of letting a test's own `THROW 'EXPECT_REJECT FAILED'` report a constraint defect that never happened. `INSERT ... VALUES` needs no guard, and `docs/seed-demo.sql` runs unguarded by design.
   - Every table with an SQL trigger **must** declare `HasTrigger(...)` in EF Core model configuration to prevent `OUTPUT`-clause save failures.
   - Views are mapped as keyless, read-only entities. Derived values (balances, ratings, aggregates) are never stored or computed by application code.
2. **React + HeroUI v3 SPA & Dual Cookie/Bearer JWT (`docs/adr/0015-react-heroui-spa-and-cookie-jwt-api.md`, superseding `ADR-0002`)**:
   - `WebAPI` accepts JWTs from either `Authorization: Bearer <token>` (for automated tests, Swagger/Scalar, and companion clients) or the `HttpOnly; SameSite=Lax` cookie `ee_access_token` (for the React SPA).
   - Browser scripts never read or store raw JWT strings in `localStorage` or `sessionStorage`.
   - Same-origin `/api/*` requests from `src/web` are reverse-proxied to `WebAPI` (`http://localhost:5001`), keeping CORS closed.
3. **Credit Wallet & Ledger (`PRODUCT.md`, `CONTEXT.md`)**:
   - Credit balance is always `SUM(Amount)` over the append-only `CreditTransactions` ledger.
   - There is no stored wallet balance column.
   - Invariant: "Block before you charge" — check all preconditions before creating a debit transaction.
   - Practice, weakness analysis, and the first placement test per certificate per learner are free and never write a ledger row; subsequent placement retakes charge configured credits by scope (`placement_retake`).
4. **Certificate Isolation (`PRODUCT.md`)**:
   - Composite foreign keys carry `CertId` down through exams, sections, attempts, and learning paths.
   - One certificate's content, scoring, or rubric can never leak into another.
5. **Practice Session Retention & Logging Bound (`CONTEXT.md`, `PRODUCT.md`)**:
   - Practice drilling generates free diagnostic data (`PracticeSessions` and `PracticeAnswers`).
   - To keep storage bounded in the long run while retaining enough data for AI mentor insights and weakness analysis (`vw_UserSectionAccuracyAll`, `vw_UserTopicAccuracy`), standalone sessions are capped per certificate per learner (default: 20 sessions).
   - Sessions exceeding the limit, along with their answer logs, hint records, and attached insights/recommendations, are automatically pruned FIFO.
   - Learning-path practice sessions (`LearnerPathStepId IS NOT NULL`) are never pruned.
   - Unanswered in-progress sessions are cleaned up immediately when starting a new session; partially answered ones are marked `abandoned`.
6. **Free-Response AI Grading Seam (`docs/adr/0008-ai-grading-processor-seam.md`)**:
   - `IAiGradingProcessor` is the sole owner of asynchronous AI grading queue processing (`IX_AiGradings_Queue`).
   - `WritingPracticeService` and `SpeakingPracticeService` orchestrate learner attempt lifecycles (start, save draft/audio, submit, review) and do not poll or process queues.
   - `SpeakingPracticeService` and `WritingPracticeService` are completely decoupled; neither service references or depends on the other.
   - The queue worker `AiGradingBackgroundService` resolves `IAiGradingProcessor` directly.
7. **Exam Sitting Lifecycle & Score Scaling Seam (`AttemptEngine`)**:
   - `IAttemptEngine` is the sole owner of exam attempt sitting mechanics: countdown timer expiration, auto-submission, answer option validation, two-step score scaling (`ScoreScales`), and CEFR band matching (`ScoreBands`).
   - `MockExamService` and `PlacementService` act as thin domain adapters over `IAttemptEngine`, maintaining domain-specific rules (e.g., mock exam credit deduction and `placement_retake` credit deduction on placement retakes via `ICreditLedger` while first placement per certificate stays free, and placement target band tracking) without duplicating sitting state machines.
8. **Streamlined Direct Ownership (ADR-0017, superseding ADR-0012)**:
   - The complex `DerivedFactOwners` lookup table, single-writer triggers (`trg_PlacementTests_SingleWriter`, etc.), and `WrittenByService`/`ProjectionVersion` columns were retired and eradicated (#314).
   - Domain services (`MockExamService`, `PlacementService`, `LearningPathService`, `CreditLedger`) own their write boundaries directly using EF Core transactions and explicit domain events without trigger interception.

---

## 2. Knowledge Graph (graphify) — MANDATORY, not advisory

A persistent knowledge graph lives in `graphify-out/`. It is **not tracked** — a local cache over this
source tree. A fresh clone starts with no graph: build it once with `graphify update .` before relying
on anything below.

`graphify` is on PATH (`C:\Users\Nguoi Viet Chim Tay\.local\bin\graphify.exe`). Use it bare. `python -m
graphify` also works and is equivalent, but it prints a spurious skill/package version warning — use the
bare command unless it is missing.

### 2.1 The rule

**Every ticket starts with graphify, before the first file is read.** This is not a suggestion and it is
not "when convenient". A brief that omits the block in §2.4 is an incomplete brief.

**Why it is binding.** grep returns text that looks like evidence and is not: it cannot tell you that
`AttemptEngine.Lifecycle.cs:50` reads `Questions` because `ExamItems` has no roster yet, and it cannot
tell you that a symbol you are about to delete has a caller in a file you did not think to search.
Graphify returns `file:line`, degree and community, so a claim can be checked. Every ticket in this
rebuild where a subagent said "nothing else touches this" and was wrong had skipped it.

### 2.2 The four commands, and how to address a node

**Node addressing is the one thing that will waste your turns if you get it wrong.** Three forms exist
and only one is reliable:

| Form | Works? | Note |
|---|---|---|
| `graphify explain "PracticeService"` | Sometimes | **Ambiguous** for common names — `Questions` matches 8 nodes. Exit 3, no output. |
| `graphify affected "PracticeService"` | **No** | `affected` requires a node **id**, not a label. `No unique node match`. |
| `graphify explain "src/Services/PracticeService.cs::PracticeService"` | **Yes** | `path::symbol`. Use this by default. |
| `graphify explain "<id from the path::symbol output>"` | **Yes** | The `ID:` line it prints. Use this for `affected`. |

Working sequence, verified on this repo:

```sh
# 1. resolve path::symbol -> id, and see neighbours in the same call
graphify explain "src/Services/PracticeService.cs::PracticeService"
#    prints: ID: src_services_practiceservice_services_practiceservice, Degree: 99, and Connections

# 2. feed that id to affected. A label will fail here.
graphify affected src_services_practiceservice_services_practiceservice --depth 2
```

```sh
graphify god-nodes --top 15          # architectural hubs: Contracts(1053) BusinessObjects(666) Services(524)
graphify path "A" "B"                # shortest dependency path between two nodes
graphify query "<q>" --seed "<Node>" # BFS, but ONLY with a seed (see 2.3)
```

### 2.3 `graphify query` is not semantic — use it only with `--seed`

`query` is keyword BFS with a default 2000-token budget. Run bare it dumps 600–950 loosely related
nodes and truncates, so the answer is frequently in the cut. Use it only as
`graphify query "<question>" --seed "<KnownNode>"`, never as the opening move.

### 2.4 The block every brief must carry (copy verbatim, fill the tickets)

```
## Start with graphify (mandatory, before reading any file)

`graphify` is on PATH. Node addressing: `graphify explain "<path>::<Symbol>"` returns the `ID:` line;
`graphify affected <id> --depth 2` needs that id, NOT a label.

Before you read a single source file:
1. `graphify explain "<path>::<the main symbol this ticket changes>"` — neighbours, degree, community, file:line.
2. `graphify affected <id> --depth 2` — everything that breaks if this changes. This is the only thing
   between a deletion ticket and a self-inflicted outage.
3. `graphify god-nodes --top 15` once per wave, so you know which hubs you are near.

In your report, quote the `affected` list you got, and name any caller that surprised you. A report
that says "nothing else references it" without an `affected` output is not evidence.

If the graph reports a `built_at_commit` other than `git rev-parse HEAD`, it is stale: run
`graphify update .` first (no API cost, AST only).
```

### 2.5 Beyond lookup — what else the graph gives you

- **`graphify reflect --if-stale`** then read `graphify-out/reflections/LESSONS.md` at the start of graph
  work. It lists preferred sources, known dead ends and prior corrections. `--if-stale` costs nothing
  when the git hook already refreshed it.
- **`graphify save-result --question … --answer … --type query --nodes … --outcome useful|dead_end|corrected`**
  feeds an answer back so `reflect` can surface it next time. Use `--outcome corrected --correction "…"`
  when you proved a node's community label or a prior claim wrong.
- **`graphify export html`** writes `graphify-out/graph.html`; review it when a wave changes the shape of
  a layer rather than a function.
- **`graphify hook status`** — the post-commit, post-checkout, post-merge and post-rewrite hooks are
  installed, so the graph follows commits automatically. `graphify-out/` is ignored, so these rebuilds
  never create chore commits.

### 2.6 Staleness

If `built_at_commit` ≠ `git rev-parse HEAD`, the graph is stale. Rebuild before trusting it:

```sh
graphify update .        # AST only, no LLM, no API cost
```

A rebuild reporting far fewer than ~14.5k nodes means an interpreter without `tree_sitter_sql` dropped
the 20 `.sql` files (§8). Check with `python -c "import tree_sitter_sql"`.

---

## 3. Look It Up Before You Write It (`heroui-react` MCP, `.heroui-docs/react`, and Context7 MCP)

Before writing or changing anything where exact syntax, versions, or configuration keys matter, query the **`heroui-react` MCP** / **`./.heroui-docs/react`** (for HeroUI v3 components and styling) and the **Context7 MCP** (`resolve-library-id` followed by `query-docs`). Do not write from memory.

**Required queries include**:
- **HeroUI React v3 (`@heroui/react` v3.2+, `@heroui/styles`, Tailwind CSS v4)**: Always read `./.heroui-docs/react` (indexed at the bottom of this file) and call `heroui-react` MCP (`list_components`, `get_component_docs`, `get_theme_variables`, `get_docs`) before using any HeroUI component. Never use HeroUI v2 flat props (`onValueChange`, `ModalContent`, `CardBody`); always use v3 compound components (`onChange`, `Modal.Dialog`, `Card.Content`).
- **EF Core 10**: Scaffolding flags, `HasTrigger`, keyless entities, query filters.
- **ASP.NET Core**: JWT bearer configuration, cookie auth options, anti-forgery middleware, `ProblemDetails`, health checks.
- **payOS API**: VietQR payload structure, webhook verification signature, status queries.
- **SpeechSuper API**: Pronunciation assessment parameters, unscripted speech evaluation (`speak.eval.pro`), fluency scoring, request signature.
- **Anthropic SDK**: Structured outputs schema, model identifiers, streaming responses.

---

## 4. Ubiquitous Domain Language (`CONTEXT.md`)

Always use the domain terms defined in `CONTEXT.md`. Never use the terms marked as *Avoid*:
- **Certificate** (*Avoid*: Exam type, test)
- **Skill** (One modality: listening, reading, speaking, writing)
- **Section** (*Avoid*: Part, except in TOEIC Part 1-7 copy)
- **Exam** (*Avoid*: Test, paper)
- **Practice session** (*Avoid*: Practice unqualified, quiz)
- **Attempt** / **Mock attempt** / **Placement attempt** / **Skill practice attempt**
- **Free response** (Spoken or written answer scored by AI)
- **Raw score** → **Scaled score** → **Band**
- **Credit**, **Ledger**, **Order**, **Gateway**, **Payment event**
- **Grading** (AI scoring of a free response against rubric criteria)
- **Insight** (AI narrative over learner accuracy history)
- **Weakness analysis** (Deterministic SQL views: `vw_UserSectionAccuracyAll`, `vw_UserTopicAccuracy`)

---

## 5. UI Design System: HeroUI v3 Default Theme + Signage Brand Hues (`DESIGN.md`)

When touching frontend code (`src/web`):
- **Follow `DESIGN.md` and `./.heroui-docs/react`**:
  - Import `@import "tailwindcss";` followed by `@import "@heroui/styles";`.
  - Use HeroUI v3 Default Theme semantic tokens (`bg-background text-foreground`, `bg-surface text-surface-foreground`, `bg-surface-secondary`, `bg-overlay`, `border-border`, `border-separator`, `--radius: 0.5rem`, `shadow-surface`, `shadow-overlay`).
- **Signage Brand Hue Overrides**:
  - Accent / Sign Blue (`--accent: #0b4ea2`): Primary actions, focus rings, reached CEFR band, active emphasis.
  - Success / Sign Green (`--success: #0a6b3c`): Positive outcomes, open conditions, correct answers.
  - Warning / Sign Gold (`--warning: #f2b705`): Caution, flagged questions, text highlights.
  - Danger / Sign Red (`--danger: #b31217`): Refusal, danger, negative net revenue, destructive actions.
- **Compound Components Only**: Compose HeroUI v3 parts (`Card.Header`, `Card.Content`, `Tabs.List`, `Tabs.Tab`, `Tabs.Panel`, `Modal.Backdrop`, `Modal.Container`, `Modal.Dialog`, `Select`, `Table`, `AlertDialog`, `Toast`, `Form`, `TextField`, `NumberField`) rather than custom primitives or v2 flat props.
- **Typography**: `IBM Plex Sans` (body/UI), `IBM Plex Sans Condensed` (display headers/badges), `IBM Plex Mono` (`font-mono-tabular` for timers, scores, prices, counters, and aligned numeric data). Use sentence-case headings.
- **Icons**: No emoji or raw unicode glyphs. Use `lucide-react` SVG icons paired with accessible labels or text.

---

## 6. Build & Test Commands

```sh
dotnet build                                   # Build backend + test solution
dotnet test                                    # Run .NET test suite against LocalDB
npm --prefix src/web test                      # Run React/Vitest unit & edge-case tests
npm --prefix src/web run build                 # Typecheck + production bundle for React SPA
npm --prefix src/web run dev                   # Start React + HeroUI SPA dev server
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -f 65001 -i docs/database-schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -f 65001 -i docs/seed-demo.sql
dotnet run --project src/WebAPI                # http://localhost:5001
scripts/seed-manual-test.sh fresh              # reset + seed + start, payment=mock, ai=fake
scripts/crawl-env.sh fresh                     # disposable crawl env: own DB, mock payment, fake AI
docker compose up -d --build                   # whole stack in containers, no SQL Server install
```

Demo accounts (all `Demo1234!`): `admin@demo.local`, `learner@demo.local`, `learner.fresh@demo.local`.
Full manual-test reference — accounts, seeded exams, credit packages, mock-gateway branches —
lives in `docs/MANUAL-TEST-CREDENTIALS.md`. Use `scripts/seed-manual-test.sh` rather than bare
`dotnet run`: `appsettings.json` selects the **real** payOS gateway with placeholder credentials,
so an unparameterised run returns 500 on checkout.

**No SQL Server on the machine?** `docs/DOCKER.md` has the container route: what comes up, the two
URLs, how to switch the gateway to the real one, how to apply a schema change, and how to switch the
OpenAPI surface (and its credential-free demo sign-in) off.

### Test run discipline — filtered during work, full suite twice at the end

`tests/Services.Tests` creates a **fresh database from `database-schema.sql` per test**, so the suite
takes ~20 minutes. Running it between edits wastes the session and teaches nothing a filtered run
would not.

- **During work:** run only the tests the change touches, by namespace or class. A filtered run is
  10–90 s and is the evidence a ticket needs.
  ```sh
  dotnet test tests/Services.Tests --filter "FullyQualifiedName~PoolDrillCatalogTests" --nologo
  dotnet test tests/Services.Tests --filter "FullyQualifiedName~PracticeServiceTests|FullyQualifiedName~PoolDrillCatalogTests" --nologo
  ```
- **When a change fans out** (a shared loader, a schema rule, a service three areas call), run the
  *set of affected areas* once — not the whole suite. That set is the ticket's "prior art" list.
- **At the end of a body of work:** the whole suite **twice**, plus the schema harness and the
  browser journeys on a fresh crawl env. Twice, because a single green run cannot tell a fix from a
  flake.
- **Before believing any count**, if a run reports a total that does not match the previous one:
  kill `testhost`, `dotnet build-server shutdown`, rebuild. A rebuild that reports
  1773/1522/1917/937 total depending on concurrent runs is a stale process, not a real suite.
- **Build errors with no `error CS`** are file locks (MSB3027/MSB3021) held by a running crawl
  server: `scripts/crawl-env.sh stop`, then rebuild.

---

## 7. Fixing a crawl-finding issue

Issues filed from the cross-role crawl (`docs/specs/exhaustive-cross-role-testing-and-triage.md`)
carry exact reproduction steps against `scripts/crawl-env.sh`. Whatever tool you use, a fix is done only when:

1. **Red first.** A test in `tests/Services.Tests` (real SQL Server, real triggers, schema loaded per run) reproduces the reported failure and fails before your change. If the defect lives only in WebMVC, use `tests/WebMVC.Tests`.
2. **Green.** That test and the whole `dotnet test` pass.
3. **Re-walk.** `scripts/crawl-env.sh fresh`, then follow the issue's reproduction steps in the browser; `scripts/crawl-env.sh errors` shows no new `fail:` entries.
4. **Root cause in the PR.** The PR says `Fixes #N` and explains why it broke, not only what changed. Do not weaken a trigger, constraint or test to make the symptom go away; if the invariant itself is wrong, stop and ask on the issue.

---

## 8. Toolbelt — what to reach for, what will hurt you

Read this before your first tool call. Everything below was verified on 2026-10-31, not assumed.

### Use

| Tool | Reach for it when | Why it earns its place |
|---|---|---|
| **graphify** | Every question about structure: "what calls this", "what breaks if I delete it", "how does X reach the DB". | Returns `file:line`, unlike grep. `affected` is the only thing between a deletion ticket and a self-inflicted outage. |
| **heroui-react** (MCP) + `.heroui-docs/react` | Before writing or editing any React UI component in `src/web`. | HeroUI v3 uses compound components (`Card.Header`, `Modal.Dialog`, `Tabs.List`) and Tailwind v4 `@heroui/styles`. Query `list_components`, `get_component_docs`, and `./.heroui-docs/react` first. |
| **context7** (MCP) | Before writing any library API: EF Core, ASP.NET Core, React Router, SpeechSuper, Anthropic SDK. | §3 requires it. Training data is stale on `HasTrigger`, structured outputs, speech payloads. |
| **playwright** (MCP) | When a ticket says "manual test", or when a fix must be seen to be believed. | Caught a 500 that unit tests called a 400 (#146), and proved 12 s → 2.1 s (#140). |
| **tdd** | Before touching behaviour. | Names the seam first, so the test can actually fail. |
| **code-review** | Before every push. | An independent read of the diff. |
| **diagnosing-bugs** | When something is broken and the cause is not obvious. | Stops the guess-and-patch loop. |
| **impeccable** | Any UI polish or layout review against `DESIGN.md`. | Enforces the HeroUI v3 + Signage Brand Hue hierarchy. |
| **ui-ux-pro-max** | Choosing hierarchy, palette, or font pairing. | Reference data only. Never copy its suggestions wholesale — the repo owns its tokens. |
| **domain-modeling** | Adding an entity or a domain term. | Keeps `GLOSSARY.md` and the ADRs in step. |
| **writing-for-agents** | Editing this file, or writing a skill. | |

**ponytail** needs no invocation: the plugin injects it into every session, sub-agents included (verified).

### graphify — four commands, and the one to avoid

```sh
graphify explain "<Symbol>"              # best first move: neighbours + file:line
graphify affected "<Symbol>" --depth 2   # reverse traversal: what breaks if this changes
graphify path "<A>" "<B>"                # shortest path between two nodes
graphify god-nodes --top 10              # the architectural hubs
```

`graphify query "<question>"` is keyword BFS, not semantic. It dumps 600–950 loosely related nodes
and truncates at a 2 000-token budget, so the answer is often in the cut. Use it only with
`--seed "<KnownNode>"`, or not at all.

**Graph maintenance.** Git hooks are installed (`graphify hook status`), so `post-commit` keeps the
local graph current. Because `graphify-out/` is ignored, those rebuilds no longer create
"chore(graph): rebuild" commits or `graph.json` merge conflicts. If you commit somewhere hooks are
skipped, run:

```sh
python -m graphify update .
```

`python -m graphify update .` and bare `graphify update .` are equivalent on this machine:
verified 2026-11-07 that **both** the `python` on PATH and the uv-tool interpreter import
`tree_sitter_sql`, so no `.sql` file is dropped. If a rebuild ever reports far fewer than
~14.5k nodes, re-check with `python -c "import tree_sitter_sql"` — an interpreter missing it
silently drops the 20 `.sql` files and the schema never enters the graph (≈1 700 nodes).

Run `/graphify --update` after large documentation or semantic changes.

### Do not use

These are installed and look plausible. They fight this repo.

| Skill | Why not |
|---|---|
| `ui-styling`, `design`, `design-system` | Assume Tailwind + shadcn (there is no `package.json`) and emit rounded corners and shadows. This system sets `--radius-*: 0` and `--shadow-*: none` deliberately. Use `impeccable`. |
| `banner-design`, `brand`, `slides` | Unrelated to this project. |
| `migrate-to-shoehorn`, `setup-pre-commit`, `setup-ts-deep-modules` | TypeScript and npm tooling. This is .NET. |
| `git-guardrails-claude-code` | A Claude Code hook. OpenCode does not read it. |
| `scaffold-exercises`, `find-skills`, `ask-matt`, `teach`, `to-questionnaire`, `loop-me` | The human's tools, not yours. |

`implement`, `to-spec`, `to-tickets`, `triage`, `wayfinder`, `handoff`, `retro` carry
`disable-model-invocation` — the human invokes them. Do not self-start them.

### Four traps this repo has already sprung

1. **Do not trust a sub-agent's numbers.** One reported "6 schema tests fail on main". True — but
   only after checking in a separate worktree. `scripts/schema-tests-baseline.sh <dir>` confirms
   whether a failure predates your branch before you claim it.
2. **A grep with the wrong scope reports zero hits and looks like proof.** A filtered grep said
   `manual-test-plan.md` had 0 references; a correct one found 5, including a spec calling it "a
   valid regression script". Re-run against the paths you actually mean before deleting anything.
3. **A test that reimplements the code under test always passes.** The first draft of the #146
   regression test copied the endpoint's `catch` into the test body. It now drives the real
   endpoint group, and was checked to fail when the `catch` is removed.
4. **Never `git stash push <file>` in a repo that already holds a stash.** It merged into the old
   entry and left conflict markers in an unrelated file. Commit, or use a worktree.

---

## 9. The rebuild briefs (`docs/research/`)

The placement test, the learning path, the admin tooling and the learner interface are being **deleted
and rebuilt**, not patched. Six research briefs define that work; each is self-contained and is sent to
a separate deep-research session that cannot read this repository.

**Start at `docs/research/README.md`.** It carries the six-file map, the send order, a ready prompt per
session, and the rule for which brief owns a disputed area when two SRS documents disagree.

| Brief | Owns |
|---|---|
| `content-brief.md` | Item levels, topics, formats, rubric descriptors, **every volume number** |
| `placement-brief.md` | How items are used in a sitting, and what a result contains |
| `path-brief.md` | Plan generation, step lifecycle, practice selection, mastery |
| `ui-brief.md` | Every learner screen, bound by the design system in §5 |
| `admin-brief.md` | Staff roles, upload and review, rubric authoring, the publish gate |
| `transition-brief.md` | What survives, what a band means outside the rebuild, and the order of operations |

Two rules from that work apply to any change here:

- **A band is not a CEFR level.** This product labels TOEIC 785–940 as "B2" internally; it is not CEFR
  B2. Placement scores on the CEFR axis. Never add a crosswalk without arguing for it.
- **One writer per fact.** A snapshot derived from an attempt is written by exactly one place. Two
  writers disagreeing about one row is a confirmed defect here
  (`docs/research/placement-free-response-defect.md`), not a hypothetical.

<!-- HEROUI-REACT-AGENTS-MD-START -->
[HeroUI React v3 Docs Index]|root: ./.heroui-docs/react|STOP. What you remember about HeroUI React v3 is WRONG for this project. Always search docs and read before any task.|If docs missing, run this command first: heroui agents-md --react --output new_AGENTS.md|components/(buttons):{button-group.mdx,button.mdx,close-button.mdx,toggle-button-group.mdx,toggle-button.mdx}|components/(collections):{dropdown.mdx,list-box.mdx,tag-group.mdx}|components/(colors):{color-area.mdx,color-field.mdx,color-picker.mdx,color-slider.mdx,color-swatch-picker.mdx,color-swatch.mdx}|components/(controls):{slider.mdx,switch.mdx}|components/(data-display):{badge.mdx,chip.mdx,table.mdx}|components/(date-and-time):{calendar.mdx,date-field.mdx,date-picker.mdx,date-range-picker.mdx,range-calendar.mdx,time-field.mdx}|components/(feedback):{alert.mdx,meter.mdx,progress-bar.mdx,progress-circle.mdx,skeleton.mdx,spinner.mdx}|components/(forms):{checkbox-group.mdx,checkbox.mdx,description.mdx,error-message.mdx,field-error.mdx,fieldset.mdx,form.mdx,input-group.mdx,input-otp.mdx,input.mdx,label.mdx,number-field.mdx,radio-group.mdx,search-field.mdx,text-area.mdx,text-field.mdx}|components/(layout):{card.mdx,separator.mdx,surface.mdx,toolbar.mdx}|components/(media):{avatar-group.mdx,avatar.mdx}|components/(navigation):{accordion.mdx,breadcrumbs.mdx,disclosure-group.mdx,disclosure.mdx,link.mdx,pagination.mdx,tabs.mdx}|components/(overlays):{alert-dialog.mdx,drawer.mdx,modal.mdx,popover.mdx,sheet.mdx,toast.mdx,tooltip.mdx}|components/(pickers):{autocomplete.mdx,combo-box.mdx,select.mdx}|components/(typography):{kbd.mdx,typography.mdx}|components/(utilities):{scroll-shadow.mdx}|getting-started/(handbook):{animation.mdx,colors.mdx,composition.mdx,dark-mode.mdx,styling.mdx,theming.mdx}|getting-started/(overview):{cli.mdx,design-principles.mdx,frameworks.mdx,quick-start.mdx}|getting-started/(ui-for-agents):{agent-skills.mdx,agents-md.mdx,design-md.mdx,llms-txt.mdx,mcp-server.mdx}|releases:{v3-0-0-alpha-32.mdx,v3-0-0-alpha-33.mdx,v3-0-0-alpha-34.mdx,v3-0-0-alpha-35.mdx,v3-0-0-beta-1.mdx,v3-0-0-beta-2.mdx,v3-0-0-beta-3.mdx,v3-0-0-beta-4.mdx,v3-0-0-beta-6.mdx,v3-0-0-beta-7.mdx,v3-0-0-beta-8.mdx,v3-0-0-rc-1.mdx,v3-0-0.mdx,v3-0-2.mdx,v3-0-3.mdx,v3-0-4.mdx,v3-0-5.mdx,v3-1-0.mdx,v3-2-0.mdx,v3-2-1.mdx,v3-2-2.mdx,v3-2-3.mdx,v3-2-4.mdx,v3-2-5.mdx,v3-2-6.mdx,v3-2-7.mdx}|demos/cn/accordion:{basic.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,disabled.tsx,faq.tsx,multiple.tsx,render-function.tsx,surface.tsx,without-separator.tsx}|demos/cn/alert-dialog:{backdrop-variants.tsx,close-methods.tsx,controlled.tsx,custom-animations.tsx,custom-backdrop.tsx,custom-icon.tsx,custom-portal.tsx,custom-styles.tsx,custom-trigger.tsx,default.tsx,dismiss-behavior.tsx,placements.tsx,sizes.tsx,statuses.tsx}|demos/cn/alert:{basic.tsx,custom-styles.tsx}|demos/cn/autocomplete:{allows-empty-collection.tsx,asynchronous-filtering.tsx,controlled-multiple.tsx,controlled-open-state.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,custom-value.tsx,default.tsx,disabled.tsx,email-recipients.tsx,full-width.tsx,location-search.tsx,multiple-select.tsx,on-surface.tsx,required.tsx,tag-group-selection.tsx,user-selection-multiple.tsx,user-selection.tsx,variants.tsx,virtualization.tsx,with-description.tsx,with-disabled-options.tsx,with-sections.tsx}|demos/cn/avatar-group:{basic.tsx,count.tsx,custom-styles.tsx,grid.tsx,max.tsx,overlap.tsx,sizes.tsx}|demos/cn/avatar:{basic.tsx,colors.tsx,custom-image-component.tsx,custom-styles.tsx,fallback.tsx,sizes.tsx,variants.tsx}|demos/cn/badge:{basic.tsx,colors.tsx,custom-styles.tsx,dot.tsx,placements.tsx,sizes.tsx,variants.tsx,with-content.tsx}|demos/cn/breadcrumbs:{basic.tsx,custom-separator.tsx,custom-styles.tsx,disabled.tsx,level-2.tsx,level-3.tsx,render-function.tsx}|demos/cn/button-group:{basic.tsx,custom-styles.tsx,disabled.tsx,full-width.tsx,orientation.tsx,sizes.tsx,variants.tsx,with-icons.tsx,without-separator.tsx}|demos/cn/button:{basic.tsx,custom-styles.tsx,custom-variants.tsx,disabled.tsx,full-width.tsx,icon-only.tsx,loading-state.tsx,loading.tsx,release-outline-variant.tsx,render-function.tsx,ripple-effect.tsx,sizes.tsx,social.tsx,variants.tsx,with-icons.tsx}|demos/cn/calendar:{basic.tsx,booking-calendar.tsx,controlled.tsx,custom-icons.tsx,custom-styles.tsx,day-view.tsx,default-value.tsx,disabled.tsx,focused-value.tsx,international-calendar.tsx,min-max-dates.tsx,multiple-months.tsx,multiple-selection.tsx,read-only.tsx,unavailable-dates.tsx,week-view.tsx,weeks-in-month.tsx,with-indicators.tsx,year-picker.tsx}|demos/cn/card:{custom-styles.tsx,default.tsx,horizontal.tsx,variants.tsx,with-avatar.tsx,with-form.tsx,with-images.tsx}|demos/cn/checkbox-group:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,features-and-addons.tsx,indeterminate.tsx,on-surface.tsx,render-function.tsx,validation.tsx,with-custom-indicator.tsx}|demos/cn/checkbox:{basic.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,default-selected.tsx,disabled.tsx,external-label.tsx,form.tsx,full-rounded.tsx,indeterminate.tsx,invalid.tsx,render-function.tsx,render-props.tsx,variants.tsx,with-description.tsx}|demos/cn/chip:{basic.tsx,custom-styles.tsx,release-vibrant-palette.tsx,statuses.tsx,variants.tsx,with-icon.tsx}|demos/cn/close-button:{custom-styles.tsx,default.tsx,interactive.tsx,variants.tsx,with-custom-icon.tsx}|demos/cn/color-area:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,render-function.tsx,space-and-channels.tsx,with-dots.tsx}|demos/cn/color-field:{basic.tsx,channel-editing.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,invalid.tsx,on-surface.tsx,render-function.tsx,required.tsx,variants.tsx,with-description.tsx}|demos/cn/color-picker:{basic.tsx,controlled.tsx,custom-styles.tsx,with-fields.tsx,with-sliders.tsx,with-swatches.tsx}|demos/cn/color-slider:{alpha-channel.tsx,basic.tsx,channels.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,render-function.tsx,rgb-channels.tsx,vertical.tsx}|demos/cn/color-swatch-picker:{basic.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,default-value.tsx,disabled.tsx,render-function.tsx,sizes.tsx,stack-layout.tsx,variants.tsx}|demos/cn/color-swatch:{accessibility.tsx,basic.tsx,custom-styles.tsx,render-function.tsx,shapes.tsx,sizes.tsx,transparency.tsx}|demos/cn/combo-box:{allows-custom-value.tsx,asynchronous-loading.tsx,controlled-input-value.tsx,controlled.tsx,custom-filtering.tsx,custom-indicator.tsx,custom-styles.tsx,custom-validation.tsx,custom-value.tsx,default-selected-key.tsx,default.tsx,disabled.tsx,form-value.tsx,full-width.tsx,menu-trigger.tsx,multiple-selection.tsx,on-surface.tsx,read-only.tsx,render-function.tsx,required.tsx,validation-behavior.tsx,with-description.tsx,with-disabled-options.tsx,with-sections.tsx}|demos/cn/date-field:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,granularity.tsx,invalid.tsx,on-surface.tsx,render-function.tsx,required.tsx,variants.tsx,with-description.tsx,with-prefix-and-suffix.tsx,with-prefix-icon.tsx,with-suffix-icon.tsx,with-validation.tsx}|demos/cn/date-picker:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,format-options-no-ssr.tsx,format-options.tsx,international-calendar.tsx,render-function.tsx,with-custom-indicator.tsx,with-validation.tsx}|demos/cn/date-range-picker:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,format-options-no-ssr.tsx,format-options.tsx,international-calendar.tsx,release-input-container.tsx,render-function.tsx,with-custom-indicator.tsx,with-validation.tsx}|demos/cn/description:{basic.tsx,custom-styles.tsx}|demos/cn/disclosure-group:{basic.tsx,controlled.tsx,custom-styles.tsx}|demos/cn/disclosure:{basic.tsx,custom-styles.tsx,render-function.tsx}|demos/cn/drawer:{backdrop-variants.tsx,basic.tsx,controlled.tsx,custom-styles.tsx,navigation.tsx,non-dismissable.tsx,placements.tsx,scrollable-content.tsx,with-form.tsx}|demos/cn/dropdown:{controlled-open-state.tsx,controlled.tsx,custom-styles.tsx,custom-trigger.tsx,default.tsx,long-press-trigger.tsx,single-with-custom-indicator.tsx,with-custom-submenu-indicator.tsx,with-descriptions.tsx,with-disabled-items.tsx,with-icons.tsx,with-keyboard-shortcuts.tsx,with-multiple-selection.tsx,with-section-level-selection.tsx,with-sections.tsx,with-single-selection.tsx,with-submenus.tsx}|demos/cn/error-message:{basic.tsx,custom-styles.tsx}|demos/cn/field-error:{basic.tsx,custom-styles.tsx}|demos/cn/fieldset:{basic.tsx,custom-styles.tsx,on-surface.tsx}|demos/cn/form:{basic.tsx,custom-styles.tsx,render-function.tsx}|demos/cn/input-group:{custom-styles.tsx,default.tsx,disabled.tsx,full-width.tsx,invalid.tsx,on-surface.tsx,password-with-toggle.tsx,required.tsx,variants.tsx,with-badge-suffix.tsx,with-copy-suffix.tsx,with-icon-prefix-and-copy-suffix.tsx,with-icon-prefix-and-text-suffix.tsx,with-keyboard-shortcut.tsx,with-loading-suffix.tsx,with-prefix-and-suffix.tsx,with-prefix-icon.tsx,with-suffix-icon.tsx,with-text-prefix.tsx,with-text-suffix.tsx,with-textarea.tsx}|demos/cn/input-otp:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,four-digits.tsx,on-complete.tsx,on-surface.tsx,variants.tsx,with-pattern.tsx,with-validation.tsx}|demos/cn/input:{basic.tsx,controlled.tsx,custom-styles.tsx,full-width.tsx,on-surface.tsx,types.tsx,variants.tsx}|demos/cn/kbd:{basic.tsx,custom-styles.tsx,inline.tsx,instructional.tsx,navigation.tsx,special.tsx,variants.tsx}|demos/cn/label:{basic.tsx,custom-styles.tsx}|demos/cn/link:{basic.tsx,custom-icon.tsx,custom-styles.tsx,icon-placement.tsx,render-function.tsx,underline-and-offset.tsx,underline-offset.tsx,underline-variants.tsx}|demos/cn/list-box:{controlled.tsx,custom-check-icon.tsx,custom-styles.tsx,default.tsx,multi-select.tsx,release-scrollbar-modes.tsx,render-function.tsx,virtualization.tsx,with-disabled-items.tsx,with-sections.tsx}|demos/cn/meter:{basic.tsx,colors.tsx,custom-styles.tsx,custom-value.tsx,sizes.tsx,without-label.tsx}|demos/cn/modal:{backdrop-variants.tsx,close-methods.tsx,controlled.tsx,custom-animations.tsx,custom-backdrop.tsx,custom-portal.tsx,custom-styles.tsx,custom-trigger.tsx,default.tsx,dismiss-behavior.tsx,placements.tsx,scroll-comparison.tsx,sizes.tsx,with-form.tsx}|demos/cn/number-field:{basic.tsx,controlled.tsx,custom-icons.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,on-surface.tsx,render-function.tsx,required.tsx,validation.tsx,variants.tsx,with-chevrons.tsx,with-description.tsx,with-format-options.tsx,with-step.tsx,with-validation.tsx}|demos/cn/pagination:{basic.tsx,controlled.tsx,custom-icons.tsx,custom-styles.tsx,disabled.tsx,simple-prev-next.tsx,sizes.tsx,with-ellipsis.tsx,with-summary.tsx}|demos/cn/popover:{basic.tsx,custom-styles.tsx,interactive.tsx,placement.tsx,render-function.tsx,with-arrow.tsx}|demos/cn/progress-bar:{basic.tsx,colors.tsx,custom-styles.tsx,custom-value.tsx,indeterminate.tsx,sizes.tsx,without-label.tsx}|demos/cn/progress-circle:{basic.tsx,colors.tsx,custom-styles.tsx,custom-svg.tsx,indeterminate.tsx,sizes.tsx,with-label.tsx}|demos/cn/radio-group:{basic.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,delivery-and-payment.tsx,disabled.tsx,horizontal.tsx,on-surface.tsx,render-function.tsx,uncontrolled.tsx,validation.tsx,variants.tsx}|demos/cn/range-calendar:{allows-non-contiguous-ranges.tsx,anchor-unavailable-dates.tsx,basic.tsx,booking-calendar.tsx,controlled.tsx,custom-styles.tsx,day-view.tsx,default-value.tsx,disabled.tsx,focused-value.tsx,international-calendar.tsx,invalid.tsx,min-max-dates.tsx,multiple-months.tsx,read-only.tsx,unavailable-dates.tsx,week-view.tsx,weeks-in-month.tsx,with-indicators.tsx,year-picker.tsx}|demos/cn/scroll-shadow:{custom-styles.tsx,default.tsx,hide-scroll-bar.tsx,orientation.tsx,size.tsx,visibility-change.tsx,with-card.tsx}|demos/cn/search-field:{basic.tsx,controlled.tsx,custom-icons.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,on-surface.tsx,render-function.tsx,required.tsx,validation.tsx,variants.tsx,with-description.tsx,with-keyboard-shortcut.tsx,with-validation.tsx}|demos/cn/select:{asynchronous-loading.tsx,controlled-multiple.tsx,controlled-open-state.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,custom-value.tsx,default.tsx,disabled.tsx,full-width.tsx,multiple-select.tsx,on-surface.tsx,render-function.tsx,required.tsx,variants.tsx,with-clear-button.tsx,with-description.tsx,with-disabled-options.tsx,with-sections.tsx}|demos/cn/separator:{basic.tsx,custom-styles.tsx,render-function.tsx,variants.tsx,vertical.tsx,with-content.tsx,with-surface.tsx}|demos/cn/sheet:{backdrop-variants.tsx,basic.tsx,controlled.tsx,detached.tsx,handle-only.tsx,nested.tsx,non-dismissable.tsx,placements.tsx,professions-picker.tsx,scrollable-content.tsx,snap-points-custom-fade.tsx,snap-points-sequential.tsx,snap-points.tsx,with-form.tsx}|demos/cn/skeleton:{animation-types.tsx,basic.tsx,card.tsx,custom-styles.tsx,grid.tsx,list.tsx,single-shimmer.tsx,text-content.tsx,user-profile.tsx}|demos/cn/slider:{controlled.tsx,custom-output.tsx,custom-styles.tsx,custom-value-formatting.tsx,default.tsx,disabled.tsx,range-with-steps-and-marks.tsx,range.tsx,render-function.tsx,vertical-with-steps-and-marks.tsx,vertical.tsx,with-marks.tsx,with-steps-and-marks.tsx,with-steps.tsx}|demos/cn/spinner:{basic.tsx,colors.tsx,custom-styles.tsx,sizes.tsx,speed.tsx}|demos/cn/surface:{basic.tsx,custom-styles.tsx,variants.tsx,with-form-components.tsx}|demos/cn/switch:{basic.tsx,controlled.tsx,custom-styles.tsx,default-selected.tsx,disabled.tsx,form.tsx,group-horizontal.tsx,group.tsx,label-position.tsx,render-function.tsx,render-props.tsx,sizes.tsx,with-description.tsx,with-icons.tsx,without-label.tsx}|demos/cn/table:{async-loading.tsx,basic.tsx,column-resizing.tsx,custom-cells.tsx,custom-styles.tsx,empty-state.tsx,expandable-rows.tsx,pagination.tsx,secondary-variant.tsx,selection.tsx,sorting.tsx,tanstack-table.tsx,virtualization.tsx}|demos/cn/tabs:{basic.tsx,custom-styles.tsx,disabled.tsx,overflow.tsx,render-function.tsx,secondary-vertical.tsx,secondary.tsx,vertical-alignment.tsx,vertical.tsx,with-separator.tsx}|demos/cn/tag-group:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,render-function.tsx,selection-modes.tsx,sizes.tsx,variants.tsx,with-error-message.tsx,with-list-data.tsx,with-prefix.tsx,with-remove-button.tsx}|demos/cn/textarea:{basic.tsx,controlled.tsx,custom-styles.tsx,full-width.tsx,on-surface.tsx,rows.tsx,variants.tsx}|demos/cn/textfield:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,full-width.tsx,input-types.tsx,on-surface.tsx,render-function.tsx,required.tsx,textarea.tsx,validation.tsx,with-description.tsx,with-error.tsx}|demos/cn/time-field:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,invalid.tsx,on-surface.tsx,render-function.tsx,required.tsx,with-description.tsx,with-prefix-and-suffix.tsx,with-prefix-icon.tsx,with-suffix-icon.tsx,with-validation.tsx}|demos/cn/toast:{callbacks.tsx,custom-indicator.tsx,custom-queue.tsx,custom-styles.tsx,custom-toast.tsx,default.tsx,expanded.tsx,placements.tsx,promise.tsx,simple.tsx,variants.tsx}|demos/cn/toggle-button-group:{attached.tsx,basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,full-width.tsx,orientation.tsx,selection-mode.tsx,sizes.tsx,without-separator.tsx}|demos/cn/toggle-button:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,icon-only.tsx,sizes.tsx,variants.tsx}|demos/cn/toolbar:{attached.tsx,basic.tsx,custom-styles.tsx,vertical.tsx,with-button-group.tsx}|demos/cn/tooltip:{basic.tsx,custom-styles.tsx,custom-trigger.tsx,placement.tsx,render-function.tsx,with-arrow.tsx}|demos/cn/typography:{custom-styles.tsx,default.tsx,primitives.tsx,prose.tsx,render-props.tsx,typography-scale.tsx}|demos/en/accordion:{basic.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,disabled.tsx,faq.tsx,multiple.tsx,render-function.tsx,surface.tsx,without-separator.tsx}|demos/en/alert-dialog:{backdrop-variants.tsx,close-methods.tsx,controlled.tsx,custom-animations.tsx,custom-backdrop.tsx,custom-icon.tsx,custom-portal.tsx,custom-styles.tsx,custom-trigger.tsx,default.tsx,dismiss-behavior.tsx,placements.tsx,sizes.tsx,statuses.tsx}|demos/en/alert:{basic.tsx,custom-styles.tsx}|demos/en/autocomplete:{allows-empty-collection.tsx,asynchronous-filtering.tsx,controlled-multiple.tsx,controlled-open-state.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,custom-value.tsx,default.tsx,disabled.tsx,email-recipients.tsx,full-width.tsx,location-search.tsx,multiple-select.tsx,on-surface.tsx,required.tsx,tag-group-selection.tsx,user-selection-multiple.tsx,user-selection.tsx,variants.tsx,virtualization.tsx,with-description.tsx,with-disabled-options.tsx,with-sections.tsx}|demos/en/avatar-group:{basic.tsx,count.tsx,custom-styles.tsx,grid.tsx,max.tsx,overlap.tsx,sizes.tsx}|demos/en/avatar:{basic.tsx,colors.tsx,custom-image-component.tsx,custom-styles.tsx,fallback.tsx,sizes.tsx,variants.tsx}|demos/en/badge:{basic.tsx,colors.tsx,custom-styles.tsx,dot.tsx,placements.tsx,sizes.tsx,variants.tsx,with-content.tsx}|demos/en/breadcrumbs:{basic.tsx,custom-separator.tsx,custom-styles.tsx,disabled.tsx,level-2.tsx,level-3.tsx,render-function.tsx}|demos/en/button-group:{basic.tsx,custom-styles.tsx,disabled.tsx,full-width.tsx,orientation.tsx,sizes.tsx,variants.tsx,with-icons.tsx,without-separator.tsx}|demos/en/button:{basic.tsx,custom-styles.tsx,custom-variants.tsx,disabled.tsx,full-width.tsx,icon-only.tsx,loading-state.tsx,loading.tsx,release-outline-variant.tsx,render-function.tsx,ripple-effect.tsx,sizes.tsx,social.tsx,variants.tsx,with-icons.tsx}|demos/en/calendar:{basic.tsx,booking-calendar.tsx,controlled.tsx,custom-icons.tsx,custom-styles.tsx,day-view.tsx,default-value.tsx,disabled.tsx,focused-value.tsx,international-calendar.tsx,min-max-dates.tsx,multiple-months.tsx,multiple-selection.tsx,read-only.tsx,unavailable-dates.tsx,week-view.tsx,weeks-in-month.tsx,with-indicators.tsx,year-picker.tsx}|demos/en/card:{custom-styles.tsx,default.tsx,horizontal.tsx,variants.tsx,with-avatar.tsx,with-form.tsx,with-images.tsx}|demos/en/checkbox-group:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,features-and-addons.tsx,indeterminate.tsx,on-surface.tsx,render-function.tsx,validation.tsx,with-custom-indicator.tsx}|demos/en/checkbox:{basic.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,default-selected.tsx,disabled.tsx,external-label.tsx,form.tsx,full-rounded.tsx,indeterminate.tsx,invalid.tsx,render-function.tsx,render-props.tsx,variants.tsx,with-description.tsx}|demos/en/chip:{basic.tsx,custom-styles.tsx,release-vibrant-palette.tsx,statuses.tsx,variants.tsx,with-icon.tsx}|demos/en/close-button:{custom-styles.tsx,default.tsx,interactive.tsx,with-custom-icon.tsx}|demos/en/color-area:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,render-function.tsx,space-and-channels.tsx,with-dots.tsx}|demos/en/color-field:{basic.tsx,channel-editing.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,invalid.tsx,on-surface.tsx,render-function.tsx,required.tsx,variants.tsx,with-description.tsx}|demos/en/color-picker:{basic.tsx,controlled.tsx,custom-styles.tsx,with-fields.tsx,with-sliders.tsx,with-swatches.tsx}|demos/en/color-slider:{alpha-channel.tsx,basic.tsx,channels.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,render-function.tsx,rgb-channels.tsx,vertical.tsx}|demos/en/color-swatch-picker:{basic.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,default-value.tsx,disabled.tsx,render-function.tsx,sizes.tsx,stack-layout.tsx,variants.tsx}|demos/en/color-swatch:{accessibility.tsx,basic.tsx,custom-styles.tsx,render-function.tsx,shapes.tsx,sizes.tsx,transparency.tsx}|demos/en/combo-box:{allows-custom-value.tsx,asynchronous-loading.tsx,controlled-input-value.tsx,controlled.tsx,custom-filtering.tsx,custom-indicator.tsx,custom-styles.tsx,custom-validation.tsx,custom-value.tsx,default-selected-key.tsx,default.tsx,disabled.tsx,form-value.tsx,full-width.tsx,menu-trigger.tsx,multiple-selection.tsx,on-surface.tsx,read-only.tsx,render-function.tsx,required.tsx,validation-behavior.tsx,with-description.tsx,with-disabled-options.tsx,with-sections.tsx}|demos/en/date-field:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,granularity.tsx,invalid.tsx,on-surface.tsx,render-function.tsx,required.tsx,variants.tsx,with-description.tsx,with-prefix-and-suffix.tsx,with-prefix-icon.tsx,with-suffix-icon.tsx,with-validation.tsx}|demos/en/date-picker:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,format-options-no-ssr.tsx,format-options.tsx,international-calendar.tsx,render-function.tsx,with-custom-indicator.tsx,with-validation.tsx}|demos/en/date-range-picker:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,format-options-no-ssr.tsx,format-options.tsx,international-calendar.tsx,release-input-container.tsx,render-function.tsx,with-custom-indicator.tsx,with-validation.tsx}|demos/en/description:{basic.tsx,custom-styles.tsx}|demos/en/disclosure-group:{basic.tsx,controlled.tsx,custom-styles.tsx}|demos/en/disclosure:{basic.tsx,custom-styles.tsx,render-function.tsx}|demos/en/drawer:{backdrop-variants.tsx,basic.tsx,controlled.tsx,custom-styles.tsx,navigation.tsx,non-dismissable.tsx,placements.tsx,scrollable-content.tsx,with-form.tsx}|demos/en/dropdown:{controlled-open-state.tsx,controlled.tsx,custom-styles.tsx,custom-trigger.tsx,default.tsx,long-press-trigger.tsx,single-with-custom-indicator.tsx,with-custom-submenu-indicator.tsx,with-descriptions.tsx,with-disabled-items.tsx,with-icons.tsx,with-keyboard-shortcuts.tsx,with-multiple-selection.tsx,with-section-level-selection.tsx,with-sections.tsx,with-single-selection.tsx,with-submenus.tsx}|demos/en/error-message:{basic.tsx,custom-styles.tsx}|demos/en/field-error:{basic.tsx,custom-styles.tsx}|demos/en/fieldset:{basic.tsx,custom-styles.tsx,on-surface.tsx}|demos/en/form:{basic.tsx,custom-styles.tsx,render-function.tsx}|demos/en/input-group:{custom-styles.tsx,default.tsx,disabled.tsx,full-width.tsx,invalid.tsx,on-surface.tsx,password-with-toggle.tsx,required.tsx,variants.tsx,with-badge-suffix.tsx,with-copy-suffix.tsx,with-icon-prefix-and-copy-suffix.tsx,with-icon-prefix-and-text-suffix.tsx,with-keyboard-shortcut.tsx,with-loading-suffix.tsx,with-prefix-and-suffix.tsx,with-prefix-icon.tsx,with-suffix-icon.tsx,with-text-prefix.tsx,with-text-suffix.tsx,with-textarea.tsx}|demos/en/input-otp:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,four-digits.tsx,on-complete.tsx,on-surface.tsx,variants.tsx,with-pattern.tsx,with-validation.tsx}|demos/en/input:{basic.tsx,controlled.tsx,custom-styles.tsx,full-width.tsx,on-surface.tsx,types.tsx,variants.tsx}|demos/en/kbd:{basic.tsx,custom-styles.tsx,inline.tsx,instructional.tsx,navigation.tsx,special.tsx,variants.tsx}|demos/en/label:{basic.tsx,custom-styles.tsx}|demos/en/link:{basic.tsx,custom-icon.tsx,custom-styles.tsx,icon-placement.tsx,render-function.tsx,underline-and-offset.tsx}|demos/en/list-box:{controlled.tsx,custom-check-icon.tsx,custom-styles.tsx,default.tsx,multi-select.tsx,release-scrollbar-modes.tsx,render-function.tsx,virtualization.tsx,with-disabled-items.tsx,with-sections.tsx}|demos/en/meter:{basic.tsx,colors.tsx,custom-styles.tsx,custom-value.tsx,sizes.tsx,without-label.tsx}|demos/en/modal:{backdrop-variants.tsx,close-methods.tsx,controlled.tsx,custom-animations.tsx,custom-backdrop.tsx,custom-portal.tsx,custom-styles.tsx,custom-trigger.tsx,default.tsx,dismiss-behavior.tsx,placements.tsx,scroll-comparison.tsx,sizes.tsx,with-form.tsx}|demos/en/number-field:{basic.tsx,controlled.tsx,custom-icons.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,on-surface.tsx,render-function.tsx,required.tsx,validation.tsx,variants.tsx,with-chevrons.tsx,with-description.tsx,with-format-options.tsx,with-step.tsx,with-validation.tsx}|demos/en/pagination:{basic.tsx,controlled.tsx,custom-icons.tsx,custom-styles.tsx,disabled.tsx,simple-prev-next.tsx,sizes.tsx,with-ellipsis.tsx,with-summary.tsx}|demos/en/popover:{basic.tsx,custom-styles.tsx,interactive.tsx,placement.tsx,render-function.tsx,with-arrow.tsx}|demos/en/progress-bar:{basic.tsx,colors.tsx,custom-styles.tsx,custom-value.tsx,indeterminate.tsx,sizes.tsx,without-label.tsx}|demos/en/progress-circle:{basic.tsx,colors.tsx,custom-styles.tsx,custom-svg.tsx,indeterminate.tsx,sizes.tsx,with-label.tsx}|demos/en/radio-group:{basic.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,delivery-and-payment.tsx,disabled.tsx,horizontal.tsx,on-surface.tsx,render-function.tsx,uncontrolled.tsx,validation.tsx,variants.tsx}|demos/en/range-calendar:{allows-non-contiguous-ranges.tsx,anchor-unavailable-dates.tsx,basic.tsx,booking-calendar.tsx,controlled.tsx,custom-styles.tsx,day-view.tsx,default-value.tsx,disabled.tsx,focused-value.tsx,international-calendar.tsx,invalid.tsx,min-max-dates.tsx,multiple-months.tsx,read-only.tsx,unavailable-dates.tsx,week-view.tsx,weeks-in-month.tsx,with-indicators.tsx,year-picker.tsx}|demos/en/scroll-shadow:{custom-styles.tsx,default.tsx,hide-scroll-bar.tsx,orientation.tsx,size.tsx,visibility-change.tsx,with-card.tsx}|demos/en/search-field:{basic.tsx,controlled.tsx,custom-icons.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,on-surface.tsx,render-function.tsx,required.tsx,validation.tsx,variants.tsx,with-description.tsx,with-keyboard-shortcut.tsx,with-validation.tsx}|demos/en/select:{asynchronous-loading.tsx,controlled-multiple.tsx,controlled-open-state.tsx,controlled.tsx,custom-indicator.tsx,custom-styles.tsx,custom-value.tsx,default.tsx,disabled.tsx,full-width.tsx,multiple-select.tsx,on-surface.tsx,render-function.tsx,required.tsx,variants.tsx,with-clear-button.tsx,with-description.tsx,with-disabled-options.tsx,with-sections.tsx}|demos/en/separator:{basic.tsx,custom-styles.tsx,render-function.tsx,variants.tsx,vertical.tsx,with-content.tsx,with-surface.tsx}|demos/en/sheet:{backdrop-variants.tsx,basic.tsx,controlled.tsx,detached.tsx,handle-only.tsx,nested.tsx,non-dismissable.tsx,placements.tsx,professions-picker.tsx,scrollable-content.tsx,snap-points-custom-fade.tsx,snap-points-sequential.tsx,snap-points.tsx,with-form.tsx}|demos/en/skeleton:{animation-types.tsx,basic.tsx,custom-styles.tsx,grid.tsx,list.tsx,single-shimmer.tsx,text-content.tsx,user-profile.tsx}|demos/en/slider:{controlled.tsx,custom-output.tsx,custom-styles.tsx,custom-value-formatting.tsx,default.tsx,disabled.tsx,range-with-steps-and-marks.tsx,range.tsx,render-function.tsx,vertical-with-steps-and-marks.tsx,vertical.tsx,with-marks.tsx,with-steps-and-marks.tsx,with-steps.tsx}|demos/en/spinner:{basic.tsx,colors.tsx,custom-styles.tsx,sizes.tsx,speed.tsx}|demos/en/surface:{basic.tsx,custom-styles.tsx,variants.tsx,with-form-components.tsx}|demos/en/switch:{basic.tsx,controlled.tsx,custom-styles.tsx,default-selected.tsx,disabled.tsx,form.tsx,group-horizontal.tsx,group.tsx,label-position.tsx,render-function.tsx,render-props.tsx,sizes.tsx,with-description.tsx,with-icons.tsx,without-label.tsx}|demos/en/table:{async-loading.tsx,basic.tsx,column-resizing.tsx,custom-cells.tsx,custom-styles.tsx,empty-state.tsx,expandable-rows.tsx,pagination.tsx,secondary-variant.tsx,selection.tsx,sorting.tsx,tanstack-table.tsx,virtualization.tsx}|demos/en/tabs:{basic.tsx,custom-styles.tsx,disabled.tsx,overflow.tsx,render-function.tsx,secondary-vertical.tsx,secondary.tsx,vertical-alignment.tsx,vertical.tsx,with-separator.tsx}|demos/en/tag-group:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,render-function.tsx,selection-modes.tsx,sizes.tsx,variants.tsx,with-error-message.tsx,with-list-data.tsx,with-prefix.tsx,with-remove-button.tsx}|demos/en/textarea:{basic.tsx,controlled.tsx,custom-styles.tsx,full-width.tsx,on-surface.tsx,rows.tsx,variants.tsx}|demos/en/textfield:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,full-width.tsx,input-types.tsx,on-surface.tsx,render-function.tsx,required.tsx,textarea.tsx,validation.tsx,with-description.tsx,with-error.tsx}|demos/en/time-field:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,form-example.tsx,full-width.tsx,invalid.tsx,on-surface.tsx,render-function.tsx,required.tsx,with-description.tsx,with-prefix-and-suffix.tsx,with-prefix-icon.tsx,with-suffix-icon.tsx,with-validation.tsx}|demos/en/toast:{callbacks.tsx,custom-indicator.tsx,custom-queue.tsx,custom-styles.tsx,custom-toast.tsx,default.tsx,expanded.tsx,placements.tsx,promise.tsx,simple.tsx,variants.tsx}|demos/en/toggle-button-group:{attached.tsx,basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,full-width.tsx,orientation.tsx,selection-mode.tsx,sizes.tsx,without-separator.tsx}|demos/en/toggle-button:{basic.tsx,controlled.tsx,custom-styles.tsx,disabled.tsx,icon-only.tsx,sizes.tsx,variants.tsx}|demos/en/toolbar:{attached.tsx,basic.tsx,custom-styles.tsx,vertical.tsx,with-button-group.tsx}|demos/en/tooltip:{basic.tsx,custom-styles.tsx,custom-trigger.tsx,placement.tsx,render-function.tsx,with-arrow.tsx}|demos/en/typography:{custom-styles.tsx,default.tsx,primitives.tsx,prose.tsx,render-props.tsx,typography-scale.tsx}
<!-- HEROUI-REACT-AGENTS-MD-END -->
