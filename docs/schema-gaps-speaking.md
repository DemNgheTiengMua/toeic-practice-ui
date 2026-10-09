# Schema gaps — speaking / writing (free-response) support

Recorded 2026-10-04 while shaping the Speaking test screens.

> **Status 2026-10-08:** gaps 1, 2, 3, 4, 6, 7 and 8 are now fixed in
> `docs/database-schema.sql` (tags UI-9, UI-20, UI-17, UI-19, UI-26, UI-31) and
> covered by `docs/schema-tests/domain11-ui-coverage.sql`. Gap 5 is kept as a
> deliberate decision. See `docs/schema-ui-audit.md`.

Originally: **nothing here was
fixed yet.** These are places where the multi-certificate companion schema
(`docs/database-schema.sql`) cannot store something the real TOEIC Speaking
test requires, found by comparing the schema against the published format.

Source for the real format: ETS, *About the TOEIC Speaking and Writing Test*,
and IIBC, *Test Format and Content — TOEIC Speaking Test*. 11 questions across
5 task types, about 20 minutes.

| Q | Task | Prep | Response | Criteria scored | Max |
|---|------|------|----------|-----------------|-----|
| 1–2 | Read a text aloud | 45s | 45s | Pronunciation; intonation & stress | 0–3 |
| 3–4 | Describe a picture | 45s | 30s | + grammar, vocabulary, cohesion | 0–3 |
| 5–7 | Respond to questions | 3s | 15s or 30s | + relevance & completeness of content | 0–3 |
| 8–10 | Respond using information provided | 45s (once, before Q8) | 15s or 30s | same as 5–7 | 0–3 |
| 11 | Express an opinion | 45s | 60s | same as 5–7 | 0–5 |

---

## Gap 1 — nowhere to store per-question timing

**Blocking.** The UI has to count down a prepare window and a speak window per
task, and both vary: 45s prep for Q1 but 3s for Q5; 45s response for Q1 but
15s for some of Q5–7.

- `Exams.DurationMinutes` is whole-exam only (`:137`).
- `Sections` carries `OptionCount` but no duration (`:32-55`).
- `Questions` carries `DifficultyLevel` and `DisplayOrder`, no timing (`:163-188`).

Response time varies *within* one task type (Q5–7 are "15 or 30 seconds"), so
section-level columns are not enough on their own.

**Suggested shape:** `Questions.PrepSeconds INT NULL` and
`Questions.ResponseSeconds INT NULL`, both `NULL` for MCQ questions, both
`NOT NULL` for `free_response`. A CHECK can pin that to `QuestionType` the
same way `QuestionOptions` already does via `FK_Options_QuestionType`.
Optionally `Sections.DefaultPrepSeconds` / `DefaultResponseSeconds` as the
authoring default the question row overrides.

**Until it exists:** the prototype hardcodes the table above as display-only
fiction. Any screen showing "45s to prepare" is showing a number the target
schema cannot currently persist.

## Gap 2 — criteria sets cannot vary by question

**Blocking for a faithful rubric.** `GradingCriteria` is keyed
`UNIQUE (SkillId, Code)` (`:422-436`), so every question in the speaking skill
draws on one single criteria set. The real test scores Q1–2 on two criteria
and Q11 on four — the set grows as the tasks get harder.

`AiGradingScores` is `PRIMARY KEY (GradingId, CriterionId)` and `AiGradings`
is one row per `FreeResponseId`, i.e. per question (`:438-477`). So the
*scores* are per-question already; it is only the *applicable criteria* that
cannot be narrowed per question.

**Suggested shape:** a `SectionCriteria (SectionId, CriterionId)` join, so a
section declares which of its skill's criteria apply. Carry `SkillId` in the
composite FK the way everything else in this schema does, so a section cannot
reference another skill's criterion.

**Workaround that needs no schema change:** store all four criteria and let
the grader write no `AiGradingScores` row for a criterion that does not apply
to that question. The UI then renders only the rows that exist. This is
representable today, but nothing *enforces* that Q1 never receives a cohesion
score.

## Gap 3 — one criterion cannot have two maximums

**Blocking for a faithful rubric.** `GradingCriteria.MaxScore` is one value
per criterion (`:428`), and `trg_AiGradingScores_BoundedByMaxScore` enforces
it (`:487-500`). The real test scores the same criteria 0–3 on questions 1–10
and 0–5 on question 11.

With a single `MaxScore`, either Q11 cannot reach 5, or Q1–10 are allowed up
to 5 and the ceiling stops meaning anything.

