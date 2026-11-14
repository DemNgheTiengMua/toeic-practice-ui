# Simplify Exam Authoring, Universal Placement, OOBE, 4-Week Learning Path, Stateless Practice, and AI Grading Rebuild

**Status:** ready-for-agent
**Date:** 2026-11-14
**ADRs touched:** ADR-0001 (database-first EF Core), ADR-0008 (AI grading processor seam), ADR-0015 (React + HeroUI v3 SPA), ADR-0017 (simplify exam authoring, placement, scoring, and learning path; superseding ADR-0012)

---

## Problem Statement

The current certification preparation platform has accumulated excessive structural complexity across six areas that make authoring, onboarding, scoring, learning-path progression, and AI evaluation difficult to understand, operate, and demonstrate:

1. **Over-engineered exam and question bank authoring**:
   - Questions are forced through a six-state item lifecycle (`draft -> validated -> active -> quarantined -> retired -> retired_defective`), four automated ingestion gates (including LLM distractor verification), a blind stratified human audit queue, exam edition versioning, publish audit tables, and structure locks before they can appear in an exam or drill.
   - Creating a new certificate requires configuring 15 low-level section JSON flags, manually building raw-to-scaled score conversion rows, authoring a seven-band rubric descriptor grid (`0..6`) plus anchor essays, running an LLM monotonicity ladder check, and locking the rubric.
   - The demo seed catalog ships five overlapping certificates with thousands of lines of noisy data, making the catalog overwhelming.

2. **Two-stage adaptive placement and dual scoring engines**:
   - Placement attempts use a separate two-stage adaptive bundle drawer and a parallel CEFR cell-mastery / cut-score projection engine (`CefrCutScores`, `LearnerSkillStates`, `PlacementSkillResults`, `UserCellMasteries`, `CalibrationDecisions`) alongside `DerivedFactOwners` single-writer SQL triggers (`WrittenByService`, `ProjectionVersion`), instead of using the standard `AttemptEngine` and certificate `ScoreBands`.
   - Learners are forced to commit to a certificate before taking a placement exam, and every new certificate requires its own separate placement paper.

3. **No guided Out-Of-Box Experience (OOBE) or soft-lock protection for unplaced learners**:
   - A new learner who has not taken the placement exam or configured a target band can click into empty or broken study screens (`Learning Path`, `Practice`, `Weakness Analysis`), or get permanently stuck if they navigate away during onboarding.

4. **Opaque sliding-window learning path with no monetization or general-knowledge fallback**:
   - Learning paths are generated for free via a sliding-window week synthesizer tied to rigid single-cell CEFR coordinates (`Skill × CefrLevel`), throwing `NoQuestionsAvailable` when a specific cell lacks items, ignoring general foundational knowledge (`ALL`), and treating Writing/Speaking steps as ungraded checklists instead of guided lessons with AI grading.

5. **Unbounded free practice and dual session-resume tracking**:
   - Learners can bypass the learning path and drill any topic cold for free, while practice sessions maintain server-side `in_progress` and `abandoned` state transitions that duplicate the exam sitting resume machinery only needed by Mock Exams and the Placement Exam.

6. **Brittle LLM grading and insight guards with plain text outputs**:
   - Writing grading refuses to score any criterion that lacks a seven-band descriptor ladder (`0..6`).
   - Speaking grading returns a mechanical SpeechSuper numeric string without passing the spoken transcription to the LLM for linguistic rubric feedback, inline error correction, or model rewrites.
   - AI Insight generation rejects valid LLM narratives whenever a regex guard fails to match legacy CEFR coordinate strings, and never receives concrete wrong-answer fingerprints from the learner's attempt.

---

## Solution

**Replace the legacy pool lifecycle, ingestion gates, blind audit queue, two-stage adaptive placement draw, CEFR cell-mastery engine, sliding-window path synthesizer, and single-writer triggers completely—leaving zero dead tables, triggers, services, or screens behind:**

1. **Streamlined Certificate Wizard, 2-State Exam Studio, Multi-Band Tagging, and Lean 2-Certificate Seed**:
   - Trim the demo seed catalog strictly to **one 2-skill certificate (`TOEIC` Listening & Reading)**, **one 4-skill certificate (`IELTS` Listening, Reading, Speaking, Writing)**, and **one single Universal Placement Exam**, removing all other seeded certificates (`VSTEP`, `TOEIC_CAT`, `TOEIC_SW`).
   - Simplify the **Certificate Wizard** to a 3-step template flow (`2-Skill` or `4-Skill` template -> 3-field `Sections` (`Code`, `Name`, `ExpectedQuestionCount`) with automatic modality defaults -> `ScoreBands` and flat `GradingCriteria`), automatically generating linear `ScoreScales` rows and immediately working with the single Universal Placement Exam.
   - Reduce **Exam Status** to two states (`draft`, `published`) with one-step authoring/Quick Ingest and direct publishing.
   - Tag every Question (`Items`) and Lesson (`Lessons`) with **`TargetBands`**—either comma-separated certificate/proficiency band codes (e.g., `A2,B1,B2`) or **`ALL`** for general foundational knowledge (`Kiến thức chung`) that applies to all learners.

