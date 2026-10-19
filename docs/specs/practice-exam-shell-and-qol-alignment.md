# Practice Exam Shell & Quality-of-Life Alignment Specification

## Problem Statement

While simulated mock exams (`/exams/take/{attemptId}`) provide a modern, real-time, zero-reload experience built on a two-column **Exam Shell** with interactive question navigation, the platform's diagnostic practice drilling (`/practice/{sessionId}`) suffers from significant architectural fragmentation and poor usability:

1. **Primitive Question Navigation**: The practice interface is constrained to a narrow, single-column container (820px) completely lacking a Question Navigator grid. Learners cannot see their overall session layout, jump between questions, or visually distinguish answered, correct, incorrect, or flagged questions. Navigation is restricted to sequential Previous/Next links.
2. **Jarring Page Reloads & Layout Churn**: Every question transition, answer submission, or skip triggers a full-page HTTP GET/POST reload. This forces the browser to re-parse DOM trees, re-run CSS entrance animations (`placard-wipe`), and reset layout scroll, creating a sluggish, discontinuous drilling experience.
3. **Audio Disruption During Listening Practice**: In listening comprehension drills, full-page reloads destroy and re-instantiate audio player elements. Audio cannot persist across question boundaries, and learners have no controls over playback speed or granular ear training.
4. **Architectural & Component Duplication**: Despite sharing common question formats (multiple-choice, audio passages, reading excerpts, image illustrations), Mock Exam and Practice maintain divergent HTML layouts, scripts, and state machines, making maintenance costly and inconsistent.
5. **Slow Keyboard Drilling Flow**: In high-volume drilling sections (such as TOEIC Part 5 Incomplete Sentences), learners are forced to repeatedly switch between keyboard and mouse to select options, check answers, and advance, slowing study velocity.

## Solution

Harmonize the Practice interface with the **Exam Shell Architecture** established in ADR 0010 and Mock Exam, while tailoring interactions to practice-specific pedagogical needs:

- **Two-Column Practice Exam Shell**: Adopt the standardized `exam-layout` featuring a dedicated question pane on the left and a persistent **Sidebar Navigator Card** on the right, housing an interactive **Questions Grid (1–N)** with status swatches.
- **Zero-Reload In-Memory Swapping (0ms)**: Ingest session question structures into an in-memory client state machine upon page load. Question transitions (via Navigator cells, Next/Previous buttons, or keyboard shortcuts) occur instantaneously (0ms) without page reloads, layout flicker, or animation restarts.
- **Cheating-Resistant Payload Ingest**: For unanswered questions, correct answer keys (`CorrectOptionId`), explanations, and transcripts are strictly withheld from the initial payload until the learner explicitly checks their answer.
- **In-Place Pedagogical Feedback Cycle**: Answering questions triggers an inline AJAX submission (`POST /practice/{sessionId}/questions/{questionId}/answer`). The response immediately reveals correct/incorrect badge styles, option coloration, explanation notes, and transcripts in-place. The "Check answer" action transforms seamlessly into "Next question", and the corresponding Navigator cell updates to Green (Correct) or Red (Incorrect).
- **Relaxed Study Room Policy**: Free learners from formal exam security constraints. No history traps (unrestricted browser Back/Forward), no context-menu or copy locks (text selection allowed for dictionary lookups), no focus-loss penalties (tab switching permitted), and a gentle exit flow preserving in-progress sessions.
- **Practice Quality-of-Life (QoL) Suite**:
  - **Keyboard Power-Drilling**: Full single-key shortcuts (`1–4` for options, `Enter`/`Space` for Check/Next, `H` for Hint, `S` for Save, `K` for Skip, `N`/`P` for Prev/Next, `?` for Controls).
  - **Keyboard Controls Helper Modal**: A compact modal displaying all available shortcuts, accessible via the `?` key or a "Controls" button in the topbar.
  - **Audio Ear-Training Controls**: Full replay permissions (`allowReplay = true`) paired with playback speed toggles (`0.8x`, `1.0x`, `1.2x`).
  - **Navigator Status Filter Tabs**: Quick filter toggles on the sidebar grid (`All`, `Incorrect`, `Saved`) to instantly isolate weak areas.
  - **Real-Time Accuracy Dashboard**: Dynamic live score indicator (`Accuracy % · Correct / Answered`) updating instantly on the sidebar after each question check.
  - **Toast Notifications for Saved Questions**: Subtle notification confirmations when bookmarking questions to `/saved-questions`.
