# Extra Efficiency Companion

Self-directed certification prep platform providing diagnostic practice, simulated mock exams, AI-driven free-response grading, and mentor bookings.

## Language

**Credit**:
The internal currency unit consumed to access premium learning actions such as mock exams, AI grading, and mentor sessions.
_Avoid_: Token, point, coin, balance unit

**Credit Ledger**:
The append-only record of all credit adjustments, purchases, debits, and refunds for each learner.
_Avoid_: Credit database, transaction table, wallet balance table

**Append-Only Ledger**:
An immutable ledger where records are strictly inserted and never deleted or modified after posting.
_Avoid_: Transaction history, audit log

**Block Before Charge**:
The domain invariant requiring that a learner's available credit balance and all action preconditions are verified before debiting credits.
_Avoid_: Pre-authorization, credit check

**Transaction Receipt**:
The immutable confirmation generated upon posting a ledger transaction, reflecting the applied delta and resulting wallet balance.
_Avoid_: Transaction record, receipt entity

**Attempt Engine**:
The deep module that encapsulates the exam sitting lifecycle: countdown timers, expiration auto-submit, answer validation and persistence, question navigation, two-step score scaling, and CEFR band scale matching.
_Avoid_: Exam engine, test runner, attempt manager

**CEFR Level**:
A measured ability on the A1-C2 scale, carried by `BusinessObjects.CefrLevel`. Pre-A1 is a reportable floor (a result below A1), never a level content is authored at.
_Avoid_: Band, difficulty, level 1-5, grade

**Certificate Band**:
A scored range the certificate defines and names (TOEIC 785-940, VSTEP 6.0-6.5). It is a score range, not a CEFR level, and there is no conversion between the two.
_Avoid_: Level, CEFR band, grade

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


**Exam Studio**:
The unified single-page admin workspace for authoring, editing, slicing audio, and auditing complete certification exams.
_Avoid_: Exam editor, question manager, test builder

**Authoring Wizard**:
The step-by-step guided workflow that adapts form structures and question grouping presets to the specific section and question type being authored.
_Avoid_: Exam wizard, question creator, test uploader

**Quick Ingest**:
The interactive text/JSON staging interface where exam content is parsed, validated, and reviewed prior to database persistence.
_Avoid_: Manual import, bulk paste, raw uploader

**Publish Readiness Audit**:
The pre-flight inspection checklist that validates question counts, answer keys, media assets, and rubric associations before an exam can transition from draft to published status.
_Avoid_: Pre-publish check, exam validation, publish verification

**Question Cluster**:
A pedagogical unit consisting of a shared asset (reading passage, image, or audio recording) paired with one or more dependent questions.
_Avoid_: Question group, item set, composite question

**Audio Slicing**:
The specification of millisecond offsets (`AudioStartMs`, `AudioEndMs`) against an exam section's master audio track to isolate playback for a specific question cluster.
_Avoid_: Audio clipping, audio trimming, time stamping

