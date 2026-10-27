# Sitting Room Navigation, Tooling & Practice QoL Defect Remediation

## Problem Statement

Candidates and learners cannot complete or meaningfully exercise several of the exam-sitting and practice flows that `docs/manual-test-plan.md` documents as working. The defects were reproduced against a freshly seeded database (`scripts/seed-manual-test.sh fresh`, payment=mock, ai=fake) with Playwright driving the real browser. They cluster into five user-visible failures:

1. **Exam sittings dead-end at the end of the first skill.** In `ETS 2024 - Test 1` (exam 1, 9 questions) the last Listening item (Q4) replaces `Next question` with a permanently disabled `Next phase starts when time expires`, and every Reading chip (Q5–Q9) is `disabled` with a `· Locked` title. The candidate must idle out the full 45-minute Listening clock; typing `?q=5` in the URL bounces back to `?q=1`. The same construction makes `Cambridge IELTS 18 — Academic Test 1` (exam 6) unwalkable: it has exactly one Listening question, so Reading Q22 can never be reached.

2. **Speaking → Writing transition silently fails and can burn the attempt.** In `TOEIC Speaking & Writing Mock Test 1` (exam 18) the Speaking verification screen's `Continue to Next Phase →` fires `POST /exams/take/{id}/transition/skill`. If that call returns 400 the button does nothing — no error, no message, overlay stays up — and Questions 2 and 3 stay unreachable. When the endpoint *does* succeed the client still snaps back to Question 1 within about a second, because the wall-clock phase model overwrites the server's `ActiveSkillId`.

3. **The tokenized highlighter and sticky notes are unreachable wherever a section has no passage.** Question stems render with class `ee-tokenized-text` but are written via `textContent`, so they contain zero `.ee-token` children. Selecting text therefore produces no floating toolbar. In `REC` / `L3` style listening sections (no passage at all) nothing on the page is tokenized, so the entire tool set documented in Scenario 9 cannot be invoked. Where a passage *does* exist (IELTS `PAS` Q22) tokenization, highlighting, note pins and `CandidateNotesJson` persistence all work correctly.

4. **Practice Mode quality-of-life controls are dead or clipped.** `Show hint (H)` hides itself and lights a `HINT USED` badge without ever revealing hint text, even though every question has a populated `Hint` column. `[Xem Lời thoại / Transcript]` is `display:none` on listening questions that do have audio. The sidebar filter pills overflow their card and clip `SAVED (n)` past the right viewport edge. An orphaned `[-5S]` button floats under the audio card, duplicating controls that already exist in the player bar. Listening stimulus copy still says `Questions 7 through 9 refer to the following conversation` inside a 4-question practice session.

5. **The question palette gives no feedback for locked chips.** Chips that cannot be entered are disabled with no accessible explanation of *why* (title text only appears on hover for some of them), so the palette reads as broken rather than gated. On `ETS 2024 - Test 1` chips 1 and 2 are additionally disabled while the candidate sits on Q3/Q4 of the same audio cluster, contradicting the "free navigation within a cluster" expectation.

## Solution

Make the sitting room's phase state authoritative from the server and navigable from the client, repair the tokenization pipeline so every `ee-tokenized-text` container is genuinely tokenized, and finish the Practice Mode QoL controls so each affordance either works or is not shown.

- **Server-authoritative phase transitions.** `GET /attempts/{id}` already returns `activeSkillId`, `activeUnitNumber`, `phaseDeadlineAt` and per-question `isLocked`. The client must treat those as the source of truth and stop deriving the active phase from elapsed wall-clock time.
- **Explicit phase advance.** When a candidate reaches the last question of a skill, the primary action becomes an enabled `Finish <skill> & start <next skill>` control that calls the existing transition endpoint; the disabled `Next phase starts when time expires` state is removed. Timer expiry must only be a fallback, not the only path.
- **Verification-screen transition must be observable.** A failed `transition/skill` surfaces a blocking error with a retry action; a successful one re-renders the target question and hides the overlay, with no client-side phase recalculation able to undo it.
- **Tokenization is a rendering contract.** Every container carrying `ee-tokenized-text` is populated through the tokenizer — stems, cluster stems and the split passage alike — so `annotations.js` never encounters a container with class but no tokens.
- **Practice QoL controls are wired to real data.** Hint text comes from the question's `Hint`, transcript visibility follows whether the question actually has a transcript, and the audio toolbar owns its rewind control rather than the page body.