2. **Single Universal Placement Exam (2-Skill or 4-Skill Scope) & Unified Scoring**:
   - Use **one single Universal Placement Exam** (`purpose = placement`) across the entire platform, sitting on the standard `AttemptEngine`.
   - Before starting the Placement Exam, the learner selects either a **2-skill scope (`listening_and_reading`, graded immediately)** or a **4-skill scope (`all_skills`, including AI-graded Speaking and Writing)**. The first placement sitting is free; retakes charge credits by scope (`placement_retake`).
   - Both Placement and Mock Exams use one unified scoring pipeline (`RawScore -> ScoreScales -> ScaledScore -> ScoreBands`). For the Universal Placement Exam, the learner's normalized per-skill percentage maps directly onto the `ScoreBands` of whichever certificate track they select (`TOEIC` or `IELTS`).

3. **Guided 4-Step Learner OOBE with Post-Placement Certificate Choice, Temporary Skip, and Soft-Lock Empty States**:
   - Unplaced learners enter a guided **Learner OOBE** (`/onboarding`):
     1. **Step 1 — Universal Placement Exam**: Choose **2 skills (`listening_and_reading`)** or **4 skills (`all_skills`)** and sit the single Universal Placement Exam (no certificate selection required beforehand).
     2. **Step 2 — Diagnostic Result & AI Review**: Wait for AI grading (if 4 skills) and inspect per-skill scores, AI insight, and equivalent starting bands across available certificates.
     3. **Step 3 — Choose Certificate Track, Target Band & Study Time**: Select the **Certificate** to study (`TOEIC` 2-skill or `IELTS` 4-skill; if the learner sat the 2-skill placement and chooses a 4-skill certificate, their Listening & Reading diagnostic percentage establishes their estimated starting band), select a **Target Band** above their starting band, and enter their **Exam Date & Weekly Study Hours** to see instant **Goal Feasibility** (`Feasible`, `Stretch`, `Unrealistic`).
     4. **Step 4 — Generate 5-Credit Learning Path**: Pay **5 Credits** (`learning_path` ledger reason, enforcing **Block Before Charge**) to generate their 4-week Learning Path.
   - **Soft-Lock & Temporary Session Skip**:
     - While OOBE is incomplete, normal study links are hidden unless the learner clicks a **temporary session-only skip (`Explore for now` stored in `sessionStorage`)** to explore Mentors, Wallet, or Pricing.
     - While skipped, a **persistent top banner** reminds the learner to resume OOBE, and navigating directly to gated study routes (`/dashboard`, `/learning-path`, `/practice`, `/weakness`, `/speaking`, `/writing`) renders a friendly HeroUI v3 **Soft-Lock Empty State** with a one-click button back to `/onboarding`.
     - Once OOBE is completed (Placement finished and Learning Path / Goal saved), `/onboarding` is soft-locked and redirects to `/dashboard`.

4. **Paid 4-Week Deterministic Learning Path (5 Credits) with Guided AI-Graded Speaking & Writing**:
   - Generating a Learning Path charges **5 Credits** (`Reason = 'learning_path'`, `Delta = -5`), verifies wallet balance before charging, and creates a fixed **4-week sequential plan (`Week 1..4`)** for the chosen certificate.
   - Content selection matches the learner's `[CurrentBand .. TargetBand]` range plus **`ALL`** general-knowledge items (Weeks 1–2 focus on `CurrentBand` + `ALL` and weakest placement skills/topics; Weeks 3–4 progress toward `TargetBand`, with nearest-band fallback so generation never fails with empty content).
   - Steps unlock sequentially (`ConceptLesson` -> `FocusPractice` / `ReviewSpaced` -> milestone `MockSection` in Weeks 2 & 4). Writing and Speaking steps include a structured Guide/Lesson + Rubric + **free AI Grading** (covered by the 5-credit Learning Path fee).

5. **Stateless Free Practice Unlocked Strictly by Learning Path Completion**:
   - Free Practice is **unlocked only for topics and sections the learner has already completed (`done`) in their Learning Path**. Uncompleted topics display a locked badge directing the learner to their Learning Path.
   - Preserve all 6 interactive objective question formats (`mcq`, `mcq_3`, `multi_select`, `inline_completion`, `dropdown`, `drag_drop`) and AI-graded `free_response` with guides and rubrics for Speaking & Writing.
   - **Stateless Practice (No Session Resume)**: Only Mock Exams and the Placement Exam (`ExamAttempts`) track `in_progress` sitting state and support session resume. Practice sessions do not track `in_progress` or `abandoned` states; only completed drill outcomes are recorded, and the HeroUI v3 catalog clearly displays completed accuracy (`%`) vs. locked status so learners can pick up where they left off visually.

