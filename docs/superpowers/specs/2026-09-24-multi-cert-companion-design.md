# Multi-Certificate Mock-Exam Companion — Database Design

Date: 2026-09-24
Status: Design (approved section-by-section; pending full-spec review)
Supersedes the TOEIC-L&R-only schema in `docs/database-schema.sql`.

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

## Domain 2 — Catalog (NEW, data-driven)

Replaces the hardcoded Parts 1–7 with a three-level hierarchy. This is what
makes "multiple certificates, each with its own skills" a schema fact.

```
Certificates   CertId, Code(TOEIC/IELTS/VSTEP), Name, Description, IsActive
Skills         SkillId, CertId FK, Code, Name,
               Modality('listening'|'reading'|'speaking'|'writing'),
               DisplayOrder
Sections       SectionId, SkillId FK, Code, Name, OptionCount(nullable),
               DisplayOrder
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
Exams          ExamId, CertId FK, Name, Status(draft/published/archived),
               DurationMinutes, IsDeleted, CreatedAt
QuestionGroups GroupId, ExamId FK, SectionId FK, Passage/AudioPath, DisplayOrder
Questions      QuestionId, GroupId FK, Stem, DifficultyLevel(int),
               QuestionType('mcq'|'free_response'), DisplayOrder
QuestionOptions OptionId, QuestionId FK, Label, Text, IsCorrect
               ← only for QuestionType='mcq'
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
one-pending-order-per-user filtered index, and the stale-pending-order expiry
job.

`CreditTransactions.Reason` expands, each debit paired with a provenance column
(extending the existing source/sign CHECK pattern):

| Direction | Reason | Source column |
|-----------|--------|---------------|
| Credit (+) | `purchase` | OrderId |
| Credit (+) | `admin_grant` | (admin) |
| Debit (−) | `mock_exam_start` | AttemptId |
| Debit (−) | `ai_grading` | GradingId |
| Debit (−) | `mentor_booking` | BookingId |
| Debit (−) | `premium_path` | LearnerPathId |
| Revoke (−) | `admin_revoke` | (admin) |

- **Free stays free at the data layer:** practice, weakness analysis, and the
  placement test never write a `CreditTransactions` row. That is what makes
  them free — a data fact, not an application policy.

## Domain 6 — Practice (generalised, credit-free)

```
PracticeSessions  SessionId, UserId, SectionId, QuestionCount, Status,
                  CorrectCount, StartedAt, FinishedAt
PracticeAnswers   SelectedOptionId, IsCorrect, DisplayOrder
```

- Practice is by **Section** (was PartNumber). Immediate per-question grading —
  which structurally requires a correct option, so practice is **MCQ-only**.
- Speaking/writing have no self-gradable answer; practising them routes through
  the paid AI-grading path (Domain 8), not here.
- Touches no ledger row → free by construction.

## Domain 7 — MockExam (renamed from "thi thật")

```
ExamAttempts      AttemptId, UserId, ExamId, Status(in_progress/submitted/
                  graded/abandoned), StartedAt, ExpiresAt(server-authoritative),
                  IsAutoSubmitted, TotalScore, BandCode(snapshot)
AttemptSkillScores AttemptId, SkillId, RawScore, ScaledScore
                  ← dynamic per-skill scores (TOEIC 2, IELTS 4), replaces the
                    four hardcoded Listening/Reading columns
AttemptAnswers    SelectedOptionId, IsCorrect  ← MCQ, graded at submit
FreeResponses     AttemptId, QuestionId, ResponseText / AudioPath,
                  Status(pending_ai/graded), SubmittedAt
                  ← speaking/writing answers, await AI grading (Domain 8)
```

- **Credit deducted at mock-exam start** (`mock_exam_start`) — this replaces the
  old real-exam charge point.
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
                RequestedAt, CompletedAt
                ← one grading per free-response answer; charged 'ai_grading' on create
GradingCriteria CriterionId, CertId FK, SkillId FK, Code, Name, MaxScore,
                Weight, DisplayOrder
                ← rubric per (certificate, skill): IELTS Speaking =
                  Fluency/Lexical/Grammar/Pronunciation; TOEIC has none → none generated
AiGradingScores GradingId FK, CriterionId FK, Score, Comment
                ← score per rubric criterion
Recommendations RecId, UserId, CertId, GradingId(NULL if aggregate), SkillId,
                Text, Severity, TargetBandCode, IsResolved, CreatedAt
                ← "what to fix to reach the target band"; can feed the path (Domain 9)
```

