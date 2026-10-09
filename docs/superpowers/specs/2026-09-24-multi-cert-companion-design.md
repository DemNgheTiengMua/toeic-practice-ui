# Multi-Certificate Mock-Exam Companion — Database Design

Date: 2026-09-24
Status: Design (approved section-by-section; pending full-spec review)
Supersedes the TOEIC-L&R-only schema in `docs/database-schema.sql`.

> Amended 2026-10-09 to match the schema after the screen audit
> (`docs/schema-ui-audit.md`); where they differ, the schema wins. Changes are
> tagged `UI-n` in the schema comments.

## Why this redesign

An advisor reviewed the pure database (the drawio ERD, not the UI) and raised
two structural objections:

1. **Features must be expressed in the schema, not hidden in backend logic.**
   The exam-import feature already exists in the product but is invisible in a
   pure schema read — so it must become tables, not code.
2. **The product must broaden** beyond a single TOEIC Listening & Reading exam.

The pivot, in one line: from a single-certificate high-stakes TOEIC L&R exam
into a **multi-certificate practice-and-mock-testing companion** that follows a
learner from a placement test, along a level-matched learning path, toward a
target band — monetised through mock exams, AI grading, and mentor bookings.

### Named changes (advisor + product owner)

- Remove **real/high-stakes exam ("thi thật")** entirely. Support only
  **practice (ôn)** and **mock testing (thi thử)**; monetise those.
- Remove **KYC / CCCD identity verification** — it is only mock testing. Keep
  ordinary login (email / password / OTP).
- Support **multiple certificate types** (not just TOEIC).
- Support **skills beyond Reading/Listening** (Speaking, Writing, …), each set
  matched to its certificate.
- **Placement test** stored in the DB, generating a **learning path** whose
  questions match the learner's level and progress upward.
- **Exam library** (thư viện đề).
- **Companion model**: after purchase, follow the learner closely and fulfil
  the learning path.
- **AI grading** (especially Speaking/pronunciation): score, recommend, and
  correct toward the **target band** — a new first-class feature.
- **Mentor booking** (human via Google Meet — the app only collects payment and
  provides the link — or AI mentor).
- **Reviews + complaints/ratings.**

### Hard constraints locked during brainstorming

- **Certificate isolation.** Each certificate is its own exam set + its own
  skill set. No mixing — a TOEIC exam cannot demand a Speaking answer if TOEIC
  has no Speaking skill. Enforced at the DB layer via foreign keys, not policy.
- **Placement selects the certificate first**, then tests, so the exam matches
  the skills that certificate actually has.
- **Parallel certificates.** A learner may pursue several certificates at once.
  Everything learner-scoped is keyed by `(UserId, CertId)` and persists
  independently. Switching the active certificate in the profile points at a
  different path; switching **back** keeps the old certificate's results intact.
  Results are lost only on an **explicit reset**.

## Domain map (10 domains)

1. Identity — login kept, KYC dropped
2. Catalog (NEW) — Certificates → Skills → Sections (data-driven)
3. Content — Exams, QuestionGroups, Questions (+DifficultyLevel), Options,
   ImportBatches + ImportLog; two answer branches (MCQ vs FreeResponse)
4. Scoring — per-certificate ScoreScales + ScoreBands (replaces hardcoded CEFR)
5. Payment — credit ledger kept, spend reasons added
6. Practice — generalised Part→Section, stays credit-free
7. MockExam — renamed from "thi thật"; dynamic per-skill scores; FreeResponses
8. AI Grading (NEW) — rubric-based grading of speaking/writing toward target band
9. LearningPath (NEW) — placement → level-matched path; companion; per (UserId,CertId)
10. Mentor (NEW) — book human/AI; app collects payment + Meet link; reviews + complaints

## Domain 1 — Identity

Keep ordinary login; drop KYC/CCCD entirely.

