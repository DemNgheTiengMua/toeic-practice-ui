# Audio Player Modalities, Interactive Seeking, and UI Polish Specification

## Problem Statement

Learners and examinees face frustrating inconsistencies, unrealistic exam simulation, and missing review capabilities when interacting with listening audio across the platform:

1. **Unrealistic Exam Hall Pausing in Mock Exams**: In formal mock exam sittings and diagnostic placement tests, the audio player inexplicably allows candidates to pause and resume the test recording. In standardized exams (TOEIC, IELTS), listening tracks play continuously in real time. Allowing pause permits examinees to artificially halt playback to read questions ahead or deliberate on options while the clock ticks, breaking standardized test validity.
2. **Missing Scrubbing and Seeking in Practice & Review**: In formative practice drills and post-attempt review screens, learners cannot scrub, seek, or rewind the audio. The progress track is completely non-interactive, and any attempt to jump forward or backward is blocked by anti-seeking guards. Learners who want to re-listen to a difficult sentence or catch an unfamiliar accent are forced to listen from the start or abandon audio review entirely.
3. **No Audio Review on Practice Summary**: When completing a listening practice session, the Practice Summary page provides only a text link ("Read in test ›") rather than embedding the audio track. Learners cannot listen to the audio passage directly next to the question stem and explanation.
4. **Cluttered UI and Robotic Disclaimer Text**: The audio player is wrapped in bulky double-bordered cards and cluttered with verbose, preachy status disclaimers (`"Plays once only · Seeking disabled"`, `"Official Audio · Plays once"`, `"PLAYING · CONTINUOUS"`, `"Replay not permitted"`). These disclaimers look like robotic copy that clutters the interface without providing value.
5. **Architectural Duplication in Client Exam Shell**: The raw HTML markup of the audio player is duplicated between the Razor partial and a 35-line concatenated JavaScript string in the Exam Shell, creating maintenance fragility and risking visual drift.

## Solution

Overhaul the audio playback subsystem into two sharply separated domain modalities—**Exam Mode** and **Study Mode**—adhering strictly to the "Bảng hiệu phòng thi" design language:

- **Exam Mode (`mode="exam"`, `AllowReplay = false`)**:
  - Enforced during timed Mock Exams (`/exams/take/{id}`) and Placement Tests (`/placement/take/{id}`).
  - **Manual Start**: Candidates click a prominent `PLAY AUDIO` button to start listening when they have surveyed the questions.
  - **Continuous Single-Play**: Once started, playback runs continuously to completion. The button transitions to an active, non-interactive `PLAYING` indicator; pausing is impossible.
  - **Locked Seeking**: The progress track displays elapsed time but rejects all scrubbing and seeking inputs.
  - **Strict Lockout**: Upon track or slice completion, the player locks permanently into a disabled `COMPLETED` state. Replay is blocked.
  - **Interactive Volume Only**: Examinees retain full control over Volume and Mute to adjust audio levels to their hardware.
- **Study Mode (`mode="study"`, `AllowReplay = true`)**:
  - Enforced during Practice Sessions (`/practice/take/{id}`), Practice Summaries (`/practice/summary/{id}`), Mock Exam Reviews (`/exams/review/{id}`), and Speaking Reviews (`/speaking/review/{id}`).
  - **Interactive Scrubbing**: A clickable and draggable scrubber track allows learners to jump to any timestamp within the track or slice.
  - **Quick Jump Buttons**: Dedicated `↺ 5s` (rewind 5 seconds) and `5s ↻` (forward 5 seconds) buttons allow granular ear training.
  - **Play / Pause Toggle**: Full freedom to pause, deliberate, and resume.
  - **Unrestricted Replay**: When audio finishes, the button becomes `REPLAY`, and clicking any position on the timeline immediately restarts playback from that timestamp.
  - **Slice Boundary Awareness**: For grouped questions with slice boundaries (`startMs` to `endMs`), seeking, scrubbing, and skip operations are strictly clamped within the slice duration.
- **Embedded Audio in Practice Summary**:
  - Extend `PracticeSummaryDto` and `QuestionOutcomeDto` to deliver question audio paths, slice offsets, and transcripts.
  - Render the Study Audio Player directly inside listening question review cards on the Practice Summary screen.
- **Clean "Bảng hiệu phòng thi" Placard UI**:
  - Completely strip all robotic disclaimers (`"Seeking disabled"`, `"Plays once only"`, `"Replay not permitted"`).
  - Unify the component into a flat industrial placard with 2px ink border (`#14181d`), zero border radius, no box shadows, IBM Plex Mono timers (`00:00 / 02:45`), and high-contrast signage buttons.
  - Eliminate duplicate HTML string concatenation in the client-side Exam Shell by centralizing template generation.