6. **Direct Rubric-Grounded LLM Grading & Fact-Grounded AI Insight with Rich HeroUI v3 Displays**:
   - **AI Grading (`Writing` & `Speaking`)**: Grade directly against flat section `GradingCriteria` (`Name`, `RubricDescription`, `MaxScore`, `Weight`), the task prompt/guide checklist, and the learner's Certificate `CurrentBand -> TargetBand` context (combining SpeechSuper acoustic scores + LLM transcript evaluation for Speaking). Return structured JSON containing `overallFeedback`, `priorityFix`, `modelRewrite`, per-criterion `criterionScores`, and inline `errorSpans` (`category`, `originalSnippet`, `correctedSnippet`, `explanationVi`), rendered with HeroUI v3 `Alert`, `ProgressBar` cards, interactive inline highlighted text, a 3-column correction table, and a side-by-side model rewrite card.
   - **AI Insight**: Feed deterministic SQL accuracy facts (Certificate, `CurrentBand -> TargetBand`, per-skill scaled scores, weakest/strongest topics, and up to 5 concrete wrong-answer fingerprints) without legacy CEFR regex rejection guards. Return structured Vietnamese/bilingual JSON (`narrative`, `trend` [`Improving` | `Steady` | `Declining`], `strengths`, `weaknesses`, and prioritized `recommendations` [`High` | `Medium` | `Low`]), rendered cleanly in `AiInsightPanel`.

---

## User Stories

### Streamlined Admin Certificate Wizard, Exam Studio & Lean Seed Catalog

1. As an admin, I want the demo catalog to ship with only one 2-skill certificate (`TOEIC`), one 4-skill certificate (`IELTS`), and one Universal Placement Exam, so that the workspace is clean, fast to navigate, and easy to demonstrate.
2. As an admin creating a new certificate via the Certificate Wizard, I want to pick either a 2-skill (`Listening & Reading`) or 4-skill (`Listening, Reading, Speaking, Writing`) template, so that skills and default section behaviors are pre-populated for me.
3. As an admin configuring a certificate's sections in the Certificate Wizard, I want to enter only the section code, section name, and expected question count, so that I do not have to hand-author 15 low-level JSON timer and layout flags.
4. As an admin saving a certificate in the Certificate Wizard, I want the system to automatically generate linear raw-to-scaled score conversion rows (`ScoreScales`) from each skill's question count and scale range, so that I never have to populate a manual score conversion matrix.
5. As an admin configuring AI grading for a 4-skill certificate, I want to define flat grading criteria (`Name`, `RubricDescription`, `MaxScore`, `Weight`) directly in the Certificate Wizard without authoring a seven-band descriptor grid (`0..6`), anchor essays, or rubric locks, so that AI grading works immediately upon saving the certificate.
6. As an admin creating a new certificate, I want learners who have completed the platform's Universal Placement Exam to immediately see their mapped starting band for my new certificate without me having to author a separate placement exam for that certificate.
7. As an admin in Question Bank Studio, I want a flat two-tab workspace (`Question Bank` and `Lessons`) where every created or imported question and lesson is immediately `active` without passing through automated gates or blind human audit queues.
8. As an admin authoring or importing a question or lesson, I want to tag its `TargetBands` with either `ALL` (foundational knowledge for everyone) or one or more band codes (e.g., `A2,B1,B2`), so that one item can serve multiple target levels.
9. As an admin authoring an exam in Exam Studio or Quick Ingest, I want exams to have only two states (`draft` and `published`) and publish in one click once sections have their questions and answer keys, so that publishing an exam takes seconds.

### Universal Placement Exam & Guided Learner OOBE

10. As a new learner signing in for the first time, I want to be guided into the Learner OOBE (`/onboarding`), so that I have a clear step-by-step path to measure my level and start studying.
11. As a learner on Step 1 of OOBE, I want to sit a single Universal Placement Exam by choosing only between **2 skills (`listening_and_reading`, instant objective grading)** and **4 skills (`all_skills`, including AI-graded Speaking and Writing)** without having to pick a certificate beforehand.
12. As a learner taking my first Universal Placement Exam, I want the sitting to be completely free (0 credits charged), while any subsequent retake charges the configured `placement_retake` credits for the chosen 2-skill or 4-skill scope ("Block Before Charge").
13. As a learner who chose the 2-skill placement scope, I want my Listening and Reading responses to be scored immediately upon submission so that I advance straight to Step 2 of OOBE with zero waiting.
14. As a learner who chose the 4-skill placement scope, I want Step 2 of OOBE to show a live grading progress state while `AiGradingProcessor` grades my Speaking and Writing responses, and automatically display my full 4-skill diagnostic result once grading completes.
15. As a learner viewing my placement result in Step 2 of OOBE, I want to see my per-skill accuracy, AI diagnostic insight, and my equivalent starting band across both the 2-skill certificate (`TOEIC`) and the 4-skill certificate (`IELTS`).
16. As a learner on Step 3 of OOBE (after finishing placement), I want to select which Certificate I want to study (`TOEIC` or `IELTS`), see my mapped `CurrentBand` for that certificate, pick a higher `TargetBand`, and enter my target exam date and weekly study hours.
17. As a learner on Step 3 of OOBE who took the 2-skill placement exam, I want to be able to choose either the 2-skill certificate (`TOEIC`) or the 4-skill certificate (`IELTS`, using my Listening & Reading diagnostic percentage to map my initial starting band), so that my placement sitting unlocks any certificate track I decide to pursue.
18. As a learner configuring my target band and study schedule in Step 3 of OOBE, I want the system to calculate and display my **Goal Feasibility** (`Feasible`, `Stretch`, or `Unrealistic`) along with the required study hours vs. available hours and recommended weekly pace.
19. As a learner on Step 4 of OOBE, I want to generate my 4-week Learning Path for **5 Credits** (`learning_path` ledger reason), with my wallet balance checked before charging ("Block Before Charge").
20. As a learner inside OOBE who wants to look around the platform before committing, I want a temporary **`Explore for now`** button that lets me browse Mentors, Wallet, and Pricing during my current browser session (`sessionStorage`) without permanently skipping OOBE.
21. As a learner who temporarily skipped OOBE (or typed a direct URL like `/practice`, `/learning-path`, `/weakness`, `/speaking`, `/writing`, or `/dashboard` before completing OOBE), I want to see a persistent top banner reminding me to finish OOBE and a friendly HeroUI v3 **Soft-Lock Empty State** on gated study pages with a one-click button back to `/onboarding`.
22. As a learner who has already completed OOBE (finished placement and generated my learning path / goal), I want navigating to `/onboarding` to automatically redirect me to `/dashboard` (soft-lock), while still allowing me to retake the placement exam or update my goal from `/dashboard` or `/learning-path`.