- **Integrated Review Dashboard**: In finished sessions or review mode, the Navigator Grid doubles as an interactive diagnostic map, allowing learners to click any cell to inspect that question's outcome, choice, and explanation.

## User Stories

### Shell Layout & Question Navigation
1. As a practice learner, I want a two-column Exam Shell with a persistent sidebar, so that my drilling environment feels spacious, organized, and consistent with the mock exam.
2. As a practice learner, I want an interactive Questions Grid (1–N) on the sidebar, so that I can see all questions in my session at a glance and jump directly to any question.
3. As a practice learner, I want navigator cells to display real-time status swatches (Correct in Green, Incorrect in Red, Skipped in Gray, Unanswered in Neutral Ink, Current with active border), so that I can track my performance visually.
4. As a practice learner, I want question changes to happen instantaneously (0ms) in-memory, so that I do not experience white-screen flicker or page reloads between questions.
5. As a practice learner, I want entrance animations to be suppressed when switching questions, so that the screen stays stable and clear.
6. As a practice learner, I want the browser URL to update smoothly via `history.replaceState` (e.g. `/practice/{sessionId}?q=5`), so that if I refresh the page I remain on the current question.

### Pedagogical In-Place Feedback
7. As a practice learner, I want to select an option and click "Check answer" to receive immediate correctness and explanation without reloading the page, so that I stay immersed in learning.
8. As a practice learner, I want the correct answer option to highlight in green and an incorrect choice to highlight in red, so that I can immediately spot mistakes.
9. As a practice learner, I want the explanation and audio transcript to reveal smoothly below the question stem after checking my answer, so that I understand the rationale behind the solution.
10. As a practice learner, I want the "Check answer" button to transform into "Next question" once evaluated, so that I can advance quickly with a single click or keystroke.
11. As a practice learner, I want to click "Skip this question" to bypass a difficult question and see its status marked as Skipped in the navigator, so that it does not penalize my current accuracy rate.
12. As a practice learner, I want to click "Show hint" before answering to reveal the hint box in-place, so that I can guide my thinking without giving away the full answer.
13. As a practice learner, I want unanswered questions to withhold correct answer keys and explanations from client-side memory, so that inspecting developer tools does not spoil the diagnostic value of the drill.

### Practice Quality of Life (QoL)
14. As a power learner, I want to use number keys `1`, `2`, `3`, `4` to pick options A, B, C, D, so that I can answer questions without moving my hand to the mouse.
15. As a power learner, I want to press `Enter` or `Space` to check an answer, and press it again to move to the next question, so that I can rapid-fire drill vocabulary and grammar sets.
16. As a learner, I want to press `?` or click "Controls" in the top bar to open a compact Keyboard Shortcuts modal, so that I can quickly reference available keys.
17. As a listening practice learner, I want to replay audio tracks as many times as needed, so that I can practice listening to difficult accents and phrases.
18. As a listening practice learner, I want playback speed toggles (`0.8x`, `1.0x`, `1.2x`), so that I can slow down fast speech or challenge my reflex speed.
19. As a practice learner reviewing my session, I want filter tabs (`All`, `Incorrect`, `Saved`) above the navigator grid, so that I can filter down to only the questions I got wrong.
20. As a practice learner, I want to see a live accuracy score (`% Accuracy`, `Correct / Total Answered`) in the sidebar, so that I have continuous feedback on my mastery during the drill.
21. As a practice learner, I want to toggle "Save question" to bookmark valuable questions, accompanied by a discreet toast notification, so that I know the question was saved to my bank.
22. As a learner practicing vocabulary, I want to freely highlight and copy text from stems and passages, so that I can paste terms into my external dictionary or notes.
23. As a learner studying on the go, I want to click "Exit" in the top bar to return to the catalog while preserving my session in an in-progress state, so that I can pick up where I left off later.

