# Spec: Project-Wide Validation Audit & 4-Tier Defense-in-Depth Hardening

**Status:** ready-for-agent  
**Date:** 2026-11-14  
**ADRs touched:** ADR-0001 (database-first EF Core), ADR-0003 (orders & payment gateways), ADR-0015 (React + HeroUI v3 SPA), ADR-0017 (streamlined direct ownership)

---

```[tasklist]
### Implementation Tasks
- [ ] #325
- [ ] #326
- [ ] #327
- [ ] #328
```

## Problem Statement

The platform currently lacks a cohesive, automated defense-in-depth validation architecture, leading to layout breakage, data corruption risks, and security vulnerabilities:

1. **Unconstrained Text Inputs & UI Layout Breakage**:
   - In user-facing forms such as Profile & Settings, text inputs allow arbitrary lengths and unvalidated characters (e.g., saving a 99-character unbroken string of random keystrokes and numbers like `Demo Learner Meow dsss... 29381...` as a Full Name).
   - The application shell, headers, cards, and tables lack defensive styling (such as text truncation and maximum widths). When an unconstrained or long string is displayed, it stretches across navigation bars and containers, breaking UI alignment and pushing action controls offscreen.

2. **Absence of Automated Minimal API Request Validation**:
   - In ASP.NET Core 10 Minimal APIs, incoming request DTOs are not automatically validated against `.NET DataAnnotations` or validation attributes unless manually handled.
   - Across the backend's 239 endpoint registrations, manual validation was only executed in eight places. The vast majority of endpoints accept raw request objects without verifying required fields, string lengths, numerical bounds, or regex patterns before passing them into domain services.

3. **Loose Domain Invariants on Identity & Formats**:
   - Full names accept numbers, punctuation, HTML markup, and script tags, with arbitrary 120-character database column limits.
   - Phone numbers rely on loose regexes that accept invalid character strings, and timezones are not universally checked against authoritative IANA timezone registries.

4. **Resource Exhaustion & Denial of Service Risks on AI & Media Payloads**:
   - Free-response submissions (writing essays and speaking audio files) are not bounded by strict pre-flight limits before entering asynchronous processing queues.
   - A user could submit massive text payloads (exhausting LLM token windows, inflating API costs, or attempting prompt injections) or upload oversized/invalid audio files that fail downstream in third-party speech assessment engines.

5. **Numerical Bound Vulnerabilities**:
   - Endpoints accepting quantities (such as custom credit purchases or pagination parameters) risk zero, negative, or integer overflow inputs if not bounded at the API boundary.

---

## Solution

Implement a comprehensive, 4-tier defense-in-depth validation architecture across the platform to guarantee data integrity, eliminate UI breakage, and protect backend and AI resources:

1. **Tier 1 — Client-Side Instant Feedback & Defensive UI Styling**:
   - Upgrade form inputs across the React SPA to HeroUI v3 compound components (`TextField`, `Label`, `Input`, `FieldError`, `Description`) equipped with native HTML5 constraints (`maxLength`, `required`, `pattern`).
   - Map backend RFC 7807 `400 Bad Request` ProblemDetails errors directly to per-field `<FieldError>` alerts.
   - Enforce defensive presentation styling (`truncate`, responsive `max-w-[...]`, `overflow-hidden`, `break-words`) with native accessible `title={fullText}` tooltips on all dynamic user-generated text displays (topbar header, profile cards, user tables, badges).

2. **Tier 2 — Automated Minimal API Request Validation Filter**:
   - Implement an automated, lightweight endpoint filter in WebAPI that intercepts incoming request contracts, validates them against .NET `DataAnnotations` and `IValidatableObject` rules, and immediately short-circuits invalid requests with standard RFC 7807 `ValidationProblem` responses.
   - Decorate contract DTOs across auth, profile, credit orders, and administrative content with precise bounds.