**Suggested shape:** move the ceiling to wherever Gap 2 lands — a per-section
`MaxScore` override on the `SectionCriteria` join — and have the trigger read
that in preference to `GradingCriteria.MaxScore`. Note `R7` already warns that
lowering `MaxScore` after scores exist leaves them above their own ceiling;
any override column inherits that problem and needs the same guard.

## Gap 4 — no recorded length on a free response

**Not blocking, but costs a real feature.** `FreeResponses` stores
`AudioPath` and `SubmittedAt` (`:323-352`) but no duration. So no screen can
say "you spoke for 38 of your 60 seconds", and nothing can distinguish a
genuine short answer from a recording that was cut off.

**Suggested shape:** `FreeResponses.DurationMs INT NULL`, written on upload,
`CHECK (DurationMs IS NULL OR DurationMs > 0)`.

Related: `CK_FreeResp_HasContent` requires `ResponseText` or `AudioPath`
(`:340`), which means a *silent* recording is indistinguishable from a good
one until grading runs. That is probably correct — the grader decides — but
worth knowing.

## Gap 5 — no version history on a re-recording

**Probably intentional; confirm.** `UQ_FreeResponses UNIQUE (AttemptId,
QuestionId)` (`:338`) means one response per task. Re-recording overwrites and
the previous take is gone.

The UI consequence is already designed for: re-record is presented as
destructive, and the existing take is playable before it is replaced. Raised
here only so the constraint is a decision rather than an accident.

---

## Gap 6 — a speaking-only attempt has no coherent band

**Not blocking, but the review screen had to work around it.**
`ExamAttempts.BandCode` is a snapshot of the band for `TotalScore`, and
`ScoreBands` is keyed by certificate only (`:86-95`). TOEIC's CEFR bands run
over the combined 10–990 scale, so a speaking-only attempt, whose scaled score
tops out at 200, can never reach A2 or above (225+), and a score of 120 would
be reported as the lowest band for reasons that have nothing to do with the
learner's ability.

`speaking-review.html` therefore shows the 0–200 scaled score and says that a
band is set from a full mock exam's total. That is a copy decision, not a fix.

**Suggested shape:** either key `ScoreBands` by skill as well as certificate, or
make `ExamAttempts.BandCode` nullable by rule for attempts that do not cover
every skill the certificate's scale sums over.

## Gap 7 — mentor price is cash, the ledger charges credits

`MentorProfiles.PricePerSlot` is `DECIMAL(10,2)` (a cash amount, shown as
50,000 ₫ on the mentor list), while a `mentor_booking` ledger row is an integer
credit `Delta`. Nothing maps one to the other. The prototype's confirm screen
charges "1 credit" for every mentor, so the list price and the charge disagree
on screen. Decide whether a slot is priced in credits (add `PriceCredits INT`) or
whether cash and credits convert at a fixed rate, and show one of them.

## Gap 8 — a cancelled booking has no ledger reason to credit it back

`booking-detail` (cancelled) says "Your credit has been refunded" and
`mentor-confirm` promises free cancellation up to 24 hours before. The reasons
that credit are `purchase` and `admin_grant` only (`:844-864`); `admin_grant` is
tied to a resolved complaint. A learner-initiated cancellation therefore has
nothing to post. Either add a `booking_cancel` reason (Delta > 0, `BookingId`
required, one per booking) or route cancellations through an admin grant.

## Not gaps — confirmed workable

- **Standalone speaking practice.** `PracticeSessions.Modality` is
  `CHECK (Modality IN ('listening','reading'))` with a composite FK pinning it
  to the section (`:236-245`), so speaking practice **cannot** be a practice
  session. It is instead an `ExamAttempt` against a speaking-only exam.
  Nothing requires an exam to be a full mock — an exam is question groups from
  one certificate's sections with its own duration.
- **Charging it once.** `UX_CreditTxn_Attempt_Reason` (`:946`) permits one
  `mock_exam_start` *and* one `ai_grading` per attempt, "but only one of
  each". A speaking-practice attempt carries `ai_grading` alone, no
  `mock_exam_start`. No new ledger reason needed.
- **Free retry after a failed grading.** The same unique index means a second
  `ai_grading` charge on that attempt is impossible, so retrying is
  structurally free. No refund path is required.
- **No complaint route for grading.** `Complaints.BookingId` is `NOT NULL`
  (`:726`), so a grading failure cannot become a complaint. Retry is the only
  remedy the schema allows.
- **Speaking prompt audio.** `QuestionGroups.AudioPath` already exists
  (`:153`) for the spoken question in Q5–10.
- **Per-skill scaled score.** `AttemptSkillScores` holds raw and scaled per
  skill (`:292-304`), and `ScoreScales` is per-exam raw→scaled, so a 0–200
  speaking scale is expressible today.
