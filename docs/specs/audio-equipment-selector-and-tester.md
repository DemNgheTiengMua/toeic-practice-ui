# Audio Equipment Selector & Tester Specification

## Problem Statement

In a multi-certificate English exam preparation platform (TOEIC, IELTS, VSTEP), learners sit timed mock exams, AI-graded speaking practice attempts, and listening drills that critically depend on functioning audio peripherals:
1. **Silent Failures and Lost Credits**: Mock exams deduct credits upon starting. If a learner enters an exam only to discover their headphones are muted or routed to the wrong audio device, the server-side countdown timer continues ticking, causing panic, lost exam time, and forfeited credits.
2. **Uncalibrated Microphone Input**: Speaking practice free responses are graded by AI engines (SpeechSuper and Azure Speech). When a microphone is muted, poorly positioned, or set to excessive gain causing clipping distortion, the recording fails grading. The previous speaking instructions offered only an uncalibrated 5-second test snippet without any input level indicator or device selector.
3. **Absence of Central Configuration**: The learner profile page lacks audio settings. Learners must wait until they are about to take a test to verify their peripherals.
4. **Mid-Exam Disconnections**: Bluetooth headsets and external USB microphones frequently disconnect or enter power-saving sleep mid-exam. Without an in-attempt switcher, learners are forced to refresh the browser, risking state loss or timing penalties.
5. **Browser Privacy Constraints**: Modern browsers isolate `MediaDeviceInfo.deviceId` using unique origin-salted UUIDs that differ across physical computers and browser profiles. Storing hardware IDs on the central database would cause broken audio routing across devices.

## Solution

A unified **Audio Equipment Check** capability:
- A centralized client-side audio dispatcher that discovers available input and output devices and persists preferences (chosen microphone, chosen speaker/headphones, and preferred volume) in client-side storage.
- A standardized, reusable audio equipment checker widget conforming to the platform's "Bảng hiệu phòng thi" design system.
- An interactive microphone tester providing a real-time RMS decibel VU-meter visualizer bar, clear status placards (`[NO SIGNAL]`, `[VOICE DETECTED]`, `[CLIPPING]`), explicit hardware stream privacy lifecycle teardown, and a 3-second echo test (loopback).
- An interactive speaker tester offering spoken calibration audio playback, fallback synthesized Web Audio chime tones, volume controls, and speaker routing via `setSinkId` (with graceful fallback for Safari/Firefox).
- Section-aware conditional check-in cards on exam and practice instruction screens containing listening or speaking sections.
- An emergency audio drawer trigger in the exam sitting top bar to switch devices mid-attempt without navigating away or losing sitting progress.

## User Stories

### Profile & Default Settings
1. As a learner, I want an "Audio & Equipment" card under my profile settings, so that I can configure and verify my headset and microphone before sitting any exams.
2. As a learner, I want to select my preferred microphone from a dropdown of connected input devices, so that the platform records from my dedicated headset rather than a muffled laptop mic.
3. As a learner, I want to select my preferred audio output device from a dropdown of connected speakers/headphones, so that exam listening audio routes to my headphones.
4. As a learner, I want my selected input and output device choices and volume level to be saved in my browser, so that I do not have to reconfigure them every time I start a practice session or exam.
5. As a learner using a browser without `setSinkId` support (e.g. Safari), I want to see a clear placard indicating that audio output routes through the system default, so that I understand why the speaker dropdown is inactive.

### Microphone Testing & Calibration
6. As a learner, I want to see a real-time VU visualizer bar that responds dynamically to my voice when testing my microphone, so that I can see whether sound is being detected.
7. As a learner, I want the visualizer to display color-coded status placards (`NO SIGNAL`, `VOICE DETECTED`, `TOO LOUD`), so that I know whether my speech volume is in the optimal range for AI grading.
8. As a learner, I want microphone hardware capture to activate only when I explicitly start a check and to shut down immediately when I finish, so that my battery and privacy are preserved.
9. As a learner, I want to click an "Echo Test" button to record 3 seconds of my voice and hear it played back immediately, so that I can verify my vocal clarity and check for background buzzing or echo.
10. As a learner whose microphone permission was blocked, I want to see a helpful warning placard with instructions on enabling browser microphone access, so that I can resolve permissions quickly.

### Speaker Testing & Calibration
11. As a learner, I want to click a "Play sound" button to listen to a standardized spoken calibration audio clip, so that I can verify my headphones are working and clear.
12. As a learner on a restricted or slow network connection, I want the speaker test to fall back to a synthesized Web Audio chime tone if static media cannot be downloaded, so that my hardware check never hangs or fails.
13. As a learner, I want a volume slider and mute toggle directly on the equipment check card, so that I can set an audible, comfortable listening level before entering a timed sitting.

### Pre-Exam & Pre-Practice Check-in
14. As a learner starting a mock exam that includes a listening or speaking section, I want to see an Audio Equipment Check section on the instructions page, so that I can ensure my audio works before spending credits.
15. As a learner starting a reading-only or writing-only mock exam, I want the Audio Equipment Check section to be omitted automatically, so that I do not experience unnecessary pre-exam friction.
16. As a learner starting speaking practice, I want to verify my microphone on the instructions page before beginning, so that my recorded free responses will be audible for AI grading.
17. As a learner starting listening practice, I want to verify my audio playback before beginning, so that I can hear question prompts without issues.
18. As a learner preparing to begin an exam, I want the audio check to be an advisory confirmation rather than a hard blocker, so that I can proceed even if I am using an external audio setup that browser device enumeration cannot inspect.