- **Rubric is data, not code.** Each `(cert, skill)` owns its criteria, keyed by
  FK, so TOEIC never produces Speaking criteria it doesn't have.
- Grading writes `AiGradingScores` per criterion, then emits `Recommendations`
  carrying a `TargetBandCode` so corrections aim at the right band.
- Charged `ai_grading` at `AiGradings` creation (source column from Domain 5).
- When a grading reaches `done`, its `FreeResponses.Status` → `graded`; only
  then can the parent attempt reach `graded`.

## Domain 9 — LearningPath (NEW, companion)

```
PlacementTests   PlacementId, UserId, CertId FK, ExamId, Status,
                 ResultBandCode(snapshot), TakenAt
                 ← placement is FREE; SELECT CERT FIRST, then test
TargetBands      UserId, CertId FK, TargetBandCode FK, SetAt
                 ← PK (UserId, CertId): one target per cert, many certs in parallel
PathTemplates    TemplateId, CertId FK, FromBandCode, ToBandCode, Name
PathModules      ModuleId, TemplateId FK, SkillId, DisplayOrder, Title
PathModuleItems  ItemId, ModuleId FK, ItemType('practice_section'|'mock_exam'|
                 'ai_task'|'mentor'), RefId, DifficultyLevel, DisplayOrder
LearnerPaths     LearnerPathId, UserId, CertId FK, TemplateId, CurrentBandCode,
                 Status(active/completed/reset), StartedAt
                 ← filtered unique (UserId, CertId) WHERE Status='active':
                   one active path per cert
LearnerPathSteps StepId, LearnerPathId FK, PathModuleItemId, Status(locked/
                 available/done), Score, CompletedAt
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
  is identical either way.

## Domain 10 — Mentor (NEW)

```
MentorProfiles  MentorId, DisplayName, Bio, CertId FK, SkillId, Type('human'|
                'ai'), PricePerSlot, IsActive
                ← AvgRating is a view, never a stored column
MentorSlots     SlotId, MentorId FK, StartAt, EndAt, Status(open/booked/closed)
                ← only human mentors need scheduled slots
Bookings        BookingId, UserId, MentorId, SlotId(NULL if AI), Status(pending/
                confirmed/done/cancelled), MeetLink, CreatedAt
                ← charged 'mentor_booking' on create; app stores only the link
Reviews         ReviewId, BookingId FK, UserId, Rating(1-5), Text, CreatedAt
Complaints      ComplaintId, BookingId FK, UserId, Reason, Status(open/reviewing/
                resolved), Resolution, CreatedAt
```

- **The app does not host sessions.** It collects the `mentor_booking` credit
  and stores a `MeetLink`. Humans meet on Google Meet; AI bookings have
  `SlotId` NULL.
- Humans need `MentorSlots`; a filtered unique index on `SlotId WHERE
  Status='booked'` prevents double-booking. AI books without a slot.
- **Reviews and complaints attach only to a `BookingId`** — reviews are scoped to
  mentors/bookings, not exams or the system. `AvgRating` is a view, never
  stored — same ledger-style discipline as the wallet.

## Cross-cutting conventions (carried from the current schema)

- `DATETIMEOFFSET` for timestamps; `VARCHAR` + CHECK for enum-like states.
- Soft-delete via `IsDeleted` where content must survive references.
- Snapshot pattern for scores/bands so history is stable against catalog edits.
- Aggregates (wallet balance, mentor rating) are always SUM/AVG over rows,
  never stored counters.
- `QUOTED_IDENTIFIER` + `ANSI_NULLS` ON (required for filtered indexes).

## Out of scope for this design

- The concrete import file format (still undecided per PRODUCT.md).
- UI/UX — deferred by the product owner until the database is correct.
- Refunds; class/cohort/teacher concepts.

## Migration note

This is a redesign of `docs/database-schema.sql`, not an in-place migration.
The new schema and the follow-on updates to `PRODUCT.md` and
`docs/toeic-user-flow.md` are explicitly sequenced **after** this design is
approved; they are not part of this document.
