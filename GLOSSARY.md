# Extra Efficiency Companion

Self-directed certification prep platform providing diagnostic practice, simulated mock exams, AI-driven free-response grading, and mentor bookings.

## Language

**Credit**:
The internal currency unit consumed to access premium learning actions such as mock exams, AI grading, and mentor sessions. Standard conversion rate is a flat 20,000 VND = 1 Credit.
_Avoid_: Token, point, coin, balance unit

**Credit Ledger**:
The append-only record of all credit adjustments, purchases, debits, and refunds for each learner.
_Avoid_: Credit database, transaction table, wallet balance table

**Order**:
A formal purchase record for credits via an integrated payment gateway (`payos` or `mock`). Each order specifies the quantity of credits purchased (`Amount = Credits * 20,000 VND`), a unique numeric merchant `PaymentNo` (for VietQR/payOS), and transitions through `pending` -> `paid` (or `cancelled`, `expired`, `failed`).
_Avoid_: Invoice entity, cart checkout, transaction slip

**PayOS Embedded Checkout**:
The in-app payment experience using `@payos/payos-checkout` (`embedded: true`) rendering dynamic VietQR code frames directly within the web app, eliminating external tab redirection and capturing `onSuccess`, `onCancel`, and `onExit` events.
_Avoid_: Pop-out checkout tab, external payment redirect, static QR image

**Context-Aware Return URL**:
The sanitized internal path (`returnUrl`, e.g., `/learning-path` or `/mentors`) where a learner is automatically redirected after a successful credit purchase.
_Avoid_: Open redirect, external callback URL

**Milestone Learning Sprint**:
A 4-week modular curriculum focused on achieving the immediate next score band (`SprintTargetBand`) towards a learner's overarching goal. Non-expiring, self-paced, and verified via an Exit Mock Exam.
_Avoid_: Fixed 28-day countdown course, calendar-locked syllabus, multi-month monolithic path

**Block Before Charge**:
The domain invariant requiring that a learner's available credit balance and all action preconditions are verified before debiting credits.
_Avoid_: Pre-authorization, credit check

**Transaction Receipt**:
The immutable confirmation generated upon posting a ledger transaction, reflecting the applied delta and resulting wallet balance.
_Avoid_: Transaction record, receipt entity

**Attempt Engine**:
The deep module that encapsulates the exam sitting lifecycle: countdown timers, expiration auto-submit, answer validation and persistence, question navigation, two-step score scaling, and CEFR band scale matching.
_Avoid_: Exam engine, test runner, attempt manager

**Certificate Band**:
A named score range defined on a certificate's reporting scale (e.g., TOEIC 550-780, IELTS 6.5, VSTEP B2). Both placement and mock attempts map their total scaled score directly to a certificate band.
_Avoid_: Separate CEFR axis, grade, tier, rank

**Placement Exam**:
The single universal diagnostic exam (`purpose = placement`) on the platform that runs through the standard Attempt Engine to measure a learner's starting proficiency and per-skill/topic accuracy before they commit to a certificate track. Learners choose either a 2-skill scope (`listening_and_reading`, graded immediately) or a 4-skill scope (`all_skills`, including AI-graded Speaking and Writing). The first sitting is free; subsequent retakes charge credits by scope.
_Avoid_: Per-certificate placement paper, two-stage adaptive draw, placement bundle

**Learner OOBE**:
The guided Out-Of-Box Experience wizard (`Sit Universal Placement [2 or 4 skills] -> Wait for Grading & View Diagnostic Result -> Choose Certificate Track [e.g., 2-skill TOEIC or 4-skill IELTS], Target Band & Study Time -> Generate 5-Credit Learning Path`) shown to learners who have not yet completed placement and goal setup. Supports a temporary session-only skip (`Explore for now`) to browse Mentors and Wallet while displaying a persistent return banner and friendly soft-lock empty states on gated learning pages (`Practice`, `Learning Path`, `Weakness Analysis`).
_Avoid_: Permanent onboarding skip, unguided cold dashboard, choosing certificate before placement


**Mock Exam**:
A full-length published exam (`purpose = mock`) for a certificate that charges credits to start and produces per-skill scaled scores, a total scaled score, and a Certificate Band.
_Avoid_: Test, paper

