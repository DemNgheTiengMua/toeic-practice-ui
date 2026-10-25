# Comprehensive Manual Test Plan — Exam System Rebuild (#106, Steps 01–15)

This manual test plan is **100% grounded in the real UI labels, routes, and seeded database rows** (`docs/seed-demo.sql` on `(localdb)\MSSQLLocalDB` / `ExtraEfficiency`). Every button label, dropdown option text, checkbox label, URL, Exam ID, Question stem, and answer key matches the live application verbatim.

---

## 0. Environment Startup & Seeded Reference Data

### 0.1 Start Services
Open two PowerShell terminals at `D:\Study\PRN3\Project\extra_efficiency_ui`:

1. **Terminal 1 (WebAPI — Port 5001)**:
   ```powershell
   dotnet run --project src/WebAPI
   ```
2. **Terminal 2 (WebMVC — Port 5000)**:
   ```powershell
   dotnet run --project src/WebMVC
   ```

### 0.2 Demo Accounts (Password for all: `Demo1234!`)
- **Admin**: `admin@demo.local` (`Demo1234!`)
- **Learner (with 8 credits & history)**: `learner@demo.local` (`Demo1234!`)
- **Fresh Learner**: `learner.fresh@demo.local` (`Demo1234!`)

### 0.3 Seeded Certificates in Database (`Certificates` Table)
| CertId | Code | Name | PresetCode | ScoreAggregationRule |
| :--- | :--- | :--- | :--- | :--- |
| `1` | `TOEIC` | `TOEIC Listening & Reading` | `toeic_lr_std` | `sum` |
| `2` | `IELTS` | `IELTS Academic & General` | `ielts_academic` | `avg_half_step` |
| `3` | `VSTEP` | `VSTEP — the Vietnamese national test` | `vstep` | `avg_whole` |
| `4` | `TOEIC_CAT` | `TOEIC Multi-Stage Adaptive CAT` | `toeic_lr_cat` | `sum` |
| `5` | `TOEIC_SW` | `TOEIC Speaking & Writing` | `toeic_sw` | `sum` |

> **IMPORTANT:** Because `TOEIC`, `IELTS`, `VSTEP`, `TOEIC_CAT`, and `TOEIC_SW` already exist in the database, clicking a preset card in the Certificate Wizard pre-fills that preset's code (`IELTS`, `TOEIC`, etc.). When saving a **new** certificate via the Wizard, change **Certificate Code \*** to a unique code (e.g., `IELTS_ACAD_2` or `CUSTOM_ENG`) so it does not collide with an existing certificate code.

### 0.4 Seeded Mock Exams Used in This Plan (`Exams` Table)
| ExamId | Certificate | Exam Name | Purpose | Seeded Questions & Mechanics |
| :--- | :--- | :--- | :--- | :--- |
| `1` | `TOEIC` | `ETS 2024 — Test 1` | `mock` | **9 questions** across `P1` (`AUDIO_DRIVEN`, `HideOptionText`, 4 options A–D), `P2` (`AUDIO_DRIVEN`, `HideOptionText`, 3 options A–C), `P3` & `P4` (`AUDIO_DRIVEN`, `ClusterSize = 3`), `P5`, `P6`, `P7` (`FULL_SANDBOX`, Highlighter & Notes). |
| `6` | `IELTS` | `Cambridge IELTS 18 — Academic Test 1` | `mock` | **2 questions**: `Q21` in `REC` (`AUDIO_DRIVEN`, `AUDIO_PLUS_CHECK` with 120s post-audio review), `Q22` in `PAS` (`FULL_SANDBOX`, `SPLIT_HORIZONTAL` 50/50 split-screen, Highlighter & Notes). |
| `10` | `VSTEP` | `VSTEP Sample Test 1` | `mock` | **2 questions**: `Q29` in `L3` (`AUDIO_DRIVEN`), `Q30` in `R4` (`FULL_SANDBOX`, `SPLIT_HORIZONTAL`, Highlighter & Notes). |
| `17` | `TOEIC_CAT` | `TOEIC CAT Benchmark Test 1` | `mock` | **7 questions** (`Q89`–`Q95`) across **Unit 1 (`P5_CAT`)** and **Unit 2 (`P7_CAT`)** with `UNIT_LOCKED_SANDBOX` and all 6 objective component types (`mcq`, `mcq_3`, `multi_select`, `inline_completion`, `dropdown`, `drag_drop`). |
| `18` | `TOEIC_SW` | `TOEIC Speaking & Writing Mock Test 1` | `mock` | **3 questions**: `Q1` in `SW_S1` (`SYSTEM_PACED_AUTO`, 5s prep / 5s record), `Q2` in `SW_W1` (`HYBRID_GROUP_LOCKED`, mandatory keywords `conference, yesterday`, On-Screen Cut/Paste toolbar), `Q3` in `SW_W3` (`HYBRID_GROUP_LOCKED` opinion essay). |

---

## Phase 1: Admin Authoring & Configuration (`admin@demo.local`)

Sign in at `http://localhost:5000/auth/signin` with `admin@demo.local` / `Demo1234!`.

---

### Scenario 1 — 4-Step Certificate Blueprint Wizard & Live Score Calculator (Steps 02 & 03 / #108, #109)

1. **Open Certificate List**:
   - Navigate to `http://localhost:5000/admin/certificates`.
   - Verify the 5 seeded certificates (`TOEIC`, `IELTS`, `VSTEP`, `TOEIC_CAT`, `TOEIC_SW`) appear in the table.
   - Click the top-right button **`+ Create Certificate Wizard`** (routes to `http://localhost:5000/admin/certificates/wizard`).

