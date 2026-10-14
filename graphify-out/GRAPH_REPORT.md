# Graph Report - extra_efficiency_ui  (2026-10-14)

## Corpus Check
- 205 files · ~208,822 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1709 nodes · 3376 edges · 206 communities (91 shown, 115 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 137 edges (avg confidence: 0.82)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `5cd71aa1`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Core Domain Services &
- Identity & Authentication Services
- Certificate Catalog Management
- Database Entity Relational Model
- Companion EF Core DbContext
- Email Delivery & Password Recovery
- MVC API Proxy &
- Project Dependencies & Package References
- AI Grading & Score Calculations
- Payment Gateway & Order
- CI Workflows & Architecture Decisions
- Adaptive Learning Path &
- User Entity & Multi-Cert Profile
- MVC Learner Dashboard & DTOs
- Learning Path Progression &
- Exam Question Bank & Navigation
- Multi-Certificate Architecture & Migration Specs
- Learner Context & Certificate Switching
- HTTP Companion Client & Contracts
- Exam Attempts & Test History
- Authentication Views & ViewModels
- Authentication Inputs & Validation
- AI Insights & Analytics
- Exam Catalog & Test Sections
- Frontend UI Icons &
- Certificate Entity & Domain Catalog
- Mentor Profiles & Booking Slots
- Learning Recommendations & Remediation
- Database ERD Generation Script
- Database Views & Financial Aggregates
- Mentor Slot Booking
- Resolved User Complaint
- User Admin Attempt
- Criterion Section Grading
- Question Group Audio
- Order Gateway Code
- Section Skill Question
- Skill Score Navigations
- Exam Placement Attempt
- Sign Token Http
- UI View: DashboardViewModel.cs
- UI View: _ViewImports.cshtml
- Path Learner Cert
- Path Module Item
- Mentor Payout Period
- Path Module Item
- Json Companion Api
- Question Session Practice
- Score Band Cert
- Attempt Question Answer
- Import Batch Cert
- Question Option Answers
- Token Http Cancellation
- Date Format The
- UI View: Prototype Finish Review
- Web Api Jwt
- Band Path Template
- Event Order Date
- Exam Section Audio
- Mentor Slot Booking
- Package Collection Credits
- Trg Exams Groups
- Certificate Read Only
- Attempt Skill Score
- Module Path Topic
- Review Booking User
- Score Scale Cert
- Credit Transactions Trg
- Async Register Sign
- Token User Email
- Token User Password
- Log Batch Date
- Category Cert Collection
- Band Target Cert
- Web Api Environment
- Web Mvc Environment
- Wwwroot Icons Each
- Keyboard Init Setup
- Time Token One
- Grading Score Criterion
- Question Saved User
- Attempts Trg Exam
- Profile Async Update
- Credits User Credit
- Section User Accuracy
- Token Cancellation Action
- Database Table: MockExam Task
- Problem Details Body
- User Category Accuracy
- User Section Accuracy
- Time Zones Contains
- UI View: DashboardViewModel
- Gradings Trg Mark
- Free Responses Trg
- Aside Practice Funnel
- Favicon Toeic Practice
- Placeholder Part Minimal
- UI View: Find a Mentor
- Api Error Codes
- Bahnschrift Display Typography
- Grading Scores Trg
- Bookings Trg Slot
- Complaints Grant Trg
- Contracts Auth User
- Trg Attempts Total
- Grading Criteria Trg
- Orders Trg Credited
- Placement Tests Trg
- Trg Refs Skill
- Score Bands Trg
- Section Criteria Trg
- Reset And Load
- Run All Script
- Exam Room Signage
- Path Placement Generated
- Read Only Contracts
- UI View: Student Profile
- UI View: ForgotPasswordViewModel
- UI View: RegisterViewModel
- UI View: ResetPasswordViewModel
- UI View: SignInViewModel
- UI View: VerifyEmailViewModel
- UI View: ProfileViewModel
- UI View: ChangePasswordViewModel
- Layout Cshtml Current
- Font Open Ibm
- Export Meta Sql
- Domain Catalog Sql
- Domain Identity Sql
- Domain Scoring Sql
- Domain Content Sql
- Domain Practice Sql
- Domain Mockexam Sql
- Domain Aigrading Sql
- Domain Learningpath Sql
- Domain Mentor Sql
- Domain Payment Sql
- Domain Coverage Sql
- Domain Writing Hints
- Domain Total Placement
- Domain Insights Payments
- Lib Sql
- Seed Demo Sql
- Listening Reading Split
- Per Attempt Grading
- Parallel Certificate Persistence
- Mentor Card Component
- Path Module Component
- Rubric Score Display
- Admin Operations Workflow
- Learner Study And
- Admin Key Performance
- Three Step Exam
- Database Table: Admin Exam Inventory
- Database Table: Admin Orders Ledger
- Question And Option
- Ielts Four Skill
- Toeic Multi Skill
- Vstep Vietnamese Proficiency
- Study With Direction
- Domain Documentation Exploration Protocol
- GitHub Issue Tracker Protocol
- Triage Label Mappings
- Product Feature Backlog
- Multi-Certificate UX Critique Report
- Speaking and Writing Schema Gaps Analysis
- Schema vs Screens Audit Log
- Cross-Certificate Isolation Architecture
- Multi-Certificate Companion Schema Plan
- Catalog Domain Task
- Content Domain Task
- TotalScore Skill Sum Trigger
- Pass 1: KYC Removal Phase
- Pass 2: Multi-Certificate Transformation Phase
- Pass 3: Companion Feature Additions Phase
- Multi-Certificate UI Redesign Implementation Plan
- Practice Funnel Architecture
- TOEIC UI Design Spec
- Two-Step Scoring Model
- Certificate Isolation Invariant
- Append-Only Credit Ledger Architecture
- Mentor Booking and Settlement System
- Multi-Certificate Companion Database Design Spec
- Certificate Picker Component
- Multi-Certificate UI Redesign Spec
- Four Credit Debit Triggers
- 2026-09-24 Architecture Pivot
- Block Before Charge Principle
- Certificate Isolation Invariant
- Derived Aggregates Principle
- Extra Efficient Product Specification
- Free Diagnostic Loop Principle
- Server-Owned State Principle
- Two-Step Score Conversion
- Read-Only Standard CEFR Bands Table
- Raw to Scaled Score Table
- SIL Open Font License v1.1 for IBM Plex
- Landing Hero and Sample Path Preview

## God Nodes (most connected - your core abstractions)
1. `DataAccess.Scaffold` - 65 edges
2. `CompanionDbContext` - 63 edges
3. `User` - 31 edges
4. `Contracts` - 30 edges
5. `TestApiClient` - 30 edges
6. `ICompanionApiClient` - 29 edges
7. `AccountEmailTests` - 28 edges
8. `CompanionApiClient` - 27 edges
9. `AuthService` - 24 edges
10. `LearnerServiceTests` - 21 edges

## Surprising Connections (you probably didn't know these)
- `TestApiClient` --implements--> `ICompanionApiClient`  [EXTRACTED]
  tests/WebMVC.Tests/PracticeControllerTests.cs → src/Contracts/CompanionApiClient.cs
- `TestApiClient` --references--> `PracticeCatalogDto`  [EXTRACTED]
  tests/WebMVC.Tests/PracticeControllerTests.cs → src/Contracts/PracticeDtos.cs
- `TestApiClient` --references--> `PracticeQuestionDto`  [EXTRACTED]
  tests/WebMVC.Tests/PracticeControllerTests.cs → src/Contracts/PracticeDtos.cs
- `TestApiClient` --references--> `GradedAnswerDto`  [EXTRACTED]
  tests/WebMVC.Tests/PracticeControllerTests.cs → src/Contracts/PracticeDtos.cs
- `TestApiClient` --references--> `PracticeSummaryDto`  [EXTRACTED]
  tests/WebMVC.Tests/PracticeControllerTests.cs → src/Contracts/PracticeDtos.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Complaint Handling and Credit Make-Good Flow** — prototype_admin_complaints_page, prototype_admin_complaint_detail_page, docs_superpowers_specs_2026_09_24_multi_cert_companion_design_mentor_booking, docs_toeic_user_flow_admin_workflow [EXTRACTED 1.00]
- **Admin Exam Authoring and Scoring Lifecycle** — prototype_admin_exam_list_page, prototype_admin_exam_editor_page, prototype_admin_question_editor_page, prototype_admin_score_conversion_page [EXTRACTED 1.00]
- **AI Graded Practice and Evaluation Flow** — prototype_student_exam_speaking_page, prototype_student_exam_writing_page, prototype_student_grading_pending_page, prototype_student_exam_result_page [EXTRACTED 1.00]
- **Credit Purchase and Order Reconciliation Flow** — prototype_payment_pricing_page, prototype_payment_checkout_page, prototype_payment_gateway_mock_page, prototype_payment_payment_result_page, prototype_payment_wallet_page [EXTRACTED 1.00]
- **TOEIC Full Mock Exam Lifecycle Flow** — prototype_student_exam_list_page, prototype_student_exam_instructions_page, prototype_student_exam_listening_page, prototype_student_exam_reading_page, prototype_student_exam_confirm_submit_page, prototype_student_exam_result_page, prototype_student_exam_review_page [EXTRACTED 1.00]
- **Visitor Onboarding and Certificate Discovery Flow** — prototype_landing_page, prototype_certificates_page, prototype_auth_register_page, prototype_auth_login_page [EXTRACTED 1.00]
- **Adaptive Learning Path Lifecycle** — docs_database_erd_learnerpaths, docs_database_erd_learnerpathsteps, docs_database_erd_pathtemplates, docs_database_erd_pathmodules, docs_database_erd_placementtests [INFERRED 0.85]
- **Mentor Session Lifecycle and Payout Settlement** — docs_database_erd_bookings, docs_database_erd_mentorslots, docs_database_erd_mentorprofiles, docs_database_erd_mentorpayouts, docs_database_erd_mentorpayoutitems [INFERRED 0.85]
- **Mock Exam Execution and AI Grading Pipeline** — docs_database_erd_examattempts, docs_database_erd_attemptanswers, docs_database_erd_freeresponses, docs_database_erd_aigradings, docs_database_erd_aigradingscores [INFERRED 0.85]
- **AI Assessment and Grading Pipeline** — docs_adr_0004_ai_insights_separate_from_rubric_grading_decision, docs_adr_0005_ai_providers_azure_speech_and_claude_proxy_decision, context_ai_vocabulary, docs_schema_gaps_speaking_speaking_format_gaps [INFERRED 0.85]
- **TOEIC Practice Visual Favicon Identity** — prototype_assets_img_favicon_toeic_brand_icon, prototype_assets_img_favicon_toeic_practice_branding, prototype_assets_img_favicon_svg_favicon_design [INFERRED 0.85]
- **Part 1 Visual Prompt Simulation** — prototype_assets_img_part1_sample_part1_photo_placeholder, prototype_assets_img_part1_sample_toeic_part1_question, prototype_assets_img_part1_sample_minimal_svg_placeholder [INFERRED 0.85]
- **Diagnostic Assessment to Adaptive Learning Path Workflow** — prototype_student_placement_select_page, prototype_student_placement_take_page, prototype_student_placement_result_page, prototype_student_target_band_set_page, prototype_student_path_generated_page, prototype_student_path_detail_page [INFERRED 0.85]
- **Section Practice, Drill, and Error Remediation Loop** — prototype_student_practice_select_page, prototype_student_practice_take_page, prototype_student_practice_summary_page, prototype_student_saved_questions_page, prototype_student_weakness_analysis_page [INFERRED 0.85]
- **Speaking and Writing Productive Skills Evaluation Suite** — prototype_student_speaking_instructions_page, prototype_student_speaking_review_page, prototype_student_writing_instructions_page, prototype_student_writing_review_page [INFERRED 0.85]
- **TOEIC Practice Brand Identity System** — src_webmvc_wwwroot_img_favicon_toeic_practice_favicon, src_webmvc_wwwroot_img_favicon_sign_brand_identity, src_webmvc_wwwroot_img_favicon_bahnschrift_typography [INFERRED 0.85]
- **Database-First Invariant Architecture** — docs_adr_0001_database_first_ef_core_decision, claude_layering_structure, product_derived_aggregates, _github_workflows_ci_mssql_service [INFERRED 0.95]
- **Exam-Hall Signage Design Philosophy** — _impeccable_surfaces_prototype_bang_hieu_viet_nam, design_signage_design_system, _impeccable_review_finish_review_flat_field_refusal, design_color_system [INFERRED 0.95]

## Communities (206 total, 115 thin omitted)

### Community 0 - "Core Domain Services &"
Cohesion: 0.12
Nodes (16): AttemptSkillScore, ICollection, PathModule, PathModuleTopic, ICollection, QuestionCategory, ScoreScale, ICollection (+8 more)

### Community 1 - "Identity & Authentication Services"
Cohesion: 0.08
Nodes (32): microsoft_extensions_logging, Now, PasswordHasher, AuthUser, ChangePasswordInput, ForgotPasswordInput, RegisterInput, ResetPasswordInput (+24 more)

### Community 2 - "Certificate Catalog Management"
Cohesion: 0.16
Nodes (14): ExitCode, IAsyncLifetime, ICollectionFixture, Output, system_data_common, system_diagnostics, string, Task (+6 more)

### Community 3 - "Database Entity Relational Model"
Cohesion: 0.09
Nodes (61): AiGradings Table, AiGradingScores Table, AiInsights Table, AttemptAnswers Table, AttemptSkillScores Table, Bookings Table, Certificates Table, Complaints Table (+53 more)

### Community 4 - "Companion EF Core DbContext"
Cohesion: 0.04
Nodes (46): 1. Certificate Picker, 2. Active Certificate Display, 3. Certificate Badge, 4. Mentor Card, 5. Path Module Card, 6. Rubric Score Display, 7. Recommendations List, Add (Pass 3) (+38 more)

### Community 5 - "Email Delivery & Password Recovery"
Cohesion: 0.15
Nodes (16): EmailMessage, IEmailSender, IEnumerable, AuthService, Fact, string, Task, AccountEmailTests (+8 more)

### Community 6 - "MVC API Proxy &"
Cohesion: 0.06
Nodes (34): DispatchProxy, IAuthenticationService, IDictionary, InlineData, ITempDataProvider, MethodInfo, CancellationToken, IActionResult (+26 more)

### Community 7 - "Project Dependencies & Package References"
Cohesion: 0.06
Nodes (33): Microsoft.AspNetCore.Authentication.JwtBearer (10.0.12), Microsoft.EntityFrameworkCore.Design (10.0.12), Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore (10.0.12), Microsoft.Extensions.Http (10.0.12), Microsoft.Extensions.Identity.Core (10.0.12), System.IdentityModel.Tokens.Jwt (8.19.2), net10.0, Microsoft.NET.Sdk (+25 more)

### Community 8 - "AI Grading & Score Calculations"
Cohesion: 0.17
Nodes (10): AttemptAnswer, PracticeAnswer, DateTimeOffset, ICollection, Question, ICollection, QuestionOption, DateTimeOffset (+2 more)

### Community 9 - "Payment Gateway & Order"
Cohesion: 0.08
Nodes (39): Order Checkout & Confirmation Page, Pending Order Duplicate Prevention Guard, Simulated Payment Gateway Page, Order Lifecycle State Branching, Payment Result & Status Page, Reconciliation Polling Status Indicator, Credit Pricing & Packages Page, Credit Wallet & Transaction Ledger Page (+31 more)

### Community 10 - "CI Workflows & Architecture Decisions"
Cohesion: 0.50
Nodes (4): Continuous Integration Workflow, SQL Server 2022 CI Container, sqlcmd Client Tools Setup, ADR-0001 Database-First EF Core

### Community 11 - "Adaptive Learning Path &"
Cohesion: 0.07
Nodes (38): Linear Learning Module Progression and Gating, Module Detail Prototype View, Diagnostic-Driven Adaptive Curriculum Generation, Path Generated Prototype View, Upgrade Path Prototype View, Credit Paywall and Premium Path Monetization, CEFR Diagnostic Baseline Scoring, Placement Result Prototype View (+30 more)

### Community 12 - "User Entity & Multi-Cert Profile"
Cohesion: 0.14
Nodes (13): DateTimeOffset, EmailVerificationToken, IOneTimeToken, PasswordResetToken, DateTimeOffset, EmailVerificationToken, DateTimeOffset, PasswordResetToken (+5 more)

### Community 13 - "MVC Learner Dashboard & DTOs"
Cohesion: 0.10
Nodes (25): ActionExecutingContext, ActionExecutionDelegate, CancellationToken, HttpPost, IActionResult, Task, ValidateAntiForgeryToken, DashboardController (+17 more)

### Community 14 - "Learning Path Progression &"
Cohesion: 0.13
Nodes (13): DataAccess.Scaffold, DateTimeOffset, ExamSectionAudio, DateTimeOffset, ICollection, LearnerPathStep, ICollection, PathModuleItem (+5 more)

### Community 15 - "Exam Question Bank & Navigation"
Cohesion: 0.06
Nodes (34): Alert, Badges, Buttons, Cards / Panels, Colors, Components, Design System: TOEIC Practice, Do: (+26 more)

### Community 16 - "Multi-Certificate Architecture & Migration Specs"
Cohesion: 0.18
Nodes (19): Admin Complaint Detail View, Complaint Resolution & Credit Grant Form, Admin Complaints Queue View, Complaints Queue Table, Admin Dashboard View, Admin Exam Editor View, Admin Exam Catalog View, Admin Orders View (+11 more)

### Community 17 - "Learner Context & Certificate Switching"
Cohesion: 0.16
Nodes (15): ActiveCertificate, InvalidOperationException, IQueryable, CertificateOption, LatestResult, LearnerContext, LearnerDashboard, RecentAttempt (+7 more)

### Community 18 - "HTTP Companion Client & Contracts"
Cohesion: 0.21
Nodes (6): HttpRequestMessage, HttpResponseMessage, JsonSerializerOptions, CompanionApiClient, CancellationToken, Task

### Community 19 - "Exam Attempts & Test History"
Cohesion: 0.07
Nodes (29): 1. Unwavering Design Concept Execution, 2. State Coverage and Error Handling, 3. Financial Transparency and Loss Prevention, Appendix: Assessment Methodology, **Casey (Mobile User): Desktop-Only Design Blocks Growth**, Command Sequence (Priority Order), Design Health Score, Design Specificity Verdict (+21 more)

### Community 20 - "Authentication Views & ViewModels"
Cohesion: 0.07
Nodes (33): Services, BusinessObjects, DataAccess, Exception, BandCatalog, CertificateCatalog, SectionCatalog, SkillCatalog (+25 more)

### Community 21 - "Authentication Inputs & Validation"
Cohesion: 0.15
Nodes (19): CertId, CorrectOptionIds, Learner, Practice, PracticeService, QuestionIds, SectionId, SpeakingSecId (+11 more)

### Community 22 - "AI Insights & Analytics"
Cohesion: 0.20
Nodes (10): Services.Tests, microsoft_aspnetcore_identity, microsoft_extensions_logging_abstractions, system_text, system_text_regularexpressions, AuthService, Fact, Task (+2 more)

### Community 23 - "Exam Catalog & Test Sections"
Cohesion: 0.07
Nodes (26): Execution Handoff, Global Constraints, Multi-Certificate Companion UI Prototype Redesign — Implementation Plan, Pass 1: Remove KYC (2-3 hours), Pass 2: Transform for Multi-Certificate (1 day), Pass 3: Add New Screens (2-3 days), Pass 4: Export to Figma (1-2 days), Review Focus (+18 more)

### Community 24 - "Frontend UI Icons &"
Cohesion: 0.25
Nodes (9): closeDrawer(), onBreakpoint(), openDrawer(), persistGroups(), setFolded(), setInert(), setRailCollapsed(), syncToggle() (+1 more)

### Community 25 - "Certificate Entity & Domain Catalog"
Cohesion: 0.09
Nodes (19): DateTimeOffset, ICollection, AiInsight, ICollection, Certificate, DateTimeOffset, ICollection, LearnerPath (+11 more)

### Community 26 - "Mentor Profiles & Booking Slots"
Cohesion: 0.20
Nodes (6): PracticeSessionDto, CancellationToken, Exception, IReadOnlyList, Task, TestApiClient

### Community 27 - "Learning Recommendations & Remediation"
Cohesion: 0.09
Nodes (22): Cognitive Load, Design Health Score, Design Specificity Verdict, Deterministic scan, False positives, Impeccable Critique — TOEIC L&R Prototype, Minor Observations, Overall Impression (+14 more)

### Community 28 - "Database ERD Generation Script"
Cohesion: 0.16
Nodes (17): domain_order(), domains_from_schema(), draw_edges(), draw_group(), draw_table(), main(), Page, Generate docs/database-erd.drawio from the live schema metadata.  Usage (from th (+9 more)

### Community 29 - "Database Views & Financial Aggregates"
Cohesion: 0.18
Nodes (8): DbContext, DbSet, ModelBuilder, CompanionDbContext, vw_UserCategoryAccuracy, vw_UserCreditBalance, vw_UserPurchaseTotal, vw_UserSectionAccuracy

### Community 30 - "Mentor Slot Booking"
Cohesion: 0.11
Nodes (18): Cross-cutting conventions (carried from the current schema), Domain 10 — Mentor (NEW), Domain 1 — Identity, Domain 2 — Catalog (NEW, data-driven), Domain 3 — Content (Exams + Questions + Import), Domain 4 — Scoring (per-certificate, replaces hardcoded CEFR), Domain 5 — Payment (credit ledger kept, spend reasons added), Domain 6 — Practice (generalised, credit-free) (+10 more)

### Community 31 - "Resolved User Complaint"
Cohesion: 0.12
Nodes (10): GradedAnswerDto, PracticeCatalogDto, PracticeOptionDto, PracticeQuestionDto, PracticeSectionDto, PracticeSummaryDto, QuestionOutcomeDto, RevealHintResponse (+2 more)

### Community 32 - "User Admin Attempt"
Cohesion: 0.13
Nodes (13): DateTimeOffset, Complaint, DateTimeOffset, ICollection, CreditTransaction, DateTimeOffset, ICollection, Order (+5 more)

### Community 33 - "Criterion Section Grading"
Cohesion: 0.11
Nodes (17): File Structure, Global Constraints, Task 10: Luyện tập — chọn part, làm bài, tổng kết, Task 11: Thanh toán — bảng giá, checkout, cổng giả, Task 12: Kết quả thanh toán và ví lượt thi, Task 13: Shell admin, dashboard và danh sách đề, Task 14: Soạn đề — wizard tạo đề và trình soạn câu hỏi, Task 1: Nền — design token, base layout, state switcher (+9 more)

### Community 34 - "Question Group Audio"
Cohesion: 0.12
Nodes (16): File structure, Global Constraints, Multi-Certificate Companion Schema Implementation Plan, Review Focus, Task 0: Test harness, Task 10: Payment domain (ledger, spend reasons, no-negative-balance, expiry, complaint make-good), Task 11: Finalize — accuracy view + full-suite run, Task 1: Catalog domain (Certificates → Skills → Sections) (+8 more)

### Community 35 - "Order Gateway Code"
Cohesion: 0.16
Nodes (10): microsoft_entityframeworkcore, DateTimeOffset, ICollection, AiGrading, AiGradingScore, DateTimeOffset, ICollection, FreeResponse (+2 more)

### Community 36 - "Section Skill Question"
Cohesion: 0.15
Nodes (10): CertificateOptionDto, LatestResultDto, LearnerContextDto, LearnerDashboardDto, RecentAttemptDto, RecentPracticeDto, SwitchCertificateRequest, HttpContext (+2 more)

### Community 37 - "Skill Score Navigations"
Cohesion: 0.14
Nodes (13): Design Health Score, Design Specificity Verdict, Deterministic Scan: 27 Legitimate Findings, LLM Assessment: Product-Authored, Not Category-Interchangeable, Overall Impression, [P0] Dashboard information overload, [P0] Grading-pending anxiety void, [P1] Em-dash saturation (+5 more)

### Community 38 - "Exam Placement Attempt"
Cohesion: 0.18
Nodes (9): DateTimeOffset, ICollection, Exam, DateTimeOffset, ICollection, ExamAttempt, DateTimeOffset, ICollection (+1 more)

### Community 39 - "Sign Token Http"
Cohesion: 0.24
Nodes (14): Authorize, CancellationToken, CurrentUser, HttpGet, HttpPost, IActionResult, Task, ValidateAntiForgeryToken (+6 more)

### Community 40 - "UI View: DashboardViewModel.cs"
Cohesion: 0.22
Nodes (9): Contracts, WebMVC, WebMVC.Tests, microsoft_aspnetcore_authentication, microsoft_aspnetcore_http, microsoft_extensions_dependencyinjection, AuthClaims, system_reflection (+1 more)

### Community 41 - "UI View: _ViewImports.cshtml"
Cohesion: 0.21
Nodes (8): WebMVC.Models, WebMVC.Controllers, microsoft_aspnetcore_authentication_cookies, microsoft_aspnetcore_authorization, microsoft_aspnetcore_mvc, microsoft_aspnetcore_mvc_filters, DashboardViewModel, ErrorViewModel

### Community 42 - "Path Learner Cert"
Cohesion: 0.14
Nodes (13): Accessibility & Inclusion, Architecture Pivot (2026-09-24), Brand Commitments, Capabilities and Constraints, Evidence on Hand, Operating Context, Platform, Positioning (+5 more)

### Community 43 - "Path Module Item"
Cohesion: 0.17
Nodes (12): 2.10 Mentor Bookings, 2.11 Complaints, 2.1 Guest Creates Account, 2.2 New Learner: Pick Certificate → Placement, 2.3 Free Practice by Section, 2.4 Weakness Analysis (Free), 2.5 Hitting the Paywall: Buy Credits, 2.6 Mock Exams (+4 more)

### Community 44 - "Mentor Payout Period"
Cohesion: 0.11
Nodes (14): DateTimeOffset, ICollection, Booking, DateTimeOffset, ICollection, MentorPayout, MentorPayoutItem, ICollection (+6 more)

### Community 45 - "Path Module Item"
Cohesion: 0.22
Nodes (6): Claim, List, AccessToken, IAccessTokenFactory, int, string

### Community 46 - "Json Companion Api"
Cohesion: 0.25
Nodes (7): HttpRequestException, ApiException, ProblemDetailsBody, system_net_http_headers, system_net_http_json, system_text_json, system_text_json_serialization

### Community 48 - "Question Session Practice"
Cohesion: 0.18
Nodes (10): Gap 1 — nowhere to store per-question timing, Gap 2 — criteria sets cannot vary by question, Gap 3 — one criterion cannot have two maximums, Gap 4 — no recorded length on a free response, Gap 5 — no version history on a re-recording, Gap 6 — a speaking-only attempt has no coherent band, Gap 7 — mentor price is cash, the ledger charges credits, Gap 8 — a cancelled booking has no ledger reason to credit it back (+2 more)

### Community 49 - "Score Band Cert"
Cohesion: 0.29
Nodes (6): Controller, ResponseCache, AdminController, CertificatesController, IActionResult, HomeController

### Community 50 - "Attempt Question Answer"
Cohesion: 0.20
Nodes (8): Agent skills, Build, test, run, Domain docs, graphify, Issue tracker, Knowledge Graph (graphify), Look it up before you write it, Triage labels

### Community 51 - "Import Batch Cert"
Cohesion: 0.29
Nodes (5): DateTimeOffset, ICollection, ImportBatch, DateTimeOffset, ImportLog

### Community 52 - "Question Option Answers"
Cohesion: 0.20
Nodes (9): 0. Actors, 1. High-Level Map, 4. Ordering Constraint — The Part That's Easiest to Get Wrong, 5. Abnormal Branches — Non-Straight Paths, 6. Free / Paid Table — The Final Tally, 7. Out of Scope, 8. What Changed in the Pivot (2026-09-24), Multi-Certificate Companion User Flow by Actor (+1 more)

### Community 53 - "Token Http Cancellation"
Cohesion: 0.22
Nodes (11): CancellationToken, HttpGet, HttpPost, IActionResult, IReadOnlyList, Task, ValidateAntiForgeryToken, ProfileController (+3 more)

### Community 54 - "Date Format The"
Cohesion: 0.29
Nodes (6): DateTimeOffset, string, DateTimeFormatter, DateTimeOffset, Fact, DateTimeFormatterTests

### Community 57 - "Web Api Jwt"
Cohesion: 0.24
Nodes (9): WebAPI, microsoft_aspnetcore_authentication_jwtbearer, microsoft_extensions_options, microsoft_identitymodel_tokens, string, JwtAccessTokenFactory, JwtOptions, Program (+1 more)

### Community 58 - "Band Path Template"
Cohesion: 0.20
Nodes (9): Audience, job, action, Challenger verdicts, Chosen direction, Constraints, Direction contract, Memorable moment, Scope and mode, Surface brief — TOEIC Practice prototype (re-skin) (+1 more)

### Community 59 - "Event Order Date"
Cohesion: 0.22
Nodes (7): 1. Core Architecture & Layering, 2. Knowledge Graph (graphify), 3. Look It Up Before You Write It (Context7 MCP), 4. Ubiquitous Domain Language (`CONTEXT.md`), 5. UI Design System: "Bảng hiệu phòng thi" (`DESIGN.md`), 6. Build & Test Commands, Binding Architectural Invariants

### Community 60 - "Exam Section Audio"
Cohesion: 0.36
Nodes (5): has(), lead(), make(), svg(), trail()

### Community 61 - "Mentor Slot Booking"
Cohesion: 0.25
Nodes (7): AI, Catalog, extra efficient, Language, Learning activity, Money, Scoring

### Community 62 - "Package Collection Credits"
Cohesion: 0.25
Nodes (7): Backlog, Batch A — learner feedback (done; verified 2026-10-12: schema, screens, and domain tests all pass), Batch B — backend groundwork (schema done 2026-10-12; screens pending), Decisions needed before backend planning, Later (decided against for now), Out of scope by decision, Screen-only (no schema change; does not block backend planning)

### Community 63 - "Trg Exams Groups"
Cohesion: 0.25
Nodes (7): Derivable, no column needed, Feature additions — 2026-10-10, Final pass — 2026-10-10, Fixed in the prototype (UI was wrong, not the schema), Fixed in the schema, Left open on purpose, Schema vs. screens audit — 2026-10-08

### Community 64 - "Certificate Read Only"
Cohesion: 0.29
Nodes (5): CertificateDto, IReadOnlyList, CancellationToken, IActionResult, Task

### Community 65 - "Attempt Skill Score"
Cohesion: 0.25
Nodes (8): 1. Mục tiêu, 2. Bối cảnh dự án đích, 4.1 Bậc CEFR, 4. Cấu trúc đề thi TOEIC L&R, 7. State cần vẽ cho mỗi trang, 8. Cấu trúc file prototype, 9. Không làm trong prototype, Hệ thống Ôn và thi chứng chỉ TOEIC — Spec màn hình & state

### Community 66 - "Module Path Topic"
Cohesion: 0.25
Nodes (8): 6. Danh sách màn hình (34), Admin (9), Auth (3), Student — luyện tập (3), Student — thanh toán (5), Student — thi (12), Student — xác thực CCCD (2), Student — điểm yếu (1)

### Community 67 - "Review Booking User"
Cohesion: 0.29
Nodes (6): Conventions, Issue tracker: GitHub, Pull requests as a triage surface, Wayfinding operations, When a skill says "fetch the relevant ticket", When a skill says "publish to the issue tracker"

### Community 68 - "Score Scale Cert"
Cohesion: 0.29
Nodes (6): ceiling, fidelity, Finish review — prototype, keep, material_fixes, persistence

### Community 69 - "Credit Transactions Trg"
Cohesion: 0.33
Nodes (5): Before exploring, read these, Domain Docs, File structure, Flag ADR conflicts, Use the glossary's vocabulary

### Community 71 - "Async Register Sign"
Cohesion: 0.14
Nodes (11): AuthTokenResponse, AuthUserDto, ChangePasswordRequest, ForgotPasswordRequest, ProfileDto, RegisterRequest, ResetPasswordRequest, SignInRequest (+3 more)

### Community 72 - "Token User Email"
Cohesion: 0.50
Nodes (4): 3.1 Phân chia free / trả tiền, 3.2 Cổng KYC chặn trước, trừ credit sau, 3.3 Luồng đầy đủ theo tác nhân, 3. Phạm vi nghiệp vụ

### Community 73 - "Token User Password"
Cohesion: 0.50
Nodes (4): 5.1 Cổng thanh toán (đứng trước phiên thi), 5.2 Phiên thi, 5.3 State từng câu hỏi, 5. State machine

### Community 74 - "Log Batch Date"
Cohesion: 0.50
Nodes (4): 3.1 Exam Import and Authoring, 3.2 Score Conversion Tables, 3.3 Other Admin Flows, 3. Admin — Step by Step

### Community 75 - "Category Cert Collection"
Cohesion: 0.50
Nodes (3): BandDto, SectionDto, SkillDto

### Community 77 - "Web Api Environment"
Cohesion: 0.22
Nodes (8): ASPNETCORE_ENVIRONMENT, profiles, WebAPI, $schema, applicationUrl, commandName, dotnetRunMessages, environmentVariables

### Community 78 - "Web Mvc Environment"
Cohesion: 0.22
Nodes (8): ASPNETCORE_ENVIRONMENT, profiles, WebMVC, $schema, applicationUrl, commandName, dotnetRunMessages, environmentVariables

### Community 79 - "Wwwroot Icons Each"
Cohesion: 0.36
Nodes (5): has(), lead(), make(), svg(), trail()

### Community 80 - "Keyboard Init Setup"
Cohesion: 0.50
Nodes (7): getFocusableElements(), handleEscape(), init(), initModal(), setupExamKeyboardShortcuts(), setupNavigatorKeyboard(), trapFocus()

### Community 85 - "Profile Async Update"
Cohesion: 0.27
Nodes (4): AuthErrorCodes, string, Roles, string

### Community 94 - "Time Zones Contains"
Cohesion: 0.40
Nodes (3): TimeZones, IReadOnlyList, string

### Community 99 - "Favicon Toeic Practice"
Cohesion: 0.67
Nodes (3): Compact Vector Favicon Pattern, TOEIC Practice Favicon, TOEIC Practice Brand Identity

### Community 100 - "Placeholder Part Minimal"
Cohesion: 0.67
Nodes (3): Minimal SVG Placeholder Graphic Pattern, Part 1 Photo Placeholder, TOEIC Part 1 Photographic Comprehension

### Community 101 - "UI View: Find a Mentor"
Cohesion: 0.67
Nodes (3): Credit-Based Mentor Discovery and Booking, Find a Mentor Prototype View, Mentor List Async States Pattern

### Community 102 - "Api Error Codes"
Cohesion: 0.50
Nodes (3): ApiError, Codes, string

### Community 103 - "Bahnschrift Display Typography"
Cohesion: 0.67
Nodes (3): Bahnschrift Display Typography, Sign Blue and Gold Brand Identity, TOEIC Practice Favicon

## Knowledge Gaps
- **512 isolated node(s):** `reset-and-load.sh script`, `run-all.sh script`, `SwitchCertificateInput`, `net10.0`, `Microsoft.NET.Sdk` (+507 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **115 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Contracts` connect `UI View: DashboardViewModel.cs` to `Identity & Authentication Services`, `Section Skill Question`, `Api Error Codes`, `Async Register Sign`, `UI View: _ViewImports.cshtml`, `Category Cert Collection`, `Path Module Item`, `Json Companion Api`, `MVC Learner Dashboard & DTOs`, `Profile Async Update`, `AI Insights & Analytics`, `Web Api Jwt`, `Time Zones Contains`, `Resolved User Complaint`?**
  _High betweenness centrality (0.102) - this node is a cross-community bridge._
- **Why does `AccountEmailTests` connect `Email Delivery & Password Recovery` to `AI Insights & Analytics`?**
  _High betweenness centrality (0.023) - this node is a cross-community bridge._
- **Why does `BusinessObjects` connect `Authentication Views & ViewModels` to `Identity & Authentication Services`, `AI Insights & Analytics`, `Learner Context & Certificate Switching`, `Web Api Jwt`?**
  _High betweenness centrality (0.018) - this node is a cross-community bridge._
- **What connects `reset-and-load.sh script`, `run-all.sh script`, `SwitchCertificateInput` to the rest of the system?**
  _512 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Core Domain Services &` be split into smaller, more focused modules?**
  _Cohesion score 0.1168091168091168 - nodes in this community are weakly interconnected._
- **Should `Identity & Authentication Services` be split into smaller, more focused modules?**
  _Cohesion score 0.07587382779198636 - nodes in this community are weakly interconnected._
- **Should `Database Entity Relational Model` be split into smaller, more focused modules?**
  _Cohesion score 0.09398907103825137 - nodes in this community are weakly interconnected._