# Full Frontend Port to React and HeroUI v3, Preserving Domain Invariants and Retiring the Legacy Server-Rendered Web App

**Status:** ready-for-agent
**Date:** 2026-11-13
**ADRs touched:** ADR-0001 (database-first), ADR-0002 (superseded by ADR-0015), ADR-0008 (AI grading seam), ADR-0010 (superseded by ADR-0015), ADR-0012 (one writer per derived fact), ADR-0013 (live dashboard revision), ADR-0014 (net revenue), ADR-0015 (React + HeroUI v3 SPA & dual cookie/bearer JWT)

---

## Problem Statement

The legacy server-rendered web application has become a source of constant user-visible defects, layout shifts, and maintenance friction across every role in the product.

**The exam sitting room is fragile and fights the learner.** Switching between questions during a placement attempt or a mock attempt swaps server-rendered HTML fragments and replays entrance animations on the progress bar every single time. Audio players and microphone states are torn down or desynchronized between transitions. On speaking items inside a placement sitting, a template bug rendered a writing text box instead of a microphone recorder, and the forward navigation rule blocked the learner from advancing to the next question. Meanwhile, legacy client scripts intercept standard browser keys with intrusive fake-proctoring popups, and a missing or denied microphone locks the speaking screen with no way to recover.

**Every interaction pays a double-hop tax.** Even the simplest learner or staff action requires a server-rendered controller, a view model, a template, a client script, and a forwarding proxy endpoint that exists only to relay requests to the backend API. When client scripts and server templates disagree on state — such as URL query parameters, timer countdowns, or offline answer retries — the learner sees stale UI or loses their place.

**Staff administration is scattered across eighteen disconnected screens.** Authoring a single exam requires jumping across separate pages for the exam header, sections, score scales, score bands, and question roster. Managing the question bank, pool imports, lessons, prompts, orders, refunds, complaints, and user accounts requires constant full-page postbacks and manual cross-referencing.

**Hand-rolled styling primitives lack consistency and accessibility.** Custom modals, dropdowns, tabs, and form controls lack unified focus management, keyboard navigation, and validation states, resulting in visual jitter and inconsistent spacing across pages.

---

## Solution

**Replace the legacy server-rendered web application 100% with a single-page application built on React 19, HeroUI v3 compound components, and Tailwind CSS v4, communicating with the existing backend API through same-origin requests authenticated by an `HttpOnly` session cookie.**

**The backend and domain services remain authoritative and unbroken.** The database schema, triggers, views, domain services, credit ledger, grading queue worker, and API endpoints stay intact. The API authentication boundary is extended so that sign-in and registration issue an `HttpOnly; SameSite=Lax` cookie alongside the existing JSON bearer token response, allowing the single-page application to authenticate automatically without exposing tokens to browser scripts while keeping all existing bearer-token clients, API documentation tools, and automated test suites working unchanged.

**Visual design adopts the HeroUI v3 default theme paired with the four signage brand hues.** The interface uses HeroUI v3 compound components, semantic surface tokens, an 8px base radius, surface and overlay shadows, sentence-case headings, and tabular monospace numerals for timers, scores, and currency, while preserving Sign Blue for primary actions and reached bands, Sign Green for positive outcomes, Sign Gold for warnings and flagged items, and Sign Red for danger, refusals, and negative net revenue.

**Every vertical slice is ported by reading the legacy behavior first, evaluating what to keep versus what to improve, building the new slice, verifying it, and deleting the old code.** All domain invariants, security guards, and sitting edge cases are preserved, while full-page reloads, animation replays, fake-proctoring traps, microphone deadlocks, and fragmented admin screens are eliminated. Once all slices are complete, the legacy web project and its Razor test project are removed completely and the new application takes over port 5000.

---

## User Stories

### Foundation, Theme, and Role-Aware Shell

