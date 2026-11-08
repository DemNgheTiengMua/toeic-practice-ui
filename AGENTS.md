# AGENTS.md

Guidance and instructions for AI coding assistants (Antigravity/AGY, Claude Code, and autonomous agents) working in this repository.

---

## 1. Core Architecture & Layering

The backend is a .NET 10 solution (`ExtraEfficiency.slnx`) structured in six strict layers:
`BusinessObjects` (domain models) ← `DataAccess` (EF Core, database-first) ← `Services` (use cases) ← `Contracts` (wire DTOs & companion API client) — consumed by:
- `WebAPI` (port 5001, minimal API, JWT bearer auth)
- `WebMVC` (port 5000, server-rendered ASP.NET Core MVC + Razor views + jQuery Ajax)

### Binding Architectural Invariants
1. **Database-First EF Core (`docs/adr/0001-database-first-ef-core.md`)**:
   - `docs/database-schema.sql` is the single source of truth for the database schema.
   - Any schema changes must be edited in `docs/database-schema.sql` first, validated with `docs/schema-tests/run-all.sh`, and re-scaffolded.
   - `run-all.sh` runs **every** test on its own fresh database and reports one verdict per file; it does not stop at the first failure, and exits non-zero if any check failed. Read the summary at the end for the failing set.
   - A fixture `INSERT ... SELECT` must match a row. The harness injects `ExpectFixtureRows` after every line-leading such statement in every domain file (`docs/schema-tests/fixture-guard.awk`), so a fixture matching zero rows aborts naming the file and line instead of letting a test's own `THROW 'EXPECT_REJECT FAILED'` report a constraint defect that never happened. `INSERT ... VALUES` needs no guard, and `docs/seed-demo.sql` runs unguarded by design.
   - Every table with an SQL trigger **must** declare `HasTrigger(...)` in EF Core model configuration to prevent `OUTPUT`-clause save failures.
   - Views are mapped as keyless, read-only entities. Derived values (balances, ratings, aggregates) are never stored or computed by application code.
2. **MVC Proxies API (`docs/adr/0002-mvc-proxies-api-with-cookie-token.md`)**:
   - The browser never calls `WebAPI` directly. CORS remains closed.
   - The JWT token is kept strictly in an **HttpOnly cookie** on the MVC site; browser scripts cannot access it.
   - All AJAX calls route through MVC forwarding endpoints protected by anti-forgery tokens.
3. **Credit Wallet & Ledger (`PRODUCT.md`, `CONTEXT.md`)**:
   - Credit balance is always `SUM(Amount)` over the append-only `CreditTransactions` ledger.
   - There is no stored wallet balance column.
   - Invariant: "Block before you charge" — check all preconditions before creating a debit transaction.
   - The free diagnostic loop (practice, weakness analysis, placement test) is completely free and never writes a ledger row.
4. **Certificate Isolation (`PRODUCT.md`)**:
   - Composite foreign keys carry `CertId` down through exams, sections, attempts, and learning paths.
   - One certificate's content, scoring, or rubric can never leak into another.
5. **Practice Session Retention & Logging Bound (`CONTEXT.md`, `PRODUCT.md`)**:
   - Practice drilling generates free diagnostic data (`PracticeSessions` and `PracticeAnswers`).
   - To keep storage bounded in the long run while retaining enough data for AI mentor insights and weakness analysis (`vw_UserSectionAccuracyAll`, `vw_UserCategoryAccuracy`), standalone sessions are capped per certificate per learner (default: 20 sessions).
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
   - `MockExamService` and `PlacementService` act as thin domain adapters over `IAttemptEngine`, maintaining domain-specific rules (e.g., mock exam credit deduction via `ICreditLedger` vs free diagnostic placement tests, and placement target band tracking) without duplicating sitting state machines.