2. **Step 1 (`1. Blueprint Preset & Identity`)**:
   - Under **Official Exam Blueprint Presets**, click the card **`IELTS Academic`** (`4 skills • avg_half_step • 0.5-band`).
   - Verify the fields below auto-populate:
     - **Certificate Code \***: Change from `IELTS` to **`IELTS_PRO`** (must be unique!).
     - **Certificate Name \***: Change to **`IELTS Academic Pro Blueprint`**.
     - **Description**: Keep the pre-filled description.
     - **Score Aggregation Rule \***: Verify **`avg_half_step (IELTS 0.5-step band)`** is selected.
     - **Active Certificate (Available in candidate test-taking rooms and authoring studio)**: Checked.
   - Click **`Continue to Step 2: Skills & Timing ›`**.

3. **Step 2 (`2. Skills & Timing`)**:
   - Verify 4 skill rows are populated from the preset:
     1. Skill Code: `LIST` | Skill Name: `Listening` | Modality: `Listening` | Duration (Min): `30`
     2. Skill Code: `READ` | Skill Name: `Reading` | Modality: `Reading` | Duration (Min): `60`
     3. Skill Code: `WRIT` | Skill Name: `Writing` | Modality: `Writing` | Duration (Min): `60`
     4. Skill Code: `SPEA` | Skill Name: `Speaking` | Modality: `Speaking` | Duration (Min): `14`
   - *(Optional test)*: Click **`+ Add Skill`** in the top-right of the card header to see a 5th skill row appear, then click **`Remove`** on that 5th row.
   - Click **`Continue to Step 3: Section Blueprints ›`**.

4. **Step 3 (`3. Section Blueprints & Tools`)**:
   - Verify 4 skill groups appear (`Listening (LIST)`, `Reading (READ)`, `Writing (WRIT)`, `Speaking (SPEA)`), and every section card displays all 5 configuration rows:
     - **Listening (`LIST`) — Section #1 (`REC`)**:
       - **Row 1**: `Section Code`: `REC` | `Section Name`: `Four recordings: everyday conversation to academic lecture` | `Score Weight`: `1` | `Expected Items`: `40`
       - **Row 2**: `Navigation Behavior`: **`AUDIO_DRIVEN (Synced to playback / One-way)`** | `Screen Layout`: **`SPLIT_HORIZONTAL (Passage left, Questions right)`**
       - **Row 3**: `Section Timing Policy`: **`AUDIO_PLUS_CHECK (Audio duration + post-audio review countdown)`** | `Post-Audio Review (Sec)`: `120`
       - **Row 4 (`Authoring Slot Blueprint & Constraints`)**: `Default Option Count`: `4` | `Cluster Size`: `1` | `Adaptive Unit Count`: `1` | `Req Keyword Count`: `0` | Checkbox **`Require Audio File`** is checked.
       - **Row 5**: Under **Allowed Question Components**, check **`Single Choice (4-opt)`** and **`Inline Text Completion`**. Under **Candidate Sitting Room Tools**, verify **`Text Highlighter & Notes Tool`** is checked.
     - **Reading (`READ`) — Section #1 (`PAS`)**:
       - **Row 2**: `Navigation Behavior`: **`FULL_SANDBOX (Free back & forth navigation)`** | `Screen Layout`: **`SPLIT_HORIZONTAL (Passage left, Questions right)`**
       - **Row 3**: `Section Timing Policy`: **`SECTION_SHARED (Shared countdown timer for whole section)`**
       - **Row 4**: Checkbox **`Require Passage Stimulus`** is checked.
       - **Row 5**: Under **Allowed Question Components**, check **`Single Choice (4-opt)`**, **`Multi-Select Checkbox`**, **`Inline Text Completion`**, and **`Dropdown Matching`**. Under **Candidate Sitting Room Tools**, verify **`Text Highlighter & Notes Tool`** is checked.
     - **Writing (`WRIT`) — Section #1 (`T1`) & Section #2 (`T2`)**:
       - Verify `T1` has `Score Weight`: `1` and `T2` has `Score Weight`: `2`.
       - Verify both have `Screen Layout`: **`SPLIT_HORIZONTAL (Passage left, Questions right)`**, **Allowed Question Components**: **`Free Response (AI Graded)`**, and **Candidate Sitting Room Tools**: **`Live Word Counter`** and **`Disable Browser Spellcheck`** checked.
     - **Speaking (`SPEA`) — Section #1 (`INT`)**:
       - Verify `Navigation Behavior`: **`SYSTEM_PACED_AUTO (Prep ➔ Beep ➔ Record)`** and `Section Timing Policy`: **`ITEM_FIXED (Per-item timed prep and recording)`**.
   - Click **`Next: Scoring Rules & Live Preview ›`**.