1. As a visitor or signed-in user, I want the application shell to render immediately with calm surface hierarchy, consistent spacing, and visible keyboard focus rings, so that every screen feels cohesive and accessible.
2. As a user, I want to switch between light mode and dark mode with semantic surfaces adapting automatically while brand status colors keep their meaning, so that I can study comfortably in any lighting.
3. As a learner, I want my session to be maintained via an `HttpOnly` cookie without any raw token stored in browser storage, so that my account is protected against script injection.
4. As an automated test runner or API documentation reviewer, I want the API to continue accepting `Authorization: Bearer` tokens alongside the session cookie, so that existing API tests and one-click documentation sign-in continue working without modification.
5. As a signed-in user whose session expires, I want any unauthorized API response to clear my client session state and redirect me to sign-in with my current local path preserved as the return URL, so that I can sign back in and resume where I left off.
6. As a learner, instructor, support agent, or administrator, I want the top navigation bar to expose only the workspaces my capabilities permit, so that staff and learner tools are cleanly separated without hiding support or instructor tools behind an admin-only check.

### Public Catalog, Authentication, and Account Recovery

7. As a visitor on the landing page, I want to see the supported certificates, how the diagnostic loop works, and direct calls to action to explore certificates or start a free placement attempt, so that I understand the product immediately.
8. As a visitor browsing the certificate catalog, I want to view each certificate's skills, sections, scoring scale, CEFR bands, and available mock exams, so that I can pick the certificate that matches my goal.
9. As a visitor on the pricing page, I want to see credit packages in Vietnamese đồng alongside what credits buy (mock attempts, speaking and writing gradings, mentor sessions, and placement retakes) and what is always free (first placement attempt per certificate, practice sessions, and weakness analysis), so that pricing is transparent before I register.
10. As a visitor signing in or registering, I want my `returnUrl` parameter validated as a strict local path before redirecting, so that a crafted link can never redirect me to an external site.
11. As a user signing in or registering, I want validation errors, invalid credentials, and rate-limit refusals displayed in an accessible alert banner without losing the form fields I already typed, so that I can correct mistakes quickly.
12. As a user following an email verification link, I want the verification page to ask for my explicit button click before submitting the verification request, so that an automated email link scanner cannot consume my one-time token on a background GET request.
13. As a user who forgot my password, I want to request a reset link and set a new password with clear confirmation feedback, so that I can recover my account without contacting support.

### Unified Exam Sitting Engine and Placement Flow

14. As a learner who has never sat a placement attempt for a certificate, I want to see "Start placement test" at zero credit cost and choose between measuring all four skills or Listening and Reading only, so that my first diagnostic is free and scoped to what I want to measure.
15. As a learner who already has a placement band for a certificate, I want to see "Retake placement test" and be taken to a confirmation screen showing both scopes, their exact credit prices (5 credits for all four skills, 3 credits for Listening and Reading only), the skills excluded by the narrower scope, and my current credit balance, so that I never spend credits by accident.
16. As a learner on the placement retake confirmation screen who does not have enough credits for the chosen scope, I want the start action blocked before any attempt is created and a direct link to top up my wallet, so that "block before you charge" is respected.
17. As a learner who already has an in-progress placement attempt, I want visiting the placement page to route me to a resume choice screen without charging me again, so that I can either resume at my saved question or abandon and restart cleanly.
18. As a learner reloading the placement sitting page in my browser, I want the reload to be a pure read that restores my current question, timer, and saved answers without advancing my question index or locking my current section, so that a browser refresh never penalizes me.
19. As a learner switching between questions inside a placement or mock attempt, I want the question content to swap instantaneously in client state while the progress bar width transitions smoothly without replaying an entrance animation, so that navigation feels steady and distraction-free.
20. As a learner tampering with the `?q=` question number in the browser address bar during a placement sitting, I want the sitting engine to reconcile the requested index against locked or unstarted sections, clamp it to the legal question range, and update the URL in place, so that section-locking rules cannot be bypassed via the URL.
21. As a learner answering an objective multiple-choice item in a sitting, I want selecting an option to update my answer state immediately and save asynchronously in the background, so that I never wait on a spinner between questions.
22. As a learner on a writing free-response item in a sitting, I want a multi-line text area with a live tabular word and character counter and automatic draft saving, so that my essay is preserved as I write.
23. As a learner on a speaking free-response item in a sitting, I want a microphone recorder with preparation and recording countdowns, live recording status, audio upload, and a "Record again" option — never a writing text box — and I want the "Next question" button to advance cleanly to the next speaking item or section, so that speaking tasks work naturally.
24. As a learner whose microphone stream is interrupted by a tab switch, page refresh, or hardware hiccup during a speaking item, I want the recorder to reset cleanly to a "Record again" state rather than locking the question, so that I can complete my recording.
25. As a learner whose network connection drops briefly while answering questions in a sitting, I want failed answer saves to be queued locally in my browser with a visible offline notice and flushed automatically when connectivity returns or before final submission, so that no answer is lost to a transient network blip.
26. As a learner clicking "Submit attempt", I want a confirmation modal that lists every section with its answered-versus-total count and highlights any section that still has unanswered questions, so that I can review gaps before finalizing my sitting.
27. As a learner who just submitted a placement attempt that includes speaking or writing items, I want the result page to poll automatically while AI grading is in progress and transition smoothly to the finished band report without manual reloads.
28. As a learner viewing a placement result where only two skills were chosen or where an AI grading item failed or timed out, I want unmeasured skills labeled explicitly as unmeasured — never shown as a zero score — and a clear notice explaining when a partial result occurred, so that the report is honest about what was measured.