## User Stories

1. As a candidate sitting a multi-skill mock exam, I want to advance from Listening to Reading when I finish the last Listening question, so that I am not forced to wait out a clock I have already beaten.
2. As a candidate, I want the Reading questions to become available immediately after I confirm the Listening phase is finished, so that my remaining exam time is spent answering rather than idling.
3. As a candidate, I want a clear primary action on the last question of a skill, so that I know what to do instead of staring at a dead button.
4. As a candidate who confirms a phase advance, I want the first question of the new skill to load immediately, so that I do not lose time to a reload.
5. As a candidate, I want irreversible phase locks to warn me before they take effect, so that I understand I cannot return.
6. As a candidate taking a short two-skill mock exam, I want to reach the second skill even when the first skill contains a single question, so that the exam is completable at all.
7. As a candidate sitting a Speaking & Writing exam, I want `Continue to Next Phase` to actually move me to the Writing section, so that I can finish the exam.
8. As a candidate, I want to be told when a phase transition fails, so that I can retry instead of assuming the product is broken.
9. As a candidate, I want my attempt to remain resumable after a failed transition, so that a transient server error does not cost me a credit.
10. As a candidate, I want the exam timer to reflect the phase deadline the server assigned me, so that client and server agree on how much time is left.
11. As a candidate reloading mid-attempt, I want to land on the question the server says I am on, so that I do not get silently rewound to Question 1.
12. As a candidate in an adaptive CAT unit, I want Unit 1 to stay locked after I confirm Stage 2, so that the adaptive contract holds.
13. As a candidate, I want to highlight words in a question stem, so that I can mark the key phrase I am reasoning about.
14. As a candidate, I want to highlight words in a reading passage, so that I can gather evidence for an answer.
15. As a candidate in a listening-only section, I want highlighting to work on the question stem, so that the tool is not dead for a whole skill.
16. As a candidate, I want a floating toolbar to appear above my selection, so that I do not have to hunt for a menu.
17. As a candidate, I want to attach a note to a highlighted phrase, so that I can record why the phrase matters.
18. As a candidate, I want a visible pin on noted phrases, so that I can find my notes again.
19. As a candidate, I want my highlights and notes to survive a page refresh, so that a crash does not erase my annotations.
20. As a candidate, I want the highlight toolbar to respect the section's configured tools, so that a certificate that forbids notes does not offer them.
21. As a candidate drilling practice questions, I want the hint to actually appear when I ask for it, so that the hint feature is worth its button.
22. As a candidate, I want the hint to state that using it affected the question's scoring, so that I understand the trade-off.
23. As a candidate listening to practice audio, I want to open the transcript when the question provides one, so that I can check what I misheard.
24. As a candidate on a question without a transcript, I want no transcript control at all, so that I do not chase a dead button.
25. As a candidate, I want the `[-5s]` rewind to sit inside the audio toolbar, so that the player reads as one coherent control cluster.
26. As a candidate, I want the sidebar filter pills to fit their card, so that I can filter my question grid without horizontal scrolling.
27. As a candidate, I want practice stimulus copy to reference my session's question numbers, so that instructions match what I am looking at.
28. As a candidate drilling a multi-component practice session, I want the previous question's options fully cleared from the DOM, so that a stale radio cannot be mistaken for a current answer.
29. As a candidate, I want locked palette chips to explain why they are locked, so that the palette reads as gated rather than broken.
30. As a candidate, I want cluster questions I have already reached to stay reachable, so that I can revise an answer inside the same audio group.
31. As a candidate, I want the phase timer to drive phase changes only when the server deadline actually expires, so that the UI never contradicts a transition I just confirmed.
32. As an administrator authoring an exam, I want the questions I mark `isLocked = false` to be the only ones the candidate can enter, so that my blueprint is honoured.
33. As a QA engineer following `docs/manual-test-plan.md`, I want Scenario 9 and Scenario 10 to be walkable end to end, so that the plan remains a valid regression script.
34. As a QA engineer, I want a failing phase transition to show up in the UI, so that I do not have to read server logs to learn the exam broke.
35. As a developer, I want one authoritative representation of the active phase, so that I am not debugging two competing sources of truth.