### 4-Week Deterministic Learning Path (5 Credits)

23. As a learner generating a Learning Path, I want 5 credits deducted from my wallet with ledger reason `learning_path` only after verifying that I have a completed placement result, a valid target band, and at least 5 credits available.
24. As a learner, I want my Learning Path to consist of a clear 4-week sequential curriculum (`Week 1` through `Week 4`) tailored to my chosen certificate (`TOEIC` or `IELTS`), progressing from my `CurrentBand` and weakest placement skills/topics in Weeks 1–2 toward my `TargetBand` in Weeks 3–4.
25. As a learner, I want the Learning Path generator to select lessons and questions tagged with bands in my `[CurrentBand .. TargetBand]` range as well as items tagged `ALL` (general foundational knowledge), falling back to the nearest available band so path generation never fails.
26. As a learner working through a week in my Learning Path, I want steps to unlock sequentially: reading the theory/guide lesson (`ConceptLesson`) first, then completing the practice step (`FocusPractice` or `ReviewSpaced`), and sitting a milestone section check (`MockSection`) in Weeks 2 and 4.
27. As a learner on a Writing or Speaking step inside my Learning Path, I want to read a structured writing/speaking guide and rubric checklist before submitting my written essay or spoken recording, and receive full AI grading at **0 extra credits** (included in the 5-credit Learning Path fee).

### Stateless Free Practice Unlocked by Path Completion

28. As a learner visiting the Practice catalog (`/practice`), I want only the sections and topics that I have already completed (`done`) in my Learning Path to be unlocked for free self-practice, while upcoming uncompleted topics show a locked badge with a link to my Learning Path.
29. As a learner on the Practice catalog, I want each unlocked topic/section card to display my historical completion badge and accuracy percentage (`%`) so I can visually scan and pick what to review next without needing a server-side "resume in-progress drill" state.
30. As a learner drilling an unlocked objective practice set, I want to practice across all 6 interactive question formats (`mcq`, `mcq_3`, `multi_select`, `inline_completion`, `dropdown`, `drag_drop`) and see instant answer explanations upon finishing the set.
31. As a learner practicing an unlocked Speaking or Writing task outside my Learning Path, I want to view the task guide and rubric criteria, submit my response for AI grading (charging 1 credit `ai_grading` with "Block Before Charge"), and review the annotated AI feedback.
32. As a learner, I want session resume (`in_progress` state recovery and countdown continuation) to apply strictly to **Mock Exam** and **Placement Exam** sittings (`ExamAttempts`), never to Practice drills.

### Rich LLM Grading & Diagnostic AI Insight UI

33. As a learner viewing my graded Writing or Speaking result, I want to see a prominent HeroUI v3 **Priority Fix** alert highlighting the single most impactful change I should make to reach my `TargetBand`.
34. As a learner viewing my graded Writing or Speaking result, I want to see per-criterion score cards (`score / maxScore`) with color-coded progress bars and specific feedback grounded in my certificate's rubric criteria.
35. As a learner viewing my graded Writing or Speaking result, I want my submitted text (or SpeechSuper spoken transcription) to highlight inline `errorSpans` (`grammar`, `vocabulary`, `cohesion`, `pronunciation`) and display a 3-column correction breakdown (`Original Snippet -> Corrected Snippet -> Vietnamese Explanation`).
36. As a learner viewing my graded Writing or Speaking result, I want to read a **Model Rewrite** card showing how my own response would be written or spoken at my `TargetBand` level.
37. As a learner viewing my graded Speaking result, I want SpeechSuper's acoustic metrics (`Pronunciation`, `Fluency`, `Completeness`) combined with Claude's linguistic rubric evaluation of my spoken transcript on the same result screen.
38. As a learner viewing the **AI Insight Panel** on my Placement Result, Mock Exam Review, or Weakness Analysis page, I want to see a trend badge (`Improving`, `Steady`, `Declining`), a concise Vietnamese narrative grounded in my actual scores and wrong-answer fingerprints, my top strengths vs. core weaknesses, and 3 prioritized study recommendations (`High`, `Medium`, `Low`) linking to my Learning Path or unlocked Practice topics.