### Mock Exams Catalog, Sitting, and Review

29. As a learner browsing mock exams, I want to filter exams by certificate and see each exam's duration, question count, section breakdown, and credit cost, so that I can choose the right mock paper.
30. As a learner starting a mock exam, I want to choose between sitting the full exam or a single section, see my current credit balance beside the cost, and be blocked with a link to the wallet if my balance is too low, so that I know the exact charge upfront.
31. As a learner sitting a timed mock exam, I want a tabular countdown timer in the sitting header that automatically submits my attempt when time reaches zero, so that my answers are scored even if I run out of time.
32. As a learner sitting a mock exam, I want to flag questions for review, jump freely across questions within open sections via the question palette, and use optional keyboard shortcuts without fake-proctoring alert popups hijacking my browser keys.
33. As a learner viewing a completed mock attempt result, I want to see my raw score, scaled score, reached CEFR band, and per-section accuracy bars, so that I know how I performed on each skill.
34. As a learner reviewing a completed mock attempt, I want to inspect every question alongside my selected answer, the correct answer, the item explanation, and any AI rubric feedback for free-response tasks, so that I can learn from my mistakes.

### Free Practice Sessions and Saved Questions

35. As a learner configuring a free practice session, I want to select my certificate, skill, section, topic, CEFR difficulty level, and question count at zero credit cost, so that I can drill weak areas whenever I want.
36. As a learner inside a practice session, I want to see reading passages or listening audio beside the questions, check my answer immediately with clear visual feedback, and read the explanation right away, so that practice reinforces learning on every item.
37. As a learner stuck on a practice question, I want to request an on-demand AI hint inside a popover without leaving the question, so that I get a nudge toward the answer before guessing.
38. As a learner in a practice session or exam review, I want to bookmark any question with an optional personal note into my saved questions bank, so that I can revisit tricky items later.
39. As a learner browsing my saved questions bank, I want to filter, paginate, review explanations, and remove items I have mastered, so that my review list stays focused.

### Productive Skills: Speaking and Writing Practice with AI Grading

40. As a learner opening the speaking or writing practice studio, I want to browse prompts by certificate, task type, and CEFR level and see the credit cost of an AI grading before starting an attempt.
41. As a learner in the speaking studio whose computer has no microphone or whose browser denied microphone permission, I want a clear, non-blocking alert with a microphone device selector and a retry action rather than a broken screen, so that I can connect a headset and continue.
42. As a learner submitting a speaking or writing practice attempt, I want the result page to display the asynchronous grading status (`queued`, `processing`, `completed`, `failed`) and, once complete, render the overall band, per-criterion rubric scores, pronunciation and fluency metrics, and annotated feedback.

### Learner Hub, Guided Learning Path, Lessons, and Weakness Analysis