**Practice Session**:
A free, untimed review drill unlocked only for topics or sections the learner has already completed (`done`) in their Learning Path. Practice carries no server-side resume state (`in_progress` / `abandoned` tracking is not used; only Mock and Placement attempts support session resume). Learners see completed vs. locked status directly on the catalog UI, and only finished practice outcomes are recorded for accuracy metrics.
_Avoid_: Resumable practice sitting, unlocked cold drill, cell matrix drill, quiz

**Audio Equipment Check**:
The diagnostic routine verifying microphone input level and speaker/headphone playback before sitting an attempt or practice session involving listening or speaking skills.
_Avoid_: Sound settings, mic wizard, audio test screen

**Exam Shell**:
The persistent container maintaining the topbar, countdown timer, audio playback, and navigator grid during an exam sitting without full-page reloads.
_Avoid_: Exam container, test layout, test wrapper

**Optimistic Answer Queue**:
The client-side buffer during an attempt that applies answer selections immediately to local state and synchronizes them asynchronously with the server.
_Avoid_: Auto-save buffer, pending answer list

**Exam Integrity Guard**:
The defensive suite enforcing exam room security by preventing history navigation escapes, tab switching, copy-paste tampering, and clock manipulation during a sitting.
_Avoid_: Anti-cheat module, proctor script, lock screen

**Exam Audio Player**:
The single-play, non-pausable, non-seekable audio playback control used during standardized mock exam sittings and diagnostic placement tests to replicate test hall conditions.
_Avoid_: Test audio, exam media player

**Study Audio Player**:
The interactive audio playback control used in practice sessions and post-attempt reviews, providing interactive scrubbing, jump buttons (±5s), and unlimited replays.
_Avoid_: Practice audio, media widget

**Permission Namespace**:
One of the five composable capabilities that constitute staff authority: `pool.content.author`, `pool.content.review`, `assessment.blueprint.design`, `operations.support.triage`, `system.governance.admin`. A role holds any combination of them, and a staff route names the namespace it needs rather than a job title.
_Avoid_: Admin role, staff role, permission level, user type

**Role Permission**:
A grant of one permission namespace to one role, carried by `RolePermissions(RoleId, PermissionCode)`. Read live on each sign-in and re-validation, so a revoked grant takes effect on the next request.
_Avoid_: Role assignment, capability flag


