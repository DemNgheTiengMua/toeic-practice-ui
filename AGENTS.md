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

---

## 2. Knowledge Graph (graphify)

A pre-built persistent knowledge graph exists in `graphify-out/` (2,053 nodes, 3,702 edges across code, database schema, design specifications, and prototype views).

**Before debugging, tracing data flow, or implementing new features**:
- **Query the graph**: Run `graphify query "<question>"` (or BFS traversal over `graphify-out/graph.json`) to understand cross-layer relationships and avoid breaking invariants.
- **Trace connections**: Run `graphify path "<Source>" "<Target>"` to inspect dependencies between layers (e.g. from a UI view to a DB trigger).
- **Inspect nodes**: Run `graphify explain "<NodeId>"` to get plain-language summaries of symbols, tables, or concepts.
- **Visual map**: Open `graphify-out/graph.html` in a browser or review `graphify-out/GRAPH_REPORT.md`.
- **Rebuild after major changes**: Run `/graphify --update` to re-extract modified files incrementally.

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
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -i docs/database-schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -i docs/seed-demo.sql
dotnet run --project src/WebAPI                # http://localhost:5001
dotnet run --project src/WebMVC                # http://localhost:5000/certificates
```
