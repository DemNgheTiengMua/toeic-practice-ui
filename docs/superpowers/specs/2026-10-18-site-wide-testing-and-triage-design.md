# Design Document: Site-Wide Testing, Playwright E2E Verification & Defect Triage

**Date:** 2026-10-18  
**Topic:** Site-Wide Testing & Defect Triage Pipeline  
**Target Solution:** `ExtraEfficiency.slnx` (WebMVC port 5000, WebAPI port 5001)  
**Status:** Draft for Review  

---

## 1. Intent and Why

The platform has reached a substantial milestone with numerous complex subsystems implemented: guided learning decks, admin exam studio with visual audio slicing, credit ledger monetization, diagnostic placement testing, AI-driven free response practice, and administrative oversight portals.

Before moving forward with new features, we must perform an exhaustive quality audit to ensure the platform is robust, navigationally sound, and faithful to its design principles. In the stakeholder's words:
> *"UI/UX intact. No dead-end, no orphan, no 500. The site is solid, everything we've implemented working as intended."*

To keep the process controlled and manageable without overwhelming the team, testing and defect resolution are strictly decoupled:
**Crawl & Take Notes → Analyze & Cluster → Write Specs → File Actionable GitHub Issues → Fix with TDD.**

---

## 2. Goals, Non-Goals, and Anti-Goals

### Goals
- **Zero Dead-End & Orphan Guarantee**: Every route, navigation bar link, back button, cancel link, and breadcrumb must lead to a meaningful, accessible page. No user should ever be trapped or stranded.
- **Zero Unhandled 500 Crashes**: All standard user journeys and common boundary/edge cases (empty submissions, malformed inputs, double-clicks, back-navigation after submission) must handle errors gracefully without raw 500 error pages.
- **Comprehensive 8-Loop Coverage**: Methodically walk all 8 functional domains across all user personas (Anonymous Guest, Fresh Learner with 0 history, Active Learner with 8 credits and existing history, and Administrator).
- **Playwright E2E Regression Harness**: Leverage `/playwright-e2e` principles (Page Object Model, accessible locators like `getByRole` and `getByLabel`, auto-waiting, and isolated test lifecycles) to extend `tests/WebMVC.E2ETests`.
- **Systematic Defect Triage**: Log every finding during sweeps into scratch findings notes, cross-reference against server logs (`api.log`, `mvc.log`), and convert into fully contextualized GitHub issues.

### Non-Goals
- **In-flight Hotfixing**: Code modifications are strictly forbidden during crawl sweeps. Ad-hoc fixing distracts from completing the full inventory of issues and risks cascading regressions.
- **Real Third-Party API Spend**: Live SpeechSuper audio evaluations and real Claude API calls are excluded during bulk sweeps. We use the offline deterministic fake AI providers (`AI_PROVIDER=fake`).
- **Real Payment Processing**: Real payOS gateway calls are excluded. All checkout tests execute through the deterministic mock gateway (`Payment__Gateway=mock`).

### Anti-Goals
- **"Click Every Pixel" Aimless Wandering**: Random clicking without a structured checklist or domain purpose.
- **Vague, Non-Reproducible Bug Reports**: Filing issues like "checkout feels broken" without exact accounts, input values, network status codes, and server stack traces.

---

## 3. Constraints & Invariants

1. **Dedicated Disposable Sandbox (`scripts/crawl-env.sh`)**:
   - All interactive and automated crawls run strictly against the isolated database `ExtraEfficiencyCrawl` on LocalDB via `scripts/crawl-env.sh fresh`.
   - Everyday development database `ExtraEfficiency` must never be touched or altered by the crawl.
2. **Database-First Integrity**:
   - Schema modifications, triggers, and constraints defined in `docs/database-schema.sql` are the single source of truth.
   - Any database error revealed during crawl must not be patched by weakening triggers or constraints.