### Session Completion & Review
24. As a practice learner who reaches the final question, I want the primary action button to show "View summary", so that I can finish the session and review aggregate insights.
25. As a practice learner navigating from the summary page back into question review, I want the full navigator grid to open in review mode, so that I can inspect all explanations and choices in the unified shell.

## Implementation Decisions

### 1. Unified Exam Shell Layout & Razor Surface
- **Layout Consistency**: Refactor the practice view model and Razor view to consume the platform's standardized `.exam-layout` CSS structure (2-column panel layout with 2px ink borders, soft rule dividers, and IBM Plex typography).
- **Topbar Synchronization**: Topbar incorporates:
  - Session metadata (Section name, "Practice Mode", "No time limit" badge).
  - Keyboard Controls modal trigger button (`[Controls ?]`).
  - Compact live accuracy pill badge.
  - Unrestricted Exit button returning cleanly to `/practice`.
- **Sidebar Integration**: Sidebar houses:
  - Practice Progress card with live accuracy metric and progress bar.
  - Navigator filter bar (`[All]`, `[Incorrect]`, `[Saved]`).
  - Questions Grid (`.navigator__grid`) with buttons representing each question in the session.
  - Action trigger ("Finish session" / "View summary").

### 2. Full Session Payload Ingest & Zero-Reload State Machine
- **Session Manifest Delivery**: Deliver all questions for the session at page initialization via a serialized `<script id="practice-data" type="application/json">` block.
- **Anti-Cheat Payload Policy**:
  - Stems, categories, passages, audio paths, images, and option lists (ID, label, text) are preloaded.
  - For questions where `isAnswered == false`, `correctOptionId`, `explanation`, and `transcript` are strictly `null`.
- **Client State Model**:
  - `localQuestions`: Array of all question records.
  - `localState`: Map of `questionId` to `{ selectedOptionId, isCorrect, isAnswered, hintUsed, isSaved, explanation, transcript, correctOptionId }`.
  - `currentOrder`: Current question index (1-based).
  - `activeFilter`: Current filter (`all`, `incorrect`, `saved`).
- **In-Memory Swapping**: Transitions update DOM nodes directly (stem, passage, image, audio player, options, hints, and explanation panes) with zero full-page roundtrips and zero entrance animations.
- **Audio Continuity & Group Awareness**: The audio player persists across question transitions if adjacent questions belong to the same audio group (sharing `audioPath`, `audioStartMs`, and `audioEndMs`). When transitioning to a new audio source or slice, re-arm the player using the centralized Study Audio Player template (`window.createExamAudioHtml` / `mode="study"`) from ADR 0011 without duplicating raw HTML strings.

### 3. In-Place Pedagogical Evaluation Workflow
- **Answer Checking**: When clicking "Check answer" (or pressing `Enter`/`Space`), client dispatches an AJAX POST to `/practice/{sessionId}/questions/{questionId}/answer`.
- **DOM Transformation**:
  - Disables radio inputs.
  - Applies `.option--correct` to the correct option and `.option--wrong` to the selected incorrect option.
  - Injects correctness badge into question header (`Accuracy · Correct` or `Incorrect`).
  - Populates and unhides explanation and transcript containers.
  - Transforms primary button text to "Next question" (or "View summary" on the last question).
  - Updates navigator cell class to `.is-correct` or `.is-incorrect`.
  - Recalculates live accuracy statistics.
- **Hint Reveal**: Dispatches AJAX POST to `/practice/{sessionId}/questions/{questionId}/hint`. Injects hint text and marks hint used in local state.
- **Question Bookmarking**: Dispatches AJAX POST to `/saved-questions/{questionId}/toggle`. Toggles button state and displays a temporary toast message.