5. **Step 4 (`4. Scoring Rules & Live Preview`)**:
   - Under **Relative Section Multipliers & Weights**, verify all 4 skills (`SKILL #1: LISTENING (LIST)`, `SKILL #2: READING (READ)`, `SKILL #3: WRITING (WRIT)`, `SKILL #4: SPEAKING (SPEA)`) and all 5 sections (`REC`, `PAS`, `T1`, `T2`, `INT`) are displayed!
   - Under **`BẢNG HIỆU PHÒNG THI • LIVE SCORE PREVIEW CALCULATOR`**, adjust the **`SAMPLE CORRECT:`** inputs:
     - Set `LISTENING (LIST)` (`REC`) to `30` (`/ 40 questions`).
     - Set `READING (READ)` (`PAS`) to `30` (`/ 40 questions`).
     - Set `WRITING (WRIT)` (`T1` and `T2`) to `1` (`/ 1 questions`).
     - Set `SPEAKING (SPEA)` (`INT`) to `2` (`/ 3 questions`).
   - Verify **`OVERALL ESTIMATED SCORE`**, **`MATCHED PROFICIENCY BAND`**, and **`Skill Breakdown Summary`** recalculate live in the dark scoreboard panel.
   - Click **`[LƯU] Save & Persist Certificate Blueprint`**.
   - Verify you are redirected to `http://localhost:5000/admin/certificates` with the green banner:
     `Certificate 'IELTS Academic Pro Blueprint' (IELTS_PRO) saved with 4 skills and 5 sections.`

---

### Scenario 2 — Score Conversion & CEFR Band Table Editor (`ScoreConversion.cshtml`) (Step 03 / #109)

1. Navigate to `http://localhost:5000/admin/score-conversion?cert=TOEIC_CAT`.
2. Verify:
   - The **Certificate:** dropdown at top-right shows `TOEIC_CAT — TOEIC Multi-Stage Adaptive CAT`.
   - Two skill conversion cards appear: **`Listening (LIST)`** and **`Reading (READ)`** with `Correct answers (Raw)` and `Scaled Score` inputs.
   - The **`Proficiency Bands / CEFR Reference (TOEIC_CAT)`** table shows `CAT_B1` (`Intermediate`, `400`–`695`) and `CAT_B2` (`Professional`, `700`–`990`).
3. Scroll down to **`BẢNG HIỆU PHÒNG THI • LIVE SCORE PREVIEW SANDBOX`**:
   - Change the `Listening (LIST)` input to `5` and `Reading (READ)` input to `5`.
   - Verify **`SIMULATED TOTAL SCORE`** updates to `1050` (or `850` for raw `5` + `4`) and **`MATCHED PROFICIENCY BAND`** updates in real time.
4. Click **`Save table`** and verify the green success alert appears.

---

### Scenario 3 — Unified 3-Column Exam Authoring Studio & Autosave (Step 04 / #110)

1. Navigate to `http://localhost:5000/admin/exams`.
2. In the exams table, locate **`TOEIC CAT Benchmark Test 1`** (`ExamId = 17`, Certificate `TOEIC_CAT`) or **`ETS 2024 — Test 1`** (`ExamId = 1`, Certificate `TOEIC`) and click **`Studio`** (or navigate directly to `http://localhost:5000/admin/exams/17/studio`).
3. Verify the **3-Column Authoring Studio** layout:
   - **Left Panel (`Blueprint Outline`)**:
     - Shows `Skills & Sections` tree:
       - `Listening` (`P1_CAT — Photographs (Adaptive)`, `P2_CAT — Question-Response (Adaptive)`)
       - `Reading` (`P5_CAT — Incomplete Sentences (Stage 1 & 2)`, `P7_CAT — Reading Comprehension (Adaptive)`)
   - **Center Panel (`Authoring Workstation`)**:
     - Header shows section title, badge `Nav: UNIT_LOCKED_SANDBOX`, badge `Layout: STANDARD_VERTICAL`, and buttons **`+ Quick Ingest`** and **`+ Add Cluster / Group`**.
   - **Right Panel (`Publish Readiness`)**:
     - Shows `Questions: 7 / 11` (or current count), `Blocking Errors`, `Warnings`, and buttons **`Publish Exam`**, **`Export ZIP`**, and **`Export JSON`**.
4. **Test All 6 Objective Question Component Panels in `P5_CAT`**:
   - Click **`P5_CAT — Incomplete Sentences (Stage 1 & 2)`** in the left outline.
   - Scroll through `Question #1` to `Question #6` inside **`Group / Cluster #1`**:
     - **Question #1 (`Q89`)**: `Component Type` is **`MCQ (4-Option)`** (`mcq`). Shows 4 radio rows (`A`, `B`, `C`, `D`) with option text and rationale inputs.
     - **Question #2 (`Q90`)**: `Component Type` is **`MCQ (3-Option)`** (`mcq_3`). Shows 3 radio rows (`A`, `B`, `C`).
     - **Question #3 (`Q91`)**: `Component Type` is **`Multi-Select Checkbox`** (`multi_select`). Shows **`Max Selections:`** (`2`) and checkboxes `A`, `B` checked.
     - **Question #4 (`Q92`)**: `Component Type` is **`Inline Text Completion`** (`inline_completion`). Shows **`Accepted Answers (pipe-separated | or JSON array):`** (`["15","fifteen"]`), **`Max Words:`** (`1`), and checkbox **`Allow Numeric Digits`**.
     - **Question #5 (`Q93`)**: `Component Type` is **`Dropdown Matching`** (`dropdown`). Shows options `A` (`consequently`), `B` (`otherwise`), `C` (`nevertheless`).
     - **Question #6 (`Q94`)**: `Component Type` is **`Drag-and-Drop`** (`drag_drop`). Shows **`Target / Choice Associations:`** (`Slot A`, `Slot B`, `Slot C`).
