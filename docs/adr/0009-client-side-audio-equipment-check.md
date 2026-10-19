# Client-Side Audio Equipment Check and Device Routing

## Status
Accepted

## Context
Mock exams, speaking practice attempts, and listening drills depend on working hardware (headphones/speakers for listening comprehension; microphone for AI speech evaluation). Previously:
1. Microphone checks were fragmented into an inline script in `Speaking/Instructions.cshtml` without volume measurement or input device selection.
2. Full mock exams (`Exams/Instructions.cshtml`) had only a static text reminder, without any audio output tester or device routing.
3. Profile settings (`/profile`) had no audio configuration, forcing learners to discover hardware issues only after starting a timed, credit-deducted sitting.
4. Modern browser privacy policies salt `MediaDeviceInfo.deviceId` per origin and per browser profile; saving device ID strings to the server database would lead to broken audio routing across multiple machines or different browsers.

## Decision
Implement a unified, client-side audio management architecture centered on `ee-audio.js` and a reusable `_AudioEquipmentCheck.cshtml` component:
- **Client-Side Preference Persistence**: Store selected `audioinput` and `audiooutput` device IDs and volume preferences strictly in browser `localStorage` (`ee_audio_device_preferences`). Never store browser-specific device IDs in the server database.
- **Centralized Dispatcher (`ee-audio.js`)**: Encapsulate device enumeration, permission probing, stream acquisition, output routing (`setSinkId`), and volume persistence. Automatically attach output sink IDs to all exam players (`_ExamAudioPlayer`) and pass device ID constraints to speaking recorders.
- **Real-Time VU Visualizer & Echo Check**: Provide an interactive microphone tester using Web Audio API `AudioContext` and `AnalyserNode` that displays a real-time RMS visualizer bar with threshold feedback (`[NO SIGNAL]`, `[VOICE DETECTED]`, `[TOO LOUD]`), alongside a 3-second echo test loopback.
- **Speaker Playback Verification**: Provide a standardized spoken calibration audio check with Play/Stop toggle, volume slider, and a Web Audio synthesized chime fallback. Gracefully adapt for browsers lacking `setSinkId` (Safari, older Firefox) by routing to system default.
- **Consistent Surface Points**:
  - Embedded as an "Audio & Equipment" card on `Views/Profile/Index.cshtml`.
  - Conditioned check-in section on pre-exam instruction screens (`Exams/Instructions.cshtml` and `Speaking/Instructions.cshtml`) when the target exam includes `listening` or `speaking` skills.
  - Emergency audio drawer trigger on timed sitting top bars (`Exams/Take.cshtml`, `Speaking/Take.cshtml`, `Practice/Take.cshtml`) to reselect devices mid-attempt without losing state or leaving the page.
- **Non-Blocking Advisory Policy**: Check-in verifies hardware readiness but remains advisory: learners can acknowledge and proceed without a hard block if browser media policies restrict enumeration.

## Consequences
- **Zero Privacy Leakage**: Browser device IDs remain local to the learner's device; no database schema migrations or invalid cross-device sync issues.
- **Consistent Audio Output**: All listening audio players and speaking playback previews route through the learner's chosen headphones rather than system speakers.
- **Early Hardware Verification**: Learners can test their equipment on `/profile` or before spending credits on a mock exam.
- **Mid-Exam Resilience**: Disconnected Bluetooth headsets can be swapped immediately from the sitting top bar without reloading or triggering exam invalidation.
