# ExtraEfficiency — Mainflow End-to-End Demo Script & Presentation Guide

> **Target Audience:** Professors, Assessment Committee, Technical Evaluators & Stakeholders  
> **System Architecture:** 6-Layer .NET 10 Solution (`BusinessObjects` ← `DataAccess` ← `Services` ← `Contracts` → `WebAPI` / `WebMVC`)  
> **AI Architecture:** Real Anthropic Claude Sonnet 3.7 via HH-Tech proxy + SpeechSuper Phoneme & Fluency Assessment Engine (`speak.eval.pro`)  
> **Database:** SQL Server LocalDB (`ExtraEfficiency`) with EF Core Database-First architecture & deterministic scoring triggers  

---

## 0. Quick Start & Service Orchestration

### 0.1 Start Services (2 Dedicated Terminals)
Open two PowerShell terminals at the repository root `D:\Study\PRN3\Project\extra_efficiency_ui`:

1. **Terminal 1 — Backend WebAPI (Port 5001, Minimal APIs, JWT Token Issuer, AI Workers):**
   ```powershell
   dotnet run --project src/WebAPI
   ```
   *Logs to look for:*
   - `Claude API configured: https://hhtechapi.net (Model: claude-sonnet-3.7)`
   - `AI Provider active: real`
   - `AiGradingBackgroundService hosted service started`
   - `AiInsightBackgroundService hosted service started`

