# Admin Exam Studio and Authoring Wizard

## Context

Authoring and modifying real-world certification exams (such as a 200-question TOEIC test or multi-task IELTS exam) requires configuring heterogeneous section formats: single photo MCQs, audio conversations with question clusters, reading passages with embedded fill-in blanks, and timed free-response prompts with rubric criteria.
Previously, admins had to navigate across disconnected pages with page reloads (`AdminExamsController`) or author complex JSON manifests by hand (ADR-0007). Content creators lacked an interactive authoring environment to compose, slice audio, and validate complete exams without leaving their flow.

## Decision

We establish an interactive **Exam Studio** and **Authoring Wizard** architecture in WebMVC and WebAPI:

1. **Single-Page Exam Studio**: Consolidates the multi-step exam management workflow into an integrated three-column workspace:
   - *Left Navigation Rail*: Section and question tree hierarchy, drag-and-drop reordering with automatic contiguous `DisplayOrder` renumbering, completion state badges, and quick jump.
   - *Center Workstation*: Adaptive form workstation rendering section-specific authoring templates.
   - *Right Placard*: Publish readiness audit checklist, target vs actual question count gauges, and schema invariant validation flags.
2. **Section-Specific Presets (Authoring Wizard)**: Dynamically binds input forms to the pedagogical format of the target section:
   - *TOEIC Part 1*: Photo upload with mandatory alt text (`CK_QGroups_ImageAlt`), audio snippet / timestamp markers, and 4 option rationale slots.
   - *TOEIC Part 2*: Prompt audio, 3 option inputs (A, B, C) with correct answer radio.
   - *TOEIC Part 3 & 4*: Audio dialogue/monologue, transcript, and auto-provisioned clusters of 3 MCQs.
   - *TOEIC Part 5*: Single-question text inputs with grammar/vocabulary category tags (`QuestionCategories`) and comprehensive explanation fields. Internal `QuestionGroup` rows are abstracted away and managed transparently 1-to-1.
   - *TOEIC Part 6*: Interactive text completion passage supporting `[1]`, `[2]`, `[3]`, `[4]` syntax that bi-directionally synchronizes with child question cards.
   - *TOEIC Part 7*: Single/multi passage editor tabs accompanied by 2–5 MCQ clusters.
   - *Speaking & Writing*: Timed free-response prompts with preparation/response limits, minimum/maximum word counts, and a multi-select rubric checklist loaded from `GradingCriteria`.
3. **Quick Ingest & Staging Parser**:
   - Accepts raw text pasted from external documents (Word, PDF) supporting both inline asterisks (`*A. option`) and trailing key lines (`ANSWER: A`, `KEY: A`, `EXPLANATION:`), as well as direct JSON snippets.
   - Renders a live pre-flight staging view highlighting syntax errors and schema rule violations before saving.
   - Provides a toggle between **Append** (appends parsed items to the section with incremented display orders) and **Replace Section** (atomically clears and replaces questions in that section within a database transaction).
4. **Dual-Model Audio Management**:
   - *Master Section Slicing*: Visual waveform/audio player tied to `ExamSectionAudio` allowing authors to mark In/Out points to populate `AudioStartMs` and `AudioEndMs`.
   - *Direct Snippet Upload*: Drag-and-drop upload for individual audio files (`.mp3`, `.wav`) directly to `QuestionGroups.AudioPath` via `IFileStore`.
5. **Autosave & Data Safety**:
   - Debounced AJAX autosaving (1.5-second inactivity delay or field blur) with explicit save status placarding (`Saved at HH:mm:ss` in IBM Plex Mono).
   - Window `beforeunload` guard preventing accidental navigation while requests are in flight.
6. **Publish Readiness Audit ("Block Before Publish")**:
   - Draft exams are prevented from transitioning to `published` until an automated audit verifies complete question counts against certificate specifications, exactly one correct option per MCQ (`UX_Options_OneCorrect`), audio attachments on listening sections, and valid rubric attachments on free-response sections.
7. **Two-Way Export**:
   - Allows instant serialization and download of the complete exam or selected sections as an ADR-0007 compliant `.json` manifest or `.zip` package (with referenced audio and images).