### 4. Practice Quality of Life (QoL) Enhancements
- **Keyboard Shortcuts Dispatcher**:
  - `1`, `2`, `3`, `4`: Select radio option by index.
  - `Enter` / `Space`: Context-aware trigger (Check Answer if unverified; Next Question if verified).
  - `H`: Reveal hint.
  - `S`: Toggle save question to bank.
  - `K`: Skip question.
  - `N` / `P`: Next / Previous question navigation.
  - `?`: Open/close Keyboard Controls Modal.
- **Keyboard Controls Modal**: Accessible, focus-trapped dialog listing all available shortcuts and mouse gestures with flat ink styling.
- **Audio Ear-Training Widget**:
  - Consumes the unified **Study Audio Player (`mode="study"`)** established in ADR 0011 and Issue #81.
  - Must NOT re-introduce removed robotic disclaimers (`"Seeking disabled"`, `"Plays once only"`, `"Replay permitted"`).
  - Inherits native Study Mode controls: Play/Pause toggle, interactive timeline scrubbing (click/drag), jump buttons (`↺ 5s` and `5s ↻`), speed selector segmented control (`0.8x`, `1.0x`, `1.2x`), and unconstrained replay.
- **Status Filter Engine**:
  - Filtering updates navigator grid cell visibility without resetting current question position.
  - Empty filter states gracefully notify the learner (e.g. "No incorrect questions yet — great job!").
- **Relaxed Integrity Rules**:
  - No `beforeunload` lock when clicking Exit.
  - No right-click or text-selection interception.
  - No window blur or visibility monitoring.

## Testing Decisions

### Test Characteristics
- External behavior verification: Test complete user workflows (navigation, answer evaluation, hint reveal, question saving, audio speed control, and review filtering) rather than private JavaScript variables.
- Verification across service layers, MVC forwarding endpoints, Razor view rendering, and browser script events.

### Modules Tested
1. **PracticeService**:
   - `GetSessionInfoAsync` and session data loading.
   - `SubmitAnswerAsync` validation, scoring, and explanation release.
   - `RevealHintAsync` state tracking.
   - FIFO session retention pruning verification.
2. **PracticeController**:
   - `Take` view model generation with full session payload and navigator metadata.
   - AJAX endpoints for `Answer`, `Hint`, and `ToggleSavedQuestion`.
3. **Practice Razor Views**:
   - Invariant assertion: Container incorporates `exam-layout`, `navigator__grid`, `<button>` cells, and `<script id="practice-data">`.
   - Invariant assertion: Absence of anchor tag full-page navigation links (`<a href="?q=">`).
   - Assertion of QoL controls: Keyboard Controls modal, speed buttons, and filter tabs.

### Prior Art
- `ExamsViewTests`: Verification of Exam Shell invariants and anti-reload button structures.
- `ExamsControllerTests`: Controller action and DTO mapping tests.
- `PracticeServiceTests`: Domain logic and answer scoring tests.

## Out of Scope

- Multi-tenant enterprise proctoring or webcam monitoring for practice sessions.
- Timed practice mode with forced auto-submit (handled strictly by Mock Exams and Placement Tests).
- Structural changes to SQL tables or database migrations (existing EF Core `PracticeSessions` and `PracticeAnswers` tables already store all required relations).

## Further Notes

- By synchronizing Practice to the Exam Shell architecture, the entire platform achieves a cohesive, premium feel while maintaining clean domain boundaries between strict formal examinations and relaxed self-paced study.
- **Strict Dependency & Non-Regression Guard (ADR 0011 / Issue #81)**: The Practice Exam Shell builds directly upon the Study Audio Player component defined in ADR 0011 and Issue #81. Implementation of this spec MUST preserve the `mode="study"` contract, interactive timeline scrubber, ±5s jump buttons, and stripped disclaimer text. It must NOT re-introduce legacy string-concatenated audio HTML or overwrite `_ExamAudioPlayer.cshtml` with pre-ADR-0011 templates.