43. As a learner on my dashboard hub, I want to see my active certificate, current CEFR band per skill, next recommended learning path step, recent attempts, and wallet balance in one view, and switch my active certificate at any time.
44. As a learner following my guided learning path, I want to see my ordered path steps, launch directly into the linked lesson or step practice session, or mark a step skipped or completed, so that my study plan adapts as I progress.
45. As a learner reading a micro-lesson, I want clean typography, structured examples, and a direct button to practice the lesson's topic or return to my learning path.
46. As a learner viewing my weakness analysis, I want to see my deterministic accuracy by section and by topic alongside an on-demand AI insight narrative that summarizes my patterns and recommends what to practice next.

### Wallet, Checkout, Mentors, Profile, Notifications, and Support

47. As a learner in my wallet and billing workspace, I want to switch via tabs between buying credit packages, viewing my append-only credit ledger history, and inspecting my past orders and receipts, so that every credit movement and payment is transparent.
48. As a learner purchasing a credit package, I want to see the payment QR/checkout instructions — or, when running against the mock gateway in development and review environments, trigger simulated payment or cancellation with one click — and see my credit balance update immediately.
49. As a learner browsing mentors, I want to view mentor profiles, specialties, and available slots, book a session using credits, and join completed or upcoming sessions via an external meeting link that is strictly validated to allow only `http:` or `https:` URLs.
50. As an instructor, I want a dedicated sessions workspace to view my upcoming and past learner bookings, update meeting links, and mark sessions completed.
51. As a learner with a billing or technical issue, I want to submit a support complaint and track its status and staff response from my support page.
52. As a signed-in user, I want to view and mark notifications as read and update my profile details and password from a clean account settings page.

### Consolidated Admin Studio Workspaces

53. As a staff member opening the admin overview dashboard, I want to see today's net revenue, this month's net revenue (cut at UTC+7 and rendered in danger red with a true minus sign when negative), refunded credits, active learners, and open queues, and I want those numbers to update in place automatically when the backend revision changes without reloading the page or replaying animations.
54. As an administrator in the People workspace, I want to search users, change roles, grant administrative credits with a mandatory reason note, and inspect the immutable audit log from a single tabbed screen.
55. As an administrator in the Catalog, Exams, and Question Bank Studio, I want to manage certificates, author exams with their sections, score scales, score bands, item rosters, and publish readiness checks in one tabbed exam workspace, and manage pool questions, JSON manifest imports, lessons, and AI prompts without jumping across nine separate URLs.
56. As a support agent, accountant, or administrator in the Finance and Operations Desk, I want to filter orders, record money or credit refunds (enforcing order ceilings and no-order ceilings), approve or reject refunds with a reason, mark money refunds settled on their settlement date, export approved money refunds as an Excel-compatible UTF-8 BOM CSV, resolve support complaints, retry or override AI gradings, and adjust system settings.

### Agent Slice Execution and Final Cutover

57. As a maintainer, I want every vertical slice ticket to require the implementing agent to inspect the dependency graph and read the legacy controllers, templates, scripts, and tests first, evaluate what domain and edge-case behaviors to keep versus what UX flaws to improve with HeroUI v3, build and test the React slice, and delete the retired legacy files in that slice.
58. As a teammate or course evaluator, I want the final cutover to delete the legacy server-rendered web project and its test project completely, bind the React application to port 5000, and keep the single-command manual seed script, crawl environment script, and container compose setup working out of the box.

---

## Implementation Decisions

### Dual Cookie and Bearer Token API Authentication

- The backend API remains the single backend entry point on port 5001, and no CORS policy is opened. All browser requests from the single-page application on port 5000 reach the API through a same-origin `/api/*` reverse proxy (via the dev/preview server proxy in local development and the web container reverse proxy in Docker).
- Sign-in and registration endpoints continue returning the existing JSON token payload for bearer-token clients while also appending an `HttpOnly; SameSite=Lax` cookie carrying the JWT.
- The JWT bearer authentication middleware inspects the `Authorization` header first; when absent, it falls back to reading the token from the `HttpOnly` cookie.
- Signing out clears the `HttpOnly` cookie immediately alongside recording any token revocation.
- Browser JavaScript never reads, parses, or stores raw JWT strings in `localStorage` or `sessionStorage`. Client session hydration relies on calling the current-user profile endpoint with credentials included.

