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

