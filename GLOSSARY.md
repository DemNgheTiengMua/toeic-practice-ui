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


