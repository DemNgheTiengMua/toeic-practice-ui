# ADR-0015: React 19 + HeroUI v3 SPA and Dual Cookie/Bearer JWT API Boundary

## Status
Accepted — Supersedes [ADR-0002](0002-mvc-proxies-api-with-cookie-token.md); updates [ADR-0010](0010-exam-shell-and-client-side-question-swapping.md).

## Context
The Razor (`src/WebMVC`) presentation layer grew to 39 controllers, 172 `.cshtml` views, and 62 vanilla JavaScript files. In practice, this split server-rendered + ad-hoc DOM-patching architecture repeatedly caused production defects:
1. **UI Churn, Layout Shifts & Animation Replays**: Hybrid full-page renders and HTML fragment swaps (`GET /placement/take/{id}/fragment`) caused layout shifts, lost focus, and re-triggered CSS entrance animations on every question switch.
2. **Duplicated & Divergent Sitting Shells**: Placement (`take.js`), Mock Exam (`exam-sitting.js`), Speaking Practice (`speaking/take.js`), and Writing Practice (`writing/take.js`) each maintained separate state machines and markup templates. A fix or modality handler in one sitting (e.g., speaking microphone capture) was missing in another, causing Placement Speaking (`Modality = Speaking`, `QuestionType = "free_response"`) to render a `<textarea>` instead of a voice recorder.
3. **Redundant BFF Boilerplate**: `src/WebMVC` existed largely to hold an `HttpOnly` cookie and forward 239 `src/WebAPI` endpoints through C# controller actions and duplicate ViewModels.

At the same time, the core security invariant from `ADR-0002` remains non-negotiable: **browser JavaScript must never read or store raw JWTs in `localStorage` or `sessionStorage`**, and existing `WebAPI.Tests` / `Services.Tests` that authenticate with `Authorization: Bearer <jwt>` must continue passing unchanged.

## Decision
1. **Replace `src/WebMVC` with a React 19 + HeroUI v3 SPA (`src/web`)**:
   - Stack: **Vite + React 19 + TypeScript + Tailwind CSS v4 + HeroUI v3 (`@heroui/react`, `@heroui/styles`) + TanStack Router + TanStack Query v5 + `openapi-typescript` / `openapi-fetch`** (generated from `WebAPI`'s `/openapi/v1.json`).
   - Serve the SPA on port `5000` (`http://localhost:5000`), replacing `src/WebMVC` on the same port.
2. **Same-Origin `/api` and `/media` Proxy + Dual JWT Resolution in `src/WebAPI`**:
   - The browser communicates on a single origin (`http://localhost:5000`). Vite (in development) and the static/reverse proxy host (in Docker/production) forward `/api/*` and `/media/*` to `src/WebAPI` (`http://localhost:5001`).
   - `POST /api/auth/sign-in` and `POST /api/auth/register` set an `HttpOnly; SameSite=Lax` cookie (`ee_access_token`) containing the signed JWT (pinned to `ExpiresAt`), in addition to returning `AuthTokenResponse` in the JSON body.
   - `POST /api/auth/sign-out` clears the `ee_access_token` cookie.
   - `WebAPI`'s `JwtBearerEvents.OnMessageReceived` resolves the token from `Authorization: Bearer <token>` first (preserving 100% compatibility with `WebAPI.Tests`, Swagger/Scalar, and CLI tools), falling back to `Request.Cookies["ee_access_token"]` when no header is present.
   - Because authentication rides on a same-origin `HttpOnly` cookie, `<audio src="/api/...">`, `<audio src="/media/...">`, and `<img src="/media/...">` stream with `206 Partial Content` range support natively without exposing tokens to JS.
3. **Unified `<SittingWorkspace />` Engine (Updating `ADR-0010`)**:
   - Placement, Mock Exam, Speaking Practice, and Writing Practice sittings share one unified React `<SittingWorkspace />` component tree.
   - The response pane switches strictly on `question.modality` and `question.questionType` (`<ObjectiveQuestionPane />`, `<SpeakingRecorderPane />`, `<WritingComposerPane />`) without unmounting the topbar, timer, audio group player, or navigator.
   - Direct microphone recovery: clicking **"Start recording"** inside `<SpeakingRecorderPane />` invokes `navigator.mediaDevices.getUserMedia({ audio: true })` directly if no live stream is cached (pausing local prep countdown while the browser permission prompt is open), eliminating the `sessionStorage` trap.
   - Hostile fake-proctoring listeners (`F12`, `contextmenu`, `Ctrl+C`, `F5` hijacks, `window.blur` disciplinary warnings) from `ADR-0010` §5 are retired. Real data-safety and exam-rule guards remain: `beforeunload` when saves/uploads are in flight, confirmation modals on one-way skill/unit/group locks and final submission, server-authoritative `DisplayOrder` URL reconciliation, single-play audio state persistence, and offline answer queueing (`PLC-17`).
4. **Execution & Cleanup Protocol**:
   - Every vertical slice follows **Read → Understand → Evaluate (Keep vs. Improve) → Build → Delete Old Razor**: inspect the existing controller/view/JS/test contracts, preserve all domain and edge-case invariants (URL tampering reconciliation, `PLC-14` pure-read reload & resume choice, `PLC-15` interrupted recording retry, `PLC-17` offline queue, `PLC-18` per-section unanswered modal, `PLC-19`/`PLC-35` partial result & free grading retry, open-redirect & `MeetUrl` protocol sanitization), improve UX in HeroUI v3, and remove `src/WebMVC` once all waves are complete.

## Consequences
- **Zero Full-Page Reloads or Animation Replays**: Persistent React layout shells and component state eliminate layout shifting and progress-bar entrance replays.
- **Zero Backend Regression Risk**: `BusinessObjects`, `DataAccess`, `Services`, `Contracts`, and `WebAPI`'s 239 endpoints remain intact; `Services.Tests`, `WebAPI.Tests`, and `docs/schema-tests/run-all.sh` continue unchanged.
- **Dramatically Smaller Surface Area**: Eliminates 39 forwarding controllers, 172 `.cshtml` files, and 62 vanilla JS scripts in favor of typed OpenAPI queries and reusable HeroUI v3 components.
