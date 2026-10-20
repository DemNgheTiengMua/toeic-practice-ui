# Cross-Role Defect Remediation Specification

## Problem Statement

Users across multiple roles (learners, exam authors, administrators, and developers) experience operational friction caused by four distinct defects in routing, client-side media ingestion, role-boundary navigation, and test harness execution:

1. **Learner Mission 404 Failure**: On the learner dashboard, clicking the "Start speaking" or "Start writing" action on the Today's Mission card directs the browser to a non-existent route (`/{skillPrefix}/instructions/{id}` instead of `/{skillPrefix}/{id}/instructions`), producing an HTTP 404 error and blocking the candidate from starting their scheduled free-response practice.
2. **Exam Studio Audio Duration Defaulting**: In the Admin Exam Studio, uploading an audio track for a listening section almost always records a duration of 300 seconds in the database rather than the audio file's actual duration. The browser's media metadata decoding is asynchronous and unawaited, racing ahead to dispatch the upload before the duration is calculated.
3. **Admin Role Boundary Navigation Leaks**: When an administrator visits public pages linked from the admin sidebar (such as Pricing, Certificates, or the landing page), the interface renders action buttons and forms meant exclusively for learners (e.g. "Choose this package" checkout buttons, "Switch active certificate" forms, and footer "Dashboard" links). Clicking these controls triggers HTTP 403 / redirection to `/auth/denied` because the underlying controllers strictly enforce the learner policy.
4. **Seed Test Database Collisions**: In parallel test execution environments (such as multiple git worktrees or custom CI runners configured with `TEST_DB_NAME`), the database seed test uses a rigid string replacement looking for a fixed default database name. When a custom database name is supplied, the replacement silently fails, causing the test to execute against the shared primary test database rather than the isolated instance.

## Solution

1. **Canonical Mission Routing**:
   - Align the mission step generator in the learner service with the canonical WebMVC routing convention (`/{skillPrefix}/{id}/instructions`).
   - Update unit tests in the service test suite to enforce this canonical route structure and prevent regressions.

2. **Asynchronous Audio Metadata Awaiting in Studio**:
   - Wrap the client-side audio metadata probe into an explicit promise with fallback timeout and proper object URL memory deallocation.
   - Ensure the media upload workflow awaits metadata resolution before dispatching the section audio record to the backend, reliably persisting the actual duration in seconds.

3. **Role-Aware Public Navigation**:
   - Make public views role-aware so that signed-in administrators see appropriate administrative pathways instead of learner-exclusive transaction triggers.
   - On the pricing page, provide administrators with a direct link to package management in the admin console rather than learner checkout links.
   - On the landing page footer, direct signed-in administrators to `/admin` and learners to `/dashboard`.
   - On the certificate catalogue, render the active certificate switch form only for learner accounts.

4. **Structured Connection String Construction in Test Harness**:
   - Replace brittle string replacements in database seed tests with structured connection string builder operations, ensuring dynamic database names configured via environment variables are reliably targeted without test collision.

## User Stories

1. As a learner viewing Today's Mission on my dashboard, I want the "Start speaking" button to navigate to `/speaking/{id}/instructions`, so that I can review practice guidelines and begin my speaking exam attempt without an HTTP 404 error.
2. As a learner viewing Today's Mission on my dashboard, I want the "Start writing" button to navigate to `/writing/{id}/instructions`, so that I can begin my writing practice smoothly.
3. As a learner navigating between learning path steps, dashboard missions, and attempt review screens, I want consistent and predictable URL structures across all free-response skills.
4. As an exam author uploading section audio in Exam Studio, I want the true duration of the audio track to be persisted in the database, so that the audio player time ruler and slice markers match the actual recording.
5. As an exam author, I want audio file metadata extraction to be awaited before the section audio details are submitted, so that my tracks are not defaulted to 300 seconds.
6. As an exam author, I want audio metadata probing to include a graceful timeout fallback, so that corrupt or unparseable audio files do not hang the upload workflow indefinitely.
7. As an exam author, I want temporary object URLs created during audio inspection to be promptly revoked, so that browser memory remains clean during long authoring sessions.
8. As an administrator browsing the public pricing page from the admin sidebar, I want package cards to display an administrative management shortcut rather than learner checkout buttons, so that I do not trigger an access-denied error.
9. As an administrator viewing the landing page, I want the footer "Dashboard" link to direct me to `/admin`, so that I can return to my workspace directly.
10. As an administrator browsing the certificate catalogue, I want to review available certificates without seeing "Switch to {Code}" forms that belong to learner profiles, so that I do not submit unauthorized session modifications.
11. As an administrator who follows navigation links throughout the application, I want all visible links to respect my administrative role, so that I never encounter accidental `/auth/denied` redirections.
12. As a candidate taking a listening practice session or mock exam, I want the audio track to reflect its genuine duration, so that playback controls accurately represent the progress of the recording.
13. As a developer running integration tests in a dedicated git worktree with `TEST_DB_NAME` set, I want `DemoSeedTests` to target the specified test database, so that my tests run in total isolation.
14. As a continuous integration system running parallel test suites, I want database connection manipulation to use structured connection builders, so that tests do not bleed across database boundaries.
15. As a developer modifying learner mission generation, I want unit tests to strictly validate external route contracts, so that URL discrepancies are caught before reaching deployment.
16. As an exam author editing listening question groups, I want question group audio slices to be bounded by the real duration of the uploaded master audio, so that slice timestamps remain mathematically valid.
17. As a learner purchasing credit packages, I want checkout links to remain reserved for learner accounts, ensuring billing and ledger transactions are consistently associated with valid student wallets.
18. As a learner switching active certificates from the catalogue, I want the action to update my learner session and return me to my dashboard, without exposing this mechanism to administrative users.
19. As a developer executing the local development crawl environment, I want all cross-role journeys to navigate without access control errors, so that automated health checks pass cleanly.
20. As an operations engineer monitoring web application logs, I want to eliminate spurious HTTP 404 and HTTP 403 warning entries caused by malformed internal navigation links, so that real system errors stand out clearly.