## User Stories

### Exam Mode (Mock Exams & Placement Tests)
1. As an examinee taking a listening section in a mock exam, I want to click "PLAY AUDIO" when I am ready, so that the audio does not begin playing before I have read the question stem.
2. As an examinee taking a mock exam, I want the audio to play continuously from start to finish without a pause button, so that my sitting authentically simulates standardized test hall rules.
3. As an examinee, I want the play button to transition to a non-interactive "PLAYING" state while audio is active, so that I clearly see the player is running.
4. As an examinee in a mock exam, I want the progress bar to show how much audio has elapsed without allowing scrubbing, so that I cannot manipulate playback or jump ahead.
5. As an examinee, I want to adjust volume and toggle mute at any point during playback, so that I can hear the dialogue clearly on my headphones.
6. As an examinee, I want the player to lock permanently into "COMPLETED" once the track or slice finishes, so that I cannot replay the official test audio.
7. As an examinee, I want the exam audio player to be free of patronizing disclaimers like "Seeking disabled" or "Plays once only", so that the interface remains clean and distraction-free.
8. As an examinee moving between questions in the same listening question group, I want the group audio to continue playing across question transitions without resetting or pausing, so that my listening comprehension is uninterrupted.
9. As an examinee taking a diagnostic placement test, I want the same single-play, non-pausable audio enforcement, so that my diagnostic score represents true real-time comprehension.

### Study Mode (Practice Sessions & Reviews)
10. As a practice learner drilling listening questions, I want to play and pause the audio at any moment, so that I can stop to think about what I just heard.
11. As a practice learner, I want to click or drag on the progress track to scrub directly to a specific timestamp, so that I can re-listen to tricky words or phrases.
12. As a practice learner, I want a `↺ 5s` quick-rewind button, so that I can instantly replay the preceding sentence with a single click.
13. As a practice learner, I want a `5s ↻` quick-forward button, so that I can skip past introductory instructions or silence.
14. As a practice learner, I want unlimited replays of the audio prompt, so that I can practice listening repeatedly until I achieve full comprehension.
15. As a practice learner, I want audio slices (`data-start-ms` / `data-end-ms`) to behave as the complete track, so that scrubbing and jump buttons stay bounded within my question group's dialogue.
16. As a practice learner, I want the timer display to show elapsed time and total duration formatted in clean monospace (`00:15 / 01:30`), so that I can track audio position precisely.
17. As a practice learner on a finished practice session summary, I want to see the audio player embedded directly inside the question review card, so that I can review mistakes with audio without leaving the summary page.
18. As a learner reviewing a completed mock exam (`/exams/review/{id}`), I want the review audio players to operate in study mode with full scrubbing and replay, so that I can analyze why I missed listening questions.
19. As a learner reviewing a speaking practice attempt (`/speaking/review/{id}`), I want to scrub through my own recorded response, so that I can listen to specific phonemes highlighted by the AI evaluation.

### Visual Polish & Component Architecture
20. As a learner across all devices, I want the audio player to be styled with flat 2px ink keylines, zero rounded corners, and no box shadows, so that it matches the platform's "Bảng hiệu phòng thi" design language.
21. As a learner, I want volume controls to be compact, responsive, and accessible via keyboard and mouse, so that adjusting sound level is effortless.
22. As a developer maintaining the codebase, I want a single unified JavaScript controller driving both exam and study modes, so that fixes apply uniformly across the application.
23. As a developer, I want to eliminate hardcoded HTML string concatenation in `Exams/Take.cshtml`, so that the Exam Shell shares authoritative DOM templates with the Razor partial.

## Implementation Decisions

### Architectural Seams & Modalities
1. **Two Core Operating Modalities**:
   - `mode="exam"`: Enforces strict single-play, manual trigger via "PLAY AUDIO", no pause button, disabled seeking, locked completion state.
   - `mode="study"`: Enforces full interactive control, Play/Pause toggle, draggable timeline scrubber, `↺ 5s` and `5s ↻` buttons, unlimited replay.
2. **Component Contract (`ExamAudioPlayerModel`)**:
   - Razor partial `_ExamAudioPlayer.cshtml` accepts an operational mode (`Mode = "exam" | "study"`), falling back to `AllowReplay ? "study" : "exam"` for backwards compatibility.
   - Emits clean data attributes: `data-exam-audio`, `data-mode="exam|study"`, `data-start-ms`, `data-end-ms`, `data-allow-replay`.
