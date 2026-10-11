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

## Final pass — 2026-10-10

| Tag | What the screen needs | Pages | Change |
|---|---|---|---|
| UI-45 | A writing/speaking result is rubric-only, never "out of 990"; placement card meta "40 questions · 30 minutes" | writing-review, speaking-review, placement-select | `trg_Attempts_NoBandForSpeakingPractice` also rejects `TotalScore` on skill-practice attempts; `Exams.QuestionCount` |
| UI-46 | Placement snapshot must trace to the attempt's own scores; a done placement carries band + total + attempt | placement-result | `trg_PlacementTests_SnapshotMatchesAttempt`, `CK_Placement_DoneHasResult` |
| UI-44 (fixed) | `ai_task` path item must open the writing/speaking practice exam it names — `CK_PMI_RefMatchesType` forced `RefExamId` NULL | path-detail "Write it" task | CHECK allows `RefExamId` for `ai_task`; `trg_PMI_AiTaskRefsSkillPracticeExam` pins it to a `writing_practice`/`speaking_practice` exam (never a mock or placement) |
| UI-33 | Wallet stat "Total purchased 548,000 ₫ · 2 paid orders" (money, not credits) | wallet | `vw_UserPurchaseTotals` |

Second-pass UI fixes (found by re-reading every modified page plus the five the first pass deferred):
- `exam-listening`: sidebar claimed "31/100 questions" while the stepper showed Part 1 done (6) and Q8 current — the count is 7. The flag moved from Q4 (inside the locked Part 1, unreachable) to Q7.
- `practice-select`: "Questions per session → All sections" is not representable — a session is per-section by FK. Now "All questions in this part".
- `path-detail`: the premium AI writing task sat inside the topics table, but `PathModuleTopics` has no premium/type columns (premium lives on `PathModuleItems`). It is now its own "AI-graded task" block.
- `placement-take`: Q12/40 with 28:45 left of 30:00 was implausible pacing. Now 21:38.
- `exam-speaking`: "Finish and submit" rendered even in the prepare state, before anything is recorded. Now it appears from the first saved recording.

## Fixed in the prototype (UI was wrong, not the schema)

- Mentor cards showed cash prices (50,000 ₫…) while booking charges 1 credit. Now "1 credit" (`mentor-list`, `mentor-detail`).
- `mentor-confirm`: "Balance after booking 8, from 9" contradicted the 8-credit top bar. Now 7 from 8.
- `admin/users`: "currently has 8, add 1 → 4 credits". Now 9.
- `weakness-analysis`: the "requires question-type labels" disclaimer no longer applies.
- `wallet` / `pricing`: "used 7, all on full tests" contradicts the credit model (AI grading and mentors also spend credits). Now "full tests, AI gradings, mentor sessions".
- `learning-path`: Module 4 said "Reading · 50 terms" — modules are practice sessions, which the schema counts in questions. Now "Practice · 50 questions".
- `register`: no phone field although the schema stores `Users.Phone` and profile collects it. Added.
- `exam-writing`: the word-meter tick said "300 min" (a word count, not minutes). Now "min 300" (matching "150 max").
- `writing-review`: "Questions 1–5 out of 3, 6–7 out of 4" read as totals (3 and 4) that contradict 21/28. Now "out of 3 each / 4 each".
- `practice-summary`: the stat "15 out of 20 · 75%" contradicted the skip rule shown on the same page ("not counted in the rate") — 15/(15+4) is 79%. Now "out of 19 answered · 79%", and the skip explanation no longer says blank answers count as incorrect.

## Feature additions — 2026-10-10

Screens added to close functional gaps; each maps to existing tables, no schema change needed.

| Tag | Screen | Backing data |
|---|---|---|
| UI-47 | `student/saved-questions.html` — the incorrect-answer bank / saved-question list that practice-take and practice-summary already promised ("Save this question", "saved to your incorrect-answer bank") | `SavedQuestions (Source 'manual'/'incorrect')`, part filter from `Sections`, type filter from `QuestionCategories` |
| UI-48 | `student/support-report.html` — learner files a complaint; type selector mirrors `CK_Complaints_TypeMatchesSubject` (mentor→booking, exam→attempt, payment→order) | `Complaints` |
| UI-49 | `student/support-history.html` — learner tracks open/resolved reports and reads `Resolution` (never `InternalNotes`) | `Complaints.Status/Resolution` |
| UI-50 | `certificates.html` — per-certificate structure & band explainer; landing certificate cards link here | `Certificates`, `Skills.DurationMinutes`, `Sections.QuestionCount`, `ScoreBands` |
| UI-51 | dashboard email-verification nudge ("Resend email", dismissible) | `Users.EmailVerifiedAt`, `EmailVerificationTokens` |

Also: "Save this question for review" buttons and the practice-summary stat now link to the bank;
booking-detail and writing-review offer "Report a problem" / "Dispute this grading";
admin complaints page links back to the learner form.

## Derivable, no column needed

Dashboard/admin totals (active students, revenue, most attempted test, avg score), best/average score,
"+40 vs previous attempt", per-Part correct counts, unanswered count, mentor rating (`vw_MentorAvgRating`),
balance, package sold/revenue, CEFR band ranges (`ScoreBands`), "oldest pending complaint".

## Left open on purpose

- **Re-recording history** (speaking gap 5): one response per question stays a decision.
- **Admin dashboard "Needs attention"** items: computed alerts (draft missing audio, reconciling > 30 min), not stored.
- **"45 hours studied"** on the dashboard: derivable only roughly from session/attempt start-end times; no time-on-task tracking was added.
- **Premium video lessons**: only a `VideoPath` per topic; no lesson-watch progress.
