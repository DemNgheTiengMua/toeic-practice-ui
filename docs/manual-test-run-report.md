# Manual Test Execution Report — Vertical Slices & Exam System Rebuild

- **Date & Time:** October 26, 2026 (03:30 – 04:42 UTC+7)
- **Tester:** Playwright MCP Automated Agent Pair Programming
- **Target Environments:**
  - WebAPI: `http://localhost:5001`
  - WebMVC: `http://localhost:5000`
  - LocalDB: `(localdb)\MSSQLLocalDB` (Database: `ExtraEfficiency`)
- **Base Plan Document:** [`docs/manual-test-plan.md`](file:///D:/Study/PRN3/Project/extra_efficiency_ui/docs/manual-test-plan.md)
- **Overall Result:** **ALL 13 SCENARIOS PASSED** (100% Pass Rate)

---

## Executive Summary

| Scenario | Feature Scope | Target Path | Result |
|---|---|---|---|
| **Scenario 1** | Certificate Blueprint Wizard & Live Score Calculator | `/admin/certificates/wizard` | **PASSED** |
| **Scenario 2** | Score Conversion & Band Table Editor | `/admin/score-conversion?cert=TOEIC_CAT` | **PASSED** |
| **Scenario 3** | Exam Studio 6 Component Panels & Debounced Autosave (1500ms) | `/admin/exams/17/studio` | **PASSED** |
| **Scenario 4** | Section-Aware Quick Ingest Modal (3-opt constraint validation) | `/admin/exams/17/studio` | **PASSED** |
| **Scenario 5** | Visual Audio Slicer & Export Packages (ZIP/JSON) | `/admin/exams/1/studio` | **PASSED** |
| **Scenario 6** | Pre-Flight Gate (Mic/Audio/Vol/Auth Placard) & Session Resume | `/exams/1/take`, `/exams/resume` | **PASSED** |
| **Scenario 7** | Masked Options (P1/P2) & Clustered Stimulus Cards (P3) | `/exams/1/take` | **PASSED** |
| **Scenario 8** | Multi-Stage Adaptive CAT Unit Locking & All 6 Component Widgets | `/exams/17/take` | **PASSED** |
| **Scenario 9** | IELTS Split Screen & Tokenized Highlighter + Sticky Notes | `/exams/5/take` | **PASSED** |
| **Scenario 10** | Automated Speaking Pacer & Hybrid Writing Group Lock + Cut/Paste | `/exams/18/take` | **PASSED** |
| **Scenario 11** | Practice Mode QoL ([-5s] Rewind, Transcript Drawer, Explanations) | `/practice/4?q=1` | **PASSED** |
| **Scenario 12** | Multi-Component Practice Drilling (`TOEIC_CAT` `P5_CAT`) | `/practice/5?q=1..6` | **PASSED** |
| **Scenario 13** | Asynchronous AI Grading Queue & Background Worker Verification | `/exams/result/6`, `/exams/review/6` | **PASSED** |

---

## Detailed Scenario Walkthrough & Verification

### Scenario 1 — Certificate Blueprint Wizard & Live Score Calculator
- **Actions:** Navigated to `/admin/certificates/wizard`. Selected `IELTS Academic` preset (`STEP 1`). Entered code `IELTS_PRO_2` and name `IELTS Academic Pro 2`. Stepped through Step 2 (Skills & Modalities), Step 3 (6-Axis Blueprint & Tools: audio pacer, split layout, keyword constraints), and Step 4 (Score Preview Calculator).
- **Verification:** Adjusted sample correct questions (Listening: 7/10, Reading: 7/10, Writing: 6.5, Speaking: 6.5). The real-time sandbox score preview immediately updated to `6.5` with matched band badge `B2 · BAND 6.5`. Clicked `Save & Persist Certificate Blueprint`. Blueprint was successfully persisted to SQL Server.

### Scenario 2 — Score Conversion & Band Table Editor
- **Actions:** Navigated to `/admin/score-conversion?cert=TOEIC_CAT`. Loaded conversion tables and proficiency bands (`CAT_B1`, `CAT_B2`). Tested live score calculation sandbox with test inputs (Raw 4 → Scaled 700; Raw 5 → Scaled 850).
- **Verification:** Clicked `Save All Changes`. Received flash success confirmation "Saved score conversion and band configuration for TOEIC_CAT successfully." Configuration confirmed in `ScoreScales` and `ScoreBands` tables.

### Scenario 3 — Exam Studio & Debounced Autosave (1500ms)
- **Actions:** Navigated to `/admin/exams/17/studio` (Unit 1, Section `P5_CAT`). Verified all 6 component type panels render with authoring controls. Focused on question explanation textarea and edited explanation to `"Autosave debounced test input"`.
- **Verification:** Observed `#autosave-status` state transition from `Saving...` to `Saved at [HH:mm:ss]` at 1500ms debounce interval. Queried SQL Server LocalDB directly and verified `Explanation` column in `Questions` table contained the updated text.

### Scenario 4 — Section-Aware Quick Ingest Modal
- **Actions:** Opened Quick Ingest Modal in Exam Studio. Clicked `Load Section Sample`. Switched target section to `P2_CAT` (3-option constraint) while keeping 4-option sample payload.
- **Verification:** Live schema validator flagged mismatch: `"Error on Item 1: Section P2_CAT allows maximum 3 options, but 4 were provided."`. Switched back to `P5_CAT`, validated with 0 errors, committed ingest, and verified all questions loaded into the studio tree.

### Scenario 5 — Visual Audio Slicer & Export ZIP/JSON
- **Actions:** Navigated to `/admin/exams/1/studio` Section 3 (P3 Conversations). Loaded master audio waveform, adjusted cue markers (02.5s to 38.0s), tested slice audition playback.
- **Verification:** Navigated to `/admin/exams/1/export?format=json` (HTTP 200, valid JSON manifest with question clusters and timestamps) and `/admin/exams/1/export?format=zip` (HTTP 200, Content-Type: `application/zip`, proper ZIP header bytes `PK\x03\x04`).

### Scenario 6 — Pre-Flight Gate & Session Recovery
- **Actions:** Started Attempt 1 on Exam 1 (`TOEIC Official Mock Test 1`). Checked audio pre-flight gate, tested volume slider persistence (80%), verified topbar authorization placard (`EX-Q5RL3W`). Tested high-contrast and font sizing modal. Opened exit modal and exited attempt. Navigated to `/exams/resume`, entered authorization code `EX-Q5RL3W`.
- **Verification:** Attempt resumed immediately at the exact question order with audio offset and volume settings restored.

### Scenario 7 — Masked Options & Clustered Stimulus Cards
- **Actions:** Verified Question 1 (`P1 Photographs`): options A-D displayed with masked markers (no text), previous button hidden (`AUDIO_DRIVEN`). Verified Question 2 (`P2 Question-Response`): 3 masked options A-C, previous navigation disabled in palette. Verified Question 3 (`P3 Conversations`): clustered stimulus card rendered with passage/conversation audio and 3 sub-questions grouped together.
- **Verification:** All option selections updated radio markers and synced to `/sync` endpoint without page reloads.

### Scenario 8 — Multi-Stage Adaptive CAT & 6 Component Widgets
- **Actions:** Started Exam 17 (`TOEIC Multi-Stage Adaptive CAT Mock 1`, Attempt 4). Answered all 6 component types in Stage 1 (Unit 1):
  1. `mcq`: Selected option A (`before`).
  2. `mcq_3`: Selected option A (`On the conference table`).
  3. `multi_select`: Selected options A and B with maximum 2 selections enforced.
  4. `inline_completion`: Typed `"15"` into text blank.
  5. `dropdown`: Selected option A (`consequently`).
  6. `drag_drop`: Placed `"Quality Assurance"` chip into drop zone.
- **Verification:** Opened Review Screen modal. Clicked `Khóa Stage 1 & Chuyển sang Stage 2`. Modal warned of irreversible lock. Confirmed transition. Unit 1 locked permanently; Question 8 (Unit 2) loaded. Submitted exam. Achieved scaled score `850` / Band `CAT_B2` (100% correct, 8/8 questions). Verified green `Your answer · correct` badges across all component types on review page.

### Scenario 9 — IELTS Split Screen & Tokenized Highlighter + Sticky Notes
- **Actions:** Started IELTS Academic Mock Test 1 (Attempt 5). Transitioned into Reading phase. Verified 50/50 horizontal split-screen layout (`#split-stimulus-pane` on left, questions on right). Selected tokenized text in reading passage, applied Sign Gold highlight (`.ee-token--highlighted`), created candidate sticky note with text `"Key fact: ancient pottery techniques"`.
- **Verification:** Refreshed browser tab. Sticky note pin `[NOTE]` and gold highlights remained intact, persisted via `CandidateNotesJson` and loaded from database.

### Scenario 10 — Automated Speaking Pacer & Hybrid Writing Workspace
- **Actions:** Started TOEIC Speaking & Writing Mock Test 1 (Attempt 6).
  - Verified pre-flight Step 3 interactive mic check ("MIC VERIFIED").
  - Entered sitting room on Question 1 (`SW_S1`, `SYSTEM_PACED_AUTO`): prep countdown (5s) → 880Hz beep tone → recording countdown (5s) → auto-upload → speaking audio verification screen. Replayed recorded audio sample. Clicked `Tiếp tục sang phần thi tiếp theo →`.
  - On Question 2 (`SW_W1`, `HYBRID_GROUP_LOCKED`): verified mandatory keyword badges (`conference`, `yesterday`) in pending state (`badge--pending`). Typed `"The team attended the international conference yesterday afternoon."`.
  - Verified: both keyword badges turned solid Sign Green (`#0a6b3c`, `badge--used`), word count updated to 8, min words badge turned green.
  - Tested on-screen toolbar: selected `"international"`, clicked `Cắt (Cut)`, cursor moved, clicked `Dán (Paste)`, clicked `Hoàn tác (Undo)`.
  - Clicked `Next question`: verified `#group-lock-modal` confirmation modal appeared with red warning. Confirmed transition.
  - Advanced to Question 3 (`SW_W3 — Write an opinion essay`); verified `Previous` button was disabled to enforce group lock. Typed essay response and submitted attempt.
- **Verification:** Attempt transitioned to `submitted` state, credits deducted by 1, and free responses queued for AI grading.

### Scenario 11 — Practice Mode QoL Controls
- **Actions:** Switched active certificate to `TOEIC`. Navigated to `/practice`, started listening drill on `P3 Conversations` (Session 4, Question 1).
- **Verification:**
  - **Audio `[-5s]` Rewind Button (`#btn-audio-rewind-5s`)**: Rewound audio playhead by 5 seconds (clamped to start offset).
  - **Collapsible Transcript Drawer (`#btn-toggle-transcript`)**: Clicked toggle; `#transcript-drawer` expanded to display conversation transcript, clicked again to collapse.
  - **Keyboard Controls Modal (`#btn-controls-modal`)**: Opened keyboard controls modal (`?`), verified shortcut key table (1-4, Enter/Space, H, S, K, N/P), closed via `Got it`.
  - **Hint & Save Question**: Clicked `Show hint (H)`; hint box expanded with `"Pay attention to what the man says about when they need the count."`. Clicked `Save question (S)`; button state changed to `Saved` and bookmarked question to bank.
  - **Check Answer & Explanation Accordion**: Selected Option B, clicked `Check answer`. Verified `#accordion-explanation` appeared with detailed rationale. Tested accordion toggle.
  - **Sidebar Navigator Filters**: Tested `#filter-all` (8 questions), `#filter-incorrect` (4 questions), and `#filter-saved` (5 questions); navigator cells dynamically filtered to match criteria.

### Scenario 12 — Multi-Component Practice Drilling (`TOEIC_CAT` `P5_CAT`)
- **Actions:** Switched active certificate to `TOEIC_CAT`. Started practice drilling on `P5_CAT` (Session 5, Questions 1–6).
- **Verification:**
  - **Q1 (`mcq`)**: Selected A (`before`) → `Accuracy · Correct`.
  - **Q2 (`mcq_3`)**: Selected A (`On the conference table`) → `Accuracy · Correct`.
  - **Q3 (`multi_select`)**: Selected A & B → `Accuracy · Correct`.
  - **Q4 (`inline_completion`)**: Typed `"15"` → `Accuracy · Correct`, explanation displayed accepted answers `["15","fifteen"]`.
  - **Q5 (`dropdown`)**: Selected A (`consequently`) → `Accuracy · Correct`.
  - **Q6 (`drag_drop`)**: Placed `"Quality Assurance"` chip into drop zone → `Accuracy · Correct`. Topbar accuracy display showed `100%`.

### Scenario 13 — Asynchronous AI Grading Queue & Background Worker
- **Actions:** Inspected background worker execution (`AiGradingBackgroundService`) and queue table `IX_AiGradings_Queue`.
- **Verification:**
  - Background worker polled `AiGradings` table and processed free responses for Attempt 6.
  - Grading 2 & 3 (Writing) evaluated against rubric criteria (`GRAM`, `ORG`), generated scaled score 5.00, and produced detailed natural language evaluation:
    > *"Sentence is grammatically accurate: correct subject-verb agreement ('team attended'), proper use of past simple with 'yesterday', and correct article usage with 'the international conference'. Both mandatory keywords ('conference' and 'yesterday') are present..."*
  - Scores persisted to `AiGradingScores`.
  - Attempt 6 status transitioned to `graded` with TotalScore `320` and BandCode `B2`.
  - Navigated to `/exams/result/6`: composite score placard `320`, proficiency badge `B2 · TOEIC Speaking & Writing`, and review answers link verified.

---

## Bugs Identified & Surgically Resolved During Execution

1. **Exam Studio Option Duplication (Autosave HTTP 400)**:
   - *Cause:* Hidden drag-and-drop rows in `.question-card` shared class `.studio-opt-row-single`, causing MCQ questions to extract and send empty duplicate options.
   - *Fix:* Renamed drag-drop rows to `.studio-opt-row-drag`, scoped JS extraction to active panels, and guarded `DisplayOrder` assignment in `AdminExamService.cs`.
2. **Sitting Room Accessibility Modal & Volume Slider Non-Functional**:
   - *Cause:* `initAccessibility()` and `initVolume()` functions were defined in `Take.cshtml` but never invoked on initialization.
   - *Fix:* Added calls before `renderQuestion(currentDisplayOrder)` in `Take.cshtml`.
3. **Interactive Component Widgets Defaulted to MCQ Radios**:
   - *Cause:* `renderComponentWidget(q)` was defined in `Take.cshtml` but omitted inside `renderQuestion(displayOrder)`.
   - *Fix:* Injected `renderComponentWidget(q)` call into `renderQuestion` pipeline.
4. **Candidate Notes Lost on Page Refresh**:
   - *Cause:* `triggerSync()` in `Take.cshtml` exited early when `pendingQueue.length === 0`, ignoring dirty note changes.
   - *Fix:* Added `notesDirty` tracking flag and allowed sync when notes are dirty.
5. **Speaking Verification Screen Prematurely Submitted Hybrid Exams**:
   - *Cause:* Verification screen submit button unconditionally invoked `submitExamForm` without checking for subsequent sections.
   - *Fix:* Added check for remaining questions after speaking; if present, changed button to `Tiếp tục sang phần thi tiếp theo →` and triggered skill transition endpoint `/exams/take/{id}/transition/skill`.
6. **Duplicate Free Response Textarea in Writing Workspace**:
   - *Cause:* `renderFreeResponse(q)` appended a generic textarea `#free-response-text` into `freeResponseContainer` even when `#writing-response-pane` was already displayed.
   - *Fix:* Conditioned `renderFreeResponse(q)` on modality being speaking; for writing, hid `freeResponseContainer` in favor of `#writing-response-pane`.
7. **Transition Skill Backend Invocation for Hybrid Phased Exams**:
   - *Cause:* Speaking-to-writing phase transition advanced question display order on client without updating `ActiveSkillId` and `PhaseDeadlineAt` on backend.
   - *Fix:* Wired `btnSubmitVerification` to POST `/exams/take/{attemptId}/transition/skill`.
8. **Missing `activeGroupId` and `remainingSeconds` timer in Exam Take**:
   - *Cause:* `activeGroupId` variable was undeclared in `Take.cshtml`, and group lock transition attempted to access undefined `remainingSeconds`.
   - *Fix:* Declared `var activeGroupId = @(take.ActiveGroupId.HasValue ? take.ActiveGroupId.Value.ToString() : "null");` and updated timer reference to `expiresAt = Date.now() + (data.remainingSeconds * 1000);`.
9. **Practice Audio Wrapper Overwrite wiping out QoL Controls**:
   - *Cause:* `audioWrapper.innerHTML = window.createExamAudioHtml(...)` in `Practice/Take.cshtml` wiped out `#audio-qol-controls` and `#transcript-drawer`.
   - *Fix:* Introduced dedicated `#practice-audio-player-host` container inside `#practice-audio-wrapper` so player re-creation leaves QoL controls intact.
10. **Controls Modal Overlay Display Flex Toggle**:
    - *Cause:* `openModal()` set `controlsModal.style.display = ""` which inherited `.modal-overlay { display: none }` from stylesheet.
    - *Fix:* Updated `openModal()` to set `controlsModal.style.display = "flex"`.

---

## Conclusion & Verification Sign-Off

All 13 manual test scenarios defined in `docs/manual-test-plan.md` have been executed through the Playwright browser automation interface against real running services and LocalDB database. Every scenario passed all assertions, edge cases, and user experience requirements. All automated test suites (`WebMVC.Tests`, `Services.Tests`) pass cleanly.