2. **Terminal 2 — Frontend WebMVC (Port 5000, Server-Rendered ASP.NET Core MVC + Razor):**
   ```powershell
   dotnet run --project src/WebMVC
   ```
   *Target Application URL:* [http://localhost:5000](http://localhost:5000)

---

### 0.2 Real AI Engine Status & Configuration
The system is configured with **live production-grade AI models** in `.env`:
- **Claude Sonnet 3.7 (`https://hhtechapi.net`):**
  - **Asynchronous Free-Response AI Grading:** Evaluates written candidate essays against CEFR rubric criteria (`Task Achievement`, `Coherence & Cohesion`, `Lexical Resource`, `Grammatical Range`).
  - **Pedagogical AI Insights & Study Plans:** Analyzes user category accuracy trends and synthesizes personalized diagnostic reviews.
  - **Generative Section Exam Ingest:** Synthesizes exam questions from scratch adhering to certificate blueprint constraints.
- **SpeechSuper Engine (`https://api.speechsuper.com`, `speak.eval.pro`):**
  - Scores spoken responses across pronunciation, rhythm, fluency, grammar, and acoustic phoneme alignment.
- **Queue Mechanics:** Free responses and AI insights are queued in `dbo.AiGradings` and `dbo.AiInsights` and processed asynchronously by background worker threads without blocking user submissions.

---

### 0.3 Demo Accounts Reference Matrix
All accounts share the uniform password: **`Demo1234!`**

| Account Role | Email Address | Password | Initial State & Purpose |
| :--- | :--- | :--- | :--- |
| **Virgin Learner** | `learner.fresh@demo.local` | `Demo1234!` | **0 Credits, No History.** Demonstrates the complete Day-1 diagnostic funnel: placement test, AI rubric grading, CEFR band allocation, dynamic learning path generation, and wallet paywall. |
| **Active Learner** | `learner@demo.local` | `Demo1234!` | **8 Credits, Existing Attempt History.** Demonstrates returning candidate workflow: weakness analytics charts, high-stakes mock exam sitting, adaptive CAT unit locking, and practice drilling. |
| **System Admin** | `admin@demo.local` | `Demo1234!` | **Full Platform Authority.** Demonstrates 4-step Certificate Blueprint Wizard, live score calculator preview, dynamic exam studio, audio slicing, and quick AI question ingestion. |
| **Mentor** | `mentor.khoa@demo.local` | `Demo1234!` | **Consultant Profile.** Demonstrates 1-on-1 expert mentor bookings and scheduling. |

---

### 0.4 Database Re-Seed Command (Clean Slate Reset)
If you ever want to reset the database to the exact virgin state before a demo, run this single command in PowerShell:
```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -I -b -Q "IF DB_ID('ExtraEfficiency') IS NOT NULL BEGIN ALTER DATABASE [ExtraEfficiency] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [ExtraEfficiency]; END; CREATE DATABASE [ExtraEfficiency];" ; sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -f 65001 -i docs/database-schema.sql ; sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -b -f 65001 -i docs/seed-demo.sql
```

---

## 1. Demo Narrative: 4 High-Impact Acts

```
Act 1: Day-1 Diagnostic Funnel ──► Act 2: Pedagogical Feedback & Practice
   (learner.fresh@demo.local)            (learner@demo.local)
               │                                   │
               ▼                                   ▼
Act 4: Admin Studio & AI Generation ◄── Act 3: Mock Exam Economy & Sitting
      (admin@demo.local)                     (learner@demo.local)
```

---

### Act 1: The Day-1 Diagnostic Funnel & Free Learning Loop
> **Presenter Pitch:** *"In ExtraEfficiency, diagnostic placement tests, weakness analysis, and practice drilling are free forever. We never charge learners to diagnose where they struggle. This builds trust and feeds our AI mentor with authentic diagnostic data."*

1. **Sign In as Virgin Learner:**
   - Go to [http://localhost:5000/auth/signin](http://localhost:5000/auth/signin).
   - Enter `learner.fresh@demo.local` / `Demo1234!`.
   - Landing on `/student/dashboard`. Notice the zero-credit wallet balance and clean onboarding placard.

2. **Certificate Selection & Placement Onboarding:**
   - Certificate picker is defaulted to **`TOEIC Listening & Reading`** (CertId: 1).
   - Click **`Take Free Placement Test`** or navigate to [http://localhost:5000/placement](http://localhost:5000/placement).
   - Point out: **0 Credits required**, completely free diagnostic sitting.

3. **Placement Sitting Room:**
   - Click **`Start Placement Test`** (starts Exam 2: `Diagnostic Benchmark 45M`).
   - Highlight the sitting room features:
     - Clear countdown timer, breadcrumb navigation, and question palette.
     - Auto-save draft triggers every 500ms on free-response typing.
     - Split-screen layout: Reading passage on the left, questions on the right with independent scrolling.
   - Answer 2-3 questions:
     - For the writing free response, type a short candidate paragraph:
       > *"In my opinion, teamwork is essential for modern business success because collaborative environments foster creative problem-solving and shared accountability across departments."*
   - Click **`Submit Placement Test`** in the header. Confirm in modal.

4. **Real AI Grading & Dynamic Path Generation:**
   - The user is redirected to `/placement/result`.
   - The AI background worker (`AiGradingBackgroundService`) immediately picks up the free response and evaluates it live against CEFR criteria via **Claude Sonnet 3.7**.
   - The diagnostic score card renders:
     - **Assigned Result Band:** e.g., `B1 (Independent Intermediate)`.
     - **Detailed Criterion Breakdown:** Task Achievement, Lexical Variety, Grammar Accuracy with examiner notes from Claude.
   - **Target Band Selection:** Select target band **`B2 (Upper Intermediate / 750+)`**.
   - Click **`Generate My Personalized Learning Path`**.
   - Show the dynamic learning path at `/student/learning-path`: The system dynamically orders remedial modules focusing directly on the learner's weakest sub-skills.

---

### Act 2: Pedagogical Feedback, Weakness Analysis & Practice QoL
> **Presenter Pitch:** *"Practice drilling features an anti-cheat exam shell, instant explanatory feedback, and rich audio controls to build learner confidence before high-stakes exams."*

1. **Switch to Active Learner:**
   - Click profile dropdown → **`Sign Out`**.
   - Sign in as `learner@demo.local` / `Demo1234!`.
   - Navigate to `/student/dashboard`. Show active credit balance (**8 Credits**), learning progress, and recent test history.

2. **Deterministic Weakness Analysis:**
   - Click **`Weakness Analysis`** or navigate to `/student/weakness`.
   - Point out the architectural invariant: *No application-level math or guess-work.* These percentages come straight from deterministic SQL views (`vw_UserSectionAccuracyAll`, `vw_UserCategoryAccuracy`).
   - Point to the weakest category (e.g., `P2 Question-Response: 42% accuracy`).
   - Click the inline CTA: **`Drill Weak Category Now`**.

3. **Practice Drilling & Quality-of-Life Tools:**
   - Enters `/practice/1002?q=1`.
   - Point out the Practice QoL tools:
     - **Audio Speed Controller:** Switch between `0.8x`, `1.0x`, `1.2x`.
     - **`[-5s]` Instant Rewind:** Allows scrubbing back 5 seconds without restarting the track.
     - **Pedagogical Feedback Cycle:** Select an option and click **`Check Answer`**.
     - **Interactive Transcript Drawer:** Unfolds the audio transcript with highlighted vocabulary and grammar explanations.
     - **Keyboard Shortcuts:** Press `1`–`4` to select options, `Enter` to verify, `H` to show hint.
     - Click **`Finish Practice Session`** to view the accuracy summary and AI recommendation placard.

---

### Act 3: High-Stakes Mock Exam Sitting & Multi-Cert Mechanics
> **Presenter Pitch:** *"When learners are ready for high-stakes mock exams, our platform enforces ETS/Cambridge-grade sitting rules: hardware pre-flight verification, masked options, clustered audio cards, adaptive stage unit locking, and split-screen stimulus panes."*

1. **Credit Wallet & Economy:**
   - Navigate to `/wallet`.
   - Explain the ledger invariant: *Wallet balance is strictly `SUM(Delta)` over the append-only `CreditTransactions` ledger.* There is no mutable balance column.
   - Highlight the rule: *"Block before you charge."* Mock exams cost exactly 1 credit; free practice costs 0.
   - Show available credit packages (e.g., 10 Credits, 25 Credits).

2. **Mandatory Pre-Flight Onboarding Gate:**
   - Navigate to `/exams` and select **`ETS 2024 — Test 1`** (Exam 1) or click **`Start Exam`**.
   - Routes to `/exams/1/preflight`.
   - Show the mandatory 3-point equipment check:
     - **Audio Output Test:** Click **`Play Audio Check`** and verify sound playback.
     - **Microphone Test:** Speak into microphone; verify live visual VU meter.
     - **Authorization Checklist:** Agree to honor code terms.
     - Notice the button **`Enter Exam Sitting Room`** is unlocked only after completing pre-flight.

3. **Sitting Room Mechanics (3 Scenarios in 1 Sitting):**
   - Click **`Enter Exam Sitting Room`** (enters `/exams/take/{attemptId}`):
   - **Scenario A: Masked Audio Options (Part 1 & Part 2):**
     - Look at Question 1 & 2: Notice options are displayed as masked placards `[A] [B] [C] [D]` without text. The stimulus is audio-driven.
   - **Scenario B: Clustered Stimulus Audio Card (Part 3):**
     - Navigate to Question 3: Notice all 3 sub-questions are rendered within a unified cluster card bound to a single audio stimulus. Notice that masked placards from previous questions do not linger.
   - **Scenario C: 100vh Split-Screen Workspace (Reading / IELTS):**
     - Navigate to Reading / Part 7:
     - Left pane displays the long reading stimulus with dedicated scrolling.
     - Right pane displays questions.
     - **Highlighter & Sticky Notes:** Select text in the passage → Right click or click **`Highlight`** (yellow/gold highlight). Select text → click **`Add Sticky Note`** → notes persist in real-time.
   - **Scenario D: Multi-Stage Adaptive CAT (`TOEIC_CAT` Benchmark Exam 17):**
     - If demonstrating CAT: Unit 1 contains Stage 1 questions (`inline_completion`, `drag_drop`, `dropdown`).
     - Completing Stage 1 prompts the **`Unit Transition Modal`**: *"Once confirmed, Stage 1 will be permanently locked."*
     - In Stage 2: Stage 1 units are permanently disabled and locked (`opacity: 0.35`, non-clickable); Stage 2 questions load based on performance.

4. **Exam Submission & Official Scaled Score Report:**
   - Click **`Submit Exam`** in the header.
   - Modal displays answered questions count and unflagged status.
   - Click **`Confirm & Submit`**.
   - Result screen `/exams/result/{attemptId}` loads:
     - Raw score converted to scaled score via two-step official conversion tables (`dbo.ScoreScales`).
     - Official CEFR Band certificate awarded (e.g., `B2`, `C1`).
     - Real Claude Sonnet 3.7 generates comprehensive AI study recommendations.

---

### Act 4: Admin Authoring Studio, Certificate Wizard & Generative AI
> **Presenter Pitch:** *"Platform administrators have enterprise-grade control: a 4-step Certificate Blueprint Wizard with live preview calculations, dynamic exam studio with audio waveform slicing, and Claude-powered question generation."*

1. **Sign In as Administrator:**
   - Sign out and sign in as `admin@demo.local` / `Demo1234!`.
   - Access admin portal at `/admin/dashboard`.

2. **4-Step Certificate Blueprint Wizard:**
   - Navigate to `/admin/certificates/wizard`.
   - **Step 1 (Preset & Identity):** Click the preset card **`IELTS Academic`**. All 6-axis configurations auto-populate! Change code to `IELTS_PRO`.
   - **Step 2 (Skills & Timing):** Shows 4 skills (Listening 30m, Reading 60m, Writing 60m, Speaking 14m). Add/remove skill dynamically.
   - **Step 3 (Section Blueprints & Sitting Rules):** Configure navigation behaviors (`AUDIO_DRIVEN`, `FULL_SANDBOX`, `SYSTEM_PACED_AUTO`), layouts (`SPLIT_HORIZONTAL`), allowed components, and candidate tools (highlighter, word counter).
   - **Step 4 (Scoring Rules & Live Calculator):** Enter sample raw scores; watch the live preview calculator compute composite scaled scores and CEFR bands dynamically in real time.

3. **Dynamic Exam Studio & Media Ingestion:**
   - Navigate to `/admin/exams/1/studio` (`ETS 2024 — Test 1`).
   - **Curriculum Rail:** Dynamic tree navigation on the left showing Sections, Groups, and Questions.
   - **Visual Audio Waveform Slicer:** Shows the audio track with start/end markers, slice bounds, and playback controls.
   - **Image Upload:** Upload stimulus diagrams with real-time preview.

4. **Real AI Section Generator & Quick Ingest:**
   - In the Studio header, click **`Quick Ingest`** (`#btn-studio-quick-ingest`).
   - Click **`Load Section Sample`**: Pulls the official blueprint JSON schema for that section.
   - Click **`Generate with AI (Claude Sonnet 3.7)`**:
     - System prompts Claude Sonnet 3.7 with section constraints (Option count, stem, distractor rules).
     - Returns syntactically validated questions parsed directly into the studio editor staging area.

5. **Score Conversion Matrix & Admin Controls:**
   - Navigate to `/admin/score-conversion?cert=TOEIC`.
   - Show the editable conversion table mapping raw scores (0–100) to scaled scores (5–495) and CEFR bands (`A1` through `C1`).
   - Navigate to `/admin/users` and `/admin/orders` to demonstrate transaction audits and complaint resolution.

---

## 2. Technical Q&A Cheat Sheet (For Evaluators)

| Question from Evaluators | Architecture Answer | Code Location |
| :--- | :--- | :--- |
| **Why is WebAPI on 5001 and WebMVC on 5000?** | MVC proxies all API requests via an internal HttpClient using an HttpOnly cookie JWT. The browser never touches the WebAPI directly, keeping CORS completely closed and preventing XSS token theft. | `docs/adr/0002-mvc-proxies-api-with-cookie-token.md` |
| **How does AI grading handle traffic spikes?** | Asynchronous Queue Seam. When a learner submits a test, attempt answers are committed immediately and an `AiGradings` queue row is created. Background worker `AiGradingBackgroundService` polls and processes jobs in FIFO batches without holding HTTP requests. | `docs/adr/0008-ai-grading-processor-seam.md`, `src/WebAPI/Program.cs` |
| **How do you prevent money/credit discrepancies?** | Append-only ledger architecture. Wallet balance is always computed as `SUM(Delta)` over `CreditTransactions`. There is no stored wallet column that can get out of sync or suffer race conditions. | `src/Services/Credits/CreditLedger.cs` |
| **How is Certificate Isolation enforced?** | Composite Foreign Keys. Every exam, section, attempt, and learning path carries `CertId` down the relational graph. It is physically impossible for an IELTS question or rubric to leak into a TOEIC attempt. | `docs/database-schema.sql` |
| **Where does the Design System come from?** | "Bảng hiệu phòng thi" (Examination Signage System): strictly 0 border-radius, 2px ink borders (`#14181d`), IBM Plex Mono for timers/scores, IBM Plex Sans for body, drawn SVG icons with text placards, and solid ink inversion for active selection. | `DESIGN.md` |

---

## 3. Verification Checklist Prior to Demo Call

- [x] Database `ExtraEfficiency` freshly loaded with schema & demo seeds.
- [x] WebAPI running on `http://localhost:5001`.
- [x] WebMVC running on `http://localhost:5000`.
- [x] Real Claude Sonnet 3.7 live ping verified (`https://hhtechapi.net`).
- [x] Real SpeechSuper API keys configured in `.env`.
- [x] Full test suite green (1,080 / 1,080 tests passing).
- [x] Sitting room UI 100% normalized in pure English with zero Mojibake.
