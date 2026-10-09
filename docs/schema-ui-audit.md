# Schema vs. screens audit — 2026-10-08

All 52 prototype pages (every state) were read against `docs/database-schema.sql`.
Each piece of per-user / per-entity data or admin input was checked for a column
that stores it or a query that derives it. Gaps are fixed in the schema and tagged
`UI-n` in its comments, so `grep UI-` finds every change.

Verification: `bash docs/schema-tests/run-all.sh` passes all 11 suites on LocalDB,
including the new `domain11-ui-coverage.sql` (33 checks for the rules below).
That suite caught three silent truncation bugs in my own first draft (`Exams.Purpose`,
`Orders.Status`, `LearnerPathSteps.Status` too narrow for the new values), fixed.

## Fixed in the schema

| Tag | What the screen needs | Pages | Change |
|---|---|---|---|
| UI-10 | Why the learner's *chosen* wrong option is wrong | exam-review, practice-take, practice-summary | `QuestionOptions.Rationale` (Explanation stays the general why-correct) |
| UI-21 | "To reach B2: …" recommendations for *this* attempt | exam-result | `Recommendations.AttemptId` + owner FK |
| UI-8 | Question-type labels ("Verb tense", "Part 7 inference") | practice-summary, weakness-analysis | `QuestionCategories`, `Questions.CategoryId` |
| UI-34 | Accuracy across exams **and** practice | weakness-analysis, dashboard | `vw_UserSectionAccuracyAll`, `vw_UserCategoryAccuracy` (old view counted practice only) |
| UI-16 | Flag for review / "Only flagged questions" | exam-reading/listening, confirm-submit, exam-review | `AttemptAnswers.IsFlagged` |
| UI-15 | "You returned to question 8" | exam-*, placement-take | `ExamAttempts.LastQuestionId` (pinned to the exam) |
| UI-14 | "Completed 11:42 · time taken 01:53:29" | exam-result, exam-history | `ExamAttempts.SubmittedAt` |
| UI-13 | Save question / incorrect-answer bank | practice-take, practice-summary | `SavedQuestions` |
| UI-11 | Question source + feedback mode options | practice-select | `PracticeSessions.QuestionSource`, `FeedbackMode` |
| UI-5/6/7 | Photo + required alt, per-Part audio file (size, length), replay one item, transcript | question-editor, exam-editor, exam-review | `QuestionGroups.ImagePath/ImageAlt/Transcript/AudioStartMs/AudioEndMs`, `ExamSectionAudio` |
| UI-4 | Test source, internal note, "Hidden" status, duplicate-name error, last-updated | exam-editor, exam-list, question-editor | `Exams.SourceName/InternalNote/UpdatedAt`, status `hidden` replaces unused `archived`, `UX_Exams_Cert_Name`, `Questions.UpdatedAt` |
| UI-18 | Fixed structure: 39 questions in Part 3, Listening 45 min | exam-editor, exam-instructions | `Sections.ExpectedQuestionCount`, `Skills.DurationMinutes` |
| UI-3/22 | Placement tests listed apart from mocks; resumable answers; L/R score, 40 questions, 28 min | placement-select/take/result | `Exams.Purpose` + `Description`, `PlacementTests.AttemptId/TotalScore`, FK forcing a placement exam |
| UI-23/24/25 | Module group, description, estimate, question count, topics, premium-only, "In progress", "built from your placement on …" | learning-path, path-detail, path-generated | `PathModules.GroupName/Description/EstimatedMinutes`, `PathModuleItems.QuestionCount/IsPremiumOnly`, `PathModuleTopics`, step status `in_progress` + `StartedAt`, `LearnerPaths.PlacementId` |
| UI-12 | Module progress / accuracy / time spent | path-detail | `PracticeSessions.LearnerPathStepId` (derive from that session) |
| UI-9/17/19/20 | Speaking gaps 1, 4, 6, 2+3 | exam-speaking, speaking-review | `Questions.PrepSeconds/ResponseSeconds`, `QuestionGroups.PrepSeconds`, `FreeResponses.DurationMs`, no band on `speaking_practice` attempts, `SectionCriteria` with per-section `MaxScore` enforced by trigger |
| UI-1/2 | Full name, phone, terms; locked / not activated / last sign-in / resend email | register, profile, admin users | `Users.FullName/Phone/TermsAcceptedAt/IsLocked/EmailVerifiedAt/LastLoginAt`, `EmailVerificationTokens` |
| UI-26/27 | Mentor price in credits, headline, expertise, 60-min session, focus area | mentor-list/detail/confirm | `MentorProfiles.PriceCredits/Headline/Expertise/SessionMinutes`, `Bookings.FocusArea`; trigger: charge = `-PriceCredits` |
| UI-31 | Cancelled booking refund (gap 8) | booking-detail | ledger reason `booking_cancel`; trigger: only on a cancelled booking, equal to the charge, once |
| UI-32 | Admin credit adjustment with required reason and admin name | admin users, complaint-detail | `CreditTransactions.AdminUserId/Note`, required for `admin_*` |
| UI-28 | Complaints about exams and orders, not only bookings; type; internal notes; who resolved | admin complaints, complaint-detail | `Complaints.AttemptId/OrderId/Type/InternalNotes/ResolvedByUserId/ResolvedAt`, exactly-one-subject + type-matches-subject CHECKs, owner FKs |
| UI-29 | Featured package, bullet list | pricing, admin packages | `Packages.IsFeatured` (one only), `Features`, `DisplayOrder` |
| UI-30 | Order code, VNPay/MoMo, gateway txn ref + error code, "Reconciling" | checkout, payment-result, wallet, admin orders | `Orders.OrderCode/Gateway/GatewayTxnRef/GatewayErrorCode`, status `reconciling` (also blocks a new order) |
| UI-33 | "15 purchased − 7 used" | wallet, pricing | `vw_UserCreditSummary` |
| UI-35 | "Grading did not finish · Retry grading" (free) | speaking-review | `AiGradings.FailureReason` (only when failed), `AttemptCount` (retry reuses the row, max 5) |
| UI-36 | Speaking result never mixes skills | speaking-review | trigger: a `speaking_practice` exam holds speaking sections only, also on re-purpose |
| UI-37 | Placement free, speaking practice = 1 credit AI grading only | placement-*, speaking-instructions | trigger: `mock_exam_start` only on mock exams, no charge on placement attempts |