## Implementation Decisions

- **Canonical URL Pattern Alignment**:
  - The mission step generator in the learner service will output `ActionUrl: $"/{skillPrefix}/{publishedSkillExam.ExamId}/instructions"`.
  - Service unit tests asserting mission step URLs will be updated to reflect the canonical route pattern, synchronizing test expectations with the MVC presentation layer.

- **Asynchronous Audio Probe with Memory Deallocation**:
  - The client-side audio upload function in Exam Studio will encapsulate audio duration extraction inside a Promise.
  - The probe will attach `loadedmetadata` and `error` listeners to an off-DOM `Audio` element, enforce a safety timeout (e.g. 3000ms), and invoke `URL.revokeObjectURL` upon completion or failure.
  - The upload pipeline will await this promise before posting the section audio payload to the server.

- **Role-Gated Presentation in Public Views**:
  - In the pricing view, package action buttons will check the current user's role:
    - Learners and unauthenticated guests receive the standard checkout link (`/checkout?packageId=...`).
    - Administrators receive a link directing them to the package administration panel (`/admin/packages`).
  - In the landing page footer, the dashboard navigation anchor will conditionally route to `/admin` when `CurrentUser.IsAdmin` is true, matching the logic already present in the top navigation bar.
  - In the certificate catalogue, the "Switch to {Code}" form will be gated to render only for authenticated learners, preventing administrative sessions from posting to learner-only profile endpoints.

- **Structured Database Connection Construction**:
  - In the demo seed test suite, connection strings for isolated test databases will be assembled using `SqlConnectionStringBuilder` targeting `InitialCatalog = dbName`, completely eliminating string pattern matching against fixed database names.

## Testing Decisions

- **Good Test Criteria**:
  - Tests must assert observable external contracts: HTTP status codes, resolved URLs, role-based DOM visibility, and persisted entity attributes.
  - Tests must avoid coupling to private implementation details or internal variable naming.

- **Testing Seams**:
  - **Service Seam (`tests/Services.Tests`)**:
    - `LearnerServiceTests.Mission.cs`: Verify that `GetDashboardAsync` returns `ActionUrl` matching `/{skillPrefix}/{id}/instructions` for both speaking and writing practice missions.
    - `DemoSeedTests.cs`: Verify that seed idempotency and database invariant assertions execute cleanly when `TEST_DB_NAME` is set to an arbitrary custom identifier.
  - **WebMVC Seam (`tests/WebMVC.Tests` & `tests/WebMVC.E2ETests`)**:
    - Controller / View Tests: Verify that an administrative session accessing `/pricing`, `/`, and `/certificates` does not receive links or forms pointing to `/checkout`, `/dashboard`, or `/learner/active-certificate`.
    - End-to-End Studio Tests: Using Playwright in `WebMVC.E2ETests.Journeys.AdminExamStudioTests`, upload a sample audio file with a known length (e.g. 12 seconds) and assert that `ExamSectionAudio.DurationSec` in the database matches the actual audio length rather than 300.

- **Prior Art**:
  - `tests/Services.Tests/LearnerServiceTests.Mission.cs`
  - `tests/Services.Tests/DemoSeedTests.cs`
  - `tests/WebMVC.E2ETests/Journeys/AdminExamStudioTests.cs`
  - `tests/WebMVC.Tests/PricingControllerTests.cs`

## Out of Scope

- Modifying the AI grading Claude proxy model identifier (`claude-sonnet-3.7`), which is validated and required by the upstream HH-Tech proxy gateway.
- Redesigning the pricing card visual layout or introducing client-side cart mechanics.
- Altering the database schema or check constraints for `ExamSectionAudio` or `CreditTransactions`.

## Further Notes

- All changes maintain strict compatibility with existing database-first triggers, ledger invariants, and certificate isolation rules.