**Certificate Wizard**:
The streamlined admin workflow for creating or editing a certificate from a 2-skill (`Listening & Reading`) or 4-skill (`Listening, Reading, Speaking, Writing`) template, requiring only certificate identity, scale range (`MinScaledScore`, `MaxScaledScore`, `ScoreStep`, `ScoreAggregationRule`), 3-field sections (`Code`, `Name`, `ExpectedQuestionCount` with automatic modality defaults), `ScoreBands`, and flat `GradingCriteria` (automatically generating linear `ScoreScales` and working immediately with the platform's single Universal Placement Exam).
_Avoid_: Rubric studio ladder lock, manual score conversion matrix editor, 15-field section JSON editor

**Exam Studio**:
The unified single-page admin workspace for authoring, editing, slicing audio, and auditing complete certification exams.
_Avoid_: Exam editor, question manager, test builder

**Authoring Wizard**:
The step-by-step guided workflow that adapts form structures and question grouping presets to the specific section and question type being authored.
_Avoid_: Exam wizard, question creator, test uploader

**Quick Ingest**:
The interactive text/JSON staging interface where exam content is parsed, validated, and reviewed prior to database persistence.
_Avoid_: Manual import, bulk paste, raw uploader

**Exam Status**:
The two-state lifecycle of an exam (`draft` and `published`). A `draft` exam is editable in Exam Studio; once it passes the Publish Readiness Audit, it becomes `published` and immediately available for learner sittings and practice drills.
_Avoid_: Validated item, six-state item lifecycle, ingestion gate, stratified audit

**Publish Readiness Audit**:
The pre-flight inspection checklist that validates question counts, answer keys, media assets, and rubric associations before an exam can transition from draft to published status.
_Avoid_: Pre-publish check, exam validation, publish verification

**Question Cluster**:
A pedagogical unit consisting of a shared asset (reading passage, image, or audio recording) paired with one or more dependent questions.
_Avoid_: Question group, item set, composite question

**Audio Slicing**:
The specification of millisecond offsets (`AudioStartMs`, `AudioEndMs`) against an exam section's master audio track to isolate playback for a specific question cluster.
_Avoid_: Audio clipping, audio trimming, time stamping

**Compensation**:
The decision, taken when a learner complaint is resolved, about what value the learner is owed in return. It is the umbrella term, and it has exactly three endings: no compensation, a make-good, or a refund. Choosing a refund is a decision to return value and *not yet* a refund — the refund desk records that separately, with its own permission and its own lifecycle, so a promise can never be mistaken for a payment.
_Avoid_: Remedy, restitution, goodwill gesture (as the name of the decision)

**Refund**:
A return of value to a learner, recorded as a decision with a requester, a decider and a reason. It is one of the arms a **Compensation** can take, and it is not the make-good grant on a complaint, which is a bounded apology rather than a return of what was taken.
_Avoid_: Compensation, cashback, reimbursement, payment reversal

**Make-good**:
A bounded credit grant attached to a resolved complaint: an apology for a failure, capped at what the learner actually spent on the subject, and therefore never more than the loss. It is the third arm of a **Compensation** beside "no compensation" and a **Refund**, and unlike a refund it moves value immediately and has no lifecycle of its own.
_Avoid_: Compensation, goodwill credits, gift, ex-gratia payment

**Refund method**:
Which form of value a refund returns: **credit**, posted to the credit ledger the moment it is approved, or **money**, paid by the accountant outside the product. One refund carries exactly one method.
_Avoid_: Refund type, refund kind

**Settlement**:
The act of the accountant recording that a money refund has actually been paid. It is the only moment money is treated as having left, and it is distinct from approval, which is only the decision to pay.
_Avoid_: Payout, disbursement, completion

**Net revenue**:
Money received from paid orders in a period, minus money refunds settled in that same period. It may be negative, and a negative figure is a fact about the period rather than an error.
_Avoid_: Income, earnings, turnover, gross revenue

**Reporting day**:
The day an accounting figure is attributed to, cut at UTC+7 regardless of the server's timezone. A payment timestamped 23:30 Vietnam time belongs to that reporting day.
_Avoid_: Business day, calendar day, local day

**Live metrics revision**:
A comparable value derived from counts and sums over dashboard-relevant rows. The dashboard refreshes when it changes, so this is its own concept and not a cache key.
_Avoid_: Watermark, version stamp, updated-at, ETag

**Mentor**:
An admin-managed human educator profile for a certificate (and optionally a specific skill) carrying a fixed meeting link, a session cost in credits, and a weekly availability schedule.
_Avoid_: AI mentor, autonomous coach, instructor user account, tutor

**Weekly Availability Window**:
A recurring time range on a specific day of the week (UTC+7) when a mentor is available to teach, from which the next upcoming session occurrence is derived.
_Avoid_: Manual one-off slot, ad-hoc calendar entry

**Mentor Payout**:
The compensation owed to a mentor for completed, non-cancelled sessions within a reporting period, denominated in the booking's credit price and converted to VND at the platform's fixed credit-to-VND rate.
_Avoid_: Separate cash slot price, ad-hoc mentor fee

**Goal Feasibility**:
The deterministic calculation comparing required study hours ($\Delta H = H_{\text{target}} - H_{\text{current}}$, minimum 30h) against available study hours ($H_{\text{avail}} = \text{Weeks} \times \text{WeeklyStudyHours}$) to classify a learner's goal as `Feasible`, `Stretch`, or `Unrealistic`.
_Avoid_: AI time estimate, arbitrary deadline check

**Learning Path**:
A deterministic 4-week study plan generated for 5 credits (`learning_path` ledger reason) from a learner's starting Certificate Band, Target Band, weekly study pace, and weakest skills/topics from placement, progressing from current-band foundation in weeks 1-2 to target-band mastery in weeks 3-4.
_Avoid_: Sliding window path, AI-generated syllabus

**Target Band Tag**:
The applicability label on a Question or Lesson indicating which band levels it serves: either one or more specific bands (e.g., `A2,B1,B2`) or `ALL` for general knowledge (`Kiến thức chung`) that every learner needs regardless of band.
_Avoid_: Single-cell CEFR lock

**Path Step**:
One sequentially unlocked learning activity within a week of a Learning Path: a theory/guide lesson (`ConceptLesson`), an interactive multi-format or AI-graded speaking/writing practice task (`FocusPractice` / `ReviewSpaced`), or a milestone mock exam (`MockSection`).
_Avoid_: Cell coordinate step, ungraded productive drill

**AI Grading**:
Rubric-based evaluation of a learner's free response against flat section `GradingCriteria` (`Name`, `Description`, `MaxScore`, `Weight`), the task prompt/guide, and the learner's Certificate `CurrentBand -> TargetBand` context (Claude LLM for Writing and linguistic criteria; SpeechSuper acoustic scores + Claude LLM transcript analysis for Speaking). Produces per-criterion scores and feedback, inline annotated `ErrorSpans` (`originalSnippet`, `correctedSnippet`, `explanationVi`, `category`), a `PriorityFix`, and a `ModelRewrite`.
_Avoid_: Seven-band descriptor ladder requirement, anchor essay requirement, automated MCQ scoring, blind item verification

**AI Insight**:
A personalized natural-language narrative, trend badge (`Improving`, `Steady`, `Declining`), and prioritized study recommendations (`High`, `Medium`, `Low`) generated by the LLM strictly from deterministic SQL accuracy, certificate band, and wrong-answer fingerprint facts without legacy CEFR cell-guard rejections.
_Avoid_: AI-computed score, hallucinated analytics, CEFR cell-coordinate narrative guard

**Defensive Layout Truncation**:
Applying bounded width (`max-w-[...]`), overflow clipping (`truncate` / `break-words`), and a native `title` attribute to dynamic user-provided strings in headers, cards, tables, and receipts so long or unspaced inputs cannot break layout geometry.
_Avoid_: Raw unclipped interpolation, JS-only string slicing

**Request Validation Filter**:
A Tier 2 Minimal API endpoint filter (`RequestValidationFilter<TRequest>`, registered via `.WithValidation<TRequest>()`) that validates incoming request DTOs against `DataAnnotations` and `IValidatableObject` rules before the handler runs, returning RFC 7807 `400 Bad Request` `ValidationProblem` with field-level camelCase errors.
_Avoid_: Per-handler boilerplate validation, silent model binding failure

**Normalized Name**:
A learner's `FullName` canonicalized by trimming leading/trailing whitespace and collapsing redundant internal ASCII spaces (`ProfileValidation.NormalizeFullName`), validated as 2–70 Unicode letter/mark characters with single apostrophe, hyphen, dot, or space separators.
_Avoid_: Unbounded free-text display name, raw whitespace-padded name

**Free-Response Bounds**:
Canonical server- and client-enforced limits for spoken and written learner responses (`FreeResponseBounds`: maximum 5,000 characters and 1,000 words for written responses; maximum 10 MB audio size and 180 seconds / 180,000 ms duration for speaking uploads).
_Avoid_: Unbounded essay/draft payloads, client-only audio duration limits

**Observation floor**:
The minimum number of graded observations (`ObservationFloor`, matching `MasteryEstimate.MinimumObservationsFor`) required before a `(topic, level)` weakness cell has enough statistical evidence to be ranked: 12 answered items for `listening` and `reading`, or 3 evaluated tasks for `speaking` and `writing`.
_Avoid_: Minimum sample size, arbitrary threshold, confidence cutoff

**Rankable vs. unranked weakness cell**:
A `(topic, level)` cell in Weakness Analysis is **rankable** once its `answered` count reaches its `observationFloor`, making it eligible to be sorted into the ranked tier and named `Weakest`; a cell below its `observationFloor` (or an unattempted section with 0 answers) is **unranked**, so it is still displayed with its counts and an evidence note stating how many answers are needed, and never carries a `Weakest` badge.
_Avoid_: Hidden sub-floor topic, unmeasured weakness, zero-answer weakest section





