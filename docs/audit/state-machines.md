# The implicit state machines, and what was wrong with them

An audit of every state-bearing column in `database-schema.sql`, taken because the CEFR rebuild changed
who writes each of them. Each entry says what the machine is, what the spec intends, where the
transition is enforced, and what the probe showed.

The distinction that decides "fix" from "record" is **reachability**, not the absence of a guard. An
unguarded transition no writer can produce is a latent hole; one a live route can produce is a defect.
Both are written down here so the next reader does not have to re-derive the difference.

Status: audit complete. Five defects fixed, three found later by driving the real browser flow (in
"Found in the browser" below), ten unguarded transitions recorded with the reason each is left alone.

---

## Defects found and fixed

### 1. `PracticeSessions.Status` — a GET finished a session

| | |
|---|---|
| Machine | `in_progress` → `finished` \| `abandoned`, terminal |
| Enforced by | *nothing, before this change* |
| Reachable through | `GET /practice/{id}/summary`, `GET /practice/insight/{id}` |

`PracticeService.GetSummaryAsync` and `AiInsightService.GetOrCreatePracticeInsightAsync` both did "if the
session is in progress, finish it and stamp `FinishedAt`". The summary page prefetches the insight panel,
so opening the summary mid-drill closed the session underneath the learner; their next submission was
then refused because the session was no longer in progress.

Fix: both reads report the state they found. A session is finished by the sitting (last question
answered, or the lesson micro-check), and by nothing else.

Test: `PracticeServiceTests.Reading_a_summary_or_an_insight_does_not_finish_an_in_progress_session`.
Red before the fix with `Expected: null / Actual: 2026-11-11T…`.

### 2. `PracticeSessions.Status` — no transition guard at all

| | |
|---|---|
| Enforced by | *nothing, before this change* |
| Probe result | `finished → in_progress` accepted; `FinishedAt` clearable; `finished → abandoned` accepted |

A finished drill could be written back to `in_progress` with its stamp cleared. Its answers were already
counted, and `SubmitAnswerAsync`'s one-answer-per-question guard reads the *answer*, trusting the session
to still be open — so the re-opened session was sittable again with content already in place.

