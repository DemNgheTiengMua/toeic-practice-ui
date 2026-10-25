# Exam System Rebuild: Comprehensive Manual Test Plan
**Target Spec:** [#106](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/106) (Exam System Rebuild: 6-Axis Certificate Wizard, 11-Component Authoring & IELTS/TOEIC/VSTEP Engine)  
**Covered Tickets:** [#107](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/107) through [#121](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/121) (Steps 01 to 15)  
**Branch:** `feat/exam-system-rebuild-106`  
**Test Design System:** "Bảng hiệu phòng thi" (`DESIGN.md` — 0 border-radius, 2px ink borders `#14181d`, IBM Plex typography, flat signage palette)

---

## 1. Test Environment Setup & Credentials

### 1.1 Local Launch
In separate terminal windows:
```bash
# Terminal 1: Start WebAPI (Port 5001)
dotnet run --project src/WebAPI

# Terminal 2: Start WebMVC (Port 5000)
dotnet run --project src/WebMVC
```
*Optional disposable database reset:* `scripts/crawl-env.sh fresh`

### 1.2 Demo Accounts (`Demo1234!`)
- **Administrator:** `admin@demo.local`
- **Learner (Existing History):** `learner@demo.local`
- **Fresh Learner (Zero History):** `learner.fresh@demo.local`

---

## 2. Test Execution Scenarios

### Scenario 1: Certificate Wizard & Preset Instantiation
**Covered Steps:** Step 01 ([#107](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/107)), Step 02 ([#116](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/116)), Step 03 ([#120](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/120))  
**Role:** Administrator (`admin@demo.local`)  
**Route:** `http://localhost:5000/admin/certificates/wizard`

#### Steps:
1. Navigate to `/admin/certificates/wizard`.
2. **Step 1 — Preset Picker:**
   - Click on the **IELTS (Academic/GT)** preset card.
   - Verify all fields pre-populate: Code: `IELTS_ACAD`, Name: `IELTS Academic`, Description pre-filled.
   - Click through **TOEIC CBT**, **TOEIC CAT**, **TOEIC S&W**, and **VSTEP** cards; observe skills and sections adapt dynamically in one click.
   - Select **Blank Custom Certificate**. Enter:
     - Certificate Code: `CUSTOM_ENG`
     - Name: `Custom Proficiency Certificate`
3. **Step 2 — Skills & Durations:**
   - Configure 3 skills:
     - `Listening`: 30 mins, Policy: *Audio playback duration + extra review countdown*
     - `Reading`: 60 mins, Policy: *Shared timer for the whole skill*
     - `Writing`: 60 mins, Policy: *Shared timer for the whole skill*
4. **Step 3 — Section 6-Axis Blueprint & Tools:**
   - For `Listening Section 1`:
     - Navigation Mode: `AUDIO_DRIVEN`
     - Stimulus: Check `RequireAudio`
     - Option Count: `4`
     - Cluster Size: `1`
   - For `Reading Section 1`:
     - Layout: `SPLIT_HORIZONTAL`
     - Navigation Mode: `FULL_SANDBOX`
     - Candidate Tools: Check `Text Highlighter & Notes`
     - Allowed Components: Check `mcq`, `multi_select`, `inline_completion`, `dropdown`
   - For `Writing Section 1 (Task 1)` & `Section 2 (Task 2)`:
     - Layout: `SPLIT_HORIZONTAL`
     - Candidate Tools: Check `Live Word Counter & Disable Spellcheck`
5. **Step 4 — Scoring Rules & Live Preview Calculator:**
   - Select Score Aggregation: `Average rounded to nearest 0.5 band (avg_half_step)`
   - Set Writing Task 1 weight = `1.0`, Task 2 weight = `2.0`
   - In the **Live Score Preview Placard**:
     - Input sample skill scores: Listening: `6.0`, Reading: `6.5`, Writing: Task 1: `6.0`, Task 2: `7.0` (Weighted Writing = `(6.0*1 + 7.0*2)/3 = 6.67 -> 6.5`)
     - Verify Overall Preview dynamically calculates: `(6.0 + 6.5 + 6.5)/3 = 6.33 -> 6.5` with matched badge.
6. Click **[LƯU CẤU HÌNH CHỨNG CHỈ]**.

#### Pass Criteria:
- Certificate is persisted without raw JSON errors.
- Redirects to `/admin/certificates` displaying `CUSTOM_ENG` in the active catalog table.

---

### Scenario 2: Dynamic Exam Studio Authoring & Pre-Publish Audit
**Covered Steps:** Step 04 ([#117](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/117))  
**Role:** Administrator (`admin@demo.local`)  
**Route:** `http://localhost:5000/admin/exams`

#### Steps:
1. On `/admin/exams`, click **[Tạo Đề Thi Mới]**.
2. Select Certificate: `Custom Proficiency Certificate` (`CUSTOM_ENG`).
3. Set Exam Title: `Custom Benchmark Exam 01`, Delivery Mode: `strict_exam`.
4. Open the created exam in Exam Studio: `/admin/exams/{id}/studio`.
5. **Verify Dynamic Studio Scaffolding:**
   - Top navigation bar renders top-level Skill tabs (`Listening`, `Reading`, `Writing`) matching `CUSTOM_ENG`.
   - Nested section tabs display live completion counts (e.g., `Task 1 (0/1)`).
   - Reading tab automatically presents split-pane stimulus on the left and question editor on the right (`SPLIT_HORIZONTAL`).
   - Question type dropdown in Reading is strictly filtered to the allowed types (`mcq`, `multi_select`, `inline_completion`, `dropdown`).
6. **Pre-Publish Checklist Verification:**
   - Observe the **Pre-Publish Readiness Checklist** drawer: all incomplete sections show yellow/red `[!]` badges.
   - Click a missing requirement link; verify it switches tabs and focuses directly on the incomplete slot.
   - Attempt to click **[Xuất bản Đề thi]**; verify publication is safely blocked with a modal explaining missing blueprint requirements.

#### Pass Criteria:
- Exam Studio dynamically adapts tabs and inputs without hardcoding `CertCode`.
- Readiness audit prevents premature publishing.

---

### Scenario 3: Batch Manifest Import & Dynamic Template Download
**Covered Steps:** Step 05 ([#118](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/118))  
**Role:** Administrator (`admin@demo.local`)  
**Route:** `http://localhost:5000/admin/exams`

#### Steps:
1. In `/admin/exams`, click **[Nạp gói đề thi (.zip/.json)]**.
2. In the modal, select Certificate: `CUSTOM_ENG`.
3. Click **[Tải file mẫu (.json)]**.
4. Open the downloaded `manifest-template.json` in a text editor:
   - Verify `certCode: "CUSTOM_ENG"` is pre-filled.
   - Verify sections contain the exact allowed question components and cluster sizes defined in Scenario 1.
5. In the Import modal, upload an existing test package (e.g. `docs/samples/toeic-sample-package.zip`).
6. Click **[Tiến hành nạp đề]**.

#### Pass Criteria:
- Dynamic manifest template downloads cleanly with correct section codes.
- Batch import parses and validates questions against blueprint constraints, logging 0 errors.

---

### Scenario 4: Section-Aware Quick Ingest & AI Section Fill
**Covered Steps:** Step 06 ([#121](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/121))  
**Role:** Administrator (`admin@demo.local`)  
**Route:** `http://localhost:5000/admin/exams/{id}/studio`

#### Steps:
1. In Exam Studio, select `Reading Section 1`.
2. Click **[Nạp nhanh / Quick Ingest]** from the section header.
3. Observe the modal placard:
   - Notice that instructions and placeholders adapt to Reading's allowed components (`inline_completion`, `multi_select`, etc.).
4. Click **[Nạp mẫu thử của phần này]**.
   - Input populates with valid passage and multi-component items.
5. Click **[Kiểm tra & Xem trước]**:
   - Staging table renders parsed questions with green `[READY]` badges.
6. Test Blueprint Mismatch Detection:
   - Introduce an invalid option (e.g., choice `E` when `OptionCount = 4`).
   - Click **[Kiểm tra & Xem trước]**; verify row displays red `[MISMATCH]` badge with explanatory text.
7. Revert to valid text and click **[Lưu vào phần thi]**.

#### Pass Criteria:
- Modal instructions adapt dynamically to the active section.
- Blueprint mismatches are caught before database commit.
- Questions appear immediately in the studio slots upon saving.

---

### Scenario 5: Preflight Onboarding Gate (Audio & Microphone Check)
**Covered Steps:** Step 07 ([#108](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/108))  
**Role:** Candidate (`learner@demo.local`)  
**Route:** `http://localhost:5000/exams/{id}/take`

#### Steps:
1. Log in as `learner@demo.local`.
2. Navigate to an exam with Listening or Speaking (e.g. IELTS or TOEIC CBT/S&W).
3. Click **[Bắt đầu thi]**:
   - Verify candidate is intercepted and redirected to `/exams/{id}/preflight?attemptId=...`.
   - Verify attempt timer has NOT started yet.
4. **Audio Pre-Buffering Test:**
   - Observe audio buffering bar reach 100%.
   - Click "Kiểm tra loa" to hear test oscillator tone.
5. **Microphone Check (if Speaking exam):**
   - Click **[Ghi âm thử (3s)]** and speak a short sample sentence.
   - Click **[Nghe lại]** to verify audio playback.
   - Select **"Có, nghe rõ"** on the clarity confirmation prompt.
6. Observe the **[VÀO PHÒNG THI]** button unlock from disabled state.
7. Click **[VÀO PHÒNG THI]**; verify redirection into sitting room `/take`.

#### Pass Criteria:
- Direct access to `/take` is strictly blocked until preflight passes.
- Audio and mic checks unlock the exam cleanly without consuming exam sitting time.

---

### Scenario 6: Sitting State Machine, Locks & Crash Recovery
**Covered Steps:** Step 08 ([#109](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/109)), Step 09 ([#110](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/110))  
**Role:** Candidate (`learner@demo.local`)  
**Route:** `http://localhost:5000/exams/take/{attemptId}`

#### Steps:
1. In the active sitting room, note the **Authorization Code** in the top header (e.g., `MÃ DỰ THI: EX-9B2F4A`).
2. Answer several questions (single choice, text input).
3. **Simulate Tab Close / Crash:**
   - Try to close or reload the browser tab: verify `beforeunload` warning modal fires: *"Bạn có chắc muốn rời khỏi phòng thi?"*.
   - Confirm tab closure.
4. **Crash Recovery Flow:**
   - Open a fresh browser window and navigate to `/exams/resume`.
   - Enter `EX-9B2F4A` and submit.
   - Verify immediate restoration to `/exams/take/{attemptId}` with:
     - Exact active skill and question.
     - Previously selected answers restored.
     - Server-authoritative timer continuing accurately from wall-clock deadline.
5. **Simulate Network Drop & Offline Buffer:**
   - In browser DevTools (F12) ➔ Network tab ➔ set throttling to **Offline**.
   - Select an answer on question 3.
   - Verify red offline banner appears: *"MẤT KẾT NỐI MẠNG. Dữ liệu đang được lưu tạm trên thiết bị..."*.
   - Switch DevTools back to **Online**.
   - Verify banner auto-dismisses and responses sync to server within 500ms.

#### Pass Criteria:
- Attempts resume cleanly via authorization code with zero answer loss.
- Offline buffering prevents lost responses during network drops.

---

### Scenario 7: Split-Screen Workspace, Highlighter & 11 Interactive Components
**Covered Steps:** Step 10 ([#111](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/111))  
**Role:** Candidate (`learner@demo.local`)  
**Route:** `http://localhost:5000/exams/take/{attemptId}` (IELTS or VSTEP Reading)

#### Steps:
1. Enter an IELTS or VSTEP Reading section (`LayoutType = SPLIT_HORIZONTAL`).
2. **Split Screen Verification:**
   - Verify 100vh dual-pane split view:
     - Left pane: Reading passage with independent vertical scrollbar.
     - Right pane: Questions with independent vertical scrollbar.
3. **Top Bar & Accessibility:**
   - Click the accessibility button in the top control bar.
   - Toggle font sizes: `Standard` ➔ `Large` ➔ `Extra Large`. Verify text scales smoothly.
   - Toggle high contrast: `Inverted (White on Black)` ➔ `Yellow on Black`. Verify instant repaint with 0 page reload.
4. **Tokenized Highlighter & Notes:**
   - In the reading passage, select a sentence with the mouse and right-click.
   - Click **[Tô sáng (Highlight)]**; verify text highlights in Sign Gold (`#f2b705`).
   - Select another phrase, click **[Ghi chú (Note)]**, type "Key fact", and save.
   - Toggle font size to Extra Large and Inverted contrast: **verify highlights and notes do not drift or shift**.
5. **Interactive Component Widgets:**
   - `mcq` & `mcq_3`: Select choices; verify solid ink `#14181d` inversion.
   - `multi_select`: If `MaxSelections = 2`, select 2 options; verify 3rd option is blocked from selection.
   - `inline_completion`: Type answers into blanks; verify `MaxWords` count badge updates live.
   - `dropdown`: Select matching options from dropdowns.
   - `drag_drop`: Click word bank chips into drop zones.

#### Pass Criteria:
- Dual-pane scrollbars operate independently.
- Highlights remain mathematically anchored to word tokens.
- All component widgets capture and autosave responses.

---

### Scenario 8: Audio Pacing, Cluster Cards & Multi-Stage CAT Unit Locking
**Covered Steps:** Step 11 ([#113](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/113))  
**Role:** Candidate (`learner@demo.local`)  
**Route:** `http://localhost:5000/exams/take/{attemptId}` (TOEIC CBT or TOEIC CAT)

#### Steps:
1. **TOEIC Part 1 (Masked Photo):**
   - Verify photograph displays prominently.
   - Options show only `(A)`, `(B)`, `(C)`, `(D)` radio buttons with masked text.
2. **TOEIC Part 2 (Audio-Only 3 Options):**
   - Verify only 3 options `(A)`, `(B)`, `(C)` render; option text is masked.
3. **TOEIC Part 3/4 (Cluster Cards):**
   - Verify 3 questions render simultaneously on one card synchronized to the audio track.
4. **One-Way Audio Progression:**
   - Verify "Previous" button is hidden/disabled during `AUDIO_DRIVEN` sections.
5. **Adaptive CAT Unit Locking (TOEIC CAT Reading):**
   - Complete questions in **Unit 1**.
   - Click **[Xem lại / Review Screen]**; test quick-filters: *Chỉ xem câu chưa làm*, *Chỉ xem câu đánh dấu*.
   - Click **[Hoàn thành Phần 1]**:
     - Confirmation modal appears: *"Giai đoạn 1 sẽ bị khóa vĩnh viễn..."*.
     - Click **[Xác nhận]**.
     - Verify Unit 1 questions become locked (disabled) and **Unit 2** questions load.

#### Pass Criteria:
- Option masking and 3-option counts match official TOEIC rules.
- Unit 1 permanently locks upon transition to Unit 2.

---

### Scenario 9: Controlled Writing Workspace & Lemma Keyword Detection
**Covered Steps:** Step 12 ([#112](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/112))  
**Role:** Candidate (`learner@demo.local`)  
**Route:** `http://localhost:5000/exams/take/{attemptId}` (TOEIC Writing or IELTS Writing)

#### Steps:
1. **Academic Writing Mode (IELTS Writing):**
   - In Task 1 / Task 2 editor:
     - Verify red squiggle spellcheck is disabled (`spellcheck="false"`).
     - Type an essay: observe `#live-word-count` update on every keystroke.
     - Observe minimum word threshold indicator (`Tối thiểu: 150 từ`).
2. **Strict Toolbar & Clipboard Blocking (TOEIC Writing):**
   - On photo-sentence task:
     - Try pressing `Ctrl+C`, `Ctrl+X`, `Ctrl+V` on the keyboard: verify native clipboard is blocked.
     - Select text and use on-screen buttons `[Cắt]`, `[Dán]`, `[Hoàn tác]`, `[Làm lại]`: verify text manipulation succeeds via on-screen toolbar.
3. **Lemma Keyword Stem Matching (TOEIC Writing Q1–5):**
   - Observe required keyword badges (e.g. `[photograph] [yesterday]`).
   - Type: *"The tourists were photographing the museum yesterday."*
   - Observe `[photograph]` badge turn green `[Đã dùng]` via inflectional lemma matching (`photographing` ➔ `photograph`).
4. **Hybrid Sub-Timer Lockout:**
   - Advance from Q1–5 pool to Q6:
     - Irreversible lock confirmation modal fires.
     - Confirming locks Q1–5 and starts dedicated 10-minute timer for Q6.

#### Pass Criteria:
- Keyboard copy/paste blocked when configured; on-screen toolbar functional.
- Required keyword badges match dynamically on inflected stems.
- Individual timers activate with zero rollover.

---

### Scenario 10: Automated Speaking Pacer & Audio Verification
**Covered Steps:** Step 13 ([#114](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/114))  
**Role:** Candidate (`learner@demo.local`)  
**Route:** `http://localhost:5000/exams/take/{attemptId}` (TOEIC Speaking or VSTEP Speaking)

#### Steps:
1. Enter a `SYSTEM_PACED_AUTO` Speaking section.
2. Verify manual "Next" / "Previous" navigation buttons are completely hidden.
3. **Automated State Machine Walk:**
   - **Phase 1 (Prep):** Observe countdown placard: *"CHUẨN BỊ TRẢ LỜI: 15s"*.
   - **Phase 2 (Beep):** When countdown reaches `00:00`, listen for the clear synthetic 1000Hz audio beep tone.
   - **Phase 3 (Record):** Immediately transitions to *"ĐANG THU ÂM: 30s"* with flashing red recording indicator and live audio level meter responding to your microphone.
   - **Phase 4 (Auto-Advance):** When recording reaches `00:00`, recording stops, uploads in background, and automatically advances to the next speaking question.
4. **Post-Section Audio Verification Screen:**
   - After final question recording finishes, verify automatic transition to the **Speaking Verification Screen**.
   - Verify list of all recorded questions with audio players.
   - Click play on recorded items to confirm playback.
   - Click **[Nộp bài nói để chấm AI]**.

#### Pass Criteria:
- Hands-free `Prep ➔ Beep ➔ Record ➔ Advance` cycle executes automatically without clicks.
- Post-speaking verification screen allows candidate review before submitting.

---

### Scenario 11: Practice Drilling (Mode B) & Deterministic Weakness Analysis
**Covered Steps:** Step 14 ([#119](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/119))  
**Role:** Learner (`learner@demo.local`)  
**Route:** `http://localhost:5000/practice`

#### Steps:
1. Navigate to `/practice` and start a practice drilling session on a Listening or Reading section containing multi-component questions.
2. **Practice QoL Controls:**
   - Click play on audio track; click the **[⏪ 5s]** button to verify seeking 5 seconds backward.
   - Click **[Xem Lời thoại / Transcript]**; verify transcript drawer expands.
3. **11-Component Answering & Rationale:**
   - Answer an `inline_completion` question and click **[Kiểm tra đáp án]**.
   - Verify accepted alternatives and explanation accordion expand with detailed rationale.
4. **Weakness Analysis Alignment:**
   - Complete and submit the practice session.
   - Navigate to `/weakness-analysis`:
     - Verify that answered multi-component practice items accurately contribute to the category and section accuracy percentages (`vw_UserCategoryAccuracy` & `vw_UserSectionAccuracyAll`).

#### Pass Criteria:
- 5s audio rewind and transcript toggles function properly.
- Explanations render post-answer.
- Accuracy views aggregate non-MCQ items correctly.

---

### Scenario 12: End-to-End Score Calibration & Result Breakdown
**Covered Steps:** Step 15 ([#115](https://github.com/DemNgheTiengMua/toeic-practice-ui/issues/115))  
**Role:** Candidate (`learner@demo.local`)  
**Route:** `http://localhost:5000/exams/attempts/{attemptId}/result`

#### Steps:
1. Submit an exam sitting across each official preset:
   - **IELTS on Computer:** Verify 0–9.0 band report with half-step rounding (`6.0`, `6.5`, `7.0`) and weighted writing calculation.
   - **TOEIC Standard CBT:** Verify 10–990 scaled score sum (Listening + Reading).
   - **TOEIC Adaptive CAT:** Verify combined score from Unit 1 + Unit 2.
   - **TOEIC S&W:** Verify independent 0–200 reporting for Speaking and Writing without artificial sum.
   - **VSTEP:** Verify 0–10 scaled band with whole-number rounding.
2. Review the score report placard:
   - Verify CEFR badge alignment (e.g. C1, B2, B1).
   - Verify section breakdown and diagnostic skill charts render cleanly per `DESIGN.md`.

#### Pass Criteria:
- All official certificate presets produce authentic score scales and CEFR bands.
- No NaN, null, or out-of-range scores appear on the final report.

---

## 3. Defect & Regression Checklist
- [ ] No `fail:` or unhandled exception entries in terminal console logs during all 12 scenarios.
- [ ] All forms, tables, and placards strictly respect `border-radius: 0`, `box-shadow: none`, and 2px ink borders.
- [ ] No raw unicode/emoji glyphs in exam sitting, wizard, or practice views.
- [ ] Composite foreign keys preserve Certificate Isolation across all created attempts and sections.