---

## Implementation Decisions

### 1. Full eradication of legacy pool/gate/audit/CEFR/sliding-window artifacts

All tables, triggers, columns, entities, DTOs, services, endpoints, and UI pages belonging to the retired complexity are removed outright:

- **Database objects dropped from `docs/database-schema.sql`**:
  - Single-writer guard infrastructure: `DerivedFactOwners` table, `trg_PlacementTests_SingleWriter`, `trg_ExamAttempts_SingleWriter`, `trg_LearnerPathSteps_SingleWriter`, and the `WrittenByService` / `ProjectionVersion` columns on `PlacementTests`, `ExamAttempts`, and `LearnerPathSteps`.
  - Pool gates, telemetry & blind stratified audit: `ReviewStrata`, `ReviewSampleItems`, `ReviewSampleDecisions`, `ItemTelemetryEvaluations`, `ContentQuotas`, `trg_Items_LifecycleTransitions`.
  - Two-stage adaptive placement draw: `AttemptStageBundles`, `AttemptItemResponses`.
  - Parallel CEFR projection & cell mastery: `CefrCutScores`, `CefrCutScoreVersions`, `LearnerSkillStates`, `PlacementSkillResults`, `UserCellMasteries`, `CalibrationDecisions`.
  - Sliding-window path & exam/rubric locks: `LearnerPathWeeks`, `ExamEditions`, `ExamPublishAudits`, `GradingRubricLocks`, `GradingCriterionDescriptors`, `GradingCriterionAnchors`.
  - Practice state-transition trigger: `trg_PracticeSessions_StatusTransitions`.
- **Backend services deleted from `src/Services`**:
  - `PoolGateOneService`, `PoolGateTwoService`, `PoolGateThreeService`, `PoolGateFourService`, `GateThreeVerifier`, `ItemPoolLifecycleService`, `BlindAuditService`, `PlacementStageOneDrawService`, `PlacementStageTwoDrawService`, `CefrProjectionService`, `LevelClassifier`, `CalibrationService`, `MasteryService`, `PracticeMatrixService`, `PathWindowSynthesizer`, `RubricGridWriter`, `RubricLadderVerifier`, and `InsightNarrativeGuard`.
- **Frontend pages deleted from `src/web`**:
  - `AdminAuditBatchPage.tsx` and the Rubric Studio / Score Conversion matrix tabs inside `CertificatesStudioPage.tsx`.

### 2. Schema additions & simplifications (`docs/database-schema.sql` & `docs/seed-demo.sql`)

- **`TargetBands` on `Items` and `Lessons`**:
  - Add `TargetBands VARCHAR(100) NOT NULL CONSTRAINT DF_Items_TargetBands DEFAULT 'ALL'` to `Items` and `TargetBands VARCHAR(100) NOT NULL CONSTRAINT DF_Lessons_TargetBands DEFAULT 'ALL'` to `Lessons`.
  - Stores either `'ALL'` (foundational knowledge applicable to every band) or a comma-separated list of band codes (e.g., `'A2,B1,B2'`, `'450,550,650'`, `'5.5,6.0,6.5'`).
- **`CreditTransactions` (`learning_path` reason)**:
  - Add `'learning_path'` to `CK_CreditTxn_Reason`.
  - In `CK_CreditTxn_DeltaSign`, require `Delta < 0` when `Reason = 'learning_path'` (specifically `-5` credits enforced by service and check constraint).
  - In `CK_CreditTxn_Source`, allow `Reason = 'learning_path'` with `OrderId IS NULL AND AttemptId IS NULL AND BookingId IS NULL`.
- **`PlacementTests` universal support**:
  - Make `CertId INT NULL` on `PlacementTests` (or bind the universal placement exam to the platform catalog while storing normalized skill percentages `ListeningPct`, `ReadingPct`, `SpeakingPct`, `WritingPct`, `OverallPct` alongside `AttemptId` and `Scope`), so a learner can sit the single Universal Placement Exam before choosing a certificate track in OOBE Step 3. When the learner selects a certificate (`TOEIC` or `IELTS`), `PlacementTests.CertId` and `RecommendedBandCode` are updated to reflect the chosen certificate's mapped band.
- **`AiGradings` structured feedback columns**:
  - Add `PriorityFix NVARCHAR(1000) NULL`, `ModelRewrite NVARCHAR(MAX) NULL`, and `ErrorSpansJson NVARCHAR(MAX) NULL` to `AiGradings` so inline error spans and model rewrites persist alongside `Feedback` and `AiGradingCriteriaScores`.
- **`PracticeSessions` stateless completed model**:
  - Default `PracticeSessions.Status` to `'completed'` (`CompletedAt` set on creation/submission). Remove `in_progress` and `abandoned` session resume queries from `PracticeService`.