3. **Credit Ledger Invariants**:
   - Wallet balance is always computed as `SUM(Delta)` over `CreditTransactions`. There is no mutable balance column.
   - "Block before you charge": preconditions must be verified before debiting credits.
   - Diagnostic placement loop is 100% free and must never produce a ledger row.
4. **UI Signage Design System ("Bảng hiệu phòng thi")**:
   - Sharp rectangular aesthetic: `border-radius: 0`, `box-shadow: none`, 2px ink frame (`#14181d`), 1px soft interior rules (`#ccd2d6`).
   - Signage color accents: Sign Blue (`#0b4ea2`), Sign Gold (`#f2b705`), Sign Red (`#b31217`), Sign Green (`#0a6b3c`).
   - Inversion rule for selection: solid ink background with white text.
   - No raw emoji or unstyled browser defaults; all icons paired with clear text labels.

---

## 4. Testing Architecture & How It Gets Made

The sweep combines two complementary techniques:

```
┌─────────────────────────────────────────────────────────────────┐
│               Disposable Sandbox (crawl-env.sh)                 │
│         WebMVC (Port 5000)  ◄──►  WebAPI (Port 5001)            │
│         Database: ExtraEfficiencyCrawl | Payment: Mock          │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                 ┌───────────────┴───────────────┐
                 ▼                               ▼
   [Live Playwright MCP Walk]       [Permanent Automated E2E]
   • Explores edge cases, inputs    • C# .NET 10 Microsoft.Playwright
   • Checks UI signage & layouts    • Page Object Model (POM)
   • Verifies no dead-ends/orphans  • Accessible locators (getByRole)
   • Monitors api.log for 500s      • Fast regression verification
                 │                               │
                 └───────────────┬───────────────┘
                                 ▼
                     Scratch Findings Notes
                                 ▼
                    Defect Analysis & Clustering
                                 ▼
               GitHub Issues (ready-for-agent)
```

### The 8 Testing Loops

#### Loop 0: Identity, Authentication & User Management
- **Personas**: Anonymous Guest, `learner@demo.local`, `admin@demo.local`
- **Surface**:
  - Guest: `/auth/register` → local activation token in `mail-drop` → `/auth/verify-email` → `/auth/sign-in`.
  - Recovery: `/auth/forgot-password` → `.eml` reset token → `/auth/reset-password`.
  - Resilience: Repeated invalid sign-ins, duplicate registration emails, invalid tokens.
  - Learner: `/profile` (update name, display info), `/profile/password` (password change).
  - Admin: `/admin/users` (filter, lock/unlock, resend activation, manual credit adjustment).
  - Cross-Role Verification: Locked user immediately blocked from sign-in; credit adjustments immediately visible on `/wallet`.

#### Loop 1: Exams, Sitting Engine & Practice Drilling
- **Personas**: `admin@demo.local`, `learner@demo.local`
- **Surface**:
  - Admin: `/admin/exams` → create exam → `{id}/studio` (question groups, questions, visual audio slicer, staging preview, reordering) → `{id}/audit` (publish readiness) → `{id}/publish`; export JSON package, delete draft exam (`[TEST-CRAWL]`).
  - Learner Exam Sitting: `/exams` catalog → `/exams/{id}/instructions` → start exam (debits 1 credit) → `/exams/take/{attemptId}` (timed sitting, radio selection, back/forward, timer expiration & auto-submit) → `/exams/result/{attemptId}` (scaled scores & CEFR band) → `/exams/review/{attemptId}` → `/exams/insight/{attemptId}` → `/exams/history`.
  - Learner Practice Drilling: `/practice` → start section drill → `/practice/{sessionId}` (instant answer feedback, hint reveals) → `/practice/{id}/summary` → `/practice/insight/{id}`.
  - Cross-Role & Invariants: Unpublished exam never visible to learner; starting exam twice does not double-charge; practice session limit pruning (FIFO cap) functions cleanly.

