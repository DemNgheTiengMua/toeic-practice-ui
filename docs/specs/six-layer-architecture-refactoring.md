# Six-Layer Enterprise Architecture & Pure MVC Refactoring (GitHub Spec #123)

## Architecture

The project is a .NET 10 enterprise solution (`ExtraEfficiency.slnx`) structured across six clean, decoupled architectural layers:

```
[WebMVC (Port 5000, HttpOnly Cookie Proxy)]   [WebAPI (Port 5001, JWT Bearer)]
         │                                                    │
         └─────────────► [Contracts (DTOs & Client Slices)] ◄─┘
                                   │
                         [Services (Domain Logic)]
                                   │
                      [DataAccess (EF Core 10)]
                                   │
                   [BusinessObjects (Entities & Enums)]
```

### Binding Architectural Invariants
1. **ADR-0001 (Database-First EF Core)**: `docs/database-schema.sql` is single source of truth. Zero code-first migrations in `DataAccess/Migrations`. All 59+ entity and keyless view classes reside in `BusinessObjects`. All 27 `HasTrigger(...)` mappings preserved in `CompanionDbContext`.
2. **ADR-0002 (MVC Proxies API)**: Browser never calls `WebAPI` directly. CORS closed. JWT kept in HttpOnly cookie on MVC site. MVC endpoints forward calls with anti-forgery tokens.
3. **ADR-0003 (Credit Ledger)**: Balance is always `SUM(Amount)` over append-only ledger. "Block before you charge".
4. **ADR-0004 & ADR-0005 (AI Separation)**: AI longitudinal insights over accuracy views are completely decoupled from attempt rubric grading.
5. **ADR-0008 (AI Grading Queue Seam)**: `IAiGradingProcessor` owns queue processing. `SpeakingPracticeService` and `WritingPracticeService` are completely decoupled.
6. **ADR-0010 (Exam Shell Invariants)**: Zero-reload in-memory question and stimulus swapping preserved when extracting external JavaScript modules and partial views.
7. **Pure Presentation Razor Views**: Zero `@inject DateTimeFormatter` or `@inject CurrentUser` in `.cshtml`. Zero inline `<style>` tags. Zero views > 450 lines. Zero inline LINQ `.GroupBy` or wire DTO instantiations in views.

---

## Feature Inventory

| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Entity & View Model Relocation | Move all 61 scaffolded entities and SQL keyless view models from `DataAccess/Scaffold` to `BusinessObjects` | M1 | #124 |
| 2 | Migration Artifact Deletion | Permanently delete dead code-first migrations in `src/DataAccess/Migrations/` per ADR-0001 | M1 | #124 |
| 3 | CompanionDbContext Decomposition | Decompose `CompanionDbContext.cs` into 8 domain partial configurations preserving all 27 `HasTrigger` calls | M1 | #124 |
| 4 | WebMVC Infrastructure Relocation | Move `CurrentUser`, `CookieBearerHandler`, `LearnerShell` to `Infrastructure/`, `DateTimeFormatter` to `Formatters/`, `TimeZoneMiddleware` to `Middlewares/` | M1 | #125 |
| 5 | Shared Layouts Styles Section | Add `@await RenderSectionAsync("Styles", required: false)` to `_Layout.cshtml`, `_LearnerLayout.cshtml`, `_AdminLayout.cshtml` | M1 | #125 |
| 6 | Shared Layout Pure Presentation | Eliminate `@inject CurrentUser` and static `LearnerShell.Get(Context)` from layouts | M1 | #125 |
| 7 | AI Insight Panel Asset Separation | Extract inline `<style>` and `<script>` from `_AiInsightPanel.cshtml` to `wwwroot/css/components/` and `wwwroot/js/components/` | M1 | #125 |
| 8 | WebAPI Startup Modularization | Modularize `WebAPI/Program.cs` via extension methods in `WebAPI/Extensions/` | M1 | #125 |
| 9 | Sitting DTO & Model Decomposition | Decompose `MockExamModels`, `PlacementModels`, and corresponding DTO files into single-type files | M2 | #126 |
| 10 | AttemptEngine Decomposition | Decompose `AttemptEngine.cs` (2,931 lines) into partial classes (`Lifecycle`, `Answers`, `Scoring`, `Preflight`) without changing public signatures | M2 | #126 |
| 11 | Sitting Service & Client Interfaces | Extract `IAttemptEngine`, `IMockExamService`, `IPlacementService` to `Services/Interfaces/`, `AttemptEngineException` to `Services/Exceptions/`, `IMockExamsClient`, `IPlacementClient` to `Contracts/Clients/` | M2 | #126 |
| 12 | Sitting API Endpoint Decomposition | Decompose `ExamEndpoints.cs` and `PlacementEndpoints.cs` into modular route handlers | M2 | #126 |
| 13 | Exam & Placement View Asset Separation | Extract inline `<style>` and `<script>` from `Exams/Take.cshtml` (4,485 lines) and `Placement/Take.cshtml` (624 lines) to `wwwroot/css/pages/` and `wwwroot/js/pages/`; extract cohesive partials | M2 | #126 |
| 14 | ExamsViewTests Alignment | Update `ExamsViewTests.cs` to assert against linked stylesheet `exam-take.css` to prevent test breakages | M2 | #126 |
| 15 | Practice & Diagnostic DTO & Model Decomposition | Decompose `PracticeModels`, `SavedQuestionModels`, `WeaknessModels`, `AiInsightModels`, and DTOs into single-type files | M3 | #127 |
| 16 | Practice & Insight Service Decomposition | Decompose `AiInsightService.cs` (1,289 lines) and `PracticeService.cs` (955 lines) into partials; extract interfaces and exceptions | M3 | #127 |
| 17 | Practice Client & Endpoint Modularization | Extract `IPracticeClient`, `ISavedQuestionsClient`, `IWeaknessAnalysisClient`, `IAiInsightsClient` to `Contracts/Clients/`; modularize practice endpoints | M3 | #127 |
| 18 | Practice View Asset Separation | Decompose `Practice/Take.cshtml` (1,766 lines) into external CSS/JS and partials; eliminate `@inject DateTimeFormatter` and inline DTOs/GroupBy | M3 | #127 |
| 19 | Free-Response Model & Client Decomposition | Decompose `SpeakingPracticeModels`, `WritingPracticeModels`, `SpeakingDtos`, `WritingDtos`; extract client interfaces | M3 | #128 |
| 20 | Free-Response Service & Queue Seam | Extract `ISpeakingPracticeService`, `IWritingPracticeService`, `IAiGradingProcessor` to `Services/Interfaces/`; maintain strict decoupling per ADR-0008 | M3 | #128 |
| 21 | Free-Response Views Asset Separation | Extract inline CSS/JS from `Speaking/Take.cshtml`, `Writing/Take.cshtml`, and pending/review views to static assets | M3 | #128 |
| 22 | Admin Model, DTO & Client Decomposition | Decompose `CertificateCatalog`, `ScoreConversionModels`, admin DTOs; extract `IAdminExamsClient`, `IAdminCertificatesClient`, `IAdminImportsClient`, etc. | M4 | #129 |
| 23 | Admin God Services Decomposition | Decompose `AdminExamService.cs` (2,539 lines), `QuestionImportService.cs` (1,597 lines), `ExamQuickIngestService.cs` (1,557 lines), `CertificateService.cs` (982 lines) into partials; extract interfaces and exceptions | M4 | #129 |
| 24 | Admin Endpoints Modularization | Modularize `AdminEndpoints.cs` (665 lines) and `AdminImportEndpoints.cs` (119 lines) | M4 | #129 |
| 25 | Admin Exam Studio & Wizard View Decomposition | Decompose `Admin/ExamStudio.cshtml` (2,618 lines), `Admin/CertificateWizard.cshtml` (1,391 lines), and `_QuickIngestModal.cshtml` into external CSS/JS and partial views; remove inline LINQ GroupBy | M4 | #129 |
| 26 | Guided Learning Models & Service Decomposition | Decompose `LearningPathModels`, `LearnerContext`, and DTOs; decompose `LearningPathService.cs`, `AdminLessonService.cs`, `MentorService.cs` into partials; extract interfaces | M5 | #130 |
| 27 | Dashboard & Lessons View Asset Separation | Decompose `Dashboard/_DashboardSimulator.cshtml` (666 lines), `Lessons/Index.cshtml`, `Admin/Lessons/Edit.cshtml` to external assets and partials; eliminate `@inject DateTimeFormatter` and inline helper functions | M5 | #130 |
| 28 | Identity & Operations Decomposition | Decompose `AuthInputs`, `AuthDtos`, `PackageDtos`, `PaymentDtos`, `AdminDtos`; decompose `Enums.cs` preserving 1:1 parity with `EnumCodesTests` | M5 | #131 |
| 29 | Identity & Operations Client & Service Extraction | Extract `IAuthClient`, `IOrdersClient`, `IWalletClient`, `ISupportClient`, and admin client interfaces; extract service interfaces and exceptions | M5 | #131 |
| 30 | Operations Views Cleanup | Eliminate `@inject CurrentUser` and `@inject DateTimeFormatter` from `Profile`, `Wallet`, `Orders`, `Pricing`, `Certificates` views; extract CSS/JS from `Checkout/Index.cshtml` | M5 | #131 |
| 31 | Legacy Bundle Retirement | Retire `src/Contracts/DomainClients.cs` once all domain client interfaces have migrated to `src/Contracts/Clients/` | M6 | #132 |
| 32 | Automated Architecture & Convention Test Suite | Add automated architecture tests in `tests/WebMVC.Tests` and `tests/Services.Tests` asserting zero `@inject`, zero inline `<style>`, zero views > 450 lines, zero inline LINQ/DTO in views, zero migrations in DataAccess, all entities in BusinessObjects | M6 | #132 |
| 33 | E2E Regression Parity & Adversarial Hardening | Verify 100% test pass on all test suites (Services.Tests, WebMVC.Tests, WebMVC.E2ETests) and adversarial coverage audit | M6 | #132 |

