# Test Architecture & Engineering Infrastructure Specification

## 1. Test Philosophy & Engineering Principles

The Extra Efficiency test architecture enforces rigorous quality standards for the six-layer enterprise .NET 10 solution (`ExtraEfficiency.slnx`). The testing framework adheres to the following foundational engineering principles:

### 1.1 Opaque-Box & Requirement-Driven Verification
Tests are derived exclusively from authoritative project specifications (`six-layer-architecture-refactoring.md` in this directory, `CONTEXT.md`, and Architectural Decision Records ADR-0001 through ADR-0011). Tests verify observable behavior, wire contracts, and architectural invariants rather than coupled implementation details. If implementation code changes internally without altering the contractual behavior, tests remain robust and green.

### 1.2 Progressive Testability & Invariant Enforcement
Every milestone implementation must be verifiable using only features from that milestone and its completed dependencies. The test suite directly guards core architectural invariants:
- **ADR-0001 (Database-First EF Core)**: Single source of truth is `docs/database-schema.sql`. Zero code-first migrations in `DataAccess/Migrations/`. All tables with triggers declare `HasTrigger(...)` in model configurations. Keyless views are read-only.
- **ADR-0002 (MVC Proxies API)**: Browser never directly accesses WebAPI. Closed CORS. JWT maintained strictly in HttpOnly cookie on MVC. Anti-forgery tokens guard all mutation endpoints.
- **ADR-0003 (Credit Ledger)**: Balance is strictly `SUM(Amount)` over the append-only `CreditTransactions` ledger. "Block before you charge" precondition check.
- **ADR-0004 & ADR-0005 (AI Separation)**: AI longitudinal insights over deterministic SQL views are strictly decoupled from attempt rubric grading.
- **ADR-0008 (AI Grading Queue Seam)**: `IAiGradingProcessor` owns asynchronous queue processing. `SpeakingPracticeService` and `WritingPracticeService` remain completely decoupled.
- **ADR-0010 (Exam Shell Invariants)**: Zero-reload in-memory question and stimulus swapping with external static JavaScript/CSS assets.
- **Pure Presentation Razor Views**: Zero `@inject DateTimeFormatter` or `@inject CurrentUser`. Zero inline `<style>` tags. Zero views > 450 lines. Zero inline LINQ `.GroupBy` or wire DTO instantiations in views.

### 1.3 Expected Output Derivation
Every test specifies an explicit, authoritative source of expected outputs:
- **Database Triggers & Ledger Balances**: Derived from SQL schema constraints, triggers, and SQL view definitions (`vw_UserSectionAccuracyAll`, `vw_UserCategoryAccuracy`).
- **Score Scaling & CEFR Bands**: Derived from official exam presets (TOEIC 10–990 scale, IELTS 0–9.0 scale with half-step rounding, VSTEP 0–10 scale).
- **HTTP / Wire Contracts**: Derived from RESTful conventions and `ApiError.Codes` constants.
- **Presentation Placards & UI Layouts**: Derived from the "Bảng hiệu phòng thi" design system specifications in `DESIGN.md`.

### 1.4 Test Isolation & Determinism
Tests must be completely independent and self-contained:
- In `tests/Services.Tests`, each test or test class creates its own isolated test entities with unique codes (`Unique("...")`) or operates in dedicated test databases managed by `DatabaseFixture`.
- In `tests/WebMVC.Tests`, unit and integration tests mock companion clients using deterministic test fixtures (`TestApiClient`, `FixedAuthentication`) with zero cross-test state leakage.
- No test depends on execution order or shared mutable static state.

### 1.5 Adversarial Verification
The test suite systematically includes adversarial test cases:
- **Encoding & Escaping Integrity**: Vietnamese diacritical marks (`Tiếng Việt`, `Hà Nội`, `Đề thi thử`), quote escaping in prompts, XSS script injection sanitization, and zero mojibake.
- **Boundary & Timeout Extremes**: Negative remaining seconds, 0 seconds auto-submission, extreme countdown durations, audio offset clamping.
- **Failure Cascading & Validation Bounds**: 404 AttemptNotFound, 400 AttemptExpired, 403 Forbidden, negative credits, double submissions.

---

## 2. Four-Tier Test Architecture Taxonomy

The test suite is structured into four distinct verification tiers across `tests/WebMVC.Tests` and `tests/Services.Tests`:

```
┌────────────────────────────────────────────────────────────────────────┐
│                   Tier 4: Real-World Scenarios                         │
│   - End-to-End Exam Sitting Flow (Zero Reload, Sync, Auto-Submit)      │
│   - Admin Exam Studio & Quick Ingest Authoring Journey                 │
│   - Learner Diagnostic-to-Guided-Path Placement Loop                   │
└────────────────────────────────────────────────────────────────────────┘
                                    ▲
┌────────────────────────────────────────────────────────────────────────┐
│               Tier 3: Cross-Feature Interactions                       │
│   - Sitting Lifecycle with Cookie Auth & Certificate Scoping           │
│   - Practice Drilling & Weakness View Aggregation (FIFO Bound)         │
│   - Credit Ledger & "Block Before You Charge" Invariants               │
│   - Free-Response AI Grading Queue Seam (ADR-0008 Decoupling)          │
└────────────────────────────────────────────────────────────────────────┘
                                    ▲
┌────────────────────────────────────────────────────────────────────────┐
│               Tier 2: Boundary & Corner Cases                          │
│   - Empty / Null Input Handling (Sync, Resume, Search, Payloads)       │
│   - Extreme Timeouts, Countdowns & Clamped Offsets                     │
│   - Vietnamese UTF-8 Encoding, Escaping & Zero Mojibake                │
│   - Error Codes (404 NotFound, 400 Expired, 403 Forbidden, 409)       │
└────────────────────────────────────────────────────────────────────────┘
                                    ▲
┌────────────────────────────────────────────────────────────────────────┐
│                   Tier 1: Feature Coverage                             │
│   - Layout Structure & Design System Placards (>=5 cases per area)     │
│   - Pure Presentation ViewModels & Formatters (Zero View Injections)   │
│   - DTO Separation, Contract Immutability & Serialization              │
│   - CompanionApiClient Slices & Error Pipelines                        │
│   - EF Core Persistence, HasTrigger Mappings & Keyless Views           │
└────────────────────────────────────────────────────────────────────────┘
```

### Tier 1: Feature Coverage
Focuses on contractual requirements, layout specifications, DTO immutability, client interfaces, and persistence invariants:
- **Minimum Requirement**: >= 5 targeted test cases per feature area.
- **Scope**:
  - `Layouts & Design Tokens`: Shared layout markup (`_Layout`, `_LearnerLayout`, `_AdminLayout`), style sections, standard CSS token linkage, signage palette classes (`btn--primary`, `btn--danger`, `field__error-placard`).
  - `Pure Presentation ViewModels & Formatters`: `DateTimeFormatter`, `CurrencyFormatter`, `AiNarrativeFormatter`, and ViewModels with pre-formatted strings.
  - `DTO Separation & Serialization`: DTO record definitions across all domains, System.Text.Json camelCase roundtrip serialization, nullability contracts.
  - `CompanionApiClient Slices & Error Handling`: API client methods, endpoint routing, ProblemDetails mapping to `ApiException`, standard `ApiError.Codes`.
  - `EF Core & Persistence Invariants`: All 27 `HasTrigger` table configurations, zero migrations in `DataAccess/Migrations`, keyless view mappings, append-only credit ledger.

### Tier 2: Boundary & Corner Cases
Focuses on input extremes, boundary conditions, character encoding, and error paths:
- **Empty / Null Handling**: Empty answer lists in sitting sync, null authorization codes, whitespace queries in question search, null stimuli.
- **Extreme Timeouts & Countdown Bounds**: 0-second timers triggering immediate auto-submission, negative remaining seconds clamped to 0, extreme durations (e.g. 999,999s) without overflow, audio offset clamping.
- **Encoding & Escaping**: Full Vietnamese diacritical alphabet support, HTML quote/bracket escaping, multi-byte UTF-8 preservation, zero mojibake.
- **Error Propagation & Failure Cascading**: Standardized error responses (`AttemptNotFound`, `AttemptExpired`, `ConcurrentSession`, `ExamNotPublishable`), credit refusal before debit.

### Tier 3: Cross-Feature Interactions
Focuses on integration between multiple subsystems, layers, and business rules:
- **Sitting Lifecycle with Auth & Claims**: Authentication cookie token propagation, active certificate filtering, preflight checks, sitting state machine transitions (In-Progress -> Expired/Submitted).
- **Practice Drilling & Weakness Views**: Free diagnostic drilling answers recorded into `PracticeSessions` and `PracticeAnswers`, FIFO retention bound (capped at 20 sessions per certificate), deterministic aggregation via `vw_UserSectionAccuracyAll` and `vw_UserCategoryAccuracy`.
- **Credit Wallet & Ledger Invariants**: "Block before you charge" policy: credit check before exam launch, ledger row creation with negative amount for paid exams, zero credit deductions for free placement tests.
- **Free-Response AI Grading Queue Seam**: `IAiGradingProcessor` processes queue entries from `AiGradings` table (`IX_AiGradings_Queue`); `SpeakingPracticeService` and `WritingPracticeService` remain decoupled from each other and from queue workers.