#### Loop 2: Lessons & Guided Learning System
- **Personas**: `admin@demo.local`, `learner@demo.local`, `learner.fresh@demo.local`
- **Surface**:
  - Admin: `/admin/lessons` → create lesson → single-page card editor (concept cards, code/example, interactive micro-check quiz) → preview deck player → download template / export manifest → delete draft lesson.
  - Learner: `/learning-path` (target band overview, generate path, reset path, unlock module, premium upgrade with credits) → `/learning-path/modules/{id}` → `/lessons/{id}` (interactive card flip, micro-check answer validation, completion marker) → `/lessons/by-category/{id}`.
  - Cross-Role Verification: Newly created lesson immediately visible in corresponding learning path step; deleting a lesson does not orphan or corrupt an active learner path.

#### Loop 3: Question Ingestion & Parser Consistency
- **Personas**: `admin@demo.local`
- **Surface**:
  - Comparison of the two ingest pipelines:
    1. Standalone Ingest: `/admin/imports` (file upload).
    2. Studio Quick Ingest: `/admin/exams/{id}/studio` (`quick-ingest/parse` → staging preview → `commit`).
  - Edge Cases: Valid JSON/text, malformed JSON, missing mandatory fields (e.g. missing correct option), duplicate questions. Verify both return friendly validation errors instead of 500s.

#### Loop 4: Packages, Checkout & Credit Ledger
- **Personas**: `admin@demo.local`, `learner@demo.local`
- **Surface**:
  - Admin: `/admin/packages` (create package, edit price/credits, deactivate, toggle featured).
  - Learner: `/pricing` → select package → `/checkout` → `/checkout/mock-gateway` (simulate success, fail, cancel) → `/payment/result` → `/wallet` (transaction ledger).
  - Admin: `/admin/orders` → order detail → reconcile order.
  - Cross-Role & Invariants: Successful payment credits wallet exactly once (idempotent across refresh/F5); cancelled/failed orders do not debit or credit; deactivated packages cannot be purchased; wallet ledger matches admin order record.

#### Loop 5: Score Conversion & Diagnostic Placement
- **Personas**: `admin@demo.local`, `learner.fresh@demo.local`
- **Surface**:
  - Admin: `/admin/score-conversion` (edit conversion scales, update CEFR band thresholds). Trigger `trg_ScoreBands_NoOverlap` must enforce integrity with clean validation errors.
  - Learner (`learner.fresh`): `/placement` → start placement test (100% free, no credit check) → take placement exam → submit → `/placement/result` → `/placement/target-band` (save target band triggers automatic learning path generation) → verify `/dashboard` displays updated target.

#### Loop 6: Support, Complaints & Mentors
- **Personas**: `learner@demo.local`, `admin@demo.local`
- **Surface**:
  - Learner Support: `/support` → `/support/report` → `/support/history`.
  - Learner Mentors: `/mentors` → `/mentors/{id}` → book available slot → confirm booking (charges credit) → boarding pass display → `/mentors/bookings` (cancel booking refunds credit and reopens slot, rate completed booking).
  - Admin: `/admin/complaints` (inspect complaint, change status, resolve); `/admin/mentors` (create mentor profile, add availability slots, close slots).
  - Cross-Role Verification: Resolved complaint updates status in learner history; booked slot disappears from mentor listing; cancelled booking restores slot.

#### Loop 7: AI Free-Response Practice & Analytics
- **Personas**: `learner@demo.local`
- **Surface**:
  - Writing Practice: `/writing/{id}/instructions` → take writing exam → draft autosave → submit → pending state → fake AI grading completion → detailed rubric review (task achievement, coherence, vocabulary, grammar).
  - Speaking Practice: `/speaking/{id}/instructions` → take speaking exam → audio upload simulation → evaluate → review pronunciation/fluency feedback.
  - Analytics & Retention: `/saved-questions` (bookmarking, filtering by tag/skill, unsave), `/weakness-analysis` (section accuracy, category accuracy views), `/learner/insights/dashboard`.