---

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Foundation & Persistence Prefactoring | Tickets #124 & #125: Relocate entities to `BusinessObjects`, delete `DataAccess/Migrations`, modularize `CompanionDbContext`, WebMVC `Infrastructure/`/`Formatters/`/`Middlewares/`, `@RenderSectionAsync("Styles")`, layout `@inject` removal, `WebAPI/Program.cs` modularization | none | PLANNED |
| M2 | Sitting Slice (Mock & Placement) | Ticket #126: `AttemptEngine.cs` decomposition, sitting models/DTOs/clients/endpoints, `Exams/Take.cshtml` CSS/JS extractions and partials, `ExamsViewTests` alignment | M1 | PLANNED |
| M3 | Practice, Weakness & Free-Response | Tickets #127 & #128: Practice & Diagnostic slice, `AiInsightService` & `PracticeService` decomposition, `Practice/Take.cshtml` extractions, Speaking & Writing practice slices, `IAiGradingProcessor` queue seam, views cleanup | M1 | PLANNED |
| M4 | Admin Authoring & Blueprint Wizard | Ticket #129: `AdminExamService`, `QuestionImportService`, `ExamQuickIngestService`, `CertificateService` decompositions, Admin DTOs/clients/endpoints, `Admin/ExamStudio.cshtml` & `Admin/CertificateWizard.cshtml` CSS/JS extractions and partials | M1 | PLANNED |
| M5 | Guided Learning, Dashboard & Identity/Operations | Tickets #130 & #131: Learning path, lessons, mentors, identity, credit wallet, checkout orders, operations slices, `Enums.cs` decomposition, dashboard & operations views cleanup | M1 | PLANNED |
| M6 | Final Milestone & Architectural Lock-In | Ticket #132: Retire `DomainClients.cs`, automated architecture & convention verification test suites, 100% E2E test pass, adversarial coverage hardening | M2, M3, M4, M5 | PLANNED |

---

## Interface Contracts