### Visual System: HeroUI v3 Default Theme with Signage Brand Hues

- The application uses React 19, HeroUI v3 compound components (`@heroui/react` and `@heroui/styles`), and Tailwind CSS v4.
- HeroUI v3's default theme variables provide the structural foundation: `--radius: 0.5rem` (8px base radius), `--field-radius: calc(var(--radius) * 1.5)`, `shadow-surface` for cards, `shadow-overlay` for dialogs and popovers, and `oklch` light and dark mode canvas and surface tokens (`bg-background`, `text-foreground`, `bg-surface`, `bg-surface-secondary`, `bg-overlay`, `border-border`, `border-separator`).
- The four semantic status and brand variables are overridden in both light and dark modes with the product's signage hues:
  - `--accent`: `#0b4ea2` (Sign Blue — primary actions, focus rings, reached CEFR band)
  - `--success`: `#0a6b3c` (Sign Green — open states, positive outcomes, correct answers)
  - `--warning`: `#f2b705` (Sign Gold — caution, flagged questions, text highlights)
  - `--danger`: `#b31217` (Sign Red — errors, refusals, destructive actions, negative net revenue)
- Typography pairs `IBM Plex Sans` for body and controls, `IBM Plex Sans Condensed` for display titles and badges, and `IBM Plex Mono` (`tabular-nums`) for timers, scores, counters, and currency figures. Headings use sentence case.
- Only HeroUI v3 compound component patterns are permitted; legacy HeroUI v2 flat-prop patterns are forbidden. Every implementing agent must consult the local HeroUI v3 documentation index and the `heroui-react` MCP server before building components.

### Client Security and Capability Invariants