5. **Test Debounced Autosave (`1500ms`)**:
   - In **Question #1 (`Q89`)**, edit the **`Explanation / pedagogical rationale`** textarea at the bottom of the card and type:
     ```text
     Use 'before' directly in front of a day of the week to denote a deadline.
     ```
   - Watch the top-right status indicator (`#autosave-status`): it changes to **`Saving...`** (in blue) and after 1.5 seconds changes to **`Saved at HH:MM:SS`** (in green).

---

### Scenario 4 — Section-Aware Quick Ingest Modal (`_QuickIngestModal.cshtml`) (Step 05 / #111)

1. Stay in `http://localhost:5000/admin/exams/17/studio` on section **`P5_CAT — Incomplete Sentences (Stage 1 & 2)`**.
2. Click the **`+ Quick Ingest`** button in the center workstation header.
3. Verify the modal **`Section-Aware Quick Ingest & Staging Box`** opens:
   - **Target Section:** dropdown has **`[P5_CAT] Incomplete Sentences (Stage 1 & 2)`** selected.
   - **Mode:** radio buttons **`Append`** (checked) and **`Replace section`**.
4. **Test Sample Loader & Live Pre-flight Blueprint Validation**:
   - Click the **`Load Section Sample`** button.
   - Verify the scratchpad textarea (`Paste Questions Text or JSON:`) populates with sample questions and automatically runs **`Pre-flight Blueprint Validation`**, rendering the **`Pre-Flight Blueprint Preview`** table with green **`READY`** badges.