---

### 4.3 Systematic Logic Defect Detection Methodology

To ensure we catch silent business logic errors (not just 500s or visual broken links), every loop incorporates four concrete verification mechanisms:

```
┌────────────────────────────────────────────────────────────────────────┐
│                   LOGIC ERROR DETECTION FRAMEWORK                      │
├────────────────────────────────────────────────────────────────────────┤
│ 1. Direct SQL Invariant Probes                                         │
│    • Ledger balance matches: SELECT SUM(Delta) FROM CreditTransactions │
│    • Free diagnostic rule: Placement attempts write ZERO ledger rows   │
│    • Session cap: Standalone practice sessions never exceed 20         │
│    • Score snapshotting: Past exam attempts preserve band on scale edit│
├────────────────────────────────────────────────────────────────────────┤
│ 2. Cross-Role Consistency Verification                                 │
│    • Admin state changes (publish/deactivate/lock/resolve) reflect     │
│      instantly and accurately in Learner views                         │
│    • Inactive/draft entities never leak into Learner catalogs          │
├────────────────────────────────────────────────────────────────────────┤
│ 3. Cross-Tenant Authorization Probes (IDOR / Data Leakage)             │
│    • User B attempting to view User A's attemptId, orderId, or profile │
│    • Learner attempting to access any /admin/* route (must redirect)   │
├────────────────────────────────────────────────────────────────────────┤
│ 4. Idempotency & Action State Transitions                              │
│    • Double-click "Start Exam" must only debit 1 credit                │
│    • F5 refresh after payment callback must not duplicate credits      │
│    • Browser "Back" button after exam submit cannot re-open sitting    │
└────────────────────────────────────────────────────────────────────────┘
```

1. **Direct SQL Database Invariant Queries**:
   - After any transactional step (exam start, practice answer, mentor booking, checkout, placement test), query `ExtraEfficiencyCrawl` directly via `sqlcmd`:
     - **Credit Balance Audit**: `SELECT ISNULL(SUM(Delta), 0) FROM CreditTransactions WHERE UserId = @UserId` must match the UI header to the exact integer.
     - **Free Placement Invariant**: Confirm `CreditTransactions` has 0 records associated with placement exams.
     - **Practice Pruning Invariant**: `SELECT COUNT(*) FROM PracticeSessions WHERE UserId = @UserId AND LearnerPathStepId IS NULL` must stay $\le 20$.
     - **Score & Band Determinism**: Confirm raw score scales correctly via `ScoreScales` and CEFR band matches `ScoreBands`.
2. **Cross-Role Reflection Verification**:
   - Actions in Admin must propagate correctly to Learner:
     - Publishing an exam in Exam Studio makes it visible in `/exams`; keeping it draft or un-audited hides it.
     - Deactivating a package in `/admin/packages` prevents Learner from purchasing it.
     - Locking a user in `/admin/users` causes the next request from that user to terminate the session and refuse login.
     - Resolving a complaint in `/admin/complaints` changes the badge in Learner `/support/history`.
3. **Cross-Tenant & Role Authorization Probes (Security & Privacy)**:
   - Verify that an unauthenticated user or Learner cannot access `/admin/*` routes (must redirect to login or return 403 Forbidden).
   - Insecure Direct Object Reference (IDOR) check: Generate an `attemptId` or `orderId` under `learner@demo.local`, sign in as `learner.fresh@demo.local`, and attempt to access `/exams/review/{attemptId}` or `/checkout/order/{orderId}`. Verify the platform cleanly forbids access rather than displaying another user's data.
4. **Idempotency, Resubmission & Race Conditions**:
   - **Double POST / Double Click**: Rapidly submitting "Start Exam", "Book Mentor", or "Pay Now" must execute idempotently (single ledger debit, single booking).
   - **Post-Submission Navigation**: Hitting the browser Back button after completing an exam or checkout must not resubmit or cause state corruption.