Added 2026-10-09: a **Query indexes** section at the end of the schema (36 indexes). SQL Server
does not index foreign keys, so every trigger join and history read was a scan.
The AI-grading worker reads `IX_AiGradings_Queue` / `IX_FreeResponses_Pending`.

## Fixed in the prototype (UI was wrong, not the schema)

- Mentor cards showed cash prices (50,000 ₫…) while booking charges 1 credit. Now "1 credit" (`mentor-list`, `mentor-detail`).
- `mentor-confirm`: "Balance after booking 8, from 9" contradicted the 8-credit top bar. Now 7 from 8.
- `admin/users`: "currently has 8, add 1 → 4 credits". Now 9.
- `weakness-analysis`: the "requires question-type labels" disclaimer no longer applies.

## Derivable, no column needed

Dashboard/admin totals (active students, revenue, most attempted test, avg score), best/average score,
"+40 vs previous attempt", per-Part correct counts, unanswered count, mentor rating (`vw_MentorAvgRating`),
balance, package sold/revenue, CEFR band ranges (`ScoreBands`), "oldest pending complaint".

## Left open on purpose

- **Re-recording history** (speaking gap 5): one response per question stays a decision.
- **Admin dashboard "Needs attention"** items: computed alerts (draft missing audio, reconciling > 30 min), not stored.
- **"45 hours studied"** on the dashboard: derivable only roughly from session/attempt start-end times; no time-on-task tracking was added.
- **Premium video lessons**: only a `VideoPath` per topic; no lesson-watch progress.
