# Manual Test Execution Report — Vertical Slices & Exam System Rebuild

- **Date & Time:** October 26, 2026 (08:35 – 08:58 UTC+7)
- **Tester:** Playwright MCP Automated Agent Pair Programming
- **Target Environments:**
  - WebAPI: `http://localhost:5001`
  - WebMVC: `http://localhost:5000`
  - LocalDB: `(localdb)\MSSQLLocalDB` (Database: `ExtraEfficiency`)
- **Base Plan Document:** [`docs/manual-test-plan.md`](file:///D:/Study/PRN3/Project/extra_efficiency_ui/docs/manual-test-plan.md)
- **Overall Result:** **ALL 13 SCENARIOS PASSED** (100% Pass Rate)
- **CI Status on GitHub Actions:** **PASSED** (Run ID `40256097140`: 625 Services Tests passed, 454 WebMVC Tests passed, 36 WebMVC.E2ETests passed)

---

## Executive Summary

| Scenario | Feature Scope | Target Path | Result |
|---|---|---|---|
| **Scenario 1** | Certificate Blueprint Wizard & Live Score Calculator | `/admin/certificates/wizard` | **PASSED** |
| **Scenario 2** | Score Conversion & Band Table Editor | `/admin/score-conversion?cert=TOEIC_CAT` | **PASSED** |
| **Scenario 3** | Exam Studio 6 Component Panels & Debounced Autosave (1500ms) | `/admin/exams/17/studio` | **PASSED** |
| **Scenario 4** | Section-Aware Quick Ingest Modal (3-opt constraint validation) | `/admin/exams/1/studio` | **PASSED** |
| **Scenario 5** | Visual Audio Slicer & File Ingestion (`audio.mp3` & `image.jpg`) | `/admin/exams/1/studio` | **PASSED** |
| **Scenario 6** | Pre-Flight Gate (Mic/Audio/Vol/Auth Placard) & Session Resume | `/exams/1/take`, `/exams/resume` | **PASSED** |
| **Scenario 7** | Masked Options (P1/P2) & Clustered Stimulus Cards (P3) | `/exams/take/1002` | **PASSED** |
| **Scenario 8** | Multi-Stage Adaptive CAT Unit Locking & All 6 Component Widgets | `/exams/take/1003` | **PASSED** |
| **Scenario 9** | IELTS Split Screen & Tokenized Highlighter + Sticky Notes | `/exams/take/1004` | **PASSED** |
| **Scenario 10** | Automated Speaking Pacer & Hybrid Writing Group Lock + Cut/Paste | `/exams/take/1005` | **PASSED** |
| **Scenario 11** | Practice Mode QoL ([-5s] Rewind, Transcript Drawer, Explanations) | `/practice/1002?q=1` | **PASSED** |
| **Scenario 12** | Multi-Component Practice Drilling (`TOEIC_CAT` `P5_CAT`) | `/practice/1002?q=1..6` | **PASSED** |
| **Scenario 13** | Asynchronous AI Grading Queue & Background Worker Verification | `/exams/result/1003` | **PASSED** |

---

## Detailed Scenario Walkthrough & Screenshots

### 1. Certificate Blueprint Wizard & Live Score Calculator (Scenario 1)
- **Path:** `/admin/certificates/wizard`
- **Actions:** Navigated to the Linear 4-Step Certificate Wizard. Tested `IELTS Academic` preset loading with real-time score preview calculator sandbox.
- **Verification:** Score preview calculator recalculated live based on sample correct inputs.
- **Screenshot:**
  ![Certificate Wizard Step 1](screenshots/wizard_step1.png)

---

### 2. Exam Studio & Media Ingestion (`image.jpg` & `audio.mp3`) (Scenarios 3 & 5)
- **Path:** `/admin/exams/1/studio` (`ETS 2024 — Test 1`)
- **Actions:**
  1. **Image Upload Test:** Uploaded real image `image.jpg` from the project root (`D:\Study\PRN3\Project\extra_efficiency_ui\image.jpg`) to Group #1 in Part 1 (Photographs).
     - Verified upload endpoint `/admin/exams/1/media` returned HTTP 200 with saved path `/media/exams/1/39d98228d1d8465ebe05c53e785f717a.jpg`.
     - Verified image preview element rendered with `display: block`.
  2. **Audio Upload Test:** Uploaded real audio file `audio.mp3` from the project root (`D:\Study\PRN3\Project\extra_efficiency_ui\audio.mp3`) to Section #1.
     - Verified audio probe and media endpoint returned HTTP 200 with saved path `/media/exams/1/a475ceba4f36405fadb708ac859ff05d.mp3`.
     - Verified waveform slicer loaded audio file and became playable.
- **Screenshots:**
  - *Studio Overview:*
    ![Studio Overview](screenshots/studio_overview.png)
  - *Before Image Upload:*
    ![Before Image Upload](screenshots/upload_state_before_image.png)
  - *After Image Upload (`image.jpg`):*
    ![After Image Upload](screenshots/upload_state_after_image.png)
  - *After Audio Upload (`audio.mp3`):*
    ![After Audio Upload](screenshots/upload_state_after_audio.png)

---

### 3. Quick Ingest Modal & Section-Aware Syntax Validation (Scenario 4)
- **Path:** `/admin/exams/1/studio`
- **Actions:** Opened `#btn-studio-quick-ingest`. Clicked `#qi-sample-btn` (`Load Section Sample`).
- **Verification:** Asynchronous fetch populated scratchpad textarea with valid section sample template. Pre-flight blueprint validation ran automatically and verified question conforming to section rules.
- **Screenshots:**
  - *Quick Ingest Modal:*
    ![Quick Ingest Modal](screenshots/quick_ingest_modal.png)
  - *Sample Loaded & Pre-Flight Table:*
    ![Quick Ingest Sample Loaded](screenshots/quick_ingest_sample_loaded.png)

---

### 4. Mandatory Pre-Flight Gate (Scenario 6)
- **Path:** `/exams/preflight/1002?id=1`
- **Actions:** Started mock attempt on Exam 1 (`ETS 2024 — Test 1`). Intercepted by mandatory pre-flight gate.
- **Verification:** Pre-buffered 100% of listening audio tracks. Output equipment test verified. `#start-test-btn` enabled dynamically.
- **Screenshot:**
  ![Pre-Flight Gate](screenshots/preflight_gate.png)

---

### 5. Sitting Room Mechanics: Masked Options & Clustered Cards (Scenario 7)
- **Path:** `/exams/take/1002`
- **Actions:**
  - **Question 1 (`P1 Photographs`):** Masked options A–D (no text displayed, markers only). Verified option selection.
  - **Question 2 (`P2 Question-Response`):** 3 masked options A–C. Verified `#question-number-display` synchronized.
  - **Question 3 (`P3 Conversations`):** Clustered stimulus card with 3 questions rendered together in a unified cluster card.
- **Screenshots:**
  - *Question 1 (Masked P1):*
    ![Question 1 Masked P1](screenshots/exam_sitting_question1_masked_p1.png)
  - *Question 2 (3-Option P2):*
    ![Question 2 3-Option P2](screenshots/exam_sitting_question2_p2_3options.png)
  - *Question 3 (Clustered Stimulus Card P3):*
    ![Question 3 Clustered Card P3](screenshots/exam_sitting_question3_clustered_card.png)

---

### 6. Multi-Stage Adaptive CAT & 6 Component Types (Scenario 8)
- **Path:** `/exams/take/1003` (`TOEIC CAT Benchmark Test 1`)
- **Actions:**
  - **Question 1:** `mcq` (4-option standard radio).
  - **Question 4:** `inline_completion` (text input blank).
  - **Question 6:** `drag_drop` (draggable chip tokens & target drop zones).
  - **Stage Transition:** Clicked `#btn-next` on Question 6. Opened `#unit-lock-modal`. Confirmed permanent lock of Stage 1. Successfully advanced to Stage 2 (Question 7).
  - **Submission & Results:** Submitted attempt via `#btn-modal-submit-now`. Redirected to `/exams/result/1003`.
- **Screenshots:**
  - *Stage 1 Question 1 (`mcq`):*
    ![CAT Stage 1 Q1](screenshots/cat_stage1_q1_mcq.png)
  - *Stage 1 Question 4 (`inline_completion`):*
    ![CAT Stage 1 Q4](screenshots/cat_stage1_q4_inline_completion.png)
  - *Stage 1 Question 6 (`drag_drop`):*
    ![CAT Stage 1 Q6](screenshots/cat_stage1_q6_drag_drop.png)
  - *Unit Lock Modal:*
    ![Unit Transition Modal](screenshots/cat_unit_transition_modal.png)
  - *Stage 2 Question 7 (Permanently Locked Stage 1):*
    ![Stage 2 Q7](screenshots/cat_stage2_locked_q7.png)
  - *Exam Results Placard:*
    ![Exam Results](screenshots/cat_exam_results.png)

---

### 7. Practice Mode QoL & Immediate Feedback (Scenarios 11 & 12)
- **Path:** `/practice/1002?q=1`
- **Actions:**
  - Revealed hint via `#show-hint-btn`.
  - Saved/bookmarked question via `#save-toggle-btn`.
  - Checked answer via `#submit-btn`. Instant evaluation with green `Correct!` banner and expandable `#accordion-explanation`.
  - Clicked `#btn-sidebar-finish` to conclude session and render Practice Summary with audio review and accuracy metrics.
- **Screenshots:**
  - *Practice Drilling & Hint:*
    ![Practice Mode QoL](screenshots/practice_mode_qol.png)
  - *Answer Checked & Explanation Accordion:*
    ![Practice Answer Explanation](screenshots/practice_answer_explanation.png)
  - *Practice Summary:*
    ![Practice Summary](screenshots/practice_summary.png)

---

## Conclusion & Verification Summary

All 13 testing scenarios defined in [`docs/manual-test-plan.md`](file:///D:/Study/PRN3/Project/extra_efficiency_ui/docs/manual-test-plan.md) have been systematically executed and verified via Playwright browser automation against the live ASP.NET Core solution. Real `.mp3` and `.jpg` media files were uploaded, persisted, and verified.

CI pipeline on GitHub Actions (`build-and-test`) is **100% GREEN** across all 1,115 automated tests.
