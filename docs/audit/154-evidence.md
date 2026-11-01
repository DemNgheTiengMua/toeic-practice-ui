# #154 — dynamic four-week path: evidence

Two screenshots and two SQL reports. Everything below was produced by the running app against the
real schema and real SQL Server; nothing here is a test double.

## 1. Two learners, one band pair, different paths

Both learners are on certificate `TOEIC`, band pair **B1 → B2**, with a goal that carries a pace.
They differ only in which sections their own practice history says are weakest.

| | learner@demo.local | learner.fresh@demo.local |
|---|---|---|
| band pair | B1 → B2 | B1 → B2 |
| goal pace | 10 h/week | 6 h/week |
| weakest scored sections | P4 (0%), P5 (22.9%) | P7 (0%), P2 (14.3%) |
| **practices drilled** | **P4 Talks, P5 Incomplete Sentences** | **P1 Photographs, P7 Reading Comprehension** |

The full step-by-step dump is `154-divergence.sql`; run it with
`sqlcmd -S "(localdb)\MSSQLLocalDB" -d ExtraEfficiency -I -W -s"|" -i docs/audit/154-divergence.sql`.

Both paths span weeks 1–4 with five steps each and a mock exam at the end of weeks 2 and 4.
They share the *shape* — that is the ticket's 60/25/15 split — and differ in *content*, which is the
point: the same band pair no longer means the same path.

## 2. The path screen

- `154-four-week-plan.png` — a freshly generated path: four weeks, 5 steps each, 45 min per step,
  every duration and the On/Off-track verdict on the week.
- `154-week-progress-after-walk.png` — the same path after one step of each type was completed
  through the app. Week 1 shows `5/5 steps done · 225 of 225 min planned · accuracy 3.8% (target 70%)`
  and is **Off track**: every step is done, but the measured accuracy is below the week's target.
  That is the rule from the ticket, not a bug.

## 3. What closes a step

| step type | activity that closes it | writer |
|---|---|---|
| `practice_section` | the last answer of the linked practice session | `PracticeService.SubmitAnswerAsync` |
| `lesson` | the last card of the lesson | `LessonService.RecordCardViewAsync` |
| `mock_exam` | submitting the mock attempt (graded, or finalized after AI grading) | `AttemptEngine.SubmitAttemptAsync` / `FinalizeAiGradedAttemptAsync` |

All three call `PathStepCompletion.CloseStepAsync`, which sets `Status`, `CompletedAt`, `Score`,
`ActualAccuracyPct`, unlocks the next step in plan order, and completes the path when nothing is left.