## Implementation Decisions

- **`AttemptTakeView` is the phase authority.** The sitting shell seeds `activeSkillId`, `activeUnitNumber`, `activeGroupId` and `phaseDeadlineAt` from the take view and mutates them only from transition endpoint responses. `getPhaseIndexAt(now)` is demoted to a display-time helper for the phase progress strip and is no longer allowed to call `renderQuestion`.
- **Transition endpoints stay the only mutation path.** No new endpoints. `POST /attempts/{id}/transition/skill`, `/transition/unit` and `/transition/group` already perform the required gating, timer reassignment and `LastQuestionId` updates; the fix is client-side adoption plus the removal of the wall-clock override.
- **Last-question affordance replaces the dead button.** When the current question is the final one in the active skill, the primary control becomes an enabled advance action that opens the existing confirmation surface and then calls the transition endpoint. `Next phase starts when time expires` is deleted from `updateNavigationForPhase`.
- **Transition failures are user-visible.** The verification-screen handler awaits the response, inspects status and body, renders an inline error with a retry control on non-2xx, and only hides the overlay and re-renders on success. It must not swallow the body, because `ATTEMPT_NOT_ACTIVE` and `WRONG_PHASE` need different remediation.
- **Tokenization becomes a rendering helper used by every `ee-tokenized-text` writer.** Question stems, cluster stems and the split passage all route through the existing tokenizer. Containers are only given the `ee-tokenized-text` class by the same code path that fills them with `.ee-token` spans, so the class can never appear without tokens.
- **Cluster-adjacent navigation is unlocked.** Questions inside the same question group as the current question are not locked by the audio-driven rule, matching the documented 3-question cluster behaviour.
- **Palette gating is announced.** Locked chips carry an accessible explanation (`aria-disabled` plus a description association, not `title` alone) and the legend gains a locked state.
- **Practice controls bind to their data.** The hint reveal writes the question's `Hint` into `#hint-text` and reveals `#hint-box`; the transcript toggle only renders when the question has a transcript; the rewind control moves into the audio toolbar container.
- **Sidebar filters are layout-safe.** The filter row wraps or scrolls within its card instead of overflowing the viewport.
- **No schema changes.** Every fix uses columns and endpoints that already exist (`Questions.Hint`, `ExamAttempts.ActiveSkillId`/`PhaseDeadlineAt`/`CandidateNotesJson`, `NavigatorItemDto.IsLocked`).

## Testing Decisions

Good tests here assert externally observable behaviour: what the candidate can click, what becomes visible, and what the server state is afterwards. They do not assert internal helper names or DOM class churn beyond the documented design-system hooks.

- **`tests/Services.Tests` (real LocalDB, schema loaded per run)** — the transition seam:
  - `TransitionToNextSkillAsync` from the last Listening question assigns the Reading skill and a fresh `PhaseDeadlineAt`, and marks Listening questions `IsLocked` in the returned navigator.
  - Transition against a non-`in_progress` attempt raises `AttemptNotActive` (the exact failure the browser swallows today).
  - A two-question, two-skill exam produces a navigator where the second skill's single question is reachable after one transition — the exam-6 shape.