3. **Tier 3 — Domain Service Invariants & Sanitization**:
   - Normalize user text inputs by auto-trimming leading/trailing whitespace and collapsing multiple consecutive whitespace characters into a single space.
   - Enforce canonical name validation: 2–70 characters, matching Unicode letters and marks with single hyphens, apostrophes, or spaces (`^[\p{L}\p{M}]+(?:['’\-\. ][\p{L}\p{M}]+)*$`), strictly rejecting numbers, control characters, and markup.
   - Enforce strict IANA timezone verification and national/E.164 phone formatting.
   - Enforce pre-flight bounds on AI free-response submissions: 10–1,000 words ($\le 5,000$ characters) for writing essays; $\le 180$ seconds and $\le 10$ MB for speaking audio with valid MIME types (`audio/webm`, `audio/wav`, `audio/mp4`).
   - Enforce numerical bounds: credit orders require $1 \le \text{credits} \le 1,000$; pagination requires $\text{page} \ge 1$ and $1 \le \text{pageSize} \le 100$.

4. **Tier 4 — Database Schema Constraints**:
   - Retain `NVARCHAR(120)` column storage for backward compatibility while adding a database `CHECK (LEN(TRIM(FullName)) >= 2)` constraint in `docs/database-schema.sql` to block empty or whitespace-only names at the database level.
   - Validate and ensure all schema-level checks pass 100% via `docs/schema-tests/run-all.sh`.

---

## User Stories