- **Lean Demo Seed (`docs/seed-demo.sql`)**:
  - Keep only `CertId = 1` (`TOEIC`, 2 skills: Listening & Reading) and `CertId = 2` (`IELTS`, 4 skills: Listening, Reading, Speaking, Writing).
  - Seed **1 Universal Placement Exam** (`purpose = 'placement'`) containing 4 sections (Listening, Reading, Speaking, Writing) so it serves both `listening_and_reading` (2-skill) and `all_skills` (4-skill) placement sittings.
  - Seed 1 published Mock Exam per certificate (`TOEIC` and `IELTS`), plus rich multi-format `Items` and `Lessons` tagged with `'ALL'` and band ranges so 4-week Learning Paths generate cleanly for any band combination.

### 3. Universal Placement Exam, OOBE Flow & Post-Placement Certificate Mapping

- **Starting Universal Placement (`IPlacementService.StartPlacementTestAsync`)**:
  - Accepts `Scope` (`listening_and_reading` or `all_skills`; `CertId` is optional and defaults to the universal placement exam's host certificate).
  - First placement sitting for a learner is free (`0` credits); subsequent retakes charge `placement_retake` credits by scope via `ICreditLedger` ("Block Before Charge").
  - Starts an `ExamAttempt` on the single published Universal Placement Exam via `IAttemptEngine`, filtering active sitting sections to `Listening + Reading` when `Scope == listening_and_reading` or all 4 skills when `Scope == all_skills`.
- **Cross-Certificate Band Mapping (`IPlacementService.GetPlacementResultAsync`)**:
  - Computes each tested skill's normalized percentage (`0..100%`) and overall percentage (`OverallPct`).
  - For every active certificate in the catalog (`TOEIC`, `IELTS`, plus any newly created admin certificate), maps `OverallPct` onto that certificate's `ScoreBands` range (`MinTotal .. MaxTotal`) to return `CertificateBandPreviewDto(CertId, CertCode, CertName, SkillCount, MappedScaledScore, MappedBandCode, MappedBandName)`.
  - If the learner sat the 2-skill scope (`listening_and_reading`), `OverallPct` is the average of Listening and Reading percentages and maps to both 2-skill (`TOEIC`) and 4-skill (`IELTS`) certificates so the learner can freely pick either certificate in OOBE Step 3.
- **OOBE State & Soft-Lock (`GET /api/learner/onboarding-state`, `OnboardingPage.tsx`, `AppShell.tsx`)**:
  - `OnboardingStateDto` reports:
    - `HasCompletedPlacement` (`bool`)
    - `ActivePlacementAttemptId` (`int?`, for resuming an in-progress placement sitting or polling pending 4-skill AI grading)
    - `HasTargetGoal` (`bool`)
    - `HasActiveLearningPath` (`bool`)
    - `IsOobeComplete` (`HasCompletedPlacement && HasTargetGoal && HasActiveLearningPath`).
  - When `!IsOobeComplete`:
    - `AppShell.tsx` hides standard study links unless `sessionStorage.getItem('ee_oobe_skip_session') === '1'`, and renders a persistent top banner (`"Bạn chưa hoàn tất thiết lập lộ trình học — Tiếp tục thiết lập (OOBE)"`) whenever skipped.
    - Direct navigation to `/dashboard`, `/learning-path`, `/practice`, `/weakness`, `/speaking`, or `/writing` while `!IsOobeComplete` renders a HeroUI v3 `OobeSoftLockEmptyState` card explaining what is locked and providing a primary button to `/onboarding` and a secondary link to `/mentors`.
  - When `IsOobeComplete`:
    - Navigating to `/onboarding` redirects to `/dashboard`.

### 4. Paid 4-Week Deterministic Learning Path (`ILearningPathService`)

- **Cost & Preconditions (`GeneratePathAsync`)**:
  - Constants: `LearningPathCostCredits = 5`, `WeekCount = 4`.
  - Verifies that the learner has a completed placement test, a valid selected `CertId` and `TargetBandCode` above `CurrentBandCode`, and `WalletBalance >= 5` ("Block Before Charge").
  - Posts a `-5` credit transaction with `Reason = 'learning_path'` in the same transaction as creating the `LearnerPaths` and `LearnerPathSteps` rows.
- **4-Week Curriculum Construction**:
  - Identifies all certificate `ScoreBands` between `CurrentBand` and `TargetBand` (inclusive).
  - Ranks the certificate's skills and sections by the learner's placement accuracy (lowest accuracy first; for unmeasured Speaking/Writing after a 2-skill placement on a 4-skill certificate, treats Speaking/Writing as priority foundation skills).
  - Builds 4 weeks (`WeekNumber = 1..4`):
    - **Weeks 1 & 2 (Foundation & Core Repair)**: Selects `Lessons` and `Items` matching `CurrentBand` or `'ALL'` (falling back to nearest band if needed) for the weakest sections. Week 1 has `ConceptLesson` -> `FocusPractice` -> `ReviewSpaced`; Week 2 adds a `MockSection` checkpoint.
    - **Weeks 3 & 4 (Target Band Acceleration & Mastery)**: Selects `Lessons` and `Items` matching intermediate/target bands (`TargetBand`) or `'ALL'` (with nearest-band fallback). Week 3 has `ConceptLesson` -> `FocusPractice` -> `ReviewSpaced`; Week 4 concludes with `ConceptLesson` -> `FocusPractice` -> final `MockSection`.
  - Opens the very first step of Week 1 (`Status = 'open'`); all subsequent steps start as `'locked'` and unlock sequentially as each prior step transitions to `'done'`.
  - Completing a Writing or Speaking step inside the Learning Path triggers `IAiGradingProcessor` at `0` additional credit cost.

### 5. Stateless Free Practice Unlocked by Path Completion (`IPracticeService`)

- **Unlock Rule**:
  - A section or topic for the learner's active certificate is `IsUnlocked = true` in `GetPracticeCatalogAsync` **if and only if** the learner has at least one `LearnerPathSteps` row with `Status == 'done'` covering that section/skill in their active Learning Path.
  - Attempting to start a free practice drill on a locked section throws `PracticeSectionLockedException` (HTTP 403 ProblemDetails pointing the learner to their Learning Path).
- **Stateless Execution**:
  - `PracticeService` does not create or resume `in_progress` practice sessions. Starting a drill returns the question set and lesson/guide metadata; submitting answers writes the completed `PracticeSessions` and `PracticeAnswers` rows in one shot and returns the scored review payload + updated accuracy percentage.

### 6. Direct Rubric LLM Grading & Fact-Grounded AI Insight (`IAiGrader`, `IAiInsightGenerator`)

- **Writing & Speaking Grading (`ClaudeAiGrader`, `SpeechSuperAiGrader`, `FakeAiGrader`)**:
  - Removes the `GradingCriterionDescriptors` (`!c.IsGradeable`) guard. Grades every `GradingCriteria` row defined on the certificate/skill using `Name`, `RubricDescription`, `MaxScore`, and `Weight`.
  - For Speaking, `SpeechSuperAiGrader` first obtains acoustic scores (`Pronunciation`, `Fluency`, `Completeness`) and the spoken `Transcription` from SpeechSuper, then invokes `ClaudeAiGrader` (or structured rubric evaluation) over the transcription + acoustic telemetry so Speaking results include full rubric criterion feedback, `PriorityFix`, `ModelRewrite`, and `ErrorSpans` alongside pronunciation metrics.
  - `FakeAiGrader` (used in local dev/demo and automated tests) deterministically produces realistic `PriorityFix`, `ModelRewrite`, and `ErrorSpans` (referencing actual snippets from the learner's submission) so offline demos and E2E tests render the complete UI.
- **Diagnostic AI Insight (`ClaudeAiInsightGenerator`, `FakeAiInsightGenerator`)**:
  - Removes `InsightNarrativeGuard` CEFR coordinate regex rejections.
  - Builds a deterministic prompt payload containing: Certificate Name, `CurrentBand -> TargetBand`, per-skill scaled scores, top 3 weakest and top 2 strongest sections/topics (`Correct/Answered`, `Accuracy %`), and up to 5 wrong-answer fingerprints (`Topic`, `QuestionStem`, `ChosenOption`, `CorrectOption`, `Explanation`).
  - Returns structured JSON (`narrative`, `trend`, `strengths`, `weaknesses`, `recommendations`) persisted in `AiInsights` and `Recommendations`.

---

## Testing Decisions

A good test verifies observable behavior at the highest module boundary — SQL constraints and triggers enforced against a real SQL Server schema, service methods called through their public interfaces (`ICertificateService`, `IPlacementService`, `ILearningPathService`, `IPracticeService`, `IAiGradingProcessor`, `IAiInsightService`), and React + HeroUI v3 page components rendered under Vitest — never private helpers or internal implementation details.

### Proposed Testing Seams

1. **Database Schema & Seed Harness Seam (`docs/schema-tests/run-all.sh` & `tests/Services.Tests/DemoSeedTests.cs`)**:
   - **What is tested**:
     - Retired tables (`DerivedFactOwners`, `ReviewStrata`, `ReviewSampleItems`, `ReviewSampleDecisions`, `AttemptStageBundles`, `AttemptItemResponses`, `CefrCutScores`, `CefrCutScoreVersions`, `LearnerSkillStates`, `PlacementSkillResults`, `UserCellMasteries`, `CalibrationDecisions`, `LearnerPathWeeks`, `ExamEditions`, `ExamPublishAudits`, `GradingRubricLocks`, `GradingCriterionDescriptors`, `GradingCriterionAnchors`) and single-writer triggers are absent (`DataDispositionTests`).
     - `Items` and `Lessons` enforce `TargetBands` default `'ALL'`; `CreditTransactions` accepts `Reason = 'learning_path'` with `Delta = -5`; `AiGradings` stores `PriorityFix`, `ModelRewrite`, and `ErrorSpansJson`.
     - `docs/seed-demo.sql` loads cleanly with exactly 2 active certificates (`TOEIC` and `IELTS`) and 1 Universal Placement Exam.
   - **Prior art**: `docs/schema-tests/run-all.sh`, `tests/Services.Tests/DataDispositionTests.cs`, `tests/Services.Tests/DemoSeedTests.cs`.

2. **Certificate Wizard & Exam Authoring Service Seam (`ICertificateService`, `IAdminExamService`, `IAdminItemService`, `IAdminLessonService`)**:
   - **What is tested**:
     - Creating a 2-skill or 4-skill certificate via `SaveCertificateWizardAsync` with 3-field sections, `ScoreBands`, and flat `GradingCriteria` automatically generates linear `ScoreScales` rows and requires no 7-band descriptor grid or rubric lock.
     - Creating/importing questions and lessons with `TargetBands` (`'ALL'` or `'A2,B1,B2'`) makes them immediately active and publishable in a 2-state (`draft` -> `published`) exam.
   - **Prior art**: `tests/Services.Tests/CertificateWizardTests.cs`, `tests/Services.Tests/AdminExamServiceTests.cs`.

3. **Universal Placement, OOBE & 4-Week Learning Path Service Seam (`IPlacementService`, `ILearningPathService`)**:
   - **What is tested**:
     - Sitting the single Universal Placement Exam in 2-skill (`listening_and_reading`) or 4-skill (`all_skills`) scope: first attempt is free, retakes deduct `placement_retake` credits, and completion returns mapped starting bands across all active certificates (`TOEIC`, `IELTS`, and any newly created certificate).
     - `GeneratePathAsync` enforces "Block Before Charge" for the **5-credit** `learning_path` fee, validates `GoalFeasibility`, and generates a 4-week sequential plan (`Week 1..4`) using `[CurrentBand .. TargetBand]` + `'ALL'` items (with nearest-band fallback so it never throws `NoQuestionsAvailable`).
     - Completing a step unlocks the next step sequentially; Writing/Speaking steps inside the path trigger AI grading without charging extra credits.
   - **Prior art**: `tests/Services.Tests/PlacementServiceTests.cs`, `tests/Services.Tests/LearningPathServiceTests.cs`, `tests/Services.Tests/PathStepCompletionTests.cs`.

4. **Stateless Practice & AI Grading / Insight Seam (`IPracticeService`, `IAiGradingProcessor`, `IAiInsightService`)**:
   - **What is tested**:
     - `GetPracticeCatalogAsync` marks sections/topics as unlocked (`IsUnlocked = true`) only when the corresponding Learning Path step is `'done'`; attempting to drill a locked section is rejected.
     - Submitting a practice set records a `'completed'` session directly with no `in_progress` resume state.
     - `IAiGradingProcessor` grades Writing and Speaking against flat `GradingCriteria` and populates per-criterion scores, `PriorityFix`, `ModelRewrite`, and `ErrorSpansJson`.
     - `IAiInsightService` generates and stores a structured narrative and recommendations from attempt accuracy and wrong-answer fingerprints without legacy CEFR guard rejections.
   - **Prior art**: `tests/Services.Tests/PracticeServiceTests.cs`, `tests/Services.Tests/AiGradingProcessorTests.cs`, `tests/Services.Tests/AiInsightServiceTests.cs`.

5. **React 19 + HeroUI v3 Frontend Seam (`src/web` Vitest component suites)**:
   - **What is tested**:
     - `OnboardingPage` 4-step OOBE flow (2/4-skill placement choice -> diagnostic result -> post-placement certificate selection + target band + feasibility -> 5-credit path generation), temporary session skip (`sessionStorage`), persistent return banner in `AppShell`, and `OobeSoftLockEmptyState` on gated pages.
     - `WritingResultPage`, `SpeakingResultPage`, and `AiInsightPanel` rendering `PriorityFix`, inline highlighted `errorSpans` + correction table, `ModelRewrite`, criterion progress bars, and prioritized recommendations.
     - `CertificatesStudioPage`, `ExamsStudioPage`, and `QuestionBankStudioPage` rendering the simplified wizard, 2-tab bank (`TargetBands`), and 2-state exam publishing without `AdminAuditBatchPage`.
   - **Prior art**: `src/web/src/features/admin/studio/__tests__/certificates-studio.test.tsx`, `src/web/src/features/learner/__tests__/`.

---

## Out of Scope

- Mobile companion client (`D:\Study\PRM_Android\Projects\extra_efficient_mobile`) — explicitly excluded by the project owner.
- Adaptive item-response-theory (IRT) parameter calibration or multi-stage adaptive test assembly.
- Server-side session resume for untimed free practice drills (only Mock Exam and Placement Exam attempts support session resume).
- Automated multi-evaluator human grading workflows or blind stratified item review queues.

---

## Further Notes

- All frontend components in `src/web` must strictly use HeroUI v3 (`@heroui/react` + `@heroui/styles`) compound components (`Card.Header`, `Card.Content`, `Modal.Dialog`, `Tabs.List`, `Table`, `Alert`, `Chip`, `Select`, `TextField`, `NumberField`) and Signage Brand Hues (`--accent: #0b4ea2`, `--success: #0a6b3c`, `--warning: #f2b705`, `--danger: #b31217`) per `DESIGN.md`, verified via the `heroui-react` MCP.