### Mid-Attempt Emergency Switching
19. As an examinee whose Bluetooth headphones disconnect during a timed exam, I want an "Audio" button in the top bar to open an emergency switcher modal, so that I can select another audio output without leaving the sitting or pausing the clock.
20. As a speaking practice examinee whose microphone fails mid-attempt, I want to swap input devices from the sitting top bar, so that subsequent questions record properly without discarding my previous saved answers.

## Implementation Decisions

### Modules & UI Surfaces
- **Client Audio Dispatcher**: A standalone client script module that encapsulates device enumeration (`navigator.mediaDevices.enumerateDevices`), preference storage, stream acquisition (`getUserMedia`), speaker routing (`setSinkId`), and fallback Web Audio tone generation.
- **Audio Equipment Check Razor Partial**: A unified, reusable component rendered across profile settings and pre-exam instruction views.
- **Emergency In-Exam Audio Switcher Modal**: A lightweight modal triggered from the sitting top bar on exam take screens.
- **Exam Audio Player Integration**: The exam audio player automatically queries the client audio dispatcher to apply the learner's preferred output sink and volume on initialization.
- **Speaking Recorder Integration**: The speaking practice take engine queries the client audio dispatcher to supply the exact `deviceId` constraint when initiating `MediaRecorder` capture.

### Design System Compliance ("Bảng hiệu phòng thi")
- All visual components must strictly adhere to the signage palette:
  - 2px ink keyline border (`#14181d`), zero border-radius (`border-radius: 0`), zero box shadows (`box-shadow: none`).
  - Placard badges for states: `[READY]`, `[LISTENING]`, `[VOICE DETECTED]` (`Sign Green`), `[NO SIGNAL]` (muted ink), `[TOO LOUD]` (`Sign Red`), `[TESTING]` (`Sign Gold`).
  - Visualizer bar styled as a flat painted gauge with an inner level strip and threshold ticks.
  - Typography: `IBM Plex Mono` for status readouts and numerical levels; `IBM Plex Sans Condensed` for uppercase placards and labels.

### Client-Side State & Persistence
- Preferences are stored in browser `localStorage` under the key `ee_audio_device_preferences`:
  ```json
  {
    "inputDeviceId": "default",
    "outputDeviceId": "default",
    "volume": 0.85,
    "muted": false,
    "lastTestedAt": "2026-10-19T13:00:00Z"
  }
  ```
- Device IDs are never sent to or stored in the backend SQL Server database, respecting origin-scoped privacy isolation (ADR 0009).

### Pre-Exam Section-Aware Heuristics
- The check-in component inspects the exam's section metadata:
  - If any section has `Skill == "listening"`: Display audio output selector, volume slider, and playback test.
  - If any section has `Skill == "speaking"`: Display microphone selector, VU visualizer, and echo test.
  - If both skills are present: Display both input and output panels.
  - If neither skill is present (e.g. Reading or Writing only): Omit the equipment check card entirely.

## Testing Decisions

### What Makes a Good Test
Tests must verify externally observable behavior and contract fulfillment rather than private implementation details:
- Assert that learners can select devices, trigger sound playback, see responsive VU feedback, and have choices remembered.
- Assert that pre-exam screens correctly surface or suppress the equipment check based on exam skill modalities.
- Assert that an examinee can open the emergency audio switcher mid-exam and reselect devices without losing sitting state.

### Testing Seams
1. **Primary Highest Seam: Playwright E2E Browser Journey (`tests/WebMVC.E2ETests/Journeys/`)**
   - Launched with Chromium arguments:
     - `--use-fake-ui-for-media-stream`
     - `--use-fake-device-for-media-stream`
   - Scenarios:
     - Profile Journey: Navigate to `/profile`, verify "Audio & Equipment" card presence, select fake microphone and speaker, adjust volume, run playback test, verify localStorage persistence.
     - Mock Exam Pre-Check Journey: Navigate to an exam with listening sections, verify audio check presence, test playback, confirm readiness, enter sitting, and verify audio player inherits the selected output.
     - Section Modality Suppression Journey: Navigate to a reading-only exam, assert the audio equipment check is omitted from instructions.
     - Mid-Sitting Emergency Switcher: Start an attempt, click topbar "Audio" button, verify modal appears, switch device, close modal, and confirm timer and answers remain intact.
2. **Secondary Seam: WebMVC Razor View & Controller Tests (`tests/WebMVC.Tests/`)**
   - Unit tests on `ExamsControllerTests`, `SpeakingControllerTests`, and `ProfileControllerTests` asserting that view models pass the correct skill flags and section data to determine equipment check visibility.

### Prior Art
- `tests/WebMVC.E2ETests/Journeys/LearnerDashboardTests.cs` (Playwright learner navigation and card presence).
- `tests/WebMVC.Tests/ExamsViewTests.cs` (Razor view rendering assertions).
- `tests/Services.Tests/SpeakingPracticeServiceTests.cs` (Free-response attempt workflows).

## Out of Scope
- Storing hardware `deviceId` tokens in the server database or synchronizing device IDs across different browsers/machines.
- Server-side acoustic digital signal processing (noise cancelling, echo cancellation DSP) beyond standard Web Audio browser APIs.
- Webcam or video proctoring equipment checks.

## Further Notes
- Implements requirements established in `GLOSSARY.md` under **Audio Equipment Check**.
- Conforms to **ADR 0009: Client-Side Audio Equipment Check and Device Routing** (`docs/adr/0009-client-side-audio-equipment-check.md`).