- **`tests/WebMVC.Tests`** — the presentation seam:
  - Take view for a seeded exam renders a locked navigator entry with an accessible explanation, not only a `title`.
  - Practice take view for a question with a `Hint` renders that hint text in the DOM (initially hidden) and for a question without a transcript renders no transcript control.
- **`tests/WebMVC.E2ETests` (Playwright)** — the journey seam, mirroring the manual plan:
  - Scenario 10 shape: complete a `SYSTEM_PACED_AUTO` item, click the verification continue control, assert the Writing question is displayed and the overlay is gone.
  - Scenario 9 shape: with a passage-bearing question, select tokens, assert the popover appears and a saved note survives reload.
  - Scenario 7 shape: reach the last Listening question and assert an enabled advance control exists.
- Prior art: `tests/WebMVC.E2ETests` already drives preflight and sitting flows with Playwright and a real LocalDB; `tests/Services.Tests` already exercises `AttemptEngine` transitions against real triggers. New tests follow those fixtures rather than introducing a new harness.

## Out of Scope

- Redesigning the six-axis certificate model or the Certificate Wizard.
- Changing how credits are charged, refunded or refunded-on-abandon.
- Adding new question component types beyond the eleven existing ones.
- Admin Exam Studio authoring surfaces except where they must reflect a blueprint fix.
- Real SpeechSuper / Claude grading behaviour; validation runs with `ai=fake`.
- Payment gateway flows.

## Further Notes

### Implementation status

All eight tickets are implemented and verified.

| Ticket | Status | Verification |
| :--- | :--- | :--- |
| d01 | Done | `activePhaseIndex` holds steady across timer ticks; `getPhaseIndexAt` is display-only |
| d02 | Done | Q4 Listening → `Finish Listening & start Reading` → Q5 Reading with a fresh 75:00 countdown |
| d03 | Done | TOEIC_SW walks Q1 Speaking → Q2 Writing (keywords + toolbar) → group lock → Q3 essay |
| d04 | Done | 17 `.ee-token` spans in a live question stem; popover/highlight/note/pin/persist all work |
| d05 | Done | `aria-disabled` + `aria-describedby` on every chip, locked legend entry, locked state follows gating |
| d06 | Done | Hint reveals real text, re-enables after a question change, hides entirely when no hint exists |
| d07 | Done | Rewind and transcript live inside `#practice-audio-toolbar` |
| d08 | Done | Filters wrap (`docScrollWidth` 1252 < 1262 viewport); stale option widgets cleared per question |

Test evidence: 497 `WebMVC.Tests` + 627 `Services.Tests` + 37 `WebMVC.E2ETests` — 1161 passed, 0 failed.

### Ticket index

| Ticket | Title | Blocked by |
| :--- | :--- | :--- |
| d01 | Server-authoritative phase state in the sitting shell | None |
| d02 | Explicit phase advance on the last question of a skill | d01 |
| d03 | Observable, retryable phase transition from the speaking verification screen | d01 |
| d04 | Tokenize every tokenized-text container | None |
| d05 | Palette gating that explains itself and respects clusters | d01 |
| d06 | Practice hint reveal bound to real hint content | None |
| d07 | Practice transcript toggle and audio toolbar consolidation | None |
| d08 | Practice layout, copy and stale-option cleanup | None |

Frontier tickets (no blockers, can start in parallel): **d01, d04, d06, d07, d08**. d02, d03 and d05 unlock once d01 lands.

Reproduction used `scripts/seed-manual-test.sh fresh` (DB `ExtraEfficiency`, payment=mock, ai=fake) and a Playwright-driven browser. Server-side logs for the transition failure are in `logs/manual/mvc.log` around the `POST /api/learner/attempts/{id}/transition/skill` entries. The version of `docs/manual-test-plan.md` in the repository still targets paths from a different checkout (`D:\Study\PRN3\Project\extra_efficiency_ui`) and a `(localdb)` database name that does not match this working tree; the plan's scenarios remain valid but its startup section should be refreshed in a separate change.