8. **One Writer Per Derived Fact (`docs/adr/0012-single-writer-derived-facts.md`)**:
   - Seven derived facts are registered in `DerivedFactOwners` with their owning service. Three are enforced today (placement result, attempt score and band, step closure); three await a table (#210, #212, #232); one (credit movement) is exempt because the ledger already carries the guarantee and SRS D-09 refuses a second guard.
   - Every covered derived-fact table carries `WrittenByService` and `ProjectionVersion`. A trigger refuses a write that declares a writer other than the fact's owner, or a projection version that is not newer than the stored one.
   - The writer is a column on the write, **never `sp_set_session_context`** (PRN-01: it survives commit and rollback). The declaration must be in the statement — the trigger checks `UPDATE(WrittenByService)`, because a second writer that changes only the value inherits the previous name.
   - The derived fact is a column set, not a row: `ExamAttempts` sitting-state writes and `LearnerPathSteps` open-state moves derive nothing and declare nothing.

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

## 3. Look It Up Before You Write It (Context7 MCP)

Before writing or changing anything where exact syntax, versions, or configuration keys matter, query the **Context7 MCP** (`resolve-library-id` followed by `query-docs`). Do not write from memory.

**Required Context7 queries include**:
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
- **Weakness analysis** (Deterministic SQL views: `vw_UserSectionAccuracyAll`, `vw_UserCategoryAccuracy`)

---

## 5. UI Design System: "Bảng hiệu phòng thi" (`DESIGN.md`)

When touching frontend code or Razor views:
- **No rounded corners (`border-radius: 0`)**, no shadows (`box-shadow: none`), no gradients, no backdrop blur.
- **Flat painted fields and keylines**: 2px ink (`#14181d`) for panel/sign frames, 1px soft rule (`#ccd2d6`) for internal dividers.
- **Signage Palette**:
  - Sign Blue (`#0b4ea2`): Primary actions, active ring, reached CEFR band.
  - Sign Gold (`#f2b705`): Warning, flagged questions, text highlights.
  - Sign Red (`#b31217`): Refusal, danger, failed conditions.
  - Sign Green (`#0a6b3c`): Open conditions, success, correct answers.
- **Inversion Rule**: Active/selected items invert to solid ink (`#14181d`) with white text. Never use a tint or wash for selection.
- **Typography**: IBM Plex Mono (numbers, timers, scores, tabular data), IBM Plex Sans Condensed (uppercase headlines, labels, placards), IBM Plex Sans (body text).
- **Icons**: No emoji or raw unicode glyphs. Use the drawn SVG icon set in `assets/js/icons.js` / `wwwroot/js/icons.js` and pair with text.

---

## 6. Build & Test Commands

```sh
dotnet build                                   # Build whole solution
dotnet test                                    # Run test suite against LocalDB
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -f 65001 -i docs/database-schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -f 65001 -i docs/seed-demo.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -f 65001 -i docs/seed-diagnostic-benchmark.sql
dotnet run --project src/WebAPI                # http://localhost:5001
dotnet run --project src/WebMVC                # http://localhost:5000/certificates
scripts/seed-manual-test.sh fresh              # reset + seed + start, payment=mock, ai=fake
scripts/crawl-env.sh fresh                     # disposable crawl env: own DB, mock payment, fake AI
```

Demo accounts (all `Demo1234!`): `admin@demo.local`, `learner@demo.local`, `learner.fresh@demo.local`.
Full manual-test reference — accounts, seeded exams, credit packages, mock-gateway branches —
lives in `docs/MANUAL-TEST-CREDENTIALS.md`. Use `scripts/seed-manual-test.sh` rather than bare
`dotnet run`: `appsettings.json` selects the **real** payOS gateway with placeholder credentials,
so an unparameterised run returns 500 on checkout.

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
| **context7** (MCP) | Before writing any library API: EF Core, ASP.NET Core, SpeechSuper, Anthropic SDK. | §3 requires it. Training data is stale on `HasTrigger`, structured outputs, speech payloads. |
| **playwright** (MCP) | When a ticket says "manual test", or when a fix must be seen to be believed. | Caught a 500 that unit tests called a 400 (#146), and proved 12 s → 2.1 s (#140). |
| **tdd** | Before touching behaviour. | Names the seam first, so the test can actually fail. |
| **code-review** | Before every push. | An independent read of the diff. |
| **diagnosing-bugs** | When something is broken and the cause is not obvious. | Stops the guess-and-patch loop. |
| **impeccable** | Any Razor view or CSS change. | Knows the "Bảng hiệu phòng thi" system. Use it for #159, #147. |
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