5. **Test Blueprint Mismatch Detection (Error Highlight)**:
   - In the **`Target Section:`** dropdown inside the modal, switch to **`[P2_CAT] Question-Response (Adaptive)`** (which enforces `3 Options (A-C)` and `Audio-Only Spoken Keys`).
   - Replace the textarea content with a 4-option question (which violates `P2_CAT`'s 3-option blueprint):
     ```text
     1. When will the maintenance crew arrive?
     *A. At 2:00 PM
     B. In the main lobby
     C. Yes, they did
     D. Tomorrow morning
     ```
   - Click **`Pre-flight Blueprint Validation`**.
   - Verify the preview row shows a red **`MISMATCH`** badge explaining that Option D is not allowed in a 3-option section, and **`Commit Questions to Section`** is disabled!
6. **Test Valid Commit**:
   - Switch **`Target Section:`** back to **`[P5_CAT] Incomplete Sentences (Stage 1 & 2)`**.
   - Click **`Pre-flight Blueprint Validation`**.
   - Verify the summary badge turns green: `✔ 1 question(s) fully conform to section blueprint. Ready to commit.`
   - Click **`Commit Questions to Section`**. Verify the modal commits and reloads the studio with the new question added.

---

### Scenario 5 — Visual Audio Timestamp Slicer & Export ZIP/JSON (Step 06 / #112)

1. Navigate to `http://localhost:5000/admin/exams/1/studio` (`ETS 2024 — Test 1`).
2. In the left **`Blueprint Outline`**, click **`P3 — Conversations`** (under `Listening`).
3. In the center workstation, locate the **`Section Master Audio & Visual Timestamp Slicer`** box:
   - Verify the **`Section Audio URL / Path`** input contains `/audio/toeic/test1-p3.mp3` (or click **`Upload Audio`** to upload any `.mp3`/`.wav`).
   - Below the `<audio>` player, locate the **`Timestamp Slicer Queue (Click group, then mark during playback):`** table.
   - Click the row for **`Group #1`** in the slicer table (it highlights).
   - Play the audio player (or scrub the playhead to a few seconds in) and click **`[ [ Set Start ]`**, advance a few seconds, and click **`[ Set End ] ]`**.
   - Verify the **`Start (ms)`** and **`End (ms)`** fields in the **`Group / Cluster #1`** card below update with the exact millisecond timestamps, and **`Save Timestamps`** persists them.
4. **Test Export Package**:
   - In the right **`Publish Readiness`** panel, click **`Export JSON`** (`/admin/exams/1/export?format=json`) and verify a JSON manifest downloads.
   - Click **`Export ZIP`** (`/admin/exams/1/export?format=zip`) and verify a `.zip` archive downloads.

---

## Phase 2: Candidate Exam Sitting Room (`learner@demo.local`)

Sign out of Admin (or open an Incognito window) and sign in at `http://localhost:5000/auth/signin` as **`learner@demo.local`** / **`Demo1234!`**.

> **How to switch certificates as a Learner:**
> Navigate to `http://localhost:5000/certificates`, scroll to the desired certificate card (`TOEIC`, `IELTS`, `VSTEP`, `TOEIC_CAT`, or `TOEIC_SW`), and click **`Switch to <CODE>`**.

---

### Scenario 6 — Pre-Flight Equipment Gate, Audio Pre-Buffering & Authorization Code Recovery (Step 07 / #113)

1. **Switch Active Certificate to `TOEIC`**:
   - Go to `http://localhost:5000/certificates` and click **`Switch to TOEIC`**.
2. **Start Mock Exam (`ETS 2024 — Test 1`, `ExamId = 1`)**:
   - Navigate to `http://localhost:5000/exams/1/instructions`.
   - Check the box **`I have read the instructions and am ready to begin`**.
   - Click **`Start test — uses 1 credit`**.
3. **Verify Pre-Flight Readiness Check (`/exams/1/preflight?attemptId=...`)**:
   - Verify you are routed to **`Pre-Flight Readiness Check: ETS 2024 — Test 1`**.
   - **STEP 1 (`Audio Asset Pre-Buffering`)**: Verify the progress bar buffers the exam audio tracks (`100%`) and the badge updates to **`100% BUFFERED`** (or **`READY (NO AUDIO)`** if no audio tracks).
   - **STEP 2 (`Speaker & Headphone Sound Check`)**: Click **`Play Test Chime`** and verify the chime plays and status shows **`TEST READY`**.
   - Verify the bottom notice turns green (`All Pre-flight Requirements Satisfied`) and the **`Start Test →`** button becomes enabled.
   - Click **`Start Test →`** to enter the sitting room (`/exams/take/{attemptId}`).
4. **Verify Topbar Placards, Accessibility Modal & Volume Slider (Step 12 / #118)**:
   - On the top bar of the sitting room, locate the badge **`MÃ DỰ THI: EX-XXXXXX`** (`#auth-code-placard`). **Copy this `EX-XXXXXX` code!**
   - Click **`Aa Cỡ chữ & Tương phản`** (`#btn-open-accessibility-modal`) on the top bar:
     - Click **`Large (115%)`** and **`Extra Large (130%)`**, then back to **`Standard (100%)`**.
     - Click **`Chữ trắng nền đen (Inverted: White on Black)`**, then **`Chữ vàng nền đen (High Contrast: Yellow on Black)`**, then back to **`Chữ đen nền trắng (Standard: Black on White)`**.
     - Click **`Hoàn tất (Apply & Close)`**.
   - Drag the topbar volume slider (`#exam-volume-slider`) and verify the percentage label (`100%`) updates and persists in `localStorage`.
5. **Test Session Recovery via Authorization Code (`/exams/resume`)**:
   - Click **`Exit`** on the top-right of the sitting room, then click **`Exit (resume later)`** in the modal to return to `/exams`.
   - On `http://localhost:5000/exams`, click the top-right button **`Khôi phục phiên thi`** (routes to `http://localhost:5000/exams/resume`).
   - Paste your **`EX-XXXXXX`** code into **`MÃ DỰ THI CỦA BẠN (AUTHORIZATION CODE)`** and click **`Tiếp tục làm bài (Resume)`**.
   - Verify you are returned directly to `/exams/take/{attemptId}` at the exact question and remaining timer!

---

### Scenario 7 — TOEIC Listening & Reading Mechanics: Masked Options (`A–D` / `A–C`) & 3-Question Clusters (Step 08 / #114)

While inside the active attempt for **`ETS 2024 — Test 1`** (`ExamId = 1`):

1. **Question 1 (`P1 — Photographs`, `AUDIO_DRIVEN`, `HideOptionText = true`, `OptionCount = 4`)**:
   - Verify the question stem is hidden/masked for audio-only listening and the 4 choices render as horizontalplacards **`A`**, **`B`**, **`C`**, **`D`** (`option--masked`) with no printed option text.
   - Verify the **`Previous`** button is hidden (`AUDIO_DRIVEN` one-way navigation).
   - Click placard **`C`** (correct answer: *"A woman is writing on a whiteboard."*). Verify it inverts to solid ink (`#14181d`) with white text.
   - Click **`Next question`**.
2. **Question 2 (`P2 — Question-Response`, `AUDIO_DRIVEN`, `HideOptionText = true`, `OptionCount = 3`)**:
   - Verify only **3 horizontal placards** (**`A`**, **`B`**, **`C`**) render — **Option D is not rendered**.
   -Verify backward navigation to Question 1 in the bottom palette (`#bottom-question-palette`) is disabled (`palette-chip--disabled`).
   - Click placard **`B`** (correct answer: *"On the conference room table."*) and click **`Next question`**.
3. **Question 3 (`P3 — Conversations`, `AUDIO_DRIVEN`, `ClusterSize = 3`)**:
   - Verify the **`#cluster-card-container`** renders the conversation cluster card with full stem and 4 options (`A`–`D`).
   - Select **`B`** (`By tomorrow noon`) and click **`Next question`**.

---

### Scenario 8 — Multi-Stage Adaptive CAT (`UNIT_LOCKED_SANDBOX`) & All 6 Interactive Question Widgets (Steps 08, 09, 11 / #114, #115, #117)

1. **Switch Active Certificate to `TOEIC_CAT`**:
   - Navigate to `http://localhost:5000/certificates` and click **`Switch to TOEIC_CAT`**.
2. **Start `TOEIC CAT Benchmark Test 1` (`ExamId = 17`)**:
   - Navigate to `http://localhost:5000/exams/17/instructions`.
   - Check **`I have read the instructions and am ready to begin`** and click **`Start test — uses 1 credit`**.
   - On the Pre-Flight page, click **`Start Test →`**.
3. **Answer All 6 Component Types in Unit 1 (`P5_CAT`, Questions 1–6)**:
   - Verify in the right **Questions (1–7)** grid and bottom **BẢNG CÂU HỎI** palette that **Questions 1–6** (Unit 1) are unlocked, while **Question 7** (Unit 2) is hidden/locked (`is-locked`).
   - **Question 1 (`Q89`, `mcq` 4-option)**:
     - Stem: *"The quarterly budget report must be submitted _____ Friday at 5:00 PM."*
     - Select **`A`** (`before`) and click **`Next question`**.
   - **Question 2 (`Q90`, `mcq_3` 3-option)**:
     - Stem: *"Where did Ms. Chen leave the presentation handouts?"*
     - Verify 3 options (`A`, `B`, `C`) appear. Select **`A`** (`On the conference table`) and click **`Next question`**.
   - **Question 3 (`Q91`, `multi_select`, `MaxSelections = 2`)**:
     - Stem: *"Select TWO benefits resulting from the newly installed automated inventory system:"*
     - Check **`A`** (`Decreased fulfillment errors`) and **`B`** (`Real-time stock level visibility`).
     - Try checking **`C`** (`Immediate relocation to another province`) — verify the UI prevents selecting more than 2 checkboxes!
     - Click **`Next question`**.
   - **Question 4 (`Q92`, `inline_completion`)**:
     - Stem: *"The express shuttle departs every [blank] minutes from terminal two."*
     - In the inline text input (`.completion-input`), type **`15`** (or **`fifteen`**).
     - Click **`Next question`**.
   - **Question 5 (`Q93`, `dropdown`)**:
     - Stem: *"Choose the best connective word to complete the sentence clause."*
     - In the `<select>` dropdown widget, select **`A: consequently`**.
     - Click **`Next question`**.
   - **Question 6 (`Q94`, `drag_drop`)**:
     - Stem: *"Drag the appropriate department heading to the organizational slot."*
     - Drag the chip **`Quality Assurance`** (or click the chip and click the dashed drop zone `.drop-zone`) to place it into the slot.
4. **Test Reading Review Screen Modal (`#review-screen-modal`)**:
   - Before finishing Unit 1, click **`Bảng xem lại (Review Screen)`** (`#btn-open-review-screen`) next to the Next button.
   - Verify the modal **`BẢNG XEM LẠI PHẦN THI ĐỌC (READING REVIEW SCREEN)`** opens with:
     - 3 stat boxes: **`Đã trả lời (Answered)`** (`6`), **`Đánh dấu (Flagged)`** (`0`), **`Chưa trả lời (Unanswered)`** (`0`).
     - Quick-action filter buttons: **`Jump to First Unanswered`**, **`Review Only Unanswered`**, **`Review Only Flagged`**, **`Show All`**.
   - Click **`Quay lại bài làm (Return)`**.
5. **Test Irreversible Adaptive Unit Transition (`Unit 1` ➔ `Unit 2`)**:
   - On Question 6 (the last question of Unit 1), notice the primary button reads **`Hoàn thành Giai đoạn 1 (Finish Unit 1)`**. Click it!
   - Verify the confirmation modal **`XÁC NHẬN KHÓA GIAI ĐOẠN (LOCK UNIT CONFIRMATION)`** (`#unit-lock-modal`) opens with the red warning:
     *"Sau khi xác nhận, Giai đoạn 1 sẽ bị khóa vĩnh viễn và bạn không thể quay lại."*
   - Click **`Xác nhận khóa và tiếp tục (Confirm & Proceed)`**.
   - Verify you advance to **Question 7 (`Q95`, Unit 2, `P7_CAT`)** and **Questions 1–6 (Unit 1)** are now permanently locked!
6. **Answer Question 7 & Submit Exam**:
   - Stem: *"What is mentioned as a key benefit of the Apex-Horizon merger?"*
   - Select **`A`** (`Reduced shipping overhead costs`).
   - Click **`Review & Submit exam`**, then in the **`Submit exam?`** modal click **`Submit now`**.
   - Verify the result page displays **7/7 correct**, Scaled Score **`850`**, and Band **`CAT_B2`** (`Professional`).
   - Click **`Review answers`** (`/exams/review/{attemptId}`) and verify all 7 questions show green **`Correct answer`** badges.

---

### Scenario 9 — IELTS 50/50 Split-Screen Layout (`SPLIT_HORIZONTAL`) & Tokenized Text Highlighter + Sticky Notes (Steps 09 & 11 / #115, #117)

1. **Switch Active Certificate to `IELTS`**:
   - Navigate to `http://localhost:5000/certificates` and click **`Switch to IELTS`**.
2. **Start `Cambridge IELTS 18 — Academic Test 1` (`ExamId = 6`)**:
   - Navigate to `http://localhost:5000/exams/6/instructions`.
   - Check **`I have read the instructions and am ready to begin`** and click **`Start test — uses 1 credit`**.
   - Complete the Pre-Flight check and click **`Start Test →`**.
3. **Test Tokenized Text Highlighter & Sticky Notes (`#highlighter-popover` & `#sticky-note-modal`)**:
   - On **Question 1 (`Q21`, `REC`)** or **Question 2 (`Q22`, `PAS`)**, use your mouse to select/drag across several words in the question stem or reading passage (`.ee-tokenized-text`).
   - Verify the floating toolbar (`#highlighter-popover`) appears directly above your selection with 3 buttons:
     - **`Tô sáng`** (Sign Gold `#f2b705`)
     - **`Ghi chú`** (White)
     - **`Xóa`** (Sign Red `#b31217`)
   - Click **`Tô sáng`**: verify the selected words turn Sign Gold (`.ee-token--highlighted`).
   - Select another phrase and click **`Ghi chú`**:
     - Verify the modal **`Thêm ghi chú bài thi`** (`#sticky-note-modal`) opens showing your quoted text (`#sticky-note-quote`).
     - In the textarea (`Nhập ghi chú của bạn tại đây...`), type:
       ```text
       Key evidence for urban heat reduction: 2.8 degrees C.
       ```
     - Click **`Lưu ghi chú`**.
     - Verify a **`[NOTE]`** pin badge (`.ee-note-pin`) appears at the end of the highlighted phrase. Click the **`[NOTE]`** pin to view your saved note.
   - Refresh the browser page (`F5`): verify the gold highlights and `[NOTE]` pin persist via `CandidateNotesJson`!
4. **Verify 50/50 Split-Screen Layout on Reading (`PAS`)**:
   - When on **Question 2 (`Q22`, `PAS`)**, verify `#exam-main-layout` switches to `.exam-layout--split` (`50% / 50%` two-column split screen) with **`ĐOẠN VĂN / TÀI LIỆU (READING PASSAGE & STIMULUS)`** scrolling independently on the left and the question pane on the right.

---

### Scenario 10 — Automated Speaking Pacer (`SYSTEM_PACED_AUTO`), Hybrid Writing Group Lock (`HYBRID_GROUP_LOCKED`), Mandatory Keywords & On-Screen Cut/Paste Toolbar (Steps 10 & 11 / #116, #117)

1. **Switch Active Certificate to `TOEIC_SW`**:
   - Navigate to `http://localhost:5000/certificates` and click **`Switch to TOEIC_SW`**.
2. **Start `TOEIC Speaking & Writing Mock Test 1` (`ExamId = 18`)**:
   - Navigate to `http://localhost:5000/exams/18/instructions`.
   - Check **`I have read the instructions and am ready to begin`** and click **`Start test — uses 1 credit`**.
3. **Verify Interactive Microphone Pre-Flight Check (`STEP 3` in `Preflight.cshtml`)**:
   - Because `TOEIC_SW` includes Speaking, verify **`STEP 3: Interactive Microphone & Voice Quality Check`** appears on the Pre-Flight page.
   - Click **`Record 3s Sample`** and speak `"The quick brown fox jumps over the lazy dog."` into your microphone.
   - Watch the **`Microphone Live Level Meter`** (`#mic-meter-fill`) move and badge show **`VOICE DETECTED`**.
   - After 3 seconds, the sample plays back and displays **`CLARITY CONFIRMATION: Is your voice recording clear, audible, and free of excessive static?`**.
   - Click **`Yes, Audio is Clear`**. Verify the badge turns green (**`MIC VERIFIED`**) and **`Start Test →`** unlocks. Click **`Start Test →`**.
4. **Test Automated Speaking Pacer (`Question 1`, `SW_S1`, `SYSTEM_PACED_AUTO`)**:
   - Upon entering the sitting room on **Question 1**, verify:
     - Both **`Previous`** and **`Next question`** buttons are hidden (`SYSTEM_PACED_AUTO`).
     - **Step 1 (Prep Countdown)**: Blue placard **`CHUẨN BỊ TRẢ LỜI: 5s`** (`#speaking-prep-placard`) counts down from `5s` to `0s`.
     - **Step 2 (Audible Beep)**: At `0s`, an 880Hz beep tone plays automatically.
     - **Step 3 (Recording Countdown)**: Red pulsing placard **`ĐANG THU ÂM: 5s`** (`#speaking-record-placard`) appears along with the **`Mức âm lượng micro`** live meter (`#audio-level-container`), counting down from `5s` to `0s`.
     - **Step 4 (Auto-Upload & Verification Screen)**: At `0s`, **`Đang lưu bản thu âm và tự động chuyển câu hỏi tiếp theo...`** appears, uploads the recording, and displays **`BẢNG XÁC NHẬN BẢN GHI ÂM PHẦN THI NÓI (SPEAKING AUDIO VERIFICATION)`** (`#speaking-verification-screen`) showing **`Câu 1 / 3`** with green badge **`Đã ghi âm [ĐÃ LÀM]`** and an `<audio controls>` player to replay your voice.
5. **Test Writing Mandatory Keyword Badges, Live Word Counter & On-Screen Cut/Paste Toolbar (`Question 2`, `SW_W1`, `HYBRID_GROUP_LOCKED`)**:
   - Navigate to **Question 2** (`SW_W1 — Write a sentence based on a picture`).
   - Verify:
     - **Mandatory Keyword Badges (`Từ khóa bắt buộc:`)**: Two dashed badges **`conference`** and **`yesterday`** (`badge--pending`) appear above the response editor.
     - **On-Screen Cut/Paste/Undo/Redo Toolbar (`#writing-onscreen-toolbar`)**: Four buttons appear above the textarea: **`Cắt (Cut)`**, **`Dán (Paste)`**, **`Hoàn tác (Undo)`**, **`Làm lại (Redo)`**.
     - **Minimum Word Counter (`#min-words-badge`)**: Shows **`Tối thiểu: 5 từ`** in red with **`(Chưa đạt số từ tối thiểu)`**.
   - In the **`Your response`** textarea (`#writing-editor`), type:
     ```text
     The team attended the international conference yesterday afternoon.
     ```
   - Verify:
     - Both **`conference`** and **`yesterday`** badges immediately turn solid Sign Green (`#0a6b3c`, `badge--used`)!
     - **`Số từ:`** updates to **`8`**, **`Tối thiểu: 5 từ`** turns green, and the under-length warning disappears!
     - Select the word `international` in the textarea, click **`Cắt (Cut)`**, move the cursor, and click **`Dán (Paste)`** or **`Hoàn tác (Undo)`** to verify the on-screen editing toolbar works.
6. **Test Irreversible Hybrid Group Lock (`Question 2` ➔ `Question 3`, `#group-lock-modal`)**:
   - Click **`Next question`** to move from Question 2 (`SW_W1`, Group 2) to Question 3 (`SW_W3`, Group 3).
   - Verify the confirmation modal **`XÁC NHẬN CHUYỂN PHẦN THI (GROUP LOCK CONFIRMATION)`** (`#group-lock-modal`) appears with the red warning:
     *"Xác nhận chuyển sang câu tiếp theo: Phần hiện tại sẽ bị khóa vĩnh viễn và thời gian còn lại sẽ không được bảo lưu."*
   - Click **`Xác nhận chuyển và khóa (Confirm & Proceed)`**.
   - Verify you advance to **Question 3 (`SW_W3 — Write an opinion essay`)** and the **`Previous`** button is now disabled so you cannot return to the locked group!

---

## Phase 3: Practice Mode QoL & AI Grading Reliability

### Scenario 11 — Practice Mode QoL: `[-5s]` Audio Rewind, Transcript Drawer, Explanation Accordion & Keyboard Shortcuts (Step 13 / #119)

1. **Switch Active Certificate to `TOEIC`**:
   - Navigate to `http://localhost:5000/certificates` and click **`Switch to TOEIC`**.
2. **Start a Listening Practice Drill**:
   - Navigate to `http://localhost:5000/practice`.
   - Leave **`Questions per session`** at `20 questions`, **`Question source`** at `All exams`, and **`Feedback type`** at `Show the answer right after each question`.
   - Under **`Listening`**, click the card **`P3`** (`Conversations`).
3. **Verify Practice QoL Controls in `Practice/Take.cshtml`**:
   - **Audio `[-5s]` Rewind Button**: Below the audio player, click **`[-5s]`** (`#btn-audio-rewind-5s`) during playback and verify the audio playhead rewinds 5 seconds (clamped to the question's `StartMs`).
   - **Collapsible Transcript Drawer**: Click **`[Xem Lời thoại / Transcript]`** (`#btn-toggle-transcript`) and verify the **`Transcript / Lời thoại:`** drawer (`#transcript-drawer`) expands and collapses.
   - **Keyboard Shortcuts Modal (`?`)**: Click **`Controls ?`** (`#btn-controls-modal`) on the top bar (or press `?` on your keyboard) to open the **`Keyboard Controls`** modal (`1`–`4`, `Enter`/`Space`, `H`, `S`, `K`, `N`/`P`). Click **`Got it`**.
   - **Hint (`H`) & Save Question (`S`)**: Press `H` (or click **`Show hint (H)`**) to reveal the hint box; press `S` (or click **`Save question`**) to bookmark the question to your saved bank.
   - **Check Answer & Explanation Accordion**: Press `2` (Option B) and press `Enter` (or click **`Check answer`**). Verify the **`[Xem Lời giải chi tiết / Explanation & Rationale]`** accordion (`#accordion-explanation`) appears and toggles open/closed when clicked.
   - **Sidebar Navigator Filters**: In the right sidebar under **`Questions (1–N)`**, click **`All`**, **`Incorrect`**, and **`Saved`** filter pills to filter the question grid.

---

### Scenario 12 — Multi-Component Practice Drilling (`TOEIC_CAT` `P5_CAT`) (Step 13 / #119)

1. Navigate to `http://localhost:5000/certificates` and click **`Switch to TOEIC_CAT`**.
2. Navigate to `http://localhost:5000/practice`.
3. Under **`Reading`**, click the card **`P5_CAT`** (`Incomplete Sentences (Stage 1 & 2)` — `6 questions available`).
4. Drill through the 6 questions and verify immediate grading works for every component type in Practice mode:
   - **Q1 (`mcq`)**: Select `A` (`before`) → Click **`Check answer`** → **`Accuracy · Correct`**.
   - **Q2 (`mcq_3`)**: Select `A` (`On the conference table`) → Click **`Check answer`** → **`Accuracy · Correct`**.
   - **Q3 (`multi_select`)**: Check `A` and `B` → Click **`Check answer`** → **`Accuracy · Correct`**.
   - **Q4 (`inline_completion`)**: Type `15` → Click **`Check answer`** → **`Accuracy · Correct`** (and verify the **`Đáp án chấp nhận / Accepted answers:`** `["15","fifteen"]` displays in the explanation accordion).
   - **Q5 (`dropdown`)**: Select `A: consequently` → Click **`Check answer`** → **`Accuracy · Correct`**.
   - **Q6 (`drag_drop`)**: Place `Quality Assurance` into the drop zone → Click **`Check answer`** → **`Accuracy · Correct`**.

---

### Scenario 13 — Asynchronous AI Grading Queue & Background Worker Verification (Step 14 / #120)

1. Submit your **`TOEIC Speaking & Writing Mock Test 1`** attempt (`ExamId = 18` from Scenario 10) or start & submit **`ETS TOEIC Writing Practice Test 1`** at `http://localhost:5000/writing/5/instructions`.
2. Watch **Terminal 1 (`WebAPI`)**:
   - Within 2 seconds, `AiGradingBackgroundService` picks up the `pending` row from `IX_AiGradings_Queue`, grades the response against the certificate's `GradingCriteria` (`GRAM`, `ORG`, `PRON`, `FLU`), writes `AiCriterionScores`, and transitions the attempt from `submitted` to `graded`.
3. Refresh the attempt result/review page in the browser and verify the per-criterion AI rubric breakdown and overall scaled score are displayed.
