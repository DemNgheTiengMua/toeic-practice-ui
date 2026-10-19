# Exam Studio Audio Slicing & Playback Polish Specification

## Problem Statement

In the Admin Exam Studio (`/admin/exams/{id}/studio`), authors creating and editing listening comprehension sections encounter two disruptive usability defects:

1. When an author listens to master section audio and clicks "Mark Start", the studio immediately dispatches an auto-save request to the server. Because the end time has not yet been marked, the server rejects the partial slice with `HTTP 400 Bad Request` ("Both AudioStartMs and AudioEndMs must be specified together"), flashing a jarring red `Save failed: HTTP 400` banner in the studio header. Slicing only succeeds if the author subsequently clicks "Mark End", creating a broken and alarming authoring experience.
2. When playing audio in Exam Studio (either the section master slicer track or question group audio previews), clicking another question card to edit or selecting another section in the curriculum rail does not pause the playing audio. Multiple audio tracks continue playing simultaneously over each other, creating cognitive overload and chaotic playback.

## Solution

1. **Two-Stage Client Slicing with Guided Feedback**:
   - Clicking **Mark Start** captures the start position in client memory, updates the input field, renders the staged start point on the slicer track, and displays an inline status placard (*"Start marked: MM:SS — click Mark End to save"*). It does not dispatch a premature network request.
   - Clicking **Mark End** validates that a start point exists and that the end timestamp is strictly after the start timestamp (`endMs > startMs >= 0`). Upon validation, it dispatches an atomic save request for both offsets, updating the status to "Saved".
   - If **Mark End** is clicked before Start is set or when the current position is earlier than Start, the studio displays an inline error placard (*"End time must be after Start time (MM:SS)"*) without sending a request or throwing generic error toasts.
   - Clicking **Clear** resets both offsets to null and persists the cleared slice cleanly to the database.

2. **Global Audio Playback Exclusivity**:
   - A centralized audio manager ensures that only one audio track can play at any time across the studio.
   - Starting playback on any audio element automatically pauses all other active audio elements on the page.
   - Clicking any question card in the workspace, selecting any question in the curriculum rail, or switching sections immediately pauses all playing audio.

## User Stories

1. As an exam author, I want to click "Mark Start" while listening to master section audio without triggering an HTTP 400 error, so that I know my start marker was captured smoothly.
2. As an exam author, I want to see an inline placard indicating that the start position has been marked and prompting me to mark the end position, so that I have clear guidance on next steps.
3. As an exam author, I want the visual slicer track to show my staged start marker immediately upon clicking "Mark Start", so that I can visually verify where my slice will begin.
4. As an exam author, I want the topbar save status to remain calm when marking start, so that I am not alarmed by false error warnings.
5. As an exam author, I want clicking "Mark End" to validate that the end timestamp is strictly after the start timestamp, so that corrupt or inverted time slices are prevented before reaching the database.
6. As an exam author, I want clicking "Mark End" with a valid time range to immediately save both start and end offsets to the server, so that my slice is persisted safely.
7. As an exam author, I want to see an inline confirmation when both bounds are successfully saved, so that I have confidence the slice is committed.
8. As an exam author, I want to see an inline error placard if I click "Mark End" before setting a start position, so that I understand why the end cannot be registered.
9. As an exam author, I want to see an inline error placard if the playback position is earlier than or equal to the start position when clicking "Mark End", so that I know to advance the audio before marking the end.
10. As an exam author, I want to click "Clear" to wipe both slice markers and save the cleared state to the server, so that I can easily reset an audio group to play full audio or group-specific audio.
11. As an exam author, I want any playing audio track to pause immediately when I click another question in the curriculum rail or workspace, so that I can focus on editing without competing background noise.
12. As an exam author, I want any playing audio track to pause immediately when I switch sections in the curriculum rail, so that audio from the previous section does not bleed into the new section.
13. As an exam author, I want starting playback on any audio element to automatically pause any other currently playing audio element across the studio, so that two tracks never play over each other.
14. As an exam author, I want play/pause button labels and icons across all audio elements to update accurately when another element preempts their playback, so that UI controls always reflect true playback state.
15. As an exam author, I want "Preview Slice" to play only the marked segment and pause at the end marker, so that I can audit the candidate's exact listening experience before saving or publishing.

## Implementation Decisions

- **State Staging Pattern**: The audio slicer client logic will manage slice coordinates in memory before committing to the server. The start timestamp will be stored in the input element and staged in UI state without triggering the single-group auto-save endpoint.
- **Atomic Slice Persistence**: Persistence to the server will occur atomically only when both start and end timestamps satisfy the invariant `endMs > startMs >= 0`, or when clearing both to `null`.
- **Non-Intrusive Validation Placards**: Validation failures (such as attempting to mark an end before a start or choosing an end earlier than start) will render as flat, accessible inline placards inside the slicer card rather than blocking modal popups or raw browser alerts.
- **Global Studio Audio Registry**: A lightweight studio-level audio coordinator will listen to `play` events globally via event delegation and pause all sibling media elements.
- **Navigation Hooks**: Section switching (`selectSection`) and question focusing (`focusQuestionCard`, rail item clicks) will invoke the audio coordinator to pause all active playback.
- **Schema Invariant Preservation**: The database check constraint `CK_QGroups_AudioSlice` (`(AudioStartMs IS NULL AND AudioEndMs IS NULL) OR (AudioStartMs >= 0 AND AudioEndMs > AudioStartMs)`) and backend domain validation in `AdminExamService` remain the strict source of truth and will not be weakened.

## Testing Decisions

- **Good Test Criteria**: Tests must verify observable user interactions and external contracts (DOM state changes, network requests, audio playback state) rather than private internal implementation details.
- **Primary Testing Seam**: End-to-end browser tests (`WebMVC.E2ETests`) using Playwright against live running services.
  - *Test Case 1*: In Exam Studio listening section, play master audio, click "Mark Start". Verify no HTTP 400 network request is dispatched and no `Save failed` toast appears. Verify staged visual indicator.
  - *Test Case 2*: Advance audio playback, click "Mark End". Verify successful HTTP 200 PUT request and `Saved` status indicator.
  - *Test Case 3*: Click "Mark End" with invalid bounds. Verify inline error placard appears and no network PUT request occurs.
  - *Test Case 4*: Start master audio playback, click another question card in the rail. Verify audio playback pauses automatically.
- **Prior Art**:
  - `WebMVC.E2ETests.Journeys.AdminExamStudioTests`
  - `WebMVC.E2ETests.Journeys.ExamAudioAndInstructionsTests`
  - `WebMVC.Tests.AdminExamsControllerTests`

## Out of Scope

- Waveform scrubbing / draggable handles (this spec maintains the existing time-based "Mark Start" / "Mark End" button workflow).
- Slicing audio for speaking practice responses (speaking practice uses isolated, per-question recorded audio).
- Backend database schema migrations or changes to `CK_QGroups_AudioSlice`.

## Further Notes

- Existing question group slicing data is preserved and backward compatible.
