# Exam Shell Architecture and Zero-Reload Question Swapping

## Status
Accepted

## Context
During simulated mock exams (`/exams/take/{attemptId}`), candidate interaction was previously driven by server-side HTML links (`<a href="?q=N">`) on Previous, Next, and Question Navigator grid cells. This introduced critical defects:
1. **Severe UI Churn**: Every question switch triggered a full HTTP GET request, re-rendering the whole page, flashing the layout, and restarting entrance animations.
2. **Audio Disruption & Exam Invalidation**: In Listening sections (TOEIC Parts 1–4), full-page reloads destroyed and re-instantiated the HTML5 `<audio>` player mid-playback. For question sets sharing the same audio passage (e.g. Parts 3 and 4), transitioning from question 32 to 33 abruptly killed audio playback or violated the single-play rule (`allowReplay = false`).
3. **Timer Drift & Race Conditions**: The countdown timer re-parsed server timestamps on every page hit, and fast clicking after option selection risked aborting in-flight HTTP POST answer saves.
4. **Vulnerable Candidate Surface**: Browser Back/Forward buttons, accidental tab closures (`Ctrl+W`), and reload keystrokes (`F5`) could unexpectedly pull candidates out of active exam attempts or cause uncommitted responses to be dropped.

## Decision
Adopt a persistent **Exam Shell** architecture with client-side in-memory question swapping, an optimistic answer queue, and strict exam integrity enforcement:
1. **Persistent Exam Shell**:
   - The topbar (exam title, countdown timer, connection status, exit trigger), audio player container, and sidebar navigator grid are rendered once and never reloaded during the attempt.
   - Question switching operates entirely in-memory at 0ms latency by swapping the question pane DOM (stem, passage, image, options).
   - URL synchronization is maintained via `history.replaceState` and `popstate` handlers without creating history spam or triggering browser document reloads.
2. **Full Attempt Payload Ingest**:
   - The server delivers all question structures, options, passages, and audio group metadata in the initial page payload via `AttemptTakeDto.Questions` serialized into a secure `<script id="exam-data" type="application/json">` block. Correct answer keys (`CorrectOptionId`) are strictly omitted until the exam is submitted, preserving complete anti-cheat integrity.
3. **Audio Group Continuity**:
   - The audio player persists across question transitions. When adjacent questions belong to the same audio group/file, audio playback continues uninterrupted without resetting or reloading. The player only re-arms when the candidate transitions to a new audio source.
4. **Optimistic Answer Queue & Offline Resilience**:
   - Selecting an option immediately updates the local state and navigator cell styles (0ms visual feedback).
   - Answer changes enter a client-side queue that dispatches asynchronous POST requests to `/exams/take/{attemptId}/answers/{questionId}`.
   - If the network drops, uncommitted answers buffer in `localStorage` with timestamps and auto-retry upon reconnection (`window.online`).
   - Final exam submission is strictly blocked until all pending answers in the queue have successfully synchronized to the server.
5. **Exam Integrity & Non-Happy Path Guards**:
   - **History Trap**: Intercepts browser Back button navigation and opens the Exit Confirmation Modal rather than exiting the exam.
   - **Tab Protection**: Attaches `beforeunload` to warn against accidental window closure or reload while an attempt is `InProgress`.
   - **Mischief Prevention**: Disables right-click context menu, text selection / copy shortcuts (`Ctrl+C`, `Ctrl+U`), developer inspection keys (`F12`, `Ctrl+Shift+I`), and reload shortcuts (`F5`, `Ctrl+R`).
   - **Focus Loss Detection**: Monitors `visibilitychange` / `window.blur` to record candidate tab switching and displays disciplinary warnings.
   - **Server-Calibrated Countdown**: Timer recalibrates against server `RemainingSeconds` returned from answer save responses to counter local system clock tampering. On expiration, interactions lock immediately and trigger auto-submit redirect.

## Consequences
- **Instantaneous Real-Time Experience**: Question navigation is instantaneous (0ms), completely eliminating screen flicker, reload delays, and animation replays.
- **Strict Compliance with Standardized Exam Rules**: Audio playback matches official test protocols (continuous group listening without re-triggering single-listen allowances).
- **Guaranteed Data Integrity**: Rapid answer selections and network interruptions never lose candidate responses.
- **Hardened Exam Environment**: Prevents accidental exits, browser history navigation bugs, and casual cheating attempts.
