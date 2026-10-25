```[tasklist]
### Implementation Tasks
- [ ] #107
- [ ] #116
- [ ] #120
- [ ] #117
- [ ] #118
- [ ] #121
- [ ] #108
- [ ] #109
- [ ] #110
- [ ] #111
- [ ] #112
- [ ] #113
- [ ] #114
- [ ] #119
- [ ] #115
```

## Problem Statement

Learners, educators, and administrators on the platform face three deeply connected problems across certificate definition, exam authoring/import, and exam delivery:

1. **Test-Taker Experience & Exam Authenticity Gap:**
   Learners preparing for international and national English certifications (IELTS on Computer, TOEIC Standard CBT, TOEIC Multi-Stage Adaptive CAT, TOEIC Speaking & Writing, VSTEP, or custom institutional certificates) need an exam simulation environment that mirrors the exact user experience, timing constraints, and navigation mechanics of official test platforms. Currently, the platform only supports generic 4-option multiple-choice questions and basic free-response inputs with a single countdown timer and unrestricted question jumping. Candidates cannot experience:
   - Irreversible macro-level section locking (e.g., permanently locking Listening once Reading begins) with zero time rollover.
   - Section-specific micro-navigation rules (audio-driven one-way progression, two-stage adaptive CAT unit locks, automated prep-beep-record speaking pacers, and hybrid group-locked writing timers).
   - Certification-authentic interfaces and tools (IELTS 100vh vertical split-screen workspace, word-tokenized persistent highlighting and notes, live word counting without browser spellcheck, 2-minute post-audio listening review timer, TOEIC Part 1/2 hidden option text, Part 3/4 3-question clusters, and TOEIC Writing on-screen Cut/Paste/Undo/Redo toolbar with blocked keyboard clipboard shortcuts).
   - Mandatory pre-flight audio pre-buffering and interactive microphone verification before starting listening or speaking sections.
   - Wall-clock deadline synchronization resilient against browser tab throttling, sleep modes, and unexpected crashes.

2. **Rigid, Tech-Heavy Certificate Configuration:**
   Every standardized certificate has its own rules for skills, sections, timing, navigation, screen layout, allowed question components, stimulus requirements, candidate tools, section weights, and score aggregation. Currently, rules are either missing or hardcoded by certificate code (`CASE c.Code WHEN 'IELTS'...`) inside database triggers and C# code. Non-technical educators lack a step-by-step, plain-language way to create or customize a certificate (or start from an official preset) and have the rest of the system obey those rules automatically.

3. **Disconnected, Unguided Exam Authoring & Content Ingest:**
   Currently, creating an exam in Exam Studio, pasting questions in Quick Ingest, or uploading a Manifest ZIP/JSON does not dynamically adapt to the structure configured on the target Certificate. Database constraints also restrict questions to single-correct MCQ (`UX_Options_OneCorrect`) or free response (`CK_Questions_Type`, `CK_Questions_Words`), blocking IELTS multi-select questions, inline text completion with word limits, dropdown matching, drag-and-drop tasks, and TOEIC Writing mandatory keywords. Educators need Exam Studio and all import/AI tools to **dynamically morph their tabs, slots, input fields, templates, and validation rules based on the chosen Certificate's configuration**.

## Solution