### Personal Profile & Identity
1. As a learner updating my profile, I want the full name field to enforce a clear 70-character limit, so that I cannot accidentally enter excessive text that distorts layouts.
2. As a learner entering my name, I want immediate feedback if I accidentally type numbers or special symbols, so that my account reflects my genuine name.
3. As a learner with a Vietnamese name containing complex diacritics, I want the system to accept all standard Vietnamese Unicode characters and accents, so that my name is spelled correctly.
4. As a learner with an international hyphenated or apostrophized name (e.g. Jean-Luc, O'Connor), I want the name field to accept hyphens and apostrophes, so that my identity is respected.
5. As a learner entering a phone number, I want the system to validate standard phone formats, so that I am notified if I enter an invalid number.
6. As a learner selecting a timezone, I want the dropdown and backend to enforce valid IANA timezones, so that my exam countdowns and mentor sessions synchronize accurately.
7. As a learner submitting a profile update with extra spaces, I want the system to automatically trim and normalize whitespace, so that my display name is clean.
8. As a learner whose input fails validation, I want to see clear, inline field-level error messages beneath the affected inputs, so that I understand exactly how to correct the errors.

### Defensive UI & Layout Integrity
9. As a learner navigating the application, I want the top navigation bar to truncate long names with an ellipsis while maintaining container bounds, so that the header controls, credit pill, and sign-out buttons never get pushed offscreen.
10. As a user viewing a truncated name in the topbar or a table, I want to hover over the name to see the complete unclipped text in a native tooltip, so that full information remains accessible.
11. As an administrator reviewing user lists, I want table cells with long names or emails to wrap or truncate gracefully without stretching the table columns, so that the admin dashboard remains readable.
12. As a learner viewing transaction receipts and order summaries, I want long descriptions or reference codes to wrap cleanly, so that financial receipts remain crisp and printable.

### API Security & Robustness
13. As an API client sending an invalid request DTO, I want to receive an RFC 7807 `400 Bad Request` ProblemDetails response with structured field error keys, so that client apps can bind errors to UI fields programmatically.
14. As a backend developer, I want all incoming request contracts in Minimal API routes to be automatically validated by an endpoint filter, so that I do not have to write repetitive validation boilerplate in every route handler.
15. As a security engineer, I want text inputs to strictly disallow HTML tags and script injections, so that stored cross-site scripting (XSS) attacks are blocked before reaching domain storage.
16. As a platform engineer, I want pagination parameters to reject negative page numbers and limit page size to at most 100 items, so that clients cannot trigger memory exhaustion or database denial-of-service queries.
17. As a finance auditor, I want credit purchase requests to enforce a strict positive integer range (1 to 1,000 credits), so that negative credit orders or abusive zero-value transactions are rejected upfront.

### AI Free-Response & Media Protection
18. As a learner submitting a writing practice essay, I want the interface to provide a live word and character counter, so that I know whether I am within the required 10 to 1,000 word range.
19. As a learner submitting a writing task, I want the system to warn me if my submission is shorter than 10 words or exceeds 5,000 characters, so that I do not waste credits on ungradable attempts.
20. As a platform engineer, I want writing submissions exceeding 5,000 characters to be rejected before hitting the asynchronous AI grading queue, so that expensive LLM context windows are protected from prompt injection and token blowup.
21. As a learner recording speaking practice audio, I want the audio recorder to enforce a maximum recording duration of 180 seconds, so that I remain aligned with standard exam timings.
22. As a platform administrator, I want uploaded audio files to be validated for MIME type (`audio/webm`, `audio/wav`, `audio/mp4`) and capped at 10 MB, so that corrupt or massive binary files do not crash speech assessment processors.

### Database Integrity
23. As a database administrator, I want SQL Server to reject empty or whitespace-only user names via a `CHECK` constraint, so that bad data can never be introduced via direct scripts or legacy migrations.
24. As an automated test runner, I want `docs/schema-tests/run-all.sh` to pass 100% on the updated database schema, ensuring no database regression is introduced.

---

## Implementation Decisions

### Decision 1: 4-Tier Defense-in-Depth Architecture
- **Tier 1 (Client UI)**: HeroUI v3 compound form controls (`TextField`, `Label`, `Input`, `FieldError`) with native HTML constraints (`maxLength`, `pattern`), client-side word/character counters, and defensive CSS (`truncate max-w-[...]`, `break-words`).
- **Tier 2 (API Gateway)**: Reusable Minimal API `IEndpointFilter` (`RequestValidationFilter<TRequest>`) that inspects request parameters, runs `Validator.TryValidateObject`, and returns `Results.ValidationProblem(...)`.
- **Tier 3 (Domain Services)**: Invariant assertions, string sanitization (trimming and whitespace collapsing), regex matching, and business range checks.
- **Tier 4 (Database Schema)**: Column types, nullability, and SQL `CHECK` constraints in `docs/database-schema.sql`.

### Decision 2: Minimal API Validation Filter Implementation
- Introduce an endpoint filter extension method (`.WithValidation<TRequest>()`) in `WebAPI`.
- When an endpoint is marked with this filter:
  - If the incoming argument fails validation attributes, return `Results.ValidationProblem(errors)`.
  - If valid, execute the handler.
- Wire this filter across core route groups: Auth, Profile, Checkout, Practice, and Administration.

### Decision 3: Canonical Unicode Name Format & Sanitization
- Normalized full name specification:
  - Minimum length: 2 characters.
  - Maximum length: 70 characters.
  - Regex pattern: `^[\p{L}\p{M}]+(?:['’\-\. ][\p{L}\p{M}]+)*$`
  - Sanitization: Leading and trailing spaces are trimmed; consecutive internal spaces are collapsed to a single space.
  - Rejection: Any digit, emoji, control character, HTML/XML tag, or invalid punctuation results in a `400 Bad Request` with an explicit message: *"Name must be 2–70 characters and contain only letters, spaces, hyphens, or apostrophes."*

### Decision 4: Defensive UI Layout Constraints
- In `AppShell`:
  - Wrap the user display name in a constrained container: `<span className="truncate max-w-[140px] sm:max-w-[200px]" title={user.fullName || user.email}>{user.fullName || user.email}</span>`.
- In Profile page, Card headers, and Admin user tables:
  - Apply `truncate` and `break-words` on all dynamic user-provided strings.
  - Ensure all truncated elements provide a native `title` attribute containing the complete text for accessibility and desktop hover inspection.

### Decision 5: AI Free-Response Bounds & Media Constraints
- **Writing Practice & Placement Submissions**:
  - Minimum word count: 10 words.
  - Maximum word count: 1,000 words.
  - Maximum character count: 5,000 characters.
  - Enforcement: Client-side counter warns the user; API endpoint filter rejects out-of-bound submissions before enqueueing into `IX_AiGradings_Queue`.
- **Speaking Practice Audio Submissions**:
  - Maximum duration: 180 seconds.
  - Maximum file size: 10 MB.
  - Allowed MIME types: `audio/webm`, `audio/wav`, `audio/mp4`, `audio/ogg`.
  - Enforcement: Audio recorder timer enforces recording cut-off; API endpoint rejects payloads exceeding 10 MB or carrying invalid MIME headers.

### Decision 6: Numerical Invariants & Pagination Guardrails
- **Credit Purchases**:
  - Quantity constraint: `[Range(1, 1000, ErrorMessage = "Credits must be between 1 and 1,000.")]`.
- **Pagination**:
  - `page`: Minimum 1.
  - `pageSize`: Minimum 1, Maximum 100 (default: 20).
- **Exam Countdown & Durations**:
  - Strict positive integer seconds.

### Decision 7: Database Schema Hardening
- In `docs/database-schema.sql`:
  - Retain `Users.FullName NVARCHAR(120) NULL` for storage compatibility.
  - Add constraint: `CONSTRAINT CK_Users_FullName_NotEmpty CHECK (FullName IS NULL OR LEN(TRIM(FullName)) >= 2)`.
  - Update `docs/schema-tests/` to verify this constraint rejects empty or single-character whitespace strings.

---

## Testing Decisions

### What Makes a Good Test
- Tests must verify external observable behavior (HTTP response status, RFC 7807 problem payloads, database persistence, and rendered DOM attributes), never internal private variables.
- Validation tests must verify both positive cases (valid Unicode Vietnamese names, hyphenated names) and negative cases (gibberish with digits, over-length strings, negative numbers, script tags).

### Tested Modules & Seams
1. **WebAPI Minimal API HTTP Seam (`WebApplicationFactory<Program>`)**:
   - `PUT /api/profile`:
     - Reject 99-character name with digits -> `400 Bad Request` with `errors.fullName`.
     - Reject empty name or whitespace -> `400 Bad Request`.
     - Reject invalid phone -> `400 Bad Request`.
     - Reject invalid timezone -> `400 Bad Request`.
     - Accept valid Vietnamese name (`Nguyễn Thị Minh Khai`) -> `200 OK` with trimmed name.
     - Accept valid international name (`Jean-Luc O'Connor`) -> `200 OK`.
   - `POST /api/checkout/orders`:
     - Reject 0 or negative credits -> `400 Bad Request`.
     - Reject credits > 1000 -> `400 Bad Request`.
   - `POST /api/practice/writing/submit`:
     - Reject essay with 3 words -> `400 Bad Request`.
     - Reject essay exceeding 5,000 characters -> `400 Bad Request`.
2. **Frontend UI Seam (`vitest` + `@testing-library/react`)**:
   - `AppShell`: Render with a 70-character name; assert that the rendered element includes `truncate` and `max-w-[...]`, and that `title` matches the full name.
   - `ProfilePage`: Render form; assert that `<input>` has `maxLength="70"`; assert that submitting invalid characters displays the error message.
3. **Database Schema Seam (`docs/schema-tests/run-all.sh`)**:
   - Assert `INSERT INTO Users (Email, FullName, ...) VALUES ('test@demo.local', '  ', ...)` throws constraint rejection.

### Prior Art
- `tests/Services.Tests/AuthServiceTests.cs`: Existing password and lockout testing.
- `src/web/src/components/shell/__tests__/AppShell.test.tsx`: AppShell rendering tests.
- `docs/schema-tests/domain/`: SQL Server constraint validation tests.

---

## Out of Scope

- Introducing heavy external third-party validation libraries such as `FluentValidation`.
- Implementing client-side CAPTCHA or proof-of-work challenges.
- Natural-language profanity filtering or sentiment moderation on essays (handled downstream by rubric evaluation).
- Modifying email verification mechanics or password hashing algorithms.

---

## Further Notes

- All changes adhere strictly to ADR-0001 (database-first EF Core) and ADR-0015 (HeroUI v3 SPA).
- The four domain terms recorded in `GLOSSARY.md` (`Defensive Layout Truncation`, `Request Validation Filter`, `Normalized Name`, `Free-Response Bounds`) establish shared terminology for all future tickets.
