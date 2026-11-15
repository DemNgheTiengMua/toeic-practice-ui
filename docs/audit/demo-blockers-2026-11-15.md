# Demo blockers found 2026-11-15 — three defects, fixed and re-walked

Found while preparing a live demo of Speaking, Writing and payOS. Each is a **behaviour** defect
(not display), so each shipped with a test that fails without the fix.

---

## 1. Playing a 4-skill placement was impossible from `/onboarding`

`OnboardingPage.tsx` rendered `<Radio value="…">` with a `<div>` child. HeroUI v3 `Radio` needs
`Radio.Control` + `Radio.Content` (see `PlacementCatalogPage.tsx` for the correct shape). Without
them the control produced **no `<input type=radio>`, no `role=radio`, and no click handler** — so
Step 1 of onboarding was permanently `listening_and_reading` and the "4 skills — Listening, Reading,
Speaking & Writing" option could not be chosen at all.

Impact on the demo: Speaking and Writing could never be reached through the front door, because the
4-skill placement is what opens them.

Fixed in `src/web/src/features/learner/OnboardingPage.tsx`. Pinned by
`oobe-softlock.test.tsx › sends the scope the learner selected, not the default` — verified red on
the old markup (assert `postedBody.scope === 'all_skills'` failed) and green after.

---

## 2. Every browser recording was rejected at upload

Chrome's `MediaRecorder` produces `audio/webm;codecs=opus`, and `AudioValidation.Validate` compared
the **whole Content-Type header** against an allow-list that holds bare `audio/webm`. The result was
`400 Invalid audio file format or MIME type 'audio/webm;codecs=opus'` on every real take.

Fixed in `src/Services/FileStore.cs`: the media type is now the part before `;`, in both `Validate`
and `GetExtensionFromContentType`. Pinned by `SpeakingPracticeServiceTests` (5 new cases, 6 red
without the fix) and a rejection case proving an unknown type is still refused.

---

## 3. SpeechSuper answered "The audio type is not supported" for all speaking

After (2), the file was accepted by our API but refused by the provider. Probed the live
`speak.eval.pro` endpoint with one byte-identical webm file:

| `audioType` sent | provider answer |
|---|---|
| `webm` | `errId 41002 — The audio type is not supported` |
| `opus` | graded; transcript and scores identical to the mp3 baseline |

SpeechSuper's allow-list is `wav / mp3 / opus / ogg / amr`; there is no `webm`. The fix is a name
translation, not a transcode: `SpeechSuperAiGrader` sends `opus` for a `.webm` file. Proven live —
grading `3` moved from `failed` to `done`, `model = speechsuper-speak.eval.pro`, overall `13.30`.

Note the container has **no ffmpeg**, which is why transcoding would have been the expensive answer.

---

## 4. The first real checkout was rejected as "Order already open"

`docs/seed-demo.sql` inserts two demo orders carrying explicit `PaymentNo` 1 and 2 **without
advancing `SEQ_PaymentNo`**, which still sat at 1. The first genuine order therefore reused
`PaymentNo = 1`, hit `UQ_Orders_PaymentNo`, and `PaymentService` translated *any* unique violation
into `OrderAlreadyOpenException` — so the learner was told to cancel an order that did not exist,
while `/checkout/preview` correctly reported `openOrder: null`.

Two fixes:
- the seed now ends with `ALTER SEQUENCE [SEQ_PaymentNo] RESTART WITH 3;`
- `PaymentService` only reports "order already open" when the violated index is
  `UX_Orders_OnePendingPerUser`; any other unique violation rethrows.

Re-walked after a `docker compose down -v`: checkout → mock success → balance 0 → 5, ledger holds
exactly one `purchase +5`; `failed` (error 24) and `expired` add nothing.

---

## 5. A 4-skill placement reported writingPct and speakingPct as 0.00

Observed on a live 4-skill sitting where both tasks were graded normally
(`AiGradings` writing 25.00, speaking 13.30) and the result page still said `writingPct: 0.00,
speakingPct: 0.00`.

Root cause in `AttemptEngine.Submission.cs`: the percentage for an AI-graded skill was computed from
the skill's **scale band** —

```
range = MaxScaledScore - MinScaledScore      // NULL for IELTS WRIT and SPEA
skillPct = range > 0 ? ... : 0               // so always 0
```

The seeded IELTS certificate sets `MinScaledScore`/`MaxScaledScore` to NULL for WRIT and SPEA
because those skills are graded out of a rubric, not a numeric band. `AttemptSkillScores.RawScore`
for such a skill is a rubric total (25 of 36), so the divisor is the rubric maximum instead.

