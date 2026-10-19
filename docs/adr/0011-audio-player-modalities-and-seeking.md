# Audio Player Modalities, Interactive Seeking, and Exam Integrity

## Status
Accepted

## Context
Audio playback in ExtraEfficiency spans two fundamentally different user contexts with divergent pedagogical and integrity requirements:
1. **Standardized Exam Sittings (Mock Exams & Placement Tests)**:
   - Candidates are evaluated under authentic exam hall conditions (TOEIC, IELTS).
   - Audio must play continuously in real time without pausing or scrubbing. Allowing candidates to pause audio while the countdown timer runs (or halting the timer) introduces gaming exploits, pre-reading advantages, and invalidates test standardization.
   - However, the previous implementation in `exam-audio.js` allowed candidates to toggle pause/resume during timed mock exams, while cluttering the interface with redundant disclaimers (`"Plays once only · Seeking disabled"`, `"Official Audio · Plays once"`).
2. **Formative Practice & Retrospective Review**:
   - In practice sessions (`/practice/take`) and post-attempt reviews (`/exams/review`, `/practice/summary`, `/speaking/review`), learners must dissect acoustic details, re-listen to misunderstood phrases, and scrub to specific dialogue timestamps.
   - However, the previous implementation hardcoded `pointer-events: none` on the progress bar and clamped `audio.currentTime` on all seeking events, making it impossible to scrub or skip backward/forward. Furthermore, practice summary completely lacked audio players.

## Decision
Refactor the audio subsystem into two distinct operational modalities governed by `mode="exam"` and `mode="study"`:

1. **Exam Mode (`mode="exam"`, `allowReplay = false`)**:
   - **Manual Start**: Candidates click a prominent `PLAY AUDIO` button when ready to begin listening to the group's prompt.
   - **Continuous Unpausable Playback**: Once started, playback cannot be paused, rewound, or seeked. The button transitions to a non-interactive `PLAYING` state indicator.
   - **Active Controls**: Only Volume slider and Mute toggle remain interactive.
   - **Single Playback Guarantee**: Reaching track/slice end locks the player into `COMPLETED`. Replay is prohibited.

2. **Study Mode (`mode="study"`, `allowReplay = true`)**:
   - **Play / Pause Toggle**: Candidates freely play, pause, and resume playback.
   - **Interactive Timeline Scrubber**: Clicking or dragging along the progress track updates `audio.currentTime` smoothly, strictly clamped within the audio slice boundaries `[segStart, segEnd]`.
   - **Quick Jump Buttons**: Dedicated `↺ 5s` and `5s ↻` buttons jump playback position by 5 seconds.
   - **Unrestricted Replay**: When playback finishes, the button switches to `REPLAY`, and clicking any point on the scrubber resumes playback from that point.

3. **Placard UI Polish & Copy Cleanup ("Bảng hiệu phòng thi")**:
   - Strip all robotic disclaimers, warnings, and preachy disclaimers (`"Seeking disabled"`, `"Plays once only · No rewinding"`, `"Replay not permitted"`).
   - Adopt a flat, high-density industrial placard: 2px solid ink keyline (`#14181d`), zero rounded corners (`border-radius: 0`), no box shadows, IBM Plex Mono live timers (`00:00 / 02:45`), and flat ink buttons.
   - Consolidate DOM generation between the Razor partial (`_ExamAudioPlayer.cshtml`) and the client-side Exam Shell (`Exams/Take.cshtml`), eliminating brittle string concatenation.

4. **Practice Summary Integration**:
   - Extend `QuestionOutcome` in `BusinessObjects` and `QuestionOutcomeDto` in `Contracts` to include `AudioPath`, `AudioStartMs`, `AudioEndMs`, and `Transcript`.
   - Embed the Study Audio Player directly in listening question cards within `Practice/Summary.cshtml`, enabling instant mistake analysis without leaving the page.

## Consequences
- **True-to-Life Exam Simulation**: Mock exams and placement tests faithfully replicate strict test-center audio protocols without pause vulnerabilities.
- **Pedagogical Freedom**: Practice sessions and review views provide full scrub, replay, and ear-training capabilities.
- **Clean Industrial Visuals**: UI copy is concise and uncluttered, matching the austere signage design system.