Rebuild the entire exam lifecycle around a **Single Chain of Authority**: **Certificate Wizard (Defines the 6 Rule Axes & Section Blueprints) ➔ Guided Exam Studio & Ingest (Dynamically Adapts to the Chosen Certificate) ➔ Sitting & Scoring Engine (Executes the Certificate's Rules)**:

1. **Certificate-Agnostic 6-Axis Rule Engine & 4-Step Linear Guided Certificate Wizard**:
   - Eliminate all hardcoded certificate-name branching in C# and SQL triggers. Every certificate stores its complete ruleset across 6 data-driven axes on `Certificates`, `Skills`, and `Sections`:
     1. **Score Aggregation Rule** (`sum`, `avg_half_step`, `avg_whole`, `independent`)
     2. **Section Score Weight** (e.g., Writing Task 2 weighted `2.0` vs Task 1 `1.0`)
     3. **Micro-Navigation Mode** (`FULL_SANDBOX`, `UNIT_LOCKED_SANDBOX`, `AUDIO_DRIVEN`, `SYSTEM_PACED_AUTO`, `HYBRID_GROUP_LOCKED`)
     4. **Timer Policy** (`SECTION_SHARED`, `AUDIO_PLUS_CHECK`, `ITEM_FIXED`, check time, warning thresholds)
     5. **Layout, Stimulus & Item Blueprint** (`FULL_WIDTH` vs `VERTICAL_SPLIT_SCREEN`, `ExpectedQuestionCount`, `OptionCount`, `ClusterSize`, `HideOptionText`, `AllowedComponentTypes`, `RequirePassage`, `RequireAudio`, `RequireImage`, `RequiredKeywordCount`, `UnitCount`)
     6. **Candidate Tools Policy** (`AllowHighlight`, `AllowNotes`, `ShowWordCount`, `DisableSpellCheck`, `OnScreenCutPasteOnly`, `ConfirmOnNext`)
   - Provide a **4-Step Linear Guided Certificate Wizard** for non-technical users:
     - **Step 1 (1-Click Official Blueprint Presets):** Pick from visual cards (*IELTS Academic/GT*, *TOEIC L&R Standard*, *TOEIC L&R CAT*, *TOEIC S&W*, *VSTEP B1–B2–C1*, or *Custom Blank*) to pre-fill 100% of skills, sections, item rules, tools, rubrics, score scales, and CEFR bands in one click.
     - **Step 2 (Skills & Timing):** Order skills linearly and pick timing behaviors in plain everyday language.
     - **Step 3 (Section Behavior, Layout, Allowed Question Types & Tools):** Toggle visual switches in plain educator language with zero JSON exposure.
     - **Step 4 (Scoring Rules & Live Score Preview Calculator):** Configure aggregation and section weights, and test sample raw scores in an interactive live calculator that immediately previews scaled scores and CEFR bands before saving.

2. **Certificate-Driven Section-Tab Guided Exam Studio & Ingest Pipeline**:
   - When an educator creates an exam and selects **any Certificate** (built-in or custom-created via the Certificate Wizard), **Exam Studio, Quick Ingest, Manifest Import/Export, and AI Exam Generator dynamically read that Certificate's persisted Section Blueprints**:
     - **Auto-Scaffolded Skill & Section Tabs:** Exam Studio automatically generates tabs for each Skill and Section configured on that Certificate, pre-building the exact number of question slots, clusters, or adaptive units (`Unit 1` / `Unit 2`) specified by the Certificate.
     - **Context-Aware Guided Slot Forms:** Inside each Section Tab, Exam Studio only renders the fields dictated by that Section's blueprint (e.g., if a section's blueprint specifies `OptionCount = 3` and `HideOptionText = true`, the studio locks slots to 3 options A–C and hides passage inputs; if `ClusterSize = 3`, it groups slots in 3s with audio timestamp pickers; if `RequiredKeywordCount = 2`, it renders 2 mandatory keyword inputs; if `VERTICAL_SPLIT_SCREEN`, it renders the left passage editor and right question group builder restricted to that section's `AllowedComponentTypes`).
     - **Certificate-Driven Quick Ingest, Manifest Templates & AI Generation:** The Quick Ingest modal, downloadable `manifest-template.json`, publish-readiness checklist, and Claude AI section generator all dynamically adapt their syntax guides, templates, and validation checks to the active Certificate and Section's blueprint rules.

3. **Practice Mode Alignment & High-Fidelity Sitting/Scoring Engine**:
   - Align Practice Drilling (Mode B), Placement Testing, and Weakness Analysis (`vw_UserCategoryAccuracy`, `vw_UserSectionAccuracyAll`) with all 11 question components.
   - Enforce pre-flight audio buffering and microphone verification, wall-clock deadlines, macro/micro state machine lockouts, 500ms debounced auto-save with `AuthorizationCode` crash recovery, and flat "Bảng hiệu phòng thi" workspaces driven entirely by the Certificate's 6 rule axes.

## User Stories

### Non-Technical Educator & Administrator — Guided Certificate Wizard (Step-by-Step)
1. As a non-technical administrator creating a certificate, I want a 4-Step Linear Guided Wizard starting with 1-click Official Blueprint Presets (*IELTS*, *TOEIC Standard*, *TOEIC CAT*, *TOEIC S&W*, *VSTEP*, or *Custom*), so that I can set up a compliant certificate in seconds without knowing technical configuration codes.
2. As a non-technical administrator in Step 2 of the Certificate Wizard, I want to arrange skill order and choose how each skill's clock works using plain-language cards (*Shared skill timer*, *Audio playback + extra review countdown*, or *Per-question prep & recording timer*), so that macro-progression and timing are configured effortlessly.
3. As a non-technical administrator in Step 3 of the Certificate Wizard, I want to configure each section's navigation behavior, screen layout, stimulus requirements (passage, audio, image), option count (3 vs 4), question clustering, required keywords, and allowed candidate tools using plain-language toggle switches, so that I never touch raw JSON.
4. As a non-technical administrator in Step 4 of the Certificate Wizard, I want to select the score aggregation rule, adjust section weights (e.g., *Task 2 counts 2× Task 1*), and type sample raw scores into a Live Score Preview Calculator to see the exact scaled scores and CEFR band computed on screen, so that I can verify my scoring rules before saving.
5. As an administrator managing score conversion tables, I want the conversion editor to automatically size each skill's raw score ceiling from the certificate's configured question counts and validate monotonicity and non-overlapping bands, so that any custom or official certificate scale is safe.

### Content Author — Certificate-Driven Guided Exam Studio & Ingest
6. As a content author creating a new exam, I want Exam Studio to read the selected Certificate's section configurations and automatically scaffold the exact Skill Tabs, Section Tabs, expected question slots, cluster groups, and CAT unit tabs, so that the exam structure is ready immediately.
7. As a content author working in Exam Studio, I want to switch freely between Skill and Section tabs while having each tab present a linear, mistake-proof form that only displays the inputs required by that Section's certificate rules (such as locking 3 options A–C when the section specifies 3 options, showing 2 mandatory keyword boxes when the section requires 2 keywords, or showing a split-screen passage editor when the section uses split-screen layout), so that I can never author a question that violates the certificate's format.
8. As a content author in Exam Studio, I want the question component selector in each section to offer only the question types enabled for that section across the 11 supported components (Single Choice 4-option, Single Choice 3-option, Multi-Select Checkbox with selection cap, Inline Text Completion with accepted answers and word/number limits, Dropdown Matching, Drag-and-Drop Labelling, and Speaking/Writing Free Responses), so that authoring stays strictly compliant with the certificate.
9. As a content author in Exam Studio, I want a live Pre-Publish Readiness Checklist that validates every section against the chosen Certificate's blueprint rules and lets me click any warning to jump straight to the incomplete question slot, so that I can fix missing items and publish with confidence.
10. As a content author opening the Quick Ingest modal inside any Section Tab, I want the modal's instructions, sample copy-paste template, and parser to dynamically adapt to that Section's certificate rules (e.g., rapid answer-key list for audio-only sections, completion syntax with `MAX_WORDS` for gap-fill sections, or keyword pairs for sentence-writing sections), so that bulk pasting is effortless.
11. As a content author using AI generation inside Exam Studio, I want to click "AI Fill Missing Slots" on any section (or full exam) and have the AI generator obey that section's exact certificate blueprint rules (option count, cluster size, allowed question components, word limits, and required keywords), so that AI-generated items drop cleanly into the section slots.
12. As a content author importing or exporting exams via ZIP/JSON packages (`ADR-0007`), I want to download a pre-structured `manifest-template.json` generated dynamically from the chosen Certificate's blueprint and have the batch importer validate uploaded packages against that Certificate's section rules, so that offline authoring and round-trip exports are 100% consistent.

### Candidate — Pre-Flight, State Machine, Sitting Workspaces & Scoring
13. As a candidate entering any Listening or Speaking exam, I want all audio assets pre-buffered and verified before the Start button unlocks, so that network blips never interrupt an unpausable audio stream mid-exam.
14. As a candidate entering a Speaking exam, I want an interactive microphone check where I record a short phrase, listen back, and confirm clarity (Yes/No), so that my recording setup is verified before timing begins.
15. As a candidate taking a multi-skill exam, I want the exam to progress strictly in the Certificate's configured skill order and permanently lock each completed or expired section with zero time rollover, so that sitting rules match official test centers.
16. As a candidate, I want the countdown timer to compute remaining seconds from an authoritative wall-clock deadline (`max(0, Deadline - WallClock)`), so that browser tab throttling or sleep mode never skews my exam clock.
17. As a candidate, I want my answers and text drafts auto-saved every 500ms with LocalStorage offline buffering and an `AuthorizationCode` crash recovery flow, so that power loss or browser crashes never lose my progress.
18. As a candidate in a `FULL_SANDBOX` section with split-screen enabled (such as IELTS Reading/Writing or VSTEP Reading/Writing), I want a 100vh dual-pane workspace with independent scrollbars, a top timer that flashes red at 10m and 5m remaining, a live font-size and high-contrast theme switcher, and a bottom question palette showing unanswered, answered, current, and flagged items, so that I have a comfortable test-day workspace.
19. As a candidate in a section with highlighting and notes enabled, I want to right-click selected text to highlight or add notes anchored to deterministic word tokens (`{ start, end }`), so that my highlights never shift when I change font size or contrast theme.
20. As a candidate in an `AUDIO_PLUS_CHECK` listening section (such as IELTS Listening), I want to preview and highlight future questions while audio plays continuously, followed by an automatic post-audio review countdown (e.g., 2 minutes) before auto-submission, so that timing matches official computer-delivered rules.
21. As a candidate in an `AUDIO_DRIVEN` listening section (such as TOEIC Listening CBT), I want back-navigation disabled, option text hidden in sections configured with `HideOptionText`, and questions grouped into clusters of 3 in sections configured with `ClusterSize = 3`, so that pacing follows the audio stream.
22. As a candidate in a `UNIT_LOCKED_SANDBOX` section (such as TOEIC CAT Reading), I want to navigate freely within Unit 1 and use Review Screen filters (*First unanswered*, *Unanswered only*, *Flagged only*) before confirming transition to permanently lock Unit 1 and enter adaptive Unit 2, so that multi-stage adaptive rules are enforced.
23. As a candidate in a `SYSTEM_PACED_AUTO` speaking section (such as TOEIC Speaking or VSTEP Speaking), I want the system to automatically run the preparation timer, play a beep tone, record my voice for the configured response window, auto-save, and advance to the next item, followed by a post-speaking audio verification screen, so that speaking runs hands-free.
24. As a candidate in a `HYBRID_GROUP_LOCKED` writing section with `OnScreenCutPasteOnly` enabled (such as TOEIC Writing), I want keyboard Ctrl+C/X/V blocked in strict exam mode, an on-screen `[Cut]`, `[Paste]`, `[Undo]`, `[Redo]` toolbar, a shared timer pool for grouped picture-sentence items, and dedicated timers with permanent-lock confirmation modals for essay items, so that the editor mirrors official ETS software.
25. As a candidate answering objective completion, multi-select, dropdown, or drag-and-drop questions, I want my answers evaluated strictly against the configured accepted answers, selection caps, and word/number limits (`MAX_WORDS`, `ALLOW_NUMBER`), awarding zero points when word limits are exceeded, so that objective scoring is exact.
26. As a candidate completing a writing or speaking exam, I want my section scores combined using the Certificate's configured `ScoreWeight` (e.g., 2:1 Task 2 weighting with under-length penalty flags) and `ScoreAggregationRule` (`sum`, `avg_half_step`, `independent`), and my sentence tasks checked for required keyword stems via lemmatization, so that my final scaled score and CEFR/Band report are completely accurate.

### Learner — Practice Mode (Mode B) & Diagnostic Alignment
27. As a learner in Practice Mode (Mode B), I want to practice individual skills or sections across all 11 question components with early submission, dark mode, seekable audio with 5-second rewind, transcripts, and explanations, while having my `IsCorrect (0/1)` results feed into Weakness Analysis and AI Mentor Insights, so that free diagnostic learning works across every certificate.

## Implementation Decisions

### 1. Seam Ownership & Service Boundaries
- **Exam Sitting & Scoring Seam (`IAttemptEngine`):** Sole authoritative owner of exam attempt sitting mechanics, wall-clock deadlines, macro/micro phase transitions, permanent section locking, auto-submission, objective answer validation across all 11 components, lemma keyword checking, two-step score scaling, weighted skill aggregation, and band resolution.
- **Certificate Wizard, Authoring & Ingest Seams (`IScoreConversionService`, `IAdminExamService`, `IQuestionImportService`, `IExamQuickIngestService`, `IExamGeneratorService`):** Own blueprint preset instantiation, 6-axis certificate/section configuration persistence, Certificate-driven Exam Studio auto-scaffolding, 11-component authoring, publish-readiness auditing, and batch manifest/quick-ingest/AI generation.
- **Domain Adapters (`MockExamService`, `PlacementService`, `PracticeService`):** Remain thin consumers over the core seams, preserving credit ledger rules ("block before you charge") and practice session retention bounds.

### 2. Single Chain of Authority: Certificate Blueprint ➔ Dynamic Studio ➔ Sitting Engine
- No service, UI view, or SQL trigger may hardcode behavior by checking `Certificates.Code = 'IELTS'` or `'TOEIC'`.
- All authoring, validation, sitting, and scoring behavior flows from the Certificate's 6 persisted configuration axes:
  1. `Certificates.ScoreAggregationRule`: `'sum'`, `'avg_half_step'`, `'avg_whole'`, `'independent'`.
  2. `Sections.ScoreWeight`: Decimal weight applied when aggregating section/task scores within a skill (default `1.0`).
  3. `Sections.NavigationMode`: `'FULL_SANDBOX'`, `'UNIT_LOCKED_SANDBOX'`, `'AUDIO_DRIVEN'`, `'SYSTEM_PACED_AUTO'`, `'HYBRID_GROUP_LOCKED'`.
  4. `Sections.TimerPolicyJson`: Timer mode (`SECTION_SHARED`, `AUDIO_PLUS_CHECK`, `ITEM_FIXED`), check time seconds, and warning thresholds.
  5. `Sections.Layout & Authoring Blueprint` (persisted on `Sections`): Defines `LayoutType` (`FULL_WIDTH` vs `VERTICAL_SPLIT_SCREEN`), `ExpectedQuestionCount`, `OptionCount`, `ClusterSize`, `HideOptionText`, `AllowedComponentTypes`, `RequirePassage`, `RequireAudio`, `RequireImage`, `RequiredKeywordCount`, and `UnitCount`.
  6. `Sections.ToolsConfigJson`: Defines `allowHighlight`, `allowNotes`, `showWordCount`, `disableSpellCheck`, `allowKeyboardCutPaste`, `showOnScreenCutPaste`, and `confirmOnNext`.
- **Dynamic Authoring Adaptation:** When an admin selects a Certificate to create or edit an exam, Exam Studio (`AdminExamService.GetExamStudioAsync`), Quick Ingest (`ExamQuickIngestService`), Manifest Import/Export (`QuestionImportService`), and AI Generator (`ExamGeneratorService`) inspect that Certificate's `Sections` blueprint to dynamically generate section tabs, pre-scaffold question/cluster/unit slots, filter input fields and allowed question components, tailor copy-paste/JSON templates, and run publish-readiness checks.

### 3. Database Schema Evolutions (Database-First)
- `docs/database-schema.sql` remains the single source of truth.
- `Certificates` adds `ScoreAggregationRule`. `trg_Attempts_TotalMatchesSkills` and `trg_Attempts_TotalMatchesSkills_OnAttempt` evaluate `ScoreAggregationRule` dynamically.
- `Exams` adds `ExamFamily` and `DeliveryMode` (`strict_exam`, `practice`).
- `Sections` adds `NavigationMode`, `ScoreWeight`, `TimerPolicyJson`, and `ToolsConfigJson` (including layout/stimulus/item authoring blueprint properties).
- `QuestionGroups` adds `UnitNumber` (default `1`, supporting multi-stage adaptive units).
- `Questions` expands supported component types (single-choice 4/3, multi-select checkbox, inline completion, dropdown, drag-and-drop, and free response) and adds `MaxSelections`, `AllowNumber`, `AcceptedAnswersJson`, and `RequiredKeywords`. Constraints `CK_Questions_Words` and `UX_Options_OneCorrect` are updated so objective completion items can carry `MaxWords` and `multi_select` questions can have multiple `IsCorrect = 1` options.
- `AttemptAnswers` and `PracticeAnswers` add `ResponseText` / `SelectedOptionIdsJson` while continuing to populate `IsCorrect (0/1)` so `vw_UserCategoryAccuracy` and `vw_UserSectionAccuracyAll` remain unbroken.
- `ExamAttempts` adds `ActiveSkillId`, `ActiveUnitNumber`, `AuthorizationCode`, and `PhaseDeadlineAt`.
- Every table with an SQL trigger explicitly declares `HasTrigger(...)` in EF Core model configuration.

### 4. Client-Server Synchronization, Pre-Flight & UI Architecture ("Bảng hiệu phòng thi")
- Pre-flight onboarding verifies 100% audio asset buffering and interactive microphone recording/playback before unlocking the sitting shell.
- Client timers compute remaining seconds against the server-authoritative wall-clock deadline: `Remaining = max(0, DeadlineTimestamp - CurrentWallClockTime)`.
- Answer changes and text drafts auto-save via 500ms debounced batch endpoints with LocalStorage offline fallback and `AuthorizationCode` session crash recovery.
- All interfaces (4-Step Certificate Wizard, Section-Tab Guided Exam Studio, Pre-flight, IELTS Split-Screen Shell, Tokenized Highlighter, and TOEIC CBT/CAT/S&W Shell) strictly follow `DESIGN.md`: `border-radius: 0`, `box-shadow: none`, 2px ink borders (`#14181d`), IBM Plex typography, and flat signage colors.

## Testing Decisions

### 1. What Makes a Good Test
- Tests verify observable domain behavior and invariants through public seam interfaces, never internal private helpers.
- Backend service tests run against real SQL Server / LocalDB with real foreign keys, check constraints, views, and triggers loaded from `docs/database-schema.sql`.
- Crucial extensibility test: Creating a brand-new custom certificate via the Certificate Wizard service seam, scaffolding an exam for it in Exam Studio, importing questions, and sitting the exam through `IAttemptEngine` must succeed end-to-end without any code changes.

### 2. Primary Seams & Modules to Test
- **Sitting & Scoring Seam (`IAttemptEngine` & `IScoreConversionService`):**
  - Macro-linearity, permanent phase locking, zero time rollover, and wall-clock deadline expiration.
  - Objective grading across all 11 question components: exact completion matching, `MAX_WORDS` zeroing, multi-select option set matching, and TOEIC Writing Q1–5 lemma keyword verification.
  - Data-driven `ScoreAggregationRule` (`sum`, `avg_half_step`, `independent`) and `Sections.ScoreWeight` (2:1 Task 2 weighting and under-length penalty flags).
- **Certificate Wizard, Dynamic Studio & Ingest Seam (`IAdminExamService`, `IQuestionImportService`, `IExamQuickIngestService`, `IExamGeneratorService`):**
  - Instantiating certificates from Official Blueprint Presets or custom 4-step wizard inputs, and verifying the Live Score Preview calculation.
  - Verifying that Exam Studio auto-scaffolds tabs/slots and Publish Audit enforces rules dynamically from the chosen Certificate's section blueprint (including a custom-defined certificate).
  - Round-trip batch import/export of `.zip`/`.json` manifests (`ADR-0007`), dynamic manifest template generation, section-aware Quick Ingest parsing, and AI section generation.
- **Practice & Diagnostic Seam (`IPracticeService`, `IWeaknessAnalysisService`):**
  - Drilling new question components in Practice Mode and verifying that `vw_UserCategoryAccuracy` and `vw_UserSectionAccuracyAll` reflect accurate `IsCorrect` aggregates.
- **End-to-End Browser Seam (`scripts/crawl-env.sh fresh` & `tests/WebMVC.Tests`):**
  - Full cross-role walk (Admin creates/edits certificate via Linear Wizard ➔ Authors/Imports exam via Guided Studio Tabs ➔ Learner completes pre-flight, sits exam, experiences section lockouts, submits, and reviews calibrated scores) with zero `fail:` logs.

### 3. Prior Art in the Codebase
- `tests/Services.Tests/AttemptEngineTests.cs` and `AttemptEngineMultiCertScoringTests.cs` for sitting lifecycle and multi-certificate score scaling.
- `tests/Services.Tests/QuestionImportServiceTests.cs` and `GenerativeExamServiceTests.cs` for manifest ingestion and AI exam generation.
- `tests/Services.Tests/PracticeServiceTests.cs` and `WeaknessAnalysisServiceTests.cs` for practice sessions and SQL accuracy views.

## Out of Scope

1. Live human video-call proctoring, webcam eye-tracking, or room scanning.
2. Synchronous human examiner scheduling for IELTS Speaking (remains asynchronous audio recording simulation).
3. Item-level IRT Computerized Adaptive Testing (adaptive support targets multi-stage unit branching as used in TOEIC CAT).
4. Payment gateway or credit ledger architectural changes.
5. Native iOS/Android mobile app wrappers.

## Further Notes

- All schema changes must be authored in `docs/database-schema.sql` first before scaffolding EF Core models.
- Ubiquitous domain terms from `CONTEXT.md` (*Certificate*, *Skill*, *Section*, *Exam*, *Practice session*, *Attempt*, *Free response*, *Raw score*, *Scaled score*, *Band*) must be used consistently across all layers.

## Implementation Roadmap (Ordered Execution Plan — Steps 01 to 15)

```[tasklist]
### Implementation Tasks (Sequential Order)
- [ ] #107
- [ ] #116
- [ ] #120
- [ ] #117
- [ ] #118
- [ ] #121
- [ ] #108
- [ ] #109
- [ ] #110
- [ ] #111
- [ ] #113
- [ ] #112
- [ ] #114
- [ ] #119
- [ ] #115
```