- **Open-redirect guard:** Any `returnUrl` query parameter on authentication screens is validated by a pure helper that accepts only single-slash local paths (beginning with `/` and not `//` or `/\`) and falls back to `/dashboard`.
- **External link sanitizer:** User- or mentor-supplied external links (such as mentor session meeting URLs) are parsed and rendered as clickable anchors only when the protocol is `https:` or `http:`; `javascript:`, `data:`, and `vbscript:` schemes are stripped and rendered as inert text.
- **Anti-prefetch email verification:** The email verification route never mutates state on mount; it renders a confirmation card and posts the verification token only when the user activates the confirm button.
- **Staff capability gates:** Navigation items and route guards use the six capability predicates (`canViewDashboard`, `canManageUsers`, `canManageCatalog`, `canManageBilling`, `canTriageSupport`, `canViewInstructorSessions`) rather than checking for the administrator role directly, preserving exact access for support agents and instructors.

### Unified Sitting Engine for Placement and Mock Attempts

- Placement attempts and mock attempts share a single client-side sitting shell driven by React state rather than HTML fragment replacement.
- **Pure-read reload and resume choice:** Loading the sitting route performs a read-only fetch of the sitting snapshot and restores the active question, section state, and timer. Visiting the placement landing screen when an in-progress attempt exists redirects to a dedicated resume screen offering a choice between resuming at the saved question index or abandoning the sitting to start fresh.
- **Question index reconciliation:** Whenever the URL query parameter `?q=` points to a closed section, an unstarted section in a section-locked sitting, or an index outside the paper's bounds, the sitting shell clamps the active index to the legal range and reconciles the browser URL with `replaceState`.
- **Modality-aware response controls:**
  - Objective items with options render an accessible radio group.
  - Writing free-response items render a text area with a live tabular word and character counter and debounced draft persistence.
  - Speaking free-response items render a microphone recorder with preparation and recording timers, audio blob upload, a clean reset to "Record again" if recording is interrupted, and unlocked forward navigation to the next speaking item.
- **Offline answer queue:** Answer saves that fail due to network errors are persisted to a local browser queue keyed by attempt and question, surfaced via a non-intrusive warning banner, and flushed automatically when connectivity returns or before submitting the attempt.
- **Per-section submit confirmation modal:** Activating the submit button opens a HeroUI modal summarizing answered versus total questions for every section and highlighting any section with unanswered questions before posting the submission.
- **Grading poll and partial results:** While a submitted placement attempt has status `grading`, the result view polls every 3 seconds. When a result is scoped to two skills or when free-response grading fails or times out, unmeasured skills are displayed with an explicit unmeasured badge rather than zero, accompanied by a warning alert when grading was incomplete.

### Consolidating Admin Screens into Three Studio Workspaces

- The eighteen legacy admin controllers are consolidated into three cohesive workspaces built with HeroUI `Tabs`, `Table`, `Modal`, `Drawer`, `AlertDialog`, and `Select`:
  1. **Overview & People (`/admin`, `/admin/users`):** Polls the lightweight dashboard revision endpoint every 5 seconds and refreshes dashboard metrics in place only when the revision tuple changes, preserving scroll position and avoiding animation replay. Renders net revenue for today and the current month (UTC+7 boundary) using a currency formatter that outputs a true minus sign in Sign Red when net revenue is negative. Combines user search, role assignment, credit grants, and the audit log into one tabbed workspace.
  2. **Catalog, Exams & Question Bank Studio (`/admin/certificates`, `/admin/exams`, `/admin/question-bank`):** Unifies exam metadata, sections, score scales, score bands, item roster selection, and publish-gate validation into a single tabbed exam studio, and groups pool items, JSON manifest import, lessons, and AI prompt templates into a single question bank studio.
  3. **Finance, Refunds & Operations Desk (`/admin/finance`, `/admin/operations`):** Groups orders, money and credit refund workflows (request, approve, reject, settle, and UTF-8 BOM CSV export), support complaint resolution, AI grading queue inspection/retry/override, speaking configuration, and system settings.

### Mandatory MCP & Local Docs Workflow for Speed and Accuracy

Every agent executing a vertical slice under this specification must use the workspace's MCP servers and local indexes rather than guessing APIs or running broad text searches:

- **`heroui-react` MCP + `./.heroui-docs/react` (UI Components & Theme Tokens):**
  - Before writing or modifying any UI component, check the `[HeroUI React v3 Docs Index]` in `AGENTS.md` (`./.heroui-docs/react/components/...` and `./.heroui-docs/react/demos/en/...`) or call `heroui-react` MCP (`list_components` → `get_component_docs` → `get_theme_variables`).
  - Use the returned v3.2.7 compound anatomy (`Card.Header`, `Card.Content`, `Modal.Backdrop`, `Modal.Container`, `Modal.Dialog`, `Tabs.List`, `Tabs.Tab`, `Tabs.Panel`, `Select`, `Table`, `AlertDialog`, `Form`, `TextField`, `NumberField`) directly on the first pass — eliminating trial-and-error TypeScript failures from hallucinated HeroUI v2 props.
- **`graphify` CLI / MCP (Dependency & Deletion Impact in One Call):**
  - Before reading or deleting any legacy controller, view model, or service seam, run `graphify explain "<path>::<Symbol>"` (or `get_node` / `get_neighbors` via the `graphify` MCP) followed by `graphify affected <id> --depth 2`.
  - Use the returned `file:line` callers to locate every connected view, script, and test immediately without wide grep scans.
- **`context7` MCP (Framework & Library Docs):**
  - Call `resolve-library-id` followed by `query-docs` whenever checking React Router, Tailwind CSS v4, Vite, Vitest, or ASP.NET Core configuration syntax.
- **`playwright` MCP (Live Browser Verification):**
  - Use `browser_navigate`, `browser_snapshot`, `browser_click`, `browser_console_messages`, and `browser_network_requests` to verify rendered slices and catch runtime console or HTTP errors in one pass.

### Slice-by-Slice Legacy Deletion and Port 5000 Cutover

- Each vertical slice ticket is responsible for reading its legacy controller, views, scripts, and tests, building and verifying the replacement routes in the React application, and deleting the retired legacy files in that slice.
- In the final cutover slice, the legacy server-rendered project and its test project are removed from the solution and deleted from the repository, the React application is bound to port 5000, and the manual test seed script, crawl environment script, and container compose configuration are updated to run the new frontend alongside the API on port 5001.

---

## Testing Decisions

A good test asserts externally observable behavior — what the user sees, what network request is sent, what URL navigation occurs, or what database state results — and never asserts internal component state names, private helper invocations, or markup implementation details.

1. **Web API Authentication Boundary (`WebAPI.Tests`)**
   - Exercises the real ASP.NET Core HTTP pipeline to verify that sign-in and registration set the `HttpOnly; SameSite=Lax` cookie while still returning the JSON bearer token, that authenticated endpoints accept either `Authorization: Bearer` or the session cookie, and that sign-out expires the cookie.
   - Prior art: existing token validation and session endpoint tests in the API test suite.
2. **Domain and Sitting State Machine Boundary (`Services.Tests`)**
   - Exercises `AttemptEngine`, `PlacementService`, `SpeakingPracticeService`, `MockExamService`, `CreditLedger`, and `AdminDashboardService` against a real SQL Server database created from the schema file per test, verifying speaking free-response navigation, scope-based placement retake charges, pure-read sitting loads, and net revenue calculations.
   - Prior art: `SittingStateMachineTests`, `PlacementResumeReloadTests`, and `PlacementScopeAndRetakeTests`.
3. **SPA Route, Security, and Sitting Harness (Vitest + Testing Library)**
   - Drives route components and the shared sitting shell against a stubbed `/api/*` fetch layer.
   - Cases pinned at this seam:
     - `isLocalPath` and `sanitizeReturnUrl` rejecting protocol-relative, backslash, and external URLs on sign-in and registration.
     - `safeExternalHref` blocking `javascript:` and `data:` URLs on mentor meeting links.
     - Email verification rendering a confirmation button and refusing to POST on initial mount.
     - Staff navigation and workspace guards honoring the six capability predicates across `admin`, `support`, `instructor`, and `learner` roles.
     - Sitting shell reconciling out-of-range or locked-section `?q=` query parameters, rendering the microphone recorder for speaking free-response items, recovering from interrupted recordings, queueing failed answer saves offline and flushing on reconnect, showing per-section unanswered counts in the submit modal, and rendering degraded partial placement results without showing zero for unmeasured skills.
     - Admin live dashboard updating metrics only when the revision tuple changes and formatting negative net revenue with a true minus sign.
   - Prior art: the security boundary, sitting view, and script tests in the retiring MVC test suite.
4. **End-to-End Browser Verification (Playwright)**
   - Walks the running application on port 5000 against the seeded API on port 5001 across public catalog, authentication, placement sitting and retake scope selection, mock exam sitting and review, free practice and saved questions, speaking and writing grading, guided learning path and weakness analysis, wallet checkout and mentor booking, and the three admin workspaces, asserting zero console errors and zero unhandled network failures.

---

## Out of Scope

- Changes to the database schema, SQL triggers, SQL views, or Entity Framework Core entity mappings.
- Changes to backend business rules, scoring formulas, credit ledger constraints, or AI grading queue mechanics beyond the already-landed speaking sitting fix and dual cookie/bearer token support.
- Server-side rendering (SSR) or React Server Components; the frontend is a client-rendered single-page application served over HTTP/HTTPS with same-origin `/api/*` proxying.
- Supporting HeroUI v2 component APIs or retaining any Razor views after the final cutover ticket closes.

---

## Further Notes

**Why every ticket carries a mandatory "Read → Evaluate → Build → Verify → Delete" protocol.** The legacy web app accumulated subtle, hard-won edge cases (such as anti-prefetch email verification, pure-read sitting reloads, offline answer queueing, and degraded partial placement results) alongside broken UX habits (such as full-page reloads, animation replays, and fake-proctoring keyboard traps). Requiring each slice agent to read the legacy code and tests first and explicitly separate domain invariants from Razor warts prevents both regressions and blind copy-pasting.

**Why HeroUI v3 documentation lookup is mandatory.** HeroUI v3 is built on React Aria and Tailwind CSS v4 using compound component parts (`Card.Header`, `Card.Content`, `Modal.Dialog`, `Tabs.List`). Agents relying on pre-trained memory frequently emit HeroUI v2 flat props that do not exist in v3. Both the local `.heroui-docs/react` index in `AGENTS.md` and the `heroui-react` MCP server are available in this workspace and must be queried before writing UI code.