3. **Placard UI Layout ("Bảng hiệu phòng thi")**:
   - Outer element: `.exam-audio` with flat 2px solid ink border (`#14181d`), white surface, zero border radius, zero box shadow.
   - Primary Action Area:
     - Exam Mode: Single button starting as `PLAY AUDIO` (`.btn--primary`). Once active, switches to disabled/inert `PLAYING` with active equalizer indicator. Once finished, switches to disabled `COMPLETED`.
     - Study Mode: Play/Pause button (`PLAY` / `PAUSE` / `REPLAY`), accompanied by compact `↺ 5s` and `5s ↻` jump buttons.
   - Timeline Area:
     - Progress bar (`.exam-audio__progress`): In Study Mode, interactive (`cursor: pointer`, handles click and drag `pointerdown`/`pointermove`/`pointerup`). In Exam Mode, non-interactive track (`pointer-events: none`).
     - Timer: Monospace live display (`.exam-audio__timer`) using `IBM Plex Mono` displaying `00:00 / 00:00`.
   - Volume Area:
     - Mute toggle button with SVG speaker/muted icons.
     - Range slider (`.exam-audio__slider`) with 2px ink thumb and flat track.
   - All disclaimers (`"Seeking disabled"`, `"Official Audio · Plays once"`, `"PLAYING · CONTINUOUS"`) are deleted.
4. **Practice Summary Integration**:
   - `BusinessObjects.QuestionOutcome` and `Contracts.QuestionOutcomeDto` records are extended with `string? AudioPath = null`, `int? AudioStartMs = null`, `int? AudioEndMs = null`, and `string? Transcript = null`.
   - `PracticeService.GetSummaryAsync` eager-loads `q.Group` to populate audio paths and slice bounds into question outcomes.
   - `WebAPI.Endpoints.PracticeEndpoints` maps these fields to `QuestionOutcomeDto`.
   - `WebMVC.Views.Practice.Summary.cshtml` renders `_ExamAudioPlayer` in study mode for any question with an `AudioPath`.
5. **Client Controller (`exam-audio.js`) Refactoring**:
   - Unifies event handling for both modes.
   - Accurately translates click/drag coordinates along the timeline to `audio.currentTime` within `[segStart, segEnd]`.
   - Clamps skip buttons (`±5s`) within `[segStart, segEnd]`.
   - Exports a clean helper `window.createExamAudioHtml(options)` to eliminate string duplication in `Exams/Take.cshtml`.

## Testing Decisions

### What Makes a Good Test
- Tests must verify observable behavior at the highest possible architectural seams rather than internal implementation minutiae.
- Server-side tests must verify that domain and controller layers correctly deliver audio metadata without breaking backwards compatibility.
- Client tests must verify state transitions (e.g. Exam Mode cannot pause once started; Study Mode updates playback position on seek).

### Seams to Test
1. **Service Layer Seam (`tests/Services.Tests/PracticeServiceTests.cs`)**:
   - Verify that `PracticeService.GetSummaryAsync` populates `AudioPath`, `AudioStartMs`, `AudioEndMs`, and `Transcript` for listening questions.
2. **WebMVC Controller Seam (`tests/WebMVC.Tests/PracticeControllerTests.cs`)**:
   - Verify that `PracticeController.Summary` successfully maps and binds listening question outcomes with audio data into the view model.
3. **End-to-End Client Seam (`tests/WebMVC.E2ETests`)**:
   - Verify that in Exam Mode, clicking "PLAY AUDIO" transitions the player to playing without displaying a pause button.
   - Verify that in Study Mode, clicking the progress bar or skip buttons shifts `currentTime` and updates the timer readout.

### Prior Art
- `PracticeControllerTests.cs` and `ExamsControllerTests.cs` for MVC action and model binding verification.
- `PracticeServiceTests.cs` for SQLite in-memory integration testing of question outcomes.
- `Playwright E2E` specs in `tests/WebMVC.E2ETests/` for sitting state and shell interactions.

## Out of Scope
- Acoustic DSP / noise cancellation filters on the server.
- Visual waveform audio slicing authoring in Admin Exam Studio (already handled separately in `ExamStudio.cshtml`).
- Real-time SpeechSuper audio evaluations (mocked in tests via fake providers).

## Further Notes
- Adheres strictly to **ADR 0010** (Exam Shell Architecture) and the newly created **ADR 0011** (Audio Player Modalities and Seeking).
- Conforms to the industrial signage styling principles in `DESIGN.md` ("Bảng hiệu phòng thi").