---

## 5. Defect Classification & Triage Pipeline

All discoveries during the crawl will be captured in `scratch/findings-loop-{N}.md` and categorized:

| Category | Definition | Priority | Action |
|---|---|---|---|
| `[CRASH]` | Unhandled 500 error, unhandled exception, raw DB trigger/FK violation visible to user, page whiteout. | Critical | Extract stack trace from `api.log`, file GitHub issue with `bug` + `ready-for-agent`. |
| `[LOGIC]` | No crash, but incorrect behavior: credit balance mismatch, score calculation wrong, bypass of auth/role permissions, double charge. | Critical | Cite architectural invariant, file GitHub issue with `bug` + `ready-for-agent`. |
| `[DEAD-END]` | Broken link, orphan page without navigation back, dead button that does nothing upon click, missing redirect. | High | Document exact URL and missing route, file GitHub issue. |
| `[UX/SIGNAGE]` | Visual layout glitch, non-compliant rounded corners/shadows, unstyled tables, missing label next to icon, unhelpful error message. | Medium | Consolidate into a single enhancement issue per loop. |

### The Triage Process
1. **Walk Loop**: Run live crawl on `http://localhost:5000` via Playwright with `scripts/crawl-env.sh`.
2. **Log Findings**: Record every oddity in `scratch/findings-loop-{N}.md`.
3. **Analyze**: Correlate browser actions with `logs/crawl/api.log`, `logs/crawl/mvc.log`, and database tables (`CreditTransactions`, `ExamAttempts`, etc.).
4. **Issue Creation**: For each distinct defect, format an issue following `.github/ISSUE_TEMPLATE/crawl-finding.md` containing:
   - Reproduction steps (exact account, URL, form inputs).
   - Expected vs Actual behavior with doc citations.
   - Stack trace / console error.
   - Suspected code location and `AGENTS.md` invariants.
   - Acceptance criteria.

---

## 6. What's Left to the Builder (Implementation Team)

- Detailed Page Object definitions in `tests/WebMVC.E2ETests/Pages/` (e.g. `ExamStudioPage`, `DeckPlayerPage`, `CheckoutPage`).
- Concrete test input datasets (test titles, sample audio files, boundary strings).
- Execution pacing and order across loops (Loop 0 through Loop 7).
- Exact NUnit assertion messages for automated tests.

---

## 7. Reviewer Clarifications & Architectural Decisions

Following architectural review, the following key decisions are codified:

1. **Phasing & Deliverables Sequence**:
   - **Phase 1 (Audit & Triage Sweep)**: Execute the live Playwright exploratory sweep across all 8 loops using `scripts/crawl-env.sh`. Correlate actions with `logs/crawl/api.log` to catch dead ends, orphans, and 500s. Record findings in `scratch/findings-loop-{N}.md`, analyze root causes, and file well-formed GitHub issues.
   - **Phase 2 (Automated Regression Harness)**: Expand `tests/WebMVC.E2ETests` with Page Object Models for verified, stable user journeys. Automated tests will not attempt to assert broken workflows until corresponding tickets are resolved.
2. **Database State & Environment Isolation**:
   - The crawl relies on `scripts/crawl-env.sh fresh` before each loop to guarantee clean state and reset `ExtraEfficiencyCrawl` with demo seed data (e.g. resetting `learner@demo.local` to 8 credits).
   - Automated `WebMVC.E2ETests` suites will be structured per journey to avoid cross-test interference, assuming a fresh demo seed.
3. **Test Boundary Allocation (E2E vs. Integration)**:
   - In accordance with `AGENTS.md` §7, deep negative edge cases (SQL triggers, composite key checks, currency overflow, race conditions) are reproduced and verified primarily in `tests/Services.Tests` and `tests/WebMVC.Tests`.
   - `tests/WebMVC.E2ETests` focuses strictly on real browser user-facing journeys, navigation links, UI state transitions, and responsive layout hygiene.