- `Roles` (Student, Admin) — unchanged.
- `Users` — keep hashed password; keep email/OTP login. Keep soft-delete
  (`IsDeleted`). **Drop** all CCCD/national-ID columns.
- `PasswordResetTokens` — unchanged.
- **Drop** `KycVerifications` and every KYC index/trigger. No identity gate
  precedes a charge anymore.

`Users` gains an **active-certificate pointer** (`ActiveCertId`, nullable FK to
Catalog.Certificates) — which certificate the profile is currently focused on.
Switching it never destroys another certificate's data (see Domain 9).

`Users` also carries `FullName`, `Phone`, `TermsAcceptedAt` (register/profile)
and `IsLocked`, `EmailVerifiedAt` (NULL = not activated), `LastLoginAt` (admin
users list). `EmailVerificationTokens` (same shape as `PasswordResetTokens`)
backs activation and the admin "resend email".

## Domain 2 — Catalog (NEW, data-driven)

Replaces the hardcoded Parts 1–7 with a three-level hierarchy. This is what
makes "multiple certificates, each with its own skills" a schema fact.

```
Certificates   CertId, Code(TOEIC/IELTS/VSTEP), Name, Description, IsActive
Skills         SkillId, CertId FK, Code, Name,
               Modality('listening'|'reading'|'speaking'|'writing'),
               DisplayOrder, DurationMinutes(NULL)
Sections       SectionId, SkillId FK, Code, Name, OptionCount(nullable),
               ExpectedQuestionCount(NULL), DisplayOrder
               ← fixed structure: "Part 3 — 39 questions", "Listening 45 min"
```

- **Certificate isolation is a foreign-key fact:** a Skill belongs to exactly
  one Certificate; a Section to exactly one Skill. TOEIC's skill set cannot
  leak a Speaking section unless TOEIC actually defines one.
- `Sections.OptionCount` generalises the old "Part 2 has 3 options, others 4"
  rule — per-section, data-driven, never hardcoded. NULL for non-MCQ sections
  (speaking/writing have no fixed option count).
- `Skills.Modality` drives the answer branch downstream: listening/reading →
  MCQ; speaking/writing → free response.

## Domain 3 — Content (Exams + Questions + Import)

```
Exams          ExamId, CertId FK, Name, Status(draft/published/hidden),
               Purpose('mock'|'placement'|'speaking_practice'), Description,
               SourceName, InternalNote(admin only), DurationMinutes,
               IsDeleted, CreatedAt, UpdatedAt
               ← filtered unique (CertId, Name) WHERE IsDeleted=0
ExamSectionAudio ExamId, SectionId, AudioPath, DurationSec, SizeBytes(≤60 MB),
               UploadedAt   ← one audio file per Listening Part; PK (ExamId, SectionId)
QuestionCategories CategoryId, CertId FK, Code, Name
               ← question-type labels ("Verb tense", "Part 7 inference")
QuestionGroups GroupId, ExamId FK, SectionId FK, Passage/AudioPath,
               AudioStartMs/AudioEndMs(slice of the Part file), ImagePath,
               ImageAlt(required with an image), Transcript, PrepSeconds,
               DisplayOrder
Questions      QuestionId, GroupId FK, Stem, DifficultyLevel(int 1-5 CHECK),
               QuestionType('mcq'|'free_response'), Explanation, CategoryId FK,
               PrepSeconds/ResponseSeconds(free_response only), DisplayOrder,
               UpdatedAt
               ← DifficultyLevel is a fixed 1-5 scale (1=easiest), not a free
                 int, so "match the level" is a schema fact; PathModuleItems and
                 placement use the same scale
QuestionOptions OptionId, QuestionId FK, Label, Text, IsCorrect, Rationale
               ← only for QuestionType='mcq'; at most one correct per question;
                 Rationale = why this option is wrong (Explanation stays the
                 general why-correct)
ImportBatches  BatchId, CertId FK, SourceName, Status(pending/done/failed),
               UploadedBy, CreatedAt, CompletedAt
ImportLog      LogId, BatchId FK, RowNo, Level(info/warn/error), Message
```

