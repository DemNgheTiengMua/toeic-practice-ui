# Site-Wide Testing, Playwright E2E Verification & Defect Triage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Perform an exhaustive, cross-role quality audit across all 8 user journeys in the web platform, detecting navigation dead-ends, orphan views, unhandled 500 errors, and business logic anomalies, triaging findings into actionable GitHub issues, and expanding permanent Playwright E2E test suites with Page Object Models.

**Architecture:** We use a disposable local sandbox (`scripts/crawl-env.sh fresh`) with an isolated database (`ExtraEfficiencyCrawl`), offline fake AI, and mock payment gateway. Interactive exploratory crawling is conducted using Playwright MCP and cross-referenced with `api.log` and direct `sqlcmd` database assertions, followed by defect clustering into GitHub issues, and codifying stable journeys into C# Playwright NUnit tests (`WebMVC.E2ETests`).

**Tech Stack:** ASP.NET Core 10 (WebMVC & WebAPI), EF Core 10 (Database-First), Microsoft.Playwright (C# .NET 10 / NUnit), Playwright MCP, LocalDB / SQL Server, Bash (`scripts/crawl-env.sh`), GitHub CLI (`gh`).

**Spec:** [`docs/superpowers/specs/2026-10-18-site-wide-testing-and-triage-design.md`](file:///D:/Study/PRN3/Project/extra_efficiency_ui/docs/superpowers/specs/2026-10-18-site-wide-testing-and-triage-design.md)

## Global Constraints

- Must run against isolated sandbox `ExtraEfficiencyCrawl` on LocalDB via `scripts/crawl-env.sh fresh`; development database `ExtraEfficiency` must never be touched.
- In-flight hotfixing is strictly forbidden during crawl sweeps; discovery and fixing are decoupled.
- All mock entities created during the crawl must use the prefix `[TEST-CRAWL]`.
- Wallet balance is always computed as `SUM(Delta)` over `CreditTransactions`; there is no mutable balance column.
- Preconditions must be verified before debiting credits ("Block before you charge").
- Diagnostic placement test loop is completely free and must never write a ledger row.
- Standalone practice sessions are capped at 20 per certificate per learner and pruned FIFO; learning path sessions (`LearnerPathStepId IS NOT NULL`) are never pruned.
- UI Design System: flat frames, 2px ink frame (`#14181d`), `border-radius: 0`, `box-shadow: none`, signage palette (Blue, Gold, Red, Green), inversion for selection.
- All bug issues filed must use `.github/ISSUE_TEMPLATE/crawl-finding.md`.

## Review Focus

1. **Double-click idempotency on transactional actions**: Submitting "Start Exam", "Book Mentor", or "Pay" multiple times rapidly must only debit credits once and produce a single record.
2. **Cross-tenant authorization & IDOR**: Direct access to another user's attempt review (`/exams/review/{attemptId}`) or order (`/checkout/order/{orderId}`) must return 403 Forbidden or redirect, never leaking private data.
3. **Role boundary violations**: Authenticated `learner` navigating directly to any `/admin/*` route must be redirected cleanly to login or forbidden, never rendering admin management UI.
4. **Trigger violation user feedback**: Admin entering overlapping score thresholds must trigger `trg_ScoreBands_NoOverlap` with a clean form validation message, never an unhandled 500 error page.
5. **Practice session pruning bound**: After completing 21 standalone practice sessions, `COUNT(*)` of standalone sessions for that learner must remain $\le 20$, with the oldest pruned FIFO and learning path sessions untouched.

---

### Task 1: Environment & Crawler Harness Verification

**Files:**
- Modify: `scripts/crawl-env.sh` (if needed for port/log adjustments)
- Test: `tests/WebMVC.E2ETests/PlaywrightTestBase.cs`
- Output: `scratch/findings-environment.md`

**Interfaces:**
- Consumes: `docs/database-schema.sql`, `docs/seed-demo.sql`
- Produces: Running WebMVC (http://localhost:5000), WebAPI (http://localhost:5001), fresh `ExtraEfficiencyCrawl` database

- [ ] **Step 1: Execute crawl environment fresh reset**

Run: `bash scripts/crawl-env.sh fresh`  
Expected: Drops and recreates `ExtraEfficiencyCrawl`, loads schema and demo seed, builds solution, starts WebAPI on 5001 and WebMVC on 5000. Reports `Learner balance: 8 credits`.

- [ ] **Step 2: Verify server status and initial health**

Run: `bash scripts/crawl-env.sh status`  
Expected: WebAPI listening on port 5001, WebMVC listening on port 5000, learner balance 8 credits.

- [ ] **Step 3: Verify initial zero-error baseline**

Run: `bash scripts/crawl-env.sh errors`  
Expected: No server exceptions logged in `logs/crawl/api.log` or `logs/crawl/mvc.log`.

- [ ] **Step 4: Initialize scratch findings directory**

Create directory `scratch/` if not present. Verify `scratch/` is ignored by git.

- [ ] **Step 5: Commit any environment adjustments**

```bash
git add scripts/crawl-env.sh
git commit -m "chore(test): verify crawl environment harness and sandbox baseline"
```

---

### Task 2: Crawl & Logic Audit — Loop 0 (Identity, Authentication & User Management)

**Files:**
- Output: `scratch/findings-loop-0.md`

**Interfaces:**
- Consumes: Anonymous guest, `learner@demo.local`, `admin@demo.local` credentials (`Demo1234!`)
- Produces: Log of defects, broken navigation links, and logic audit results for Loop 0

- [ ] **Step 1: Execute Guest Registration and Mail Activation Walk**
  1. Navigate to `http://localhost:5000/auth/register`.
  2. Register account `[TEST-CRAWL] guest.audit@demo.local`.
  3. Inspect latest `.eml` file in `src/WebAPI/mail-drop/` to extract activation token.
  4. Navigate to `/auth/verify-email?token={token}`. Verify activation success placard.
  5. Sign in with newly activated account. Verify redirect to `/dashboard` or onboarding.

- [ ] **Step 2: Test Guest Negative Edge Cases**
  1. Duplicate registration: Register again with `guest.audit@demo.local`. Verify friendly validation error (no 500).
  2. Forgot password: `/auth/forgot-password` with valid email; check `.eml` token in `src/WebAPI/mail-drop/`; reset password at `/auth/reset-password`.
  3. Lockout: Attempt 5 rapid sign-ins with wrong password. Verify lock notice / message.

- [ ] **Step 3: Execute Learner Profile & Password Audit**
  1. Sign in as `learner@demo.local`.
  2. Navigate to `/profile`. Update display name to `[TEST-CRAWL] Learner Audited`. Save.
  3. Navigate to `/profile/password`. Test wrong current password vs matching new password.

- [ ] **Step 4: Execute Admin User Management & Logic Invariant Probes**
  1. Sign in as `admin@demo.local`.
  2. Navigate to `/admin/users`. Search for `guest.audit@demo.local`.
  3. Toggle lock on `guest.audit@demo.local`.
  4. In parallel/incognito, attempt sign-in as `guest.audit@demo.local`. Verify sign-in refused.
  5. Adjust credits: Add 5 credits to `learner@demo.local`.
  6. **SQL Logic Probe**: Run `sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiencyCrawl -Q "SELECT ISNULL(SUM(Delta),0) FROM CreditTransactions c JOIN Users u ON u.UserId = c.UserId WHERE u.Email = 'learner@demo.local';"` — verify result equals 13.
  7. Switch session to `learner@demo.local`. Navigate to `/wallet`. Verify balance displays 13 credits.

- [ ] **Step 5: Role Guard & IDOR Probes**
  1. While signed in as `learner@demo.local`, attempt to navigate to `/admin/users` and `/admin/exams`.
  2. Verify user is redirected or denied access (HTTP 403 / redirect to sign-in), with 0 administrative UI rendered.

- [ ] **Step 6: Check Server Errors and Compile Findings**
  1. Run `bash scripts/crawl-env.sh errors`.
  2. Record all findings, dead-ends, and visual glitches into `scratch/findings-loop-0.md`.

---

### Task 3: Crawl & Logic Audit — Loop 1 (Exams, Sitting Engine & Practice Drilling)

**Files:**
- Output: `scratch/findings-loop-1.md`

**Interfaces:**
- Consumes: `admin@demo.local`, `learner@demo.local` (reset to fresh state before run)
- Produces: Log of defects, sitting engine state checks, and pruning invariants for Loop 1

- [ ] **Step 1: Reset sandbox to clean state**

Run: `bash scripts/crawl-env.sh fresh`  
Verify learner balance is 8 credits.

- [ ] **Step 2: Admin Exam Studio Authoring & Publish Readiness Walk**
  1. Sign in as `admin@demo.local`. Navigate to `/admin/exams`.
  2. Create exam `[TEST-CRAWL] Audit Exam 1`.
  3. Open `{id}/studio`. Verify single-page shell, curriculum rail, and section templates.
  4. Add a Question Group and 2 Questions. Test visual audio slicer component.
  5. Open `{id}/audit`. Verify audit placard detects publish readiness status.
  6. Publish exam.
  7. **Logic Probe**: Switch to `learner@demo.local`, navigate to `/exams`. Verify `[TEST-CRAWL] Audit Exam 1` is visible. Un-publish or leave draft; verify hidden from learner.

- [ ] **Step 3: Learner Exam Sitting & Score Scaling Lifecycle**
  1. Sign in as `learner@demo.local` (8 credits).
  2. Navigate to `/exams`, select an available exam. Open `/exams/{id}/instructions`.
  3. Click "Start Exam".
  4. **Logic Probe (Debit)**: Verify credit balance drops to 7. Run SQL query on `CreditTransactions` confirming single -1 row with `AttemptId`.
  5. **Idempotency Probe**: Double-click "Start Exam" rapidly on a second attempt; verify it does not double debit.
  6. Sitting interface (`/exams/take/{attemptId}`):
     - Select radio answers across questions.
     - Verify countdown timer displays.
     - Navigate between question numbers.
     - Submit exam.
  7. Results view (`/exams/result/{attemptId}`):
     - Verify raw score scaled correctly via `ScoreScales` and CEFR band assigned.
     - Review detailed question explanations at `/exams/review/{attemptId}`.
     - Verify AI narrative insight at `/exams/insight/{attemptId}`.
     - Check `/exams/history` displays the completed attempt.

- [ ] **Step 4: Learner Practice Drilling & FIFO Pruning Invariant**
  1. Navigate to `/practice`. Select a skill section (e.g. TOEIC Part 5).
  2. Click "Start Practice".
  3. Answer practice questions: verify instant feedback placard (Sign Green for correct, Sign Red for incorrect).
  4. Reveal hints: verify hint tracking and explanation rendering.
  5. Complete drill: navigate to summary and practice insight.
  6. **FIFO Pruning Invariant Probe**:
     - Run script to simulate 21 practice sessions for this user.
     - **SQL Logic Probe**: `SELECT COUNT(*) FROM PracticeSessions WHERE UserId = @UserId AND LearnerPathStepId IS NULL;`
     - Verify count is $\le 20$. Verify oldest standalone session was pruned FIFO. Verify learning path sessions (`LearnerPathStepId IS NOT NULL`) are not pruned.

- [ ] **Step 5: Cross-Tenant IDOR Probe on Exam Attempt**
  1. Copy the `attemptId` of the completed exam.
  2. Sign in as `learner.fresh@demo.local`.
  3. Attempt to navigate directly to `http://localhost:5000/exams/review/{attemptId}`.
  4. Verify system returns 403 or redirects cleanly without leaking questions or answers.

- [ ] **Step 6: Check Server Errors and Compile Findings**
  1. Run `bash scripts/crawl-env.sh errors`.
  2. Record all findings into `scratch/findings-loop-1.md`.

---

### Task 4: Crawl & Logic Audit — Loop 2 (Lessons & Guided Learning System)

**Files:**
- Output: `scratch/findings-loop-2.md`

**Interfaces:**
- Consumes: `admin@demo.local`, `learner@demo.local`, `learner.fresh@demo.local`
- Produces: Log of defects, deck player audit, and path progression checks for Loop 2

- [ ] **Step 1: Admin Lesson Card Authoring & Management Walk**
  1. Sign in as `admin@demo.local`. Navigate to `/admin/lessons`.
  2. Create lesson `[TEST-CRAWL] Audit Lesson 101` with skill, category, and target band.
  3. Use single-page editor: add 3 cards (Concept Explanation, Code/Example Markdown, Micro-Check Question with options).
  4. Click "Preview": verify deck player modal renders card flips and markdown accurately.
  5. Test export manifest and download sample template.

- [ ] **Step 2: Learner Guided Learning Path Walk**
  1. Sign in as `learner.fresh@demo.local`.
  2. Navigate to `/learning-path`.
  3. If path not generated, click "Generate Path" based on initial target band.
  4. Verify module hierarchy, step indicators, and lock/unlock icons.
  5. Navigate to `/lessons/{id}` to launch the interactive walkthrough deck player.
  6. Step through cards using keyboard navigation (Arrow keys / Space) and on-screen buttons.
  7. Answer the micro-check question: test incorrect option feedback vs correct option unlock.
  8. Click "Complete Lesson": verify path progress updates on `/learning-path`.

- [ ] **Step 3: Premium Path Upgrade & Credit Debit Probe**
  1. Sign in as `learner@demo.local` (has credits).
  2. Open a locked premium module on `/learning-path`.
  3. Click "Unlock with Credits".
  4. **SQL Logic Probe**: Check `CreditTransactions` for correct debit amount. Verify module changes to unlocked state.
  5. Verify unlocked status persists across page refresh.

- [ ] **Step 4: Cross-Role Deletion Safety Probe**
  1. As Admin, delete a lesson that is attached to a step in a learner's active path.
  2. As Learner, revisit `/learning-path`.
  3. Verify the learning path handles the missing lesson gracefully (displays disabled indicator or placeholder) without crashing with a 500 error.

- [ ] **Step 5: Check Server Errors and Compile Findings**
  1. Run `bash scripts/crawl-env.sh errors`.
  2. Record all findings into `scratch/findings-loop-2.md`.

---

### Task 5: Crawl & Logic Audit — Loop 3 & Loop 4 (Question Ingest, Packages, Checkout & Credit Ledger)

**Files:**
- Output: `scratch/findings-loop-3.md`, `scratch/findings-loop-4.md`

**Interfaces:**
- Consumes: `admin@demo.local`, `learner@demo.local`
- Produces: Log of parser consistency, payment gateway state, and credit ledger reconciliation

- [ ] **Step 1: Reset sandbox to clean state**

Run: `bash scripts/crawl-env.sh fresh`  
Verify learner balance is 8 credits.

- [ ] **Step 2: Loop 3 — Question Ingest Pipeline Comparison**
  1. Prepare 3 test payloads:
     - Payload A: Valid 5-question JSON matching schema.
     - Payload B: Malformed JSON (syntax error, unclosed bracket).
     - Payload C: Missing mandatory fields (question text present, options missing correct key).
  2. Test Path 1: Standalone import at `/admin/imports`.
  3. Test Path 2: Studio Quick Ingest at `/admin/exams/{id}/studio` (`quick-ingest/parse` → staging preview → `commit`).
  4. Verify: Both paths return structured error messages for Payloads B and C without crashing with 500.
  5. Note any schema discrepancies or redundancy between the two paths in `scratch/findings-loop-3.md`.

- [ ] **Step 3: Loop 4 — Admin Credit Package Management**
  1. Sign in as `admin@demo.local`. Navigate to `/admin/packages`.
  2. Create package `[TEST-CRAWL] 50 Credits Pack` (Price: 200,000 VND, Credits: 50).
  3. Edit price to 180,000 VND. Toggle "Featured".
  4. Create a second package `[TEST-CRAWL] Deactivated Pack` and mark as deactivated.

- [ ] **Step 4: Loop 4 — Learner Checkout & Mock Gateway Flow**
  1. Sign in as `learner@demo.local`. Navigate to `/pricing`.
  2. Verify `[TEST-CRAWL] 50 Credits Pack` is visible; verify `[TEST-CRAWL] Deactivated Pack` is hidden.
  3. Select `[TEST-CRAWL] 50 Credits Pack` and click "Buy Now".
  4. Navigate to `/checkout` and initiate payment.
  5. Mock Gateway Simulation (`/checkout/mock-gateway`):
     - Case A (Cancel): Click "Cancel Payment". Verify return to `/payment/result` with cancelled status; verify balance remains 8 credits.
     - Case B (Fail): Initiate checkout, simulate payment failure. Verify failure status placard; verify balance remains 8 credits.
     - Case C (Success): Initiate checkout, simulate successful payment. Verify redirect to `/payment/result` with success placard.
  6. **SQL Logic Probe (Idempotency)**:
     - Run SQL query: `SELECT ISNULL(SUM(Delta),0) FROM CreditTransactions c JOIN Users u ON u.UserId = c.UserId WHERE u.Email = 'learner@demo.local';`
     - Verify balance is exactly 58 credits ($8 + 50$).
     - Refresh `/payment/result` multiple times (F5). Re-run SQL query: verify balance remains 58 (no duplicate credits).

- [ ] **Step 5: Loop 4 — Admin Order Reconcile & Ledger Match**
  1. Sign in as `admin@demo.local`. Navigate to `/admin/orders`.
  2. Locate the completed order. Inspect order detail.
  3. Verify order total, credits granted, and user ID match the learner transaction.
  4. Test order reconcile action if applicable.

- [ ] **Step 6: Check Server Errors and Compile Findings**
  1. Run `bash scripts/crawl-env.sh errors`.
  2. Record all findings into `scratch/findings-loop-3.md` and `scratch/findings-loop-4.md`.

---

### Task 6: Crawl & Logic Audit — Loop 5 (Score Conversion & Diagnostic Placement Test)

**Files:**
- Output: `scratch/findings-loop-5.md`

**Interfaces:**
- Consumes: `admin@demo.local`, `learner.fresh@demo.local`
- Produces: Log of score conversion trigger checks, placement test zero-debit verification, and target band path generation

- [ ] **Step 1: Admin Score Conversion & Trigger Validation**
  1. Sign in as `admin@demo.local`. Navigate to `/admin/score-conversion`.
  2. Inspect existing score scales and CEFR band ranges.
  3. Attempt to save an overlapping band range (e.g. Band B1: 400–600 and Band B2: 550–750).
  4. **Trigger Validation Probe**: Verify that database trigger `trg_ScoreBands_NoOverlap` blocks the change and the MVC controller renders a helpful validation placard, NOT an unhandled 500 error page.

- [ ] **Step 2: Learner Free Diagnostic Placement Test Walk**
  1. Sign in as `learner.fresh@demo.local` (fresh account, 0 prior attempts).
  2. Navigate to `/placement`.
  3. Start placement exam.
  4. Complete and submit the placement test.
  5. Results view: verify diagnostic breakdown across listening and reading, suggested target band, and diagnostic narrative.
  6. **SQL Logic Probe (Free Diagnostic Invariant)**:
     - Run: `SELECT COUNT(*) FROM CreditTransactions c JOIN Users u ON u.UserId = c.UserId WHERE u.Email = 'learner.fresh@demo.local';`
     - Verify result is **EXACTLY 0**. The diagnostic test must never write a ledger transaction.

- [ ] **Step 3: Target Band Selection & Path Auto-Generation**
  1. On `/placement/target-band`, select a target band (e.g. TOEIC 750 / B2). Click "Save Target Band".
  2. Verify redirect to `/dashboard`.
  3. Verify `/dashboard` placard displays the newly chosen target band.
  4. Navigate to `/learning-path`: verify a personalized learning path was automatically generated based on the target band.

- [ ] **Step 4: Past Score Snapshotted Invariant Probe**
  1. As Admin, edit the score band threshold slightly (non-overlapping).
  2. Revisit the placement test result or mock exam result of a previously completed attempt.
  3. Verify the previously awarded band is unchanged (snapshotted at submission time, not retroactively recomputed).

- [ ] **Step 5: Check Server Errors and Compile Findings**
  1. Run `bash scripts/crawl-env.sh errors`.
  2. Record all findings into `scratch/findings-loop-5.md`.

---

### Task 7: Crawl & Logic Audit — Loop 6 (Support, Complaints & Mentors)

**Files:**
- Output: `scratch/findings-loop-6.md`

**Interfaces:**
- Consumes: `learner@demo.local`, `admin@demo.local`
- Produces: Log of complaint handling, mentor slot concurrency, and refund ledger verification

- [ ] **Step 1: Reset sandbox to clean state**

Run: `bash scripts/crawl-env.sh fresh`  
Verify learner balance is 8 credits.

- [ ] **Step 2: Learner Support Ticket Submission Walk**
  1. Sign in as `learner@demo.local`. Navigate to `/support`.
  2. Click "Report Issue" (`/support/report`).
  3. Fill out complaint form with category "Exam Issue", description `[TEST-CRAWL] Audio skipped on question 4`. Submit.
  4. Navigate to `/support/history`. Verify the new ticket appears with status "Pending" or "Submitted".

- [ ] **Step 3: Admin Complaint Resolution & Cross-Role Reflection**
  1. Sign in as `admin@demo.local`. Navigate to `/admin/complaints`.
  2. Open the newly submitted complaint. Inspect description.
  3. Change status to "Resolved", add resolution notes `[TEST-CRAWL] Verified audio track, resolved`. Save.
  4. Switch back to `learner@demo.local`. Revisit `/support/history`.
  5. Verify status badge updates to "Resolved" with admin notes displayed.

- [ ] **Step 4: Admin Mentor Management & Slot Scheduling**
  1. As Admin, navigate to `/admin/mentors`.
  2. Create mentor `[TEST-CRAWL] Expert Mentor`.
  3. Navigate to mentor slots: add an available slot for tomorrow at 14:00 (Cost: 2 credits).

- [ ] **Step 5: Learner Booking, Ledger Debit & Boarding Pass Walk**
  1. Sign in as `learner@demo.local` (8 credits). Navigate to `/mentors`.
  2. View `[TEST-CRAWL] Expert Mentor`. Select the 14:00 slot.
  3. Click "Book Slot". Confirm modal.
  4. **SQL Logic Probe (Debit)**: Verify credit balance is 6 credits (-2 credits).
  5. Verify booking boarding pass card renders on `/mentors/bookings`.
  6. **Concurrency Probe**: Open a second browser session as `learner.fresh@demo.local` with credits; verify the 14:00 slot is no longer available to book.

- [ ] **Step 6: Booking Cancellation, Slot Restoration & Refund Invariant**
  1. As `learner@demo.local`, navigate to `/mentors/bookings`.
  2. Click "Cancel Booking". Confirm cancellation modal.
  3. **SQL Logic Probe (Refund)**: Verify credit balance returns to 8 credits (+2 credit refund transaction in `CreditTransactions`).
  4. Verify the slot reappears as available in the mentor's booking schedule.

- [ ] **Step 7: Check Server Errors and Compile Findings**
  1. Run `bash scripts/crawl-env.sh errors`.
  2. Record all findings into `scratch/findings-loop-6.md`.

---

### Task 8: Crawl & Logic Audit — Loop 7 (AI Free-Response Practice & Analytics)

**Files:**
- Output: `scratch/findings-loop-7.md`

**Interfaces:**
- Consumes: `learner@demo.local`
- Produces: Log of AI grading lifecycle checks, weakness analysis verification, and insights dashboard audit

- [ ] **Step 1: Writing Practice & Offline AI Grading Lifecycle Walk**
  1. Sign in as `learner@demo.local`. Navigate to `/writing`.
  2. Select an IELTS or CEFR writing prompt. View `/writing/{id}/instructions`.
  3. Click "Start Writing".
  4. In the drafting editor, enter response text (250+ words). Verify draft autosave indicator.
  5. Click "Submit".
  6. Verify UI transitions to "Grading in Progress" / Pending status.
  7. **Worker Processing**: Allow the background worker `AiGradingBackgroundService` to process the fake AI grading (`AI_PROVIDER=fake`).
  8. Refresh or observe status transition to "Graded".
  9. Inspect review page: verify rubric breakdown (Task Achievement, Coherence & Cohesion, Lexical Resource, Grammatical Range) and highlight annotations.

- [ ] **Step 2: Speaking Practice & Audio Upload Simulation Walk**
  1. Navigate to `/speaking`. Select a speaking prompt.
  2. View instructions and start attempt.
  3. Simulate audio submission (upload sample WAV/MP3 or invoke test audio endpoint).
  4. Submit speaking response.
  5. Verify pending queue state transitions to completed grading.
  6. Inspect fluency, pronunciation, and vocabulary evaluation scores.

- [ ] **Step 3: Saved Questions Bookmarking & Filtering Walk**
  1. Revisit any completed review (Exam, Practice, or Placement).
  2. Click "Save Question" / Bookmark icon on 2 distinct questions.
  3. Navigate to `/saved-questions`.
  4. Verify bookmarked questions appear with accurate tags, skill indicators, and explanations.
  5. Test skill filters (Listening vs Reading).
  6. Remove bookmark: verify question un-saves cleanly.

- [ ] **Step 4: Weakness Analysis Views & Insights Dashboard Audit**
  1. Navigate to `/weakness-analysis`.
  2. Verify deterministic SQL views render properly:
     - `vw_UserSectionAccuracyAll`: Section-level accuracy percentages and question counts.
     - `vw_UserCategoryAccuracy`: Category/grammar topic accuracy ratings.
  3. Navigate to `/learner/insights/dashboard`.
  4. Verify AI mentor recommendation narrative displays cleanly with signage typography.

- [ ] **Step 5: Check Server Errors and Compile Findings**
  1. Run `bash scripts/crawl-env.sh errors`.
  2. Record all findings into `scratch/findings-loop-7.md`.

---

### Task 9: Defect Triage, Root-Cause Analysis & GitHub Issue Generation

**Files:**
- Output: `scratch/triage-summary.md`
- Creates: GitHub Issues via `gh issue create`

**Interfaces:**
- Consumes: `scratch/findings-loop-0.md` through `scratch/findings-loop-7.md`, `logs/crawl/api.log`
- Produces: Well-formed GitHub issues labeled `ready-for-agent` and `bug`

- [ ] **Step 1: Consolidate and cluster all scratch findings**
  1. Aggregate findings from all 8 loop scratch files.
  2. Group findings by severity and category:
     - `[CRASH]`: Unhandled 500 exceptions, whiteout pages, raw SQL exceptions.
     - `[LOGIC]`: Credit discrepancies, bypasses, state synchronization failures.
     - `[DEAD-END]`: Broken links, missing redirects, unclickable buttons.
     - `[UX/SIGNAGE]`: Visual inconsistencies, missing icon labels, alignment issues.

- [ ] **Step 2: Correlate server logs and identify root-cause files**
  1. For each `[CRASH]` or `[LOGIC]` defect, search `logs/crawl/api.log` and `logs/crawl/mvc.log` for the exact stack trace and timestamp.
  2. Query `graphify` (e.g. `graphify query "<failing symbol>"`) to trace affected controllers, services, and database triggers.
  3. Pinpoint suspected file and line number.

- [ ] **Step 3: Deduplicate against existing open/closed issues**

Run: `gh issue list --state all --limit 50`  
Filter out any defects already tracked.

- [ ] **Step 4: Format and file GitHub issues**
  1. For each distinct `[CRASH]` or `[LOGIC]` defect, draft an issue body using `.github/ISSUE_TEMPLATE/crawl-finding.md`:
     - Exact reproduction steps (account, URL, input data).
     - Expected behavior (citing `AGENTS.md`, `PRODUCT.md`, or `CONTEXT.md`) vs Actual behavior.
     - Server stack trace / console errors.
     - Suspected code location and architectural constraints.
     - Acceptance criteria.
  2. Create issue via `gh issue create`:
     ```bash
     gh issue create --title "[CRASH] Loop 1: <Short description>" \
       --label bug --label ready-for-agent --body-file scratch/issue-<slug>.md
     ```
  3. For `[UX/SIGNAGE]` findings, consolidate by loop into a single enhancement issue per loop (`--label enhancement`).

- [ ] **Step 5: Compile Final Triage Summary Report**

Write `scratch/triage-summary.md` listing all created issues, categorized by loop and priority.

---

### Task 10: Page Object Model Refactoring & C# Playwright E2E Suite Expansion

**Files:**
- Create: `tests/WebMVC.E2ETests/Pages/BasePage.cs`
- Create: `tests/WebMVC.E2ETests/Pages/LoginPage.cs`
- Create: `tests/WebMVC.E2ETests/Pages/ExamStudioPage.cs`
- Create: `tests/WebMVC.E2ETests/Pages/DeckPlayerPage.cs`
- Create: `tests/WebMVC.E2ETests/Pages/CheckoutPage.cs`
- Create: `tests/WebMVC.E2ETests/Pages/PlacementPage.cs`
- Modify: `tests/WebMVC.E2ETests/PlaywrightTestBase.cs`
- Test: `tests/WebMVC.E2ETests/Journeys/*.cs`

**Interfaces:**
- Consumes: Running WebMVC (http://localhost:5000), running WebAPI (http://localhost:5001)
- Produces: Clean, reusable Page Object Models and green automated E2E tests for verified core journeys

- [ ] **Step 1: Create `BasePage.cs` with resilient locator helpers**
  - Implement Page Object Model base class wrapping `IPage`.
  - Provide helper methods: `NavigateAsync(string path)`, `WaitForPageReadyAsync()`, `GetPlacardTextAsync()`.
  - Enforce resilient accessibility selectors (`getByRole`, `getByLabel`, `getByTestId`).

- [ ] **Step 2: Create Page Objects for primary user journeys**
  - `LoginPage.cs`: Encapsulate email, password, submit button, error placards.
  - `ExamStudioPage.cs`: Encapsulate curriculum rail, question group cards, audio slicer controls, publish readiness button.
  - `DeckPlayerPage.cs`: Encapsulate card navigation (next/prev), flip action, micro-check quiz options and validation placards.
  - `CheckoutPage.cs`: Encapsulate package selection, mock gateway controls, wallet balance badge.
  - `PlacementPage.cs`: Encapsulate placement start, question sitting, target band selection dropdown.

- [ ] **Step 3: Refactor existing E2E journeys to use Page Objects**
  - Update `AuthenticationTests.cs` to use `LoginPage`.
  - Update `AdminExamStudioTests.cs` to use `ExamStudioPage`.
  - Update `GuidedLearningDeckTests.cs` to use `DeckPlayerPage`.

- [ ] **Step 4: Add new E2E journey test: PlacementToLearningPathTests.cs**
  - Write test verifying fresh learner takes free placement test, saves target band, and arrives at auto-generated learning path.
  - Assert credit balance remains 0 (free loop).

- [ ] **Step 5: Run Playwright test suite against crawl environment**

Run: `dotnet test tests/WebMVC.E2ETests/WebMVC.E2ETests.csproj --no-build`  
Expected: All automated journey tests PASS against `http://localhost:5000`.

- [ ] **Step 6: Commit Playwright E2E suite enhancements**

```bash
git add tests/WebMVC.E2ETests/
git commit -m "feat(test): implement Page Object Models and expand Playwright E2E journey suite"
```