Fix: `trg_PracticeSessions_StatusTransitions` — both terminal states are terminal.
`CK_Practice_FinishedStamp` was also tried and withdrawn: a schema fixture legitimately writes a
`finished` session with no stamp (a row from before #156), and the dashboard already falls back to
`StartedAt` for it. The trigger is the real guard; the CHECK was not.

Note: adding the trigger required `HasTrigger` on `PracticeSession` in EF — without it every practice
save threw `DbUpdateException` ("the target table of the DML statement cannot have any enabled triggers
if the statement contains an OUTPUT clause"). AGENTS.md §1 warns about exactly this.

Test: `domain43b-practice-session-state-machine.sql`, which asserts the legal moves **first** and is
verified to fail with the trigger dropped.

### 3. `ExamAttempts.Status` → `abandoned` — cancel had no precondition

| | |
|---|---|
| Machine | `in_progress` → `submitted` → `graded`; `in_progress` → `abandoned` |
| Enforced by | *nothing, before this change* |
| Reachable through | `POST /attempts/{id}/cancel` |

`AttemptEngine.CancelAttemptAsync` set `abandoned` on any attempt it could load. Cancelling a **graded**
attempt therefore erased a result the learner had earned — score, band and submitted stamp all went, and
the attempt read as abandoned. The endpoint already maps `AttemptNotActive`, which is the branch that was
meant to answer this, so the precondition was intended and simply missing.

Fix: refuse unless the attempt is `in_progress`.

Test: `MockExamServiceTests.CancelAttempt_refuses_to_abandon_a_graded_attempt_and_keeps_its_result`.

### 4. A published pool-only paper froze an empty phase plan

`trg_Exams_PublishFreezesEdition` built `PhasePlanJson` from a `Questions`-only subquery, so a
pool-authored paper froze `[]`, and `ParseFrozenPhases` treats an empty plan as "no edition" and falls
back to today's `Skill` rows — defeating the pin #227 exists to hold.

Measured on a real schema: `phasePlan=[]`, `ExamItems=1`, `Questions=0`. After the fix: the correct
single-skill plan. The subquery now accepts either store.

### 5. `AiGradingScores` ceiling — a pool response could score 99 against a maximum of 5

`trg_AiGradingScores_BoundedByMaxScore` joined `Questions` on the nullable `FreeResponse.QuestionId`, so
for a pool response the join produced no row and the guard never fired. Measured: a score of 99 was
accepted against a `MaxScore` of 5, and the section-membership arm was equally blind.

Fix: both arms resolve the section from the class row through the roster position.

---

## Found in the browser, not by a test

These three were invisible to the suite and only appeared when the real flows ran with real files. Each
now has a test that writes only pool rows and asserts the paper holds zero `Questions` before asserting
the result, so a green assertion cannot be the legacy branch answering.

### 6. The AI grading processor could not grade a pool response

`fr.Question.SectionId` threw `NullReferenceException` inside EF's parameter evaluation. The learner was
charged the grading credit at submit and the result never arrived. The section, stem, word window,
required keywords, reference text and modality now come from the pinned version. The position lookup also
moved inside the per-item `try`: it was outside, so its failure escaped the catch and left every grading
in the batch `pending`.

### 7. Three readers took the page down after a successful grading

`AttemptEngine.RubricTotalsAsync` (band estimate), `WritingPracticeService.GetGradingPendingAsync`,
`SpeakingPracticeService.GetGradingPendingAsync` and both review pages all dereferenced
`FreeResponse.Question`. Measured: "Nullable object must have a value" on the page that reports grading
progress, after the grading had already succeeded and the credit was taken.

### 8. Audio upload rejected every pool recording, and the routes crossed modalities

`SpeakingPracticeService.SaveAudioAsync` resolved the task with a `db.Questions` query, so every upload
answered "Speaking question 36 not found in this exam" — the recording window, the response shape and the
skill all came from a row that does not exist. Measured with a real 20,900-byte mp3: 500 before, 200
after, file at `App_Data/audio/attempts/4/q_36_….mp3`, row stored as `QuestionId=NULL, ExamItemId=36`.

Separately, writing and speaking both render as `free_response`, and every filter asked only for that
format — so `GET /api/learner/writing/attempts/{speakingAttemptId}` answered 200 with the speaking exam
and its three speaking tasks. Both services now filter on their own modality, and a mismatched route
answers 404.

`ItemTelemetryService` had the same projection defect and aborted the whole telemetry pass; its "a pool
grading is skipped below" comment was unreachable because the query died before the skip.

---

## Unguarded transitions recorded, not changed

Each of these is unguarded at the database, and **no writer can produce the transition**. Guarding them
would be inventing a rule the product does not have; the note exists so that the first writer to try one
finds this instead of a silent success.

| Column | Unguarded move | Why it is left alone |
|---|---|---|
| `ExamAttempts.Status` | `graded → submitted` | A documented retry: `WritingPracticeService`/`SpeakingPracticeService` move a graded attempt back to `submitted` so a failed AI grading can be re-finalised. |
| `ExamAttempts.Status` | `graded → abandoned` | Refused today only accidentally, by `CK_Attempts_SubmittedAt`. The cancel route now refuses it explicitly (defect 3); no other writer sets `abandoned` on a graded row. A dedicated trigger is worth adding only if a second writer appears. |
| `PlacementTests.Status` | `done → in_progress` | `PlacementResultWriter` deliberately returns a placement to `in_progress` while a productive task is still queued (PLC-16). The single-writer trigger already forces that write to declare itself. |
| `LearnerPaths.Status` | `completed/reset → active` | Paths are only created `active`; `reset` and `completed` are terminal in every writer. |
| `MentorSlots.Status` | `closed → open` | Closing refuses a booked slot and has one writer; nothing reopens one. |
| `Bookings.Status` | `cancelled → confirmed` | Cancelling is one-way in code, and `trg_CreditTransactions_BookingAmounts` refuses a second refund. A revival would be visible as a booking whose refund still stands, so it is worth a guard the day an admin un-cancel exists. |
| `ImportBatches.Status` | `done → pending` | `done → failed` is a real reprocess (`IngestionWorkbenchService`); nothing moves a batch back to `pending`. |
| `AiGradings.Status` | `done → pending` | A documented retry that clears its scores before reuse; `CK_AiGradings_FeedbackOnlyWhenDone` catches leftovers. |
| `Complaints.Status` | `resolved → open` | Admin-only; no writer reopens one. |
| `Orders.Status` | `expired/failed → paid` | A late payment confirming after the local expiry is a **supported** gateway scenario, and paying is the money-bearing direction. The dangerous direction — leaving `paid` after crediting — is refused by `trg_Orders_CreditedStaysPaid`. |

---

## Machines that were already sound

Checked, guard located, probe run:

- **`Items.LifecycleStatus`** — explicit allow-list of ten transitions in `trg_Items_LifecycleTransitions`;
  `retired`/`retired_defective` terminal; hard delete refused.
- **`LearnerPathSteps.Status`** — `trg_LearnerPathSteps_SingleWriter` refuses reopening a `done` step.
- **`PlacementTests`/`PlacementSkillResults`** — the best-guarded tables in the schema: single writer,
  projection version must increase, `done` requires a measured skill or a recorded failure, snapshot must
  match the attempt.
- **`FreeResponses.Status`** — `graded → draft` refused on a non-`in_progress` attempt; no draft into a
  submitted or graded attempt; every draft must be submitted.
- **`MentorSlots`/`Bookings` sync** — `trg_Bookings_SlotStatusGuardAndSync` keeps the slot's status
  derived from its bookings and refuses a booking on a closed slot.
- **`UserCellMasteries.State`** — single writer plus `StateIsEarned`; a state cannot be typed in.
- **`Exams.Status`** — publishing is one door: never created published, and a move to `published` needs a
  readiness pass newer than the last edit.
- **`WritingPractice`/`SpeakingPractice` attempts** — every write path checks `in_progress` and expiry
  first, and `SubmitAttemptAsync` is idempotent on an already-submitted attempt.