- **Exam import is now schema, not backend logic** (the advisor's core point):
  `ImportBatches` + `ImportLog` make ingest a first-class, queryable feature.
- An Exam belongs to one Certificate; every QuestionGroup names a Section of
  that certificate's skills — isolation flows down from Catalog.
- **Two answer branches:**
  - `QuestionType='mcq'` → answered against `QuestionOptions.IsCorrect`,
    auto-graded (Practice + MockExam MCQ).
  - `QuestionType='free_response'` → speaking/writing; no options; answered via
    `FreeResponses` (Domain 7) and graded by AI/mentor (Domain 8).
- `Exams.Purpose` separates placement tests and speaking-practice sets from
  mocks. Only `published` exams can be attempted; `hidden` takes an exam out of
  the library without deleting it.
- A `speaking_practice` exam holds only speaking sections (trigger on both
  `QuestionGroups` and a later `Purpose` change).
- `Questions.DifficultyLevel` is the hook the learning path uses to match
  questions to a learner's level (Domain 9).
- Correct answers/explanations are never sent to the client mid-exam
  (unchanged product rule); only Review and Practice expose them.

## Domain 4 — Scoring (per-certificate, replaces hardcoded CEFR)

The old schema hardcoded six CEFR bands for TOEIC's 10–990 total. IELTS uses
0–9, VSTEP uses bậc — so bands must be per-certificate data.

```
ScoreScales  ScaleId, CertId FK, SkillId(NULL=total), ExamId(NULL=default),
             RawScore, ScaledScore
             ← raw→scaled: admin-editable per exam (difficulty varies per exam)
ScoreBands   BandId, CertId FK, Code, Name, MinTotal, MaxTotal, DisplayOrder
             UNIQUE (CertId, Code)  ← so TargetBands/snapshots can reference by code
             ← scaled→band: fixed standard, admin CANNOT edit; snapshot at grading
```

- **Two-step conversion, two owners** (product rule preserved): `raw→scaled`
  varies by exam and is admin-editable; `scaled→band` is fixed standard data,
  read-only to admin — otherwise two learners with the same score land in
  different bands.
- Bands are **per certificate**: TOEIC→CEFR, IELTS→0–9, VSTEP→bậc. The band
  `Code` is snapshotted into results at grading time so history stays stable
  even if the catalog changes later.

## Domain 5 — Payment (credit ledger kept, spend reasons added)

Keep the append-only ledger: **wallet balance = SUM over CreditTransactions**,
never a stored column. Keep the no-negative-balance trigger, the
one-open-order-per-user filtered index (`pending` or `reconciling` blocks a new
order), and the stale-pending-order expiry job.

```
Packages     PackageId, Name, Credits, Price, IsActive, IsFeatured(one only),
             Features(bullet list), DisplayOrder
Orders       OrderId, UserId, PackageId, Status(pending/reconciling/paid/
             failed/expired), Amount, OrderCode(unique), Gateway('vnpay'|'momo'),
             GatewayTxnRef, GatewayErrorCode, CreatedAt, PaidAt
             ← reconciling = gateway has not returned a final result;
               (Gateway, GatewayTxnRef) unique; a credited order stays paid
```

`CreditTransactions.Reason` expands, each debit paired with a provenance column
(extending the existing source/sign CHECK pattern):

| Direction | Reason | Source column |
|-----------|--------|---------------|
| Credit (+) | `purchase` | OrderId |
| Credit (+) | `admin_grant` | (admin: AdminUserId + Note) |
| Credit (+) | `booking_cancel` | BookingId (refund of a cancelled booking) |
| Debit (−) | `mock_exam_start` | AttemptId (only on a `mock` exam) |
| Debit (−) | `ai_grading` | AttemptId (one charge covers all answers in the attempt; `mock` or `speaking_practice` exams only) |
| Debit (−) | `mentor_booking` | BookingId (Delta = −`PriceCredits`) |
| Debit (−) | `premium_path` | LearnerPathId |
| Revoke (−) | `admin_revoke` | (admin: AdminUserId + Note) |

- **Admin adjustments name their actor.** `admin_grant`/`admin_revoke` require
  `AdminUserId` and a `Note` (the stated reason); every other reason has no
  `AdminUserId`.
- **Charges are pinned to exam purpose** (trigger): a placement attempt carries
  no charge at all.
- **Free stays free at the data layer:** practice, weakness analysis, and the
  placement test never write a `CreditTransactions` row. That is what makes
  them free — a data fact, not an application policy.
- `vw_UserCreditSummary` derives balance, credits purchased and credits used
  (wallet "15 purchased − 7 used"); a `booking_cancel` refund un-uses its charge.

## Domain 6 — Practice (generalised, credit-free)

```
PracticeSessions  SessionId, UserId, SectionId, QuestionCount, Status,
                  CorrectCount, QuestionSource('all'|'wrong_before'|'unseen'),
                  FeedbackMode('immediate'|'end'), LearnerPathStepId(NULL) FK,
                  StartedAt, FinishedAt
                  ← LearnerPathStepId: the path module step this session serves
PracticeAnswers   SelectedOptionId, IsCorrect, DisplayOrder
SavedQuestions    UserId, QuestionId, Source('manual'|'incorrect'), SavedAt
                  ← PK (UserId, QuestionId): saved questions + incorrect-answer bank
```

- Practice is by **Section** (was PartNumber). Immediate per-question grading —
  which structurally requires a correct option, so practice is **MCQ-only**.
- Speaking/writing have no self-gradable answer; practising them is an
  `ExamAttempt` on a `speaking_practice` exam graded through the paid
  AI-grading path (Domain 8), not here.
- Touches no ledger row → free by construction.
- Weakness analysis reads `vw_UserSectionAccuracyAll` and
  `vw_UserCategoryAccuracy`, which union practice answers with graded exam
  answers.

## Domain 7 — MockExam (renamed from "thi thật")

```
ExamAttempts      AttemptId, UserId, ExamId, Status(in_progress/submitted/
                  graded/abandoned), StartedAt, ExpiresAt(server-authoritative),
                  IsAutoSubmitted, SubmittedAt, LastQuestionId(resume, pinned
                  to the exam), TotalScore, BandCode(snapshot)
AttemptSkillScores AttemptId, SkillId, RawScore, ScaledScore
                  ← dynamic per-skill scores (TOEIC 2, IELTS 4), replaces the
                    four hardcoded Listening/Reading columns
AttemptAnswers    SelectedOptionId, IsCorrect, IsFlagged  ← MCQ, graded at submit
FreeResponses     AttemptId, QuestionId, ResponseText / AudioPath, DurationMs,
                  Status(pending_ai/graded), SubmittedAt
                  ← speaking/writing answers, await AI grading (Domain 8);
                    UNIQUE (AttemptId, QuestionId): a re-record overwrites, no
                    take history (deliberate)
```

- One table serves three exam purposes: `mock`, `placement` (free, linked from
  `PlacementTests`) and `speaking_practice`.
- **Credit deducted at mock-exam start** (`mock_exam_start`, `mock` exams
  only) — this replaces the old real-exam charge point.
- A `speaking_practice` attempt never carries a `BandCode`: it covers one skill
  of a summed scale, so a certificate band would be meaningless.
- **KYC trigger dropped.** Kept: "published exams only" and "non-negative
  balance" triggers.
- Scores are **per-skill dynamic** (`AttemptSkillScores`) because IELTS has four
  skills and VSTEP differs. `TotalScore` remains the SUM of scaled skill scores,
  preserving the total-matches-components constraint.
- An attempt containing free-response questions cannot reach `graded` on submit;
  completion waits on the AI (or mentor) pass.
- Timer stays server-authoritative; correct answers never reach the client
  mid-exam (unchanged product rules).

## Domain 8 — AI Grading (NEW)

Grades free-response answers against a rubric and recommends corrections toward
the target band.

```
AiGradings      GradingId, FreeResponseId FK, SkillId, Status(pending/done/
                failed), OverallScaled, BandCode(snapshot), Model,
                FailureReason(NULL), AttemptCount, RequestedAt, CompletedAt
                ← one grading row per free-response answer (grading is still
                  per-answer), but NOT charged per row — see charging note below;
                  a retry reuses the same row (AttemptCount++)
GradingCriteria CriterionId, CertId FK, SkillId FK, Code, Name, MaxScore,
                Weight, DisplayOrder
                ← rubric per (certificate, skill): IELTS Speaking =
                  Fluency/Lexical/Grammar/Pronunciation; TOEIC has none → none generated
SectionCriteria SectionId, CriterionId, MaxScore(NULL = criterion's own)
                ← which of the skill's criteria apply to a section, with an
                  optional per-section ceiling (TOEIC Speaking Q1-10 score 0-3,
                  Q11 0-5). A section with no rows accepts every criterion.
AiGradingScores GradingId FK, CriterionId FK, Score, Comment
                ← score per rubric criterion
Recommendations RecId, UserId, CertId, GradingId(NULL if aggregate),
                AttemptId(NULL, owner FK), SkillId, Text, Severity,
                TargetBandCode, IsResolved, CreatedAt
                ← AttemptId: "To reach B2: …" on that attempt's result
                ← "what to fix to reach the target band"; can feed the path (Domain 9)
```

- **Rubric is data, not code.** Each `(cert, skill)` owns its criteria, keyed by
  FK, so TOEIC never produces Speaking criteria it doesn't have.
- Grading writes `AiGradingScores` per criterion, then emits `Recommendations`
  carrying a `TargetBandCode` so corrections aim at the right band.
- **Charged once per attempt, not per answer.** A single `ai_grading` debit
  (source `AttemptId`) covers grading every free-response answer in that attempt,
  so an 8-question Writing+Speaking attempt is one charge, not eight. The
  per-answer `AiGradings` rows remain for grading detail; billing groups them by
  attempt. (Standalone speaking practice is an attempt on a `speaking_practice`
  exam: one `ai_grading` debit, no `mock_exam_start`.)
- **Retry is free.** A `failed` grading records `FailureReason` (only a failed
  row carries one); a retry sets the same row back to `pending` and bumps
  `AttemptCount` (max 5). The one-`ai_grading`-per-attempt unique index
  makes a second charge impossible, so no refund path is needed.
- A rubric score cannot exceed its ceiling (`SectionCriteria.MaxScore`, else
  `GradingCriteria.MaxScore`), and a section that lists its criteria accepts
  only those (trigger). Neither ceiling can be lowered below a stored score.
- When a grading reaches `done`, its `FreeResponses.Status` → `graded`; only
  then can the parent attempt reach `graded`.
- Grading failures can be raised as a `grading_issue` complaint on the attempt
  (Domain 10).

## Domain 9 — LearningPath (NEW, companion)

```
PlacementTests   PlacementId, UserId, CertId FK, ExamId, Status(in_progress/
                 done/abandoned), ResultBandCode(snapshot), AttemptId(NULL,
                 unique, owner FK), TotalScore(snapshot), TakenAt
                 ← placement is FREE; SELECT CERT FIRST, then test. ExamId
                   must be a Purpose='placement' exam of that cert (FK). The
                   answers/timer/skill scores live in the linked ExamAttempt,
                   which carries no charge.
TargetBands      UserId, CertId FK, TargetBandCode FK, SetAt
                 ← PK (UserId, CertId): one target per cert, many certs in parallel
PathTemplates    TemplateId, CertId FK, FromBandCode, ToBandCode, Name
PathModules      ModuleId, TemplateId FK, SkillId, DisplayOrder, Title,
                 GroupName, Description, EstimatedMinutes
PathModuleItems  ItemId, ModuleId FK, ItemType('practice_section'|'mock_exam'|
                 'ai_task'|'mentor'),
                 RefSectionId FK->Sections NULL, RefExamId FK->Exams NULL,
                 DifficultyLevel, QuestionCount, IsPremiumOnly, DisplayOrder
                 + CHECK: the ref column matching ItemType is non-null, others
                   null (practice_section->RefSectionId, mock_exam->RefExamId;
                   ai_task/mentor carry no ref, resolved to a grading/booking at
                   runtime)
PathModuleTopics TopicId, ModuleId FK, Title, CategoryId FK NULL, QuestionCount,
                 VideoPath(premium), DisplayOrder
                 ← "Topics covered"; topic status derives from practice answers
                   in that CategoryId
LearnerPaths     LearnerPathId, UserId, CertId FK, TemplateId, CurrentBandCode,
                 PlacementId(NULL, owner FK), Status(active/completed/reset),
                 StartedAt
                 ← filtered unique (UserId, CertId) WHERE Status='active':
                   one active path per cert; PlacementId = "built from your
                   placement on …"
LearnerPathSteps StepId, LearnerPathId FK, PathModuleItemId, Status(locked/
                 available/in_progress/done), Score, StartedAt, CompletedAt
                 ← module progress/accuracy/time derive from the
                   PracticeSessions linked to the step
```

- **Flow:** select cert → placement (free) → band → generate a `LearnerPath`
  from the `PathTemplate` matching `FromBand→ToBand` → unlock steps upward. The
  companion follows the learner along these steps.
- **Parallel certificates:** everything keyed by `(UserId, CertId)`, independent.
  Switching the active cert points at a different path; switching **back** leaves
  the old `LearnerPath`, its steps, and its `PlacementTest` intact.
- **Explicit reset:** a reset action sets `LearnerPaths.Status='reset'` (soft),
  re-enabling placement for that cert. Without a reset, results are immutable.
- **Level progression:** `PathModuleItems.DifficultyLevel` links to
  `Questions.DifficultyLevel` (Domain 3), so path content matches the level.
- `Recommendations` (Domain 8) can spawn an `ai_task` step, closing the
  grade → correct → practice loop.
- **Free vs premium path.** Placement and the basic generated path are free.
  A learner may upgrade to a premium path (richer modules / closer companion
  guidance), charged once as `premium_path` (Domain 5, source `LearnerPathId`).
  The upgrade is the only ledger write in this domain; the path structure itself
  is identical either way, except items flagged `IsPremiumOnly` (and topic
  videos), which only a premium path opens. "Is premium" is derived from the
  `premium_path` charge (`vw_LearnerPathIsPremium`), never stored.

## Domain 10 — Mentor (NEW)

```
MentorProfiles  MentorId, UserId(NULL for AI), DisplayName, Bio, Headline,
                Expertise, CertId FK, SkillId, Type('human'|'ai'),
                PricePerSlot(cash, payout basis), PriceCredits(learner charge),
                SessionMinutes, IsActive
                ← AvgRating is a view, never a stored column
MentorSlots     SlotId, MentorId FK, StartAt, EndAt, Status(open/booked/closed)
                ← only human mentors need scheduled slots
Bookings        BookingId, UserId, MentorId, MentorType, SlotId(NULL if AI),
                Status(pending/confirmed/done/cancelled), MeetLink, FocusArea,
                CancelledAt, CreatedAt
                ← charged 'mentor_booking' (= −PriceCredits) on create; app
                  stores only the link
Reviews         ReviewId, BookingId FK, UserId, Rating(1-5), Text, CreatedAt
Complaints      ComplaintId, BookingId | AttemptId | OrderId (exactly one),
                UserId, Type('mentor_no_show'|'mentor_quality'|'grading_issue'|
                'technical_error'|'payment_issue'), Reason, Status(open/
                reviewing/resolved), Resolution, InternalNotes(admin only),
                ResolvedByUserId, ResolvedAt, ResolvedByGrantTxnId(NULL) FK,
                CreatedAt
MentorPayouts   PayoutId, MentorId FK, PeriodStart, PeriodEnd, Status(pending/
                paid), PaidAt
                ← BookingCount/GrossAmount derived by vw_MentorPayoutTotals
MentorPayoutItems PayoutId FK, BookingId FK   ← which bookings a payout settles
```

- **The app does not host sessions.** It collects the `mentor_booking` credit
  and stores a `MeetLink`. Humans meet on Google Meet; AI bookings have
  `SlotId` NULL.
- Humans need `MentorSlots`; a filtered unique index on `Bookings(SlotId) WHERE
  Status<>'cancelled'` prevents double-booking, and a trigger keeps the slot's
  `Status` in sync. AI books without a slot.
- **Cancelling refunds the credit.** A cancelled booking may post one
  `booking_cancel` credit equal to its charge (trigger); the 24-hour window is
  read from `CancelledAt` against the slot.
- **Reviews attach only to a `BookingId`.** **Complaints attach to exactly one
  subject**: a booking, an exam attempt, or an order, and the `Type` must match
  it (mentor types → booking, `grading_issue`/`technical_error` → attempt,
  `payment_issue` → order). Each subject FK proves the complainant owns it.
  `AvgRating` is a view, never stored — same ledger-style discipline as the
  wallet.
- **Mentor payout (settlement).** Learner spending is a `mentor_booking` debit on
  the learner's wallet; paying the human mentor is the other side. `MentorPayouts`
  batches confirmed/done bookings into a settlement period, and `MentorPayoutItems`
  records exactly which bookings each payout covers — so mentor income is
  reconcilable, not implicit. Payout amount derives from `PricePerSlot` × settled
  bookings; the actual outbound transfer happens outside the app (the app records
  the obligation, it does not move money to mentors).
- **Complaint resolution can refund.** A `resolved` complaint (e.g. mentor
  no-show, failed grading) may issue a credit refund by writing an `admin_grant`
  transaction (with the admin's `AdminUserId` and a `Note`) and linking it via
  `Complaints.ResolvedByGrantTxnId` — so a resolved complaint is
  never a dead end, and the refund is auditable through the same ledger. General
  cash refunds remain out of scope; this is a credit make-good.

## Cross-cutting conventions (carried from the current schema)

- `DATETIMEOFFSET` for timestamps; `VARCHAR` + CHECK for enum-like states.
- Soft-delete via `IsDeleted` where content must survive references.
- Snapshot pattern for scores/bands so history is stable against catalog edits.
- Aggregates (wallet balance, mentor rating) are always SUM/AVG over rows,
  never stored counters.
- `QUOTED_IDENTIFIER` + `ANSI_NULLS` ON (required for filtered indexes).
- SQL Server does not index foreign keys, so a closing "Query indexes" section
  adds non-unique `IX_*` indexes on FK joins the triggers run and on hot screen
  reads (history by user, exam list, path modules, admin queues). They enforce
  nothing. The AI-grading worker queue reads filtered indexes on
  `AiGradings(Status, RequestedAt) WHERE Status IN ('pending','failed')` and
  `FreeResponses WHERE Status='pending_ai'`.

## Out of scope for this design

- The concrete import file format (still undecided per PRODUCT.md).
- UI/UX — deferred by the product owner until the database is correct.
- Cash refunds (a resolved complaint may issue a *credit* make-good via
  `admin_grant`, but money is never returned to a card/bank); class / cohort /
  teacher concepts; the outbound money transfer to mentors (the app records the
  payout obligation, a finance process settles it).

## Migration note

This is a redesign of `docs/database-schema.sql`, not an in-place migration.
The new schema and the follow-on updates to `PRODUCT.md` and
`docs/toeic-user-flow.md` are explicitly sequenced **after** this design is
approved; they are not part of this document.