### Tier 4: Real-World Scenarios
Focuses on multi-step end-to-end user journeys and complete lifecycle workflows:
- **Zero-Reload Exam Sitting Flow (ADR-0010)**: Start sitting -> receive complete JSON manifest and questions -> client navigates questions in memory -> real-time sync batches answers -> countdown auto-submits on expiration -> scoring engine scales raw score to band.
- **Admin Studio Exam Authoring & Ingest**: Admin imports exam via quick ingest -> validates sections, questions, and options -> runs readiness audit -> publishes exam -> exam becomes active in learner catalog.
- **Learner Placement-to-Guided-Path Loop**: Diagnostic placement test taken -> target band calculated -> personalized learning path step assigned -> learner drills practice session linked to `LearnerPathStepId` (protected from pruning).

---

## 3. Feature Inventory Test Mapping

The following matrix maps all 33 features from `six-layer-architecture-refactoring.md` to their corresponding verification tiers and test implementations:

| # | Feature Inventory Item | Primary Tier | Target Test File | Key Invariants Verified |
|---|------------------------|--------------|------------------|-------------------------|
| 1 | Entity & View Model Relocation | Tier 1 | `Tier1PersistenceAndDomainTests.cs` | Entities reside in `BusinessObjects`, 0 in `DataAccess/Scaffold` |
| 2 | Migration Artifact Deletion | Tier 1 | `Tier1PersistenceAndDomainTests.cs` | `DataAccess/Migrations/` permanently deleted per ADR-0001 |
| 3 | CompanionDbContext Decomposition | Tier 1 | `Tier1PersistenceAndDomainTests.cs` | All 27 `HasTrigger(...)` mappings intact across partial configs |
| 4 | WebMVC Infrastructure Relocation | Tier 1 | `Tier1FeatureCoverageTests.cs` | `CurrentUser`, `CookieBearerHandler`, `DateTimeFormatter` in modular dirs |
| 5 | Shared Layouts Styles Section | Tier 1 | `Tier1FeatureCoverageTests.cs` | `@await RenderSectionAsync("Styles", required: false)` in shared layouts |
| 6 | Shared Layout Pure Presentation | Tier 1 | `Tier1FeatureCoverageTests.cs` | 0 `@inject CurrentUser` or `@inject DateTimeFormatter` in layout markup |
| 7 | AI Insight Panel Asset Separation | Tier 1 | `Tier1FeatureCoverageTests.cs` | Extracted CSS/JS in `wwwroot/css/components/`, `wwwroot/js/components/` |
| 8 | WebAPI Startup Modularization | Tier 1 | `Tier1FeatureCoverageTests.cs` | Extension methods in `WebAPI/Extensions/`, lightweight `Program.cs` |
| 9 | Sitting DTO & Model Decomposition | Tier 1 | `Tier1FeatureCoverageTests.cs` | Single-type sitting DTOs, record immutability, camelCase serialization |
| 10 | AttemptEngine Decomposition | Tier 1 | `Tier1PersistenceAndDomainTests.cs` | Partial classes for Lifecycle, Answers, Scoring, Preflight |
| 11 | Sitting Service & Client Interfaces | Tier 1 | `Tier1FeatureCoverageTests.cs` | `IAttemptEngine`, `IMockExamService`, `IPlacementService`, `IMockExamsClient` |
| 12 | Sitting API Endpoint Decomposition | Tier 2 | `Tier2BoundaryCornerCaseTests.cs` | Modular route handlers, error response mapping, status codes |
| 13 | Exam & Placement View Asset Separation | Tier 1, 4 | `Tier1FeatureCoverageTests.cs`, `Tier4RealWorldScenarioTests.cs` | External CSS/JS, 0 inline `<style>`, zero-reload in-memory swapping |
| 14 | ExamsViewTests Alignment | Tier 1 | `ExamsViewTests.cs`, `Tier1FeatureCoverageTests.cs` | Assertions against linked external stylesheets and clean Placard copy |
| 15 | Practice & Diagnostic DTO & Model | Tier 1 | `Tier1FeatureCoverageTests.cs` | Practice single-type models, JSON roundtrip, validation |
| 16 | Practice & Insight Service Decomp | Tier 3 | `Tier3ServicesInteractionTests.cs` | `AiInsightService` & `PracticeService` interfaces, deterministic SQL views |
| 17 | Practice Client & Endpoint Modular | Tier 1 | `Tier1FeatureCoverageTests.cs` | `IPracticeClient`, `ISavedQuestionsClient`, `IWeaknessAnalysisClient` |
| 18 | Practice View Asset Separation | Tier 1, 2 | `Tier1FeatureCoverageTests.cs`, `Tier2BoundaryCornerCaseTests.cs` | External CSS/JS, QoL controls, audio speed toggles, zero mojibake |
| 19 | Free-Response Model & Client Decomp | Tier 1 | `Tier1FeatureCoverageTests.cs` | Speaking & Writing DTOs, audio payload contracts |
| 20 | Free-Response Service & Queue Seam | Tier 3 | `Tier3ServicesInteractionTests.cs` | `IAiGradingProcessor` queue isolation, service decoupling per ADR-0008 |
| 21 | Free-Response Views Asset Separation | Tier 1 | `Tier1FeatureCoverageTests.cs` | External CSS/JS, accessible modals, countdown recording placards |
| 22 | Admin Model, DTO & Client Decomp | Tier 1 | `Tier1FeatureCoverageTests.cs` | Certificate catalog, score conversion DTOs, admin client interfaces |
| 23 | Admin God Services Decomposition | Tier 1 | `Tier1PersistenceAndDomainTests.cs` | `AdminExamService`, `QuestionImportService`, `ExamQuickIngestService` |
| 24 | Admin Endpoints Modularization | Tier 2 | `Tier2BoundaryCornerCaseTests.cs` | Admin route handlers, validation errors, CSRF protection |
| 25 | Admin Exam Studio & Wizard View | Tier 1, 4 | `Tier1FeatureCoverageTests.cs`, `Tier4RealWorldScenarioTests.cs` | External CSS/JS, partial views, 6-axis configurations, 0 inline GroupBy |
| 26 | Guided Learning Models & Services | Tier 3, 4 | `Tier3ServicesInteractionTests.cs`, `Tier4RealWorldScenarioTests.cs` | `ILearningPathService`, diagnostic step assignment, unpruned steps |
| 27 | Dashboard & Lessons View Asset | Tier 1 | `Tier1FeatureCoverageTests.cs` | Pure presentation templates, pre-formatted dates, 0 view injections |
| 28 | Identity & Operations Decomposition | Tier 1, 2 | `Tier1FeatureCoverageTests.cs`, `Tier2BoundaryCornerCaseTests.cs` | `Enums.cs` parity with `EnumCodesTests`, auth & order models |
| 29 | Identity & Operations Client/Service | Tier 1 | `Tier1FeatureCoverageTests.cs` | `IAuthClient`, `IOrdersClient`, `IWalletClient`, `ISupportClient` |
| 30 | Operations Views Cleanup | Tier 1 | `Tier1FeatureCoverageTests.cs` | 0 `@inject CurrentUser`, external checkout styles, anti-forgery |
| 31 | Legacy Bundle Retirement | Tier 1 | `Tier1FeatureCoverageTests.cs` | Contract interfaces migrated to `src/Contracts/Clients/` |
| 32 | Automated Architecture Test Suite | Tier 1 | `Tier1FeatureCoverageTests.cs`, `Tier1PersistenceAndDomainTests.cs` | Repo-wide scanner: 0 `@inject`, 0 inline `<style>`, 0 migrations |
| 33 | E2E Regression Parity & Hardening | Tier 4 | `Tier4RealWorldScenarioTests.cs` | Full sitting lifecycles, score calibration, 100% test pass |

---

## 4. Test Execution & CI Verification Commands

```sh
# 1. Full Solution Build (Must be 0 warnings, 0 errors)
dotnet build

# 2. Run All WebMVC Unit, Presentation & Architecture Tests
dotnet test tests/WebMVC.Tests

# 3. Run All Services, Persistence & Domain Tests against LocalDB
dotnet test tests/Services.Tests

# 4. Targeted Tiered Test Runs
dotnet test tests/WebMVC.Tests --filter FullyQualifiedName~Tier1
dotnet test tests/WebMVC.Tests --filter FullyQualifiedName~Tier2
dotnet test tests/WebMVC.Tests --filter FullyQualifiedName~Tier3
dotnet test tests/Services.Tests --filter FullyQualifiedName~Tier1
dotnet test tests/Services.Tests --filter FullyQualifiedName~Tier2
dotnet test tests/Services.Tests --filter FullyQualifiedName~Tier3
dotnet test tests/Services.Tests --filter FullyQualifiedName~Tier4
```