### M1 ↔ All Vertical Slices (M2–M5)
- **Entity Namespaces**: Entities reside in `BusinessObjects.Entities` (or `BusinessObjects`), with global or namespace usings so existing service code compiles without breakages.
- **DbContext Configuration**: `CompanionDbContext` split across partial classes in `DataAccess/Configurations/`; all 27 `HasTrigger` calls preserved verbatim.
- **Layout Section**: Shared layouts expose `@await RenderSectionAsync("Styles", required: false)`. Slices can safely link external stylesheets via `@section Styles { <link rel="stylesheet" href="~/css/pages/..." /> }`.
- **User Claims**: Views access user display state via ViewModels or `ClaimsPrincipal` extensions, never `@inject CurrentUser`.
- **Date Formatting**: Views display pre-formatted strings or use standard tag helpers/viewmodel properties, never `@inject DateTimeFormatter`.

### Contracts Clients ↔ WebMVC & WebAPI
- **Domain Client Interfaces**: Each domain defines its interface in `src/Contracts/Clients/I<Domain>Client.cs`.
- **CompanionApiClient**: `CompanionApiClient` is a `partial class` across `src/Contracts/Clients/CompanionApiClient.<Domain>.cs`, sharing the base HTTP client and error pipeline.
- **Public Signatures**: No breaking changes to existing client methods or response DTO types.

### Service Interfaces & Exceptions
- **Interfaces**: Extracted into `src/Services/Interfaces/I<Service>.cs`.
- **Exceptions**: Extracted into `src/Services/Exceptions/<Domain>Exception.cs`.
- **God Service Decompositions**: Sliced into partial classes sharing the same class name and namespace, preserving 100% binary compatibility for DI registration.

---

## Code Layout

```
src/
├── BusinessObjects/
│   ├── Entities/                 # 52 table entities relocated from DataAccess/Scaffold
│   ├── Views/                    # 9 keyless SQL view models relocated from DataAccess/Scaffold
│   ├── Enums/                    # Decomposed domain enum definitions
│   └── Models/                   # Domain-specific internal models
├── DataAccess/
│   ├── Configurations/           # CompanionDbContext domain partial configurations (8 files)
│   ├── CompanionDbContext.cs     # Core DbContext, DbSets, OnModelCreating partial hook
│   └── (NO Migrations/ directory - strictly database-first per ADR-0001)
├── Services/
│   ├── Interfaces/               # All public service contracts (IAttemptEngine, etc.)
│   ├── Exceptions/               # Domain custom exceptions (AttemptEngineException, etc.)
│   ├── Partials/ or in-place     # Focused partial classes for AttemptEngine, AdminExamService, etc.
│   └── Graders/                  # AI grading provider adapters (Claude, SpeechSuper)
├── Contracts/
│   ├── Clients/                  # Individual client interfaces and CompanionApiClient partial slices
│   ├── DTOs/                     # Decomposed single-type wire DTOs grouped by domain
│   └── Enums/                    # Wire enums maintaining parity with BusinessObjects.Enums
├── WebAPI/
│   ├── Extensions/               # ServiceCollection, Auth, and Middleware extension methods
│   ├── Endpoints/                # Modular domain endpoint route groups
│   └── Program.cs                # Lightweight, focused startup
└── WebMVC/
    ├── Infrastructure/           # CurrentUser, CookieBearerHandler, LearnerShell, ClaimsExtensions
    ├── Formatters/               # DateTimeFormatter, AiNarrativeFormatter
    ├── Middlewares/              # TimeZoneMiddleware
    ├── ViewModels/               # Dedicated per-page ViewModels with pre-formatted strings
    ├── Views/                    # Pure presentation templates (<450 lines, 0 @inject, 0 inline <style>)
    │   └── <Domain>/Partials/    # Cohesive sub-components (Stimulus, Question, Nav, Wizard steps)
    └── wwwroot/
        ├── css/pages/            # Extracted page-level stylesheets
        └── js/pages/             # Extracted page-level JavaScript modules

tests/
├── Services.Tests/               # Domain & Integration tests against LocalDB (with ArchitectureTests)
└── WebMVC.Tests/                 # MVC unit, layout, and view tests (with ArchitectureTests)
```