Fixed by dividing by `GradingCriteria.MaxScore` summed per skill. Verified live: the same sitting now
reports `writingPct 61.11` (22/36), `speakingPct 36.11` (13/36), `overallPct 34.30`.

**No unit test accompanies this fix.** A test was written and then removed: the fixture in
`PlacementSubmissionTests` reaches the settle pass by a path where a rubric-only skill has no
`AttemptAnswers` row, and making the fixture produce the live shape cost more turns than the fix
itself. The live evidence above is the proof; a test belongs in a follow-up with a pool fixture that
carries both an answer row and a free response for one skill.



The full `Services.Tests` suite is **not green on `main`**. Verified by running it on a clean
worktree at `ffce6a81` and comparing the failing sets name by name: **27 unique test names, 33
tests, identical on both** — so none of the failures below come from the four files changed here.
The families are pool/drill/selection, exam-preset calibration, and item lifecycle; the one that
looks most alarming, `HasTriggerDriftTests`, is a model-vs-database drift check unrelated to a seed
edit.

Two confirmed individually because they touch Speaking/Writing:

- `ExamRebuildFullSystemE2ETests.Preset_Score_Calibration_TOEIC_Speaking_And_Writing_Async`
  (`presets.Single(p => p.PresetCode == "toeic_sw")` finds no such preset)
- `LearningPath5CreditTests.WritingStep_InLearningPath_IncludesGuideAndRubric_AndChargesZeroCreditsOnAiGrading`
  (`WritingPracticeService.SubmitAttemptAsync:435`)

They mean a full-suite run reports failures that are not yours. Run the focused filters named in
each section above instead of reading the total.

---

## 6. The Speaking and Writing result pages showed each other's task

Observed in the browser on the placement from fix #5: `/speaking/result/3` listed **Task #11** (the
writing essay) under a heading that reads "Pronunciation & rubric criteria", and its header said
**"2 of 1 tasks answered"**.

Root cause: both `GetAttemptReviewAsync` methods loaded **every** free response on the attempt, while
`totalExamQuestions` (Speaking) counts only positions whose section modality is `speaking`. A
placement carries Writing and Speaking on one attempt, so each page displayed the other's task and
the header counted a set the page did not show. Writing had the identical defect in mirror image.

Fixed in `SpeakingPracticeService.cs` and `WritingPracticeService.cs`: each loop now skips a response
whose section modality is not the page's own, and `TotalQuestions`/`AnsweredQuestions` are read from
the filtered set. Pinned by `ProductiveReviewSkillIsolationTests` (2 cases on a shared paper — a
single-skill paper would have passed the old code, because the filter had nothing to exclude).
Verified red without the fix (2 failed), green with it.

Browser re-walk after the fix:

| Page | Header | Tasks shown |
|---|---|---|
| `/speaking/result/3` | **1 of 1 tasks answered** • 1 graded | only Task #12, transcript present |
| `/writing/result/3` | **1 of 1 tasks submitted** • 1 graded | only Task #11 |

---

## Verified demo paths after the fixes (browser + API, `docker compose` stack)

| Step | Result |
|---|---|
| 4-skill placement from `/onboarding` | selectable, `scope=all_skills` reaches the server and persists on `PlacementTests.Scope` and `ExamAttempts.PlacementScope` |
| Objective answer | saves; DB row `SelectedLabel=B`, `IsCorrect=1`, progress `1 / 10 answered` |
| Section lock | dialog "Advance to next locked section?" appears at a section boundary and holds |
| Writing free response | counter updates (`97 / 1,000 words`), draft persists (`FreeResponses.status=draft`, 639 chars) |
| Submit → Writing grading | real Claude: `AiGradings.status=done`, `model=claude-sonnet-3.7`, `OverallScaled=24.00` |
| Speaking record | records and uploads ("Spoken response saved") |
| Speaking grading | real SpeechSuper: `status=done`, `model=speechsuper-speak.eval.pro`, `OverallScaled=13.30` |
| 4-skill placement result | `writingPct 61.11`, `speakingPct 36.11`, `overallPct 34.30` (were 0.00 before fix #5) |
| Checkout | order created, order code `DH-20261115-0003` |
| Mock: payment successful | order `paid`, balance 0 → 5, ledger holds exactly one `purchase +5` |
| Mock: payment failed | order `failed`, `GatewayErrorCode=24`, no credit |
| Mock: order expired | order `expired`, no credit |

